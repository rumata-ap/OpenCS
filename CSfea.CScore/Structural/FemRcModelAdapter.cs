using System.Globalization;
using CScore;
using CScore.Fem;
using CScore.Fem.Loads;
using CScore.Planar;
using CSfea.Core;

namespace CSfea.CScoreBridge.Structural;

/// <summary>
/// Сечение пластинчатого КЭ из источника армирования (тот же, что в проверке по КЭ) с диаграммами материалов;
/// <see cref="MaterialsKey"/> различает одинаковое армирование с разными диаграммами.
/// </summary>
public sealed record FemRcPlateSection(PlateElementSection Section, PlateSectionMaterials Materials, string MaterialsKey);

/// <summary>Сечение CScore стержня (материалы привязаны, диаграммы построены), ключ одинаковых сечений и GJ, Н·м².</summary>
public sealed record FemRcBeamCross(CrossSection Section, string Key, double TorsionGJ);

/// <summary>
/// Стадия нагружения: приращение нагрузки — загружения схемы (<see cref="FemLoadCase.Id"/>) с коэффициентами, дробится
/// на <see cref="Steps"/> шагов.
/// </summary>
public sealed record FemRcStage(string Tag, IReadOnlyList<(int LoadCaseId, double Factor)> Terms, int Steps = 1);

/// <summary>Суммарная нагрузка стадии в глобальных осях, Н.</summary>
public sealed record FemRcStageTotal(string Tag, double Fx, double Fy, double Fz);

/// <summary>Вход адаптера: уже загруженная схема FEM (оба уровня), свойства и сечения КЭ, стадии.</summary>
public sealed class FemRcModelInput
{
    /// <summary>Узлы сетки (теги — целые числа).</summary>
    public required IReadOnlyList<FemMeshNode> MeshNodes { get; init; }
    /// <summary>КЭ сетки (стержни и пластины; четырёхугольник хранится «1 2 4 3»).</summary>
    public required IReadOnlyList<FemElement> MeshElements { get; init; }
    /// <summary>Узлы конструктивного уровня (закрепления, узловые нагрузки).</summary>
    public IReadOnlyList<FemNode> Nodes { get; init; } = [];
    /// <summary>Конструктивные элементы (повороты сечений, нагрузки на стержни).</summary>
    public IReadOnlyList<FemMember> Members { get; init; } = [];
    /// <summary>Группы схемы (цели нагрузок на КЭ).</summary>
    public IReadOnlyList<FemMemberGroup> Groups { get; init; } = [];

    public IReadOnlyList<FemMeshNodeSupport> Supports { get; init; } = [];
    public IReadOnlyList<FemSpring> Springs { get; init; } = [];
    public IReadOnlyList<FemRigidBody> RigidBodies { get; init; } = [];

    public required IReadOnlyList<FemLoadCase> LoadCases { get; init; }
    public IReadOnlyList<FemNodeLoad> NodeLoads { get; init; } = [];
    public IReadOnlyList<FemMemberLoad> MemberLoads { get; init; } = [];
    public IReadOnlyList<FemElementLoad> ElementLoads { get; init; } = [];
    public IReadOnlyList<FemMeshNodeLoad> MeshNodeLoads { get; init; } = [];
    public IReadOnlyList<FemKinematicLoad> KinematicLoads { get; init; } = [];

    /// <summary>Упругие свойства КЭ без нелинейного сечения и данные собственного веса; null — нет.</summary>
    public IFemElementStiffnessSource? Properties { get; init; }

    /// <summary>Сечение пластинчатого КЭ из источника армирования; null или сечение без армирования — упругий КЭ.</summary>
    public Func<FemElement, FemRcPlateSection?>? PlateSection { get; init; }

    /// <summary>Сечение CScore стержневого КЭ; null — упругий по <see cref="Properties"/>.</summary>
    public Func<FemElement, FemRcBeamCross?>? BeamSection { get; init; }

