using System.Text.Json;
using CScore.Fem;
using CScore.Planar;

namespace CScore.Import;

/// <summary>Набор КЭ сетки, из которого собираются конструктивные элементы.</summary>
/// <param name="Tag">Имя элемента; если частей несколько — «Имя · 1», «Имя · 2»…</param>
/// <param name="Kind">Вид плоских частей: "plate" | "wall"; null — по геометрии.</param>
/// <param name="KindSource">Источник заданного <paramref name="Kind"/> (<see cref="FemMember.KindSource"/>).</param>
public sealed record MeshMemberRequest(string Tag, string? Kind, IReadOnlyList<string> ElementTags, string KindSource = "manual");

/// <summary>Конструктивный элемент, собранный из части набора КЭ, и КЭ сетки, которые он покрывает.</summary>
public sealed record MeshMemberPart(FemMember Member, PlanarRegion? Region, IReadOnlyList<string> ElementTags);

/// <summary>Результат сборки: узлы концов стержней, элементы и диагностика.</summary>
public sealed record MeshMemberBuild(
    IReadOnlyList<FemNode> Nodes,
    IReadOnlyList<MeshMemberPart> Parts,
    IReadOnlyList<FemValidationDiagnostic> Diagnostics);

/// <summary>
/// Собирает конструктивные элементы из набора КЭ сохранённой сетки схемы (кБ ЛИРЫ, выбор пользователя, группа КЭ):
/// прямые цепочки стержневых КЭ — в стержни, плоские связные части пластинчатых КЭ — в плоские элементы с
/// <see cref="PlanarRegion"/>, контур которого восстановлен по граничным рёбрам КЭ. Набор, не складывающийся в одну
/// деталь, разбивается на части. Сетку не меняет. Без БД.
/// Элементы создаются с <c>MeshSource = "imported"</c> — их сетка взята из внешней программы, дискретизация её не трогает.
/// </summary>
public static class MeshMemberBuilder
{
    /// <summary>Допуск отклонения узла от плоскости пластины, м.</summary>
    public const double PlaneToleranceM = 0.001;
    /// <summary>Допуск излома цепочки стержней и непараллельности пластин, градусы.</summary>
    public const double AngularToleranceDeg = 0.5;
    /// <summary>|n.Z| нормали, ниже которого пластина считается вертикальной (базис стены).</summary>
    const double VerticalNormalTolerance = 1e-4;

    /// <summary>Имя элемента — <paramref name="tag"/> или его часть «<paramref name="tag"/> · k».</summary>
    public static bool IsPartTag(string memberTag, string tag) =>
        memberTag == tag || memberTag.StartsWith(tag + " · ", StringComparison.Ordinal);

