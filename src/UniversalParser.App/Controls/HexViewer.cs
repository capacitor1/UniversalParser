using System.Buffers;
using System.Text;
using UniversalParser.Core.Binary;
using UniversalParser.Core.Export;

namespace UniversalParser.App.Controls;

/// <summary>
/// Lazy hex viewer. It never owns a stream position and never preloads the node: only the visible
/// lines are read through the random-access source. Line numbers stay 64-bit; the scroll bar is
/// mapped onto at most <see cref="ScrollSteps"/> positions so files beyond 32 GB still scroll.
/// Selection is byte based so a copied range always matches the bytes on screen.
/// </summary>
internal sealed class HexViewer : Control
{
    private const int ScrollSteps = 1_000_000;
    private const int WheelLines = 3;

    /// <summary>Clipboard guard: a single copy never materialises more than this many bytes.</summary>
    private const long MaxCopyBytes = 1L << 20;

    private const string HexDigits = "0123456789ABCDEF";

    private const TextFormatFlags TextFlags = TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix
        | TextFormatFlags.SingleLine | TextFormatFlags.PreserveGraphicsClipping
        | TextFormatFlags.PreserveGraphicsTranslateTransform;

    private static readonly string Header8 = HexDumpFormatter.FormatHeader(false);
    private static readonly string Header16 = HexDumpFormatter.FormatHeader(true);

    private readonly VScrollBar _scrollBar;
    private IRandomAccessSource? _source;
    private long _baseOffset;
    private long _length;
    private long _firstLine;
    private long _lineCount;
    private bool _wideOffset;
    private bool _updatingScroll;
    private int _lineHeight;
    private readonly int[] _columnPositions = new int[HexDumpFormatter.MaxLineLength + 1];
    private bool _metricsValid;
    private float _metricsDpiX;
    private float _metricsDpiY;

    private long _activeLine;
    private long _selectionAnchor = -1;
    private long _selectionCaret = -1;
    private bool _hasSelection;

    public HexViewer()
    {
        SetStyle(
            ControlStyles.AllPaintingInWmPaint
            | ControlStyles.UserPaint
            | ControlStyles.OptimizedDoubleBuffer
            | ControlStyles.ResizeRedraw,
            true);

        BackColor = SystemColors.Control;
        ForeColor = SystemColors.ControlText;
        _lineHeight = Math.Max(1, Font.Height);

        _scrollBar = new VScrollBar
        {
            Dock = DockStyle.Right,
            SmallChange = 1,
            Visible = false,
        };
        _scrollBar.Scroll += OnScrollBarScroll;
        Controls.Add(_scrollBar);

        ContextMenuStrip = BuildContextMenu();
    }

    public long FirstLine => _firstLine;

    public long LineCount => _lineCount;

    public void Bind(IRandomAccessSource? source, long baseOffset, long length)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(baseOffset);
        ArgumentOutOfRangeException.ThrowIfNegative(length);

