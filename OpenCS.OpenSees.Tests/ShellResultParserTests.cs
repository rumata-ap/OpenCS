using OpenCS.OpenSees.Results;
using OpenCS.OpenSees.Structural;

namespace OpenCS.OpenSees.Tests;

public sealed class ShellResultParserTests
{
    private static IReadOnlyDictionary<int, NormalizedShellElement> FixtureElements() =>
        new Dictionary<int, NormalizedShellElement>
        {
            [10] = new(10, ShellElementKind.ASDShellQ4, [1, 2, 3, 4], 20, "s",
                ShellFrame.Identity, ShellIntegrationPolicy.Full, "fixture")
        };

    private static void WriteOneStepFixture(ShellArtifactFixture fixture, int ipCount = 4)
    {
        fixture.Write("recorder_order.json",
            "{\"nodeTags\":[1],\"restrainedTags\":[],\"shellElementTags\":[10]," +
            "\"nonlinearBeamElementTags\":[],\"sectionForceGroups\":[" +
            string.Join(',', Enumerable.Range(1, ipCount).Select(p =>
                $"{{\"point\":{p},\"elementTags\":[10],\"file\":\"shell_section_forces_ip{p}.out\"}}")) +
            "]}");
        fixture.Write("step_status.out", "1 0 1.0 1 0\n");
        fixture.Write("shell_node_disp.out", "1.0 0.001 0 0 0 0 0\n");
        fixture.Write("shell_element_forces.out",
            "1.0 " + string.Join(' ', Enumerable.Repeat("1", 24)) + "\n");
        for (int p = 1; p <= ipCount; p++)
            fixture.Write($"shell_section_forces_ip{p}.out", "1.0 1 2 3 4 5 6 7 8\n");
        fixture.Write("completed.marker", "done\n");
    }

    [Fact]
    public void Parse_ReadsSingleConvergedStep()
    {
        using var fixture = new ShellArtifactFixture();
        WriteOneStepFixture(fixture);

        var result = new ShellResultParser().Parse(fixture.Directory, FixtureElements());

        var step = Assert.Single(result.Steps);
        Assert.Equal(1, step.StepIndex);
        Assert.Equal(1.0, step.LoadFactor);
        Assert.True(step.Converged);
        Assert.Single(step.Displacements);
        Assert.Single(step.SectionResultants, s => s.ElementTag == 10 && s.IntegrationPoint == 1);
        Assert.Equal(4, step.SectionResultants.Count);
    }

    [Fact]
    public void Parse_TopLevelViewsReflectLastStep()
    {
        using var fixture = new ShellArtifactFixture();
        WriteOneStepFixture(fixture);

        var result = new ShellResultParser().Parse(fixture.Directory, FixtureElements());

        Assert.Equal("completed", result.Status);
        Assert.Single(result.Displacements);
        Assert.Equal(0.001, result.Displacements[0].Ux);
        Assert.Equal(4, result.SectionResultants.Count);
    }

    [Fact]
    public void Parse_LoadsMaterialStateCatalogWithoutEagerStateRows()
    {
        using var fixture = new ShellArtifactFixture();
        WriteOneStepFixture(fixture);
        fixture.Write("state_order.json", """
        {
          "version": 1,
          "shellLayerGroups": [
            { "integrationPoint": 1, "layerIndex": 1, "responseKind": "stress", "elementTags": [10], "fileName": "stress.out", "componentCount": 5 },
            { "integrationPoint": 1, "layerIndex": 1, "responseKind": "strain", "elementTags": [10], "fileName": "strain.out", "componentCount": 5 }
          ],
          "beamFiberLocations": [],
          "optionalResponses": []
        }
        """);

        var result = new ShellResultParser().Parse(fixture.Directory, FixtureElements());

        Assert.NotNull(result.StateCatalog);
        Assert.Equal(2, result.StateCatalog!.ShellLayerGroups.Count);
        Assert.Single(result.Steps);
    }

    [Fact]
    public void Parse_RejectsMissingRecorderOrder()
    {
        using var fixture = new ShellArtifactFixture();

        var ex = Assert.Throws<OpenSeesResultException>(() =>
            new ShellResultParser().Parse(fixture.Directory, FixtureElements()));

        Assert.Equal("MissingFile", ex.Code);
    }

    [Fact]
    public void Parse_LastStepFailed_StatusIsNotConverged()
    {
        using var fixture = new ShellArtifactFixture();
        fixture.Write("recorder_order.json",
            "{\"nodeTags\":[1],\"restrainedTags\":[],\"shellElementTags\":[10]," +
            "\"nonlinearBeamElementTags\":[],\"sectionForceGroups\":[]}");
        fixture.Write("step_status.out", "1 0 1.0 0 1\n");
        fixture.Write("completed.marker", "done\n");

        var result = new ShellResultParser().Parse(fixture.Directory, FixtureElements());

        Assert.Equal("not_converged", result.Status);
        var step = Assert.Single(result.Steps);
        Assert.False(step.Converged);
        Assert.Empty(step.Displacements);
    }
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
