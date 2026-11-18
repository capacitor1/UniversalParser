using System.Collections.Immutable;
using UniversalParser.Core.Binary;
using UniversalParser.Core.Formats.Embedded;

namespace UniversalParser.Core.Formats.Blend;

internal readonly record struct BlendField(ushort TypeIndex, ushort NameIndex);

internal readonly record struct BlendStruct(ushort TypeIndex, ImmutableArray<BlendField> Fields);

internal readonly record struct BlendFieldLayout(
    string Name,
    string TypeName,
    int Offset,
    int Size,
    int PointerDepth,
    ImmutableArray<int> Dimensions,
    bool IsStruct,
    bool IsChar);

/// <summary>
/// Blender SDNA table from the DNA1 block. The table names the C structures and fields used by
/// each block; it is authoritative for block structure names, not for executable semantics.
/// Layout: "SDNA" "NAME" names "TYPE" types "TLEN" sizes "STRC" structures (BLO_core_bhead.hh).
/// </summary>
internal sealed class BlendDna
{
    private const int MaxNameBytes = 4096;

    private readonly Dictionary<ushort, int> _structIndexByType = [];

    private BlendDna(
        ImmutableArray<string> names,
        ImmutableArray<string> types,
        ImmutableArray<ushort> typeLengths,
        ImmutableArray<BlendStruct> structs)
    {
        Names = names;
        Types = types;
        TypeLengths = typeLengths;
        Structs = structs;
        for (var index = 0; index < structs.Length; index++)
        {
            _structIndexByType.TryAdd(structs[index].TypeIndex, index);
        }
    }

    public ImmutableArray<string> Names { get; }
    public ImmutableArray<string> Types { get; }
    public ImmutableArray<ushort> TypeLengths { get; }
    public ImmutableArray<BlendStruct> Structs { get; }

    public static BlendDna? TryParse(
        IRandomAccessSource source,
        long offset,
        long length,
        BlendEndian endian,
        CancellationToken cancellationToken,
        out string? error)
    {
        ArgumentNullException.ThrowIfNull(source);
        error = null;
        if (offset < 0 || length < 0 || offset > source.Length || length > source.Length - offset)
        {
            error = "The DNA1 block range is outside the file.";
            return null;
        }

        var reader = new BlockReader(source, offset, length);
        var little = endian == BlendEndian.Little;

        if (!TryMarker(reader, "SDNA"u8))
        {
            error = "The DNA1 block does not start with 'SDNA'.";
            return null;
        }

        if (!TryMarker(reader, "NAME"u8) || !TryUInt32(reader, little, out var nameCount))
        {
            error = "The DNA NAME section is truncated.";
            return null;
        }

        if (!TryReadNames(reader, nameCount, cancellationToken, out var names, out error))
        {
            return null;
        }

        reader.Skip(AlignmentPadding(reader.Position - offset, 4));

        if (!TryMarker(reader, "TYPE"u8) || !TryUInt32(reader, little, out var typeCount))
        {
            error = "The DNA TYPE section is truncated.";
            return null;
        }

        if (!TryReadNames(reader, typeCount, cancellationToken, out var types, out error))
        {
            return null;
        }

        reader.Skip(AlignmentPadding(reader.Position - offset, 4));

        if (!TryMarker(reader, "TLEN"u8))
        {
            error = "The DNA TLEN section is truncated.";
            return null;
        }

        if (typeCount > int.MaxValue)
        {
            error = "The DNA type count is too large.";
            return null;
        }

        var typeLengths = ImmutableArray.CreateBuilder<ushort>((int)typeCount);
        for (var index = 0; index < typeCount; index++)
        {
            if ((index & 0xFF) == 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
            }

            if (!TryUInt16(reader, little, out var typeLength))
            {
                error = "The DNA TLEN section is truncated.";
                return null;
            }

            typeLengths.Add(typeLength);
        }

        reader.Skip(AlignmentPadding(reader.Position - offset, 4));

        if (!TryMarker(reader, "STRC"u8) || !TryUInt32(reader, little, out var structCount))
        {
            error = "The DNA STRC section is truncated.";
            return null;
        }

        if (structCount > (ulong)(reader.Remaining / 4))
        {
            error = "The DNA structure count exceeds the block.";
            return null;
        }

        var structs = ImmutableArray.CreateBuilder<BlendStruct>((int)structCount);
        for (var index = 0; index < structCount; index++)
        {
            if ((index & 0xFF) == 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
            }

            if (!TryUInt16(reader, little, out var typeIndex)
                || !TryUInt16(reader, little, out var fieldCount))
            {
                error = "A DNA structure entry is truncated.";
                return null;
            }

            if (typeIndex >= typeCount || fieldCount > reader.Remaining / 4)
            {
                error = "A DNA structure entry references invalid data.";
                return null;
            }

            var fields = ImmutableArray.CreateBuilder<BlendField>(fieldCount);
            for (var fieldIndex = 0; fieldIndex < fieldCount; fieldIndex++)
            {
                if (!TryUInt16(reader, little, out var fieldType)
                    || !TryUInt16(reader, little, out var fieldName))
                {
                    error = "A DNA structure field is truncated.";
                    return null;
                }

                if (fieldType >= typeCount || fieldName >= nameCount)
                {
                    error = "A DNA structure field references an invalid name or type.";
                    return null;
                }

                fields.Add(new BlendField(fieldType, fieldName));
            }

            structs.Add(new BlendStruct(typeIndex, fields.ToImmutable()));
        }

        return new BlendDna(
            names,
            types,
            typeLengths.ToImmutable(),
            structs.ToImmutable());
    }

