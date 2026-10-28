using CScore.Fem;
using CScore.Fem.Fragment;
using CScore.Fem.Fragment.Masks;
using CScore.Planar;
using CScore.Submodel;
using Xunit;
using static CScore.Tests.Fem.Fragment.Masks.FragmentMaskFixtures;

namespace CScore.Tests.Fem.Fragment.Masks;

/// <summary>
/// Низ фрагмента z = 1,5: колонна C1 в (0, 3) — узел сетки «1»; стена W1 вдоль X от 2 до 6 (узлы 10…14 через 1 м,
/// нормаль +Y); колонна C2 стоит на конце стены (6, 0) — общий узел «14». Маски загружений 1 и 2.
/// </summary>
public sealed class FragmentCutBoundaryTests
{
    const int C1 = 101, W1 = 102, C2 = 103;

    static readonly List<FemMeshNode> Mesh =
    [
        new() { NodeTag = "1", X = 0, Y = 3, Z = 1.5, SourceNodeTag = "n1" },
        .. Enumerable.Range(0, 5).Select(i => new FemMeshNode { NodeTag = $"{10 + i}", X = 2 + i, Y = 0, Z = 1.5 }),
        new() { NodeTag = "50", X = 3, Y = 0, Z = 3 },
    ];

    static readonly FragmentCut CutC1 = new("C1", FragmentCutKind.End, FragmentCutSide.Bottom, 1.5, "n1", []);
    static readonly FragmentCut CutW1 = new("W1", FragmentCutKind.Edge, FragmentCutSide.Bottom, 1.5, null, [(V(2, 0, 1.5), V(6, 0, 1.5))]);
    static readonly FragmentCut CutC2 = new("C2", FragmentCutKind.End, FragmentCutSide.Bottom, 1.5, "n2", []);

    static List<(int, FragmentMaskCutInput)> Cuts =>
    [
        (C1, new(CutC1, V(0, 3, 1.5), null)),
        (W1, new(CutW1, null, V(0, 1, 0))),
        (C2, new(CutC2, V(6, 0, 1.5), null)),
    ];

    /// <summary>Сэмплы стены: s = 0, 2, 4; перемещения растут по s, погонная сила — линейно.</summary>
    static List<FragmentMaskSample> Wall(double k) => new[] { 0.0, 2.0, 4.0 }.Select(s => new FragmentMaskSample(s,
        new Dof6(k * s * 1e-3, 0, -k * (1 + s) * 1e-3, k * 1e-4, k * 2e-4, k * 3e-4),
        new Dof6(0, 0, k * (100 + 10 * s) * 1e3, 0, 0, 0))).ToList();

    static List<FragmentMaskSample> Column(double k, double fz) =>
        [new(0, new Dof6(k * 1e-3, k * 2e-3, -k * 3e-3, k * 1e-4, -k * 1e-4, k * 5e-5), new Dof6(k * 1e3, 0, k * fz * 1e3, 0, k * 2e3, 0))];

    static FragmentMaskSet Masks(params FragmentMaskKey[] keys) => new()
    {
        SourceKind = FragmentMaskSet.SourceCsfeaLinear,
        Items = keys.SelectMany((key, i) => new FragmentMaskItem[]
        {
            new(C1, key, Column(i + 1, 300)), new(W1, key, Wall(i + 1)), new(C2, key, Column(i + 1, 200)),
        }).ToList(),
    };

    static FragmentCutBoundaryInput Input(FragmentBcPreset preset, FragmentMaskSet? masks,
        params FragmentBcStage[] stages) => new()
    {
        Cuts = Cuts, Preset = preset, Masks = masks, MeshNodes = Mesh,
        Stages = stages.Length > 0 ? stages : [new("S1", [(1, 1.0)]), new("S2", [(2, 1.0)])],
        LoadCaseTags = new Dictionary<int, string> { [1] = "G", [2] = "Q" },
    };

    static readonly FragmentMaskKey L1 = FragmentMaskKey.OfLoadCase(1), L2 = FragmentMaskKey.OfLoadCase(2);

