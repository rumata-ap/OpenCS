using System.Globalization;
using CScore.Fem.Loads;
using CScore.Planar;

namespace CScore.Fem.Fragment;

public static partial class StoreyFragmentBuilder
{
    /// <summary>Относительный допуск покрытия куска КЭ (площадь, длина) и необрезанности стены (площадь).</summary>
    const double KeepCoverTol = 1e-3;

    /// <summary>
    /// Сохранение сетки родителя у необрезанных КонЭ (спека 2026-11-10-fragment-keep-parent-mesh, разд. 2–3): КЭ родителя
    /// по КонЭ, узлы сетки снимка, уже сохранённые КЭ и узлы.
    /// </summary>
    sealed class KeepState
    {
        public KeepState(StoreyFragmentParentSnapshot parent, StoreyFragmentRequest request)
        {
            MeshNodes = parent.MeshNodes.GroupBy(n => n.NodeTag).ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);
            Rebuild = (request.MeshRebuildMemberTags ?? []).ToHashSet(StringComparer.Ordinal);
            foreach (var e in parent.MeshElements.Where(e => e.ElemType is "beam" or "shell" && e.SourceMemberTag is not null))
            {
                if (!ElementsOf.TryGetValue(e.SourceMemberTag!, out var list)) ElementsOf[e.SourceMemberTag!] = list = [];
                list.Add(e);
            }
        }

        /// <summary>КЭ родителя по тегу КонЭ родителя (у частей «КЭ без КонЭ» — КЭ части).</summary>
        public Dictionary<string, List<FemElement>> ElementsOf { get; } = new(StringComparer.Ordinal);
        public Dictionary<string, FemMeshNode> MeshNodes { get; }
        /// <summary>Теги КонЭ родителя, которые всегда перемешиваются.</summary>
        public HashSet<string> Rebuild { get; }
        /// <summary>Теги сохранённых КЭ родителя.</summary>
        public HashSet<string> KeptElementTags { get; } = new(StringComparer.Ordinal);
        /// <summary>Сохранённые узлы сетки (копии) по тегу.</summary>
        public Dictionary<string, FemMeshNode> KeptNodes { get; } = new(StringComparer.Ordinal);

