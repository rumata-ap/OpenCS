using CScore.Fem;
using CSfea.CScoreBridge.Structural;
using OpenCS.Services;
using OpenCS.Tasks;
using Xunit;

namespace OpenCS.Tests;

/// <summary>Комбинации схемы по линейному результату CSfea — суперпозицией полей загружений.</summary>
public sealed class FemCsfeaCombinationFieldsTests
{
    static (FemSchema Schema, FemAnalysis Analysis, FemCsfeaResultSummary Summary) Model()
    {
        var schema = new FemSchema { Id = 1, Tag = "Схема" };
        schema.LoadCases.Add(new FemLoadCase { Id = 11, SchemaId = 1, Tag = "G", Sp20Type = "permanent" });
        schema.LoadCases.Add(new FemLoadCase { Id = 12, SchemaId = 1, Tag = "Q1", Sp20Type = "short_term" });
        schema.LoadCases.Add(new FemLoadCase { Id = 13, SchemaId = 1, Tag = "Снег", Sp20Type = "short_term" });
        // В расчёте — G и Q1; G второй раз (повтор — первая строка).
        var stages = new[] { 11, 12, 11 }.Select(id => new FemAnalysisStage
        {
            Tag = id.ToString(),
            LoadExpressionJson = new FemLoadExpression { Mode = FemLoadExpressionMode.Single, LoadCaseIds = [id] }.ToJson(),
        }).ToList();
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

    static FemLoadDefinition Combination(params (int Id, double K)[] terms) => new()
    {
        Tag = "К",
        CombinationType = "C",
        ExpressionJson = new FemLoadExpression
        {
            Mode = FemLoadExpressionMode.Sum, CombinationType = "C",
            Terms = terms.Select(t => new FemLoadTerm { LoadCaseId = t.Id, Coefficient = t.K }).ToList(),
        }.ToJson(),
    };

    /// <param name="shellStrains">С деформациями пластин; <paramref name="beamStrains"/> — стержней.</param>
    static RcSecantStepFields Fields(double scale, bool shellStrains = false, bool beamStrains = false) => new()
    {
        HasStrains = shellStrains,
        ShellStrains = shellStrains ? Enumerable.Range(0, 6).Select(i => scale * 1e-4 * (i + 1)).ToArray() : [],
        BeamStrains = beamStrains ? Enumerable.Range(0, 9).Select(i => -scale * 1e-5 * (i + 1)).ToArray() : [],
        NodeIds = [1, 2], Displacements = Enumerable.Range(0, 12).Select(i => scale * i).ToArray(),
        ShellIds = [101], ShellForces = Enumerable.Range(0, 8).Select(i => scale * (i + 1)).ToArray(),
        ShellStates = Enumerable.Repeat(double.NaN, 5).ToArray(), ShellFlags = [0],
        BeamIds = [201], BeamForces = Enumerable.Range(0, 12).Select(i => -scale * i).ToArray(), BeamFlags = [0],
        ReactionNodeIds = [1], Reactions = Enumerable.Range(0, 6).Select(i => scale * 10 * i).ToArray(), ReactionKinds = [1],
        ReactionSums = Enumerable.Range(0, 12).Select(i => scale * i).ToArray(), LoadSum = [0, 0, -scale],
    };

    [Fact]
    public void CaseSteps_SingleLoadCaseRows_FirstOfRepeated()
    {
        var (_, analysis, summary) = Model();
        var steps = FemCsfeaCombinationFields.CaseSteps(analysis, summary);
        Assert.Equal(new Dictionary<int, int> { [11] = 1, [12] = 2 }, steps);
    }

    [Fact]
    public void Resolve_MissingLoadCase_Unavailable_ZeroFactorIgnored()
    {
        var (schema, analysis, summary) = Model();
        var steps = FemCsfeaCombinationFields.CaseSteps(analysis, summary);

        var ok = FemCsfeaCombinationFields.Resolve(Combination((11, 1.1), (12, 1.4), (13, 0)), schema.LoadCases, steps);
        Assert.True(ok.IsAvailable);
        Assert.Equal([(1, 1.1), (2, 1.4)], ok.Terms.Select(t => (t.StepN, t.Factor)));

        var snow = FemCsfeaCombinationFields.Resolve(Combination((11, 1), (13, 0.9)), schema.LoadCases, steps);
        Assert.False(snow.IsAvailable);
        Assert.Equal("Снег", Assert.Single(snow.Missing).Tag);
    }

    [Fact]
    public void Superpose_LinearInAllFields_StatesStayEmpty()
    {
        var f = FemCsfeaCombinationFields.Superpose([(Fields(1), 1.1), (Fields(2), 0.5)]);
        // 1,1·1 + 0,5·2 = 2,1 на единицу масштаба.
        Assert.Equal(Fields(2.1).Displacements, f.Displacements, (a, b) => Math.Abs(a - b) < 1e-12);
        Assert.Equal(Fields(2.1).ShellForces, f.ShellForces, (a, b) => Math.Abs(a - b) < 1e-12);
        Assert.Equal(Fields(2.1).BeamForces, f.BeamForces, (a, b) => Math.Abs(a - b) < 1e-12);
        Assert.Equal(Fields(2.1).Reactions, f.Reactions, (a, b) => Math.Abs(a - b) < 1e-12);
        Assert.Equal(-2.1, f.LoadSum[2], 12);
        Assert.Equal(12, f.ReactionSums.Length);
        Assert.All(f.ShellStates, s => Assert.True(double.IsNaN(s)));
        Assert.Equal([1], f.ReactionKinds);
        Assert.Equal(-1, f.Stage);
    }

    /// <summary>
    /// Деформации сечений линейны так же, как усилия: комбинация складывает их, если они записаны у всех слагаемых
    /// («НДС сечения» по комбинации линейного расчёта); у одного слагаемого нет — в сумме нет.
    /// </summary>
    [Fact]
    public void Superpose_SectionStrains_WhenAllTermsHaveThem()
    {
        RcSecantStepFields WithStrains(double scale, bool shell = true) => Fields(scale, shell, beamStrains: true);

        var f = FemCsfeaCombinationFields.Superpose([(WithStrains(1), 1.1), (WithStrains(2), 0.5)]);
        Assert.True(f.HasStrains);
        Assert.True(f.HasBeamStrains);
        Assert.Equal(WithStrains(2.1).ShellStrains, f.ShellStrains, (a, b) => Math.Abs(a - b) < 1e-15);
        Assert.Equal(WithStrains(2.1).BeamStrains, f.BeamStrains, (a, b) => Math.Abs(a - b) < 1e-15);

        var partial = FemCsfeaCombinationFields.Superpose([(WithStrains(1), 1), (WithStrains(1, shell: false), 1)]);
        Assert.False(partial.HasStrains);
        Assert.True(partial.HasBeamStrains);
        Assert.False(FemCsfeaCombinationFields.Superpose([(WithStrains(1), 1), (Fields(1), 1)]).HasBeamStrains);
    }

    /// <summary>Полный прогиб п. 8.2.24: u₁ − u₂ + u₃ по перемещениям; усилия и реакции — нули, состояния пусты.</summary>
    [Fact]
    public void FullDeflection_TotalMinusLongPlusLongNL_ForcesZero()
    {
        var f = FemCsfeaCombinationFields.FullDeflection(Fields(1), Fields(0.6), Fields(1.5));
        // 1 − 0,6 + 1,5 = 1,9 на единицу масштаба.
        Assert.Equal(Fields(1.9).Displacements, f.Displacements, (a, b) => Math.Abs(a - b) < 1e-12);
        Assert.All(f.ShellForces, v => Assert.Equal(0, v));
        Assert.All(f.BeamForces, v => Assert.Equal(0, v));
        Assert.Empty(f.Reactions);
        Assert.All(f.ShellStates, s => Assert.True(double.IsNaN(s)));
        // Нулевая длительная часть: u₂ = 0.
        Assert.Equal(Fields(2.5).Displacements, FemCsfeaCombinationFields.FullDeflection(Fields(1), null, Fields(1.5)).Displacements,
            (a, b) => Math.Abs(a - b) < 1e-12);
        var moved = new RcSecantStepFields
        {
            NodeIds = [1, 3], Displacements = new double[12], ShellIds = [101], ShellForces = new double[8],
            ShellStates = new double[5], ShellFlags = [0], BeamIds = [201], BeamForces = new double[12], BeamFlags = [0],
            ReactionNodeIds = [1],
        };
        Assert.Throws<InvalidOperationException>(() => FemCsfeaCombinationFields.FullDeflection(Fields(1), null, moved));
    }

    [Fact]
    public void Superpose_DifferentMesh_Throws_OldRecordWithoutSums_Empty()
    {
        var other = Fields(1);
        var moved = new RcSecantStepFields
        {
            NodeIds = [1, 3], Displacements = other.Displacements, ShellIds = other.ShellIds, ShellForces = other.ShellForces,
            ShellStates = other.ShellStates, ShellFlags = other.ShellFlags, BeamIds = other.BeamIds,
            BeamForces = other.BeamForces, BeamFlags = other.BeamFlags,
            ReactionNodeIds = other.ReactionNodeIds, Reactions = other.Reactions, ReactionKinds = other.ReactionKinds,
        };
        Assert.Throws<InvalidOperationException>(() => FemCsfeaCombinationFields.Superpose([(Fields(1), 1), (moved, 1)]));

        var noSums = new RcSecantStepFields
        {
            NodeIds = other.NodeIds, Displacements = other.Displacements, ShellIds = other.ShellIds,
            ShellForces = other.ShellForces, ShellStates = other.ShellStates, ShellFlags = other.ShellFlags,
            BeamIds = other.BeamIds, BeamForces = other.BeamForces, BeamFlags = other.BeamFlags,
            ReactionNodeIds = other.ReactionNodeIds, Reactions = other.Reactions, ReactionKinds = other.ReactionKinds,
        };
        var f = FemCsfeaCombinationFields.Superpose([(Fields(1), 1), (noSums, 1)]);
        Assert.Empty(f.ReactionSums);
        Assert.Empty(f.LoadSum);
    }
}
