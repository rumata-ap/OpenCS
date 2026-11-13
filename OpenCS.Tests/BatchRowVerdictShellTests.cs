using CScore;
using CScore.Batch;
using OpenCS.Tasks;
using Xunit;

namespace OpenCS.Tests;

/// <summary>Причина строки в пакетах пластин: «не сошлось» отдельно от «не проходит».</summary>
public sealed class BatchRowVerdictShellTests
{
    [Fact]
    public void Sls_AboveLimit()
    {
        var v = ShellLayeredBatchVerdicts.Sls(true, 1e-9, 0.36, 0.3, "x", "верх");
        Assert.Equal(BatchRowStatus.NotPassed, v.Status);
        Assert.Equal(1.2, v.Util!.Value, 6);
        Assert.Contains("0,360", v.Text);
        Assert.Contains("x / верх", v.Text);
        Assert.Equal("п. 8.2.6, (8.118)", v.Ref);
    }

    [Fact]
    public void Sls_NotConverged_IsNotFailure()
        => Assert.Equal(BatchRowStatus.NotConverged, ShellLayeredBatchVerdicts.Sls(false, 0.5, 0, 0.3, "", "").Status);

    [Fact]
    public void Sls_NoLimit_Passes()
        => Assert.Equal(BatchRowStatus.Ok, ShellLayeredBatchVerdicts.Sls(true, 0, 0.5, 0, "", "").Status);

    [Fact]
    public void Uls_NotPassedKeepsFormulaAndUtil()
    {
        var v = ShellLayeredBatchVerdicts.Uls(new ShellLayeredCheck.Result(false, 1.3, "п. 8.1.24, (8.37); εb,ult — п. 8.1.30", "εb2 = …", true, 7, 1e-8));
        Assert.Equal(BatchRowStatus.NotPassed, v.Status);
        Assert.Equal(1.3, v.Util);
        Assert.Equal("п. 8.1.24, (8.37); εb,ult — п. 8.1.30", v.Ref);
    }

    [Fact]
    public void Uls_NotConverged_IgnoresConventionalUtil()
    {
        // Несошедшийся решатель ставит условный Кисп = 2 — в причину он не идёт.
        var v = ShellLayeredBatchVerdicts.Uls(new ShellLayeredCheck.Result(false, 2.0, "НДС", "Нет сходимости", false, 50, 0.1));
        Assert.Equal(BatchRowStatus.NotConverged, v.Status);
        Assert.Null(v.Util);
    }

    [Fact]
    public void Simpl_ShearGovernsOnTie()
        => Assert.Equal("eta_q", ShellSimplBatchHandlerBase.SimplEtaVerdict(1.2, 1.2, false).Code);

    [Fact]
    public void Simpl_NoEta_IsNotPassed()
        => Assert.Equal(BatchRowStatus.NotPassed, ShellSimplBatchHandlerBase.SimplEtaVerdict(null, null, false).Status);

    [Fact]
    public void Simpl_Acrc_FollowsOk()
    {
        var v = ShellSimplBatchHandlerBase.SimplAcrcVerdict(0.25, 0.3, true);
        Assert.Equal(BatchRowStatus.Ok, v.Status);
        Assert.Equal(0.25 / 0.3, v.Util!.Value, 6);
    }
}
