using System;
using System.Drawing;
using System.IO;
using System.Windows.Forms;

public class HexViewer : Control
{
    private Stream? _stream;

    private long _baseOffset;
    private long _firstLine;

    private readonly VScrollBar _scrollBar;

    private int _lineHeight;
    private bool _is64;

    private const string Header8 =
        "          +0 +1 +2 +3 +4 +5 +6 +7 +8 +9 +A +B +C +D +E +F  |  0123456789ABCDEF"; 
    private const string Header16 =
        "                  +0 +1 +2 +3 +4 +5 +6 +7 +8 +9 +A +B +C +D +E +F  |  0123456789ABCDEF";

    public HexViewer()
    {
        DoubleBuffered = true;

        SetStyle(ControlStyles.AllPaintingInWmPaint |
                 ControlStyles.UserPaint |
                 ControlStyles.OptimizedDoubleBuffer |
                 ControlStyles.ResizeRedraw, true);

        BackColor = Color.Black;
        ForeColor = Color.White;

        _lineHeight = TextRenderer.MeasureText("A", Font).Height;

        _scrollBar = new VScrollBar
        {
            Dock = DockStyle.Right,
            SmallChange = 1
        };

        _scrollBar.Scroll += (_, __) =>
        {
            _firstLine = _scrollBar.Value;
            Invalidate();
        };

        Controls.Add(_scrollBar);
    }

    // =========================
    // Bind
    // =========================
    public void Bind(Stream stream, long baseOffset, long length,long rawlenfor_is64)
    {
        _stream = stream;
        _baseOffset = baseOffset;
        _is64 = rawlenfor_is64 > uint.MaxValue;

        int lineCount = HexDumpCore.GetLineCount(length);

        _scrollBar.Minimum = 0;
        _scrollBar.Maximum = Math.Max(0, lineCount - 1);
        _scrollBar.Value = 0;

        _firstLine = 0;

        Invalidate();
    }

    // =========================
    // Mouse wheel
    // =========================
    protected override void OnMouseWheel(MouseEventArgs e)
    {
        if (_stream == null)
            return;

        int delta = e.Delta > 0 ? -3 : 3;

        int newVal = _scrollBar.Value + delta;

        newVal = Math.Max(_scrollBar.Minimum, newVal);
        newVal = Math.Min(_scrollBar.Maximum, newVal);

        _scrollBar.Value = newVal;
        _firstLine = newVal;

        Invalidate();
    }

    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        Invalidate();
    }

    // =========================
    // Paint
    // =========================
    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);

        if (_stream == null)
            return;

        int y = 0;

        // =========================
        // Header（固定顶端）
        // =========================
        TextRenderer.DrawText(
            e.Graphics,
            _is64 ? Header16 : Header8,
            Font,
            new Point(0, y),
            ForeColor,
            TextFormatFlags.NoPadding);

        y += _lineHeight;

        // =========================
        // visible lines
        // =========================
        int visibleLines = Height / _lineHeight;

        Span<char> buffer = stackalloc char[256];

        for (int i = 0; i < visibleLines; i++)
        {
            long lineIndex = _firstLine + i;

            HexDumpCore.RenderLine(
                _stream,
                _is64,
                _baseOffset,
                lineIndex,
                buffer,
                out int len);

            TextRenderer.DrawText(
                e.Graphics,
                new string(buffer[..len]),
                Font,
                new Point(0, y),
                ForeColor,
                TextFormatFlags.NoPadding);

            y += _lineHeight;
        }
    }
}