    public static MeshMemberBuild Build(
        MeshMemberRequest request, IReadOnlyList<FemMeshNode> meshNodes, IReadOnlyList<FemElement> meshElements)
    {
        ArgumentNullException.ThrowIfNull(request);
        var diagnostics = new List<FemValidationDiagnostic>();
        var nodeByTag = new Dictionary<string, FemMeshNode>(StringComparer.Ordinal);
        foreach (var n in meshNodes) nodeByTag.TryAdd(n.NodeTag, n);
        var elementByTag = new Dictionary<string, FemElement>(StringComparer.Ordinal);
        foreach (var e in meshElements) elementByTag.TryAdd(e.ElemTag, e);

        var bars = new List<(FemElement Element, string[] Nodes)>();
        var shells = new List<(FemElement Element, string[] Nodes)>();
        foreach (var tag in request.ElementTags.Distinct())
        {
            if (!elementByTag.TryGetValue(tag, out var element))
            {
                diagnostics.Add(new("mesh_member_element_missing", $"{request.Tag}: КЭ {tag} нет в сетке схемы.", false, [tag]));
                continue;
            }
            var nodes = FemMeshTopology.ReadNodeTags(element)?.ToArray();
            if (nodes is null || nodes.Any(t => !nodeByTag.ContainsKey(t)))
            {
                diagnostics.Add(new("mesh_member_element_broken", $"{request.Tag}: у КЭ {tag} нет узлов в сетке.", false, [tag]));
                continue;
            }
            // ЛИРА (и SCAD) нумеруют узлы четырёхузлового КЭ «1 2 4 3» по обходу контура (см. Fem3DVM.BuildShellEdges):
            // геометрический обход — n1→n2→n4→n3.
            if (nodes.Length == 4)
                nodes = [nodes[0], nodes[1], nodes[3], nodes[2]];
            if (nodes.Length is 3 or 4)
                nodes = DropRepeatedNodes(nodes); // треугольник, записанный четырёхузловым КЭ с повтором узла
            if (nodes.Length == 2 && element.ElemType != "shell")
            {
                if (Direction(nodeByTag, nodes[0], nodes[1]).Length < PlaneToleranceM)
                    diagnostics.Add(new("mesh_member_element_degenerate", $"{request.Tag}: стержень {tag} нулевой длины пропущен.", false, [tag]));
                else bars.Add((element, nodes));
            }
            else if (nodes.Length is 3 or 4)
            {
                if (NewellVector(nodes.Select(t => Point(nodeByTag[t])).ToArray()).Length < 1e-12)
                    diagnostics.Add(new("mesh_member_element_degenerate", $"{request.Tag}: пластина {tag} нулевой площади пропущена.", false, [tag]));
                else shells.Add((element, nodes));
            }
            else if (element.ElemType == "shell")
                diagnostics.Add(new("mesh_member_element_degenerate", $"{request.Tag}: пластина {tag} вырождена (менее трёх разных узлов) и пропущена.", false, [tag]));
            else diagnostics.Add(new("mesh_member_element_unsupported", $"{request.Tag}: КЭ {tag} ({nodes.Length} узлов) не стержень и не пластина.", false, [tag]));
        }

        var drafts = new List<(FemMember Member, PlanarRegion? Region, string[] Elements)>();
        var endNodes = new Dictionary<string, FemNode>(StringComparer.Ordinal);
        foreach (var chain in BarChains(bars, nodeByTag))
        {
            var first = chain.Nodes[0];
            var last = chain.Nodes[^1];
            foreach (var t in new[] { first, last })
            {
                var m = nodeByTag[t];
                endNodes.TryAdd(t, new FemNode { NodeTag = t, X = m.X, Y = m.Y, Z = m.Z });
            }
            drafts.Add((new FemMember
            {
                ElemType = "beam",
                NodeIdsJson = JsonSerializer.Serialize(new[] { int.Parse(first), int.Parse(last) }),
                SectionTag = chain.Elements[0].SectionTag,
                MeshSource = FemMember.MeshSourceImported,
            }, null, chain.Elements.Select(e => e.ElemTag).ToArray()));
        }

        foreach (var part in PlanarParts(shells, nodeByTag))
        {
            var region = BuildRegion(request.Tag, part, nodeByTag, diagnostics);
            if (region is null) continue;
            var sample = part[0].Element;
            string kind = request.Kind ?? PlanarKindClassifier.Classify(region.Frame, out _);
            drafts.Add((new FemMember
            {
                ElemType = "shell",
                NodeIdsJson = "[]",
                SectionTag = sample.SectionTag,
                ThicknessM = sample.ThicknessM,
                Kind = kind,
                KindSource = request.Kind is null ? "auto" : request.KindSource,
                MeshSource = FemMember.MeshSourceImported,
            }, region, part.Select(p => p.Element.ElemTag).ToArray()));
        }

        var parts = new List<MeshMemberPart>();
        for (int i = 0; i < drafts.Count; i++)
        {
            var (member, region, elements) = drafts[i];
            member.ElemTag = drafts.Count == 1 ? request.Tag : $"{request.Tag} · {i + 1}";
            if (region is not null) { region.Tag = member.ElemTag; region.RecalcFingerprint(); }
            parts.Add(new(member, region, elements));
        }
        return new(endNodes.Values.ToList(), parts, diagnostics);
    }

    // ---------------- стержни ----------------

    sealed record Chain(List<string> Nodes, List<FemElement> Elements);

