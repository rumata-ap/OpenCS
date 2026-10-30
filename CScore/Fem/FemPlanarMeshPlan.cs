using System.Globalization;
using System.Text.Json;
using CScore.Planar;

namespace CScore.Fem;

/// <summary>Ограничения сетки области: ручные объекты области, выведенные из схемы и свободные узлы.</summary>
public sealed record FemPlanarRegionConstraints(
    IReadOnlyList<PlanarConstraintObject> Constraints,
    string SourceFingerprint,
    IReadOnlyList<FemValidationDiagnostic> Diagnostics,
    DerivedPlanarConstraintSet Derived,
    int FreeNodeCount,
    int JunctionCount = 0,
    int RigidJunctionCount = 0);

/// <summary>
/// Волны построения сеток областей: в волне нет касающихся друг друга областей — их сетки строятся параллельно.
/// Из касающихся областей с разным размером КЭ раньше строится мелкая (её узлы встраивает крупная); при равном — та,
/// что в меньшей волне. Индексы — в списке упорядоченных областей (<see cref="FemPlanarMeshPlan.OrderedRegions"/>).
/// </summary>
public sealed class FemPlanarMeshWaves
{
    /// <summary>Волны: индексы областей по возрастанию.</summary>
    public IReadOnlyList<IReadOnlyList<int>> Waves { get; }
    /// <summary>Номер волны области.</summary>
    public IReadOnlyList<int> WaveOf { get; }
    /// <summary>Касающиеся области (по возрастанию индекса).</summary>
    public IReadOnlyList<IReadOnlyList<int>> Neighbours { get; }
    /// <summary>Порядок сборки: по волнам, в волне — по исходному порядку. Префикс области волны w — области волн
    /// 0…w−1 в этом порядке, итоговая материализация — в нём же (теги узлов префикса = теги итоговой сетки).</summary>
    public IReadOnlyList<int> Sequence { get; }

    internal FemPlanarMeshWaves(IReadOnlyList<int> waveOf, IReadOnlyList<IReadOnlyList<int>> neighbours)
    {
        WaveOf = waveOf;
        Neighbours = neighbours;
        Waves = Enumerable.Range(0, waveOf.Count == 0 ? 0 : waveOf.Max() + 1)
            .Select(w => (IReadOnlyList<int>)Enumerable.Range(0, waveOf.Count).Where(i => waveOf[i] == w).ToList()).ToList();
        Sequence = Waves.SelectMany(w => w).ToList();
    }

    /// <summary>Касающиеся области, которые строятся позже <paramref name="index"/>-й: их линии стыка с ней — по
    /// геометрии. Некасающиеся стыков не дают — их можно не перечислять.</summary>
    public List<(string Tag, PlanarRegion Region)> Later(IReadOnlyList<(FemMember Member, PlanarRegion Region)> ordered, int index) =>
        Neighbours[index].Where(j => WaveOf[j] > WaveOf[index]).Select(j => (ordered[j].Member.ElemTag, ordered[j].Region)).ToList();
}

/// <summary>
/// Сетка ранее построенных областей (префикс) с пространственным индексом КЭ по габаритам: отбор КЭ у плоскости и
/// габарита области (<see cref="FemPlanarMeshPlan.Constraints(int, IReadOnlyList{FemNode}, IReadOnlyList{FemMember}, FemPlanarMeshPrefix, PlanarRegion, PlanarConstraintDerivationOptions?, IReadOnlyList{ValueTuple{string, PlanarRegion}}?, RigidJunctionTraceSet?)"/>)
/// без перебора всей сетки — у многоэтажного здания сотни областей и до миллиона КЭ. Только чтение: один префикс на
/// волну, вывод ограничений её областей — параллельно.
/// </summary>
public sealed class FemPlanarMeshPrefix
{
    const double CellM = 2.0;
    public FemPlanarMaterializationResult Mesh { get; }
    internal readonly Dictionary<string, int> NodeIndex = new(StringComparer.Ordinal);
    internal readonly int[][] ElementIds;
    readonly (double X0, double Y0, double Z0, double X1, double Y1, double Z1)[] _boxes;
    /// <summary>КЭ без узлов или с узлами вне сетки — отбираются всегда (их разберёт вывод).</summary>
    readonly List<int> _unbounded = [];
    readonly Dictionary<(long, long, long), List<int>> _cells = [];

    public FemPlanarMeshPrefix(FemPlanarMaterializationResult mesh)
    {
        Mesh = mesh;
        for (int i = 0; i < mesh.Nodes.Count; i++) NodeIndex.TryAdd(mesh.Nodes[i].NodeTag, i);
        ElementIds = new int[mesh.Elements.Count][];
        _boxes = new (double, double, double, double, double, double)[mesh.Elements.Count];
        for (int e = 0; e < mesh.Elements.Count; e++)
        {
            var ids = ElementIds[e] = FemPlanarMeshPlan.ReadIds(mesh.Elements[e].NodeIdsJson);
            double x0 = double.PositiveInfinity, y0 = x0, z0 = x0, x1 = double.NegativeInfinity, y1 = x1, z1 = x1;
            bool known = ids.Length > 0;
            foreach (var id in ids)
            {
                if (!NodeIndex.TryGetValue(id.ToString(CultureInfo.InvariantCulture), out var n)) { known = false; break; }
                var node = mesh.Nodes[n];
                (x0, y0, z0) = (Math.Min(x0, node.X), Math.Min(y0, node.Y), Math.Min(z0, node.Z));
                (x1, y1, z1) = (Math.Max(x1, node.X), Math.Max(y1, node.Y), Math.Max(z1, node.Z));
            }
            if (!known) { _unbounded.Add(e); continue; }
            _boxes[e] = (x0, y0, z0, x1, y1, z1);
            var (a, b) = (Cell(x0, y0, z0), Cell(x1, y1, z1));
            for (long x = a.X; x <= b.X; x++)
                for (long y = a.Y; y <= b.Y; y++)
                    for (long z = a.Z; z <= b.Z; z++)
                    {
                        if (!_cells.TryGetValue((x, y, z), out var list)) _cells[(x, y, z)] = list = [];
                        list.Add(e);
                    }
        }
    }

