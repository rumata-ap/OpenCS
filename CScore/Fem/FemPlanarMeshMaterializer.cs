using System.Globalization;
using System.Text.Json;
using CScore.Planar;

namespace CScore.Fem;

/// <summary>Сетка одной области своей схемы на входе материализации: КонЭ-пластина, область, её снимок и
/// толщина по сечению пластины (null — сечение не назначено).</summary>
public sealed record FemPlanarRegionMesh(FemMember Member, PlanarRegion Region, PlanarMeshSnapshot Snapshot, double? ThicknessM);

/// <summary>Сетка схемы после материализации пластин и отчёт.</summary>
public sealed class FemPlanarMaterializationResult
{
    public List<FemMeshNode> Nodes { get; init; } = [];
    public List<FemElement> Elements { get; init; } = [];
    public List<FemValidationDiagnostic> Diagnostics { get; init; } = [];
    public bool HasErrors => Diagnostics.Any(d => d.IsError);
    /// <summary>Сколько КЭ стержней разделено узлами пластин.</summary>
    public int SplitBeamCount { get; init; }
    /// <summary>Сколько узлов пластин совпало с узлами стержней, импортной сетки или других областей.</summary>
    public int SharedNodeCount { get; init; }
}

/// <summary>
/// Материализация снимков сеток областей своей схемы в сетку схемы (CSfea 4г): узлы и КЭ пластин
/// (<c>ElemType = "shell"</c>, <c>Origin = generated</c>, <c>SourceMemberTag</c> = КонЭ области). Узел пластины,
/// совпавший (±<see cref="FemMeshDiscretizer.CollinearToleranceM"/>) с узлом стержня, импортной сетки или ранее
/// материализованной области, заменяется им — общие узлы; совпавший с <see cref="FemNode"/> получает его тег в
/// <c>SourceNodeTag</c> (опоры и узловые нагрузки КонЭ). Сгенерированные стержни, внутри которых лежат узлы пластин,
/// дробятся; импортные не трогаются. Q4 хранится в порядке «1 2 4 3» (как импорт). Узел пластины внутри ребра КЭ
/// другой области — ошибка (несовпадающие сетки на стыке, MPC нет). Теги: КЭ стержней — подряд (после импортных),
/// затем пластины по порядку областей; новые узлы — после наибольшего тега. Результат детерминирован.
/// </summary>
public static class FemPlanarMeshMaterializer
{
    const double Tolerance = FemMeshDiscretizer.CollinearToleranceM;
    /// <summary>Размер ячейки грубой сетки поиска узлов на отрезках, м.</summary>
    const double CoarseCellM = 1.0;

