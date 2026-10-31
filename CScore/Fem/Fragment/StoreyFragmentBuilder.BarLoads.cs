using System.Globalization;
using CScore.Fem.Loads;
using CScore.Planar;

namespace CScore.Fem.Fragment;

public static partial class StoreyFragmentBuilder
{
    /// <summary>Часть-стержень фрагмента: концы, КонЭ родителя, поворот сечения.</summary>
    sealed record BarPart(string Tag, string ParentTag, PlanarVector3 A, PlanarVector3 B, double RotationDeg)
    {
        public double Length => (B - A).Length;
        public PlanarVector3 Axis => (B - A) * (1 / Length);

        /// <summary>Вектор из местных осей части в глобальные.</summary>
        public PlanarVector3 ToGlobal(PlanarVector3 local)
        {
            var (x, y, z) = BeamLocalAxisConvention.Frame(A, B, RotationDeg);
            return x * local.X + y * local.Y + z * local.Z;
        }
    }

    /// <summary>Кусок погонной нагрузки на части: участок s0…s1 (м от начала части), интенсивности в своей СК.</summary>
    sealed record BarPiece(string PartTag, int LoadCase, bool Local, double S0, double S1, PlanarVector3 Q0, PlanarVector3 Q1)
    {
        public PlanarVector3 At(double s) => S1 - S0 < 1e-12 ? Q0 : Q0 + (Q1 - Q0) * ((s - S0) / (S1 - S0));
    }

    static string S(int value) => value.ToString(CultureInfo.InvariantCulture);

    static List<BarPart> BarParts(Ctx c)
    {
        var nodes = c.Draft.Nodes.ToDictionary(n => n.NodeTag, StringComparer.Ordinal);
        return c.Draft.Members.Where(m => m.Member.ElemType == "beam").Select(m =>
        {
            var ids = NodeTags(m.Member);
            return new BarPart(m.Member.ElemTag, m.ParentMemberTag, P(nodes[S(ids[0])]), P(nodes[S(ids[^1])]), m.Member.RotationDeg);
        }).ToList();
    }

    /// <summary>
    /// Часть s0…s1 луча a + u·s внутри фрагмента — полосы z₁…z₂ и призмы (Сайрус–Бек; пустая — Hi ≤ Lo). Луч вдоль
    /// плоскости — целиком внутри или снаружи по допуску.
    /// </summary>
    static (double Lo, double Hi) ClipRange(Ctx c, PlanarVector3 a, PlanarVector3 u, double s0, double s1) =>
        c.Bounds.ClipRange(a, u, s0, s1);

    static void AddMemberLoad(LoadCtx l, string partTag, FemMemberLoad load)
    {
        l.C.Draft.MemberLoads.Add(new FragmentMemberLoad(partTag, load));
        l.CategoryOf[load] = LoadCategory.Bars;
    }

