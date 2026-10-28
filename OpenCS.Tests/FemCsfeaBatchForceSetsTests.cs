using CScore;
using CScore.Fem;
using CSfea.CScoreBridge.Structural;
using OpenCS.Services;
using OpenCS.Tasks;
using Xunit;

namespace OpenCS.Tests;

/// <summary>Наборы усилий пакета CSfea «строка на вариант» (спека 2026-10-17 «наборы по вариантам», срез 1).</summary>
public sealed class FemCsfeaBatchForceSetsTests
{
    static readonly FemSchema Schema = new() { Id = 1, Tag = "Схема" };

    // Без пользовательских переопределений Windows: у ru-RU на машине может стоять точка вместо запятой.
    public FemCsfeaBatchForceSetsTests() => System.Globalization.CultureInfo.CurrentCulture = new("ru-RU", useUserOverride: false);

    /// <summary>Пластины 101–103 (Mx = k·(1, 2, 3) кН·м/м), стержни 201–202.</summary>
    static RcSecantStepFields Fields(double k)
    {
        var shell = new double[3 * RcSecantStepFields.ShellForceComponents];
        for (int e = 0; e < 3; e++) shell[e * RcSecantStepFields.ShellForceComponents + 3] = k * (e + 1) * 1e3;
        return new RcSecantStepFields
        {
            NodeIds = [], Displacements = [], ShellIds = [101, 102, 103], ShellForces = shell,
            ShellStates = new double[3 * RcSecantStepFields.ShellStateComponents], ShellFlags = new byte[3],
            BeamIds = [201, 202], BeamForces = new double[2 * RcSecantStepFields.BeamForceComponents], BeamFlags = new byte[2],
        };
    }

    static FemCsfeaBatchSummary Variant(string tag, string type, int? record, bool completed = true, bool done = true,
        double lambda = 1, string kind = FemCsfeaBatchItemKind.Combination) => new()
    {
        Tag = tag, Type = type, Kind = kind, RecordN = record, Done = done, Completed = completed,
        LoadFactor = completed ? 1 : lambda,
    };

    /// <summary>C: C1 (запись 1), C2 (не дошёл, λ = 0,73), C3 (запись 3); N: N1 (запись 4); загружение q (запись 5).</summary>
    static FemCsfeaResultSummary Summary() => new()
    {
        Batch =
        [
            Variant("C1", "C", 1), Variant("C2", "C", 2, completed: false, lambda: 0.73), Variant("C3", "C", 3),
            Variant("N1", "N", 4), Variant("q", "Q", 5, kind: FemCsfeaBatchItemKind.LoadCase),
        ],
    };

    static FemCsfeaBatchForceSets.Result Build(IReadOnlyCollection<string> groups, IReadOnlyCollection<string>? types = null,
        FemCsfeaForceSetScope? scope = null, IReadOnlyCollection<ForceSet>? existing = null, List<int>? read = null,
        IProgress<(int, int)>? progress = null, CancellationToken ct = default) =>
        FemCsfeaBatchForceSets.Build(Schema, "Пакет", Summary(), n => { read?.Add(n); return Fields(n); }, groups,
            types ?? [], scope, existing ?? [], progress, ct);

    [Fact]
    public void GroupC_RowPerVariantAndElement_SkipsNotCompleted()
    {
        var read = new List<int>();
        var scope = new FemCsfeaForceSetScope([101, 103, 202], ["Пл-1"]);

        var result = Build(["C"], scope: scope, read: read);

        Assert.Equal([1, 3], read);
        Assert.Equal(2, result.Sets.Count);
        var shells = result.Sets[0];
        Assert.Equal("Пакет — C, 2 вар. — пластины [выбор: 3 КЭ] (C)", shells.Tag);
        Assert.Equal(CalcType.C, FemCheckRunner.ExtractCalcType(shells.Tag, null));
        Assert.Equal(["101 · C1", "103 · C1", "101 · C3", "103 · C3"], shells.ShellItems.Select(r => r.Label));
        Assert.Equal([101, 103, 101, 103], shells.ShellItems.Select(r => r.SourceElementNum));
        Assert.Equal([1.0, 3.0, 3.0, 9.0], shells.ShellItems.Select(r => r.Mx));
        Assert.Equal([1, 2, 3, 4], shells.ShellItems.Select(r => r.Num));
        Assert.Contains("Не вошли 1: C2 (λ = 0,73).", shells.Description);
        Assert.Contains("Охват — выбранные КЭ: 3", shells.Description);

        var bars = result.Sets[1];
        Assert.Equal("Пакет — C, 2 вар. — стержни [выбор: 3 КЭ] (C)", bars.Tag);
        Assert.Equal(["202-1 · C1", "202-2 · C1", "202-1 · C3", "202-2 · C3"], bars.Items.Select(r => r.Label));
        Assert.Equal([1, 2, 1, 2], bars.Items.Select(r => r.SourceSectionNum));
        Assert.Contains(result.Warnings, w => w.Contains("C2 (λ = 0,73)", StringComparison.Ordinal));
        Assert.All(result.Sets, s => Assert.Null(s.SourceElementId));
    }