    static (long X, long Y, long Z) Cell(double x, double y, double z) =>
        ((long)Math.Floor(x / CellM), (long)Math.Floor(y / CellM), (long)Math.Floor(z / CellM));

    /// <summary>КЭ, габарит которых пересекает заданный, и КЭ без габарита — по возрастанию индекса.</summary>
    internal List<int> Candidates(double x0, double y0, double z0, double x1, double y1, double z1)
    {
        var result = new HashSet<int>(_unbounded);
        bool finite = new[] { x0, y0, z0, x1, y1, z1 }.All(double.IsFinite);
        var (a, b) = finite ? (Cell(x0, y0, z0), Cell(x1, y1, z1)) : default;
        // Габарит области больше всей сетки (или бесконечен) — перебор КЭ дешевле перебора ячеек.
        if (!finite || (double)(b.X - a.X + 1) * (b.Y - a.Y + 1) * (b.Z - a.Z + 1) > _cells.Count)
        {
            for (int e = 0; e < _boxes.Length; e++) Test(e);
        }
        else
            for (long x = a.X; x <= b.X; x++)
                for (long y = a.Y; y <= b.Y; y++)
                    for (long z = a.Z; z <= b.Z; z++)
                        if (_cells.TryGetValue((x, y, z), out var list))
                            foreach (var e in list) Test(e);
        var sorted = result.ToList();
        sorted.Sort();
        return sorted;

        void Test(int e)
        {
            var box = _boxes[e];
            if (ElementIds[e].Length > 0 && box.X0 <= x1 && box.X1 >= x0 && box.Y0 <= y1 && box.Y1 >= y0 && box.Z0 <= z1 && box.Z1 >= z0)
                result.Add(e);
        }
    }
}

/// <summary>
/// Общий для команды «Построить сетку схемы» и диалога области порядок построения сеток областей и вход вывода
/// их ограничений (CSfea 4г). Вход не зависит от дробления стержней по длине и от Id строк сетки в БД — отпечаток
/// снимка стабилен между сборками. Область видит узлы схемы, стержни, разбитые только узлами схемы, и пластины
/// областей прошлых волн (<see cref="Waves"/>; поздняя область встраивает узлы ранних — стыки совпадают).
/// </summary>
public static class FemPlanarMeshPlan
{
    /// <summary>Размер КЭ пластины: локальный шаг КонЭ, иначе общий шаг пластин схемы, иначе размер из области.</summary>
    public static double MeshSize(FemMember member, PlanarRegion region, double? schemaPlateStepM) =>
        member.TargetMeshLengthM is > 0 and var local ? local
        : schemaPlateStepM is > 0 and var common ? common
        : region.MeshMaxElementSizeM;

    /// <summary>КонЭ-пластины своей схемы с областью и незаблокированной сеткой — мелкие (по размеру КЭ,
    /// <see cref="MeshSize"/>) раньше, затем по Id и тегу.</summary>
    public static List<(FemMember Member, PlanarRegion Region)> OrderedRegions(
        IReadOnlyList<FemMember> members, IReadOnlyList<PlanarRegion> regions, double? schemaPlateStepM = null)
    {
        var byId = new Dictionary<int, PlanarRegion>();
        foreach (var region in regions) byId.TryAdd(region.Id, region);
        return members
            .Where(m => m.PlanarRegionId is int id && byId.ContainsKey(id) && !m.IsMeshLocked)
            .Select(m => (Member: m, Region: byId[m.PlanarRegionId!.Value]))
            .OrderBy(p => MeshSize(p.Member, p.Region, schemaPlateStepM))
            .ThenBy(p => p.Member.Id)
            .ThenBy(p => p.Member.ElemTag, StringComparer.Ordinal)
            .ToList();
    }

    /// <summary>
    /// Волны построения упорядоченных областей (<see cref="OrderedRegions"/>): касающиеся — стык по геометрии
    /// (<see cref="JunctionConstraints"/>) хотя бы в одну сторону; кандидаты — пары с пересекающимися габаритами
    /// (заметание по X, без перебора всех пар). Волна области (по порядку) — наименьшая, не занятая касающимися
    /// областями и строго больше волн касающихся областей с более мелкой сеткой (их узлы она встроит). Между равными
    /// по размеру КЭ порядок произволен — так стены, стоящие друг на друге, не выстраиваются в цепочку по этажам:
    /// число волн задают локальные касания, а не этажность.
    /// </summary>
    public static FemPlanarMeshWaves Waves(IReadOnlyList<(FemMember Member, PlanarRegion Region)> ordered,
        double? schemaPlateStepM = null)
    {
        int n = ordered.Count;
        var boxes = ordered.Select(p => Box(p.Region, 2 * MergeToleranceM)).ToArray();
        var byX = Enumerable.Range(0, n).OrderBy(i => boxes[i].X0).ToArray();
        var candidates = new List<(int A, int B)>();
        for (int a = 0; a < n; a++)
        {
            int i = byX[a];
            for (int b = a + 1; b < n && boxes[byX[b]].X0 <= boxes[i].X1; b++)
            {
                int j = byX[b];
                var (p, q) = (boxes[i], boxes[j]);
                if (p.Y0 <= q.Y1 && p.Y1 >= q.Y0 && p.Z0 <= q.Z1 && p.Z1 >= q.Z0) candidates.Add((Math.Min(i, j), Math.Max(i, j)));
            }
        }
        var options = DefaultOptions();
        var touches = new bool[candidates.Count];
        Parallel.For(0, candidates.Count, k =>
        {
            var (a, b) = candidates[k];
            touches[k] = JunctionConstraints(ordered[a].Region, "", ordered[b].Region, options).Count > 0 ||
                         JunctionConstraints(ordered[b].Region, "", ordered[a].Region, options).Count > 0;
        });
        var neighbours = Enumerable.Range(0, n).Select(_ => new List<int>()).ToArray();
        for (int k = 0; k < candidates.Count; k++)
            if (touches[k])
            {
                neighbours[candidates[k].A].Add(candidates[k].B);
                neighbours[candidates[k].B].Add(candidates[k].A);
            }
        var size = ordered.Select(p => MeshSize(p.Member, p.Region, schemaPlateStepM)).ToArray();
        var waveOf = new int[n];
        for (int i = 0; i < n; i++)
        {
            neighbours[i].Sort();
            var earlier = neighbours[i].Where(j => j < i).ToList();
            int wave = earlier.Where(j => size[j] < size[i]).Select(j => waveOf[j] + 1).DefaultIfEmpty(0).Max();
            var taken = earlier.Select(j => waveOf[j]).ToHashSet();
            while (taken.Contains(wave)) wave++;
            waveOf[i] = wave;
        }
        return new FemPlanarMeshWaves(waveOf, neighbours);
    }

