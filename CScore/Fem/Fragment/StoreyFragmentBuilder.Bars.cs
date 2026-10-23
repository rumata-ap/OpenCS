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
            parts.Add(part);
            c.Add(s.Member, part, null);
        }
        AddEndCut(c, parts[0], nodes[0]);
        AddEndCut(c, parts[^1], nodes[^1]);
    }

    /// <summary>Рассечение конца стержня, лежащего на z₁/z₂, если узел не опорный.</summary>
    static void AddEndCut(Ctx c, FemMember part, FemNode node)
    {
        if (!c.OnCutPlane(node.Z) || node.DofMask != 0) return;
        c.Draft.Cuts.Add(new(part.ElemTag, FragmentCutKind.End, c.SideOf(node.Z), node.Z, node.NodeTag, []));
    }
}
