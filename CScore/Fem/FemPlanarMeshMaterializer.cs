using System.Globalization;
using System.Text.Json;
using CScore.Planar;

namespace CScore.Fem;

/// <summary>Сетка одной области своей схемы на входе материализации: КонЭ-пластина, область, её снимок,
/// толщина по сечению пластины (null — сечение не назначено) и следы АЖТ примыканий, вшитые в снимок (null — нет).</summary>
public sealed record FemPlanarRegionMesh(FemMember Member, PlanarRegion Region, PlanarMeshSnapshot Snapshot, double? ThicknessM,
    RigidJunctionTraceSet? Junctions = null);

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
    /// <summary>Теги узлов сетки схемы по индексу узла снимка — для каждой области в порядке входа (пусто, если
    /// снимок не материализован).</summary>
    public IReadOnlyList<IReadOnlyList<string>> RegionNodeTags { get; init; } = [];
    /// <summary>КонЭ импортной (сохранённой) сетки, у которых нарушена связность с новой сеткой: висящие узлы на стыке
    /// с новой пластиной, узлы пластин внутри импортных стержней. Пусто — нарушений нет. По этому множеству лестница
    /// перемешивания фрагмента снимает замки сетки.</summary>
    public IReadOnlySet<string> ImportedMembersAtFault { get; init; } = new SortedSet<string>(StringComparer.Ordinal);
    /// <summary>Автоматические АЖТ примыканий стержней к плитам (<see cref="FemRigidJunctionMaterializer"/>).</summary>
    public List<FemRigidBody> RigidBodies { get; } = [];
}