    /// <summary>Габарит контуров области в глобальных осях, расширенный на <paramref name="margin"/>.</summary>
    static (double X0, double Y0, double Z0, double X1, double Y1, double Z1) Box(PlanarRegion region, double margin)
    {
        double x0 = double.PositiveInfinity, y0 = x0, z0 = x0, x1 = double.NegativeInfinity, y1 = x1, z1 = x1;
        foreach (var contour in region.Contours)
            for (int i = 0; i < contour.X.Count; i++)
            {
                var g = region.Frame.Origin + region.Frame.LocalX * contour.X[i] + region.Frame.LocalY * contour.Y[i];
                (x0, y0, z0) = (Math.Min(x0, g.X), Math.Min(y0, g.Y), Math.Min(z0, g.Z));
                (x1, y1, z1) = (Math.Max(x1, g.X), Math.Max(y1, g.Y), Math.Max(z1, g.Z));
            }
        return (x0 - margin, y0 - margin, z0 - margin, x1 + margin, y1 + margin, z1 + margin);
    }

    /// <summary>Допуски вывода по умолчанию — как у швов (см. <see cref="Constraints(int, IReadOnlyList{FemNode}, IReadOnlyList{FemMember}, FemPlanarMeshPrefix, PlanarRegion, PlanarConstraintDerivationOptions?, IReadOnlyList{ValueTuple{string, PlanarRegion}}?, RigidJunctionTraceSet?)"/>).</summary>
    static PlanarConstraintDerivationOptions DefaultOptions() =>
        new() { PlaneToleranceM = SeamToleranceM, GeometryToleranceM = SeamToleranceM };

    /// <summary>Сетка стержней для вывода ограничений: КонЭ-стержни, разбитые только узлами схемы (без дробления
    /// по целевой длине — она задаётся в редакторе и не хранится).</summary>
    public static (List<FemMeshNode> Nodes, List<FemElement> Elements) DerivationBeams(
        int schemaId, IReadOnlyList<FemNode> nodes, IReadOnlyList<FemMember> members) =>
        FemMeshDiscretizer.Discretize(schemaId, nodes,
            members.Where(m => m.ElemType == "beam" && !m.IsMeshLocked)
                .Select(m => new FemMember { Id = m.Id, ElemTag = m.ElemTag, ElemType = m.ElemType, NodeIdsJson = m.NodeIdsJson })
                .ToList(),
            null);

    /// <summary>
    /// Ограничения сетки области: вывод из топологии (узлы схемы, сетка <paramref name="prefixMesh"/> — стержни и
    /// пластины ранее построенных областей) + ручные объекты области + точки свободных узлов схемы (не принадлежащих
    /// ни одному КонЭ), лежащих в области, — опоры и сосредоточенные нагрузки на плите. Области, которые строятся
    /// позже (<paramref name="laterRegions"/>), дают линии стыка по геометрии: их сеток ещё нет, а узлы на линии стыка
    /// должны быть у обеих сторон (стена сквозь плиту) — поздняя область потом встроит узлы ранней.
    /// Следы АЖТ примыканий стержней (<paramref name="junctions"/>) вшиваются как Region-ограничения; в отпечаток входит
    /// только их геометрия — маска и вид тела на сетку не влияют.
    /// </summary>
    public static FemPlanarRegionConstraints Constraints(
        int schemaId,
        IReadOnlyList<FemNode> nodes,
        IReadOnlyList<FemMember> members,
        FemPlanarMaterializationResult prefixMesh,
        PlanarRegion region,
        PlanarConstraintDerivationOptions? options = null,
        IReadOnlyList<(string Tag, PlanarRegion Region)>? laterRegions = null,
        RigidJunctionTraceSet? junctions = null,
        IReadOnlyDictionary<string, RigidJunctionTraceSet?>? laterJunctions = null) =>
        Constraints(schemaId, nodes, members, new FemPlanarMeshPrefix(prefixMesh), region, options, laterRegions, junctions, laterJunctions);