    /// <summary>Вид расчёта для сечений CScore стержней.</summary>
    public CalcType Calc { get; init; } = CalcType.N;

    /// <summary>Ось x выдачи усилий пластин своей схемы (ось области) по тегу конструктивного элемента.</summary>
    public IReadOnlyDictionary<string, PlanarVector3>? RegionAxisX { get; init; }

    /// <summary>Учитываются ли сдвиговые деформации по хомутам (только для отчёта о КЭ без хомутов).</summary>
    public bool BeamShear { get; init; } = true;

    public required IReadOnlyList<FemRcStage> Stages { get; init; }
}

/// <summary>Итог адаптации: модель (номера узлов и КЭ — теги сетки), диагностики, суммарные нагрузки стадий.</summary>
public sealed record FemRcModelResult(RcStructuralModel Model, IReadOnlyList<FemValidationDiagnostic> Diagnostics,
    IReadOnlyList<FemRcStageTotal> StageTotals)
{
    /// <summary>Есть ошибки — модель считать нельзя.</summary>
    public bool HasErrors => Diagnostics.Any(d => d.IsError);

    /// <summary>Строки отчёта: ошибки, затем предупреждения и сведения.</summary>
    public IEnumerable<string> Report => Diagnostics.OrderByDescending(d => d.IsError)
        .Select(d => (d.IsError ? "Ошибка: " : "") + d.Message);
}

/// <summary>
/// Адаптер схемы FEM OpenCS (своей или импортированной) → <see cref="RcStructuralModel"/>. Решатель берёт сетку:
/// пластины — Q4/T3 (хранение «1 2 4 3» → обход контура), ось x сечения — ось выдачи усилий (угол
/// <see cref="FemElement.LocalAxisAngleDeg"/> или ось области), повёрнутая в оси армирования на −ForceAngleDeg;
/// стержни — сечение CScore или упругие свойства, оси — <see cref="BeamLocalAxisConvention"/> с поворотом КонЭ;
/// ГУ — <see cref="FemBoundaryResolver"/>; нагрузки — узловые силы (<see cref="FemLoadCaseNodalForces"/>, узловые
/// нагрузки КонЭ, нагрузки стержней КонЭ через <see cref="FemMemberLoadSegmenter"/>), у стержней с шарнирами — концевые
/// силы КЭ (<see cref="RcBeamEndLoad"/>, конденсируются в ядре). Ошибки — в диагностики, исключений по данным нет.
/// </summary>
public static class FemRcModelAdapter
{
    public static FemRcModelResult Adapt(FemRcModelInput input)
    {
        ArgumentNullException.ThrowIfNull(input);
        var diag = new List<FemValidationDiagnostic>();
        var model = new RcStructuralModel();

        // Узлы сетки: теги — целые числа (NodeIdsJson КЭ хранит их числами).
        var nodesByTag = new Dictionary<string, FemMeshNode>(StringComparer.Ordinal);
        var badTags = new List<string>();
        foreach (var n in input.MeshNodes)
        {
            if (FemMeshTopology.CanonicalNodeTag(n.NodeTag) is not { } tag) { badTags.Add(n.NodeTag); continue; }
            nodesByTag.TryAdd(tag, n);
        }
        Add(diag, "node_tag_not_numeric", "Узлы сетки с нечисловым тегом", badTags, true);

        var boundary = FemBoundaryResolver.Resolve(input.Nodes, input.MeshNodes, input.MeshElements, input.Supports,
            input.Springs, input.RigidBodies);
        diag.AddRange(boundary.Diagnostics);

        var memberByTag = new Dictionary<string, FemMember>(StringComparer.Ordinal);
        foreach (var m in input.Members) memberByTag.TryAdd(m.ElemTag, m);

        var used = new HashSet<int>();
        var badElements = new List<string>();
        AddShells(input, model, nodesByTag, memberByTag, boundary, used, badElements, diag);
        AddBeams(input, model, nodesByTag, memberByTag, boundary, used, badElements, diag);
        Add(diag, "element_topology", "КЭ с неизвестными узлами, нечисловым тегом или не 2–4 узлами", badElements, true);

        int bodyId = 0;
        foreach (var b in boundary.RigidBodies)
        {
            bodyId++;
            int id = int.TryParse(b.SourceElemTag, NumberStyles.Integer, CultureInfo.InvariantCulture, out int src) ? src : bodyId;
            int master = int.Parse(b.MasterNodeTag, CultureInfo.InvariantCulture);
            var slaves = b.SlaveNodeTags.Select(t => int.Parse(t, CultureInfo.InvariantCulture)).ToArray();
            model.RigidBodies.Add(new RcRigidBody(id, master, slaves, b.Mask));
            used.Add(master);
            used.UnionWith(slaves);
        }

        foreach (var (tag, n) in nodesByTag.OrderBy(kv => int.Parse(kv.Key, CultureInfo.InvariantCulture)))
        {
            int id = int.Parse(tag, CultureInfo.InvariantCulture);
            if (used.Contains(id)) model.Nodes.Add(new RcNode(id, n.X, n.Y, n.Z));
        }
        var orphanSupports = new List<string>();
        foreach (var (tag, mask) in boundary.SupportMasks)
        {
            int id = int.Parse(tag, CultureInfo.InvariantCulture);
            if (used.Contains(id)) model.Supports.Add(new RcSupport(id, mask));
            else orphanSupports.Add(tag);
        }
        Add(diag, "support_without_elements", "Закрепления узлов без КЭ не учитываются", orphanSupports, false);
        var orphanSprings = new List<string>();
        foreach (var s in boundary.Springs)
        {
            int id = int.Parse(s.MeshNodeTag, CultureInfo.InvariantCulture);
            if (!used.Contains(id)) { orphanSprings.Add(s.MeshNodeTag); continue; }
            for (int d = 0; d < 6; d++)
                if (s.Stiffnesses[d] != 0) model.Springs.Add(new RcSpring(id, d, s.Stiffnesses[d]));
        }
        Add(diag, "spring_without_elements", "Пружины узлов без КЭ не учитываются", orphanSprings, false);

        var totals = AddLoads(input, model, nodesByTag, memberByTag, used, diag);
        return new FemRcModelResult(model, diag, totals);
    }

