using System.Drawing.Drawing2D;
using System.Globalization;
using UniversalParser.Core.Formatting;
using UniversalParser.Core.Statistics;

namespace UniversalParser.App.Controls;

/// <summary>A two-dimensional pie and its numerical legend, with shared hover hit testing.</summary>
internal sealed class StatisticsPieChart : Control
{
    private static readonly Color[] Palette =
    [
        Color.FromArgb(59, 130, 246),
        Color.FromArgb(20, 184, 166),
        Color.FromArgb(245, 158, 11),
        Color.FromArgb(139, 92, 246),
        Color.FromArgb(236, 72, 153),
        Color.FromArgb(34, 197, 94),
        Color.FromArgb(6, 182, 212),
        Color.FromArgb(148, 163, 184),
        Color.FromArgb(239, 68, 68),
        Color.FromArgb(132, 204, 22),
        Color.FromArgb(249, 115, 22),
        Color.FromArgb(99, 102, 241),
        Color.FromArgb(14, 165, 233),
        Color.FromArgb(168, 85, 247),
        Color.FromArgb(234, 179, 8),
        Color.FromArgb(16, 185, 129),
        Color.FromArgb(100, 116, 139),
        Color.FromArgb(217, 70, 239),
        Color.FromArgb(8, 145, 178),
        Color.FromArgb(180, 83, 9),
    ];

    private const TextFormatFlags TextFlags = TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix
        | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis | TextFormatFlags.VerticalCenter;

    private readonly ToolTip _toolTip = new() { InitialDelay = 200, ReshowDelay = 100, AutoPopDelay = 10000 };
    private Font? _headingFont;
    private IReadOnlyList<StatisticsShare> _shares = Array.Empty<StatisticsShare>();
    private RectangleF _pieBounds;
    private Rectangle[] _legendRows = [];
    private decimal _total;
    private int _hotIndex = -1;
    private bool _ready;