    public string GetStructName(int structIndex) =>
        structIndex >= 0 && structIndex < Structs.Length
            ? Types[Structs[structIndex].TypeIndex]
            : string.Empty;

    public bool TryBuildLayout(
        string typeName,
        int pointerSize,
        out ImmutableArray<BlendFieldLayout> layout,
        out int structSize)
    {
        layout = default;
        structSize = 0;
        for (var index = 0; index < Structs.Length; index++)
        {
            if (string.Equals(Types[Structs[index].TypeIndex], typeName, StringComparison.Ordinal))
            {
                return TryBuildLayout(index, pointerSize, out layout, out structSize);
            }
        }

        return false;
    }

    private bool TryBuildLayout(
        int structIndex,
        int pointerSize,
        out ImmutableArray<BlendFieldLayout> layout,
        out int structSize)
    {
        layout = default;
        structSize = 0;
        var definition = Structs[structIndex];
        structSize = TypeLengths[definition.TypeIndex];
        var alignments = new Dictionary<ushort, int>();
        var fields = ImmutableArray.CreateBuilder<BlendFieldLayout>(definition.Fields.Length);
        var offset = 0L;

        foreach (var field in definition.Fields)
        {
            var rawName = Names[field.NameIndex];
            var (name, pointerDepth, dimensions) = ParseFieldName(rawName);
            if (string.IsNullOrWhiteSpace(name))
            {
                name = rawName;
            }
            var alignment = pointerDepth > 0
                ? pointerSize
                : GetAlignment(field.TypeIndex, pointerSize, alignments);
            offset = Align(offset, Math.Max(1, alignment));
            var elementSize = pointerDepth > 0 ? pointerSize : TypeLengths[field.TypeIndex];
            var elementCount = 1L;
            foreach (var dimension in dimensions)
            {
                elementCount *= Math.Max(1, dimension);
            }

            var size = elementSize * elementCount;
            if (size > int.MaxValue || offset > int.MaxValue)
            {
                return false;
            }

            fields.Add(new BlendFieldLayout(
                name,
                Types[field.TypeIndex],
                (int)offset,
                (int)size,
                pointerDepth,
                dimensions,
                pointerDepth == 0 && _structIndexByType.ContainsKey(field.TypeIndex),
                pointerDepth == 0 && string.Equals(Types[field.TypeIndex], "char", StringComparison.Ordinal)));
            offset += size;
        }

        if (offset > structSize)
        {
            return false;
        }

        layout = fields.ToImmutable();
        return true;
    }