    // ---------------------------------------------------------------- пластины

    static void AddShells(FemRcModelInput input, RcStructuralModel model, IReadOnlyDictionary<string, FemMeshNode> nodes,
        IReadOnlyDictionary<string, FemMember> members, FemResolvedBoundary boundary, HashSet<int> used,
        List<string> badElements, List<FemValidationDiagnostic> diag)
    {
        var sections = new Dictionary<string, RcShellSection>(StringComparer.Ordinal);
        var noSection = new List<string>();
        var elasticFallback = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        int onFoundation = 0, reinforced = 0;
        foreach (var e in input.MeshElements.Where(e => e.ElemType == "shell"))
        {
            var g = FemElementGeometry.Of(e, nodes);
            if (g is not { IsShell: true } || !int.TryParse(e.ElemTag, NumberStyles.Integer, CultureInfo.InvariantCulture, out int id))
            {
                badElements.Add(e.ElemTag);
                continue;
            }
            int[] contour = g.Contour.Select(i => int.Parse(g.NodeTags[i], CultureInfo.InvariantCulture)).ToArray();

            RcShellSection? section = null;
            double forceAngle = 0;
            var plate = input.PlateSection?.Invoke(e);
            if (plate?.Section.Section is { } ps)
            {
                string key = $"rc|{plate.Section.RebarKey}|{plate.MaterialsKey}";
                if (!sections.TryGetValue(key, out section))
                    sections[key] = section = new RcShellSection(key) { Plate = ps, PlateMaterials = plate.Materials };
                forceAngle = plate.Section.ForceAngleDeg;
                reinforced++;
            }
            else if (input.Properties?.Shell(e) is { } el)
            {
                string key = FormattableString.Invariant($"elastic|{el.E:R}|{el.Nu:R}|{el.H:R}");
                if (!sections.TryGetValue(key, out section))
                    sections[key] = section = new RcShellSection(key)
                    {
                        Elastic = new Laminate(new[] { new Ply(new OrthotropicMaterial(el.E, el.E, el.Nu, el.E / (2 * (1 + el.Nu))), 0.0, el.H) }),
                    };
                if (plate != null)
                {
                    string why = plate.Section.Reason ?? "нет армирования";
                    if (!elasticFallback.TryGetValue(why, out var list)) elasticFallback[why] = list = [];
                    list.Add(e.ElemTag);
                }
            }
            if (section == null) { noSection.Add(e.ElemTag); continue; }

            var axis = ForceAxis(e, g, members, input.RegionAxisX);
            if (forceAngle != 0) axis = Rotate(axis, g.ShellFrame().Z, -forceAngle);
            double? c1 = boundary.FoundationC1.TryGetValue(e.ElemTag, out double k) ? k : null;
            if (c1 != null) onFoundation++;
            model.Shells.Add(new RcShell(id, contour, section, [axis.X, axis.Y, axis.Z], c1));
            used.UnionWith(contour);
        }
        Add(diag, "shell_no_section",
            "Пластины без сечения: нет армирования из источника и упругих свойств (толщина, E, ν)", noSection, true);
        foreach (var (why, tags) in elasticFallback)
            Add(diag, "shell_elastic_fallback", $"Пластины без армирования ({why}) — упругие по жёсткости", tags, false);
        if (reinforced > 0) Info(diag, "shell_reinforced", $"ЖБ-пластин с армированием: {reinforced}; сечений: " +
            $"{sections.Keys.Count(k => k.StartsWith("rc|", StringComparison.Ordinal))}.");
        if (onFoundation > 0) Info(diag, "shell_foundation", $"Упругое основание C1: {onFoundation} пластин.");
    }

