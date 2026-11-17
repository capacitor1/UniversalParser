using System.Buffers.Binary;
using System.Collections.Immutable;
using System.Globalization;
using UniversalParser.Core.Binary;
using UniversalParser.Core.Model;
using UniversalParser.Core.Parsing;

namespace UniversalParser.Core.Formats.Asf;

/// <summary>
/// Builds the ASF object tree from object headers only. Header objects and header-extension
/// objects are GUID-delimited and become containers; Data Object packets are embedded payload
/// bytes, so they become one explicit unparsed region instead of per-packet nodes.
/// </summary>
internal sealed class AsfDocumentParser
{
    private readonly ParseContext _context;
    private readonly IRandomAccessSource _source;
    private readonly DiagnosticBag _diagnostics;

    public AsfDocumentParser(ParseContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        _context = context;
        _source = context.Source;
        _diagnostics = context.Diagnostics;
    }

    public FormatNode Parse()
    {
        var fileRange = new SourceRange(0, _source.Length);
        var children = ImmutableArray.CreateBuilder<FormatNode>();
        var position = 0L;
        while (position < _source.Length)
        {
            _context.ThrowIfCancellationRequested();
            _context.ReportProgress("ASF", position, _source.Length);

            if (_source.Length - position < AsfConstants.ObjectHeaderLength)
            {
                AddUnparsed(
                    children,
                    position,
                    _source.Length,
                    "<trailing data>",
                    DiagnosticCodes.TruncatedHeader,
                    $"{_source.Length - position} trailing byte(s) cannot form an ASF object header.");
                break;
            }

            var header = ReadObjectHeader(position, _source.Length);
            if (header.SizeZero)
            {
                if (header.Id != AsfConstants.DataObject)
                {
                    AddUnparsed(
                        children,
                        position,
                        _source.Length,
                        "<unparsed data>",
                        DiagnosticCodes.InvalidLength,
                        "Object Size is 0; only a Data Object in a broadcast file may declare an unknown size.");
                    break;
                }
            }
            else if (header.RawSize < AsfConstants.ObjectHeaderLength)
            {
                AddUnparsed(
                    children,
                    position,
                    _source.Length,
                    "<unparsed data>",
                    DiagnosticCodes.InvalidLength,
                    $"Object Size is {header.RawSize}; an ASF object header requires at least {AsfConstants.ObjectHeaderLength} bytes.");
                break;
            }

            var node = BuildObject(header);
            children.Add(node);
            if (header.End <= position)
            {
                _diagnostics.Warning(
                    DiagnosticCodes.InvalidLength,
                    "The ASF object end does not advance past its header.",
                    SourceRange.FromBounds(position, _source.Length));
                break;
            }

            position = header.End;
        }

        return CreateRoot(fileRange, children.ToImmutable());
    }

    private ImmutableArray<FormatNode> ParseObjectRange(long start, long end, uint? expectedCount)
    {
        var children = ImmutableArray.CreateBuilder<FormatNode>();
        if (start >= end)
        {
            return children.ToImmutable();
        }

        var position = start;
        var parsedCount = 0u;
        while (position < end)
        {
            _context.ThrowIfCancellationRequested();
            _context.ReportProgress("ASF", position, _source.Length);

            if (end - position < AsfConstants.ObjectHeaderLength)
            {
                AddUnparsed(
                    children,
                    position,
                    end,
                    "<trailing data>",
                    DiagnosticCodes.TruncatedChild,
                    $"{end - position} trailing byte(s) cannot form a child ASF object header.");
                break;
            }

            var header = ReadObjectHeader(position, end);
            if (header.SizeZero)
            {
                if (header.Id != AsfConstants.DataObject)
                {
                    AddUnparsed(
                        children,
                        position,
                        end,
                        "<unparsed data>",
                        DiagnosticCodes.InvalidLength,
                        "Child Object Size is 0; only a Data Object in a broadcast file may declare an unknown size.");
                    break;
                }
            }
            else if (header.RawSize < AsfConstants.ObjectHeaderLength)
            {
                AddUnparsed(
                    children,
                    position,
                    end,
                    "<unparsed data>",
                    DiagnosticCodes.InvalidLength,
                    $"Child Object Size is {header.RawSize}; an ASF object header requires at least {AsfConstants.ObjectHeaderLength} bytes.");
                break;
            }

            var node = BuildObject(header);
            children.Add(node);
            parsedCount++;
            if (header.End <= position)
            {
                _diagnostics.Warning(
                    DiagnosticCodes.InvalidLength,
                    "The child ASF object end does not advance past its header.",
                    SourceRange.FromBounds(position, end));
                break;
            }

            position = header.End;
        }

        if (expectedCount is { } expected && parsedCount != expected)
        {
            _diagnostics.Warning(
                DiagnosticCodes.SchemaMismatch,
                $"Number of Header Objects is {expected}, but {parsedCount} object(s) were parsed.",
                SourceRange.FromBounds(start, end));
        }

        return children.ToImmutable();
    }