    /// <inheritdoc cref="Constraints(int, IReadOnlyList{FemNode}, IReadOnlyList{FemMember}, FemPlanarMaterializationResult, PlanarRegion, PlanarConstraintDerivationOptions?, IReadOnlyList{ValueTuple{string, PlanarRegion}}?, RigidJunctionTraceSet?)"/>
    /// <remarks>Чистая функция (префикс только читается) — области одной волны выводятся параллельно.</remarks>
    public static FemPlanarRegionConstraints Constraints(
        int schemaId,
        IReadOnlyList<FemNode> nodes,
        IReadOnlyList<FemMember> members,
        FemPlanarMeshPrefix prefix,
        PlanarRegion region,
        PlanarConstraintDerivationOptions? options = null,
        IReadOnlyList<(string Tag, PlanarRegion Region)>? laterRegions = null,
        RigidJunctionTraceSet? junctions = null,
        IReadOnlyDictionary<string, RigidJunctionTraceSet?>? laterJunctions = null)
    {
        // Допуски плоскости и геометрии — как у швов: узлы готовой сетки соседней области на стыке отходят от плоскости и кромки этой на
        // микроны (импортные контуры «Музея»: плита и стоящая на её кромке стена — до 6 мкм), и при 10⁻⁸ из 42 м стыка
        // выводилось 2,6 мм — сетки расходились (висящие узлы).
        options ??= DefaultOptions();
        var diagnostics = new List<FemValidationDiagnostic>();
        var prefixMesh = prefix.Mesh;

        // Id узлов, КонЭ и КЭ — синтетические, по порядку тегов: выведенные ограничения именуются по Id, а отпечаток не
        // должен зависеть от Id строк в БД (меняются при пересохранении схемы) и от того, сохранена ли сессия редактора.
        // КЭ префикса, которые могут дать ограничение области: касаются её плоскости и габарита (с допусками вывода).
        // Остальные ничего не выводят, но разбирались бы и входили в отпечаток — у фрагмента «Музея» 80 тыс. КЭ на
        // каждую из 88 областей, и правка далёкой области устаревала бы сетки всех.
        var relevant = RelevantElements(prefix, region, options, out var relevantNodes);
        int synthetic = 0;
        var topologyNodes = new Dictionary<string, FemNode>(StringComparer.Ordinal);
        foreach (var node in nodes.OrderBy(n => n.NodeTag, StringComparer.Ordinal))
            topologyNodes.TryAdd(node.NodeTag, new FemNode
            {
                Id = ++synthetic, SchemaId = schemaId, NodeTag = node.NodeTag, X = node.X, Y = node.Y, Z = node.Z,
            });
        // Узлы отобранных КЭ — в порядке сетки префикса.
        foreach (var meshNode in relevantNodes.Select(i => prefixMesh.Nodes[i]))
        {
            if (topologyNodes.TryGetValue(meshNode.NodeTag, out var source))
            {
                if (Math.Abs(source.X - meshNode.X) > 1e-9 || Math.Abs(source.Y - meshNode.Y) > 1e-9 ||
                    Math.Abs(source.Z - meshNode.Z) > 1e-9)
                    diagnostics.Add(new("planar_constraint_node_tag_coordinate_conflict",
                        $"Тег узла сетки '{meshNode.NodeTag}' совпадает с тегом узла схемы с другими координатами."));
                continue;
            }
            topologyNodes.Add(meshNode.NodeTag, new FemNode
            {
                Id = ++synthetic, SchemaId = schemaId, NodeTag = meshNode.NodeTag, X = meshNode.X, Y = meshNode.Y, Z = meshNode.Z,
            });
        }
        int elementId = 0;
        var elements = relevant.Select(e => new FemElement
        {
            Id = ++elementId, SchemaId = schemaId, ElemTag = e.ElemTag, ElemType = e.ElemType,
            NodeIdsJson = e.NodeIdsJson, SourceMemberTag = e.SourceMemberTag, Origin = e.Origin,
        }).ToList();

        int memberId = 0;
        var topologyMembers = members.OrderBy(m => m.ElemTag, StringComparer.Ordinal).Select(m => new FemMember
        {
            Id = ++memberId, SchemaId = schemaId, ElemTag = m.ElemTag, ElemType = m.ElemType, NodeIdsJson = m.NodeIdsJson,
            PlanarRegionId = m.PlanarRegionId, MeshSource = m.MeshSource,
        }).ToList();
        var topology = new FemSchemaTopology(schemaId, topologyNodes.Values.ToList(), topologyMembers, elements);
        var derived = PlanarConstraintDeriver.Derive(topology, region, options);
        diagnostics.AddRange(derived.Diagnostics);

        var constraints = region.ConstraintObjects.ToList();
        var ids = constraints.Select(c => c.Id).ToHashSet(StringComparer.Ordinal);
        foreach (var constraint in derived.Constraints.OrderBy(c => c.Id, StringComparer.Ordinal))
        {
            if (!ids.Add(constraint.Id))
            {
                diagnostics.Add(new("planar_constraint_derived_id_duplicate",
                    $"Выведенное ограничение '{constraint.Id}' совпадает с ручным объектом области."));
                continue;
            }
            if (SnapDerivedToContour(constraint, region, options.GeometryToleranceM))
                constraints.Add(constraint);
        }

        int free = 0;
        var used = new HashSet<int>();
        foreach (var member in members)
            foreach (var id in ReadIds(member.NodeIdsJson)) used.Add(id);
        // Точки свободных узлов добавляются последними: их привязывают к уже собранным ограничениям и контуру.
        var freeNodes = new List<(string Id, FemNode Node, double U, double V)>();
        foreach (var node in nodes.OrderBy(n => n.NodeTag, StringComparer.Ordinal))
        {
            if (!int.TryParse(node.NodeTag, NumberStyles.Integer, CultureInfo.InvariantCulture, out var tag) || used.Contains(tag))
                continue;
            var delta = new PlanarVector3(node.X, node.Y, node.Z) - region.Frame.Origin;
            double u = delta.Dot(region.Frame.LocalX), v = delta.Dot(region.Frame.LocalY), w = delta.Dot(region.Frame.LocalZ);
            // Узел на стыке может стоять в микронах за кромкой (импортные контуры «Музея»: до 6 мкм) — соседняя область
            // его берёт, и эта должна, иначе сетки на стыке разойдутся; точка потом привязывается к кромке.
            if (Math.Abs(w) > options.PlaneToleranceM ||
                !InsideRegion(u, v, region, Math.Max(options.GeometryToleranceM, SeamToleranceM))) continue;
            var id = $"free-node:{node.NodeTag}";
            if (!ids.Add(id)) continue;
            freeNodes.Add((id, node, u, v));
        }

        var seams = new List<PlanarConstraintObject>();
        foreach (var (tag, other) in laterRegions ?? [])
        {
            seams.AddRange(JunctionConstraints(region, tag, other, options));
            if (laterJunctions?.GetValueOrDefault(tag) is { } traces)
                seams.AddRange(TracePoints(region, tag, other, traces, options));
        }
        var fingerprint = derived.SourceFingerprint;
        if (seams.Count > 0)
        {
            foreach (var junction in seams)
                if (ids.Add(junction.Id)) constraints.Add(junction);
            var text = string.Join("|", seams.Select(j =>
                j.Id + ":" + string.Join(";", j.Geometry.Points.Select(p => p.U.ToString("G17", CultureInfo.InvariantCulture) + "," +
                                                                         p.V.ToString("G17", CultureInfo.InvariantCulture)))));
            fingerprint = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(
                System.Text.Encoding.UTF8.GetBytes(fingerprint + "|junctions:" + text))).ToLowerInvariant();
        }