    public static FemPlanarMaterializationResult Materialize(
        int schemaId,
        IReadOnlyList<FemMeshNode> baseNodes,
        IReadOnlyList<FemElement> baseElements,
        IReadOnlyList<FemNode> femNodes,
        IReadOnlyList<FemPlanarRegionMesh> regions)
    {
        ArgumentNullException.ThrowIfNull(baseNodes);
        ArgumentNullException.ThrowIfNull(baseElements);
        ArgumentNullException.ThrowIfNull(femNodes);
        ArgumentNullException.ThrowIfNull(regions);

        var diagnostics = new List<FemValidationDiagnostic>();
        var nodes = new List<FemMeshNode>(baseNodes);
        var nodesByTag = new Dictionary<string, FemMeshNode>(StringComparer.Ordinal);
        foreach (var node in baseNodes) nodesByTag.TryAdd(node.NodeTag, node);
        var fine = new PointGrid(Tolerance);
        foreach (var node in baseNodes) fine.Add(node);
        var femGrid = new PointGrid(Tolerance);
        var femByKey = new Dictionary<FemMeshNode, FemNode>(ReferenceEqualityComparer.Instance);
        foreach (var femNode in femNodes)
        {
            var probe = new FemMeshNode { NodeTag = femNode.NodeTag, X = femNode.X, Y = femNode.Y, Z = femNode.Z };
            femGrid.Add(probe);
            femByKey[probe] = femNode;
        }

        int nextNodeTag = baseNodes.Select(n => PositiveTag(n.NodeTag))
            .Concat(femNodes.Select(n => PositiveTag(n.NodeTag)))
            .DefaultIfEmpty(0).Max() + 1;

        // Узлы и КЭ пластин по областям.
        var shells = new List<(FemElement Element, int RegionIndex)>();
        var shellNodeTags = new HashSet<string>(StringComparer.Ordinal);
        var regionOfNode = new Dictionary<string, HashSet<int>>(StringComparer.Ordinal);
        int shared = 0;
        for (int r = 0; r < regions.Count; r++)
        {
            var (member, region, snapshot, thickness) = regions[r];
            if (!snapshot.IsCalculable || snapshot.Nodes.Count == 0)
            {
                diagnostics.Add(new("planar_materialize_snapshot_invalid",
                    $"Область «{member.ElemTag}»: нет расчётной сетки — постройте её заново.", true, [member.ElemTag]));
                continue;
            }
            if (thickness is null)
                diagnostics.Add(new("planar_materialize_thickness_missing",
                    $"Область «{member.ElemTag}»: не назначено сечение пластины — толщина КЭ не задана.", false, [member.ElemTag]));

            var tags = new string[snapshot.Nodes.Count];
            foreach (var sn in snapshot.Nodes)
            {
                var probe = new FemMeshNode { X = sn.X, Y = sn.Y, Z = sn.Z };
                if (fine.Find(probe) is { } existing)
                {
                    tags[sn.Index] = existing.NodeTag;
                    shared++;
                }
                else
                {
                    var created = new FemMeshNode
                    {
                        SchemaId = schemaId,
                        NodeTag = (nextNodeTag++).ToString(CultureInfo.InvariantCulture),
                        X = sn.X, Y = sn.Y, Z = sn.Z,
                        SourceMemberTag = member.ElemTag,
                        SourceNodeTag = femGrid.Find(probe) is { } femProbe ? femByKey[femProbe].NodeTag : null,
                    };
                    nodes.Add(created);
                    nodesByTag[created.NodeTag] = created;
                    fine.Add(created);
                    tags[sn.Index] = created.NodeTag;
                }
                shellNodeTags.Add(tags[sn.Index]);
                if (!regionOfNode.TryGetValue(tags[sn.Index], out var owners)) regionOfNode[tags[sn.Index]] = owners = [];
                owners.Add(r);
            }

            CheckPointMappings(member, snapshot, tags, nodesByTag, diagnostics);

            var degenerate = new List<string>();
            foreach (var se in snapshot.Elements)
            {
                var ids = se.NodeIndices.Select(i => tags[i]).ToArray();
                if (ids.Distinct(StringComparer.Ordinal).Count() != ids.Length)
                {
                    degenerate.Add(se.Index.ToString(CultureInfo.InvariantCulture));
                    continue;
                }
                // Снимок обходит КЭ против часовой в осях области (нормаль — ось Z области); сетка схемы хранит
                // Q4 как импорт ЛИРЫ/SCAD — «1 2 4 3».
                var stored = ids.Length == 4 ? new[] { ids[0], ids[1], ids[3], ids[2] } : ids;
                shells.Add((new FemElement
                {
                    SchemaId = schemaId,
                    ElemType = "shell",
                    NodeIdsJson = JsonSerializer.Serialize(stored.Select(t => int.Parse(t, CultureInfo.InvariantCulture)).ToArray()),
                    SourceMemberTag = member.ElemTag,
                    Origin = FemMember.MeshSourceGenerated,
                    ThicknessM = thickness,
                }, r));
            }
            if (degenerate.Count > 0)
                diagnostics.Add(new("planar_materialize_element_degenerate",
                    $"Область «{member.ElemTag}»: КЭ {Short(degenerate)} вырождены после слияния узлов (сетка мельче 1 мм?).",
                    true, [member.ElemTag]));
        }

        // Дробление стержней узлами пластин.
        var coarse = new PointGrid(CoarseCellM);
        foreach (var tag in shellNodeTags) coarse.Add(nodesByTag[tag]);
        var beams = new List<FemElement>();
        int split = 0;
        var importedInner = new List<string>();
        foreach (var element in baseElements)
        {
            if (element.ElemType != "beam" || !TryNodes(element, nodesByTag, out var ends) || ends.Length != 2)
            {
                beams.Add(element);
                continue;
            }
            var inner = InnerNodes(ends[0], ends[1], coarse);
            if (inner.Count == 0) { beams.Add(element); continue; }
            if (element.Origin == FemMember.MeshSourceImported)
            {
                importedInner.Add(element.ElemTag);
                beams.Add(element);
                continue;
            }
            split++;
            var chain = new List<FemMeshNode> { ends[0] };
            chain.AddRange(inner);
            chain.Add(ends[1]);
            for (int i = 0; i + 1 < chain.Count; i++)
            {
                var piece = CopyBeam(element);
                piece.NodeIdsJson = JsonSerializer.Serialize(new[] { int.Parse(chain[i].NodeTag, CultureInfo.InvariantCulture), int.Parse(chain[i + 1].NodeTag, CultureInfo.InvariantCulture) });
                piece.ReleaseI = i == 0 ? element.ReleaseI : null;
                piece.ReleaseJ = i + 2 == chain.Count ? element.ReleaseJ : null;
                beams.Add(piece);
            }
        }
        if (importedInner.Count > 0)
            diagnostics.Add(new("planar_materialize_imported_beam_inner_node",
                $"Узлы пластин лежат внутри импортных стержней (КЭ {Short(importedInner)}) — стержни не дробятся, связи с пластиной в этих узлах нет.",
                false, importedInner));

        CheckHangingNodes(shells, regions, nodesByTag, regionOfNode, diagnostics);

        // Теги КЭ: импортные — как были; сгенерированные стержни подряд, затем пластины.
        int nextElementTag = baseElements.Where(e => e.Origin == FemMember.MeshSourceImported)
            .Select(e => PositiveTag(e.ElemTag)).DefaultIfEmpty(0).Max() + 1;
        foreach (var beam in beams.Where(e => e.Origin != FemMember.MeshSourceImported))
            beam.ElemTag = (nextElementTag++).ToString(CultureInfo.InvariantCulture);
        foreach (var (shell, _) in shells)
            shell.ElemTag = (nextElementTag++).ToString(CultureInfo.InvariantCulture);

        return new FemPlanarMaterializationResult
        {
            Nodes = nodes,
            Elements = [.. beams, .. shells.Select(s => s.Element)],
            Diagnostics = diagnostics,
            SplitBeamCount = split,
            SharedNodeCount = shared,
        };
    }