    private FormatNode BuildObject(AsfObjectHeader header)
    {
        var name = AsfGuids.GetObjectTreeName(header.Id);
        var range = new SourceRange(header.Offset, header.End - header.Offset);
        var stableId = string.Create(
            CultureInfo.InvariantCulture,
            $"asf:obj:{header.Id:D}:{header.Offset}");

        if (header.Id == AsfConstants.HeaderObject)
        {
            return BuildHeaderObject(header, range, stableId);
        }

        if (header.Id == AsfConstants.HeaderExtensionObject)
        {
            return BuildHeaderExtensionObject(header, range, stableId);
        }

        if (header.Id == AsfConstants.DataObject)
        {
            return BuildDataObject(header, range, stableId);
        }

        var kind = header.Id == AsfConstants.PaddingObject
            ? NodeKind.Padding
            : AsfGuids.IsKnownObject(header.Id) ? NodeKind.Leaf : NodeKind.Unparsed;

        return new FormatNode(name, kind, range, formatId: AsfParser.IdValue, stableId: stableId);
    }

    private FormatNode BuildHeaderObject(
        AsfObjectHeader header,
        SourceRange range,
        string stableId)
    {
        var dataStart = header.PayloadOffset;
        var fixedLength = Math.Min(AsfConstants.HeaderObjectFixedLength, header.End - dataStart);
        var childStart = dataStart + Math.Max(0, fixedLength);
        uint numberOfHeaderObjects = 0;

        if (fixedLength < AsfConstants.HeaderObjectFixedLength)
        {
            _diagnostics.Warning(
                DiagnosticCodes.TruncatedHeader,
                "The Header Object is too short for Number of Header Objects, Reserved1, and Reserved2.",
                range);
        }
        else
        {
            Span<byte> fixedHeader = stackalloc byte[AsfConstants.HeaderObjectFixedLength];
            if (SourceRead.ReadAtMost(_source, dataStart, fixedHeader) == fixedHeader.Length)
            {
                numberOfHeaderObjects = BinaryPrimitives.ReadUInt32LittleEndian(fixedHeader[..4]);
                if (fixedHeader[4] != AsfConstants.HeaderReserved1 || fixedHeader[5] != AsfConstants.HeaderReserved2)
                {
                    _diagnostics.Warning(
                        DiagnosticCodes.SchemaMismatch,
                        $"Reserved1/Reserved2 are {fixedHeader[4]}/{fixedHeader[5]}; the specification requires 1/2.",
                        new SourceRange(dataStart + 4, 2));
                }
            }
            else
            {
                _diagnostics.Warning(
                    DiagnosticCodes.TruncatedHeader,
                    "The Header Object fixed fields could not be read.",
                    new SourceRange(dataStart, fixedLength));
            }
        }

        var children = ParseObjectRange(childStart, header.End, numberOfHeaderObjects);
        var payloadRange = SourceRange.FromBounds(Math.Min(childStart, header.End), header.End);
        return new FormatNode(
            "HeaderObject",
            NodeKind.Container,
            range,
            payloadRange,
            children,
            AsfParser.IdValue,
            stableId);
    }

