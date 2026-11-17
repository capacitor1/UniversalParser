using UniversalParser.Core.Binary;

namespace UniversalParser.Core.Formats.Nbt;

internal sealed class NbtScanResult
{
    public bool Success { get; set; }
    public bool Complete { get; set; }
    public bool IncompleteTail { get; set; }
    public byte RootType { get; set; }
    public long RootEnd { get; set; }
    public long TagCount { get; set; }
}

/// <summary>
/// Lightweight NBT validation used by probing. It verifies framing (types, name lengths, list and
/// array lengths) without allocating tag names or building nodes. A tag that crosses the probe
/// window is reported as an incomplete tail so a large valid file can still match weakly.
/// </summary>
internal static class NbtScanner
{
    public static bool TryScan(
        IRandomAccessSource source,
        long limit,
        CancellationToken cancellationToken,
        out NbtScanResult result)
    {
        result = new NbtScanResult();
        if (limit < 4 || limit > source.Length)
        {
            return false;
        }

        if (!NbtRead.TryReadByte(source, 0, limit, out var rootType)
            || rootType != NbtConstants.CompoundType)
        {
            return false;
        }

        if (!NbtRead.TryReadUInt16BE(source, 1, limit, out var nameLength))
        {
            return false;
        }

        var nameStart = 3L;
        if (nameLength > limit - nameStart)
        {
            return false;
        }

        var position = nameStart + nameLength;
        result.RootType = rootType;
        result.TagCount = 1;
        var incomplete = false;
        if (!ScanPayload(
                source,
                rootType,
                limit,
                depth: 0,
                ref position,
                cancellationToken,
                ref incomplete,
                ref result))
        {
            if (!incomplete || result.TagCount == 0)
            {
                return false;
            }

            result.Success = true;
            result.IncompleteTail = true;
            result.RootEnd = position;
            return true;
        }

        result.Success = true;
        result.RootEnd = position;
        result.Complete = position == source.Length;
        result.IncompleteTail = incomplete;
        return true;
    }

    private static bool ScanPayload(
        IRandomAccessSource source,
        byte type,
        long limit,
        int depth,
        ref long position,
        CancellationToken cancellationToken,
        ref bool incomplete,
        ref NbtScanResult result)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (depth > NbtConstants.MaxScanDepth)
        {
            return false;
        }

        switch (type)
        {
            case NbtConstants.ByteType:
                return Advance(source, limit, ref position, 1, ref incomplete);
            case NbtConstants.ShortType:
                return Advance(source, limit, ref position, 2, ref incomplete);
            case NbtConstants.IntType:
            case NbtConstants.FloatType:
                return Advance(source, limit, ref position, 4, ref incomplete);
            case NbtConstants.LongType:
            case NbtConstants.DoubleType:
                return Advance(source, limit, ref position, 8, ref incomplete);
            case NbtConstants.ByteArrayType:
                {
                    if (!ReadInt32(source, limit, ref position, ref incomplete, out var length) || length < 0)
                    {
                        return false;
                    }

                    return Advance(source, limit, ref position, length, ref incomplete);
                }

            case NbtConstants.StringType:
                {
                    if (!ReadUInt16(source, limit, ref position, ref incomplete, out var length))
                    {
                        return false;
                    }

                    return Advance(source, limit, ref position, length, ref incomplete);
                }

            case NbtConstants.IntArrayType:
            case NbtConstants.LongArrayType:
                {
                    if (!ReadInt32(source, limit, ref position, ref incomplete, out var length) || length < 0)
                    {
                        return false;
                    }

                    var elementSize = type == NbtConstants.IntArrayType ? 4L : 8L;
                    return Advance(source, limit, ref position, length * elementSize, ref incomplete);
                }

            case NbtConstants.ListType:
                {
                    if (!NbtRead.TryReadByte(source, position, limit, out var elementType))
                    {
                        return FailAtWindowEdge(source, limit, ref incomplete);
                    }

                    position++;
                    if (!ReadInt32(source, limit, ref position, ref incomplete, out var length) || length < 0)
                    {
                        return false;
                    }

                    if (elementType > NbtConstants.MaxTagType
                        || (elementType == NbtConstants.EndType && length != 0))
                    {
                        return false;
                    }

                    for (var index = 0L; index < length; index++)
                    {
                        result.TagCount++;
                        if (!ScanPayload(
                                source,
                                elementType,
                                limit,
                                depth + 1,
                                ref position,
                                cancellationToken,
                                ref incomplete,
                                ref result))
                        {
                            return false;
                        }
                    }

                    return true;
                }

            case NbtConstants.CompoundType:
                {
                    while (true)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        if (position >= limit)
                        {
                            incomplete = limit < source.Length;
                            return false;
                        }

                        if (!NbtRead.TryReadByte(source, position, limit, out var childType))
                        {
                            return FailAtWindowEdge(source, limit, ref incomplete);
                        }

                        if (childType == NbtConstants.EndType)
                        {
                            position++;
                            return true;
                        }

                        if (!NbtConstants.IsValidTagType(childType))
                        {
                            return false;
                        }

                        result.TagCount++;
                        position++;
                        if (!ReadNameLength(source, limit, ref position, ref incomplete, out var nameLength))
                        {
                            return false;
                        }

                        if (!Advance(source, limit, ref position, nameLength, ref incomplete))
                        {
                            return false;
                        }

                        if (!ScanPayload(
                                source,
                                childType,
                                limit,
                                depth + 1,
                                ref position,
                                cancellationToken,
                                ref incomplete,
                                ref result))
                        {
                            return false;
                        }
                    }
                }

            default:
                return false;
        }
    }

    private static bool ReadNameLength(
        IRandomAccessSource source,
        long limit,
        ref long position,
        ref bool incomplete,
        out ushort nameLength)
    {
        nameLength = 0;
        if (!NbtRead.TryReadUInt16BE(source, position, limit, out nameLength))
        {
            return FailAtWindowEdge(source, limit, ref incomplete);
        }

        position += 2;
        return true;
    }

    private static bool ReadUInt16(
        IRandomAccessSource source,
        long limit,
        ref long position,
        ref bool incomplete,
        out ushort value)
    {
        value = 0;
        if (!NbtRead.TryReadUInt16BE(source, position, limit, out value))
        {
            return FailAtWindowEdge(source, limit, ref incomplete);
        }

        position += 2;
        return true;
    }

    private static bool ReadInt32(
        IRandomAccessSource source,
        long limit,
        ref long position,
        ref bool incomplete,
        out int value)
    {
        value = 0;
        if (!NbtRead.TryReadInt32BE(source, position, limit, out value))
        {
            return FailAtWindowEdge(source, limit, ref incomplete);
        }

        position += 4;
        return true;
    }

    private static bool Advance(
        IRandomAccessSource source,
        long limit,
        ref long position,
        long count,
        ref bool incomplete)
    {
        if (count < 0 || count > limit - position)
        {
            incomplete = limit < source.Length;
            return false;
        }

        position += count;
        return true;
    }

    private static bool FailAtWindowEdge(IRandomAccessSource source, long limit, ref bool incomplete)
    {
        incomplete = limit < source.Length;
        return false;
    }
}
