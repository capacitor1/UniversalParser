namespace UniversalParser.Core.Binary;

/// <summary>
/// Raised when a bounded read cannot be satisfied by the source.
/// </summary>
public sealed class SourceReadException : IOException
{
    public SourceReadException()
        : base("A bounded source read failed.")
    {
    }

    public SourceReadException(string message)
        : base(message)
    {
    }

    public SourceReadException(string message, Exception innerException)
        : base(message, innerException)
    {
    }

    public SourceReadException(long offset, int requested, long available, string? message = null)
        : base(message ?? $"Source read failed at offset 0x{offset:X}: requested {requested} byte(s), available {available}.")
    {
        Offset = offset;
        Requested = requested;
        Available = available;
    }

    public long Offset { get; }
    public int Requested { get; }
    public long Available { get; }
}
