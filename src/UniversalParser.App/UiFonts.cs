using System.Drawing.Text;

namespace UniversalParser.App;

/// <summary>
/// Central font factory. The legacy UI used Fira Code; when it is not installed we fall back to
/// Consolas so the monospace layout stays identical instead of silently becoming proportional.
/// </summary>
internal static class UiFonts
{
    private static readonly Lazy<string> MonoFamily = new(ResolveMonoFamily);

    public static Font Mono(float size, FontStyle style = FontStyle.Regular) =>
        new(MonoFamily.Value, size, style, GraphicsUnit.Point);

    public static Font Title() => Mono(14.25F);

    public static Font Heading() => Mono(26.25F);

    public static Font Body() => Mono(10.5F);

    public static Font Small() => Mono(9F);

    private static string ResolveMonoFamily()
    {
        try
        {
            using var installed = new InstalledFontCollection();
            var names = installed.Families.Select(static family => family.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
            if (names.Contains("Fira Code"))
            {
                return "Fira Code";
            }

            if (names.Contains("Cascadia Mono"))
            {
                return "Cascadia Mono";
            }

            if (names.Contains("Consolas"))
            {
                return "Consolas";
            }
        }
        catch
        {
            // Font enumeration is best effort; GDI+ falls back to a default family.
        }

        return FontFamily.GenericMonospace.Name;
    }
}