    /// <summary>Точка снимка, выведенная из узла стержня, должна слиться с этим узлом.</summary>
    static void CheckPointMappings(FemMember member, PlanarMeshSnapshot snapshot, string[] tags,
        IReadOnlyDictionary<string, FemMeshNode> nodesByTag, List<FemValidationDiagnostic> diagnostics)
    {
        var missed = new List<string>();
        foreach (var mapping in snapshot.ConstraintMappings)
        {
            if (mapping.PointNodeIndices.Count != 1) continue;
            var tag = tags[mapping.PointNodeIndices[0]];
            foreach (var source in mapping.SourceReferences)
                foreach (var sourceTag in source.NodeTags)
                    if (nodesByTag.TryGetValue(sourceTag, out var sourceNode) && sourceNode.NodeTag != tag &&
                        Distance(sourceNode, nodesByTag[tag]) <= Tolerance)
                        missed.Add(sourceTag);
        }
        if (missed.Count > 0)
            diagnostics.Add(new("planar_materialize_point_not_shared",
                $"Область «{member.ElemTag}»: узлы {Short(missed)} не стали общими с пластиной.", true, missed));
    }

    /// <summary>Узел одной области внутри ребра КЭ другой — несовпадающие сетки на стыке.</summary>
    static void CheckHangingNodes(List<(FemElement Element, int RegionIndex)> shells, IReadOnlyList<FemPlanarRegionMesh> regions,
        IReadOnlyDictionary<string, FemMeshNode> nodesByTag, IReadOnlyDictionary<string, HashSet<int>> regionOfNode,
        List<FemValidationDiagnostic> diagnostics)
    {
        if (regions.Count < 2) return;
        var coarse = new PointGrid(CoarseCellM);
        foreach (var tag in regionOfNode.Keys) coarse.Add(nodesByTag[tag]);
        var seen = new HashSet<(string, string)>();
        var pairs = new SortedSet<(int, int)>();
        var hanging = new SortedSet<string>(StringComparer.Ordinal);
        foreach (var (element, r) in shells)
        {
            var ids = JsonSerializer.Deserialize<int[]>(element.NodeIdsJson)!.Select(i => i.ToString(CultureInfo.InvariantCulture)).ToArray();
            var contour = ids.Length == 4 ? new[] { ids[0], ids[1], ids[3], ids[2] } : ids;
            for (int i = 0; i < contour.Length; i++)
            {
                string a = contour[i], b = contour[(i + 1) % contour.Length];
                if (!seen.Add(string.CompareOrdinal(a, b) < 0 ? (a, b) : (b, a))) continue;
                foreach (var node in InnerNodes(nodesByTag[a], nodesByTag[b], coarse))
                {
                    var owners = regionOfNode[node.NodeTag];
                    if (owners.Contains(r)) continue;
                    hanging.Add(node.NodeTag);
                    foreach (var other in owners)
                        pairs.Add((Math.Min(r, other), Math.Max(r, other)));
                }
            }
        }
        foreach (var (a, b) in pairs)
            diagnostics.Add(new("planar_materialize_hanging_node",
                $"Сетки областей «{regions[a].Member.ElemTag}» и «{regions[b].Member.ElemTag}» не совпадают на стыке " +
                $"(узлы {Short(hanging)} внутри рёбер КЭ соседней области): постройте сетку заново или задайте областям одинаковый размер КЭ.",
                true, [regions[a].Member.ElemTag, regions[b].Member.ElemTag]));
    }