        int rigid = 0;
        if (junctions != null)
        {
            diagnostics.AddRange(junctions.Diagnostics);
            var traces = junctions.ToConstraints();
            foreach (var trace in traces)
            {
                if (!ids.Add(trace.Id))
                {
                    diagnostics.Add(new("planar_constraint_derived_id_duplicate",
                        $"След АЖТ '{trace.Id}' совпадает с другим ограничением области."));
                    continue;
                }
                constraints.Add(trace);
            }
            rigid = junctions.Traces.Count;
            if (traces.Count > 0)
            {
                var text = string.Join("|", traces.Select(t =>
                    t.Id + ":" + string.Join(";", t.Geometry.Points.Select(p => p.U.ToString("G17", CultureInfo.InvariantCulture) + "," +
                                                                       p.V.ToString("G17", CultureInfo.InvariantCulture)))));
                fingerprint = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(
                    System.Text.Encoding.UTF8.GetBytes(fingerprint + "|rigid:" + text))).ToLowerInvariant();
            }
        }

        var snapped = new List<string>();
        foreach (var (id, node, u0, v0) in freeNodes)
        {
            var (u, v) = SnapFreePoint((u0, v0), constraints, region);
            if (u != u0 || v != v0)
                snapped.Add(id + ":" + u.ToString("G17", CultureInfo.InvariantCulture) + "," + v.ToString("G17", CultureInfo.InvariantCulture));
            var point = PlanarConstraintObject.Point(id, new PlanarPoint2D(u, v),
                new PlanarStructuralFacet(PlanarStructuralKind.None), new PlanarMeshFacet(PlanarMeshKind.EmbeddedPoint), id);
            point.IsDerived = true;
            point.ToleranceM = options.GeometryToleranceM;
            point.SourceReferences = [new PlanarSourceReference(0, "", [], [], [topologyNodes[node.NodeTag].Id], [node.NodeTag])];
            constraints.Add(point);
            free++;
        }
        if (snapped.Count > 0)
            fingerprint = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(
                System.Text.Encoding.UTF8.GetBytes(fingerprint + "|free-snap:" + string.Join("|", snapped)))).ToLowerInvariant();
        // Допуск слияния вершин меняет сетку при той же геометрии: снимки с прежним допуском устаревают.
        fingerprint = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(
            fingerprint + "|merge:" + MergeToleranceM.ToString("G17", CultureInfo.InvariantCulture)))).ToLowerInvariant();

        return new FemPlanarRegionConstraints(constraints, fingerprint, diagnostics, derived, free, seams.Count, rigid);
    }

    /// <summary>
    /// Точка свободного узла ближе <see cref="FemMeshDiscretizer.CollinearToleranceM"/> к другому ограничению области
    /// (точке, линии, контуру следа) или к её контуру — на него: иначе Gmsh дробит сетку до долей миллиметра между ними
    /// (узловые нагрузки SCAD у кромок и швов фрагмента «Музея» — до получаса на плиту). Узел схемы и узел сетки остаются
    /// в пределах того же допуска — материализация сопоставляет их как прежде.
    /// </summary>
    static (double U, double V) SnapFreePoint((double U, double V) p, IReadOnlyList<PlanarConstraintObject> constraints, PlanarRegion region)
    {
        const double snap = FemMeshDiscretizer.CollinearToleranceM;
        (double U, double V) best = p;
        double bestD = snap * snap;
        bool atPoint = false;
        void Try((double U, double V) q, bool isPoint)
        {
            double d = (p.U - q.U) * (p.U - q.U) + (p.V - q.V) * (p.V - q.V);
            // Точка (вершина) важнее отрезка рядом с ней: иначе остаётся крошечное ребро до неё.
            if (isPoint ? d <= snap * snap && (!atPoint || d < bestD) : !atPoint && d < bestD)
            {
                (best, bestD) = (q, d);
                atPoint |= isPoint;
            }
        }
        void Segments(IReadOnlyList<(double U, double V)> points, bool closed)
        {
            foreach (var q in points) Try(q, true);
            int n = closed ? points.Count : points.Count - 1;
            for (int i = 0; i < n; i++)
            {
                var a = points[i];
                var b = points[(i + 1) % points.Count];
                double rx = b.U - a.U, ry = b.V - a.V, l2 = rx * rx + ry * ry;
                if (l2 == 0) continue;
                double t = Math.Clamp(((p.U - a.U) * rx + (p.V - a.V) * ry) / l2, 0, 1);
                Try((a.U + t * rx, a.V + t * ry), false);
            }
        }
        // Другие свободные узлы — не цель: два узла схемы в одном узле сетки потеряли бы нагрузку одного из них.
        foreach (var c in constraints.Where(c => !c.Id.StartsWith("free-node:", StringComparison.Ordinal)))
            Segments(c.Geometry.Points.Select(q => (q.U, q.V)).ToList(), c.Geometry.Kind == PlanarConstraintGeometryKind.Region);
        foreach (var contour in region.Contours)
        {
            var (x, y) = PlanarRegionTopologyValidator.ToOpenLoop(contour.X, contour.Y);
            Segments(Enumerable.Range(0, x.Length).Select(i => (x[i], y[i])).ToList(), closed: true);
        }
        return best;
    }

    /// <summary>
    /// Линии (точки) стыка области <paramref name="region"/> с областью <paramref name="other"/> по геометрии: пересечение
    /// многоугольника <paramref name="other"/> с плоскостью области, обрезанное ею; рёбра <paramref name="other"/>, лежащие
    /// в плоскости, — целиком; у компланарной соседки — её вершины в области (угол соседней плиты на кромке).
    /// </summary>
    static List<PlanarConstraintObject> JunctionConstraints(PlanarRegion region, string tag,
        PlanarRegion other, PlanarConstraintDerivationOptions options)
    {
        double planeTol = options.PlaneToleranceM, tol = options.GeometryToleranceM;
        var frame = region.Frame;
        (double U, double V, double W) Local(double u, double v)
        {
            var g = other.Frame.Origin + other.Frame.LocalX * u + other.Frame.LocalY * v;
            var d = g - frame.Origin;
            return (d.Dot(frame.LocalX), d.Dot(frame.LocalY), d.Dot(frame.LocalZ));
        }
        var loops = other.Contours.Select(c =>
        {
            var (x, y) = PlanarRegionTopologyValidator.ToOpenLoop(c.X, c.Y);
            return Enumerable.Range(0, x.Length).Select(i => Local(x[i], y[i])).ToArray();
        }).Where(l => l.Length >= 3).ToList();

        var result = new List<PlanarConstraintObject>();
        void AddPoint(double u, double v)
        {
            if (!InsideRegion(u, v, region, Math.Max(tol, MergeToleranceM))) return;
            (u, v) = SnapToContour((u, v), region, tol);
            var id = $"region-junction:{tag}:p{result.Count}";
            var point = PlanarConstraintObject.Point(id, new PlanarPoint2D(u, v),
                new PlanarStructuralFacet(PlanarStructuralKind.None), new PlanarMeshFacet(PlanarMeshKind.EmbeddedPoint), id);
            point.IsDerived = true;
            point.ToleranceM = Math.Max(tol, MergeToleranceM);
            result.Add(point);
        }
        void AddSegment((double U, double V) a, (double U, double V) b)
        {
            var pieces = Clip(a, b, region, tol);
            if (pieces.Count == 0)
            {
                // Соседка касается области только концом следа (стена, упёртая в кромку плиты, — «Музей», · 55 у · 84):
                // узел там есть у неё — должен быть и здесь.
                AddPoint(a.U, a.V);
                AddPoint(b.U, b.V);
                return;
            }
            foreach (var (p0, q0) in pieces)
            {
                var (p, q) = (SnapToContour(p0, region, tol), SnapToContour(q0, region, tol));
                // Обрезок за кромкой длиной в доли микрона после привязки вырождается в точку.
                if (Math.Abs(p.U - q.U) + Math.Abs(p.V - q.V) <= 1e-6) continue;
                var id = $"region-junction:{tag}:c{result.Count}";
                var curve = PlanarConstraintObject.Curve(id, [new PlanarPoint2D(p.U, p.V), new PlanarPoint2D(q.U, q.V)],
                    new PlanarStructuralFacet(PlanarStructuralKind.None), new PlanarMeshFacet(PlanarMeshKind.ConformingPartition), id);
                curve.IsDerived = true;
                curve.ToleranceM = Math.Max(tol, MergeToleranceM);
                result.Add(curve);
            }
        }

        if (loops.All(l => l.All(p => Math.Abs(p.W) <= planeTol)))
        {
            // Компланарная соседка: общая кромка — граница обеих областей; её вершины на кромке делят контур.
            foreach (var p in loops.SelectMany(l => l)) AddPoint(p.U, p.V);
            return result;
        }

        int Sign(double w) => Math.Abs(w) <= planeTol ? 0 : Math.Sign(w);
        var crossings = new List<(double U, double V)>();
        foreach (var loop in loops)
        {
            int n = loop.Length;
            for (int i = 0; i < n; i++)
            {
                var a = loop[i];
                var b = loop[(i + 1) % n];
                int sa = Sign(a.W), sb = Sign(b.W);
                if (sa == 0 && sb == 0) { AddSegment((a.U, a.V), (b.U, b.V)); continue; }
                if (sa * sb < 0)
                {
                    var t = a.W / (a.W - b.W);
                    crossings.Add((a.U + t * (b.U - a.U), a.V + t * (b.V - a.V)));
                }
                if (sa != 0) continue;
                // Вершина в плоскости: пересечение, если соседние вершины (вне плоскости) по разные стороны; касание —
                // точка. Вершины рёбер, лежащих в плоскости, уже учтены отрезком.
                int prev = Sign(loop[(i - 1 + n) % n].W);
                if (prev == 0 || sb == 0) continue;
                if (prev * sb < 0) crossings.Add((a.U, a.V));
                else AddPoint(a.U, a.V);
            }
        }
        if (crossings.Count >= 2)
        {
            // Пересечения лежат на одной прямой: упорядочить вдоль неё и соединить парами (вход — выход).
            var origin = crossings[0];
            var far = crossings.MaxBy(p => (p.U - origin.U) * (p.U - origin.U) + (p.V - origin.V) * (p.V - origin.V));
            double du = far.U - origin.U, dv = far.V - origin.V;
            var sorted = crossings.OrderBy(p => (p.U - origin.U) * du + (p.V - origin.V) * dv).ToList();
            for (int i = 0; i + 1 < sorted.Count; i += 2)
                AddSegment(sorted[i], sorted[i + 1]);
        }
        return result;
    }

    /// <summary>
    /// Вершины следов АЖТ поздней области <paramref name="other"/>, лежащие на этой (в плоскости, внутри или на контуре), —
    /// точки стыка: поздняя область вшивает след, и его вершины на общей кромке стали бы узлами только у неё (висящие
    /// узлы). Фрагмент «Музея»: балка в плоскости плиты без сечения упирается в стену — для стены она «колонна» с условным
    /// квадратом, его углы на кромке плиты, а плита полосы ребра не строит.
    /// </summary>
    static List<PlanarConstraintObject> TracePoints(PlanarRegion region, string tag, PlanarRegion other,
        RigidJunctionTraceSet traces, PlanarConstraintDerivationOptions options)
    {
        double tol = Math.Max(options.GeometryToleranceM, MergeToleranceM);
        var frame = region.Frame;
        var result = new List<PlanarConstraintObject>();
        var added = new List<(double U, double V)>();
        foreach (var q in traces.Traces.SelectMany(t => t.Pieces).SelectMany(piece => piece))
        {
            var d = other.Frame.Origin + other.Frame.LocalX * q.U + other.Frame.LocalY * q.V - frame.Origin;
            double u = d.Dot(frame.LocalX), v = d.Dot(frame.LocalY), w = d.Dot(frame.LocalZ);
            if (Math.Abs(w) > options.PlaneToleranceM || !InsideRegion(u, v, region, tol)) continue;
            (u, v) = SnapToContour((u, v), region, options.GeometryToleranceM);
            if (added.Any(a => Math.Abs(a.U - u) <= tol && Math.Abs(a.V - v) <= tol)) continue;
            added.Add((u, v));
            var id = $"region-junction:{tag}:t{result.Count}";
            var point = PlanarConstraintObject.Point(id, new PlanarPoint2D(u, v),
                new PlanarStructuralFacet(PlanarStructuralKind.None), new PlanarMeshFacet(PlanarMeshKind.EmbeddedPoint), id);
            point.IsDerived = true;
            point.ToleranceM = tol;
            result.Add(point);
        }
        return result;
    }

    /// <summary>Части отрезка внутри области (или на её контуре).</summary>
    static List<((double U, double V) P, (double U, double V) Q)> Clip((double U, double V) a, (double U, double V) b,
        PlanarRegion region, double tol)
    {
        double rx = b.U - a.U, ry = b.V - a.V;
        var length = Math.Sqrt(rx * rx + ry * ry);
        var result = new List<((double U, double V) P, (double U, double V) Q)>();
        if (length <= tol) return result;
        var ts = new List<double> { 0, 1 };
        foreach (var contour in region.Contours)
        {
            var (x, y) = PlanarRegionTopologyValidator.ToOpenLoop(contour.X, contour.Y);
            for (int i = 0; i < x.Length; i++)
            {
                int j = (i + 1) % x.Length;
                double sx = x[j] - x[i], sy = y[j] - y[i];
                double qx = x[i] - a.U, qy = y[i] - a.V;
                var den = rx * sy - ry * sx;
                if (Math.Abs(den) <= 1e-12 * length * Math.Sqrt(sx * sx + sy * sy))
                {
                    // Коллинеарный участок контура — его концы, лежащие на отрезке.
                    if (Math.Abs(qx * ry - qy * rx) > tol * length) continue;
                    foreach (var (px, py) in new[] { (x[i], y[i]), (x[j], y[j]) })
                    {
                        var tc = ((px - a.U) * rx + (py - a.V) * ry) / (length * length);
                        if (tc > 0 && tc < 1) ts.Add(tc);
                    }
                    continue;
                }
                var t = (qx * sy - qy * sx) / den;
                var u = (qx * ry - qy * rx) / den;
                if (t > 0 && t < 1 && u >= -1e-12 && u <= 1 + 1e-12) ts.Add(t);
            }
        }
        ts.Sort();
        for (int i = 0; i + 1 < ts.Count; i++)
        {
            double t0 = ts[i], t1 = ts[i + 1];
            if ((t1 - t0) * length <= tol) continue;
            var tm = (t0 + t1) / 2;
            if (!InsideRegion(a.U + tm * rx, a.V + tm * ry, region, tol)) continue;
            result.Add(((a.U + t0 * rx, a.V + t0 * ry), (a.U + t1 * rx, a.V + t1 * ry)));
        }
        return result;
    }

    /// <summary>
    /// Точка ближе max(<paramref name="tol"/>, <see cref="MergeToleranceM"/>) к контуру области (внешнему или отверстию) — на контур: вершина,
    /// иначе проекция на ребро. Импортные контуры соседних областей расходятся от долей микрона (у «Музея» конец шва
    /// стены на 1,7·10⁻⁸ м за кромкой плиты при допуске вывода 10⁻⁸ — проверка ограничений области точная) до десятых
    /// миллиметра (конец шва в 0,18 мм от угла — КЭ со стороной 0,18 мм, вырожденный после слияния узлов).
    /// </summary>
    static (double U, double V) SnapToContour((double U, double V) p, PlanarRegion region, double tol)
    {
        (double U, double V) best = p;
        double snap = Math.Max(tol, MergeToleranceM), bestD = snap * snap;
        foreach (var contour in region.Contours)
        {
            var (x, y) = PlanarRegionTopologyValidator.ToOpenLoop(contour.X, contour.Y);
            for (int i = 0; i < x.Length; i++)
            {
                double dx = p.U - x[i], dy = p.V - y[i];
                if (dx * dx + dy * dy <= bestD) return (x[i], y[i]);
            }
            for (int i = 0; i < x.Length; i++)
            {
                int j = (i + 1) % x.Length;
                double rx = x[j] - x[i], ry = y[j] - y[i], l2 = rx * rx + ry * ry;
                if (l2 == 0) continue;
                double t = Math.Clamp(((p.U - x[i]) * rx + (p.V - y[i]) * ry) / l2, 0, 1);
                double qx = x[i] + t * rx, qy = y[i] + t * ry, d = (p.U - qx) * (p.U - qx) + (p.V - qy) * (p.V - qy);
                if (d < bestD) (best, bestD) = ((qx, qy), d);
            }
        }
        return best;
    }

    /// <summary>
    /// Выведенное ограничение (узлы и КЭ схемы и ранних областей) — к контуру области, как швы (<see cref="SnapToContour"/>):
    /// узел соседней сетки в 1,3 мкм от угла контура (фрагмент «Музея», стена · 89) валидатор считал «вне области», а
    /// Gmsh дал бы отрезок в микроны. Допуск ограничения — не меньше <see cref="MergeToleranceM"/>: построитель .geo
    /// сливает близкие вершины, сопоставление с сеткой идёт в том же допуске. Совпавшие после привязки соседние вершины
    /// кривой — одна; кривая, ставшая точкой, отбрасывается (false).
    /// </summary>
    static bool SnapDerivedToContour(PlanarConstraintObject constraint, PlanarRegion region, double tol)
    {
        var points = new List<PlanarPoint2D>();
        foreach (var p in constraint.Geometry.Points)
        {
            var (u, v) = SnapToContour((p.U, p.V), region, tol);
            var q = u == p.U && v == p.V ? p : new PlanarPoint2D(u, v);
            if (points.Count == 0 || points[^1].U != q.U || points[^1].V != q.V) points.Add(q);
        }
        if (constraint.Geometry.Kind == PlanarConstraintGeometryKind.Curve && points.Count < 2) return false;
        constraint.Geometry = constraint.Geometry with { Points = points };
        constraint.ToleranceM = Math.Max(constraint.ToleranceM, MergeToleranceM);
        return true;
    }

    /// <summary>
    /// Допуск плоскости стыка, м: узлы готовой сетки соседней области и свободные узлы на стыке отходят от плоскости и
    /// кромки этой области на микроны (импортные контуры «Музея» — до 6 мкм); 0,1 мм — с запасом.
    /// </summary>
    public const double SeamToleranceM = 1e-4;

    /// <summary>
    /// Допуск слияния вершин швов и выведенных ограничений, м, — тот же, что у слияния узлов сетки схемы
    /// (<see cref="FemMeshDiscretizer.CollinearToleranceM"/>): вершины ближе него всё равно станут одним узлом, и КЭ
    /// между ними выродится. Концы швов двух стен и кромка плиты в одном углу расходятся от долей микрона (Gmsh висел
    /// на отрезке 0,76 мкм десятки минут, плита +10,090 «Музея») до 0,18 мм; с этим допуском построитель .geo сливает
    /// такие вершины, а сопоставление узлов сетки с ограничением идёт в том же допуске.
    /// </summary>
    public const double MergeToleranceM = FemMeshDiscretizer.CollinearToleranceM;

    /// <summary>КЭ сетки, узлы которых лежат по обе стороны плоскости области или в ней (± допуск плоскости), а габарит
    /// в осях области пересекает габарит её внешнего контура (± допуск геометрии); КЭ с неизвестными узлами — тоже
    /// (их разберёт вывод). Кандидаты — из индекса префикса по глобальному габариту слоя «габарит контура ± допуск
    /// геометрии × ± допуск плоскости»: КЭ, касающийся области, его пересекает. <paramref name="nodes"/> — индексы
    /// узлов отобранных КЭ в сетке префикса, по возрастанию.</summary>
    static List<FemElement> RelevantElements(FemPlanarMeshPrefix prefix, PlanarRegion region,
        PlanarConstraintDerivationOptions options, out List<int> nodes)
    {
        var mesh = prefix.Mesh;
        double planeTol = options.PlaneToleranceM, tol = options.GeometryToleranceM;
        double u0 = double.NegativeInfinity, u1 = double.PositiveInfinity, v0 = double.NegativeInfinity, v1 = double.PositiveInfinity;
        if (region.Hull is { } hull && hull.X.Count > 0)
            (u0, u1, v0, v1) = (hull.X.Min() - tol, hull.X.Max() + tol, hull.Y.Min() - tol, hull.Y.Max() + tol);
        var frame = region.Frame;
        double x0 = double.PositiveInfinity, y0 = x0, z0 = x0, x1 = double.NegativeInfinity, y1 = x1, z1 = x1;
        foreach (var u in new[] { u0, u1 })
            foreach (var v in new[] { v0, v1 })
                foreach (var w in new[] { -planeTol, planeTol })
                {
                    var g = frame.Origin + frame.LocalX * u + frame.LocalY * v + frame.LocalZ * w;
                    (x0, y0, z0) = (Math.Min(x0, g.X), Math.Min(y0, g.Y), Math.Min(z0, g.Z));
                    (x1, y1, z1) = (Math.Max(x1, g.X), Math.Max(y1, g.Y), Math.Max(z1, g.Z));
                }
        var nodeSet = new HashSet<int>();
        var result = new List<FemElement>();
        foreach (var e in prefix.Candidates(x0, y0, z0, x1, y1, z1))
        {
            var ids = prefix.ElementIds[e];
            bool keep = ids.Length == 0;
            double wMin = double.PositiveInfinity, wMax = double.NegativeInfinity;
            double uMin = double.PositiveInfinity, uMax = double.NegativeInfinity, vMin = double.PositiveInfinity, vMax = double.NegativeInfinity;
            foreach (var id in ids)
            {
                if (!prefix.NodeIndex.TryGetValue(id.ToString(CultureInfo.InvariantCulture), out var n)) { keep = true; break; }
                var node = mesh.Nodes[n];
                var d = new PlanarVector3(node.X, node.Y, node.Z) - frame.Origin;
                double u = d.Dot(frame.LocalX), v = d.Dot(frame.LocalY), w = d.Dot(frame.LocalZ);
                (wMin, wMax) = (Math.Min(wMin, w), Math.Max(wMax, w));
                (uMin, uMax, vMin, vMax) = (Math.Min(uMin, u), Math.Max(uMax, u), Math.Min(vMin, v), Math.Max(vMax, v));
            }
            if (!keep && (wMin > planeTol || wMax < -planeTol || uMax < u0 || uMin > u1 || vMax < v0 || vMin > v1)) continue;
            result.Add(mesh.Elements[e]);
            foreach (var id in ids)
                if (prefix.NodeIndex.TryGetValue(id.ToString(CultureInfo.InvariantCulture), out var n)) nodeSet.Add(n);
        }
        nodes = [.. nodeSet];
        nodes.Sort();
        return result;
    }

    internal static int[] ReadIds(string json)
    {
        try { return JsonSerializer.Deserialize<int[]>(json) ?? []; }
        catch (JsonException) { return []; }
    }

    /// <summary>Точка в области: внутри или на внешнем контуре и не строго внутри отверстия.</summary>
    static bool InsideRegion(double u, double v, PlanarRegion region, double tolerance)
    {
        if (region.Hull is not { } hull || !InsideOrOn(u, v, hull, tolerance)) return false;
        return !region.Holes.Any(hole => InsideOrOn(u, v, hole, -1) && !OnBoundary(u, v, hole, tolerance));
    }

    static bool InsideOrOn(double u, double v, Contour contour, double tolerance)
    {
        if (tolerance >= 0 && OnBoundary(u, v, contour, tolerance)) return true;
        var (x, y) = PlanarRegionTopologyValidator.ToOpenLoop(contour.X, contour.Y);
        var inside = false;
        for (int i = 0, j = x.Length - 1; i < x.Length; j = i++)
            if ((y[i] > v) != (y[j] > v) && u < (x[j] - x[i]) * (v - y[i]) / (y[j] - y[i]) + x[i])
                inside = !inside;
        return inside;
    }

    static bool OnBoundary(double u, double v, Contour contour, double tolerance)
    {
        var (x, y) = PlanarRegionTopologyValidator.ToOpenLoop(contour.X, contour.Y);
        for (int i = 0; i < x.Length; i++)
        {
            int j = (i + 1) % x.Length;
            double rx = x[j] - x[i], ry = y[j] - y[i];
            var lengthSquared = rx * rx + ry * ry;
            var t = lengthSquared == 0 ? 0 : Math.Clamp(((u - x[i]) * rx + (v - y[i]) * ry) / lengthSquared, 0, 1);
            double dx = u - (x[i] + t * rx), dy = v - (y[i] + t * ry);
            if (dx * dx + dy * dy <= tolerance * tolerance) return true;
        }
        return false;
    }
}
