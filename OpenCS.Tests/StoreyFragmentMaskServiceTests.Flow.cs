using CScore.Fem;
using CScore.Fem.Fragment.Masks;
using OpenCS.Services;
using OpenCS.Utilites;
using OpenCS.ViewModels;
using Xunit;

namespace OpenCS.Tests;

/// <summary>Команда «Маски из родителя…» (срез 6 C/D, задача 1): источники и снятие без окон.</summary>
public sealed partial class StoreyFragmentMaskServiceTests
{
    /// <summary>
    /// Источники — только линейные и секущие CSfea родителя со статусом «ok» и результатом; по умолчанию — источник
    /// действующих масок. Снятие пишет маски, итог отчёта первой строкой; отказ прежние маски не трогает.
    /// </summary>
    [Fact]
    public void Flow_Sources_FilterAndDefault_ExtractAndRefusal()
    {
        var p = Create();
        using var db = p.Db;
        var lc = db.GetFemLoadCases(p.Schema.Id);
        Assert.Empty(StoreyFragmentMaskFlow.Sources(db, p.Fragment, out string? none));
        Assert.Equal(Loc.S("StoreyFragmentMasksNoSources"), none);

        var stages = new[] { Single("Пост.", lc[0].Id), Single("Ветер", lc[1].Id) };
        var linear = SaveResult(db, p.Schema, FemCsfeaRunner.LinearAnalysisKind, stages, stages);
        var secant = SaveResult(db, p.Schema, FemCsfeaRunner.AnalysisKind, stages, stages);
        db.SaveFemAnalysis(new FemAnalysis { SchemaId = p.Schema.Id, Tag = "Без результата", Kind = FemCsfeaRunner.LinearAnalysisKind });
        db.SaveFemAnalysis(new FemAnalysis { SchemaId = p.Schema.Id, Tag = "OpenSees", Kind = "linear", Status = "ok", ResultId = linear.ResultId });
        var failed = new FemAnalysis { SchemaId = p.Schema.Id, Tag = "Сбой", Kind = FemCsfeaRunner.LinearAnalysisKind, Status = "error", ResultId = linear.ResultId };
        db.SaveFemAnalysis(failed);

        var sources = StoreyFragmentMaskFlow.Sources(db, p.Fragment, out string? reason);
        Assert.Null(reason);
        Assert.Equal([(linear.Id, FragmentMaskSet.SourceCsfeaLinear), (secant.Id, FragmentMaskSet.SourceCsfeaSecant)],
            sources.Select(s => (s.AnalysisId, s.SourceKind)));
        Assert.Equal(sources[0], StoreyFragmentMaskFlow.DefaultSource(db, p.Fragment, sources));

        var r = StoreyFragmentMaskFlow.Extract(db, p.Fragment, sources[1]);
        Assert.False(r.IsError, string.Join("\n", r.Rows.Select(x => x.Message)));
        Assert.True(r.Rows[0].IsSummary);
        Assert.Equal(Loc.S("StoreyFragmentMasksTaken"), r.LogMessage);
        Assert.Equal(sources[1], StoreyFragmentMaskFlow.DefaultSource(db, p.Fragment, sources));
        int items = db.GetFragmentMasks(p.Fragment.Id)!.Items.Count;

        // Источник пропал между открытием окна и снятием — отказ, маски секущего остаются.
        db.DeleteFemAnalysis(linear);
        var refused = StoreyFragmentMaskFlow.Extract(db, p.Fragment, sources[0]);
        Assert.True(refused.IsError);
        Assert.True(refused.Rows[0].IsError);
        Assert.Equal(Loc.S("StoreyFragmentMasksFailed"), refused.LogMessage);
        Assert.Equal(items, db.GetFragmentMasks(p.Fragment.Id)!.Items.Count);

        Assert.Empty(StoreyFragmentMaskFlow.Sources(db, p.Schema, out string? notFragment));
        Assert.Equal(Loc.S("StoreyFragmentMasksNotFragment"), notFragment);
        db.DeleteFemSchema(p.Schema);
        Assert.Empty(StoreyFragmentMaskFlow.Sources(db, p.Fragment, out string? deleted));
        Assert.Equal(Loc.S("StoreyFragmentMasksParentDeleted"), deleted);
    }
    /// <summary>
    /// Шапка фрагмента — строка масок: не сняты (без предупреждения), действующие, устарели после пересчёта родителя,
    /// источник удалён; отчёт снятия — с итогом первым.
    /// </summary>
    [Fact]
    public async Task Banner_MaskStatus_None_Current_Stale_SourceDeleted()
    {
        var p = Create();
        using var db = p.Db;
        var lc = db.GetFemLoadCases(p.Schema.Id);
        var vm = new StoreyFragmentBannerVM(db, p.Fragment);
        await vm.LoadAsync();
        Assert.Equal(FragmentMaskStatus.None, vm.MaskStatus);
        Assert.False(vm.IsMaskWarning);
        Assert.False(vm.HasMaskReport);
        Assert.Equal(Loc.S("StoreyFragmentMasksBannerNone"), vm.MaskText);

        var stages = new[] { Single("Пост.", lc[0].Id) };
        var analysis = SaveResult(db, p.Schema, FemCsfeaRunner.LinearAnalysisKind, stages, stages);
        var source = StoreyFragmentMaskFlow.Sources(db, p.Fragment, out _).Single();
        Assert.False(StoreyFragmentMaskFlow.Extract(db, p.Fragment, source).IsError);
        vm = new StoreyFragmentBannerVM(db, p.Fragment);
        Assert.Equal(Loc.S("StoreyFragmentMasksChecking"), vm.MaskText);
        await vm.LoadAsync();
        Assert.Equal(FragmentMaskStatus.Current, vm.MaskStatus);
        Assert.False(vm.IsMaskWarning);
        Assert.Equal(Loc.S("StoreyFragmentMasksBannerCurrent"), vm.MaskText);
        Assert.True(vm.HasMaskReport);
        Assert.True(vm.MaskReportRows[0].IsSummary);

        SaveResult(db, p.Schema, FemCsfeaRunner.LinearAnalysisKind, stages, stages, analysis);
        vm = new StoreyFragmentBannerVM(db, p.Fragment);
        await vm.LoadAsync();
        Assert.Equal(FragmentMaskStatus.Stale, vm.MaskStatus);
        Assert.True(vm.IsMaskWarning);
        Assert.Equal(Loc.S("StoreyFragmentMasksBannerStale"), vm.MaskText);

        db.DeleteFemAnalysis(analysis);
        vm = new StoreyFragmentBannerVM(db, p.Fragment);
        await vm.LoadAsync();
        Assert.Equal(FragmentMaskStatus.SourceDeleted, vm.MaskStatus);
        Assert.True(vm.IsMaskWarning);
        Assert.Equal(Loc.S("StoreyFragmentMasksBannerSourceDeleted"), vm.MaskText);
    }
}
