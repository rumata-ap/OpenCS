using CScore;
using OpenCS.Tasks;
using Xunit;

namespace OpenCS.Tests;

/// <summary>Выбор строк пакета для задач: определяющая и все не прошедшие (с не сошедшимися).</summary>
public sealed class BatchRowTaskFactoryVerdictTests
{
    const string NewRows = """
        {"rows":[
          {"num":10,"verdict":{"status":"ok","util":0.5,"util_inf":false,"code":"x","ref":"","text":""}},
          {"num":20,"verdict":{"status":"not_passed","util":1.3,"util_inf":false,"code":"x","ref":"","text":""}},
          {"num":30,"verdict":{"status":"not_converged","util":null,"util_inf":false,"code":"","ref":"","text":""}}]}
        """;

    static CalcResult R(string kind, string json) => new() { TaskKind = kind, DataJson = json };

    [Fact]
    public void Governing_IsMaxUtil()
        => Assert.Equal([20], BatchRowTaskFactory.SelectRows(R("strength_ndm_batch", NewRows), BatchRowSelection.Governing));

    [Fact]
    public void Failed_IncludesNotConverged()
        => Assert.Equal([20, 30], BatchRowTaskFactory.SelectRows(R("strength_ndm_batch", NewRows), BatchRowSelection.Failed));

    [Fact]
    public void OldResultWithoutVerdict_SameAnswer()
    {
        const string old = """
            {"rows":[
              {"num":1,"status":"ok","eps_concrete_compression":-0.002,"eps_concrete_ult":-0.0035,"eps_rebar_tension":0.001,"eps_rebar_ult":0.025},
              {"num":2,"status":"ok","eps_concrete_compression":-0.004,"eps_concrete_ult":-0.0035,"eps_rebar_tension":0.001,"eps_rebar_ult":0.025},
              {"num":3,"status":"not_converged"}]}
            """;
        Assert.Equal([2], BatchRowTaskFactory.SelectRows(R("strength_ndm_batch", old), BatchRowSelection.Governing));
        Assert.Equal([2, 3], BatchRowTaskFactory.SelectRows(R("strength_ndm_batch", old), BatchRowSelection.Failed));
    }

    [Fact]
    public void CapriDesign_PascalCaseRows()
    {
        const string capri = """{"Rows":[{"Num":4,"Feasible":true,"Diagnostics":""},{"Num":5,"Feasible":false,"Diagnostics":"нет"}]}""";
        Assert.Equal([5], BatchRowTaskFactory.SelectRows(R("shell_simpl_capri_design_uls_batch", capri), BatchRowSelection.Failed));
    }

    [Fact]
    public void ErrorResult_Empty()
        => Assert.Empty(BatchRowTaskFactory.SelectRows(R("strength_ndm_batch", """{"error":"x"}"""), BatchRowSelection.Failed));

    [Fact]
    public void NoUtil_GoverningEmpty()
        => Assert.Empty(BatchRowTaskFactory.SelectRows(R("cracking_batch", """{"rows":[{"num":1,"converged":true}]}"""), BatchRowSelection.Governing));
}
