using System.Text.Json;
using CScore;
using CScore.Fem;
using OpenCS.OpenSees.Artifacts;
using OpenCS.OpenSees.CScore;
using OpenCS.OpenSees.Results;
using OpenCS.OpenSees.Runtime;
using OpenCS.OpenSees.Services;
using OpenCS.OpenSees.Tcl;

namespace OpenCS.OpenSees.Tests;

public class FemLinearAnalysisWorkflowTests
{
    static FemLinearAnalysisWorkflow MakeWorkflow()
    {
        var service = new FemLinearAnalysisService(
            new FemLinearTclGenerator(),
            new OpenSeesProcessRunner(),
            new OpenSeesArtifactStore(Path.Combine(Path.GetTempPath(), "opencs_fem_art_" + Guid.NewGuid().ToString("N"))),
            new FemLinearResultParser());
        return new FemLinearAnalysisWorkflow(service);
    }

    [Fact]
    public async Task RunAsync_MissingSection_ReturnsErrorWithoutRunning()
    {
        var input = new FemLinearWorkflowInput(
            MeshNodes: [new FemMeshNode { NodeTag = "1", SourceNodeTag = "1", SourceMemberTag = "1" },
                        new FemMeshNode { NodeTag = "2", X = 3, SourceNodeTag = "2", SourceMemberTag = "1" }],
            MeshElements: [new FemElement { ElemTag = "1", NodeIdsJson = "[1,2]", SourceMemberTag = "1",
                                            CrossSectionId = null, GjStrategy = "manual", GjManualValue = 1e6 }],
            SourceNodes: [new FemNode { Id = 1, NodeTag = "1", DofMask = 63 },
                          new FemNode { Id = 2, NodeTag = "2", X = 3 }],
            SourceMembers: [new FemMember { ElemTag = "1", CrossSectionId = null, GjStrategy = "manual", GjManualValue = 1e6 }],
            ResolvedLoads: [],
            SectionProps: new Dictionary<int, GeoProps>());

        var request = new OpenSeesRunRequest { ExecutablePath = "OpenSees.exe", WorkingDirectory = Path.GetTempPath() };
        var output = await MakeWorkflow().RunAsync(input, request, CancellationToken.None);

        Assert.Equal("error", output.Status);
        Assert.NotEmpty(output.Errors);
        using var doc = JsonDocument.Parse(output.DataJson);
        Assert.True(doc.RootElement.TryGetProperty("errors", out _));
    }
}
