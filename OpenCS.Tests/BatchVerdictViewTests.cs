using CScore.Batch;
using OpenCS.ViewModels;
using Xunit;

namespace OpenCS.Tests;

/// <summary>Отображение причины в окнах пакетов: сортировка, тон строки, фильтр.</summary>
public sealed class BatchVerdictViewTests
{
    static BatchVerdictView V(string status, double? util, string text = "t", string @ref = "")
        => new(new BatchRowVerdict(status, util, "c", @ref, text));

    [Fact]
    public void SortUtil_InfinityTopNullBottom()
    {
        Assert.Equal(double.MaxValue, V("not_passed", double.PositiveInfinity).SortUtil);
        Assert.Equal(-1.0, V("not_converged", null).SortUtil);
        Assert.Equal(0.5, V("ok", 0.5).SortUtil);
    }

    [Fact]
    public void UtilText()
    {
        Assert.Equal("∞", V("not_passed", double.PositiveInfinity).UtilText);
        Assert.Equal("—", V("not_converged", null).UtilText);
    }

    [Theory]
    [InlineData("ok", 0.5, BatchVerdictTone.Ok)]
    [InlineData("ok", 0.995, BatchVerdictTone.NearLimit)]
    [InlineData("not_passed", 1.01, BatchVerdictTone.Fail)]
    [InlineData("not_passed", null, BatchVerdictTone.Fail)]
    [InlineData("not_converged", null, BatchVerdictTone.NotConverged)]
    [InlineData("not_applicable", null, BatchVerdictTone.NotApplicable)]
    [InlineData("error", null, BatchVerdictTone.Error)]
    public void Tone(string status, double? util, BatchVerdictTone tone)
        => Assert.Equal(tone, V(status, util).Tone);

    [Fact]
    public void Filter_Failed_OnlyNotPassed()
    {
        Assert.True(BatchVerdictFilters.Match(V("not_passed", 1.2), BatchVerdictFilter.Failed));
        Assert.False(BatchVerdictFilters.Match(V("not_converged", null), BatchVerdictFilter.Failed));
        Assert.False(BatchVerdictFilters.Match(V("ok", 0.4), BatchVerdictFilter.Failed));
    }

    [Fact]
    public void Filter_NotConverged_IncludesErrors()
    {
        Assert.True(BatchVerdictFilters.Match(V("not_converged", null), BatchVerdictFilter.NotConverged));
        Assert.True(BatchVerdictFilters.Match(V("error", null), BatchVerdictFilter.NotConverged));
        Assert.False(BatchVerdictFilters.Match(V("not_passed", 2), BatchVerdictFilter.NotConverged));
    }

    [Fact]
    public void Filter_All_MatchesEverything()
        => Assert.True(BatchVerdictFilters.Match(V("ok", 0.1), BatchVerdictFilter.All));

    [Fact]
    public void ShellSlsVm_ElemLimitAndReason()
    {
        const string json = """
            {"acrc_lim_mm":0.3,"rows":[
              {"num":1,"label":"a","elem":17,"status":"not_passed","acrc_mm":0.36,"direction":"x","face":"верх",
               "verdict":{"status":"not_passed","util":1.2,"util_inf":false,"code":"acrc","ref":"п. 8.2.6, (8.118)","text":"acrc > acrc,ult"}},
              {"num":2,"label":"b","status":"not_converged"}]}
            """;
        var vm = new ShellLayeredSlsBatchResultVM(json);
        Assert.Equal(0.3, vm.AcrcLimMm);
        Assert.Equal("17", vm.Rows[0].ElemText);
        Assert.Equal(BatchVerdictTone.Fail, vm.Rows[0].Reason.Tone);
        Assert.Equal(0.36, vm.Rows[0].AcrcValue);
        Assert.Equal("—", vm.Rows[1].ElemText);
        Assert.Equal(BatchVerdictTone.NotConverged, vm.Rows[1].Reason.Tone);
        Assert.Equal(-1, vm.Rows[1].AcrcValue);
    }

    [Fact]
    public void ShellSlsVm_OldResultWithoutLimit()
        => Assert.Null(new ShellLayeredSlsBatchResultVM("""{"rows":[{"num":1,"label":"a","status":"ok","acrc_mm":0.1}]}""").AcrcLimMm);

    [Fact]
    public void ReasonAndRef()
    {
        var v = V("not_passed", 1.2, "εb > εb,ult", "п. 8.1.24, (8.37)");
        Assert.Equal("εb > εb,ult", v.ReasonText);
        Assert.Equal("п. 8.1.24, (8.37)", v.RefText);
    }
}
