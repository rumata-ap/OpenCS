using CScore;
using CScore.Combinations;
using CScore.Fem;
using CSfea.CScoreBridge.Structural;
using OpenCS.Services;
using OpenCS.Tasks;
using Xunit;

namespace OpenCS.Tests;

/// <summary>Наборы усилий по загружениям линейного расчёта и комбинации из огибающей (срез 5а).</summary>
public sealed class FemLinearLoadCaseForceSetsTests
{
    // Mx двух пластин по загружениям, кН·м/м: G (10, −2), Q1 (5, 4), Q2 (−3, 6).
    static readonly double[][] Mx = [[10, -2], [5, 4], [-3, 6]];

    static (FemSchema Schema, FemAnalysis Analysis, FemCsfeaResultSummary Summary) Model()
    {
        var schema = new FemSchema { Id = 1, Tag = "Схема" };
        schema.LoadCases.Add(new FemLoadCase { Id = 11, SchemaId = 1, Tag = "G", Sp20Type = "permanent" });
        schema.LoadCases.Add(new FemLoadCase { Id = 12, SchemaId = 1, Tag = "Q1", Sp20Type = "short_term", Sp20Group = "полезная" });
        schema.LoadCases.Add(new FemLoadCase { Id = 13, SchemaId = 1, Tag = "Q2", Sp20Type = "short_term", GammaFUnfav = 1.3 });
        var stages = schema.LoadCases.Select(lc => new FemAnalysisStage
        {
            Tag = lc.Tag,
            LoadExpressionJson = new FemLoadExpression { Mode = FemLoadExpressionMode.Single, LoadCaseIds = [lc.Id] }.ToJson(),
        }).ToList();
        // Строка-комбинация — не загружение, пропускается.
        stages.Add(new FemAnalysisStage
        {
            Tag = "Комб",
            LoadExpressionJson = new FemLoadExpression
            {
                Mode = FemLoadExpressionMode.Sum,
                Terms = [new FemLoadTerm { LoadCaseId = 11, Coefficient = 1.1 }, new FemLoadTerm { LoadCaseId = 12, Coefficient = 1.4 }],
            }.ToJson(),
        });
        var analysis = new FemAnalysis
        {
            Id = 5, SchemaId = 1, Tag = "Лин", Kind = FemCsfeaRunner.LinearAnalysisKind,
            ParamsJson = new FemAnalysisParams { Stages = stages }.ToJson(),
        };
        var summary = new FemCsfeaResultSummary
        {
            Kind = FemCsfeaRunner.LinearAnalysisKind,
            Stages = stages.Select(s => new FemCsfeaStageSummary(s.Tag, 1, 0, 0, 0)).ToList(),
            Steps = stages.Select((_, i) => new FemCsfeaStepSummary(i + 1, i, 1, 1, false, true, 1, 0, null, Fields: true)).ToList(),
        };
        return (schema, analysis, summary);
    }

    static RcSecantStepFields? Fields(int stepN)
    {
        if (stepN > Mx.Length) return null;
        var forces = new double[2 * RcSecantStepFields.ShellForceComponents];
        for (int e = 0; e < 2; e++) forces[e * RcSecantStepFields.ShellForceComponents + 3] = Mx[stepN - 1][e] * 1e3;
        return new RcSecantStepFields
        {
            NodeIds = [], Displacements = [], ShellIds = [101, 102], ShellForces = forces,
            ShellStates = new double[2 * RcSecantStepFields.ShellStateComponents], ShellFlags = new byte[2],
            BeamIds = [], BeamForces = [], BeamFlags = [],
        };
    }

    [Fact]
    public void Build_SetPerLoadCaseWithSp20Name_SkipsCombinationRows()
    {
        var (schema, analysis, summary) = Model();
        var result = FemLinearLoadCaseForceSets.Build(schema, analysis, summary, Fields, [], Sp20GammaDefaults.Standard);

        Assert.Equal([11, 12, 13], result.Sets.Select(s => s.LoadCase.Id));
        Assert.Single(result.Warnings, w => w.Contains("Комб"));
        Assert.Equal("Q(γf=1.4): Q1 — Лин — пластины [полезная]", result.Sets[1].Set.Tag);
        var (type, title, group, gamma, warn) = SP20Combinations.ParseForceSetName(result.Sets[2].Set.Tag);
        Assert.Equal(NormLoadType.ShortTerm, type);
        Assert.Equal("Q2 — Лин — пластины", title);
        Assert.Null(group);
        Assert.Equal(1.3, gamma);
        Assert.Null(warn);
        Assert.Equal(SP20Combinations.ParseForceSetName(result.Sets[0].Set.Tag).Type, NormLoadType.Permanent);
        Assert.Equal(-2, result.Sets[0].Set.ShellItems[1].Mx, 9);
        Assert.Equal(3, result.Sets.Select(s => s.Set.Num).Distinct().Count());
    }

