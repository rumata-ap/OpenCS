using System.Text.Json;
using CScore;
using CScore.Fem;
using OpenCS.OpenSees.Artifacts;
using OpenCS.OpenSees.CScore;
using OpenCS.OpenSees.Results;
using OpenCS.OpenSees.Runtime;
using OpenCS.OpenSees.Services;
using OpenCS.OpenSees.Tcl;
using Xunit;

namespace OpenCS.OpenSees.Tests;

public class FemNonlinearAnalysisWorkflowTests
{
    static FemNonlinearAnalysisWorkflow MakeWorkflow()
    {
        var service = new FemNonlinearAnalysisService(
            new FemNonlinearTclGenerator(),
            new OpenSeesProcessRunner(),
            new OpenSeesArtifactStore(Path.Combine(Path.GetTempPath(), "opencs_fem_nl_wf_" + Guid.NewGuid().ToString("N"))),
            new FemNonlinearResultParser());
        return new FemNonlinearAnalysisWorkflow(service);
    }

    static FemNonlinearAnalysisOptions Options() => new("Linear", 10, 1e-6, 50, 5);

    [Fact]
    public async Task RunAsync_MissingSection_ReturnsErrorWithoutRunning()
    {
        var input = new FemNonlinearWorkflowInput(
            MeshNodes: [new FemMeshNode { NodeTag = "1", SourceNodeTag = "1", SourceMemberTag = "1" },
                        new FemMeshNode { NodeTag = "2", X = 3, SourceNodeTag = "2", SourceMemberTag = "1" }],
            MeshElements: [new FemElement { ElemTag = "1", NodeIdsJson = "[1,2]", SourceMemberTag = "1",
                                            CrossSectionId = null, GjStrategy = "manual", GjManualValue = 1e6 }],
            SourceNodes: [new FemNode { Id = 1, NodeTag = "1", DofMask = 63 },
                          new FemNode { Id = 2, NodeTag = "2", X = 3 }],
            SourceMembers: [new FemMember { ElemTag = "1", CrossSectionId = null, GjStrategy = "manual", GjManualValue = 1e6 }],
            ResolvedLoads: [],
            Sections: new Dictionary<int, CrossSection>(),
            Materials: new Dictionary<int, Material>(),
            CustomDiagramPool: null,
            CalcType: CalcType.C,
            Options: Options());

        var request = new OpenSeesRunRequest { ExecutablePath = "OpenSees.exe", WorkingDirectory = Path.GetTempPath() };
        var output = await MakeWorkflow().RunAsync(input, request, CancellationToken.None);

        Assert.Equal("error", output.Status);
        Assert.NotEmpty(output.Errors);
        using var doc = JsonDocument.Parse(output.DataJson);
        Assert.True(doc.RootElement.TryGetProperty("errors", out _));
    }
}
