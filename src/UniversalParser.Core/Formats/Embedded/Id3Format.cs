namespace UniversalParser.Core.Formats.Embedded;

/// <summary>Display helpers shared by the ID3 frame decoders.</summary>
internal static class Id3Format
{
    public static string Hex(int value, int digits = 2) => "0x" + value.ToString("X" + digits);

    public static string Number(int value) => value.ToString();

    public static string PayloadTypeFromMime(string mime)
    {
        if (mime.Equals("image/jpeg", StringComparison.OrdinalIgnoreCase)
            || mime.Equals("image/jpg", StringComparison.OrdinalIgnoreCase))
        {
            return "jpeg";
        }

        if (mime.Equals("image/png", StringComparison.OrdinalIgnoreCase))
        {
            return "png";
        }

        if (mime.Equals("image/gif", StringComparison.OrdinalIgnoreCase))
        {
            return "gif";
        }

        return "payload";
    }

    public static string Payload(string type, int length) => "<" + type + ":" + length + ">";
}