    [Fact]
    public void EnvelopeDefinitions_FormulasWithRoles()
    {
        var (schema, analysis, summary) = Model();
        var result = FemLinearLoadCaseForceSets.Build(schema, analysis, summary, Fields, [], Sp20GammaDefaults.Standard);
        var warnings = new List<string>();
        var definitions = FemLinearLoadCaseForceSets.EnvelopeDefinitions(schema, result, Sp20GammaDefaults.Standard,
            Sp20LoadValueKind.Normative, warnings);

        Assert.NotEmpty(definitions);
        Assert.All(definitions, d =>
        {
            Assert.Equal("C", d.CombinationType);
            Assert.Equal("envelope", d.SourceKind);
            Assert.All(d.GetExpression().Terms, t => Assert.True(t.IsAuto));
        });
        // max Mx пластины 102: G разгружает (п. 7.3, 0,9), Q2 — основная (1,3), Q1 — вторая (1,4·0,9).
        Assert.Contains(definitions, d => Formula(d) == "11:0.9:permanent_reduced|12:1.26:second|13:1.3:leading");
        // max Mx пластины 101: G 1,1, Q1 — основная.
        Assert.Contains(definitions, d => Formula(d) == "11:1.1:permanent|12:1.4:leading");
        Assert.Equal(definitions.Count, definitions.Select(Formula).Distinct().Count());

        var design = FemLinearLoadCaseForceSets.EnvelopeDefinitions(schema, result, Sp20GammaDefaults.Standard,
            Sp20LoadValueKind.Design, warnings);
        Assert.Contains(design, d => Formula(d) == "11:1:permanent|12:1:leading");
    }

    [Fact]
    public void SelectEnvelopeDefinitions_CoverWithinTolerance_AndLimit()
    {
        var (schema, analysis, summary) = Model();
        var result = FemLinearLoadCaseForceSets.Build(schema, analysis, summary, Fields, [], Sp20GammaDefaults.Standard);
        var warnings = new List<string>();
        var all = FemLinearLoadCaseForceSets.EnvelopeDefinitions(schema, result, Sp20GammaDefaults.Standard,
            Sp20LoadValueKind.Normative, warnings);

        // Допуск 0: огибающая воспроизводится вся, формулы — из числа уникальных, номера — по порядку отбора.
        var (exact, reports) = FemLinearLoadCaseForceSets.SelectEnvelopeDefinitions(schema, result, Sp20GammaDefaults.Standard,
            Sp20LoadValueKind.Normative, warnings, new CScore.Fem.Combinations.FemEnvelopeSelectionOptions(0), null);
        var report = Assert.Single(reports); // особых нет — только C
        Assert.Equal("C", report.Group);
        Assert.Equal(all.Count, report.Candidates);
        Assert.Equal(report.Triples, report.Covered);
        Assert.InRange(exact.Count, 1, all.Count);
        Assert.All(exact, d => Assert.Contains(all, a => Formula(a) == Formula(d)));
        Assert.Equal("Огибающая C 001", exact[0].Tag);

        // Лимит 1: одна формула — та, что покрывает больше всего троек.
        var (one, partial) = FemLinearLoadCaseForceSets.SelectEnvelopeDefinitions(schema, result, Sp20GammaDefaults.Standard,
            Sp20LoadValueKind.Normative, warnings, new CScore.Fem.Combinations.FemEnvelopeSelectionOptions(0, 1), null);
        Assert.Single(one);
        Assert.Equal(Formula(exact[0]), Formula(one[0]));
        Assert.True(partial[0].Covered < partial[0].Triples);

        // Больший допуск — не больше формул.
        var (loose, _) = FemLinearLoadCaseForceSets.SelectEnvelopeDefinitions(schema, result, Sp20GammaDefaults.Standard,
            Sp20LoadValueKind.Normative, warnings, new CScore.Fem.Combinations.FemEnvelopeSelectionOptions(0.5), null);
        Assert.True(loose.Count <= exact.Count);
    }