    /// <summary>Нагрузки КонЭ-стержней родителя → куски на частях фрагмента; лишнее за z₁/z₂ отброшено.</summary>
    static void TransferMemberLoads(LoadCtx l, List<BarPiece> pieces)
    {
        var nodeByTag = l.Parent.Nodes.GroupBy(n => n.NodeTag).ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);
        var memberById = l.Parent.Members.Where(m => m.Id > 0 && m.ElemType == "beam").GroupBy(m => m.Id).ToDictionary(g => g.Key, g => g.First());
        foreach (var load in l.Parent.MemberLoads)
        {
            if (!memberById.TryGetValue(load.MemberId, out var m)) continue;
            var ids = NodeTags(m);
            if (ids.Length < 2 || !nodeByTag.TryGetValue(S(ids[0]), out var na) || !nodeByTag.TryGetValue(S(ids[^1]), out var nb)) continue;
            PlanarVector3 a = P(na), b = P(nb);
            double len = (b - a).Length;
            if (len < l.C.Tol) continue;
            var u = (b - a) * (1 / len);
            bool local = load.CoordinateSystem == "local";
            var (fx, fy, fz) = BeamLocalAxisConvention.Frame(a, b, m.RotationDeg);
            PlanarVector3 G(PlanarVector3 q) => local ? fx * q.X + fy * q.Y + fz * q.Z : q;
            var q0 = new PlanarVector3(load.QxStart, load.QyStart, load.QzStart);
            var q1 = new PlanarVector3(load.QxEnd, load.QyEnd, load.QzEnd);
            var mine = l.Bars.Where(p => p.ParentTag == m.ElemTag).ToList();
            int lc = load.LoadCaseId;

            if (load.DistributionType == "point")
            {
                var at = a + u * load.StartOffsetM;
                if (!l.C.InBox(at)) continue;
                l.AddParent(lc, LoadCategory.Bars, at, G(q0), G(new PlanarVector3(load.Mx, load.My, load.Mz)));
                var part = mine.FirstOrDefault(p => (at - p.A).Dot(p.Axis) is var s && s >= -l.C.Tol && s <= p.Length + l.C.Tol);
                if (part is null) { l.Lost.Add(m.ElemTag); continue; }
                AddMemberLoad(l, part.Tag, new FemMemberLoad
                {
                    LoadCaseId = lc, CoordinateSystem = load.CoordinateSystem, DistributionType = "point",
                    StartOffsetM = Math.Clamp((at - part.A).Dot(part.Axis), 0, part.Length),
                    QxStart = q0.X, QyStart = q0.Y, QzStart = q0.Z, QxEnd = q0.X, QyEnd = q0.Y, QzEnd = q0.Z,
                    Mx = load.Mx, My = load.My, Mz = load.Mz,
                });
                continue;
            }

            double s0 = load.StartOffsetM, s1 = len - load.EndOffsetM;
            if (s1 - s0 <= 1e-9) continue;
            PlanarVector3 Q(double s) => q0 + (q1 - q0) * ((s - s0) / (s1 - s0));
            var (lo, hi) = ClipRange(l.C, a, u, s0, s1);
            if (hi - lo > 1e-9) l.AddParentLine(lc, LoadCategory.Bars, a + u * lo, a + u * hi, G(Q(lo)), G(Q(hi)));
            foreach (var p in mine)
            {
                double p0 = (p.A - a).Dot(u), p1 = (p.B - a).Dot(u);
                double x0 = Math.Max(s0, p0), x1 = Math.Min(s1, p1);
                if (x1 - x0 > 1e-9) pieces.Add(new BarPiece(p.Tag, lc, local, x0 - p0, x1 - p0, Q(x0), Q(x1)));
            }
        }
    }

    /// <summary>
    /// Нагрузка КЭ-стержня родителя (равномерная, трапеция, сосредоточенная) → куски на частях фрагмента в координатах
    /// по длине части. Местная СК сохраняется, если оси КЭ и части совпадают (направление и поворот), иначе — глобальная.
    /// </summary>
    static void BarElementLoad(LoadCtx l, FemElementLoad load, FemElement e, FemElementGeometry g, double? rotation, List<BarPiece> pieces)
    {
        int lc = load.LoadCaseId;
        var v = load.Values;
        double len = g.Length;
        if (len < 1e-9) return;
        if (FemElementLoadNodalizer.Direction(load, g, rotation) is not { } dir) { l.Lost.Add(e.ElemTag); return; }
        var p0 = g.Points[0];
        var axis = (g.Points[1] - p0) * (1 / len);
        (double A, double QA, double B, double QB)? seg = load.LoadKind switch
        {
            FemElementLoadKinds.Uniform when v.Count >= 1 => (0, v[0], len, v[0]),
            FemElementLoadKinds.Trapezoid when v.Count >= 4 => FemElementLoadNodalizer.TrapezoidOnBar(v[0], v[1], v[2], v[3], len),
            _ => null,
        };
        bool point = load.LoadKind == FemElementLoadKinds.Point && v.Count >= 2;
        if (seg is null && !point)
        {
            // Трапеция целиком за концом КЭ — не нагрузка; прочие виды на стержне не поддержаны.
            if (load.LoadKind is not (FemElementLoadKinds.Uniform or FemElementLoadKinds.Trapezoid)) l.Lost.Add(e.ElemTag);
            return;
        }

        double pa = point ? Math.Clamp(v[1], 0, len) : 0;
        if (point)
        {
            var at = p0 + axis * pa;
            if (!l.C.InBox(at)) return;
            l.AddParent(lc, LoadCategory.Bars, at, dir * v[0], PlanarVector3.Zero);
        }
        else
        {
            var (a, qa, b, qb) = seg!.Value;
            double QE(double s) => qa + (qb - qa) * (s - a) / (b - a);
            var (lo, hi) = ClipRange(l.C, p0, axis, a, b);
            if (hi - lo <= 1e-9) return;
            l.AddParentLine(lc, LoadCategory.Bars, p0 + axis * lo, p0 + axis * hi, dir * QE(lo), dir * QE(hi));
        }

        var candidates = l.Bars.Where(p => p.ParentTag == e.SourceMemberTag).ToList();
        if (candidates.Count == 0) candidates = l.Bars;
        bool found = false;
        foreach (var part in candidates)
        {
            if (1 - Math.Abs(axis.Dot(part.Axis)) > 1e-6) continue;
            double si = (p0 - part.A).Dot(part.Axis);
            if ((p0 - (part.A + part.Axis * si)).Length > SourceSnapM) continue;
            double sign = axis.Dot(part.Axis) > 0 ? 1 : -1;
            bool local = load.IsLocal && sign > 0 && rotation is { } r && Math.Abs(r - part.RotationDeg) < 1e-9;
            PlanarVector3 Vec(double q) => local ? Unit(load.AxisIndex) * q : dir * q;
            if (point)
            {
                double s = si + sign * pa;
                if (s < -l.C.Tol || s > part.Length + l.C.Tol) continue;
                var f = Vec(v[0]);
                AddMemberLoad(l, part.Tag, new FemMemberLoad
                {
                    LoadCaseId = lc, CoordinateSystem = local ? "local" : "global", DistributionType = "point",
                    StartOffsetM = Math.Clamp(s, 0, part.Length),
                    QxStart = f.X, QyStart = f.Y, QzStart = f.Z, QxEnd = f.X, QyEnd = f.Y, QzEnd = f.Z,
                });
                found = true;
                break;
            }
            var (a, qa, b, qb) = seg!.Value;
            double sa = si + sign * a, sb = si + sign * b;
            if (sa > sb) (sa, sb, qa, qb) = (sb, sa, qb, qa);
            double x0 = Math.Max(sa, 0), x1 = Math.Min(sb, part.Length);
            if (x1 - x0 <= 1e-9) continue;
            double Qs(double s) => qa + (qb - qa) * (s - sa) / (sb - sa);
            pieces.Add(new BarPiece(part.Tag, lc, local, x0, x1, Vec(Qs(x0)), Vec(Qs(x1))));
            found = true;
        }
        if (!found) l.Lost.Add(e.ElemTag);
    }

    static PlanarVector3 Unit(int axis) => axis switch { 0 => new(1, 0, 0), 1 => new(0, 1, 0), _ => new(0, 0, 1) };

    /// <summary>
    /// Куски погонных нагрузок → <see cref="FemMemberLoad"/>: суперпозиция по (часть, загружение, СК) на участках между
    /// концами кусков, нулевые участки выбрасываются, соседние на одной прямой сливаются; постоянный участок — «uniform».
    /// </summary>
    static void FlushBarPieces(LoadCtx l, List<BarPiece> pieces)
    {
        var length = l.Bars.ToDictionary(p => p.Tag, p => p.Length, StringComparer.Ordinal);
        foreach (var group in pieces.GroupBy(p => (p.PartTag, p.LoadCase, p.Local)))
        {
            var list = group.ToList();
            var xs = new List<double>();
            foreach (var x in list.SelectMany(p => new[] { p.S0, p.S1 }).Order())
                if (xs.Count == 0 || x - xs[^1] > 1e-9) xs.Add(x);
            double eps = 1e-9 * Math.Max(list.Max(p => Math.Max(p.Q0.Length, p.Q1.Length)), 1e-12);
            var runs = new List<(double S0, double S1, PlanarVector3 Q0, PlanarVector3 Q1)>();
            for (int k = 0; k + 1 < xs.Count; k++)
            {
                double x0 = xs[k], x1 = xs[k + 1], mid = (x0 + x1) / 2;
                var cover = list.Where(p => p.S0 <= mid && p.S1 >= mid).ToList();
                var q0 = cover.Aggregate(PlanarVector3.Zero, (s, p) => s + p.At(x0));
                var q1 = cover.Aggregate(PlanarVector3.Zero, (s, p) => s + p.At(x1));
                if (q0.Length <= eps && q1.Length <= eps) continue;
                if (runs.Count > 0 && Math.Abs(runs[^1].S1 - x0) <= 1e-9 && Continues(runs[^1], x1, q0, q1, eps))
                    runs[^1] = (runs[^1].S0, x1, runs[^1].Q0, q1);
                else
                    runs.Add((x0, x1, q0, q1));
            }
            foreach (var r in runs)
            {
                bool uniform = (r.Q1 - r.Q0).Length <= eps;
                var q1 = uniform ? r.Q0 : r.Q1;
                AddMemberLoad(l, group.Key.PartTag, new FemMemberLoad
                {
                    LoadCaseId = group.Key.LoadCase, CoordinateSystem = group.Key.Local ? "local" : "global",
                    DistributionType = uniform ? "uniform" : "trapezoidal",
                    StartOffsetM = r.S0, EndOffsetM = Math.Max(0, length[group.Key.PartTag] - r.S1),
                    QxStart = r.Q0.X, QyStart = r.Q0.Y, QzStart = r.Q0.Z, QxEnd = q1.X, QyEnd = q1.Y, QzEnd = q1.Z,
                });
            }
        }
    }

    /// <summary>Участок до x1 со значениями q0 → q1 продолжает прогон по той же прямой.</summary>
    static bool Continues((double S0, double S1, PlanarVector3 Q0, PlanarVector3 Q1) run, double x1, PlanarVector3 q0, PlanarVector3 q1, double eps)
    {
        if ((run.Q1 - q0).Length > eps) return false;
        double ratio = (x1 - run.S0) / (run.S1 - run.S0);
        return (run.Q0 + (run.Q1 - run.Q0) * ratio - q1).Length <= eps * Math.Max(1, ratio);
    }

    /// <summary>Нагрузки на КЭ родителя полосы: цели разрешаются в КЭ сетки родителя, дальше — по типу КЭ и виду.</summary>
    static void TransferElementLoads(LoadCtx l, List<BarPiece> pieces)
    {
        var shells = new ShellLoads(l);
        foreach (var load in l.Parent.ElementLoads)
            foreach (var e in FemLoadTargets.Resolve(load, l.Mesh, []))
            {
                if (!l.BandTags.Contains(e.ElemTag) || l.Mesh.Geometry(e) is not { } g) continue;
                if (load.LoadKind == FemElementLoadKinds.SelfWeight) shells.SelfWeight(load, e, g);
                else if (g.IsBar) BarElementLoad(l, load, e, g, l.Mesh.BarRotationDeg(e), pieces);
                else if (g.IsShell) shells.Add(load, e, g);
            }
        shells.Flush();
    }
}
