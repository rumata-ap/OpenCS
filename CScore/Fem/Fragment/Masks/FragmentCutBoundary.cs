using System.Globalization;
using CScore.Planar;
using CScore.Submodel;

namespace CScore.Fem.Fragment.Masks;

/// <summary>Стадия расчёта фрагмента: тег и приращение — загружения фрагмента с коэффициентами (стадии копятся).</summary>
public sealed record FragmentBcStage(string Tag, IReadOnlyList<(int LoadCaseId, double Factor)> Terms);

/// <summary>Вход сборки ГУ разрезов (спека масок 5.1–5.3) — всё в Id и тегах фрагмента.</summary>
public sealed class FragmentCutBoundaryInput
{
    /// <summary>Разрезы: Id записи <c>fem_fragment_cuts</c> и вход с точкой конца или нормалью стены.</summary>
    public IReadOnlyList<(int CutId, FragmentMaskCutInput Cut)> Cuts { get; init; } = [];
    public FragmentBcPreset Preset { get; init; }
    /// <summary>Пресет боковых граней призмы (<see cref="FemSchema.FragmentLateralBcPreset"/>).</summary>
    public FragmentBcPreset LateralPreset { get; init; } = FragmentBcPreset.Static;
    /// <summary>Правки DOF разрезов поверх пресета (Id разреза → 6 байт).</summary>
    public IReadOnlyDictionary<int, byte[]> Overrides { get; init; } = new Dictionary<int, byte[]>();
    /// <summary>Действующий набор масок; null — не снимались.</summary>
    public FragmentMaskSet? Masks { get; init; }
    public IReadOnlyList<FemMeshNode> MeshNodes { get; init; } = [];
    /// <summary>Прочие кинематические связи фрагмента (опоры, пружины сетки) — для проверки жёстких смещений.</summary>
    public IReadOnlyList<KinematicDofRef> OtherConstraints { get; init; } = [];
    public IReadOnlyList<FragmentBcStage> Stages { get; init; } = [];
    /// <summary>Теги загружений фрагмента для сообщений.</summary>
    public IReadOnlyDictionary<int, string> LoadCaseTags { get; init; } = new Dictionary<int, string>();
    public double ToleranceM { get; init; } = 0.001;
}

/// <summary>Заданное значение DOF узла сетки в загружении (DOF 0..5, общие оси).</summary>
public sealed record FragmentPrescribedDof(int LoadCaseId, string MeshNodeTag, int Dof, double Value);

/// <summary>Узловая сила маски в загружении (Н, Н·м, общие оси).</summary>
public sealed record FragmentNodeForce(int LoadCaseId, string MeshNodeTag, Dof6 Force);

/// <summary>
/// Загружение приращения маски стадии (маски сочетаний секущего источника): Id отрицательный, во фрагменте не хранится;
/// входит в стадию <see cref="StageIndex"/> с коэффициентом 1.
/// </summary>
public sealed record FragmentMaskStageCase(int StageIndex, int LoadCaseId, string Tag);

/// <summary>DOF автозакрепления жёстких смещений (спека 5.3).</summary>
public sealed record FragmentGaugeDof(string MeshNodeTag, int Dof);

/// <summary>ГУ разрезов на сетке фрагмента: закрепления, заданные перемещения и силы по загружениям, автозакрепления.</summary>
public sealed class FragmentCutBoundaryResult
{
    /// <summary>Закрепления узлов сетки: тег → биты DOF (как <see cref="FemMeshNodeSupport.Mask"/>), включая автозакрепления.</summary>
    public Dictionary<string, int> Supports { get; } = new(StringComparer.Ordinal);
    public List<FragmentPrescribedDof> Prescribed { get; } = [];
    public List<FragmentNodeForce> Loads { get; } = [];
    public List<FragmentMaskStageCase> StageCases { get; } = [];
    public List<FragmentGaugeDof> Gauge { get; } = [];
    /// <summary>Σ|F| сил масок по загружению, Н — масштаб проверки реакций автозакреплений.</summary>
    public Dictionary<int, double> ForceScale { get; } = [];
    /// <summary>Характерный размер разрезов, м: момент автозакрепления приводится к силе делением на него.</summary>
    public double GaugeLength { get; set; } = 1;
    public List<FemValidationDiagnostic> Diagnostics { get; } = [];
    public bool HasErrors => Diagnostics.Any(d => d.IsError);
}

