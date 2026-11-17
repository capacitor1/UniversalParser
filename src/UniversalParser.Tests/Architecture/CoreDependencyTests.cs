using UniversalParser.Core.Formats.Raw;

namespace UniversalParser.Tests.Architecture;

public sealed class CoreDependencyTests
{
    [Fact]
    public void CoreDoesNotReferenceWinForms()
    {
        var references = typeof(RawParser).Assembly
            .GetReferencedAssemblies()
            .Select(assembly => assembly.Name ?? string.Empty)
            .ToArray();

        Assert.DoesNotContain("System.Windows.Forms", references);
        Assert.DoesNotContain("System.Drawing", references);
    }
}