    /// <summary>
    /// Ось x выдачи усилий пластины: «узел 1 → узел 2», повёрнутая на <see cref="FemElement.LocalAxisAngleDeg"/>
    /// вокруг нормали; без угла — ось области конструктивного элемента, спроецированная на плоскость КЭ.
    /// </summary>
    static PlanarVector3 ForceAxis(FemElement e, FemElementGeometry g, IReadOnlyDictionary<string, FemMember> members,
        IReadOnlyDictionary<string, PlanarVector3>? regionAxes)
    {
        var (x1, _, n) = g.ShellFrame();
        if (e.LocalAxisAngleDeg is { } angle) return Rotate(x1, n, angle);
        if (e.SourceMemberTag is { } tag && members.ContainsKey(tag) && regionAxes?.GetValueOrDefault(tag) is { } a)
        {
            var p = a - n * a.Dot(n);
            if (p.Length > 1e-9) return p.Normalize();
        }
        return x1;
    }

    /// <summary>Поворот вектора в плоскости КЭ вокруг единичной нормали на угол, град (против часовой с конца нормали).</summary>
    static PlanarVector3 Rotate(PlanarVector3 v, PlanarVector3 normal, double deg)
    {
        double a = deg * Math.PI / 180;
        return (v * Math.Cos(a) + normal.Cross(v) * Math.Sin(a)).Normalize();
    }

    // ---------------------------------------------------------------- стержни

