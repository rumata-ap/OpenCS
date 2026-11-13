using CScore;
using CScore.Batch;
using OpenCS.Tasks;
using Xunit;

namespace OpenCS.Tests;

/// <summary>Причина строки в стержневых пакетах: чистые функции обработчиков, без сечений.</summary>
public sealed class BatchRowVerdictHandlersTests
{
    static StrengthNDMBatchHandler.StrengthNdmItemResult Ndm(bool conv, double eb, double ebu, double es, double esu)
        => new(new Kurvature(), conv, 5, 1e-9, eb, ebu, es, esu, Math.Abs(eb) <= Math.Abs(ebu), es <= esu);

    [Fact]
    public void Ndm_RebarGoverns()
    {
        var v = StrengthNDMBatchHandler.RowVerdict(Ndm(true, -0.002, -0.0035, 0.03, 0.025));
        Assert.Equal(BatchRowStatus.NotPassed, v.Status);
        Assert.Equal("eps_s", v.Code);
        Assert.Equal(1.2, v.Util!.Value, 6);
        Assert.Equal("п. 8.1.24, (8.38); εs,ult — п. 8.1.30", v.Ref);
    }

    [Fact]
    public void Ndm_Passes()
        => Assert.Equal(BatchRowStatus.Ok, StrengthNDMBatchHandler.RowVerdict(Ndm(true, -0.003, -0.0035, 0.01, 0.025)).Status);

    [Fact]
    public void Ndm_NotConverged()
        => Assert.Equal(BatchRowStatus.NotConverged, StrengthNDMBatchHandler.RowVerdict(Ndm(false, 0, 0, 0, 0)).Status);

    [Fact]
    public void CrackWidth_LongGoverns()
    {
        var v = BatchRowVerdictReader.CrackWidthVerdict(new CrackWidthResult
        { AcrcLong = 0.36, AcrcUltLong = 0.3, AcrcShort = 0.3, AcrcUltShort = 0.4, UtilSteel = 0.7 });
        Assert.Equal(BatchRowStatus.NotPassed, v.Status);
        Assert.Equal("acrc_long", v.Code);
        Assert.Equal(1.2, v.Util!.Value, 6);
        Assert.Contains("0,360", v.Text);
    }

    [Fact]
    public void CrackWidth_SteelGoverns()
        => Assert.Equal("sigma_s", BatchRowVerdictReader.CrackWidthVerdict(new CrackWidthResult
        { AcrcLong = 0.1, AcrcUltLong = 0.3, AcrcShort = 0.2, AcrcUltShort = 0.4, UtilSteel = 1.05 }).Code);

    [Fact]
    public void CrackWidth_TotalNotConverged_IsNotConverged()
        => Assert.Equal(BatchRowStatus.NotConverged, BatchRowVerdictReader.CrackWidthVerdict(new CrackWidthResult
        { AcrcLong = 0.1, AcrcUltLong = 0.3, AcrcUltShort = 0.4, TotalConverged = false }).Status);

    [Fact]
    public void CrackWidth_TotalNotConvergedButLongFails_IsNotPassed()
        => Assert.Equal(BatchRowStatus.NotPassed, BatchRowVerdictReader.CrackWidthVerdict(new CrackWidthResult
        { AcrcLong = 0.5, AcrcUltLong = 0.3, AcrcUltShort = 0.4, TotalConverged = false }).Status);

    [Fact]
    public void CrackWidth_NotCracked_Ok()
        => Assert.Equal(BatchRowStatus.Ok, BatchRowVerdictReader.CrackWidthVerdict(new CrackWidthResult
        { Cracked = false, AcrcUltLong = 0.3, AcrcUltShort = 0.4 }).Status);

    [Fact]
    public void Limit_InfiniteUtilization_Fails()
    {
        var v = LimitForceTaskHelper.RowVerdict(new LimitForceResult
        { Converged = true, Utilization = double.PositiveInfinity, Governing = "concrete" });
        Assert.Equal(BatchRowStatus.NotPassed, v.Status);
        Assert.Equal("limit_concrete", v.Code);
        Assert.Contains("бетон", v.Text);
    }

    [Fact]
    public void Limit_UnderOne_Ok()
        => Assert.Equal(BatchRowStatus.Ok, LimitForceTaskHelper.RowVerdict(new LimitForceResult
        { Converged = true, Utilization = 0.7, Governing = "rebar" }).Status);

    [Fact]
    public void Shear_NullUtil_IsInfiniteFail()
    {
        var v = ShearInclinedBatchHandler.RowVerdict("ok", null, "(8.55)", "Qb + Qsw", "");
        Assert.Equal(BatchRowStatus.NotPassed, v.Status);
        Assert.True(double.IsPositiveInfinity(v.Util!.Value));
        Assert.Equal("(8.55)", v.Ref);
        Assert.Contains("Qb + Qsw", v.Text);
    }

    [Fact]
    public void Shear_NotApplicable_And_Error()
    {
        Assert.Equal(BatchRowStatus.NotApplicable, ShearInclinedBatchHandler.RowVerdict("not_applicable", null, "", "", "").Status);
        var e = ShearInclinedBatchHandler.RowVerdict("error", null, "", "", "нет хомутов");
        Assert.Equal(BatchRowStatus.Error, e.Status);
        Assert.Equal("нет хомутов", e.Text);
    }

    [Fact]
    public void Fire_FactorBelowOne_Fails()
    {
        var check = new CScore.Fire.FireCheckResult
        {
            Passed = false, Margin = -0.2,
            Details = new() { ["governing"] = "concrete" },
        };
        var v = FireRCheckBatchHandler.RowVerdict(check);
        Assert.Equal(BatchRowStatus.NotPassed, v.Status);
        Assert.Equal(1.25, v.Util!.Value, 6);
        Assert.Equal("fire_concrete", v.Code);
    }

    [Fact]
    public void Fire_ExactlyOne_Passes()
        => Assert.Equal(BatchRowStatus.Ok, FireRCheckBatchHandler.RowVerdict(new CScore.Fire.FireCheckResult { Passed = true, Margin = 0 }).Status);

    [Fact]
    public void Limit_NotConverged()
        => Assert.Equal(BatchRowStatus.NotConverged, LimitForceTaskHelper.RowVerdict(new LimitForceResult { Converged = false }).Status);
}
