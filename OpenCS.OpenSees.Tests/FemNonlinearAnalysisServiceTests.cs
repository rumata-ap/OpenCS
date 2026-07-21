using OpenCS.OpenSees.Artifacts;
using OpenCS.OpenSees.Results;
using OpenCS.OpenSees.Runtime;
using OpenCS.OpenSees.Services;
using OpenCS.OpenSees.Structural;
using OpenCS.OpenSees.Tcl;
using Xunit;

namespace OpenCS.OpenSees.Tests;

public class FemNonlinearAnalysisServiceTests
{
    [Fact]
    public async Task RunAsync_InvalidModel_ReturnsErrorWithoutRunningProcess()
    {
        var service = new FemNonlinearAnalysisService(
            new FemNonlinearTclGenerator(),
            new OpenSeesProcessRunner(),
            new OpenSeesArtifactStore(Path.Combine(Path.GetTempPath(), "opencs_fem_nl_art_" + Guid.NewGuid().ToString("N"))),
            new FemNonlinearResultParser());

        var invalidModel = new FemNonlinearModel();   // без узлов/элементов/секций — Validate() бросит

        var result = await service.RunAsync(invalidModel,
            new OpenSeesRunRequest { ExecutablePath = "OpenSees.exe", WorkingDirectory = Path.GetTempPath() },
            CancellationToken.None);

        Assert.Equal("error", result.Status);
        Assert.NotEmpty(result.Diagnostics);
    }
}