    static void AddBeams(FemRcModelInput input, RcStructuralModel model, IReadOnlyDictionary<string, FemMeshNode> nodes,
        IReadOnlyDictionary<string, FemMember> members, FemResolvedBoundary boundary, HashSet<int> used,
        List<string> badElements, List<FemValidationDiagnostic> diag)
    {
        var sections = new Dictionary<string, RcBeamSection>(StringComparer.Ordinal);
        var noSection = new List<string>();
        var noStirrups = new List<string>();
        var degenerate = new List<string>();
        var noAxes = new List<string>();
        int released = 0, cscore = 0;
        foreach (var e in input.MeshElements.Where(e => e.ElemType == "beam"))
        {
            var g = FemElementGeometry.Of(e, nodes);
            if (g is not { IsBar: true } || !int.TryParse(e.ElemTag, NumberStyles.Integer, CultureInfo.InvariantCulture, out int id))
            {
                badElements.Add(e.ElemTag);
                continue;
            }
            if (e.BeamRotationDeg == null && e.Origin == FemMember.MeshSourceImported) noAxes.Add(e.ElemTag);
            PlanarVector3 refVec;
            try { refVec = BeamLocalAxisConvention.Frame(g.Points[0], g.Points[1], Rotation(e, members)).Y; }
            catch (InvalidOperationException) { degenerate.Add(e.ElemTag); continue; }

            var cross = input.BeamSection?.Invoke(e);
            var bar = input.Properties?.Bar(e);
            if (cross == null && bar == null) { noSection.Add(e.ElemTag); continue; }
            var lin = bar == null ? null : new BeamSection(bar.E, bar.A, bar.Iy, bar.Iz, bar.J, bar.G);
            string key = cross?.Key ?? "-";
            if (bar != null) key += FormattableString.Invariant($"|{bar.E:R}|{bar.G:R}|{bar.A:R}|{bar.Iy:R}|{bar.Iz:R}|{bar.J:R}");
            if (!sections.TryGetValue(key, out var section))
                sections[key] = section = new RcBeamSection(key)
                {
                    Elastic = lin,
                    Cross = cross?.Section,
                    Calc = input.Calc,
                    TorsionGJ = cross?.TorsionGJ ?? (bar == null ? 0 : bar.G * bar.J),
                };
            if (cross != null)
            {
                cscore++;
                if (input.BeamShear && !cross.Section.Areas.Any(a => a.Stirrups.Count > 0)) noStirrups.Add(e.ElemTag);
            }
            var (ri, rj) = boundary.Releases.GetValueOrDefault(e.ElemTag);
            if (ri != 0 || rj != 0) released++;
            int ni = int.Parse(g.NodeTags[0], CultureInfo.InvariantCulture), nj = int.Parse(g.NodeTags[1], CultureInfo.InvariantCulture);
            model.Beams.Add(new RcBeam(id, ni, nj, section, [refVec.X, refVec.Y, refVec.Z], ri, rj));
            used.Add(ni);
            used.Add(nj);
        }
        Add(diag, "beam_no_section", "Стержни без сечения CScore и без упругих свойств жёсткости", noSection, true);
        Add(diag, "beam_degenerate", "Стержни нулевой длины", degenerate, true);
        Add(diag, "beam_axes_unknown", "Стержни импорта без прочитанных местных осей (поворот сечения 0; дочитайте граничные условия)",
            noAxes, false);
        Add(diag, "beam_no_stirrups", "Стержни с сечением CScore без хомутов: сдвиг упругий", noStirrups, false);
        if (cscore > 0) Info(diag, "beam_cscore", $"Стержней с сечением CScore: {cscore}.");
        if (released > 0) Info(diag, "beam_releases", $"Стержней с шарнирами: {released}.");
    }

    /// <summary>Поворот сечения стержня вокруг оси, град: у импортного КЭ — свой (<see cref="FemElement.BeamRotationDeg"/>),
    /// у КЭ своей схемы — поворот конструктивного элемента; не задан — 0.</summary>
    static double Rotation(FemElement e, IReadOnlyDictionary<string, FemMember> members) =>
        e.BeamRotationDeg ?? (e.SourceMemberTag is { } tag && members.TryGetValue(tag, out var m) ? m.RotationDeg : 0);

    // ---------------------------------------------------------------- нагрузки