    /// <summary>Узлы грубой сетки строго внутри отрезка (±допуск), по порядку от <paramref name="a"/>.</summary>
    static List<FemMeshNode> InnerNodes(FemMeshNode a, FemMeshNode b, PointGrid grid)
    {
        double dx = b.X - a.X, dy = b.Y - a.Y, dz = b.Z - a.Z;
        var length = Math.Sqrt(dx * dx + dy * dy + dz * dz);
        var result = new List<(double T, FemMeshNode Node)>();
        if (length <= Tolerance) return [];
        foreach (var node in grid.InBox(Math.Min(a.X, b.X) - Tolerance, Math.Min(a.Y, b.Y) - Tolerance, Math.Min(a.Z, b.Z) - Tolerance,
                     Math.Max(a.X, b.X) + Tolerance, Math.Max(a.Y, b.Y) + Tolerance, Math.Max(a.Z, b.Z) + Tolerance))
        {
            if (ReferenceEquals(node, a) || ReferenceEquals(node, b) || node.NodeTag == a.NodeTag || node.NodeTag == b.NodeTag) continue;
            double px = node.X - a.X, py = node.Y - a.Y, pz = node.Z - a.Z;
            var t = (px * dx + py * dy + pz * dz) / (length * length);
            if (t * length <= Tolerance || (1 - t) * length <= Tolerance) continue;
            double ex = px - t * dx, ey = py - t * dy, ez = pz - t * dz;
            if (ex * ex + ey * ey + ez * ez <= Tolerance * Tolerance) result.Add((t, node));
        }
        return result.OrderBy(item => item.T).Select(item => item.Node).ToList();
    }