        _source = source;
        _baseOffset = baseOffset;
        _length = length;
        _wideOffset = source is not null && source.Length > uint.MaxValue;
        _lineCount = HexDumpFormatter.GetLineCount(length);
        _activeLine = 0;
        _selectionAnchor = -1;
        _selectionCaret = -1;
        _hasSelection = false;
        SetFirstLine(0);
        UpdateScrollBar();
        Invalidate();
    }

    public void Unbind()
    {
        _source = null;
        _length = 0;
        _lineCount = 0;
        _firstLine = 0;
        _activeLine = 0;
        _selectionAnchor = -1;
        _selectionCaret = -1;
        _hasSelection = false;
        _scrollBar.Visible = false;
        Invalidate();
    }

    protected override void OnFontChanged(EventArgs e)
    {
        base.OnFontChanged(e);
        _metricsValid = false;
        Invalidate();
    }

    protected override void OnDpiChangedAfterParent(EventArgs e)
    {
        base.OnDpiChangedAfterParent(e);
        _metricsValid = false;
        Invalidate();
    }

    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        SetFirstLine(_firstLine);
        UpdateScrollBar();
        Invalidate();
    }

    protected override void OnMouseWheel(MouseEventArgs e)
    {
        if (_source is null || _lineCount == 0)
        {
            return;
        }

        var delta = e.Delta > 0 ? -WheelLines : WheelLines;
        SetFirstLine(_firstLine + delta);
        UpdateScrollBar();
        Invalidate();
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        Focus();

        if (e.Button != MouseButtons.Left)
        {
            if (e.Button == MouseButtons.Right && !_hasSelection
                && TryHitTest(e.Location, out var currentLine, out _))
            {
                _activeLine = currentLine;
            }

            return;
        }

        if (!TryHitTest(e.Location, out var line, out var byteIndex))
        {
            return;
        }

        _activeLine = line;
        _selectionAnchor = (line * HexDumpFormatter.BytesPerLine) + byteIndex;
        _selectionCaret = _selectionAnchor;
        _hasSelection = true;
        Capture = true;
        Invalidate();
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (!_hasSelection || (e.Button & MouseButtons.Left) == 0)
        {
            return;
        }

        if (!TryHitTest(e.Location, out var line, out var byteIndex))
        {
            return;
        }

        _selectionCaret = (line * HexDumpFormatter.BytesPerLine) + byteIndex;
        Invalidate();
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        Capture = false;
    }

    protected override bool IsInputKey(Keys keyData) => keyData switch
    {
        Keys.PageUp or Keys.PageDown or Keys.Home or Keys.End => true,
        _ => base.IsInputKey(keyData),
    };

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.Control && e.KeyCode == Keys.C)
        {
            CopySelection();
            e.Handled = true;
            return;
        }

        if (_source is null || _lineCount == 0)
        {
            base.OnKeyDown(e);
            return;
        }

        var page = Math.Max(1, VisibleLineCount - 1);
        switch (e.KeyCode)
        {
            case Keys.PageUp:
                SetFirstLine(_firstLine - page);
                break;
            case Keys.PageDown:
                SetFirstLine(_firstLine + page);
                break;
            case Keys.Home:
                SetFirstLine(0);
                break;
            case Keys.End:
                SetFirstLine(long.MaxValue);
                break;
            default:
                base.OnKeyDown(e);
                return;
        }

        UpdateScrollBar();
        Invalidate();
        e.Handled = true;
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        EnsureTextMetrics(e.Graphics);
        if (_source is null || _length == 0)
        {
            return;
        }

        var y = 0;
        TextRenderer.DrawText(
            e.Graphics,
            _wideOffset ? Header16 : Header8,
            Font,
            new Point(0, y),
            ForeColor,
            TextFlags);
        y += _lineHeight;

        // Paint beneath the glyphs so the highlight does not tint or blur the text itself.
        PaintSelection(e.Graphics);

        var visible = VisibleLineCount;
        var lineCount = (int)Math.Min(visible, _lineCount - _firstLine);
        if (lineCount <= 0)
        {
            return;
        }

        var relative = _firstLine * (long)HexDumpFormatter.BytesPerLine;
        if (_baseOffset > long.MaxValue - relative)
        {
            return;
        }

        var startOffset = _baseOffset + relative;
        var available = Math.Max(0, Math.Min(_length - relative, _source.Length - startOffset));
        var wanted = (int)Math.Min((long)lineCount * HexDumpFormatter.BytesPerLine, available);
        if (wanted <= 0)
        {
            return;
        }

        var bytes = ArrayPool<byte>.Shared.Rent(wanted);
        try
        {
            var read = SourceRead.ReadAtMost(_source, startOffset, bytes.AsSpan(0, wanted));
            var drawn = Math.Min(lineCount, (read + HexDumpFormatter.BytesPerLine - 1) / HexDumpFormatter.BytesPerLine);
            Span<char> buffer = stackalloc char[HexDumpFormatter.MaxLineLength];
            for (var index = 0; index < drawn; index++)
            {
                var count = Math.Min(HexDumpFormatter.BytesPerLine, read - (index * HexDumpFormatter.BytesPerLine));
                if (count <= 0)
                {
                    break;
                }

                var length = HexDumpFormatter.RenderLine(
                    _wideOffset,
                    startOffset + (index * HexDumpFormatter.BytesPerLine),
                    bytes.AsSpan(index * HexDumpFormatter.BytesPerLine, count),
                    buffer);
                TextRenderer.DrawText(
                    e.Graphics,
                    buffer[..length],
                    Font,
                    new Point(0, y),
                    ForeColor,
                    TextFlags);
                y += _lineHeight;
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(bytes);
        }
    }

    private int VisibleLineCount => Math.Max(1, (Height - _lineHeight) / Math.Max(1, _lineHeight));

    private int HexStart => HexDumpFormatter.GetHexColumn(_wideOffset);

    private int AsciiStart => HexDumpFormatter.GetAsciiColumn(_wideOffset);

    private ContextMenuStrip BuildContextMenu()
    {
        var menu = new ContextMenuStrip();
        menu.Items.Add("Copy Selection", null, (_, _) => CopySelection());
        menu.Items.Add("Copy as Hex", null, (_, _) => CopyAsHex());
        menu.Items.Add("Copy as ASCII", null, (_, _) => CopyAsAscii());
        return menu;
    }

    private void EnsureTextMetrics(Graphics graphics)
    {
        if (_metricsValid && _metricsDpiX == graphics.DpiX && _metricsDpiY == graphics.DpiY)
        {
            return;
        }

        _lineHeight = Math.Max(1, TextRenderer.MeasureText(graphics, "0", Font, Size.Empty, TextFlags).Height);
        // Use the same GDI context and flags as DrawText. Measuring each prefix avoids cumulative
        // rounding from an integer average character width at fractional font sizes / DPI scales.
        for (var column = 1; column < _columnPositions.Length; column++)
        {
            _columnPositions[column] = TextRenderer.MeasureText(
                graphics, new string('0', column), Font, Size.Empty, TextFlags).Width;
        }

        _metricsDpiX = graphics.DpiX;
        _metricsDpiY = graphics.DpiY;
        _metricsValid = true;
        SetFirstLine(_firstLine);
        UpdateScrollBar();
    }

    private void PaintSelection(Graphics graphics)
    {
        if (!_hasSelection || _source is null)
        {
            return;
        }

        if (!GetSelection(out var start, out var end))
        {
            return;
        }

        using var brush = new SolidBrush(Color.FromArgb(96, SystemColors.Highlight));
        var row = 0;
        for (var line = _firstLine; line < _lineCount && row < VisibleLineCount; line++, row++)
        {
            var lineStart = line * HexDumpFormatter.BytesPerLine;
            var lineEnd = lineStart + Math.Min(HexDumpFormatter.BytesPerLine, _length - lineStart);
            var from = Math.Max(start, lineStart);
            var to = Math.Min(end, lineEnd);
            if (to <= from)
            {
                continue;
            }

            var firstColumn = (int)(from - lineStart);
            var lastColumn = (int)(to - lineStart);
            var span = HexDumpFormatter.GetColumnSpan(_wideOffset, firstColumn, lastColumn - firstColumn);
            var y = _lineHeight + (row * _lineHeight);

            var hexX = _columnPositions[span.HexStart];
            var hexWidth = _columnPositions[span.HexEnd] - hexX;
            graphics.FillRectangle(brush, hexX, y, hexWidth, _lineHeight);

            var asciiX = _columnPositions[span.AsciiStart];
            var asciiWidth = _columnPositions[span.AsciiEnd] - asciiX;
            graphics.FillRectangle(brush, asciiX, y, asciiWidth, _lineHeight);
        }
    }

    private bool TryHitTest(Point point, out long line, out int byteIndex)
    {
        line = 0;
        byteIndex = 0;
        if (!_metricsValid)
        {
            using var graphics = CreateGraphics();
            EnsureTextMetrics(graphics);
        }
        if (_source is null || _lineCount == 0 || point.Y < _lineHeight)
        {
            return false;
        }

        var row = (point.Y - _lineHeight) / Math.Max(1, _lineHeight);
        var candidate = _firstLine + row;
        if (candidate < 0 || candidate >= _lineCount || row >= VisibleLineCount
            || point.X < 0 || point.X >= ClientSize.Width - (_scrollBar.Visible ? _scrollBar.Width : 0))
        {
            return false;
        }

        var bytesOnLine = (int)Math.Min(HexDumpFormatter.BytesPerLine, _length - (candidate * HexDumpFormatter.BytesPerLine));
        if (bytesOnLine <= 0)
        {
            return false;
        }

        var column = Array.BinarySearch(_columnPositions, point.X);
        if (column < 0)
        {
            column = ~column - 1;
        }

        if (column >= AsciiStart && column < AsciiStart + bytesOnLine)
        {
            byteIndex = column - AsciiStart;
        }
        else if (column >= HexStart && column < HexStart + (bytesOnLine * 3) - 1)
        {
            byteIndex = (column - HexStart) / 3;
        }
        else
        {
            return false;
        }

        line = candidate;
        return true;
    }

    private bool GetSelection(out long start, out long end)
    {
        if (_hasSelection && _selectionAnchor >= 0 && _selectionCaret >= 0)
        {
            start = Math.Min(_selectionAnchor, _selectionCaret);
            end = Math.Max(_selectionAnchor, _selectionCaret) + 1;
        }
        else
        {
            start = _activeLine * HexDumpFormatter.BytesPerLine;
            end = start + Math.Min(HexDumpFormatter.BytesPerLine, _length - start);
        }

        start = Math.Clamp(start, 0, _length);
        end = Math.Clamp(end, 0, _length);
        return end > start;
    }

    private void CopySelection()
    {
        if (_source is null || !GetSelection(out var start, out var end))
        {
            return;
        }

        SetClipboard(BuildSelectionDump(start, end));
    }

    private void CopyAsHex()
    {
        if (_source is null || !GetSelection(out var start, out var end))
        {
            return;
        }

        SetClipboard(BuildByteText(start, end, hex: true));
    }

    private void CopyAsAscii()
    {
        if (_source is null || !GetSelection(out var start, out var end))
        {
            return;
        }

        SetClipboard(BuildByteText(start, end, hex: false));
    }

    private static void SetClipboard(string text)
    {
        if (text.Length == 0)
        {
            return;
        }

        try
        {
            Clipboard.SetText(text);
        }
        catch (System.Runtime.InteropServices.ExternalException)
        {
            // Another process can hold the clipboard; copying is best effort.
        }
    }

    private string BuildSelectionDump(long start, long end)
    {
        var builder = new StringBuilder();
        var truncated = end - start > MaxCopyBytes;
        if (truncated)
        {
            end = start + MaxCopyBytes;
        }

        Span<byte> buffer = stackalloc byte[HexDumpFormatter.BytesPerLine];
        var position = start;
        while (position < end)
        {
            var column = (int)(position % HexDumpFormatter.BytesPerLine);
            var count = (int)Math.Min(HexDumpFormatter.BytesPerLine - column, end - position);
            var read = SourceRead.ReadAtMost(_source!, _baseOffset + position, buffer[..count]);
            if (read <= 0)
            {
                break;
            }

            AppendDumpLine(builder, _baseOffset + position, column, buffer[..read]);
            position += read;
        }

        if (truncated)
        {
            builder.AppendLine("... (selection truncated)");
        }

        return builder.ToString();
    }

    private void AppendDumpLine(StringBuilder builder, long absoluteOffset, int column, ReadOnlySpan<byte> bytes)
    {
        builder.Append(HexDumpFormatter.FormatOffset(absoluteOffset, _wideOffset));
        builder.Append("  ");

        for (var index = 0; index < column; index++)
        {
            builder.Append("   ");
        }

        foreach (var value in bytes)
        {
            builder.Append(HexDigits[value >> 4]);
            builder.Append(HexDigits[value & 0xF]);
            builder.Append(' ');
        }

        for (var index = column + bytes.Length; index < HexDumpFormatter.BytesPerLine; index++)
        {
            builder.Append("   ");
        }

        builder.Append(" | ");
        foreach (var value in bytes)
        {
            builder.Append(value is >= 32 and <= 126 ? (char)value : '.');
        }

        builder.AppendLine();
    }

    private string BuildByteText(long start, long end, bool hex)
    {
        var builder = new StringBuilder();
        var truncated = end - start > MaxCopyBytes;
        if (truncated)
        {
            end = start + MaxCopyBytes;
        }

        Span<byte> buffer = stackalloc byte[4096];
        var position = start;
        while (position < end)
        {
            var count = (int)Math.Min(buffer.Length, end - position);
            var read = SourceRead.ReadAtMost(_source!, _baseOffset + position, buffer[..count]);
            if (read <= 0)
            {
                break;
            }

            for (var index = 0; index < read; index++)
            {
                var value = buffer[index];
                if (hex)
                {
                    builder.Append(HexDigits[value >> 4]);
                    builder.Append(HexDigits[value & 0xF]);
                    builder.Append(' ');
                }
                else
                {
                    builder.Append(value is >= 32 and <= 126 ? (char)value : '.');
                }
            }

            position += read;
        }

        if (truncated)
        {
            builder.AppendLine();
            builder.Append("... (selection truncated)");
        }

        return builder.ToString();
    }

    private void OnScrollBarScroll(object? sender, ScrollEventArgs e)
    {
        if (_updatingScroll)
        {
            return;
        }

        SetFirstLine(ValueToLine(e.NewValue));
        Invalidate();
    }

    private long MaxFirstLine
    {
        get
        {
            var visible = VisibleLineCount;
            return Math.Max(0, _lineCount - visible);
        }
    }

    private void SetFirstLine(long line)
    {
        _firstLine = Math.Clamp(line, 0, MaxFirstLine);
        if (!_hasSelection && _lineCount > 0)
        {
            var lastVisible = Math.Min(_lineCount - 1, _firstLine + VisibleLineCount - 1);
            _activeLine = Math.Clamp(_activeLine, _firstLine, lastVisible);
        }
    }

    private void UpdateScrollBar()
    {
        if (_source is null || _lineCount <= 0)
        {
            _scrollBar.Visible = false;
            return;
        }

        var visible = VisibleLineCount;
        var maxTop = MaxFirstLine;
        if (maxTop == 0)
        {
            _scrollBar.Visible = false;
            return;
        }

        _scrollBar.Visible = true;
        _updatingScroll = true;
        try
        {
            _scrollBar.Minimum = 0;
            if (_lineCount <= ScrollSteps)
            {
                // WinForms caps the reachable value at Maximum - LargeChange + 1, so Maximum must
                // include the page size or the last page can never be reached.
                var large = Math.Max(1, visible);
                _scrollBar.LargeChange = large;
                _scrollBar.Maximum = (int)Math.Min(int.MaxValue - large, maxTop + large - 1);
                _scrollBar.Value = (int)Math.Clamp(_firstLine, 0, maxTop);
            }
            else
            {
                var page = (Int128)ScrollSteps * visible / _lineCount;
                var large = page < 1 ? 1 : (int)page;
                _scrollBar.LargeChange = large;
                _scrollBar.Maximum = ScrollSteps + large - 1;
                _scrollBar.Value = (int)Math.Clamp(LineToValue(_firstLine), 0, ScrollSteps);
            }
        }
        finally
        {
            _updatingScroll = false;
        }
    }

    private int LineToValue(long line)
    {
        if (_lineCount <= ScrollSteps)
        {
            return (int)line;
        }

        var maxTop = Math.Max(1, MaxFirstLine);
        var scaled = (Int128)line * ScrollSteps / maxTop;
        return (int)Math.Clamp((long)scaled, 0, ScrollSteps);
    }

    private long ValueToLine(int value)
    {
        if (_lineCount <= ScrollSteps)
        {
            return value;
        }

        var maxTop = MaxFirstLine;
        if (maxTop == 0)
        {
            return 0;
        }

        var scaled = (Int128)value * maxTop / ScrollSteps;
        return Math.Clamp((long)scaled, 0, maxTop);
    }
}