    private int GetAlignment(ushort typeIndex, int pointerSize, Dictionary<ushort, int> cache)
    {
        if (cache.TryGetValue(typeIndex, out var cached))
        {
            return cached;
        }

        if (_structIndexByType.TryGetValue(typeIndex, out var structIndex))
        {
            cache[typeIndex] = 1;
            var alignment = 1;
            foreach (var field in Structs[structIndex].Fields)
            {
                var (_, pointerDepth, _) = ParseFieldName(Names[field.NameIndex]);
                var fieldAlignment = pointerDepth > 0
                    ? pointerSize
                    : GetAlignment(field.TypeIndex, pointerSize, cache);
                alignment = Math.Max(alignment, fieldAlignment);
            }

            cache[typeIndex] = alignment;
            return alignment;
        }

        var size = TypeLengths[typeIndex];
        var result = size switch
        {
            1 => 1,
            2 => 2,
            4 => 4,
            8 => 8,
            _ => Math.Min(Math.Max((int)size, 1), 8),
        };
        cache[typeIndex] = result;
        return result;
    }

    private static (string Name, int PointerDepth, ImmutableArray<int> Dimensions) ParseFieldName(string rawName)
    {
        var pointerDepth = 0;
        while (pointerDepth < rawName.Length && rawName[pointerDepth] == '*')
        {
            pointerDepth++;
        }

        var rest = rawName[pointerDepth..];
        var bracket = rest.IndexOf('[', StringComparison.Ordinal);
        if (rest.Contains('(', StringComparison.Ordinal) || bracket == 0)
        {
            return (rest.Trim('(', ')', '*'), Math.Max(pointerDepth, 1), []);
        }

        var name = bracket >= 0 ? rest[..bracket] : rest;
        var dimensions = ImmutableArray.CreateBuilder<int>();
        var position = bracket;
        while (position >= 0 && position < rest.Length && rest[position] == '[')
        {
            var close = rest.IndexOf(']', position + 1);
            if (close < 0 || !int.TryParse(rest.AsSpan(position + 1, close - position - 1), out var dimension))
            {
                dimensions.Clear();
                break;
            }

            dimensions.Add(dimension);
            position = close + 1;
        }

        return (name, pointerDepth, dimensions.ToImmutable());
    }

    private static bool TryReadNames(
        BlockReader reader,
        uint count,
        CancellationToken cancellationToken,
        out ImmutableArray<string> names,
        out string? error)
    {
        error = null;
        names = default;
        if (count > (ulong)reader.Remaining || count > 1_000_000)
        {
            error = "The DNA name or type count exceeds the block.";
            return false;
        }

        var builder = ImmutableArray.CreateBuilder<string>((int)count);
        for (var index = 0; index < count; index++)
        {
            if ((index & 0xFF) == 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
            }

            if (!reader.TryReadNullTerminatedString(MaxNameBytes, out var name, out _))
            {
                error = "A DNA name is truncated.";
                return false;
            }

            builder.Add(name);
        }

        names = builder.ToImmutable();
        return true;
    }

    private static bool TryMarker(BlockReader reader, ReadOnlySpan<byte> marker)
    {
        Span<byte> bytes = stackalloc byte[4];
        return reader.TryReadBytes(bytes) && bytes.SequenceEqual(marker);
    }

    private static bool TryUInt16(BlockReader reader, bool little, out ushort value) =>
        little ? reader.TryUInt16LE(out value) : reader.TryUInt16BE(out value);

    private static bool TryUInt32(BlockReader reader, bool little, out uint value) =>
        little ? reader.TryUInt32LE(out value) : reader.TryUInt32BE(out value);

    private static int AlignmentPadding(long position, int alignment)
    {
        var remainder = (int)(position % alignment);
        return remainder == 0 ? 0 : alignment - remainder;
    }

    private static long Align(long value, int alignment) =>
        (value + alignment - 1) / alignment * alignment;
}