/// <summary>
/// Сборка ГУ разрезов фрагмента (спека масок 5.1–5.3): режимы DOF по пресету и правкам, узлы сетки разрезов, значения
/// масок — по загружениям (маски загружений) или приращениями стадий (маски сочетаний: маска стадии k минус маска стадии
/// k−1 в загружении-приращении). Узел, общий для конца стержня и кромки, берёт режим и перемещение у стержня, силы кромки
/// к нему прибавляются; узел нескольких кромок — режим с приоритетом «закреплён» → «перемещение» → «сила» → «свободен».
/// При нехватке кинематических связей — автозакрепление минимальных DOF узлов с силовым режимом.
/// </summary>
public static class FragmentCutBoundary
{
    /// <summary>Допуск реакций автозакреплений к Σ|F| сил масок (спека 5.3).</summary>
    public const double GaugeReactionTolerance = 0.01;

    static readonly string[] DofNames = ["Ux", "Uy", "Uz", "Rx", "Ry", "Rz"];

    public static FragmentCutBoundaryResult Resolve(FragmentCutBoundaryInput input)
    {
        ArgumentNullException.ThrowIfNull(input);
        var r = new FragmentCutBoundaryResult();
        if (input.Cuts.Count == 0) return r;
        var nodeByTag = new Dictionary<string, FemMeshNode>(StringComparer.Ordinal);
        foreach (var n in input.MeshNodes) nodeByTag.TryAdd(n.NodeTag, n);

        // Режимы и узлы разрезов.
        var cuts = new List<CutState>();
        bool oblique = false;
        foreach (var (id, cut) in input.Cuts)
        {
            var modes = FragmentCutBc.Resolve(FragmentCutBc.PresetOf(cut.Cut, input.Preset, input.LateralPreset), cut.Cut,
                input.Overrides.GetValueOrDefault(id), cut.ShellNormal, out bool o);
            oblique |= o;
            var nodes = FragmentCutMeshNodes.Find(cut, input.MeshNodes, input.ToleranceM);
            string where = Where(cut.Cut);
            if (nodes.Error != null)
            {
                r.Diagnostics.Add(new("fragment_cut_mesh", $"Разрез {where}: {nodes.Error}.", true, [cut.Cut.MemberTag]));
                continue;
            }
            cuts.Add(new CutState(id, cut, modes, nodes, where));
        }
        if (oblique)
            r.Diagnostics.Add(new("fragment_cut_oblique_wall", "Стены не вдоль общих осей: поворот вокруг нормали стены не " +
                "выделяется одним DOF и задаётся по пресету.", false));
        // Фрагмент с призмой держится только силами масок и ничем не закреплён — отказ (спека призмы 3.2); при опорах и
        // пружинах фрагмента (колонна на фундаментной плите) он устойчив и без кинематики разрезов.
        if (cuts.Count > 0 && cuts.Any(c => c.Cut.Cut.Side == FragmentCutSide.Lateral) && input.OtherConstraints.Count == 0
            && !cuts.Any(c => c.Modes.Any(m => m is FragmentDofMode.Fixed or FragmentDofMode.MaskDisplacement)))
        {
            r.Diagnostics.Add(new("fragment_bc_all_static", "Все разрезы фрагмента — силовые, а опор у него нет: задайте " +
                "кинематику хотя бы на одном горизонтальном разрезе.", true));
            return r;
        }
        bool needsMask = cuts.Any(c => c.Modes.Any(FragmentCutBc.NeedsMask));
        if (needsMask && (input.Masks == null || input.Masks.Items.Count == 0))
        {
            r.Diagnostics.Add(new("fragment_masks_missing", "ГУ разрезов требуют маски, а они не сняты: снимите их командой " +
                "«Маски из родителя…» или выберите пресет «Изоляция».", true));
            return r;
        }
        if (r.HasErrors) return r;

        // Режим узла: стержень важнее кромки; кромки между собой — по приоритету режима.
        var nodeModes = new Dictionary<string, (FragmentDofMode[] Modes, CutState?[] Owner)>(StringComparer.Ordinal);
        foreach (var c in cuts.Where(c => c.Cut.Cut.Kind == FragmentCutKind.End))
            nodeModes[c.Nodes.EndNode!] = ((FragmentDofMode[])c.Modes.Clone(), Enumerable.Repeat<CutState?>(c, 6).ToArray());
        var barNodes = nodeModes.Keys.ToHashSet(StringComparer.Ordinal);
        foreach (var c in cuts.Where(c => c.Cut.Cut.Kind == FragmentCutKind.Edge))
            foreach (var tag in c.Nodes.Tags)
            {
                if (barNodes.Contains(tag)) continue;
                if (!nodeModes.TryGetValue(tag, out var m))
                {
                    nodeModes[tag] = ((FragmentDofMode[])c.Modes.Clone(), Enumerable.Repeat<CutState?>(c, 6).ToArray());
                    continue;
                }
                for (int d = 0; d < 6; d++)
                    if (Rank(c.Modes[d]) > Rank(m.Modes[d])) { m.Modes[d] = c.Modes[d]; m.Owner[d] = c; }
            }

        foreach (var (tag, (modes, _)) in nodeModes)
        {
            int bits = 0;
            for (int d = 0; d < 6; d++)
                if (modes[d] == FragmentDofMode.Fixed) bits |= 1 << d;
            if (bits != 0) r.Supports[tag] = bits;
        }

        if (needsMask) Values(input, r, cuts, nodeModes);
        if (r.HasErrors) return r;
        Gauge(input, r, nodeModes, nodeByTag);
        return r;
    }