    /// <summary>Разбивает стержневые КЭ на прямые цепочки: концы — в узлах степени ≠ 2, в изломах и
    /// при смене сечения. Порядок — по наименьшему номеру КЭ цепочки.</summary>
    static List<Chain> BarChains(List<(FemElement Element, string[] Nodes)> bars, Dictionary<string, FemMeshNode> nodes)
    {
        var incident = new Dictionary<string, List<int>>(StringComparer.Ordinal);
        for (int i = 0; i < bars.Count; i++)
            foreach (var t in bars[i].Nodes)
            {
                if (!incident.TryGetValue(t, out var list)) incident[t] = list = [];
                list.Add(i);
            }

        bool IsBreak(string node)
        {
            var list = incident[node];
            if (list.Count != 2) return true;
            var a = bars[list[0]];
            var b = bars[list[1]];
            if (!string.Equals(a.Element.SectionTag, b.Element.SectionTag, StringComparison.Ordinal)) return true;
            var da = Direction(nodes, Other(a.Nodes, node), node);
            var db = Direction(nodes, node, Other(b.Nodes, node));
            return AngleDeg(da, db) > AngularToleranceDeg;
        }

        var used = new bool[bars.Count];
        var chains = new List<Chain>();
        void Walk(string start, int firstBar)
        {
            var chain = new Chain([start], []);
            string node = start;
            int bar = firstBar;
            while (true)
            {
                used[bar] = true;
                chain.Elements.Add(bars[bar].Element);
                node = Other(bars[bar].Nodes, node);
                chain.Nodes.Add(node);
                if (IsBreak(node)) break;
                int next = incident[node].First(i => i != bar);
                if (used[next]) break;
                bar = next;
            }
            chains.Add(chain);
        }

        foreach (var node in incident.Keys.Where(IsBreak).OrderBy(NodeOrder))
            foreach (int bar in incident[node].Where(i => !used[i]).ToList())
                if (!used[bar]) Walk(node, bar);
        // Замкнутые кольца без точек излома — разрываем в узле с наименьшим номером.
        for (int i = 0; i < bars.Count; i++)
            if (!used[i]) Walk(bars[i].Nodes.OrderBy(NodeOrder).First(), i);

        return chains.OrderBy(c => c.Elements.Min(e => ElemOrder(e.ElemTag))).ToList();
    }

    static string Other(string[] ends, string node) => ends[0] == node ? ends[1] : ends[0];

    static PlanarVector3 Direction(Dictionary<string, FemMeshNode> nodes, string from, string to)
        => Point(nodes[to]) - Point(nodes[from]);

    static double AngleDeg(PlanarVector3 a, PlanarVector3 b)
    {
        double cos = a.Dot(b) / (a.Length * b.Length);
        return Math.Acos(Math.Clamp(cos, -1, 1)) * 180 / Math.PI;
    }

    // ---------------- пластины ----------------

    /// <summary>Делит пластинчатые КЭ на плоские части с одним сечением, связные по рёбрам.</summary>
    static List<List<(FemElement Element, string[] Nodes)>> PlanarParts(
        List<(FemElement Element, string[] Nodes)> shells, Dictionary<string, FemMeshNode> nodes)
    {
        var byEdge = new Dictionary<(string, string), List<int>>();
        for (int i = 0; i < shells.Count; i++)
            foreach (var edge in Edges(shells[i].Nodes))
            {
                if (!byEdge.TryGetValue(edge, out var list)) byEdge[edge] = list = [];
                list.Add(i);
            }

        var normals = shells.Select(s => Normal(s.Nodes.Select(t => Point(nodes[t])).ToArray())).ToArray();
        double cosTol = Math.Cos(AngularToleranceDeg * Math.PI / 180);
        var assigned = new bool[shells.Count];
        var parts = new List<List<(FemElement, string[])>>();
        foreach (int seed in Enumerable.Range(0, shells.Count).OrderBy(i => ElemOrder(shells[i].Element.ElemTag)))
        {
            if (assigned[seed]) continue;
            var n = normals[seed];
            var origin = Point(nodes[shells[seed].Nodes[0]]);
            var section = SectionKey(shells[seed].Element);
            var part = new List<(FemElement, string[])>();
            var queue = new Queue<int>([seed]);
            assigned[seed] = true;
            while (queue.Count > 0)
            {
                int i = queue.Dequeue();
                part.Add(shells[i]);
                foreach (var edge in Edges(shells[i].Nodes))
                    foreach (int j in byEdge[edge])
                    {
                        if (assigned[j] || SectionKey(shells[j].Element) != section) continue;
                        if (Math.Abs(normals[j].Dot(n)) < cosTol) continue;
                        if (shells[j].Nodes.Any(t => Math.Abs((Point(nodes[t]) - origin).Dot(n)) > PlaneToleranceM)) continue;
                        assigned[j] = true;
                        queue.Enqueue(j);
                    }
            }
            parts.Add(part);
        }
        return parts;
    }

    static string SectionKey(FemElement e) => $"{e.SectionTag}|{e.ThicknessM}";

