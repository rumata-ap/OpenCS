using System.Text.Json;
using CScore.Batch;
using Xunit;

namespace CScore.Tests.Batch;

/// <summary>Восстановление причины для результатов пакетов, посчитанных до появления verdict.</summary>
public sealed class BatchRowVerdictReaderTests
{
    static BatchRowVerdict R(string kind, string json)
    {
        using var doc = JsonDocument.Parse(json);
        return BatchRowVerdictReader.Read(kind, doc.RootElement.Clone());
    }

    [Fact]
    public void VerdictFieldWins()
        => Assert.Equal("x", R("strength_ndm_batch",
            "{\"status\":\"ok\",\"verdict\":{\"status\":\"not_passed\",\"util\":2,\"code\":\"x\",\"ref\":\"\",\"text\":\"\"}}").Code);

    [Fact]
    public void Error_KeepsMessage()
    {
        var v = R("shear_inclined_batch", "{\"status\":\"error\",\"error\":\"нет сечения\"}");
        Assert.Equal(BatchRowStatus.Error, v.Status);
        Assert.Equal("нет сечения", v.Text);
    }

    [Fact]
    public void Ndm_ConcreteGoverns()
    {
        var v = R("strength_ndm_batch", "{\"status\":\"ok\",\"eps_concrete_compression\":-0.0041,\"eps_concrete_ult\":-0.0035,\"eps_rebar_tension\":0.001,\"eps_rebar_ult\":0.025}");
        Assert.Equal(BatchRowStatus.NotPassed, v.Status);
        Assert.Equal("eps_b", v.Code);
        Assert.Equal(0.0041 / 0.0035, v.Util!.Value, 6);
        Assert.Contains("0,0041", v.Text);
        Assert.Contains(">", v.Text);
    }

    [Fact]
    public void Ndm_NotConverged_IsNotFailure()
        => Assert.Equal(BatchRowStatus.NotConverged, R("strength_ndm_batch", "{\"status\":\"not_converged\"}").Status);

    [Fact]
    public void StrainState_NotApplicableKeepsReasonCode()
    {
        var v = R("strain_state_batch", "{\"status\":\"not_applicable\",\"reason_code\":\"no_rebar\"}");
        Assert.Equal(BatchRowStatus.NotApplicable, v.Status);
        Assert.Equal("no_rebar", v.Code);
    }

    [Fact]
    public void Limit_NullUtilIsInfinity()
    {
        var v = R("limit_force_batch", "{\"status\":\"ok\",\"governing\":\"rebar\",\"utilization\":null}");
        Assert.True(double.IsPositiveInfinity(v.Util!.Value));
        Assert.Equal("limit_rebar", v.Code);
        Assert.Equal(BatchRowStatus.NotPassed, v.Status);
    }

    [Fact]
    public void Limit_UnderOne_Ok()
        => Assert.Equal(BatchRowStatus.Ok, R("limit_moment_batch", "{\"status\":\"ok\",\"governing\":\"concrete\",\"utilization\":0.8}").Status);

    [Fact]
    public void CrackWidth_FailedLongWithoutUtil_IsNotPassed()
    {
        var v = R("crack_width_batch",
            "{\"status\":\"not_passed\",\"passed_long\":false,\"passed_short\":true,\"passed_steel\":true,\"util_steel\":0.5,\"acrc_long\":0.41}");
        Assert.Equal(BatchRowStatus.NotPassed, v.Status);
        Assert.Equal("acrc_long", v.Code);
    }

    [Fact]
    public void CrackWidth_SteelGoverns()
    {
        var v = R("crack_width_batch",
            "{\"passed_long\":true,\"passed_short\":true,\"passed_steel\":false,\"util_steel\":1.1}");
        Assert.Equal("sigma_s", v.Code);
        Assert.Equal(BatchRowStatus.NotPassed, v.Status);
    }