    static double Prescribed(FragmentCutBoundaryResult r, int lc, string tag, int dof) =>
        Assert.Single(r.Prescribed, p => p.LoadCaseId == lc && p.MeshNodeTag == tag && p.Dof == dof).Value;

    [Fact]
    public void Kinematic_BarOwnsSharedNode_WallDrillingFree()
    {
        var r = FragmentCutBoundary.Resolve(Input(FragmentBcPreset.Kinematic, Masks(L1, L2)));

        Assert.False(r.HasErrors, string.Join("; ", r.Diagnostics.Select(d => d.Message)));
        Assert.Empty(r.Gauge);
        Assert.Empty(r.Loads);
        // C1: 6 DOF × 2 загружения; стена: узлы 10…13 по 5 DOF (Ry свободен); узел 14 — от C2, все 6.
        Assert.Equal(2 * (6 + 4 * 5 + 6), r.Prescribed.Count);
        Assert.DoesNotContain(r.Prescribed, p => p.MeshNodeTag == "11" && p.Dof == 4);
        Assert.Equal(-2 * 1e-4, Prescribed(r, 2, "14", 4), 15);           // Ry узла 14 — маска C2 (k = 2)
        Assert.Equal(-2 * 3e-3, Prescribed(r, 2, "14", 2), 15);           // Uz — тоже C2, не стены
        Assert.Equal(1e-3, Prescribed(r, 1, "11", 0), 15);                // стена: s = 1 → Ux = 1·1e-3
        Assert.Equal(-2 * 4e-3, Prescribed(r, 2, "13", 2), 15);           // x = 5 → s = 3, между сэмплами 2 и 4
        Assert.Equal(2e-3, Prescribed(r, 1, "1", 1), 15);
    }

    [Fact]
    public void Static_ForcesPreserveResultant_GaugeSixTranslations()
    {
        var r = FragmentCutBoundary.Resolve(Input(FragmentBcPreset.Static, Masks(L1, L2)));

        Assert.False(r.HasErrors, string.Join("; ", r.Diagnostics.Select(d => d.Message)));
        Assert.Empty(r.Prescribed);
        Assert.Equal(6, r.Gauge.Count);
        Assert.All(r.Gauge, g => Assert.True(g.Dof < 3));
        Assert.Contains(r.Diagnostics, d => d.Code == "fragment_gauge" && !d.IsError);
        foreach (var g in r.Gauge) Assert.True((r.Supports[g.MeshNodeTag] & 1 << g.Dof) != 0);

        // Загружение 2: ΣFz = 2·300 + 2·200 + стена 2·∫(100 + 10 s) ds по 0…4 = 2·480 кН.
        var loads2 = r.Loads.Where(l => l.LoadCaseId == 2).ToList();
        Assert.Equal((600 + 400 + 960) * 1e3, loads2.Sum(l => l.Force.Z), 6);
        // Узел 14 — сила C2 плюс доля стены (крышка последнего узла: ∫ φ(s)(100 + 10 s) по 3…4 = 0,5·(100+30) + ...).
        double wallShare = 2 * (1.0 / 6 * (130 + 2 * 140) * 1e3);  // точный интеграл линейной функции с функцией узла
        Assert.Equal(400e3 + wallShare, loads2.Single(l => l.MeshNodeTag == "14").Force.Z, 6);
        Assert.Equal(2 * 2e3, loads2.Single(l => l.MeshNodeTag == "1").Force.Ry, 9);
    }

    [Fact]
    public void Isolation_NeedsNoMasks_KinematicWithoutMasksIsError()
    {
        var iso = FragmentCutBoundary.Resolve(Input(FragmentBcPreset.Isolation, null));
        var kin = FragmentCutBoundary.Resolve(Input(FragmentBcPreset.Kinematic, null));

        Assert.False(iso.HasErrors);
        Assert.Equal(0b111, iso.Supports["1"]);
        Assert.Equal(0b111, iso.Supports["12"]);
        Assert.Empty(iso.Gauge);
        Assert.Contains(kin.Diagnostics, d => d.Code == "fragment_masks_missing" && d.IsError);
    }

