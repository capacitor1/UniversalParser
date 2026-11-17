using UniversalParser.Core.Formats.Raw;
using UniversalParser.Core.Parsing;

namespace UniversalParser.Tests.Parsing;

public sealed class ParseSessionTests
{
    [Fact]
    public void OpenBuildsDocumentAndParsesNodes()
    {
        var path = CreateTemporaryFile([0x10, 0x20, 0x30, 0x40, 0x50]);
        try
        {
            using var session = ParseSession.Open(path, new ParserEngine(ParserRegistry.CreateDefault()));

            Assert.Equal(RawParser.IdValue, session.Document.ParserId);
            Assert.Equal(5, session.Length);
            Assert.False(session.IsDisposed);
            Assert.Equal(path, session.FilePath);
            Assert.Equal(File.GetCreationTimeUtc(path), session.CreationTimeUtc);
            Assert.Equal(File.GetAttributes(path), session.FileAttributes);

            var root = session.Document.Root;
            var result = session.ParseNode(root);
            Assert.Equal(root.Range, result.Range);
            Assert.Contains(result.Fields, static field => field.Key == "<FileSize>");
        }
        finally
        {
            DeleteTemporaryFile(path);
        }
    }

    [Fact]
    public void DisposeReleasesSourceAndBlocksFurtherUse()
    {
        var path = CreateTemporaryFile([1, 2, 3]);
        try
        {
            var session = ParseSession.Open(path, new ParserEngine(ParserRegistry.CreateDefault()));
            var node = session.Document.Root;
            session.Dispose();

            Assert.True(session.IsDisposed);
            Assert.Throws<ObjectDisposedException>(() => _ = session.Source);
            Assert.Throws<ObjectDisposedException>(() => session.ParseNode(node));
            session.Dispose();
        }
        finally
        {
            DeleteTemporaryFile(path);
        }
    }

    [Fact]
    public void ReopeningWorksAfterDispose()
    {
        var path = CreateTemporaryFile([9, 8, 7]);
        try
        {
            var engine = new ParserEngine(ParserRegistry.CreateDefault());
            for (var index = 0; index < 3; index++)
            {
                using var session = ParseSession.Open(path, engine);
                Assert.Equal(RawParser.IdValue, session.Document.ParserId);
            }
        }
        finally
        {
            DeleteTemporaryFile(path);
        }
    }

    [Fact]
    public void CancelledOpenThrowsAndLeavesNothingBehind()
    {
        var path = CreateTemporaryFile([4, 5, 6]);
        try
        {
            using var cts = new CancellationTokenSource();
            cts.Cancel();

            Assert.Throws<OperationCanceledException>(
                () => ParseSession.Open(path, new ParserEngine(ParserRegistry.CreateDefault()), null, null, cts.Token));
        }
        finally
        {
            DeleteTemporaryFile(path);
        }
    }

    [Fact]
    public void MissingFileThrowsFileNotFound()
    {
        var missing = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"), "missing.bin");

        Assert.ThrowsAny<IOException>(
            () => ParseSession.Open(missing, new ParserEngine(ParserRegistry.CreateDefault())));
    }

    private static string CreateTemporaryFile(byte[] bytes)
    {
        var path = Path.Combine(Path.GetTempPath(), $"universalparser-{Guid.NewGuid():N}.bin");
        File.WriteAllBytes(path, bytes);
        return path;
    }

    private static void DeleteTemporaryFile(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (IOException)
        {
            // Best-effort cleanup only; a locked temp file must not fail the test.
        }
    }
}
