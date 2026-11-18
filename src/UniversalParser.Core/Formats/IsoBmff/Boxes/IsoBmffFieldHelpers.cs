using System.Globalization;
using UniversalParser.Core.Model;
using UniversalParser.Core.Parsing;

namespace UniversalParser.Core.Formats.IsoBmff.Boxes;

/// <summary>
/// Shared loop guards and formatting for the ISOBMFF table boxes. Every table loop is bounded by
/// <see cref="ParseLimits.MaxDataLines"/> so a corrupt 32-bit entry count cannot stall the parser.
/// </summary>
internal static class IsoBmffFieldHelpers
{
    /// <summary>
    /// Clamps a declared record count to the configured row budget. Returns the number of rows the
    /// caller may emit; a LIMIT_RECORDS diagnostic is raised when the count is reduced.
    /// </summary>
    public static int RowLimit(ParseContext context, ulong declared, string boxType, FieldList fields)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(fields);

        var budget = context.Limits.MaxDataLines;
        if (declared <= (ulong)budget)
        {
            return (int)declared;
        }

        fields.Warning(
            DiagnosticCodes.LimitRecords,
            string.Create(
                CultureInfo.InvariantCulture,
                $"Box '{boxType}' declares {declared} record(s); only the first {budget} are read."));
        return budget;
    }

    public static string Join(params object?[] values)
    {
        var builder = new System.Text.StringBuilder();
        for (var index = 0; index < values.Length; index++)
        {
            if (index > 0)
            {
                builder.Append(',');
            }

            builder.Append(Convert.ToString(values[index], CultureInfo.InvariantCulture));
        }

        return builder.ToString();
    }

    public static string Fixed1616(uint raw) =>
        IsoBmffFormatting.FormatFixed1616(raw);

    public static string Fixed88(ushort raw) =>
        IsoBmffFormatting.FormatFixed88(raw);

    public static string Scaled(uint raw, double scale, string format) =>
        (unchecked((int)raw) / scale).ToString(format, CultureInfo.InvariantCulture);

    public static string Scaled(ushort raw, double scale, string format) =>
        (unchecked((short)raw) / scale).ToString(format, CultureInfo.InvariantCulture);

    public static string Language(ushort packed)
    {
        var text = IsoBmffFormatting.FormatLanguage(packed);
        return string.Create(CultureInfo.InvariantCulture, $"{text} (0x{packed:X4})");
    }

    /// <summary>Reads a NUL-terminated UTF-8 string bounded by the box payload and a byte budget.</summary>
    public static string ReadCString(IsoBmffBoxReader data, int maxBytes)
    {
        ArgumentNullException.ThrowIfNull(data);
        var buffer = new byte[Math.Min(maxBytes, 256)];
        var length = 0;
        var terminated = false;
        while (length < maxBytes)
        {
            if (!data.TryReadUInt8Raw(out var value))
            {
                break;
            }

            if (value == 0)
            {
                terminated = true;
                break;
            }

            if (length == buffer.Length)
            {
                var next = buffer.Length >= maxBytes / 2 ? maxBytes : buffer.Length * 2;
                if (next <= buffer.Length)
                {
                    break;
                }

                Array.Resize(ref buffer, next);
            }

            buffer[length++] = value;
        }

        var text = IsoBmffFormatting.DecodeText(buffer.AsSpan(0, length));
        return terminated ? text + "\\0" : text;
    }
}