    static bool TryNodes(FemElement element, IReadOnlyDictionary<string, FemMeshNode> nodesByTag, out FemMeshNode[] nodes)
    {
        nodes = [];
        int[]? ids;
        try { ids = JsonSerializer.Deserialize<int[]>(element.NodeIdsJson); }
        catch (JsonException) { return false; }
        if (ids is null) return false;
        var result = new FemMeshNode[ids.Length];
        for (int i = 0; i < ids.Length; i++)
            if (!nodesByTag.TryGetValue(ids[i].ToString(CultureInfo.InvariantCulture), out result[i]!)) return false;
        nodes = result;
        return true;
    }

    static FemElement CopyBeam(FemElement e) => new()
    {
        SchemaId = e.SchemaId, ElemType = e.ElemType, SourceMemberTag = e.SourceMemberTag, Origin = e.Origin,
        SectionTag = e.SectionTag, StiffnessNum = e.StiffnessNum, MaterialTag = e.MaterialTag, ThicknessM = e.ThicknessM,
        ReinforcementTypeIds = e.ReinforcementTypeIds, LocalAxisAngleDeg = e.LocalAxisAngleDeg, BeamRotationDeg = e.BeamRotationDeg,
        FoundationC1 = e.FoundationC1, CrossSectionId = e.CrossSectionId, GjStrategy = e.GjStrategy,
        GjManualValue = e.GjManualValue, GjTorsionTaskId = e.GjTorsionTaskId,
    };

    static int PositiveTag(string tag) =>
        int.TryParse(tag, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) && value > 0 ? value : 0;

    static double Distance(FemMeshNode a, FemMeshNode b)
    {
        double dx = a.X - b.X, dy = a.Y - b.Y, dz = a.Z - b.Z;
        return Math.Sqrt(dx * dx + dy * dy + dz * dz);
    }

    static string Short(IEnumerable<string> tags)
    {
        var list = tags.Distinct().ToList();
        return list.Count <= 10 ? string.Join(", ", list) : string.Join(", ", list.Take(10)) + $" … (всего {list.Count})";
    }

    /// <summary>Хеш-сетка узлов по кубическим ячейкам.</summary>
    sealed class PointGrid(double cell)
    {
        readonly Dictionary<(long, long, long), List<FemMeshNode>> _cells = [];

        (long, long, long) Key(double x, double y, double z) =>
            ((long)Math.Floor(x / cell), (long)Math.Floor(y / cell), (long)Math.Floor(z / cell));

        public void Add(FemMeshNode node)
        {
            var key = Key(node.X, node.Y, node.Z);
            if (!_cells.TryGetValue(key, out var list)) _cells[key] = list = [];
            list.Add(node);
        }

        /// <summary>Узел в пределах размера ячейки (для точной сетки — допуск совпадения); ближайший.</summary>
        public FemMeshNode? Find(FemMeshNode probe)
        {
            var (cx, cy, cz) = Key(probe.X, probe.Y, probe.Z);
            FemMeshNode? best = null;
            double bestDistance = double.PositiveInfinity;
            for (long dx = -1; dx <= 1; dx++)
                for (long dy = -1; dy <= 1; dy++)
                    for (long dz = -1; dz <= 1; dz++)
                        if (_cells.TryGetValue((cx + dx, cy + dy, cz + dz), out var list))
                            foreach (var node in list)
                            {
                                var d = Distance(node, probe);
                                if (d <= cell && d < bestDistance) { best = node; bestDistance = d; }
                            }
            return best;
        }

        public IEnumerable<FemMeshNode> InBox(double x0, double y0, double z0, double x1, double y1, double z1)
        {
            var (ax, ay, az) = Key(x0, y0, z0);
            var (bx, by, bz) = Key(x1, y1, z1);
            for (long x = ax; x <= bx; x++)
                for (long y = ay; y <= by; y++)
                    for (long z = az; z <= bz; z++)
                        if (_cells.TryGetValue((x, y, z), out var list))
                            foreach (var node in list)
                                yield return node;
        }
    }
}
