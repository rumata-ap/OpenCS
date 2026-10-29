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
/// Общий для команды «Построить сетку схемы» и диалога области порядок построения сеток областей и вход вывода
/// их ограничений (CSfea 4г). Вход не зависит от дробления стержней по длине и от Id строк сетки в БД — отпечаток
/// снимка стабилен между сборками. Область k видит узлы схемы, стержни, разбитые только узлами схемы, и пластины
/// областей 1…k−1 (поздняя область встраивает узлы ранних — стыки совпадают).
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
        RigidJunctionTraceSet? junctions = null)
    {
        options ??= new PlanarConstraintDerivationOptions();
        var diagnostics = new List<FemValidationDiagnostic>();

        // Id узлов, КонЭ и КЭ — синтетические, по порядку тегов: выведенные ограничения именуются по Id, а отпечаток не
        // должен зависеть от Id строк в БД (меняются при пересохранении схемы) и от того, сохранена ли сессия редактора.
        int synthetic = 0;
        var topologyNodes = new Dictionary<string, FemNode>(StringComparer.Ordinal);
        foreach (var node in nodes.OrderBy(n => n.NodeTag, StringComparer.Ordinal))
            topologyNodes.TryAdd(node.NodeTag, new FemNode
            {
                Id = ++synthetic, SchemaId = schemaId, NodeTag = node.NodeTag, X = node.X, Y = node.Y, Z = node.Z,
            });
        foreach (var meshNode in prefixMesh.Nodes)
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
        var elements = prefixMesh.Elements.Select(e => new FemElement
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
            if (Math.Abs(w) > options.PlaneToleranceM || !InsideRegion(u, v, region, options.GeometryToleranceM)) continue;
            var id = $"free-node:{node.NodeTag}";
            if (!ids.Add(id)) continue;
            freeNodes.Add((id, node, u, v));
        }

        var seams = new List<PlanarConstraintObject>();
        foreach (var (tag, other) in laterRegions ?? [])
            seams.AddRange(JunctionConstraints(region, tag, other, options));
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
            if (!InsideRegion(u, v, region, tol)) return;
            (u, v) = SnapToContour((u, v), region, tol);
            var id = $"region-junction:{tag}:p{result.Count}";
            var point = PlanarConstraintObject.Point(id, new PlanarPoint2D(u, v),
                new PlanarStructuralFacet(PlanarStructuralKind.None), new PlanarMeshFacet(PlanarMeshKind.EmbeddedPoint), id);
            point.IsDerived = true;
            point.ToleranceM = Math.Max(tol, SeamToleranceM);
            result.Add(point);
        }
        void AddSegment((double U, double V) a, (double U, double V) b)
        {
            foreach (var (p0, q0) in Clip(a, b, region, tol))
            {
                var (p, q) = (SnapToContour(p0, region, tol), SnapToContour(q0, region, tol));
                // Обрезок за кромкой длиной в доли микрона после привязки вырождается в точку.
                if (Math.Abs(p.U - q.U) + Math.Abs(p.V - q.V) <= 1e-6) continue;
                var id = $"region-junction:{tag}:c{result.Count}";
                var curve = PlanarConstraintObject.Curve(id, [new PlanarPoint2D(p.U, p.V), new PlanarPoint2D(q.U, q.V)],
                    new PlanarStructuralFacet(PlanarStructuralKind.None), new PlanarMeshFacet(PlanarMeshKind.ConformingPartition), id);
                curve.IsDerived = true;
                curve.ToleranceM = Math.Max(tol, SeamToleranceM);
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
    /// Точка ближе max(<paramref name="tol"/>, <see cref="SeamToleranceM"/>) к контуру области (внешнему или отверстию) — на контур: вершина,
    /// иначе проекция на ребро. Импортные контуры соседних областей расходятся на доли микрона (у «Музея» конец шва
    /// стены на 1,7·10⁻⁸ м за кромкой плиты при допуске вывода 10⁻⁸), а проверка ограничений области точная.
    /// </summary>
    static (double U, double V) SnapToContour((double U, double V) p, PlanarRegion region, double tol)
    {
        (double U, double V) best = p;
        double snap = Math.Max(tol, SeamToleranceM), bestD = snap * snap;
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
    /// Допуск линий и точек стыка, м: импортные контуры соседних областей расходятся на микроны, и концы швов двух стен
    /// и кромка плиты в одном углу дают отрезки длиной в доли микрона — Gmsh на них виснет на десятки минут и выдаёт
    /// вырожденные КЭ (фрагмент «Музея», плита +10,090: 0,76 мкм при шаге 0,5 м). С этим допуском построитель .geo
    /// сливает такие вершины, а сопоставление узлов сетки с ограничением идёт в том же допуске.
    /// </summary>
    public const double SeamToleranceM = 1e-5;

    static int[] ReadIds(string json)
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