    [Fact]
    public void ShellSls_StatusAndAcrcText()
    {
        var v = R("shell_layered_sls_batch", "{\"status\":\"not_passed\",\"acrc_mm\":0.41}");
        Assert.Equal(BatchRowStatus.NotPassed, v.Status);
        Assert.Equal("acrc", v.Code);
        Assert.Contains("0,410", v.Text);
    }

    [Fact]
    public void ShellSls_NotConverged()
        => Assert.Equal(BatchRowStatus.NotConverged, R("shell_layered_sls_batch", "{\"status\":\"not_converged\"}").Status);

    [Fact]
    public void ShellUls_OldFailStatus()
    {
        var v = R("shell_layered_uls_batch", "{\"status\":\"fail\",\"formula\":\"(8.1.30)\",\"note\":\"εb > εb,ult\"}");
        Assert.Equal(BatchRowStatus.NotPassed, v.Status);
        Assert.Equal("(8.1.30)", v.Ref);
        Assert.Equal("εb > εb,ult", v.Text);
    }

    [Fact]
    public void ShellSimpl_EtaGoverns()
        => Assert.Equal("eta_q", R("shell_simpl_wa_uls_batch", "{\"status\":\"fail\",\"eta_max\":0.8,\"eta_q\":1.2}").Code);

    [Fact]
    public void ShellSimpl_FailWithoutEtaAboveOne_IsNotPassed()
        => Assert.Equal(BatchRowStatus.NotPassed, R("shell_simpl_capri_sls_batch", "{\"status\":\"fail\",\"eta_max\":0.8}").Status);

    [Fact]
    public void Shear_Failed()
    {
        var v = R("shear_inclined_batch", "{\"status\":\"failed\",\"utilization\":1.4,\"worstFormula\":\"(8.55)\"}");
        Assert.Equal(BatchRowStatus.NotPassed, v.Status);
        Assert.Equal("(8.55)", v.Ref);
    }

    [Fact]
    public void Shear_NoCapacity_IsInfinity()
        => Assert.True(double.IsPositiveInfinity(R("shear_inclined_batch", "{\"status\":\"failed\",\"utilization\":null,\"worstFormula\":\"\"}").Util!.Value));

    [Fact]
    public void Fire_FactorInverted()
    {
        var v = R("fire_r_check_batch", "{\"passed\":true,\"factor\":1.25,\"governing\":\"concrete\"}");
        Assert.Equal(0.8, v.Util!.Value, 6);
        Assert.Equal("fire_concrete", v.Code);
    }

    [Fact]
    public void Fire_ZeroFactor_Infinity()
        => Assert.True(double.IsPositiveInfinity(R("fire_r_check_batch", "{\"passed\":false,\"factor\":0}").Util!.Value));

    [Fact]
    public void Cracking_ConvergedIsOk()
        => Assert.Equal(BatchRowStatus.Ok, R("cracking_batch", "{\"converged\":true}").Status);

    [Fact]
    public void Curvature_NotConverged()
        => Assert.Equal(BatchRowStatus.NotConverged, R("total_curvature_batch", "{\"converged\":false}").Status);

    [Fact]
    public void CapriDesign_Infeasible_TakesDiagnostics()
    {
        var v = R("shell_simpl_capri_design_uls_batch", "{\"Num\":3,\"Feasible\":false,\"Diagnostics\":\"(8.55): Кисп,Q = 1,20 > 1\"}");
        Assert.Equal(BatchRowStatus.NotPassed, v.Status);
        Assert.Equal("design_infeasible", v.Code);
        Assert.Equal("(8.55): Кисп,Q = 1,20 > 1", v.Text);
    }

    [Fact]
    public void CapriDesign_Feasible_Ok()
        => Assert.Equal(BatchRowStatus.Ok, R("shell_simpl_capri_design_sls_batch", "{\"Feasible\":true,\"Diagnostics\":\"\"}").Status);

    [Fact]
    public void ShellStrain_Converged_Ok()
        => Assert.Equal(BatchRowStatus.Ok, R("shell_strain_state_batch", "{\"status\":\"ok\",\"converged\":true}").Status);
}
