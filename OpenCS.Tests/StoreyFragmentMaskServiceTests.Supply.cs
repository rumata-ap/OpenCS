using CScore.Fem;
using OpenCS.Services;
using OpenCS.Tasks;
using OpenCS.Utilites;
using Xunit;

namespace OpenCS.Tests;

/// <summary>
/// Служебные постановки «Маски фрагмента» (кнопки «Маски для всех загружений» и «Маски для секущего расчёта»): линейная —
/// одна стадия на каждое общее загружение; секущая — стадии секущего расчёта фрагмента, переведённые на загружения
/// родителя. Повторный вызов — та же постановка (без дублей).
/// </summary>
public sealed partial class StoreyFragmentMaskServiceTests
{
    [Fact]
    public void SupplyAnalysisHasOneStagePerCommonLoadCaseAndIsReused()
    {
        var p = Create();
        var first = Assert.IsType<FemAnalysis>(StoreyFragmentMaskSupply.Prepare(p.Db, p.Fragment, out var reason));
        Assert.Null(reason);
        Assert.Equal(StoreyFragmentMaskSupply.AnalysisTag, first.Tag);
        Assert.Equal(FemCsfeaRunner.LinearAnalysisKind, first.Kind);
        Assert.Equal(p.Schema.Id, first.SchemaId);

        var stages = FemAnalysisParams.Parse(first.ParamsJson).Stages;
        var parentIds = p.Db.GetFemLoadCases(p.Schema.Id).ToDictionary(c => c.Tag, c => c.Id);
        Assert.Equal(2, stages.Count);
        Assert.Equal(parentIds["Пост."], stages[0].LoadCaseId());
        Assert.Equal(parentIds["Ветер"], stages[1].LoadCaseId());
        Assert.All(stages, s => Assert.Equal(1.0, s.LoadFactorStep));

        var second = Assert.IsType<FemAnalysis>(StoreyFragmentMaskSupply.Prepare(p.Db, p.Fragment, out _));
        Assert.Equal(first.Id, second.Id);
        Assert.Single(p.Db.GetFemAnalyses(p.Schema.Id), a => a.Tag == StoreyFragmentMaskSupply.AnalysisTag);
    }

    [Fact]
    public void SecantSupplyAnalysisMirrorsFragmentStagesOnParentLoadCases()
    {
        var p = Create();
        // Без секущего расчёта фрагмента постановку не создать.
        Assert.Null(StoreyFragmentMaskSupply.PrepareSecant(p.Db, p.Fragment, out var none));
        Assert.Equal(Loc.S("StoreyFragmentMasksNoFragmentSecant"), none);

        var fragmentIds = p.Db.GetFemLoadCases(p.Fragment.Id).ToDictionary(c => c.Tag, c => c.Id);
        p.Db.SaveFemAnalysis(new FemAnalysis
        {
            SchemaId = p.Fragment.Id, Tag = "Секущий", Kind = FemCsfeaRunner.AnalysisKind,
            ParamsJson = new FemAnalysisParams { Stages = [Single("Пост.", fragmentIds["Пост."]), Single("Ветер", fragmentIds["Ветер"])] }.ToJson(),
        });

        var secant = Assert.IsType<FemAnalysis>(StoreyFragmentMaskSupply.PrepareSecant(p.Db, p.Fragment, out var reason));
        Assert.Null(reason);
        Assert.Equal(StoreyFragmentMaskSupply.SecantAnalysisTag, secant.Tag);
        Assert.Equal(FemCsfeaRunner.AnalysisKind, secant.Kind);

        var parentIds = p.Db.GetFemLoadCases(p.Schema.Id).ToDictionary(c => c.Tag, c => c.Id);
        var stages = FemAnalysisParams.Parse(secant.ParamsJson).Stages;
        Assert.Equal(2, stages.Count);
        Assert.Equal(parentIds["Пост."], stages[0].LoadCaseId());
        Assert.Equal(parentIds["Ветер"], stages[1].LoadCaseId());

        var again = Assert.IsType<FemAnalysis>(StoreyFragmentMaskSupply.PrepareSecant(p.Db, p.Fragment, out _));
        Assert.Equal(secant.Id, again.Id);
    }
}