    private FormatNode BuildHeaderExtensionObject(
        AsfObjectHeader header,
        SourceRange range,
        string stableId)
    {
        var dataStart = header.PayloadOffset;
        var fixedLength = Math.Min(AsfConstants.HeaderExtensionFixedLength, header.End - dataStart);
        var childStart = dataStart + Math.Max(0, fixedLength);
        var dataSize = 0u;

        if (fixedLength < AsfConstants.HeaderExtensionFixedLength)
        {
            _diagnostics.Warning(
                DiagnosticCodes.TruncatedHeader,
                "The Header Extension Object is too short for its fixed fields.",
                range);
        }
        else
        {
            Span<byte> dataSizeBytes = stackalloc byte[4];
            if (SourceRead.ReadAtMost(_source, dataStart + 18, dataSizeBytes) == dataSizeBytes.Length)
            {
                dataSize = BinaryPrimitives.ReadUInt32LittleEndian(dataSizeBytes);
            }
            else
            {
                _diagnostics.Warning(
                    DiagnosticCodes.TruncatedHeader,
                    "Header Extension Data Size could not be read.",
                    new SourceRange(dataStart + 18, 4));
            }
        }

        var declaredChildEnd = childStart + dataSize;
        var childEnd = Math.Min(header.End, declaredChildEnd);
        if (declaredChildEnd > header.End)
        {
            _diagnostics.Warning(
                DiagnosticCodes.TruncatedPayload,
                $"Header Extension Data Size ({dataSize}) points beyond the object end; using the available range.",
                new SourceRange(dataStart + 18, 4));
        }

        var children = ImmutableArray.CreateBuilder<FormatNode>();
        children.AddRange(ParseObjectRange(childStart, childEnd, expectedCount: null));
        if (childEnd < header.End)
        {
            AddUnparsed(
                children,
                childEnd,
                header.End,
                "<unparsed data>",
                DiagnosticCodes.InvalidLength,
                $"{header.End - childEnd} byte(s) follow the declared Header Extension data.");
        }

        var payloadRange = SourceRange.FromBounds(Math.Min(childStart, header.End), header.End);
        return new FormatNode(
            "HeaderExtensionObject",
            NodeKind.Container,
            range,
            payloadRange,
            children.ToImmutable(),
            AsfParser.IdValue,
            stableId);
    }

    private FormatNode BuildDataObject(AsfObjectHeader header, SourceRange range, string stableId)
    {
        var dataStart = header.PayloadOffset;
        var childStart = dataStart + Math.Min(AsfConstants.DataObjectFixedLength, header.End - dataStart);
        if (header.End - dataStart < AsfConstants.DataObjectFixedLength)
        {
            _diagnostics.Warning(
                DiagnosticCodes.TruncatedHeader,
                "The Data Object is too short for File ID, Total Data Packets, and Reserved.",
                range);
        }

        var children = ImmutableArray.CreateBuilder<FormatNode>();
        if (childStart < header.End)
        {
            var packetRange = SourceRange.FromBounds(childStart, header.End);
            children.Add(new FormatNode(
                "DataPackets",
                NodeKind.Unparsed,
                packetRange,
                packetRange,
                stableId: string.Create(
                    CultureInfo.InvariantCulture,
                    $"asf:data-packets:{childStart}")));
        }

        var payloadRange = SourceRange.FromBounds(Math.Min(childStart, header.End), header.End);
        return new FormatNode(
            "DataObject",
            NodeKind.Container,
            range,
            payloadRange,
            children.ToImmutable(),
            AsfParser.IdValue,
            stableId);
    }

    private AsfObjectHeader ReadObjectHeader(long offset, long limit)
    {
        Span<byte> buffer = stackalloc byte[AsfConstants.ObjectHeaderLength];
        if (SourceRead.ReadAtMost(_source, offset, buffer) < buffer.Length)
        {
            return new AsfObjectHeader(Guid.Empty, 0, offset, offset, Truncated: true, SizeZero: false);
        }

        var id = new Guid(buffer[..16]);
        var rawSize = BinaryPrimitives.ReadUInt64LittleEndian(buffer[16..24]);
        if (rawSize == 0)
        {
            return new AsfObjectHeader(id, rawSize, offset, limit, Truncated: false, SizeZero: true);
        }

        if (rawSize < AsfConstants.ObjectHeaderLength)
        {
            return new AsfObjectHeader(id, rawSize, offset, offset, Truncated: false, SizeZero: false);
        }

        var available = (ulong)(limit - offset);
        if (rawSize > available)
        {
            _diagnostics.Warning(
                DiagnosticCodes.TruncatedPayload,
                $"Object Size is {rawSize}, but only {available} byte(s) remain; the object is clamped to the available range.",
                new SourceRange(offset + 16, 8));
            return new AsfObjectHeader(id, rawSize, offset, limit, Truncated: true, SizeZero: false);
        }

        return new AsfObjectHeader(
            id,
            rawSize,
            offset,
            offset + (long)rawSize,
            Truncated: false,
            SizeZero: false);
    }

    private void AddUnparsed(
        ImmutableArray<FormatNode>.Builder children,
        long start,
        long end,
        string name,
        string code,
        string message)
    {
        if (start < end)
        {
            var range = SourceRange.FromBounds(start, end);
            children.Add(new FormatNode(name, NodeKind.Unparsed, range, range));
        }

        _diagnostics.Warning(code, message, SourceRange.FromBounds(start, end));
    }

    private static FormatNode CreateRoot(SourceRange range, ImmutableArray<FormatNode> children) =>
        new(
            "Advanced Systems Format",
            NodeKind.Container,
            range,
            range,
            children,
            AsfParser.IdValue,
            "asf-root");
}
