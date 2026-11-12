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

    /// <summary>Ведомый узел АЖТ на разрезе (узел стены 11 у ведущего 10 на том же разрезе) следует за ведущим: заданных
    /// перемещений по DOF тела у него нет, у ведущего — есть; предупреждения нет. Тело по X Y Z — повороты ведомого из маски.</summary>
    [Fact]
    public void Kinematic_RigidSlaveOnCut_FollowsMaster()
    {
        var body = new FemRigidBody { MasterNodeTag = "10", Mask = 0b111 };
        body.SetSlaveNodeTags(["11"]);
        var input = Input(FragmentBcPreset.Kinematic, Masks(L1, L2));
        var r = FragmentCutBoundary.Resolve(new FragmentCutBoundaryInput
        {
            Cuts = input.Cuts, Preset = input.Preset, Masks = input.Masks, MeshNodes = input.MeshNodes, Stages = input.Stages,
            LoadCaseTags = input.LoadCaseTags, RigidBodies = [body],
        });

        Assert.False(r.HasErrors, string.Join("; ", r.Diagnostics.Select(d => d.Message)));
        Assert.DoesNotContain(r.Prescribed, p => p.MeshNodeTag == "11" && p.Dof < 3);
        Assert.Contains(r.Prescribed, p => p.MeshNodeTag == "11" && p.Dof == 3);
        Assert.Equal(3, r.Prescribed.Count(p => p.LoadCaseId == 1 && p.MeshNodeTag == "10" && p.Dof < 3));
        Assert.Equal(2 * (6 + 4 * 5 + 6) - 2 * 3, r.Prescribed.Count);
        Assert.DoesNotContain(r.Diagnostics, d => d.Code == "fragment_cut_rigid_slave");
    }

    /// <summary>Ведущий узел вне разрезов: кинематика ведомого всё равно снимается — предупреждение со списком узлов.</summary>
    [Fact]
    public void Kinematic_RigidSlaveOnCut_MasterOffCut_Warns()
    {
        var body = new FemRigidBody { MasterNodeTag = "50" };
        body.SetSlaveNodeTags(["12"]);
        var input = Input(FragmentBcPreset.Kinematic, Masks(L1, L2));
        var r = FragmentCutBoundary.Resolve(new FragmentCutBoundaryInput
        {
            Cuts = input.Cuts, Preset = input.Preset, Masks = input.Masks, MeshNodes = input.MeshNodes, Stages = input.Stages,
            LoadCaseTags = input.LoadCaseTags, RigidBodies = [body],
        });

        Assert.DoesNotContain(r.Prescribed, p => p.MeshNodeTag == "12");
        var w = Assert.Single(r.Diagnostics, d => d.Code == "fragment_cut_rigid_slave");
        Assert.False(w.IsError);
        Assert.Equal(["12"], w.SourceKeys);
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

    // ------------------------------------------------------------------ боковые грани призмы (спека призмы 3.2)

    /// <summary>Конец балки C1 на боковой грани +X (узел «1»), стена W1 — на низе.</summary>
    static FragmentCutBoundaryInput LateralInput(FragmentBcPreset preset, FragmentBcPreset lateral,
        IReadOnlyList<KinematicDofRef>? other = null, Dictionary<int, byte[]>? overrides = null, bool horizontal = true) => new()
    {
        Cuts = horizontal
            ?
            [
                (C1, new(CutC1 with { Side = FragmentCutSide.Lateral, Normal = V(1, 0, 0) }, V(0, 3, 1.5), null)),
                (W1, new(CutW1, null, V(0, 1, 0))),
            ]
            :
            [
                (C1, new(CutC1 with { Side = FragmentCutSide.Lateral, Normal = V(1, 0, 0) }, V(0, 3, 1.5), null)),
            ],
        Preset = preset, LateralPreset = lateral, Masks = Masks(L1), MeshNodes = Mesh,
        Stages = [new("S1", [(1, 1.0)])], LoadCaseTags = new Dictionary<int, string> { [1] = "G" },
        OtherConstraints = other ?? [], Overrides = overrides ?? [],
    };

    [Fact]
    public void Lateral_OwnPreset()
    {
        var r = FragmentCutBoundary.Resolve(LateralInput(FragmentBcPreset.Kinematic, FragmentBcPreset.Static));
        Assert.False(r.HasErrors, string.Join("; ", r.Diagnostics.Select(d => d.Message)));
        Assert.DoesNotContain(r.Prescribed, p => p.MeshNodeTag == "1");
        Assert.Contains(r.Loads, l => l.MeshNodeTag == "1");
        Assert.Contains(r.Prescribed, p => p.MeshNodeTag == "11");
    }

    [Fact]
    public void Lateral_IsolationPinsUz_DefaultPresetStatic()
    {
        var cut = CutC1 with { Side = FragmentCutSide.Lateral, Normal = V(1, 0, 0) };
        Assert.Equal(new[] { FragmentDofMode.Free, FragmentDofMode.Free, FragmentDofMode.Fixed, FragmentDofMode.Free,
            FragmentDofMode.Free, FragmentDofMode.Free }, FragmentCutBc.PresetModes(FragmentBcPreset.Isolation, FragmentCutSide.Lateral));
        Assert.Equal(FragmentBcPreset.Static, FragmentCutBc.ParseLateralPreset(null));
        Assert.Equal(FragmentBcPreset.Kinematic, FragmentCutBc.ParseLateralPreset("Kinematic"));
        Assert.Equal(FragmentBcPreset.Isolation, FragmentCutBc.PresetOf(cut, FragmentBcPreset.Kinematic, FragmentBcPreset.Isolation));
        Assert.Equal(FragmentBcPreset.Kinematic, FragmentCutBc.PresetOf(CutC1, FragmentBcPreset.Kinematic, FragmentBcPreset.Isolation));
    }

    /// <summary>Кромка плиты на боковой грани в кинематике: сверлильный поворот — Rz (нормаль плиты Z).</summary>
    [Fact]
    public void Lateral_SlabEdge_DrillingRz()
    {
        var cut = new FragmentCut("S1", FragmentCutKind.Edge, FragmentCutSide.Lateral, 3, null, [(V(4.5, 0, 3), V(4.5, 3, 3))], V(1, 0, 0));
        var modes = FragmentCutBc.Resolve(FragmentBcPreset.Kinematic, cut, null, V(0, 0, 1), out bool oblique);
        Assert.False(oblique);
        Assert.Equal(FragmentDofMode.Free, modes[5]);
        Assert.All(modes.Take(5), m => Assert.Equal(FragmentDofMode.MaskDisplacement, m));
    }

    [Fact]
    public void AllStatic_WithLateral_NoSupports_Refused()
    {
        var r = FragmentCutBoundary.Resolve(LateralInput(FragmentBcPreset.Static, FragmentBcPreset.Static));
        Assert.Contains(r.Diagnostics, d => d.IsError && d.Code == "fragment_bc_all_static");
    }

    [Fact]
    public void AllStatic_WithSupports_OrOverride_OrStorey_NotRefused()
    {
        var supported = FragmentCutBoundary.Resolve(LateralInput(FragmentBcPreset.Static, FragmentBcPreset.Static,
            other: [new KinematicDofRef(V(3, 0, 3), 2)]));
        Assert.DoesNotContain(supported.Diagnostics, d => d.Code == "fragment_bc_all_static");

        var fixedUz = FragmentCutBc.Encode([null, null, FragmentDofMode.Fixed, null, null, null]);
        var overridden = FragmentCutBoundary.Resolve(LateralInput(FragmentBcPreset.Static, FragmentBcPreset.Static,
            overrides: new() { [W1] = fixedUz }));
        Assert.DoesNotContain(overridden.Diagnostics, d => d.Code == "fragment_bc_all_static");

        var storey = FragmentCutBoundary.Resolve(Input(FragmentBcPreset.Static, Masks(L1, L2)));
        Assert.DoesNotContain(storey.Diagnostics, d => d.Code == "fragment_bc_all_static");
    }

    /// <summary>Полоса без горизонтальных разрезов (только боковой): совет — боковые грани, а не недостижимый горизонтальный.</summary>
    [Fact]
    public void AllStatic_OnlyLateralCuts_AdviceNamesLateralFaces()
    {
        var r = FragmentCutBoundary.Resolve(LateralInput(FragmentBcPreset.Static, FragmentBcPreset.Static, horizontal: false));

        var d = Assert.Single(r.Diagnostics, x => x.Code == "fragment_bc_all_static");
        Assert.Contains("боковых", d.Message);
        Assert.DoesNotContain("горизонтальном", d.Message);
        // С горизонтальным разрезом прежний совет.
        var withHorizontal = FragmentCutBoundary.Resolve(LateralInput(FragmentBcPreset.Static, FragmentBcPreset.Static));
        Assert.Contains("горизонтальном", Assert.Single(withHorizontal.Diagnostics, x => x.Code == "fragment_bc_all_static").Message);
    }

    // ------------------------------------------------------------------ свободные части сетки (спека 2026-11-06)

    const int S1 = 104;

    /// <summary>
    /// Стена W1 (y = 0, x 2…6, z 1,5…2,5; узлы 10…14 и 20…24, 4 КЭ) с разрезом низа и отдельная полоса плиты z = 3
    /// (узлы 30 (8; 0), 31 (8; 1), 32 (9; 0), 33 (9; 1), КЭ «s1») с боковым разрезом x = 8 на узлах 30, 31.
    /// </summary>
    static FragmentCutBoundaryInput FreePart(bool withElements = true, bool withWall = true,
        IReadOnlyList<FemRigidBody>? bodies = null, IReadOnlyList<KinematicDofRef>? other = null)
    {
        var nodes = new List<FemMeshNode>();
        for (int i = 0; i < 5; i++)
        {
            nodes.Add(new() { NodeTag = $"{10 + i}", X = 2 + i, Y = 0, Z = 1.5 });
            nodes.Add(new() { NodeTag = $"{20 + i}", X = 2 + i, Y = 0, Z = 2.5 });
        }
        nodes.Add(new() { NodeTag = "30", X = 8, Y = 0, Z = 3 });
        nodes.Add(new() { NodeTag = "31", X = 8, Y = 1, Z = 3 });
        nodes.Add(new() { NodeTag = "32", X = 9, Y = 0, Z = 3 });
        nodes.Add(new() { NodeTag = "33", X = 9, Y = 1, Z = 3 });
        var elements = Enumerable.Range(0, 4).Select(i => new FemElement
        {
            ElemTag = $"w{i}", ElemType = "shell", SourceMemberTag = "W1",
            NodeIdsJson = $"[{10 + i},{11 + i},{20 + i},{21 + i}]",
        }).Append(new FemElement { ElemTag = "s1", ElemType = "shell", SourceMemberTag = "S", NodeIdsJson = "[30,32,31,33]" }).ToList();

        var slab = new FragmentCut("S", FragmentCutKind.Edge, FragmentCutSide.Lateral, 3, null, [(V(8, 0, 3), V(8, 1, 3))], V(-1, 0, 0));
        var slabSamples = new[] { 0.0, 0.5, 1.0 }.Select(s => new FragmentMaskSample(s,
            new Dof6(1e-3, 0, -1e-3 * (1 + s), 0, 0, 0), new Dof6(0, 0, 1e3, 0, 0, 0))).ToList();
        var cuts = new List<(int, FragmentMaskCutInput)> { (S1, new(slab, null, V(0, 0, 1))) };
        var items = new List<FragmentMaskItem> { new(S1, L1, slabSamples) };
        if (withWall)
        {
            cuts.Insert(0, (W1, new(CutW1, null, V(0, 1, 0))));
            items.Add(new(W1, L1, Wall(1)));
        }
        return new FragmentCutBoundaryInput
        {
            Cuts = cuts, Preset = FragmentBcPreset.Kinematic, LateralPreset = FragmentBcPreset.Static,
            Masks = new FragmentMaskSet { SourceKind = FragmentMaskSet.SourceCsfeaLinear, Items = items },
            MeshNodes = nodes, MeshElements = withElements ? elements : [], RigidBodies = bodies ?? [],
            OtherConstraints = other ?? [], Stages = [new("S1", [(1, 1.0)])],
            LoadCaseTags = new Dictionary<int, string> { [1] = "G" },
        };
    }

    static string Text(FragmentCutBoundaryResult r) => string.Join("; ", r.Diagnostics.Select(d => $"{d.Code}: {d.Message}"));

    /// <summary>Полоса не связана со стеной и не закреплена — её разрез на перемещениях родителя, сверлильный Rz свободен.</summary>
    [Fact]
    public void FreeComponent_CutsSwitchedToKinematic()
    {
        var r = FragmentCutBoundary.Resolve(FreePart());

        Assert.False(r.HasErrors, Text(r));
        foreach (var tag in new[] { "30", "31" })
        {
            Assert.Equal(5, r.Prescribed.Count(p => p.MeshNodeTag == tag));
            Assert.DoesNotContain(r.Prescribed, p => p.MeshNodeTag == tag && p.Dof == 5);
            Assert.DoesNotContain(r.Loads, l => l.MeshNodeTag == tag);
        }
        Assert.Equal(1e-3, Prescribed(r, 1, "30", 0), 15);
        Assert.Equal(-2e-3, Prescribed(r, 1, "31", 2), 15);
        var d = Assert.Single(r.Diagnostics, x => x.Code == "fragment_component_free");
        Assert.False(d.IsError);
        Assert.Contains("S", d.SourceKeys!);
    }

    [Fact]
    public void FreeComponent_WithoutMeshElements_Unchanged()
    {
        var r = FragmentCutBoundary.Resolve(FreePart(withElements: false));

        Assert.False(r.HasErrors, Text(r));
        Assert.Contains(r.Loads, l => l.MeshNodeTag == "30");
        Assert.DoesNotContain(r.Prescribed, p => p.MeshNodeTag == "30");
        Assert.DoesNotContain(r.Diagnostics, x => x.Code == "fragment_component_free");
    }

    /// <summary>Жёсткое тело 22 → 32 связывает полосу со стеной — одна закреплённая компонента, разрез силовой.</summary>
    [Fact]
    public void ComponentLinkedByRigidBody_NotFree()
    {
        var body = new FemRigidBody { MasterNodeTag = "22" };
        body.SetSlaveNodeTags(["32"]);
        var r = FragmentCutBoundary.Resolve(FreePart(bodies: [body]));

        Assert.False(r.HasErrors, Text(r));
        Assert.Contains(r.Loads, l => l.MeshNodeTag == "30");
        Assert.DoesNotContain(r.Diagnostics, x => x.Code == "fragment_component_free");
    }

    /// <summary>Опора в узле полосы 32 (прочая связь по положению) — полоса закреплена, разрез силовой.</summary>
    [Fact]
    public void ComponentWithSupport_NotFree()
    {
        var other = Enumerable.Range(0, 3).Select(dof => new KinematicDofRef(V(9, 0, 3), dof)).ToList();
        var r = FragmentCutBoundary.Resolve(FreePart(other: other));

        Assert.False(r.HasErrors, Text(r));
        Assert.Contains(r.Loads, l => l.MeshNodeTag == "30");
        Assert.DoesNotContain(r.Diagnostics, x => x.Code == "fragment_component_free");
    }

    /// <summary>
    /// Две несвязанные стены (деформационный шов): W1 (y = 0, узлы 10…14 / 20…24) и W2 (y = 5, узлы 40…44 / 50…54), обе с
    /// разрезом низа z = 1,5; <paramref name="slab"/> — ещё отдельная плита z = 3 без разрезов (узлы 30…33).
    /// </summary>
    static FragmentCutBoundaryInput TwoWalls(FragmentBcPreset preset, bool slab = false, IReadOnlyList<KinematicDofRef>? other = null)
    {
        var nodes = new List<FemMeshNode>();
        var elements = new List<FemElement>();
        foreach (var (wall, y, b) in new[] { ("W1", 0.0, 10), ("W2", 5.0, 40) })
        {
            for (int i = 0; i < 5; i++)
            {
                nodes.Add(new() { NodeTag = $"{b + i}", X = 2 + i, Y = y, Z = 1.5 });
                nodes.Add(new() { NodeTag = $"{b + 10 + i}", X = 2 + i, Y = y, Z = 2.5 });
            }
            for (int i = 0; i < 4; i++)
                elements.Add(new() { ElemTag = $"{wall}.{i}", ElemType = "shell", SourceMemberTag = wall,
                    NodeIdsJson = $"[{b + i},{b + 1 + i},{b + 10 + i},{b + 11 + i}]" });
        }
        if (slab)
        {
            nodes.Add(new() { NodeTag = "30", X = 8, Y = 0, Z = 3 });
            nodes.Add(new() { NodeTag = "31", X = 8, Y = 1, Z = 3 });
            nodes.Add(new() { NodeTag = "32", X = 9, Y = 0, Z = 3 });
            nodes.Add(new() { NodeTag = "33", X = 9, Y = 1, Z = 3 });
            elements.Add(new() { ElemTag = "s1", ElemType = "shell", SourceMemberTag = "S", NodeIdsJson = "[30,32,31,33]" });
        }
        var cutW2 = new FragmentCut("W2", FragmentCutKind.Edge, FragmentCutSide.Bottom, 1.5, null, [(V(2, 5, 1.5), V(6, 5, 1.5))]);
        const int W2 = 105;
        return new FragmentCutBoundaryInput
        {
            Cuts = [(W1, new(CutW1, null, V(0, 1, 0))), (W2, new(cutW2, null, V(0, 1, 0)))],
            Preset = preset,
            Masks = new FragmentMaskSet { SourceKind = FragmentMaskSet.SourceCsfeaLinear, Items = [new(W1, L1, Wall(1)), new(W2, L1, Wall(2))] },
            MeshNodes = nodes, MeshElements = elements, OtherConstraints = other ?? [], Stages = [new("S1", [(1, 1.0)])],
            LoadCaseTags = new Dictionary<int, string> { [1] = "G" },
        };
    }

    /// <summary>Силовые разрезы обеих стен без опор: жёсткие смещения снимаются у каждой части, не одни шесть на фрагмент.</summary>
    [Fact]
    public void SplitParts_AllStatic_GaugePerPart()
    {
        var r = FragmentCutBoundary.Resolve(TwoWalls(FragmentBcPreset.Static));

        Assert.False(r.HasErrors, Text(r));
        var parts = Assert.Single(r.Diagnostics, x => x.Code == "fragment_components");
        Assert.False(parts.IsError);
        Assert.Contains("частей 2:", parts.Message);
        Assert.Equal(6, r.Gauge.Count(g => int.Parse(g.MeshNodeTag) < 40));
        Assert.Equal(6, r.Gauge.Count(g => int.Parse(g.MeshNodeTag) >= 40));
        Assert.Contains("по частям сетки, 2", Assert.Single(r.Diagnostics, x => x.Code == "fragment_gauge").Message);
        Assert.DoesNotContain(r.Diagnostics, x => x.Code == "fragment_mechanism");
        Assert.DoesNotContain(r.Diagnostics, x => x.Code == "fragment_component_free");
    }

    /// <summary>
    /// Опоры у стены W2 снимают её жёсткие смещения сами (автозакреплений у неё нет); свободная W1 уходит на перемещения из
    /// родителя (спека 2026-11-06) и автозакрепления тоже не требует.
    /// </summary>
    [Fact]
    public void SplitParts_SupportCountsOnlyForItsPart()
    {
        var other = new[] { 0, 1, 2 }.SelectMany(d => new[] { V(2, 5, 2.5), V(6, 5, 2.5), V(4, 5, 1.5) }
            .Select(p => new KinematicDofRef(p, d))).ToList();
        var r = FragmentCutBoundary.Resolve(TwoWalls(FragmentBcPreset.Static, other: other));

        Assert.False(r.HasErrors, Text(r));
        Assert.Empty(r.Gauge);
        Assert.Contains("W1", Assert.Single(r.Diagnostics, x => x.Code == "fragment_component_free").SourceKeys!);
        Assert.Contains(r.Prescribed, p => p.MeshNodeTag == "10");
        Assert.Contains(r.Loads, l => l.MeshNodeTag == "40");
    }

    /// <summary>Плита без разрезов и без опор ничем не держится — механизм с её КонЭ; стены на перемещениях из родителя.</summary>
    [Fact]
    public void SplitParts_FloatingPart_Mechanism()
    {
        var r = FragmentCutBoundary.Resolve(TwoWalls(FragmentBcPreset.Kinematic, slab: true));

        Assert.False(r.HasErrors, Text(r));
        Assert.Contains("частей 3:", Assert.Single(r.Diagnostics, x => x.Code == "fragment_components").Message);
        var m = Assert.Single(r.Diagnostics, x => x.Code == "fragment_mechanism");
        Assert.Contains("«S»", m.Message);
        Assert.DoesNotContain("«W1»", m.Message);
    }

    /// <summary>Одна связная часть — сообщения о частях нет, автозакрепление прежнее.</summary>
    [Fact]
    public void SingleConnectedPart_NoPartsMessage()
    {
        var body = new FemRigidBody { MasterNodeTag = "22" };
        body.SetSlaveNodeTags(["32"]);
        var r = FragmentCutBoundary.Resolve(FreePart(bodies: [body]));

        Assert.DoesNotContain(r.Diagnostics, x => x.Code == "fragment_components");
    }

    /// <summary>Закреплённых частей нет вовсе (только силовой боковой разрез) — режимы прежние, прежний отказ.</summary>
    [Fact]
    public void NoRestrainedComponent_Unchanged()
    {
        var r = FragmentCutBoundary.Resolve(FreePart(withWall: false));

        Assert.DoesNotContain(r.Diagnostics, x => x.Code == "fragment_component_free");
        Assert.Contains(r.Diagnostics, x => x.Code == "fragment_bc_all_static" && x.IsError);
    }
}