    static IEnumerable<(string, string)> Edges(string[] ring)
    {
        for (int i = 0; i < ring.Length; i++)
        {
            string a = ring[i], b = ring[(i + 1) % ring.Length];
            yield return string.CompareOrdinal(a, b) < 0 ? (a, b) : (b, a);
        }
    }

    /// <summary>Контур части по граничным рёбрам (встречаются у одного КЭ): внешний — петля наибольшей
    /// площади, остальные — отверстия. Null + диагностика, если петли не собираются однозначно.</summary>
    static PlanarRegion? BuildRegion(string tag, List<(FemElement Element, string[] Nodes)> part,
        Dictionary<string, FemMeshNode> nodes, List<FemValidationDiagnostic> diagnostics)
    {
        var elementTags = part.Select(p => p.Element.ElemTag).ToArray();
        // Ориентированные рёбра: граничное ребро КЭ не имеет встречного (или такого же) ребра у соседа.
        var count = new Dictionary<(string, string), int>();
        foreach (var (_, ring) in part)
            foreach (var edge in Edges(ring))
                count[edge] = count.GetValueOrDefault(edge) + 1;
        var boundary = count.Where(kv => kv.Value == 1).Select(kv => kv.Key).ToList();

        var adjacency = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        foreach (var (a, b) in boundary)
        {
            if (!adjacency.TryGetValue(a, out var la)) adjacency[a] = la = [];
            if (!adjacency.TryGetValue(b, out var lb)) adjacency[b] = lb = [];
            la.Add(b); lb.Add(a);
        }
        if (adjacency.Values.Any(l => l.Count != 2))
        {
            diagnostics.Add(new("mesh_member_contour_ambiguous",
                $"{tag}: контур части ({elementTags.Length} КЭ) касается сам себя в узле — часть пропущена.", false, elementTags));
            return null;
        }

        var loops = new List<List<string>>();
        var visited = new HashSet<string>(StringComparer.Ordinal);
        foreach (var start in adjacency.Keys.OrderBy(NodeOrder))
        {
            if (visited.Contains(start)) continue;
            var loop = new List<string> { start };
            visited.Add(start);
            string prev = start, cur = adjacency[start][0];
            while (cur != start)
            {
                loop.Add(cur);
                visited.Add(cur);
                var next = adjacency[cur][0] == prev ? adjacency[cur][1] : adjacency[cur][0];
                prev = cur;
                cur = next;
            }
            loops.Add(loop);
        }
        if (loops.Count == 0)
        {
            diagnostics.Add(new("mesh_member_contour_missing", $"{tag}: у части ({elementTags.Length} КЭ) нет границы — часть пропущена.", false, elementTags));
            return null;
        }

        var normal = PartNormal(part, nodes);
        var outer = loops.OrderByDescending(l => Math.Abs(AreaAlong(l, nodes, normal))).First();
        var frame = BuildFrame(outer, nodes, normal);

        Contour ToContour(List<string> loop, ContourType type)
        {
            var local = loop.Select(t => PlanarBoundaryFrameConverter.ToLocalPoint(frame, Point(nodes[t]))).ToList();
            local = DropCollinear(local);
            return new Contour { Type = type, X = local.Select(p => p.X).ToList(), Y = local.Select(p => p.Y).ToList() };
        }

        var hull = ToContour(outer, ContourType.Hull);
        var holes = loops.Where(l => l != outer).Select(l => ToContour(l, ContourType.Hole)).ToList();
        var (region, regionDiagnostics) = PlanarRegionCreation.TryCreate(hull, holes, frame, tag);
        if (region is null)
        {
            diagnostics.Add(new("mesh_member_region_invalid",
                $"{tag}: контур части ({elementTags.Length} КЭ) не годится для плоского элемента — "
                + string.Join("; ", regionDiagnostics.Select(d => d.Message)), false, elementTags));
            return null;
        }
        return region;
    }

