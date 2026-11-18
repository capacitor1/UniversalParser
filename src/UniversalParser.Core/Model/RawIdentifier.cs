using System.Buffers;
using System.Text;
using UniversalParser.Core.Binary;

namespace UniversalParser.Core.Model;

/// <summary>
/// Formats a raw identifier exactly as it appears in the file for use inside a title:
/// printable single-byte characters stay as text, everything else becomes \xNN. GUIDs use the
/// canonical curly-braced form required for ASF.
/// </summary>
public static class RawIdentifier
{
    public static string FormatBytes(ReadOnlySpan<byte> bytes)
    {
        if (bytes.IsEmpty)
        {
            return string.Empty;
        }

        long length = bytes.Length;
        foreach (var value in bytes)
        {
            if (value is < 0x20 or > 0x7E)
            {
                length += 3;
            }
        }

        if (length > Array.MaxLength)
        {
            throw new ArgumentException("The formatted string would exceed the maximum length.", nameof(bytes));
        }

        return string.Create((int)length, bytes, static (destination, source) =>
        {
            _ = WriteBytes(source, destination);
        });
    }

    public static string FormatText(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        var byteCount = Encoding.Latin1.GetByteCount(value);
        byte[]? rented = null;
        Span<byte> bytes = byteCount <= 256
            ? stackalloc byte[byteCount]
            : (rented = ArrayPool<byte>.Shared.Rent(byteCount)).AsSpan(0, byteCount);
        try
        {
            var written = Encoding.Latin1.GetBytes(value.AsSpan(), bytes);
            return FormatBytes(bytes[..written]);
        }
        finally
        {
            if (rented is not null)
            {
                ArrayPool<byte>.Shared.Return(rented);
            }
        }
    }

    public static string FormatGuid(Guid value) =>
        string.Create(38, value, static (destination, guid) =>
        {
            _ = guid.TryFormat(destination, out _, "B");
            for (var index = 0; index < destination.Length; index++)
            {
                destination[index] = char.ToUpperInvariant(destination[index]);
            }
        });

    internal static int WriteBytes(ReadOnlySpan<byte> bytes, Span<char> destination)
    {
        var offset = 0;
        foreach (var value in bytes)
        {
            if (value is >= 0x20 and <= 0x7E)
            {
                destination[offset++] = (char)value;
            }
            else
            {
                HexFormatting.WriteEscapedByte(value, destination[offset..]);
                offset += 4;
            }
        }

        return offset;
    }
}
