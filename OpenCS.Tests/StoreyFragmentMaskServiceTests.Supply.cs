using CScore.Fem;
using OpenCS.Services;
using OpenCS.Tasks;
using Xunit;

namespace OpenCS.Tests;

/// <summary>
/// Служебная постановка «Маски фрагмента» (кнопка «Маски для всех загружений»): одна линейная стадия на каждое общее
/// загружение родителя, повторный вызов — та же постановка (без дублей).
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
        Assert.Single(p.Db.GetFemAnalyses(p.Schema.Id).Where(a => a.Tag == StoreyFragmentMaskSupply.AnalysisTag));
    }
}
