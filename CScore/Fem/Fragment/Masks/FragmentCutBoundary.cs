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
    /// <summary>
    /// КЭ сетки фрагмента — для поиска частей, не связанных с остальной и ничем не закреплённых (спека 2026-11-06); пусто —
    /// поиск не выполняется.
    /// </summary>
    public IReadOnlyList<FemElement> MeshElements { get; init; } = [];
    /// <summary>Жёсткие тела и объединения перемещений сетки — связи частей помимо общих узлов КЭ.</summary>
    public IReadOnlyList<FemRigidBody> RigidBodies { get; init; } = [];
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
        cuts = FreeComponents(input, cuts, r);
        if (oblique)
            r.Diagnostics.Add(new("fragment_cut_oblique_wall", "Стены не вдоль общих осей: поворот вокруг нормали стены не " +
                "выделяется одним DOF и задаётся по пресету.", false));
        // Фрагмент с призмой держится только силами масок и ничем не закреплён — отказ (спека призмы 3.2); при опорах и
        // пружинах фрагмента (колонна на фундаментной плите) он устойчив и без кинематики разрезов.
        if (cuts.Count > 0 && cuts.Any(c => c.Cut.Cut.Side == FragmentCutSide.Lateral) && input.OtherConstraints.Count == 0
            && !cuts.Any(c => c.Modes.Any(m => m is FragmentDofMode.Fixed or FragmentDofMode.MaskDisplacement)))
        {
            // Горизонтальных разрезов может не быть (полоса не доходит до плит): тогда совет — боковые грани.
            bool horizontal = cuts.Any(c => c.Cut.Cut.Side != FragmentCutSide.Lateral);
            r.Diagnostics.Add(new("fragment_bc_all_static", "Все разрезы фрагмента — силовые, а опор у него нет: " +
                (horizontal
                    ? "задайте кинематику хотя бы на одном горизонтальном разрезе."
                    : "горизонтальных разрезов нет — задайте кинематику на боковых гранях (пресет «Изоляция» или маски)."), true));
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
        FollowRigidMasters(input, r, nodeModes);

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

    /// <summary>
    /// Ведомый узел жёсткого тела на разрезе следует за ведущим: по DOF маски тела закрепление и заданное перемещение у него
    /// снимаются (решатель не задаёт подчинённые DOF; тела SCAD у «Музея» лежат целиком в плоскости разреза), силы масок
    /// остаются. Ведущий без кинематики на том же DOF — предупреждение со списком ведомых.
    /// </summary>
    static void FollowRigidMasters(FragmentCutBoundaryInput input, FragmentCutBoundaryResult r,
        Dictionary<string, (FragmentDofMode[] Modes, CutState?[] Owner)> nodeModes)
    {
        static bool Kinematic(FragmentDofMode m) => m is FragmentDofMode.Fixed or FragmentDofMode.MaskDisplacement;
        var original = nodeModes.ToDictionary(kv => kv.Key, kv => (FragmentDofMode[])kv.Value.Modes.Clone(), StringComparer.Ordinal);
        var unanchored = new List<string>();
        foreach (var b in input.RigidBodies)
        {
            var master = original.GetValueOrDefault(b.MasterNodeTag);
            foreach (var s in b.SlaveNodeTags)
            {
                if (!nodeModes.TryGetValue(s, out var m)) continue;
                bool lost = false;
                for (int d = 0; d < 6; d++)
                {
                    if ((b.Mask & (1 << d)) == 0 || !Kinematic(m.Modes[d])) continue;
                    m.Modes[d] = FragmentDofMode.Free;
                    if (master == null || !Kinematic(master[d])) lost = true;
                }
                if (lost) unanchored.Add(s);
            }
        }
        if (unanchored.Count == 0) return;
        r.Diagnostics.Add(new("fragment_cut_rigid_slave", $"Узлы разрезов — ведомые узлы жёстких тел, ведущий узел которых не " +
            $"получает кинематики разреза: их закрепления и перемещения из масок сняты, они следуют за ведущим ({unanchored.Count}): " +
            $"{string.Join(", ", unanchored.Take(10))}{(unanchored.Count > 10 ? " …" : "")}.", false, [.. unanchored]));
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

    /// <summary>
    /// Части сетки, не связанные с остальной (общие узлы КЭ, жёсткие тела) и ничем не закреплённые (прочие связи,
    /// закреплённые или заданные DOF разрезов, упругое основание КЭ): при хоть одной закреплённой части разрезы свободных
    /// переводятся на пресет «Как в родителе» с теми же правками — кусок повторяет родителя, механизма нет
    /// (спека 2026-11-06). Нет закреплённых частей — режимы прежние.
    /// </summary>
    static List<CutState> FreeComponents(FragmentCutBoundaryInput input, List<CutState> cuts, FragmentCutBoundaryResult r)
    {
        if (input.MeshElements.Count == 0 || cuts.Count == 0) return cuts;
        var root = new Dictionary<string, string>(StringComparer.Ordinal);
        string Find(string x)
        {
            if (!root.TryGetValue(x, out var p)) return root[x] = x;
            return p == x ? x : root[x] = Find(p);
        }
        void Union(string a, string b)
        {
            string ra = Find(a), rb = Find(b);
            if (ra != rb) root[ra] = rb;
        }
        var elementsOf = new Dictionary<string, List<FemElement>>(StringComparer.Ordinal);
        var restrained = new HashSet<string>(StringComparer.Ordinal);
        var elementNodes = new List<(FemElement E, IReadOnlyList<string> Tags)>();
        foreach (var e in input.MeshElements)
        {
            if (FemMeshTopology.ReadNodeTags(e) is not { Count: > 0 } tags) continue;
            elementNodes.Add((e, tags));
            for (int i = 1; i < tags.Count; i++) Union(tags[0], tags[i]);
            Find(tags[0]);
        }
        foreach (var b in input.RigidBodies)
            foreach (var s in b.SlaveNodeTags)
                if (root.ContainsKey(b.MasterNodeTag) || root.ContainsKey(s)) Union(b.MasterNodeTag, s);
        foreach (var (e, tags) in elementNodes)
        {
            string c = Find(tags[0]);
            if (!elementsOf.TryGetValue(c, out var list)) elementsOf[c] = list = [];
            list.Add(e);
            if (e.FoundationC1 is > 0) restrained.Add(c);
        }
        if (elementsOf.Count < 2) return cuts;

        // Узлы разрезов с закреплёнными или заданными DOF.
        IEnumerable<string> CutNodes(CutState c) =>
            c.Cut.Cut.Kind == FragmentCutKind.End ? c.Nodes.EndNode is { } n ? [n] : [] : c.Nodes.Tags;
        foreach (var c in cuts)
            if (c.Modes.Any(m => m is FragmentDofMode.Fixed or FragmentDofMode.MaskDisplacement))
                foreach (var n in CutNodes(c))
                    if (root.ContainsKey(n)) restrained.Add(Find(n));
        // Прочие связи — по положению узла сетки в допуске.
        if (input.OtherConstraints.Count > 0)
        {
            double h = Math.Max(input.ToleranceM, 1e-9);
            (long, long, long) Cell(double x, double y, double z) =>
                ((long)Math.Floor(x / h), (long)Math.Floor(y / h), (long)Math.Floor(z / h));
            var grid = new Dictionary<(long, long, long), List<FemMeshNode>>();
            foreach (var n in input.MeshNodes)
            {
                if (!root.ContainsKey(n.NodeTag)) continue;
                var k = Cell(n.X, n.Y, n.Z);
                if (!grid.TryGetValue(k, out var list)) grid[k] = list = [];
                list.Add(n);
            }
            foreach (var p in input.OtherConstraints.Select(o => o.Point).Distinct())
            {
                var (cx, cy, cz) = Cell(p.X, p.Y, p.Z);
                for (long i = cx - 1; i <= cx + 1; i++)
                    for (long j = cy - 1; j <= cy + 1; j++)
                        for (long k = cz - 1; k <= cz + 1; k++)
                            if (grid.TryGetValue((i, j, k), out var list))
                                foreach (var n in list)
                                    if ((new PlanarVector3(n.X, n.Y, n.Z) - p).Length <= h) restrained.Add(Find(n.NodeTag));
            }
        }
        if (restrained.Count == 0) return cuts;

        var free = elementsOf.Keys.Where(c => !restrained.Contains(c)).ToHashSet(StringComparer.Ordinal);
        if (free.Count == 0) return cuts;
        var result = new List<CutState>(cuts.Count);
        var switched = new HashSet<string>(StringComparer.Ordinal);
        foreach (var c in cuts)
        {
            var hit = CutNodes(c).Where(n => root.ContainsKey(n)).Select(Find).Where(free.Contains).ToList();
            if (hit.Count == 0)
            {
                result.Add(c);
                continue;
            }
            switched.UnionWith(hit);
            var modes = FragmentCutBc.Resolve(FragmentBcPreset.Kinematic, c.Cut.Cut, input.Overrides.GetValueOrDefault(c.Id),
                c.Cut.ShellNormal, out _);
            result.Add(c with { Modes = modes });
        }
        foreach (var comp in switched)
        {
            var elements = elementsOf[comp];
            var members = elements.Select(e => e.SourceMemberTag).OfType<string>().Distinct(StringComparer.Ordinal).ToList();
            var keys = members.Count > 0 ? members : elements.Select(e => e.ElemTag).Take(10).ToList();
            string names = members.Count == 0 ? ""
                : " (КонЭ " + string.Join(", ", members.Take(5).Select(m => "«" + m + "»")) + (members.Count > 5 ? ", …" : "") + ")";
            r.Diagnostics.Add(new("fragment_component_free",
                $"Часть сетки фрагмента не связана с остальной и ничем не закреплена: {elements.Count} КЭ{names}" +
                " — её разрезы переведены на перемещения из родителя (пресет «Как в родителе»).", false, keys));
        }
        return result;
    }

    static int Rank(FragmentDofMode m) => m switch
    {
        FragmentDofMode.Fixed => 3, FragmentDofMode.MaskDisplacement => 2, FragmentDofMode.MaskForce => 1, _ => 0,
    };

    static string Where(FragmentCut c) => string.Create(CultureInfo.CurrentCulture,
        $"«{c.MemberTag}» ({FragmentCutLabel.Side(c)}, z = {c.Z:0.###})");

    sealed record CutState(int Id, FragmentMaskCutInput Cut, FragmentDofMode[] Modes, FragmentCutMeshNodes Nodes, string Where);
}
