namespace CScore.LimitSurface;

/// <summary>
/// Узел меридиана сетки поверхности: параметр t ∈ [0, π] вдоль меридиана (0 — растянутый полюс, π —
/// сжатый), усилия, плоскость деформаций, активное ограничение и переход (у излома).
/// </summary>
internal readonly record struct MeridianNode(
    double T, Kurvature Plane, ForcePoint Force, ActiveConstraint Active, KinkTransition? Transition);

/// <summary>
/// Общая сшивка меридианов в замкнутую сетку для построителей поверхностей: меридианы — упорядоченные
/// по t узлы между двумя общими полюсами, соседние сшиваются полосами с якорями в общих изломах.
/// </summary>
internal static class MeridianStitcher
{
    /// <summary>Индексы, параметры и переходы меридиана с полюсами 0 и 1 по краям.</summary>
    public static (List<int> Idx, List<double> T, List<KinkTransition?> Tr) Strip(IReadOnlyList<MeridianNode> m, int offset)
    {
        var idx = new List<int> { 0 }; var t = new List<double> { 0 }; var tr = new List<KinkTransition?> { null };
        for (int i = 0; i < m.Count; i++) { idx.Add(offset + i); t.Add(m[i].T); tr.Add(m[i].Transition); }
        idx.Add(1); t.Add(Math.PI); tr.Add(null);
        return (idx, t, tr);
    }

    /// <summary>Наибольшее расстояние вершин среднего меридиана до полосы (a, b), нормированные усилия.</summary>
    public static double StripDeviation(MeridianNode top, MeridianNode bot,
        IReadOnlyList<MeridianNode> ma, IReadOnlyList<MeridianNode> mb, IReadOnlyList<MeridianNode> mm, ForcePoint s)
    {
        var pts = new List<ForcePoint> { top.Force.Scale(s), bot.Force.Scale(s) };
        pts.AddRange(ma.Select(x => x.Force.Scale(s)));
        pts.AddRange(mb.Select(x => x.Force.Scale(s)));
        var (ia, ta, ra) = Strip(ma, 2);
        var (ib, tb, rb) = Strip(mb, 2 + ma.Count);
        var tris = SurfaceMeshGeometry.LoftAnchored(ia, ta, ra, ib, tb, rb);
        double worst = 0;
        foreach (var q in mm.Select(x => x.Force.Scale(s)))
        {
            double best = double.PositiveInfinity;
            foreach (var (a, b, c) in tris)
                best = Math.Min(best, SurfaceMeshGeometry.PointTriangleDistance(q, pts[a], pts[b], pts[c]));
            worst = Math.Max(worst, best);
        }
        return worst;
    }

    /// <summary>
    /// Собирает сетку: полюса — вершины 0 и 1, затем узлы меридианов по порядку углов
    /// <paramref name="phis"/> (замкнутый круг); нормали — наружу.
    /// </summary>
    public static LimitSurfaceMesh Assemble(MeridianNode top, MeridianNode bot, IReadOnlyList<double> phis,
        IReadOnlyList<IReadOnlyList<MeridianNode>> mers, IReadOnlyList<(int, double)> log)
    {
        var v = new List<ForcePoint> { top.Force, bot.Force };
        var planes = new List<Kurvature> { top.Plane, bot.Plane };
        var act = new List<ActiveConstraint> { top.Active, bot.Active };
        var trs = new List<KinkTransition?> { null, null };
        var merOf = new List<int> { -1, -1 };
        var angles = new List<(double Theta, double Phi)> { (top.T, 0), (bot.T, 0) };
        var strips = new List<(List<int>, List<double>, List<KinkTransition?>)>();
        for (int k = 0; k < mers.Count; k++)
        {
            strips.Add(Strip(mers[k], v.Count));
            foreach (var nd in mers[k])
            {
                v.Add(nd.Force); planes.Add(nd.Plane); act.Add(nd.Active); trs.Add(nd.Transition); merOf.Add(k);
                angles.Add((nd.T, phis[k]));
            }
        }
        var tris = new List<(int A, int B, int C)>();
        for (int k = 0; k < strips.Count; k++)
        {
            var (ia, ta, ra) = strips[k];
            var (ib, tb, rb) = strips[(k + 1) % strips.Count];
            tris.AddRange(SurfaceMeshGeometry.LoftAnchored(ia, ta, ra, ib, tb, rb));
        }
        if (SurfaceMeshGeometry.SignedVolume(v, tris) < 0)
            tris = tris.Select(t => (t.A, t.C, t.B)).ToList();
        return new LimitSurfaceMesh(v, tris, planes, act, trs, merOf, log, angles);
    }
}
