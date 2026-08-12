using System.Text.Json;
using OpenCS.OpenSees.Structural;
using Xunit;

namespace OpenCS.OpenSees.Tests;

public class FemNonlinearResultLegacyCompatTests
{
    // Снимок реального DataJson, сохранённого ДО появления path control (нет
    // StagePathControls/PathControlSwitches/StageCompletions) — воспроизводит структуру,
    // которую пишет FemNonlinearAnalysisService.RunAsync.
    const string LegacyJson = """
    {
        "Status": "ok",
        "Steps": [
            { "StepIndex": 1, "LoadFactor": 1.0, "Converged": true, "Displacements": [], "Reactions": [], "ElementForces": [], "StageIndex": 0 }
        ],
        "Diagnostics": [],
        "ArtifactDirectory": null,
        "LimitReached": false,
        "LastConvergedLoadFactor": 1.0,
        "RefinementDivisions": 10,
        "CalcTypeName": "C",
        "StageTags": ["Стадия 1"]
    }
    """;

    [Fact]
    public void Deserialize_LegacyJsonWithoutNewFields_DoesNotThrow()
    {
        var result = JsonSerializer.Deserialize<FemNonlinearResult>(LegacyJson);
        Assert.NotNull(result);
        Assert.Empty(result!.StagePathControls);
        Assert.Empty(result.PathControlSwitches);
        Assert.Empty(result.StageCompletions);
        Assert.Single(result.Steps);
        Assert.Null(result.Steps[0].StopReason);
    }
}