    [Fact]
    public void ScopeOfOneMember_SetsBoundToMember()
    {
        var member = new FemMember { Id = 5, SchemaId = Schema.Id, ElemTag = "Пл-1" };

        var result = Build(["C"], scope: new FemCsfeaForceSetScope([101, 103], ["Пл-1"], member));

        var shells = Assert.Single(result.Sets);
        Assert.Equal("Пакет — C, 2 вар. — пластины [КонЭ Пл-1: 2 КЭ] (C)", shells.Tag);
        Assert.Equal(5, shells.SourceElementId);
        Assert.Equal("Пл-1", shells.SourceElementTag);
    }

    [Fact]
    public void LoadCases_SetPerType_SamePairBase_NumberedWhenTaken()
    {
        var taken = new ForceSet { Num = 7, Tag = "Пакет — Загружения, 1 вар. — пластины (NL)" };

        var result = Build([FemCsfeaBatchGroup.LoadCases, "N"], ["NL", "N"], new FemCsfeaForceSetScope([102], []), [taken]);

        Assert.Equal(
        [
            "Пакет — N, 1 вар. — пластины [выбор: 1 КЭ] (N)",
            "Пакет — Загружения, 1 вар. — пластины [выбор: 1 КЭ] (N)",
            "Пакет — Загружения, 1 вар. — пластины [выбор: 1 КЭ] (NL)",
        ], result.Sets.Select(s => s.Tag));
        Assert.Equal([8, 9, 10], result.Sets.Select(s => s.Num));
        Assert.NotSame(result.Sets[1].ShellItems, result.Sets[2].ShellItems);
        Assert.Equal(["102 · q"], result.Sets[2].ShellItems.Select(r => r.Label));
        Assert.Same(result.Sets[2], FemEtaPsi.LongTermSet(result.Sets[1], result.Sets));

        // Без охвата занятое имя «(NL)» сдвигает номер обоих типов группы.
        var whole = Build([FemCsfeaBatchGroup.LoadCases], ["N", "NL"], existing: [taken]);
        Assert.Equal(
        [
            "Пакет — Загружения, 1 вар. — пластины (2) (N)", "Пакет — Загружения, 1 вар. — пластины (2) (NL)",
            "Пакет — Загружения, 1 вар. — стержни (N)", "Пакет — Загружения, 1 вар. — стержни (NL)",
        ], whole.Sets.Select(s => s.Tag));
        Assert.Equal(CalcType.N, FemCheckRunner.ExtractCalcType(whole.Sets[0].Tag, null));
    }

    [Fact]
    public void LoadCasesWithoutType_AndGroupWithoutCompleted_NoSets()
    {
        var result = FemCsfeaBatchForceSets.Build(Schema, "Пакет", new FemCsfeaResultSummary
        {
            Batch = [Variant("A1", "A", 1, completed: false, lambda: 0.4), Variant("q", "", 2, kind: FemCsfeaBatchItemKind.LoadCase)],
        }, n => Fields(n), ["A", FemCsfeaBatchGroup.LoadCases], [], null, [], null, CancellationToken.None);

        Assert.Empty(result.Sets);
        Assert.Contains(result.Warnings, w => w.Contains("«Загружения»: не выбран тип", StringComparison.Ordinal));
        Assert.Contains(result.Warnings, w => w.Contains("«A»: нет вариантов", StringComparison.Ordinal));
        Assert.Equal("C", FemCsfeaBatchForceSets.CalcTypeLabel("A"));
    }

    [Fact]
    public void Progress_CountsIncludedVariants_CancelStopsBeforeNext()
    {
        var reports = new List<(int, int)>();
        Build(["C", "N"], progress: new SyncProgress<(int, int)>(reports.Add));
        Assert.Equal([(0, 3), (1, 3), (2, 3), (3, 3)], reports);

        using var cts = new CancellationTokenSource();
        Assert.Throws<OperationCanceledException>(() =>
            Build(["C"], progress: new SyncProgress<(int, int)>(p => { if (p.Item1 == 1) cts.Cancel(); }), ct: cts.Token));
    }

    [Fact]
    public void ManySkipped_ListsFirstTen()
    {
        var batch = Enumerable.Range(1, 12).Select(i => Variant($"V{i}", "C", i, completed: false, done: false))
            .Append(Variant("ok", "C", 13)).ToList();

        var result = FemCsfeaBatchForceSets.Build(Schema, "Пакет", new FemCsfeaResultSummary { Batch = batch }, n => Fields(n),
            ["C"], [], null, [], null, CancellationToken.None);

        Assert.Contains("Не вошли 12: V1 (не посчитан), ", result.Sets[0].Description);
        Assert.Contains("V10 (не посчитан) и ещё 2.", result.Sets[0].Description);
    }

    sealed class SyncProgress<T>(Action<T> report) : IProgress<T>
    {
        public void Report(T value) => report(value);
    }
}
