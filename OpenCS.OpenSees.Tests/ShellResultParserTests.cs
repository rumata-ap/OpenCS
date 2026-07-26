using OpenCS.OpenSees.Results;
using OpenCS.OpenSees.Structural;

namespace OpenCS.OpenSees.Tests;

public sealed class ShellResultParserTests
{
    [Fact]
    public void Parse_ReadsSectionResultantsAndIntegrationPoint()
    {
        using var fixture = new ShellArtifactFixture();
        fixture.WriteMarker("0\n");
        fixture.Write("node_disp.out", "1 0 0 0 0 0 0\n");
        fixture.Write("node_reactions.out", "1 10 20 30 40 50 60\n");
        fixture.Write("element_forces.out", "10 1 2 3 4 5 6 7 8 9 10 11 12 13 14 15 16 17 18 19 20 21 22 23 24\n");
        fixture.Write("section_forces.out", "10 1 1 2 3 4 5 6 7 8\n");

        var result = new ShellResultParser().Parse(fixture.Directory, FixtureElements());

        var force = Assert.Single(result.SectionResultants);
        Assert.Equal(10, force.ElementTag);
        Assert.Equal(1, force.IntegrationPoint);
        Assert.Equal(4, force.Mx);
        Assert.Equal(8, force.Qy);
    }

    [Fact]
    public void Parse_TreatsOpenSeesAnalyzeZeroAsCompleted()
    {
        using var fixture = new ShellArtifactFixture();
        fixture.WriteMarker("0\n");
        fixture.Write("node_disp.out", "1 0 0 0 0 0 0\n");
        fixture.Write("element_forces.out", "10 1 2 3 4 5 6 7 8 9 10 11 12 13 14 15 16 17 18 19 20 21 22 23 24\n");
        fixture.Write("section_forces.out", "10 1 1 2 3 4 5 6 7 8\n");

        var result = new ShellResultParser().Parse(fixture.Directory, FixtureElements());

        Assert.Equal("completed", result.Status);
    }

    [Fact]
    public void Parse_RejectsMissingMarker()
    {
        using var fixture = new ShellArtifactFixture();

        var ex = Assert.Throws<OpenSeesResultException>(() =>
            new ShellResultParser().Parse(fixture.Directory, FixtureElements()));

        Assert.Equal("MissingMarker", ex.Code);
    }

    private static IReadOnlyDictionary<int, NormalizedShellElement> FixtureElements() =>
        new Dictionary<int, NormalizedShellElement>
        {
            [10] = new(10, ShellElementKind.ASDShellQ4, [1, 2, 3, 4], 20, "s",
                ShellFrame.Identity, ShellIntegrationPolicy.Full, "fixture")
        };
}

internal sealed class ShellArtifactFixture : IDisposable
{
    public ShellArtifactFixture()
    {
        Directory = Path.Combine(Path.GetTempPath(), "opencs-shell-tests", Guid.NewGuid().ToString("N"));
        System.IO.Directory.CreateDirectory(Directory);
    }

    public string Directory { get; }

    public void Write(string name, string content) =>
        File.WriteAllText(Path.Combine(Directory, name), content);

    public void WriteMarker(string content) => Write("completed.marker", content);

    public void Dispose()
    {
        if (System.IO.Directory.Exists(Directory))
            System.IO.Directory.Delete(Directory, recursive: true);
    }
}
