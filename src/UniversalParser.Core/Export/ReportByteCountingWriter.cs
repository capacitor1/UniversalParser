using System.Text;

namespace UniversalParser.Core.Export;

/// <summary>
/// Pass-through writer that reports how many UTF-8 bytes the report body occupies. It deliberately
/// has no budget: only the hex section is windowed, and the field section must never be truncated.
/// </summary>
[System.Diagnostics.CodeAnalysis.SuppressMessage(
    "Reliability",
    "CA2213:Disposable fields should be disposed",
    Justification = "The wrapper observes but does not own the caller's writer.")]
internal sealed class ReportByteCountingWriter : TextWriter
{
    private readonly TextWriter _inner;
    private long _bytesWritten;

    public ReportByteCountingWriter(TextWriter inner)
    {
        ArgumentNullException.ThrowIfNull(inner);
        _inner = inner;
    }

    public override Encoding Encoding => _inner.Encoding;

    public long BytesWritten => _bytesWritten;

    public override void Write(char value)
    {
        _inner.Write(value);
        _bytesWritten += Encoding.UTF8.GetByteCount(stackalloc char[] { value });
    }

    public override void Write(string? value) => Write(value.AsSpan());

    public override void Write(ReadOnlySpan<char> value)
    {
        _inner.Write(value);
        _bytesWritten += Encoding.UTF8.GetByteCount(value);
    }

    public override void WriteLine() => WriteLine(string.Empty);

    public override void WriteLine(string? value)
    {
        _inner.WriteLine(value);
        _bytesWritten += Encoding.UTF8.GetByteCount(value.AsSpan());
        _bytesWritten += Encoding.UTF8.GetByteCount(_inner.NewLine);
    }

    public override void Flush() => _inner.Flush();

    protected override void Dispose(bool disposing)
    {
        try
        {
            if (disposing)
            {
                _inner.Flush();
            }
        }
        finally
        {
            base.Dispose(disposing);
        }
    }
}