    /// <summary>Значения масок по загружениям расчёта: заданные перемещения и силы узлов.</summary>
    static void Values(FragmentCutBoundaryInput input, FragmentCutBoundaryResult r, List<CutState> cuts,
        Dictionary<string, (FragmentDofMode[] Modes, CutState?[] Owner)> nodeModes)
    {
        var masks = input.Masks!;
        var byCut = masks.Items.GroupBy(i => i.CutId)
            .ToDictionary(g => g.Key, g => g.GroupBy(i => i.Key).ToDictionary(k => k.Key, k => k.First().Samples));
        bool stageKeys = masks.Items.Any(i => i.Key.Kind == FragmentMaskKeyKind.Stage);

        // Загружения расчёта: (Id, подпись, маска разреза или null).
        var cases = new List<(int Id, string Label, Func<CutState, IReadOnlyList<FragmentMaskSample>?> Samples)>();
        IReadOnlyList<FragmentMaskSample>? Of(CutState c, FragmentMaskKey key) =>
            byCut.TryGetValue(c.Id, out var keys) && keys.TryGetValue(key, out var s) ? s : null;
        if (!stageKeys)
        {
            foreach (int lc in input.Stages.SelectMany(s => s.Terms).Select(t => t.LoadCaseId).Distinct())
            {
                var key = FragmentMaskKey.OfLoadCase(lc);
                cases.Add((lc, $"загружение «{input.LoadCaseTags.GetValueOrDefault(lc, $"#{lc}")}»", c => Of(c, key)));
            }
        }
        else
        {
            var accumulated = new Dictionary<int, double>();
            FragmentMaskKey? previous = null;
            for (int k = 0; k < input.Stages.Count; k++)
            {
                var stage = input.Stages[k];
                if (stage.Terms.Count == 0) continue;
                foreach (var (lc, f) in stage.Terms) accumulated[lc] = accumulated.GetValueOrDefault(lc) + f;
                var key = FragmentMaskKey.OfStageTerms(accumulated.Select(kv => (kv.Key, kv.Value)));
                var before = previous;
                int id = -(k + 1);
                r.StageCases.Add(new FragmentMaskStageCase(k, id, $"Маски стадии «{stage.Tag}»"));
                cases.Add((id, $"стадия «{stage.Tag}»", c =>
                {
                    var now = Of(c, key);
                    if (now == null || before == null) return now;
                    var prev = Of(c, before);
                    return prev == null ? null : FragmentMaskDistribution.Combine([(now, 1.0), (prev, -1.0)]);
                }));
                previous = key;
            }
        }

        var reported = new HashSet<(int, int, int)>();
        void Missing(CutState c, int dof, int caseId, string label, string what)
        {
            if (!reported.Add((c.Id, dof, caseId))) return;
            r.Diagnostics.Add(new("fragment_mask_missing", $"Разрез {c.Where}, DOF {DofNames[dof]}, {label}: {what}.", true,
                [c.Cut.Cut.MemberTag]));
        }

        foreach (var (caseId, label, samplesOf) in cases)
        {
            var samples = cuts.ToDictionary(c => c, samplesOf);
            // Заданные перемещения — по владельцу DOF узла.
            foreach (var (tag, (modes, owner)) in nodeModes)
                for (int d = 0; d < 6; d++)
                {
                    if (modes[d] != FragmentDofMode.MaskDisplacement) continue;
                    var c = owner[d]!;
                    if (samples[c] is not { } s) { Missing(c, d, caseId, label, "нет маски"); continue; }
                    if (DisplacementAt(c, s, tag) is not { } u) { Missing(c, d, caseId, label, "в маске нет перемещений"); continue; }
                    r.Prescribed.Add(new FragmentPrescribedDof(caseId, tag, d, u[d]));
                }
            // Силы — от каждого разреза по его режиму (кромка прибавляет и к узлу стержня).
            var forces = new Dictionary<string, double[]>(StringComparer.Ordinal);
            double scale = 0;
            foreach (var c in cuts)
            {
                if (!c.Modes.Any(m => m == FragmentDofMode.MaskForce)) continue;
                int dof = Array.IndexOf(c.Modes, FragmentDofMode.MaskForce);
                if (samples[c] is not { } s) { Missing(c, dof, caseId, label, "нет маски"); continue; }
                Dictionary<string, Dof6>? nodal = c.Cut.Cut.Kind == FragmentCutKind.End
                    ? s.Count > 0 && s[0].F is { } endForce ? new(StringComparer.Ordinal) { [c.Nodes.EndNode!] = endForce } : null
                    : FragmentMaskDistribution.EdgeNodalForces(c.Cut.Cut, c.Nodes, s);
                if (nodal == null) { Missing(c, dof, caseId, label, "в маске нет сил"); continue; }
                foreach (var (tag, f) in nodal)
                {
                    if (!forces.TryGetValue(tag, out var v)) forces[tag] = v = new double[6];
                    for (int d = 0; d < 6; d++)
                        if (c.Modes[d] == FragmentDofMode.MaskForce) v[d] += f[d];
                    scale += new PlanarVector3(c.Modes[0] == FragmentDofMode.MaskForce ? f.X : 0,
                        c.Modes[1] == FragmentDofMode.MaskForce ? f.Y : 0, c.Modes[2] == FragmentDofMode.MaskForce ? f.Z : 0).Length;
                }
            }
            foreach (var (tag, v) in forces)
                if (v.Any(x => x != 0)) r.Loads.Add(new FragmentNodeForce(caseId, tag, new Dof6(v[0], v[1], v[2], v[3], v[4], v[5])));
            r.ForceScale[caseId] = scale;
        }
    }

    static Dof6? DisplacementAt(CutState c, IReadOnlyList<FragmentMaskSample> samples, string tag)
    {
        if (c.Cut.Cut.Kind == FragmentCutKind.End) return samples.Count > 0 ? samples[0].U : null;
        var parts = FragmentMaskDistribution.SplitBySegments(c.Cut.Cut.Segments, samples);
        for (int i = 0; i < c.Nodes.Segments.Count; i++)
        {
            var node = c.Nodes.Segments[i].FirstOrDefault(n => n.Tag == tag);
            if (node != null) return FragmentMaskDistribution.DisplacementAt(parts[c.Nodes.SegmentIndices[i]], node.S);
        }
        return null;
    }

    /// <summary>
    /// Автозакрепление (спека 5.3): ранг шести жёстких смещений по прочим связям, закреплённым и заданным DOF разрезов;
    /// недостающее — DOF узлов с силовым режимом: сначала поступательные трёх разнесённых узлов, затем остальные.
    /// </summary>
    static void Gauge(FragmentCutBoundaryInput input, FragmentCutBoundaryResult r,
        Dictionary<string, (FragmentDofMode[] Modes, CutState?[] Owner)> nodeModes, Dictionary<string, FemMeshNode> nodeByTag)
    {
        static PlanarVector3 P(FemMeshNode n) => new(n.X, n.Y, n.Z);
        var constrained = new List<KinematicDofRef>(input.OtherConstraints);
        var forceNodes = new List<(string Tag, PlanarVector3 X, FragmentDofMode[] Modes)>();
        foreach (var (tag, (modes, _)) in nodeModes.OrderBy(kv => kv.Key, StringComparer.Ordinal))
        {
            if (!nodeByTag.TryGetValue(tag, out var n)) continue;
            for (int d = 0; d < 6; d++)
                if (modes[d] is FragmentDofMode.Fixed or FragmentDofMode.MaskDisplacement) constrained.Add(new(P(n), d));
            if (modes.Contains(FragmentDofMode.MaskForce)) forceNodes.Add((tag, P(n), modes));
        }
        var all = nodeModes.Keys.Where(nodeByTag.ContainsKey).Select(t => P(nodeByTag[t])).ToList();
        if (all.Count == 0) return;
        var origin = all.Aggregate(PlanarVector3.Zero, (s, p) => s + p) * (1.0 / all.Count);
        double length = all.Max(p => (p - origin).Length) * 2;
        if (length > 0) r.GaugeLength = length;

        // Порядок кандидатов: три разнесённых узла (дальний от центра, дальний от него, дальний от их прямой).
        var order = new List<int>();
        if (forceNodes.Count > 0)
        {
            int a = Enumerable.Range(0, forceNodes.Count).MaxBy(i => (forceNodes[i].X - origin).Length);
            int b = Enumerable.Range(0, forceNodes.Count).MaxBy(i => (forceNodes[i].X - forceNodes[a].X).Length);
            var ab = forceNodes[b].X - forceNodes[a].X;
            int c = Enumerable.Range(0, forceNodes.Count).MaxBy(i => ab.Cross(forceNodes[i].X - forceNodes[a].X).Length);
            foreach (int i in new[] { a, b, c }.Concat(Enumerable.Range(0, forceNodes.Count)))
                if (!order.Contains(i)) order.Add(i);
        }
        var candidates = new List<GaugeCandidate>();
        var tagOf = new Dictionary<GaugeCandidate, string>();
        foreach (var range in new[] { new[] { 0, 1, 2 }, new[] { 3, 4, 5 } })
            foreach (int i in order)
                foreach (int d in range)
                {
                    if (forceNodes[i].Modes[d] != FragmentDofMode.MaskForce) continue;
                    var g = new GaugeCandidate(false, forceNodes[i].X, d);
                    if (tagOf.TryAdd(g, forceNodes[i].Tag)) candidates.Add(g);
                }

        var result = RigidModeGauge.Complete(origin, length, constrained, candidates);
        foreach (var g in result.Added)
        {
            string tag = tagOf[g];
            r.Gauge.Add(new FragmentGaugeDof(tag, g.Dof));
            r.Supports[tag] = r.Supports.GetValueOrDefault(tag) | 1 << g.Dof;
        }
        if (result.Added.Count > 0)
            r.Diagnostics.Add(new("fragment_gauge", string.Create(CultureInfo.CurrentCulture,
                $"Силовые ГУ разрезов: жёсткие смещения фрагмента сняты автозакреплением {result.Added.Count} DOF " +
                $"({string.Join(", ", r.Gauge.Select(g => $"{g.MeshNodeTag}:{DofNames[g.Dof]}"))}); их реакции сверяются после расчёта."),
                false, r.Gauge.Select(g => g.MeshNodeTag).Distinct().ToList()));
        if (!result.Complete && nodeModes.Values.Any(m => m.Modes.Any(x => x is FragmentDofMode.Free or FragmentDofMode.MaskForce)))
            r.Diagnostics.Add(new("fragment_mechanism", "Связей разрезов и опор фрагмента не хватает на все шесть жёстких " +
                "смещений: расчёт может не найти равновесия — закрепите DOF разрезов.", false));
    }

    /// <summary>
    /// Сверка реакций автозакреплений после расчёта: Σ|R| по DOF автозакреплений против Σ|F| сил масок сочетания
    /// <paramref name="terms"/>; больше <see cref="GaugeReactionTolerance"/> — предупреждение «маска не уравновешена».
    /// <paramref name="reaction"/> — реакция (тег узла сетки, DOF), null — нет в результате.
    /// </summary>
    public static FemValidationDiagnostic? CheckGaugeReactions(FragmentCutBoundaryResult boundary,
        IReadOnlyList<(int LoadCaseId, double Factor)> terms, Func<string, int, double?> reaction, string label)
    {
        if (boundary.Gauge.Count == 0) return null;
        double scale = terms.Sum(t => Math.Abs(t.Factor) * boundary.ForceScale.GetValueOrDefault(t.LoadCaseId));
        double sum = 0, max = 0;
        FragmentGaugeDof? worst = null;
        foreach (var g in boundary.Gauge)
        {
            double v = Math.Abs(reaction(g.MeshNodeTag, g.Dof) ?? 0) / (g.Dof >= 3 ? boundary.GaugeLength : 1);
            sum += v;
            if (v > max) { max = v; worst = g; }
        }
        if (scale <= 0 || sum <= GaugeReactionTolerance * scale) return null;
        return new FemValidationDiagnostic("fragment_gauge_reaction", string.Create(CultureInfo.CurrentCulture,
            $"{label}: маска не уравновешена — реакции автозакреплений Σ|R| = {sum / 1e3:0.###} кН ({sum / scale:P1} от Σ|F| масок " +
            $"{scale / 1e3:0.###} кН), наибольшая — узел {worst!.MeshNodeTag}, {DofNames[worst.Dof]}."), false,
            [worst.MeshNodeTag]);
    }

    static int Rank(FragmentDofMode m) => m switch
    {
        FragmentDofMode.Fixed => 3, FragmentDofMode.MaskDisplacement => 2, FragmentDofMode.MaskForce => 1, _ => 0,
    };

    static string Where(FragmentCut c) => string.Create(CultureInfo.CurrentCulture,
        $"«{c.MemberTag}» ({FragmentCutLabel.Side(c)}, z = {c.Z:0.###})");

    sealed record CutState(int Id, FragmentMaskCutInput Cut, FragmentDofMode[] Modes, FragmentCutMeshNodes Nodes, string Where);
}
