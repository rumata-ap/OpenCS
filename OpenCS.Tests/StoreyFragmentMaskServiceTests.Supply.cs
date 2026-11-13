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

    /// <summary>
    /// Загружение-несовершенство (крен) нагрузок не содержит: стадия из него одного валит линейный расчёт целиком
    /// («Стадия … без загружений», ручная сверка «Музея» 13.11) — в служебную линейную постановку оно не входит.
    /// </summary>
    [Fact]
    public void SupplyAnalysisSkipsImperfectionLoadCases()
    {
        var p = Create();
        foreach (var schemaId in new[] { p.Schema.Id, p.Fragment.Id })
            p.Db.SaveFemLoadCase(new FemLoadCase { SchemaId = schemaId, Tag = "Крен", Sp20Type = FemSp20Types.Imperfection });

        var analysis = Assert.IsType<FemAnalysis>(StoreyFragmentMaskSupply.Prepare(p.Db, p.Fragment, out _));
        var stages = FemAnalysisParams.Parse(analysis.ParamsJson).Stages;
        Assert.Equal(["Пост.", "Ветер"], stages.Select(s => s.Tag));
    }

    /// <summary>
    /// Служебная постановка из БД — другой экземпляр, чем в дереве схемы: сохранение заменяет строку с тем же Id, а не
    /// добавляет вторую («Маски фрагмента» [ok] и [error] рядом в дереве, сверка 13.11).
    /// </summary>
    [Fact]
    public void SupplyAnalysisSavedTwice_SingleTreeEntry()
    {
        var p = Create();
        p.Db.LoadAll();
        var first = Assert.IsType<FemAnalysis>(StoreyFragmentMaskSupply.Prepare(p.Db, p.Fragment, out _));
        var second = Assert.IsType<FemAnalysis>(StoreyFragmentMaskSupply.Prepare(p.Db, p.Fragment, out _));
        Assert.NotSame(first, second);
        var schema = p.Db.FemSchemas.Single(s => s.Id == p.Schema.Id);
        Assert.Same(second, Assert.Single(schema.Analyses, a => a.Tag == StoreyFragmentMaskSupply.AnalysisTag));
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

    /// <summary>
    /// Секущая постановка родителя для масок (спека второго порядка фрагмента, срез 2): геомнелин и физика — как у фрагмента,
    /// но второй порядок полный (не «только P-δ»), флажок масок первого порядка снят, концы стадий записываются (ключ маски —
    /// стадия; при «только конечный» маски были бы лишь у последней стадии).
    /// </summary>
    [Fact]
    public void SecantSupplyAnalysis_FullSecondOrder_RecordsStageEnds()
    {
        var p = Create();
        var fragmentIds = p.Db.GetFemLoadCases(p.Fragment.Id).ToDictionary(c => c.Tag, c => c.Id);
        p.Db.SaveFemAnalysis(new FemAnalysis
        {
            SchemaId = p.Fragment.Id, Tag = "Секущий", Kind = FemCsfeaRunner.AnalysisKind,
            ParamsJson = new FemAnalysisParams
            {
                Stages = [Single("Пост.", fragmentIds["Пост."]), Single("Ветер", fragmentIds["Ветер"])],
                Csfea = new FemCsfeaParams
                {
                    GeomNonlinear = true, PhysicalNonlinearity = false, SecondOrderScope = FemCsfeaSecondOrderScope.LocalOnly,
                    FragmentSecondOrderOnFirstOrderMasks = true, ResultRecording = FemCsfeaRecording.Final,
                },
            }.ToJson(),
        });

        var secant = Assert.IsType<FemAnalysis>(StoreyFragmentMaskSupply.PrepareSecant(p.Db, p.Fragment, out _));
        var c = FemAnalysisParams.Parse(secant.ParamsJson).Csfea!;
        Assert.True(c.GeomNonlinear);
        Assert.False(c.PhysicalNonlinearity);
        Assert.Equal(FemCsfeaSecondOrderScope.Full, c.SecondOrderScope);
        Assert.False(c.FragmentSecondOrderOnFirstOrderMasks);
        Assert.Equal((FemCsfeaRecording.Selected, "", true), (c.ResultRecording, c.RecordSteps, c.RecordStageEnds));
    }
    /// <summary>
    /// Секущие постановки фрагмента с креном и без (сверка GUI 13.11): по всем — отказ с именами постановок, номером стадии и
    /// причиной «крен»; по выбранной — постановка родителя по её стадиям. Выбор по умолчанию — самая длинная постановка.
    /// </summary>
    [Fact]
    public void SecantSupply_ImperfectionAndPlain_RefusesWithNames_PicksChosen()
    {
        var p = Create();
        foreach (var schemaId in new[] { p.Schema.Id, p.Fragment.Id })
            p.Db.SaveFemLoadCase(new FemLoadCase { SchemaId = schemaId, Tag = "Крен", Sp20Type = FemSp20Types.Imperfection });
        var f = p.Db.GetFemLoadCases(p.Fragment.Id).ToDictionary(c => c.Tag, c => c.Id);
        FemAnalysisStage SumStage(string tag, params int[] ids) => new()
        {
            Tag = tag, LoadFactorStep = 0.1, MaxLoadFactor = 1.0,
            LoadExpressionJson = new FemLoadExpression { Mode = FemLoadExpressionMode.Sum, LoadCaseIds = [.. ids] }.ToJson(),
        };
        var plain = new FemAnalysis
        {
            SchemaId = p.Fragment.Id, Tag = "Без крена", Kind = FemCsfeaRunner.AnalysisKind,
            ParamsJson = new FemAnalysisParams { Stages = [Single("Пост.", f["Пост."])] }.ToJson(),
        };
        var sway = new FemAnalysis
        {
            SchemaId = p.Fragment.Id, Tag = "С креном", Kind = FemCsfeaRunner.AnalysisKind,
            ParamsJson = new FemAnalysisParams { Stages = [SumStage("Пост. + крен", f["Пост."], f["Крен"]), Single("Ветер", f["Ветер"])] }.ToJson(),
        };
        p.Db.SaveFemAnalysis(plain);
        p.Db.SaveFemAnalysis(sway);

        Assert.Null(StoreyFragmentMaskSupply.PrepareSecant(p.Db, p.Fragment, out var reason));
        Assert.Contains("«С креном»", reason);
        Assert.Contains("«Без крена»", reason);
        Assert.Contains("стадии 1", reason);
        Assert.Contains("с креном", reason);

        var chosen = Assert.IsType<FemAnalysis>(StoreyFragmentMaskSupply.PrepareSecant(p.Db, p.Fragment, out var none, sway.Id));
        Assert.Null(none);
        Assert.Equal(["Пост. + крен", "Ветер"], FemAnalysisParams.Parse(chosen.ParamsJson).Stages.Select(s => s.Tag));

        var choices = OpenCS.ViewModels.StoreyFragmentMaskFlow.SecantChoices(p.Db, p.Fragment, out var selected);
        Assert.Equal(3, choices.Count);
        Assert.Null(choices[0].AnalysisId);
        Assert.Equal(sway.Id, selected?.AnalysisId);
    }

    /// <summary>Постановки, продолжающие одна другую: по умолчанию «Все секущие постановки».</summary>
    [Fact]
    public void SecantChoices_Prefixes_DefaultAll()
    {
        var p = Create();
        var f = p.Db.GetFemLoadCases(p.Fragment.Id).ToDictionary(c => c.Tag, c => c.Id);
        foreach (var (tag, stages) in new[] { ("Короткий", new[] { Single("Пост.", f["Пост."]) }),
                     ("Длинный", new[] { Single("Пост.", f["Пост."]), Single("Ветер", f["Ветер"]) }) })
            p.Db.SaveFemAnalysis(new FemAnalysis
            {
                SchemaId = p.Fragment.Id, Tag = tag, Kind = FemCsfeaRunner.AnalysisKind,
                ParamsJson = new FemAnalysisParams { Stages = [.. stages] }.ToJson(),
            });
        var choices = OpenCS.ViewModels.StoreyFragmentMaskFlow.SecantChoices(p.Db, p.Fragment, out var selected);
        Assert.Equal(3, choices.Count);
        Assert.Same(choices[0], selected);
    }
}