    [Fact]
    public void SelectEnvelopeDefinitions_Groups_LongTermShareAndNoReducedWeightInN()
    {
        var (schema, analysis, summary) = Model();
        var result = FemLinearLoadCaseForceSets.Build(schema, analysis, summary, Fields, [], Sp20GammaDefaults.Standard);
        // Доля длительной части — только у Q1 (правка редактора): в CL/NL Q2 не входит.
        var edited = schema.LoadCases.Select(lc => new FemLoadCase
        {
            Id = lc.Id, SchemaId = lc.SchemaId, Tag = lc.Tag, Sp20Type = lc.Sp20Type, Sp20Group = lc.Sp20Group,
            GammaFUnfav = lc.GammaFUnfav, LongTermShare = lc.Id == 12 ? 0.35 : null,
        }).ToList();
        var warnings = new List<string>();
        var (definitions, reports) = FemLinearLoadCaseForceSets.SelectEnvelopeDefinitions(schema, result,
            Sp20GammaDefaults.Standard, Sp20LoadValueKind.Normative, warnings,
            new CScore.Fem.Combinations.FemEnvelopeSelectionOptions(0) { Groups = ["NL", "C", "CL", "N", "A"] }, null, edited);

        Assert.Equal(["C", "CL", "N", "NL"], reports.Select(r => r.Group)); // A без особых загружений не строится
        Assert.All(reports, r => Assert.Equal(r.Triples, r.Covered));
        Assert.Contains(warnings, w => w.Contains("доля длительной части"));
        var byType = definitions.GroupBy(d => d.CombinationType).ToDictionary(g => g.Key, g => g.ToList());
        Assert.Equal("Огибающая CL 001", byType["CL"][0].Tag);
        // CL: max Mx пластины 102 — G при уменьшении веса 0,9 и Q1 1,4·0,35 = 0,49; Q2 без доли не входит.
        Assert.Contains(byType["CL"], d => Formula(d) == "11:0.9:permanent_reduced|12:0.49:leading");
        Assert.All(byType["CL"].Concat(byType["NL"]), d => Assert.DoesNotContain(d.GetExpression().Terms, t => t.LoadCaseId == 13));
        // N: нормативные ×1, снижения веса нет; NL: Q1 долей 0,35.
        Assert.All(byType["N"], d => Assert.DoesNotContain(d.GetExpression().Terms, t => t.Role == "permanent_reduced"));
        Assert.Contains(byType["N"], d => Formula(d) == "11:1:permanent|12:0.9:second|13:1:leading");
        Assert.Contains(byType["NL"], d => Formula(d) == "11:1:permanent|12:0.35:leading");
    }

    static string Formula(FemLoadDefinition d) => string.Join("|", d.GetExpression().Terms.Select(t =>
        $"{t.LoadCaseId}:{Math.Round(t.Coefficient, 6).ToString(System.Globalization.CultureInfo.InvariantCulture)}:{t.Role}"));

    [Fact]
    public void Build_TakenTag_NumberedBeforeGroup_EditorLoadCasesWin()
    {
        var (schema, analysis, summary) = Model();
        var first = FemLinearLoadCaseForceSets.Build(schema, analysis, summary, Fields, [], Sp20GammaDefaults.Standard);
        // Повторный выпуск: занятые имена получают номер, разбор имени СП 20 не ломается.
        var second = FemLinearLoadCaseForceSets.Build(schema, analysis, summary, Fields, first.Sets.Select(s => s.Set).ToList(),
            Sp20GammaDefaults.Standard);
        Assert.Equal("Q(γf=1.4): Q1 — Лин — пластины (2) [полезная]", second.Sets[1].Set.Tag);
        Assert.Equal("полезная", SP20Combinations.ParseForceSetName(second.Sets[1].Set.Tag).Group);

        // Атрибуты — из переданных загружений (несохранённая правка в редакторе).
        var edited = schema.LoadCases.Select(lc => new FemLoadCase
        {
            Id = lc.Id, Tag = lc.Tag, Sp20Type = lc.Sp20Type, Sp20Group = lc.Sp20Group, GammaFUnfav = lc.Id == 12 ? 1.2 : lc.GammaFUnfav,
        }).ToList();
        var third = FemLinearLoadCaseForceSets.Build(schema, analysis, summary, Fields, [], Sp20GammaDefaults.Standard, edited);
        Assert.Equal("Q(γf=1.2): Q1 — Лин — пластины [полезная]", third.Sets[1].Set.Tag);
    }

    [Fact]
    public void LatestLinear_OnlyCsfeaLinearWithResult()
    {
        FemAnalysis A(int id, string kind, int? result, string status = "ok") =>
            new() { Id = id, Kind = kind, ResultId = result, Status = status };
        var analyses = new[]
        {
            A(1, FemCsfeaRunner.LinearAnalysisKind, 10),
            A(2, FemCsfeaRunner.AnalysisKind, 11),
            A(3, FemCsfeaRunner.LinearAnalysisKind, null),
            A(4, FemCsfeaRunner.LinearAnalysisKind, 12, "failed"),
        };
        Assert.Equal(1, FemLinearLoadCaseForceSets.LatestLinear(analyses)?.Id);
        Assert.Null(FemLinearLoadCaseForceSets.LatestLinear(analyses.Skip(1)));
    }
}