        public FemElementGeometry? Geometry(FemElement e) => FemElementGeometry.Of(e, MeshNodes);
    }

    /// <summary>Теги узлов сетки снимка — их не получают новые узлы схемы фрагмента (кроме узла в той же точке).</summary>
    static HashSet<int> ReservedMeshTags(StoreyFragmentParentSnapshot parent) =>
        parent.MeshNodes.Select(n => int.TryParse(n.NodeTag, NumberStyles.Integer, CultureInfo.InvariantCulture, out int t) ? t : 0)
            .Where(t => t > 0).ToHashSet();

    /// <summary>КЭ КонЭ родителя, пригодные к сохранению: флажок включён, КонЭ не в списке перемешивания, КЭ есть, все их
    /// узлы в снимке; при <paramref name="untouched"/> — и ни один КЭ ещё не сохранён другой частью (у кусков стены
    /// уже сохранённые соседним куском КЭ пропускаются). Иначе null.</summary>
    static List<FemElement>? KeepCandidates(Ctx c, string sourceTag, bool untouched = true)
    {
        if (c.Keep is not { } k || k.Rebuild.Contains(sourceTag) || !k.ElementsOf.TryGetValue(sourceTag, out var list) || list.Count == 0)
            return null;
        foreach (var e in list)
            if ((untouched && k.KeptElementTags.Contains(e.ElemTag)) || k.Geometry(e) is not { } g || !(g.IsBar || g.IsShell)) return null;
        return list;
    }

    /// <summary>КонЭ взят целиком (плита «копия как есть», часть «КЭ без КонЭ»): сохраняется, если все узлы его КЭ внутри
    /// фрагмента.</summary>
    static void TryKeepWhole(Ctx c, StoreyFragmentMember part, string sourceTag)
    {
        if (KeepCandidates(c, sourceTag) is not { } list) return;
        if (list.Any(e => c.Keep!.Geometry(e)!.Points.Any(p => !c.InBox(p)))) return;
        KeepElements(c, part, list);
    }

    /// <summary>
    /// Кусок стены (деление по плитам — не обрезка, спека разд. 2): КЭ стены относится к куску по центру; кусок
    /// сохраняется, если узлы всех его КЭ в многоугольнике куска и сумма площадей КЭ — площадь куска.
    /// </summary>
    static void TryKeepPiece(Ctx c, StoreyFragmentMember part, string sourceTag, Frame3D f, PlanarPolygonWithHoles polygon)
    {
        if (KeepCandidates(c, sourceTag, untouched: false) is not { } list) return;
        var k = c.Keep!;
        var outer = polygon.Outer;
        bool Inside(PlanarPoint2D q, double tol) =>
            PlanarPolygonOps.Contains(outer, q, tol) && !polygon.Holes.Any(h => PlanarPolygonOps.Contains(h, q, -tol));
        var mine = new List<FemElement>();
        double area = 0;
        foreach (var e in list.Where(e => !k.KeptElementTags.Contains(e.ElemTag)))
        {
            var g = k.Geometry(e)!;
            if (!g.IsShell) return;
            var loop = g.Contour.Select(i => Local(f, g.Points[i])).ToList();
            if (loop.Any(q => Math.Abs(q.Z) > c.Tol)) return;
            var uv = loop.Select(q => new PlanarPoint2D(q.X, q.Y)).ToList();
            var centre = new PlanarPoint2D(uv.Average(q => q.U), uv.Average(q => q.V));
            if (!Inside(centre, 0)) continue;
            if (uv.Any(q => !Inside(q, c.Tol))) return;
            mine.Add(e);
            area += Math.Abs(PlanarPolygonOps.SignedArea(uv));
        }
        double pieceArea = Area(polygon);
        if (mine.Count == 0 || Math.Abs(area - pieceArea) > KeepCoverTol * pieceArea) return;
        KeepElements(c, part, mine);
    }

    /// <summary>Площадь многоугольника с отверстиями.</summary>
    static double Area(PlanarPolygonWithHoles polygon) =>
        Math.Abs(PlanarPolygonOps.SignedArea(polygon.Outer)) - polygon.Holes.Sum(h => Math.Abs(PlanarPolygonOps.SignedArea(h)));

    /// <summary>Стена не обрезана: площадь кусков после клипа полосой и боковыми гранями равна исходной.</summary>
    static bool WholeAfterClip(PlanarPolygonWithHoles source, IEnumerable<Piece> pieces)
    {
        double a0 = Area(source);
        return a0 > 0 && Math.Abs(pieces.Sum(p => Area(p.Polygon)) - a0) <= KeepCoverTol * a0;
    }

    /// <summary>
    /// Стержень не обрезан (<paramref name="stations"/> от 0 до 1): КЭ относится к части по середине; каждая станция
    /// деления — узел КЭ стержня, сумма длин КЭ части — её длина. Все части или ни одной: КЭ по частям, иначе null.
    /// </summary>
    static List<List<FemElement>>? KeepBarParts(Ctx c, string sourceTag, PlanarVector3 a, PlanarVector3 d, IReadOnlyList<double> stations)
    {
        if (stations[0] != 0 || stations[^1] != 1 || KeepCandidates(c, sourceTag) is not { } list) return null;
        var k = c.Keep!;
        double len = d.Length;
        var u = d * (1 / len);
        var parts = stations.Skip(1).Select(_ => new List<FemElement>()).ToList();
        var lengths = new double[parts.Count];
        var nodeParams = new List<double>();
        foreach (var e in list)
        {
            var g = k.Geometry(e)!;
            if (!g.IsBar) return null;
            var (p, q) = (g.Points[0], g.Points[1]);
            if (Offset(p - a, u) > c.Tol || Offset(q - a, u) > c.Tol) return null;
            double tp = (p - a).Dot(u) / len, tq = (q - a).Dot(u) / len, mid = (tp + tq) / 2;
            nodeParams.Add(tp);
            nodeParams.Add(tq);
            int i = Enumerable.Range(0, parts.Count).FirstOrDefault(j => mid > stations[j] && mid < stations[j + 1], -1);
            if (i < 0) return null;
            if (Math.Min(tp, tq) < stations[i] - c.Tol / len || Math.Max(tp, tq) > stations[i + 1] + c.Tol / len) return null;
            parts[i].Add(e);
            lengths[i] += g.Length;
        }
        for (int i = 0; i < parts.Count; i++)
            if (Math.Abs(lengths[i] - (stations[i + 1] - stations[i]) * len) > Math.Max(c.Tol, KeepCoverTol * len)) return null;
        foreach (var t in stations.Skip(1).SkipLast(1))
            if (!nodeParams.Any(x => Math.Abs(x - t) * len <= c.Tol)) return null;
        return parts;
    }

    /// <summary>Копии КЭ части (теги родителя, <c>Origin = imported</c>, КонЭ — часть) и их узлов; КонЭ — с замком сетки.</summary>
    static void KeepElements(Ctx c, StoreyFragmentMember part, IReadOnlyList<FemElement> elements)
    {
        var k = c.Keep!;
        string tag = part.Member.ElemTag;
        var copies = new List<FemElement>(elements.Count);
        foreach (var e in elements)
        {
            k.KeptElementTags.Add(e.ElemTag);
            var copy = e.ShallowCopy();
            copy.Id = 0;
            copy.SchemaId = 0;
            copy.Origin = FemMember.MeshSourceImported;
            copy.SourceMemberTag = tag;
            copies.Add(copy);
            foreach (var t in FemMeshTopology.ReadNodeTags(e)!)
            {
                if (k.KeptNodes.ContainsKey(t)) continue;
                var n = k.MeshNodes[t];
                var node = new FemMeshNode
                {
                    NodeTag = n.NodeTag, X = n.X, Y = n.Y, Z = n.Z, SourceMemberTag = tag, Origin = FemMember.MeshSourceImported,
                };
                k.KeptNodes[t] = node;
                c.Draft.KeptMeshNodes.Add(node);
                c.Nodes.Keep(node);
            }
        }
        part.KeptElements = copies;
        part.Member.MeshSource = FemMember.MeshSourceImported;
    }

    /// <summary>Узлы сохранённой сетки ссылаются на узел схемы в своей точке (<see cref="FemMeshNode.SourceNodeTag"/>):
    /// закрепления, пружины и нагрузки узлов схемы и концы стержней находят узел сетки, как у построенной.</summary>
    static void LinkKeptNodes(Ctx c)
    {
        foreach (var n in c.Draft.KeptMeshNodes) n.SourceNodeTag = c.Nodes.FindNear(P(n))?.NodeTag;
    }

    /// <summary>Узел сетки сохранён.</summary>
    static bool IsKeptNode(Ctx c, string meshNodeTag) => c.Keep?.KeptNodes.ContainsKey(meshNodeTag) == true;

    /// <summary>КЭ сохранён.</summary>
    static bool IsKeptElement(Ctx c, string elemTag) => c.Keep?.KeptElementTags.Contains(elemTag) == true;

    static PlanarVector3 Local(Frame3D f, PlanarVector3 p) => PlanarBoundaryFrameConverter.ToLocalPoint(f, p);
}
