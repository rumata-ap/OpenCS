using CScore.Planar;

namespace CScore.Fem.Fragment;

public static partial class StoreyFragmentBuilder
{
    /// <summary>
    /// Стержень: участок внутри фрагмента (полоса и призма), деление в точках пересечения с плитами фрагмента (внутри
    /// контура), рассечения на концах, лежащих на z₁/z₂ или гранях призмы (кроме опорных узлов). Стержень в плоскости
    /// границы — отказ; колонна с осью вне призмы не входит (предупреждение, если её сечение задевает призму).
    /// </summary>
    static void CutBar(Ctx c, SourceMember s)
    {
        string tag = s.Member.ElemTag;
        PlanarVector3 a = s.A, d = s.B - s.A;
        double len = d.Length;
        if (len < c.Tol) return;
        double eps = c.Tol / len;
        bool horizontal = Math.Abs(d.Z) <= c.Tol;
        var (lo, hi) = ClipRange(c, a, d, 0, 1);
        if (hi - lo <= eps)
        {
            WarnColumnOutside(c, s);
            return;
        }
        if (c.Planes.FirstOrDefault(pl => Math.Abs(pl.Normal.Dot(d)) <= c.Tol && Math.Abs(pl.Eval(a)) <= c.Tol) is { } on)
        {
            if (on.Side == FragmentCutSide.Lateral)
                c.Error("fragment_member_on_prism_face",
                    $"Стержень {tag} лежит в плоскости боковой грани фрагмента — измените отступ d.", tag);
            else
                c.Error("fragment_member_on_cut_plane",
                    $"Стержень {tag} на отметке {a.Z:0.###} м лежит на границе полосы — сдвиньте z₁/z₂ к середине этажа.", tag);
            return;
        }
        if (lo < eps) lo = 0;
        if (hi > 1 - eps) hi = 1;
        var u = d * (1 / len);
        bool Shallow(double t) => c.Planes.Any(pl => Math.Abs(pl.Eval(At(a, d, t))) <= c.Tol && Math.Abs(pl.Normal.Dot(u)) < ShallowCutSine);
        if ((lo > 0 && Shallow(lo)) || (hi < 1 && Shallow(hi)))
            c.Warn("fragment_bar_shallow_cut", $"Стержень {tag} почти лежит в плоскости рассечения — проверьте отметки z₁/z₂ и отступ d.", tag);

        var stations = new List<double> { lo, hi };
        if (!horizontal)
            foreach (var slab in c.Slabs)
            {
                double t = (slab.Z - a.Z) / d.Z;
                if (t > lo + eps && t < hi - eps && RegionContains(slab.Region, At(a, d, t), c.Tol)) stations.Add(t);
            }
        stations = stations.Distinct().OrderBy(t => t).ToList();
        var nodes = stations.Select(t => c.Nodes.Get(At(a, d, t), t == 0 ? s.DofA : t == 1 ? s.DofB : 0)).ToList();

        int count = nodes.Count - 1;
        var names = PartNames(tag, count, twoLevels: true);
        var parts = new List<FemMember>();
        for (int k = 0; k < count; k++)
        {
            // Имена — снизу вверх: у стержня, идущего вниз, первая часть — верхняя.
            string name = names[d.Z < 0 ? count - 1 - k : k];
            var part = CopyPart(s.Member, name, [int.Parse(nodes[k].NodeTag), int.Parse(nodes[k + 1].NodeTag)]);
            part.CrossSectionId ??= SectionOnPart(c, s.Member.ElemTag, At(a, d, stations[k]), At(a, d, stations[k + 1]), name);
            parts.Add(part);
            c.Add(s.Member, part, null);
        }
        AddEndCut(c, parts[0], nodes[0], u * -1);
        AddEndCut(c, parts[^1], nodes[^1], u);
    }

    /// <summary>
    /// Правило (а) спеки призмы: колонна, не вошедшая во фрагмент (ось вне призмы), но заходящая в полосу и задевающая
    /// призму габаритом сечения, — предупреждение «не входит».
    /// </summary>
    static void WarnColumnOutside(Ctx c, SourceMember s)
    {
        if (c.Prism is not { } prism || !FragmentPrismBuilder.IsColumn(s.A, s.B)) return;
        if (Math.Max(s.A.Z, s.B.Z) <= c.Z1 + c.Tol || Math.Min(s.A.Z, s.B.Z) >= c.Z2 - c.Tol) return;
        int? section = s.Member.CrossSectionId
                       ?? (c.ParentBars.TryGetValue(s.Member.ElemTag, out var bars) ? bars.Select(b => b.SectionId).FirstOrDefault(id => id is not null) : null);
        var footprint = FragmentPrismBuilder.Column(s.Member, s.A, s.B, section, c.SectionOutline, 0, c.Tol).Corners();
        var overlap = PlanarPolygonOps.ClipByConvex(footprint, prism.Corners());
        if (overlap.Count < 3 || Math.Abs(PlanarPolygonOps.SignedArea(overlap)) <= c.Tol * c.Tol) return;
        string tag = s.Member.ElemTag;
        c.Warn("fragment_column_outside_prism",
            $"Колонна {tag}: ось вне призмы фрагмента, но сечение задевает её грань — колонна не входит во фрагмент. " +
            "Измените отступ d, если она нужна.", tag);
    }

