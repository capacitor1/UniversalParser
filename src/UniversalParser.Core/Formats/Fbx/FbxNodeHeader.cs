namespace UniversalParser.Core.Formats.Fbx;

/// <summary>
/// One decoded FBX node-record header. <see cref="EndOffset"/> is the absolute file offset of the
/// first byte after the record (including its null-record terminator).
/// </summary>
internal readonly record struct FbxNodeHeader(
    long Offset,
    long EndOffset,
    ulong NumProperties,
    ulong PropertyListLength,
    int NameLength,
    long NameOffset,
    string Name,
    long PropertyOffset,
    bool IsNullRecord);