    static List<FemRcStageTotal> AddLoads(FemRcModelInput input, RcStructuralModel model,
        IReadOnlyDictionary<string, FemMeshNode> nodes, IReadOnlyDictionary<string, FemMember> members, HashSet<int> used,
        List<FemValidationDiagnostic> diag)
    {
        var caseById = input.LoadCases.GroupBy(c => c.Id).ToDictionary(g => g.Key, g => g.First());
        var needed = input.Stages.SelectMany(s => s.Terms.Select(t => t.LoadCaseId)).Distinct().ToList();
        var missing = needed.Where(id => !caseById.ContainsKey(id)).ToList();
        if (missing.Count > 0)
            diag.Add(new("stage_load_case_missing", $"Стадии ссылаются на отсутствующие загружения: {string.Join(", ", missing)}.", true));
        foreach (var st in input.Stages.Where(s => s.Terms.Count == 0))
            diag.Add(new("stage_empty", $"Стадия «{st.Tag}» без загружений.", true));

        var beamById = model.Beams.ToDictionary(b => b.Id);
        var releasedTags = model.Beams.Where(b => b.ReleaseI != 0 || b.ReleaseJ != 0)
            .Select(b => b.Id.ToString(CultureInfo.InvariantCulture)).ToHashSet(StringComparer.Ordinal);
        var mesh = new FemLoadMeshContext(input.MeshNodes, input.MeshElements, input.Groups, input.Properties,
            members.ToDictionary(kv => kv.Key, kv => kv.Value.RotationDeg, StringComparer.Ordinal));
        var meshNodeBySource = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var n in input.MeshNodes)
            if (n.SourceNodeTag is { } s) meshNodeBySource.TryAdd(s, n.NodeTag);
        var nodeById = input.Nodes.GroupBy(n => n.Id).ToDictionary(g => g.Key, g => g.First());
        var nodeByTag = new Dictionary<string, FemNode>(StringComparer.Ordinal);
        foreach (var n in input.Nodes) nodeByTag.TryAdd(n.NodeTag, n);

        foreach (int caseId in needed.Where(caseById.ContainsKey))
        {
            var lc = caseById[caseId];
            var perBeam = new Dictionary<string, Dictionary<string, double[]>>(StringComparer.Ordinal);
            Dictionary<string, double[]>? Sink(FemElement e)
            {
                if (!releasedTags.Contains(e.ElemTag)) return null;
                if (!perBeam.TryGetValue(e.ElemTag, out var d)) perBeam[e.ElemTag] = d = new(StringComparer.Ordinal);
                return d;
            }

            // Сеточный уровень и собственный вес.
            var resolved = FemLoadCaseNodalForces.Resolve([(lc, 1.0)], input.ElementLoads, input.MeshNodeLoads, mesh, Sink);
            diag.AddRange(resolved.Diagnostics);
            var forces = new Dictionary<string, double[]>(StringComparer.Ordinal);
            foreach (var f in resolved.Forces) AddForce(forces, f.NodeTag, new(f.Fx, f.Fy, f.Fz), new(f.Mx, f.My, f.Mz));

            // Узловые нагрузки конструктивного уровня.
            var lostNodeLoads = new List<string>();
            foreach (var l in input.NodeLoads.Where(l => l.LoadCaseId == caseId))
            {
                if (!nodeById.TryGetValue(l.NodeId, out var n) || !meshNodeBySource.TryGetValue(n.NodeTag, out var tag))
                {
                    lostNodeLoads.Add(nodeById.GetValueOrDefault(l.NodeId)?.NodeTag ?? $"#{l.NodeId}");
                    continue;
                }
                AddForce(forces, tag, new(l.Fx, l.Fy, l.Fz), new(l.Mx, l.My, l.Mz));
            }
            Add(diag, "node_load_not_in_mesh", $"Загружение «{lc.Tag}»: узловые нагрузки на узлы без узла сетки", lostNodeLoads, true);

            // Нагрузки стержней конструктивного уровня — по КЭ.
            var memberLoads = input.MemberLoads.Where(l => l.LoadCaseId == caseId).ToList();
            if (memberLoads.Count > 0)
                AddMemberLoads(lc, memberLoads, input, mesh, members, nodeByTag, forces, Sink, diag);

            var kinematic = input.KinematicLoads.Where(l => l.LoadCaseId == caseId).ToList();
            if (kinematic.Count > 0)
                diag.Add(new("kinematic_not_supported",
                    $"Загружение «{lc.Tag}»: кинематические нагрузки ({kinematic.Count}) в расчёте CSfea не поддерживаются.", true));

            var rc = new RcLoadCase(lc.Id, lc.Tag);
            var hanging = new List<string>();
            foreach (var (tag, v) in forces.OrderBy(kv => int.TryParse(kv.Key, out int x) ? x : int.MaxValue))
            {
                if (v.All(c => c == 0)) continue;
                if (!int.TryParse(tag, NumberStyles.Integer, CultureInfo.InvariantCulture, out int id) || !used.Contains(id))
                {
                    hanging.Add(tag);
                    continue;
                }
                rc.Nodal.Add(new RcNodalLoad(id, v));
            }
            Add(diag, "load_on_free_node", $"Загружение «{lc.Tag}»: нагрузки на узлы без КЭ модели не учтены", hanging, false);
            foreach (var (tag, d) in perBeam.OrderBy(kv => int.Parse(kv.Key, CultureInfo.InvariantCulture)))
            {
                var b = beamById[int.Parse(tag, CultureInfo.InvariantCulture)];
                var end = new double[12];
                string ti = b.NodeI.ToString(CultureInfo.InvariantCulture), tj = b.NodeJ.ToString(CultureInfo.InvariantCulture);
                if (d.TryGetValue(ti, out var fi)) Array.Copy(fi, 0, end, 0, 6);
                if (d.TryGetValue(tj, out var fj)) Array.Copy(fj, 0, end, 6, 6);
                if (end.Any(c => c != 0)) rc.BeamEnds.Add(new RcBeamEndLoad(b.Id, end));
            }
            model.LoadCases.Add(rc);
        }