    /// <summary>Маски есть только для загружения 1, стадия S2 требует 2 → ошибка на каждом разрезе с DOF и загружением.</summary>
    [Fact]
    public void MissingLoadCaseMask_IsErrorPerCut()
    {
        var r = FragmentCutBoundary.Resolve(Input(FragmentBcPreset.Kinematic, Masks(L1)));

        var errors = r.Diagnostics.Where(d => d.Code == "fragment_mask_missing").ToList();
        Assert.True(r.HasErrors);
        Assert.Contains(errors, d => d.Message.Contains("«W1»") && d.Message.Contains("загружение «Q»") && d.Message.Contains("DOF Ux"));
        Assert.Equal(3, errors.Select(d => d.SourceKeys![0]).Distinct().Count());
    }

    /// <summary>
    /// Маски сочетаний (секущий источник): стадии S1 = {1}, S2 = {2} копятся; ключи {1} и {1 + 2}. Загружение-приращение
    /// стадии 2 — разность масок; во фрагменте этих загружений нет (Id −1, −2).
    /// </summary>
    [Fact]
    public void StageKeyedMasks_BecomeIncrementCases()
    {
        var k1 = FragmentMaskKey.OfStageTerms([(1, 1.0)]);
        var k12 = FragmentMaskKey.OfStageTerms([(2, 1.0), (1, 1.0)]);
        var r = FragmentCutBoundary.Resolve(Input(FragmentBcPreset.Kinematic, Masks(k1, k12)));

        Assert.False(r.HasErrors, string.Join("; ", r.Diagnostics.Select(d => d.Message)));
        Assert.Equal([(0, -1), (1, -2)], r.StageCases.Select(c => (c.StageIndex, c.LoadCaseId)));
        Assert.Equal(1e-3, Prescribed(r, -1, "1", 0), 15);          // маска k1 (k = 1)
        Assert.Equal(2e-3 - 1e-3, Prescribed(r, -2, "1", 0), 15);   // k12 (k = 2) минус k1
        Assert.DoesNotContain(r.Prescribed, p => p.LoadCaseId > 0);
    }

    [Fact]
    public void CheckGaugeReactions_WarnsAboveOnePercent()
    {
        var r = FragmentCutBoundary.Resolve(Input(FragmentBcPreset.Static, Masks(L1, L2)));
        double scale = r.ForceScale[1];
        var g0 = r.Gauge[0];

        Assert.Null(FragmentCutBoundary.CheckGaugeReactions(r, [(1, 1.0)], (_, _) => 0, "S1"));
        Assert.Null(FragmentCutBoundary.CheckGaugeReactions(r, [(1, 1.0)],
            (t, d) => t == g0.MeshNodeTag && d == g0.Dof ? 0.005 * scale : 0, "S1"));
        var w = FragmentCutBoundary.CheckGaugeReactions(r, [(1, 1.0)],
            (t, d) => t == g0.MeshNodeTag && d == g0.Dof ? -0.05 * scale : 0, "S1");
        Assert.NotNull(w);
        Assert.Equal("fragment_gauge_reaction", w.Code);
        Assert.Contains(g0.MeshNodeTag, w.SourceKeys!);
    }

    /// <summary>Опоры фрагмента (прочие связи) уже снимают жёсткие смещения — автозакреплений нет.</summary>
    [Fact]
    public void Static_WithOtherSupports_NoGauge()
    {
        var other = new List<KinematicDofRef>();
        foreach (var p in new[] { V(3, 0, 3), V(10, 0, 3), V(3, 8, 3) })
            for (int d = 0; d < 3; d++) other.Add(new(p, d));
        var input = Input(FragmentBcPreset.Static, Masks(L1, L2));
        var r = FragmentCutBoundary.Resolve(new FragmentCutBoundaryInput
        {
            Cuts = input.Cuts, Preset = input.Preset, Masks = input.Masks, MeshNodes = input.MeshNodes,
            Stages = input.Stages, OtherConstraints = other,
        });
        Assert.Empty(r.Gauge);
        Assert.Empty(r.Supports);
    }
}
