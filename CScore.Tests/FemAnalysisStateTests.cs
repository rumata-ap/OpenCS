using CScore.Fem;
using Xunit;

namespace CScore.Tests;

public sealed class FemAnalysisStateTests
{
    [Fact]
    public void InvalidateResult_clears_result_and_resets_status()
    {
        var analysis = new FemAnalysis
        {
            ResultId = 42,
            Status = "ok"
        };

        analysis.InvalidateResult();

        Assert.Null(analysis.ResultId);
        Assert.Equal("created", analysis.Status);
    }
}