    /// <summary>Стержневые КЭ сетки родителя с КонЭ — по тегу КонЭ (импорт SCAD/ЛИРЫ: сечение есть у КЭ, у КонЭ нет).</summary>
    static Dictionary<string, List<(PlanarVector3 A, PlanarVector3 B, int? SectionId)>> ParentBars(StoreyFragmentParentSnapshot parent)
    {
        var node = parent.MeshNodes.GroupBy(n => n.NodeTag).ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);
        var result = new Dictionary<string, List<(PlanarVector3, PlanarVector3, int?)>>(StringComparer.Ordinal);
        foreach (var e in parent.MeshElements.Where(e => e.ElemType == "beam" && e.SourceMemberTag is not null))
        {
            var tags = FemMeshTopology.ReadNodeTags(e)?.ToArray();
            if (tags is not { Length: 2 } || !node.TryGetValue(tags[0], out var a) || !node.TryGetValue(tags[1], out var b)) continue;
            if (!result.TryGetValue(e.SourceMemberTag!, out var list)) result[e.SourceMemberTag!] = list = [];
            list.Add((new(a.X, a.Y, a.Z), new(b.X, b.Y, b.Z), e.CrossSectionId));
        }
        return result;
    }

    /// <summary>
    /// Сечение части стержня без своего: по КЭ родителя этого КонЭ на участке <paramref name="p"/>–<paramref name="q"/>
    /// (по длине перекрытия). Разные сечения на участке — предупреждение и сечение большей длины; КЭ без сечения — null.
    /// </summary>
    static int? SectionOnPart(Ctx c, string memberTag, PlanarVector3 p, PlanarVector3 q, string partTag)
    {
        if (!c.ParentBars.TryGetValue(memberTag, out var elements)) return null;
        var axis = q - p;
        double len = axis.Length;
        if (len < c.Tol) return null;
        var unit = axis * (1 / len);
        var lengths = new Dictionary<int, double>();
        foreach (var (a, b, section) in elements)
        {
            if (section is not int id) continue;
            // КЭ на оси части: оба конца в допуске от прямой.
            if (Offset(a - p, unit) > c.Tol || Offset(b - p, unit) > c.Tol) continue;
            double ta = (a - p).Dot(unit), tb = (b - p).Dot(unit);
            double overlap = Math.Min(len, Math.Max(ta, tb)) - Math.Max(0, Math.Min(ta, tb));
            if (overlap > c.Tol) lengths[id] = lengths.GetValueOrDefault(id) + overlap;
        }
        return PickSection(c, lengths, partTag);
    }

    /// <summary>Сечение по длинам КЭ с ним; разные — предупреждение.</summary>
    static int? PickSection(Ctx c, Dictionary<int, double> lengths, string partTag)
    {
        if (lengths.Count == 0) return null;
        var best = lengths.OrderByDescending(p => p.Value).ThenBy(p => p.Key).First().Key;
        if (lengths.Count > 1)
            c.Warn("fragment_bar_section_varies",
                $"У КЭ стержня {partTag} в родителе разные сечения ({string.Join(", ", lengths.Keys.Order())}) — взято сечение {best} " +
                "по большей длине. Разделите КонЭ по смене сечения, если это важно.", partTag);
        return best;
    }

    static double Offset(PlanarVector3 v, PlanarVector3 unit) => (v - unit * v.Dot(unit)).Length;

    /// <summary>
    /// Рассечение конца стержня, лежащего на z₁/z₂ или грани призмы, если узел не опорный; у ребра фрагмента — плоскость,
    /// за которую стержень выходит сильнее (<paramref name="outward"/> — направление от части к отброшенному). Нормаль
    /// задаётся только у боковых (у низа и верха — по стороне).
    /// </summary>
    static void AddEndCut(Ctx c, FemMember part, FemNode node, PlanarVector3 outward)
    {
        if (node.DofMask != 0) return;
        var p = P(node);
        if (c.Planes.Where(pl => Math.Abs(pl.Eval(p)) <= c.Tol).MaxBy(pl => pl.Normal.Dot(outward)) is not { } plane) return;
        c.Draft.Cuts.Add(new(part.ElemTag, FragmentCutKind.End, plane.Side, node.Z, node.NodeTag, [],
            plane.Side == FragmentCutSide.Lateral ? plane.Normal : null));
    }
}
