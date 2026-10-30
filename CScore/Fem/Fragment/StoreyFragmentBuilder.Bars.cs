using CScore.Planar;

namespace CScore.Fem.Fragment;

public static partial class StoreyFragmentBuilder
{
    /// <summary>
    /// Стержень: участок в полосе, деление в точках пересечения с плитами фрагмента (внутри контура), рассечения на
    /// концах, лежащих на z₁/z₂ (кроме опорных узлов).
    /// </summary>
    static void CutBar(Ctx c, SourceMember s)
    {
        string tag = s.Member.ElemTag;
        PlanarVector3 a = s.A, d = s.B - s.A;
        double len = d.Length;
        if (len < c.Tol) return;
        double eps = c.Tol / len, lo, hi;
        bool horizontal = Math.Abs(d.Z) <= c.Tol;
        if (horizontal)
        {
            if (c.OnCutPlane(a.Z))
            {
                c.Error("fragment_member_on_cut_plane",
                    $"Стержень {tag} на отметке {a.Z:0.###} м лежит на границе полосы — сдвиньте z₁/z₂ к середине этажа.", tag);
                return;
            }
            if (a.Z < c.Z1 || a.Z > c.Z2) return;
            (lo, hi) = (0, 1);
        }
        else
        {
            double t1 = (c.Z1 - a.Z) / d.Z, t2 = (c.Z2 - a.Z) / d.Z;
            lo = Math.Max(0, Math.Min(t1, t2));
            hi = Math.Min(1, Math.Max(t1, t2));
            if (hi - lo <= eps) return;
            if (lo < eps) lo = 0;
            if (hi > 1 - eps) hi = 1;
            if (Math.Abs(d.Z) / len < ShallowCutSine && (lo > 0 || hi < 1))
                c.Warn("fragment_bar_shallow_cut", $"Стержень {tag} почти лежит в плоскости рассечения — проверьте отметки z₁/z₂.", tag);
        }

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
        AddEndCut(c, parts[0], nodes[0]);
        AddEndCut(c, parts[^1], nodes[^1]);
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

    /// <summary>Рассечение конца стержня, лежащего на z₁/z₂, если узел не опорный.</summary>
    static void AddEndCut(Ctx c, FemMember part, FemNode node)
    {
        if (!c.OnCutPlane(node.Z) || node.DofMask != 0) return;
        c.Draft.Cuts.Add(new(part.ElemTag, FragmentCutKind.End, c.SideOf(node.Z), node.Z, node.NodeTag, []));
    }
}