    public StatisticsPieChart(string title, string nameHeading, string valueHeading, string unit)
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint
            | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        BackColor = Color.White;
        ForeColor = Color.FromArgb(31, 41, 55);
        Font = UiFonts.Small();
        Dock = DockStyle.Fill;
        Title = title;
        NameHeading = nameHeading;
        ValueHeading = valueHeading;
        Unit = unit;
        AccessibleName = title;
    }

    public string Title { get; }
    public string NameHeading { get; }
    public string ValueHeading { get; }
    public string Unit { get; }

    public void SetData(IReadOnlyList<StatisticsShare> shares)
    {
        _shares = shares.Where(static share => share.Value > 0).ToArray();
        _total = _shares.Sum(static share => (decimal)share.Value);
        _ready = true;
        _legendRows = new Rectangle[_shares.Count];
        SetHotIndex(-1);
        Invalidate();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _toolTip.Dispose();
            _headingFont?.Dispose();
        }

        base.Dispose(disposing);
    }

    protected override void OnFontChanged(EventArgs e)
    {
        base.OnFontChanged(e);
        _headingFont?.Dispose();
        _headingFont = null;
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        var graphics = e.Graphics;
        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        var margin = LogicalToDeviceUnits(18);
        var textHeight = TextRenderer.MeasureText(graphics, "Ag", Font).Height;
        var headingFont = _headingFont ??= new Font(Font, FontStyle.Bold);
        DrawText(graphics, Title, new Rectangle(margin, margin / 2, Width - margin * 2, textHeight + 6), headingFont, ForeColor);

        var contentTop = margin + textHeight + LogicalToDeviceUnits(6);
        if (_total == 0)
        {
            DrawText(graphics, _ready ? "No data to display." : "Computing...",
                new Rectangle(margin, contentTop, Width - margin * 2, Math.Max(0, Height - contentTop - margin)),
                Font, SystemColors.GrayText, TextFormatFlags.HorizontalCenter);
            _pieBounds = RectangleF.Empty;
            return;
        }

        var contentHeight = Height - contentTop - margin;
        var diameter = Math.Max(1, Math.Min(contentHeight - textHeight - margin, Math.Min(Width / 3, LogicalToDeviceUnits(228))));
        var pieAreaWidth = diameter + (margin * 2);
        _pieBounds = new RectangleF(
            margin,
            contentTop + Math.Max(0, (contentHeight - diameter - textHeight - margin / 2) / 2),
            diameter,
            diameter);

        var legendLeft = pieAreaWidth + margin;
        var legendWidth = Math.Max(1, Width - legendLeft - margin);
        var rowHeight = Math.Max(textHeight + LogicalToDeviceUnits(5),
            Math.Min(LogicalToDeviceUnits(27), contentHeight / (_shares.Count + 1)));
        var shareWidth = TextRenderer.MeasureText(graphics, WidestShare(), Font).Width + margin;
        var valueWidth = Math.Min(legendWidth / 2, Math.Max(LogicalToDeviceUnits(90),
            _shares.Max(share => TextRenderer.MeasureText(graphics, Number(share.Value), Font).Width) + margin));
        var nameWidth = Math.Max(1, legendWidth - shareWidth - valueWidth);

        DrawText(graphics, NameHeading, new Rectangle(legendLeft, contentTop, nameWidth, rowHeight), Font, SystemColors.GrayText);
        DrawText(graphics, ValueHeading, new Rectangle(legendLeft + nameWidth, contentTop, valueWidth, rowHeight),
            Font, SystemColors.GrayText, TextFormatFlags.Right);
        DrawText(graphics, "Share", new Rectangle(legendLeft + nameWidth + valueWidth, contentTop, shareWidth, rowHeight),
            Font, SystemColors.GrayText, TextFormatFlags.Right);

        var angle = -90F;
        using var border = new Pen(Color.White, LogicalToDeviceUnits(2));
        for (var index = 0; index < _shares.Count; index++)
        {
            var share = _shares[index];
            var sweep = index == _shares.Count - 1 ? 270F - angle : (float)(share.Value / _total * 360M);
            var colour = Palette[index % Palette.Length];
            var bounds = _pieBounds;
            if (index == _hotIndex)
            {
                var mid = (angle + sweep / 2) * Math.PI / 180;
                var distance = LogicalToDeviceUnits(4);
                bounds.Offset((float)Math.Cos(mid) * distance, (float)Math.Sin(mid) * distance);
                colour = ControlPaint.Light(colour, 0.2F);
            }

            if (sweep > 0)
            {
                using var brush = new SolidBrush(colour);
                graphics.FillPie(brush, bounds.X, bounds.Y, bounds.Width, bounds.Height, angle, sweep);
                graphics.DrawPie(border, bounds.X, bounds.Y, bounds.Width, bounds.Height, angle, sweep);
            }

            angle += sweep;
            var row = new Rectangle(legendLeft - 4, contentTop + ((index + 1) * rowHeight), legendWidth + 4, rowHeight);
            _legendRows[index] = row;
            if (index == _hotIndex)
            {
                using var highlight = new SolidBrush(Color.FromArgb(24, colour));
                graphics.FillRectangle(highlight, row);
            }

            var swatchSize = LogicalToDeviceUnits(10);
            using var swatch = new SolidBrush(colour);
            graphics.FillRectangle(swatch, legendLeft, row.Y + (rowHeight - swatchSize) / 2, swatchSize, swatchSize);
            var nameInset = swatchSize + LogicalToDeviceUnits(9);
            DrawText(graphics, share.Name, new Rectangle(legendLeft + nameInset, row.Y, Math.Max(1, nameWidth - nameInset), rowHeight), Font, ForeColor);
            DrawText(graphics, Number(share.Value), new Rectangle(legendLeft + nameWidth, row.Y, valueWidth, rowHeight),
                Font, ForeColor, TextFormatFlags.Right);
            DrawText(graphics, Percentage(share.Value),
                new Rectangle(legendLeft + nameWidth + valueWidth, row.Y, shareWidth, rowHeight),
                Font, ForeColor, TextFormatFlags.Right);
        }

        DrawText(graphics, $"{Number(_total)} {Unit}",
            new Rectangle(margin / 2, (int)_pieBounds.Bottom + margin / 2, pieAreaWidth - margin, textHeight + 4),
            Font, SystemColors.GrayText, TextFormatFlags.HorizontalCenter);
        using var line = new Pen(Color.FromArgb(229, 231, 235));
        graphics.DrawLine(line, margin, Height - 1, Width - margin, Height - 1);
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        SetHotIndex(HitTest(e.Location));
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        base.OnMouseLeave(e);
        SetHotIndex(-1);
    }

    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        SetHotIndex(-1);
    }

    private int HitTest(Point point)
    {
        for (var index = 0; index < _legendRows.Length; index++)
        {
            if (_legendRows[index].Contains(point))
            {
                return index;
            }
        }

        if (_pieBounds.IsEmpty || _total == 0)
        {
            return -1;
        }

        var dx = point.X - (_pieBounds.Left + _pieBounds.Width / 2);
        var dy = point.Y - (_pieBounds.Top + _pieBounds.Height / 2);
        var radius = _pieBounds.Width / 2;
        if (dx * dx + dy * dy > radius * radius)
        {
            return -1;
        }

        var angle = (Math.Atan2(dy, dx) * 180 / Math.PI + 450) % 360;
        var end = 0D;
        for (var index = 0; index < _shares.Count; index++)
        {
            end += (double)(_shares[index].Value / _total * 360M);
            if (angle < end || index == _shares.Count - 1)
            {
                return index;
            }
        }

        return -1;
    }

    private void SetHotIndex(int index)
    {
        if (_hotIndex == index)
        {
            return;
        }

        _hotIndex = index;
        _toolTip.SetToolTip(this, index >= 0
            ? $"{_shares[index].Name}\n{Number(_shares[index].Value)} {Unit} ({Percentage(_shares[index].Value)})"
            : null);
        Invalidate();
    }

    private string Percentage(long value) => ValueFormat.Percentage(value / _total);

    /// <summary>A "100%" sample measured with the configured decimals so the column never clips.</summary>
    private static string WidestShare() =>
        "100." + new string('0', ValueFormat.PercentageDecimals) + " %";

    private static string Number(decimal value) => value.ToString("N0", CultureInfo.InvariantCulture);

    private static void DrawText(Graphics graphics, string text, Rectangle bounds, Font font, Color colour,
        TextFormatFlags alignment = TextFormatFlags.Left) =>
        TextRenderer.DrawText(graphics, text, font, bounds, colour, TextFlags | alignment);
}