/// <summary>
/// Материализация снимков сеток областей своей схемы в сетку схемы (CSfea 4г): узлы и КЭ пластин
/// (<c>ElemType = "shell"</c>, <c>Origin = generated</c>, <c>SourceMemberTag</c> = КонЭ области). Узел пластины,
/// совпавший (±<see cref="FemMeshDiscretizer.CollinearToleranceM"/>) с узлом стержня, импортной сетки или ранее
/// материализованной области, заменяется им — общие узлы; совпавший с <see cref="FemNode"/> получает его тег в
/// <c>SourceNodeTag</c> (опоры и узловые нагрузки КонЭ). Сгенерированные стержни, внутри которых лежат узлы пластин,
/// дробятся; импортные не трогаются. Q4 хранится в порядке «1 2 4 3» (как импорт). Узел пластины внутри ребра КЭ
/// другой области — ошибка (несовпадающие сетки на стыке, MPC нет). То же на стыке с импортной сеткой
/// (<c>planar_materialize_imported_hanging_node</c>): ошибка при <c>strictImported</c> (фрагмент с сохранённой сеткой
/// родителя), иначе предупреждение. Теги: КЭ стержней — подряд (после импортных),
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
        IReadOnlyList<FemPlanarRegionMesh> regions,
        bool strictImported = false)
    {
        ArgumentNullException.ThrowIfNull(regions);
        var builder = new Builder(schemaId, baseNodes, baseElements, femNodes);
        foreach (var region in regions) builder.Add(region);
        return builder.Build(checkHangingNodes: true, strictImported);
    }

    /// <summary>
    /// Пошаговая материализация для вывода ограничений областей по порядку сборки: перед областью k — пластины 1…k−1.
    /// <see cref="Build"/> даёт то же, что <see cref="Materialize"/> добавленных областей, без проверки висящих узлов
    /// (префиксу она не нужна); каждая область материализуется один раз, а не заново для каждой следующей (фрагмент
    /// «Музея»: 88 областей, 80 тыс. узлов — минуты).
    /// </summary>
    public sealed class Prefix(int schemaId, IReadOnlyList<FemMeshNode> baseNodes, IReadOnlyList<FemElement> baseElements,
        IReadOnlyList<FemNode> femNodes)
    {
        readonly Builder _builder = new(schemaId, baseNodes, baseElements, femNodes);

        public void Add(FemPlanarRegionMesh region) => _builder.Add(region);

        public FemPlanarMaterializationResult Build() => _builder.Build(checkHangingNodes: false, strictImported: false);
    }

    /// <summary>Состояние материализации: узлы и КЭ пластин добавленных областей.</summary>
    sealed class Builder
    {
        readonly int _schemaId;
        readonly IReadOnlyList<FemElement> _baseElements;
        readonly List<FemPlanarRegionMesh> _regions = [];
        readonly List<FemValidationDiagnostic> _diagnostics = [];
        /// <summary>КонЭ сохранённой сетки, чьи узлы не стали общими с точками новых областей (<c>planar_materialize_point_not_shared</c>):
        /// виновные для лестницы перемешивания.</summary>
        readonly SortedSet<string> _pointFaults = new(StringComparer.Ordinal);
        readonly List<FemMeshNode> _nodes;
        readonly Dictionary<string, FemMeshNode> _nodesByTag = new(StringComparer.Ordinal);
        readonly PointGrid _fine = new(Tolerance);
        readonly PointGrid _femGrid = new(Tolerance);
        readonly Dictionary<FemMeshNode, FemNode> _femByKey = new(ReferenceEqualityComparer.Instance);
        readonly List<(FemElement Element, int RegionIndex)> _shells = [];
        readonly HashSet<string> _shellNodeTags = new(StringComparer.Ordinal);
        readonly Dictionary<string, HashSet<int>> _regionOfNode = new(StringComparer.Ordinal);
        readonly List<IReadOnlyList<string>> _regionNodeTags = [];
        int _nextNodeTag;
        int _shared;

        public Builder(int schemaId, IReadOnlyList<FemMeshNode> baseNodes, IReadOnlyList<FemElement> baseElements,
            IReadOnlyList<FemNode> femNodes)
        {
            ArgumentNullException.ThrowIfNull(baseNodes);
            ArgumentNullException.ThrowIfNull(baseElements);
            ArgumentNullException.ThrowIfNull(femNodes);
            _schemaId = schemaId;
            _baseElements = baseElements;
            _nodes = new List<FemMeshNode>(baseNodes);
            foreach (var node in baseNodes) _nodesByTag.TryAdd(node.NodeTag, node);
            foreach (var node in baseNodes) _fine.Add(node);
            foreach (var femNode in femNodes)
            {
                var probe = new FemMeshNode { NodeTag = femNode.NodeTag, X = femNode.X, Y = femNode.Y, Z = femNode.Z };
                _femGrid.Add(probe);
                _femByKey[probe] = femNode;
            }
            _nextNodeTag = baseNodes.Select(n => PositiveTag(n.NodeTag))
                .Concat(femNodes.Select(n => PositiveTag(n.NodeTag)))
                .DefaultIfEmpty(0).Max() + 1;
        }

        /// <summary>Узлы и КЭ пластин одной области.</summary>
        public void Add(FemPlanarRegionMesh regionMesh)
        {
            int r = _regions.Count;
            _regions.Add(regionMesh);
            var (member, _, snapshot, thickness, _) = regionMesh;
            if (!snapshot.IsCalculable || snapshot.Nodes.Count == 0)
            {
                _regionNodeTags.Add([]);
                _diagnostics.Add(new("planar_materialize_snapshot_invalid",
                    $"Область «{member.ElemTag}»: нет расчётной сетки — постройте её заново.", true, [member.ElemTag]));
                return;
            }
            if (thickness is null)
                _diagnostics.Add(new("planar_materialize_thickness_missing",
                    $"Область «{member.ElemTag}»: не назначено сечение пластины — толщина КЭ не задана.", false, [member.ElemTag]));

            var tags = new string[snapshot.Nodes.Count];
            foreach (var sn in snapshot.Nodes)
            {
                var probe = new FemMeshNode { X = sn.X, Y = sn.Y, Z = sn.Z };
                if (_fine.Find(probe) is { } existing)
                {
                    tags[sn.Index] = existing.NodeTag;
                    _shared++;
                }
                else
                {
                    var created = new FemMeshNode
                    {
                        SchemaId = _schemaId,
                        NodeTag = (_nextNodeTag++).ToString(CultureInfo.InvariantCulture),
                        X = sn.X, Y = sn.Y, Z = sn.Z,
                        SourceMemberTag = member.ElemTag,
                        SourceNodeTag = _femGrid.Find(probe) is { } femProbe ? _femByKey[femProbe].NodeTag : null,
                    };
                    _nodes.Add(created);
                    _nodesByTag[created.NodeTag] = created;
                    _fine.Add(created);
                    tags[sn.Index] = created.NodeTag;
                }
                _shellNodeTags.Add(tags[sn.Index]);
                if (!_regionOfNode.TryGetValue(tags[sn.Index], out var owners)) _regionOfNode[tags[sn.Index]] = owners = [];
                owners.Add(r);
            }

            _regionNodeTags.Add(tags);
            CheckPointMappings(member, snapshot, tags, _nodesByTag, _diagnostics, _pointFaults);

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
                _shells.Add((new FemElement
                {
                    SchemaId = _schemaId,
                    ElemType = "shell",
                    NodeIdsJson = JsonSerializer.Serialize(stored.Select(t => int.Parse(t, CultureInfo.InvariantCulture)).ToArray()),
                    SourceMemberTag = member.ElemTag,
                    Origin = FemMember.MeshSourceGenerated,
                    ThicknessM = thickness,
                }, r));
            }
            if (degenerate.Count > 0)
                _diagnostics.Add(new("planar_materialize_element_degenerate",
                    $"Область «{member.ElemTag}»: КЭ {Short(degenerate)} вырождены после слияния узлов (сетка мельче 1 мм?).",
                    true, [member.ElemTag]));
        }

        /// <summary>Сетка схемы по добавленным областям: дробление стержней, теги КЭ. Узлы и КЭ пластин построителя
        /// не меняются — можно добавлять области дальше.</summary>
        public FemPlanarMaterializationResult Build(bool checkHangingNodes, bool strictImported)
        {
            var diagnostics = new List<FemValidationDiagnostic>(_diagnostics);
            var atFault = new SortedSet<string>(_pointFaults, StringComparer.Ordinal);

            // Дробление стержней узлами пластин. Строгий режим (фрагмент с сохранённой сеткой): и узлами импортных пластин —
            // сгенерированный стержень на сохранённой плите делится по её узлам, сохранённый стержень с узлом сохранённой
            // плиты внутри — нарушение (перемешивается стержень, плита остаётся).
            var coarse = new PointGrid(CoarseCellM);
            var splitNodes = new HashSet<string>(_shellNodeTags, StringComparer.Ordinal);
            if (strictImported)
                foreach (var element in _baseElements)
                    if (element.Origin == FemMember.MeshSourceImported && element.ElemType == "shell" && TryNodes(element, _nodesByTag, out var shellNodes))
                        foreach (var node in shellNodes) splitNodes.Add(node.NodeTag);
            foreach (var tag in splitNodes) coarse.Add(_nodesByTag[tag]);
            var beams = new List<FemElement>();
            int split = 0;
            var importedInner = new List<string>();
            foreach (var element in _baseElements)
            {
                if (element.ElemType != "beam" || !TryNodes(element, _nodesByTag, out var ends) || ends.Length != 2)
                {
                    beams.Add(element);
                    continue;
                }
                var inner = InnerNodes(ends[0], ends[1], coarse);
                if (inner.Count == 0) { beams.Add(element); continue; }
                if (element.Origin == FemMember.MeshSourceImported)
                {
                    importedInner.Add(element.ElemTag);
                    atFault.Add(MemberOf(element));
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
                    strictImported, importedInner));

            if (checkHangingNodes)
            {
                CheckHangingNodes(_shells, _regions, _nodesByTag, _regionOfNode, diagnostics);
                CheckImportedSeams(_baseElements, _shells, _regions, _nodesByTag, _regionOfNode, strictImported, diagnostics, atFault);
            }

            // Теги КЭ: импортные — как были; сгенерированные стержни подряд, затем пластины (копии КЭ построителя).
            int nextElementTag = _baseElements.Where(e => e.Origin == FemMember.MeshSourceImported)
                .Select(e => PositiveTag(e.ElemTag)).DefaultIfEmpty(0).Max() + 1;
            foreach (var beam in beams.Where(e => e.Origin != FemMember.MeshSourceImported))
                beam.ElemTag = (nextElementTag++).ToString(CultureInfo.InvariantCulture);
            var shells = _shells.Select(s => new FemElement
            {
                SchemaId = s.Element.SchemaId, ElemType = s.Element.ElemType, NodeIdsJson = s.Element.NodeIdsJson,
                SourceMemberTag = s.Element.SourceMemberTag, Origin = s.Element.Origin, ThicknessM = s.Element.ThicknessM,
                ElemTag = (nextElementTag++).ToString(CultureInfo.InvariantCulture),
            }).ToList();

            return new FemPlanarMaterializationResult
            {
                Nodes = [.. _nodes],
                Elements = [.. beams, .. shells],
                Diagnostics = diagnostics,
                SplitBeamCount = split,
                SharedNodeCount = _shared,
                RegionNodeTags = [.. _regionNodeTags],
                ImportedMembersAtFault = atFault,
            };
        }
    }

    /// <summary>Точка снимка, выведенная из узла стержня, должна слиться с этим узлом. Не слился узел сохранённой сетки КонЭ
    /// (два совпадающих узла ЛИРЫ у концов стержней «паука», общим становится один) — КонЭ в <paramref name="faults"/>.</summary>
    static void CheckPointMappings(FemMember member, PlanarMeshSnapshot snapshot, string[] tags,
        IReadOnlyDictionary<string, FemMeshNode> nodesByTag, List<FemValidationDiagnostic> diagnostics, ISet<string> faults)
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
                    {
                        missed.Add(sourceTag);
                        if (sourceNode.Origin == FemMember.MeshSourceImported && sourceNode.SourceMemberTag is { } owner)
                            faults.Add(owner);
                    }
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
        // Узлы — по паре областей: общий список на все пары называл бы в каждой ошибке чужие узлы.
        var pairs = new SortedDictionary<(int, int), SortedSet<string>>();
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
                    foreach (var other in owners)
                    {
                        var key = (Math.Min(r, other), Math.Max(r, other));
                        if (!pairs.TryGetValue(key, out var hanging)) pairs[key] = hanging = new SortedSet<string>(StringComparer.Ordinal);
                        hanging.Add(node.NodeTag);
                    }
                }
            }
        }
        foreach (var ((a, b), hanging) in pairs)
            diagnostics.Add(new("planar_materialize_hanging_node",
                $"Сетки областей «{regions[a].Member.ElemTag}» и «{regions[b].Member.ElemTag}» не совпадают на стыке " +
                $"(узлы {Short(hanging)} внутри рёбер КЭ соседней области): постройте сетку заново или задайте областям одинаковый размер КЭ.",
                true, [regions[a].Member.ElemTag, regions[b].Member.ElemTag]));
    }

    /// <summary>
    /// Стык импортной сетки с новыми пластинами: узел новой области внутри ребра импортной пластины и узел импортной
    /// сетки (пластины или стержня) внутри ребра новой пластины — висящие узлы, связи нет. Одна диагностика на пару
    /// «КонЭ импортной сетки — область»; КонЭ импортной сетки — в <paramref name="atFault"/>. Узлы новых пластин
    /// внутри импортных стержней проверяются при дроблении стержней.
    /// </summary>
    static void CheckImportedSeams(IReadOnlyList<FemElement> baseElements, List<(FemElement Element, int RegionIndex)> shells,
        IReadOnlyList<FemPlanarRegionMesh> regions, IReadOnlyDictionary<string, FemMeshNode> nodesByTag,
        IReadOnlyDictionary<string, HashSet<int>> regionOfNode, bool strict, List<FemValidationDiagnostic> diagnostics,
        SortedSet<string> atFault)
    {
        if (regions.Count == 0) return;
        // Узел импортной сетки → КонЭ импортных КЭ, которые на него ссылаются.
        var importedOwners = new Dictionary<string, SortedSet<string>>(StringComparer.Ordinal);
        var importedShells = new List<(string[] Contour, string Member)>();
        foreach (var element in baseElements)
        {
            if (element.Origin != FemMember.MeshSourceImported || !TryNodes(element, nodesByTag, out var nodes)) continue;
            var member = MemberOf(element);
            foreach (var node in nodes)
            {
                if (!importedOwners.TryGetValue(node.NodeTag, out var owners))
                    importedOwners[node.NodeTag] = owners = new SortedSet<string>(StringComparer.Ordinal);
                owners.Add(member);
            }
            if (element.ElemType == "shell" && nodes.Length >= 3)
            {
                var ids = nodes.Select(n => n.NodeTag).ToArray();
                importedShells.Add((ids.Length == 4 ? [ids[0], ids[1], ids[3], ids[2]] : ids, member));
            }
        }
        if (importedOwners.Count == 0) return;

        var violations = new SortedDictionary<(string Member, int Region), SortedSet<string>>();
        void Add(string member, int region, string node)
        {
            if (!violations.TryGetValue((member, region), out var list))
                violations[(member, region)] = list = new SortedSet<string>(StringComparer.Ordinal);
            list.Add(node);
        }

        // Узлы новых областей внутри рёбер импортных пластин.
        var fresh = new PointGrid(CoarseCellM);
        foreach (var tag in regionOfNode.Keys)
            if (!importedOwners.ContainsKey(tag)) fresh.Add(nodesByTag[tag]);
        var seen = new HashSet<(string, string)>();
        foreach (var (contour, member) in importedShells)
            for (int i = 0; i < contour.Length; i++)
            {
                string a = contour[i], b = contour[(i + 1) % contour.Length];
                if (!seen.Add(string.CompareOrdinal(a, b) < 0 ? (a, b) : (b, a))) continue;
                foreach (var node in InnerNodes(nodesByTag[a], nodesByTag[b], fresh))
                    foreach (var region in regionOfNode[node.NodeTag])
                        Add(member, region, node.NodeTag);
            }

        // Узлы импортной сетки внутри рёбер новых пластин.
        var imported = new PointGrid(CoarseCellM);
        foreach (var tag in importedOwners.Keys) imported.Add(nodesByTag[tag]);
        seen.Clear();
        foreach (var (element, r) in shells)
        {
            var ids = JsonSerializer.Deserialize<int[]>(element.NodeIdsJson)!.Select(i => i.ToString(CultureInfo.InvariantCulture)).ToArray();
            var contour = ids.Length == 4 ? new[] { ids[0], ids[1], ids[3], ids[2] } : ids;
            for (int i = 0; i < contour.Length; i++)
            {
                string a = contour[i], b = contour[(i + 1) % contour.Length];
                if (!seen.Add(string.CompareOrdinal(a, b) < 0 ? (a, b) : (b, a))) continue;
                foreach (var node in InnerNodes(nodesByTag[a], nodesByTag[b], imported))
                {
                    if (regionOfNode.TryGetValue(node.NodeTag, out var owners) && owners.Contains(r)) continue;
                    foreach (var member in importedOwners[node.NodeTag]) Add(member, r, node.NodeTag);
                }
            }
        }

        foreach (var ((member, region), nodes) in violations)
        {
            var regionTag = regions[region].Member.ElemTag;
            atFault.Add(member);
            diagnostics.Add(new("planar_materialize_imported_hanging_node",
                $"Импортная сетка «{member}» и сетка области «{regionTag}» не совпадают на стыке (узлы {Short(nodes)} " +
                "внутри рёбер КЭ соседней сетки) — связи в этих узлах нет.",
                strict, [member, regionTag]));
        }
    }

    /// <summary>КонЭ импортного КЭ (по <see cref="FemElement.SourceMemberTag"/>, иначе тег КЭ).</summary>
    static string MemberOf(FemElement element) =>
        string.IsNullOrEmpty(element.SourceMemberTag) ? element.ElemTag : element.SourceMemberTag;

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