    /// <summary>Базис плоского элемента в соглашениях редактора: плита — глобальные оси, стена — X по
    /// горизонтали вдоль стены, Y вверх; наклонная пластина — по контуру. Начало — первый узел контура.</summary>
    static Frame3D BuildFrame(List<string> outer, Dictionary<string, FemMeshNode> nodes, PlanarVector3 normal)
    {
        var origin = Point(nodes[outer[0]]);
        if (outer.All(t => Math.Abs(nodes[t].Z - origin.Z) <= PlaneToleranceM))
            return PlanarFrameBuilder.BuildPlateFrame(origin);
        if (Math.Abs(normal.Z) <= VerticalNormalTolerance)
        {
            var along = new PlanarVector3(0, 0, 1).Cross(normal);
            return PlanarFrameBuilder.BuildWallFrame(origin, origin + along);
        }
        var pts = outer.Select(t => Point(nodes[t])).ToArray();
        var fromPolygon = Frame3D.FromPolygon(pts.Select(p => p.X).ToArray(), pts.Select(p => p.Y).ToArray(), pts.Select(p => p.Z).ToArray());
        return fromPolygon with { Origin = origin };
    }

    static List<PlanarVector3> DropCollinear(List<PlanarVector3> loop)
    {
        var result = new List<PlanarVector3>(loop);
        bool changed = true;
        while (changed && result.Count > 3)
        {
            changed = false;
            for (int i = 0; i < result.Count && result.Count > 3; i++)
            {
                var prev = result[(i - 1 + result.Count) % result.Count];
                var cur = result[i];
                var next = result[(i + 1) % result.Count];
                var d = next - prev;
                double len = Math.Sqrt(d.X * d.X + d.Y * d.Y);
                double dist = Math.Abs((cur.X - prev.X) * d.Y - (cur.Y - prev.Y) * d.X) / Math.Max(len, 1e-12);
                if (dist <= PlaneToleranceM)
                {
                    result.RemoveAt(i);
                    changed = true;
                    i--;
                }
            }
        }
        return result;
    }

    /// <summary>Нормаль части — среднее нормалей КЭ, ориентированных по первому.</summary>
    static PlanarVector3 PartNormal(List<(FemElement Element, string[] Nodes)> part, Dictionary<string, FemMeshNode> nodes)
    {
        var first = Normal(part[0].Nodes.Select(t => Point(nodes[t])).ToArray());
        var sum = PlanarVector3.Zero;
        foreach (var (_, ring) in part)
        {
            var n = Normal(ring.Select(t => Point(nodes[t])).ToArray());
            sum += n.Dot(first) >= 0 ? n : n * -1;
        }
        return sum.Normalize();
    }

    /// <summary>Единичная нормаль многоугольника по Ньюэллу (многоугольник не вырожден — см. <see cref="Build"/>).</summary>
    static PlanarVector3 Normal(PlanarVector3[] ring) => NewellVector(ring).Normalize();

    /// <summary>Узлы КЭ без повторов подряд (по кругу): «1 2 3 3» → «1 2 3».</summary>
    static string[] DropRepeatedNodes(string[] ring)
    {
        var result = new List<string>();
        foreach (var t in ring)
            if (result.Count == 0 || result[^1] != t) result.Add(t);
        while (result.Count > 1 && result[^1] == result[0]) result.RemoveAt(result.Count - 1);
        return result.Distinct().Count() == result.Count ? result.ToArray() : result.Distinct().ToArray();
    }

    /// <summary>Вектор Ньюэлла многоугольника: направление — нормаль, длина — удвоенная площадь.</summary>
    static PlanarVector3 NewellVector(PlanarVector3[] ring)
    {
        double nx = 0, ny = 0, nz = 0;
        for (int i = 0; i < ring.Length; i++)
        {
            var a = ring[i];
            var b = ring[(i + 1) % ring.Length];
            nx += (a.Y - b.Y) * (a.Z + b.Z);
            ny += (a.Z - b.Z) * (a.X + b.X);
            nz += (a.X - b.X) * (a.Y + b.Y);
        }
        return new PlanarVector3(nx, ny, nz);
    }

    /// <summary>Площадь петли в проекции на плоскость с нормалью n (со знаком).</summary>
    static double AreaAlong(List<string> loop, Dictionary<string, FemMeshNode> nodes, PlanarVector3 n)
    {
        var sum = PlanarVector3.Zero;
        for (int i = 0; i < loop.Count; i++)
            sum += Point(nodes[loop[i]]).Cross(Point(nodes[loop[(i + 1) % loop.Count]]));
        return sum.Dot(n) / 2;
    }

    static PlanarVector3 Point(FemMeshNode n) => new(n.X, n.Y, n.Z);

    static long NodeOrder(string tag) => long.TryParse(tag, out var v) ? v : long.MaxValue;
    static long ElemOrder(string tag) => long.TryParse(tag, out var v) ? v : long.MaxValue;
}