        var totals = new List<FemRcStageTotal>();
        var rcById = model.LoadCases.ToDictionary(c => c.Id);
        foreach (var st in input.Stages)
        {
            double fx = 0, fy = 0, fz = 0;
            foreach (var (id, k) in st.Terms)
            {
                if (!rcById.TryGetValue(id, out var c)) continue;
                foreach (var p in c.Nodal) { fx += k * p.Force[0]; fy += k * p.Force[1]; fz += k * p.Force[2]; }
                foreach (var p in c.BeamEnds)
                {
                    fx += k * (p.Forces[0] + p.Forces[6]); fy += k * (p.Forces[1] + p.Forces[7]); fz += k * (p.Forces[2] + p.Forces[8]);
                }
            }
            model.Stages.Add(new RcStage(st.Tag, st.Terms.Where(t => rcById.ContainsKey(t.LoadCaseId)).ToList(), Math.Max(1, st.Steps)));
            totals.Add(new FemRcStageTotal(st.Tag, fx, fy, fz));
        }
        return totals;
    }

    /// <summary>
    /// Нагрузки стержней конструктивного уровня (<see cref="FemMemberLoadSegmenter"/>): куски по КЭ → согласованные
    /// узловые силы (трапеция и частичная — интегрированием, сосредоточенная — эрмитовы функции), в глобальных осях.
    /// </summary>
    static void AddMemberLoads(FemLoadCase lc, IReadOnlyList<FemMemberLoad> loads, FemRcModelInput input,
        FemLoadMeshContext mesh, IReadOnlyDictionary<string, FemMember> members, IReadOnlyDictionary<string, FemNode> nodeByTag,
        Dictionary<string, double[]> forces, Func<FemElement, Dictionary<string, double[]>?> sink,
        List<FemValidationDiagnostic> diag)
    {
        var seg = FemMemberLoadSegmenter.Segment(input.MeshNodes, input.MeshElements, input.Nodes, input.Members, loads);
        foreach (var err in seg.Errors)
            diag.Add(new("member_load_error", $"Загружение «{lc.Tag}»: {err.Message}", true,
                err.MemberTag == null ? null : [err.MemberTag]));

        var frames = new Dictionary<string, (PlanarVector3 X, PlanarVector3 Y, PlanarVector3 Z)?>(StringComparer.Ordinal);
        (PlanarVector3 X, PlanarVector3 Y, PlanarVector3 Z)? Frame(string memberTag)
        {
            if (frames.TryGetValue(memberTag, out var f)) return f;
            f = null;
            if (members.TryGetValue(memberTag, out var m) && m.Node1 is { } a && m.Node2 is { } b && m.Node3 == null
                && nodeByTag.TryGetValue(a.ToString(CultureInfo.InvariantCulture), out var na)
                && nodeByTag.TryGetValue(b.ToString(CultureInfo.InvariantCulture), out var nb))
                try { f = BeamLocalAxisConvention.Frame(new(na.X, na.Y, na.Z), new(nb.X, nb.Y, nb.Z), m.RotationDeg); }
                catch (InvalidOperationException) { }
            frames[memberTag] = f;
            return f;
        }
        PlanarVector3? Global(PlanarVector3 v, string cs, string memberTag)
        {
            if (!cs.Equals("local", StringComparison.OrdinalIgnoreCase)) return v;
            return Frame(memberTag) is { } f ? f.X * v.X + f.Y * v.Y + f.Z * v.Z : null;
        }

        var noFrame = new List<string>();
        foreach (var p in seg.Distributed)
        {
            if (!mesh.ElementsByTag.TryGetValue(p.MeshElementTag, out var e) || mesh.Geometry(e) is not { IsBar: true } g) continue;
            if (Global(p.QAtA, p.CoordinateSystem, p.MemberTag) is not { } qa || Global(p.QAtB, p.CoordinateSystem, p.MemberTag) is not { } qb)
            {
                noFrame.Add(p.MemberTag);
                continue;
            }
            FemElementLoadNodalizer.BarSegment(g, p.AOverL * g.Length, p.BOverL * g.Length, qa, qb, sink(e) ?? forces);
        }
        foreach (var p in seg.InElements)
        {
            if (!mesh.ElementsByTag.TryGetValue(p.MeshElementTag, out var e) || mesh.Geometry(e) is not { IsBar: true } g) continue;
            if (Global(p.Force, p.CoordinateSystem, p.MemberTag) is not { } f) { noFrame.Add(p.MemberTag); continue; }
            FemElementLoadNodalizer.BarPointForce(g, f, p.XOverL * g.Length, sink(e) ?? forces);
        }
        foreach (var p in seg.OnNodes)
        {
            if (Global(p.Force, p.CoordinateSystem, p.MemberTag) is not { } f
                || Global(p.Moment, p.CoordinateSystem, p.MemberTag) is not { } m)
            {
                noFrame.Add(p.MemberTag);
                continue;
            }
            AddForce(forces, p.MeshNodeTag, f, m);
        }
        Add(diag, "member_load_no_frame", $"Загружение «{lc.Tag}»: местные нагрузки стержней без осей (узлы КонЭ)", noFrame, true);
    }

    static void AddForce(Dictionary<string, double[]> forces, string tag, PlanarVector3 f, PlanarVector3 m)
    {
        if (!forces.TryGetValue(tag, out var v)) forces[tag] = v = new double[6];
        v[0] += f.X; v[1] += f.Y; v[2] += f.Z; v[3] += m.X; v[4] += m.Y; v[5] += m.Z;
    }

    // ---------------------------------------------------------------- диагностики

    static void Add(List<FemValidationDiagnostic> diag, string code, string text, IReadOnlyCollection<string> tags, bool isError)
    {
        if (tags.Count == 0) return;
        var distinct = tags.Distinct(StringComparer.Ordinal).ToArray();
        diag.Add(new(code, $"{text}: {FemLoadTargets.Sample(distinct)}.", isError, distinct));
    }

    static void Info(List<FemValidationDiagnostic> diag, string code, string text) => diag.Add(new(code, text, false));
}
