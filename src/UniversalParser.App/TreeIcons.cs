using System.Drawing.Drawing2D;

namespace UniversalParser.App;

/// <summary>
/// Builds the tree icon list from the 48 px PNG artwork in <c>Assets</c>, downscaled once to the
/// 16 px size the legacy tree used. Keeping the sources as embedded resources avoids design-time
/// ImageList data and keeps the repository text-only apart from the PNGs themselves.
/// </summary>
internal static class TreeIcons
{
    public const int Root = 0;
    public const int FolderClosed = 1;
    public const int FolderOpen = 2;
    public const int File = 3;
    public const int Checked = 4;

    private const int IconSize = 16;

    public static ImageList Create()
    {
        var images = new ImageList
        {
            ColorDepth = ColorDepth.Depth32Bit,
            ImageSize = new Size(IconSize, IconSize),
            TransparentColor = Color.Transparent,
        };

        images.Images.Add(Load("tree.root.png"));
        images.Images.Add(Load("tree.folder.png"));
        images.Images.Add(Load("tree.folder-open.png"));
        images.Images.Add(Load("tree.file.png"));
        images.Images.Add(Load("tree.checked.png"));
        return images;
    }

    private static Bitmap Load(string resourceName)
    {
        using var stream = typeof(TreeIcons).Assembly.GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException($"Missing tree icon resource '{resourceName}'.");
        using var source = new Bitmap(stream);
        var scaled = new Bitmap(IconSize, IconSize);
        using var graphics = Graphics.FromImage(scaled);
        graphics.Clear(Color.Transparent);
        graphics.CompositingQuality = CompositingQuality.HighQuality;
        graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
        graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
        graphics.SmoothingMode = SmoothingMode.HighQuality;
        graphics.DrawImage(source, new Rectangle(0, 0, IconSize, IconSize));
        return scaled;
    }
}
