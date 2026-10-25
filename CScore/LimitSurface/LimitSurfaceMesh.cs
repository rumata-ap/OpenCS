namespace CScore.LimitSurface;

/// <summary>Режим луча для коэффициента использования — как режимы пакетов limit_force.</summary>
public enum UtilizationMode
{
    /// <summary>k·(N, Mx, My) из начала координат.</summary>
    All,
    /// <summary>k·(Mx, My) при N = const.</summary>
    Moment,
    /// <summary>k·N при (Mx, My) = const.</summary>
    Axial,
}

/// <summary>
/// Результат проверки точки по сетке: внутри ли точка (чётность пересечений), Кисп — коэффициент
/// использования (η в СП 63 — продольный изгиб), лежит ли начало
/// луча внутри тела, точка поверхности на луче, интерполированная плоскость деформаций и активное
/// ограничение (вершина с наибольшим барицентрическим весом).
/// </summary>
public sealed record SurfaceUtilization(
    bool Inside, double Ratio, bool RayOriginInside,
    ForcePoint? Capacity, Kurvature? StrainPlane, ActiveConstraint? Active);

/// <summary>
/// Замкнутая треугольная сетка поверхности (N, Mx, My). Связность — из параметризации области
/// деформаций, поэтому сетка может быть невыпуклой. Запросы используют символическое возмущение:
/// касания вершин и рёбер учитываются ровно один раз, без допусков на близость.
/// </summary>
public sealed class LimitSurfaceMesh
{
    /// <summary>Создаёт сетку.</summary>
    public LimitSurfaceMesh(
        IReadOnlyList<ForcePoint> vertices,
        IReadOnlyList<(int A, int B, int C)> triangles,
        IReadOnlyList<Kurvature>? strainPlanes = null,
        IReadOnlyList<ActiveConstraint>? active = null,
        IReadOnlyList<KinkTransition?>? transitions = null,
        IReadOnlyList<int>? meridianOf = null,
        IReadOnlyList<(int Count, double MaxDeviation)>? refineLog = null,
        IReadOnlyList<(double Theta, double Phi)>? angles = null)
    {
        Vertices = vertices;
        Triangles = triangles;
        StrainPlanes = strainPlanes ?? [];
        Active = active ?? [];
        Transitions = transitions ?? [];
        MeridianOf = meridianOf ?? [];
        RefineLog = refineLog ?? [];
        Angles = angles ?? [];
        MeridianCount = MeridianOf.Count == 0 ? 0 : MeridianOf.Max() + 1;
        _bvh = new Lazy<TriangleBvh>(() => new TriangleBvh(Vertices, Triangles));
    }

    /// <summary>Вершины (N, Mx, My).</summary>
    public IReadOnlyList<ForcePoint> Vertices { get; }
    /// <summary>Треугольники, нормали наружу.</summary>
    public IReadOnlyList<(int A, int B, int C)> Triangles { get; }
    /// <summary>Плоскость деформаций каждой вершины (пусто у тестовых сеток).</summary>
    public IReadOnlyList<Kurvature> StrainPlanes { get; }
    /// <summary>Активное ограничение каждой вершины.</summary>
    public IReadOnlyList<ActiveConstraint> Active { get; }
    /// <summary>Переход у вершины-излома, иначе null.</summary>
    public IReadOnlyList<KinkTransition?> Transitions { get; }
    /// <summary>Номер меридиана вершины (−1 — полюс).</summary>
    public IReadOnlyList<int> MeridianOf { get; }
    /// <summary>Журнал адаптации по φ: число новых меридианов и наибольшее отклонение на круге.</summary>
    public IReadOnlyList<(int Count, double MaxDeviation)> RefineLog { get; }
    /// <summary>Число меридианов.</summary>
    public int MeridianCount { get; }
    /// <summary>
    /// Параметры вершины в области деформаций: θ (0 — полюс растяжения… π) и φ меридиана (у полюсов — 0).
    /// Пусто у тестовых сеток и сохранённых без углов. Нужны для линий меридианов и параллелей.
    /// </summary>
    public IReadOnlyList<(double Theta, double Phi)> Angles { get; }

    /// <summary>Срез Mx–My при N = const.</summary>
    public IReadOnlyList<IReadOnlyList<ForcePoint>> SliceAtN(double n) => SliceLoops(new(1, 0, 0), n);
    /// <summary>Срез N–My при Mx = const.</summary>
    public IReadOnlyList<IReadOnlyList<ForcePoint>> SliceAtMx(double mx) => SliceLoops(new(0, 1, 0), mx);
    /// <summary>Срез N–Mx при My = const.</summary>
    public IReadOnlyList<IReadOnlyList<ForcePoint>> SliceAtMy(double my) => SliceLoops(new(0, 0, 1), my);
    /// <summary>Срез N–M плоскостью через ось N в направлении момента (cos α, sin α).</summary>
    public IReadOnlyList<IReadOnlyList<ForcePoint>> SliceNM(double alphaRad) =>
        SliceLoops(new(0, -Math.Sin(alphaRad), Math.Cos(alphaRad)), 0);

    /// <summary>
    /// Пересечение с плоскостью normal·x = d: все замкнутые контуры в порядке обхода. Вершина на
    /// плоскости считается «сверху»; точка пересечения идентифицируется ребром сетки.
    /// </summary>
    public IReadOnlyList<IReadOnlyList<ForcePoint>> SliceLoops(ForcePoint normal, double d)
    {
        var s = Vertices.Select(v => v.Dot(normal) - d).ToArray();
        var pts = new Dictionary<(int, int), ForcePoint>();
        var adj = new Dictionary<(int, int), List<(int, int)>>();

        (int, int) Edge(int i, int j)
        {
            var key = i < j ? (i, j) : (j, i);
            if (!pts.ContainsKey(key))
            {
                double t = s[i] / (s[i] - s[j]);
                pts[key] = Vertices[i] + t * (Vertices[j] - Vertices[i]);
            }
            return key;
        }

        foreach (var (a, b, c) in Triangles)
        {
            bool pa = s[a] >= 0, pb = s[b] >= 0, pc = s[c] >= 0;
            if (pa == pb && pb == pc) continue;
            var keys = new List<(int, int)>(2);
            if (pa != pb) keys.Add(Edge(a, b));
            if (pb != pc) keys.Add(Edge(b, c));
            if (pc != pa) keys.Add(Edge(c, a));
            foreach (var (k1, k2) in new[] { (keys[0], keys[1]), (keys[1], keys[0]) })
            {
                if (!adj.TryGetValue(k1, out var list)) adj[k1] = list = [];
                list.Add(k2);
            }
        }

        var loops = new List<IReadOnlyList<ForcePoint>>();
        var visited = new HashSet<(int, int)>();
        var starts = adj.Where(e => e.Value.Count == 1).Select(e => e.Key).Concat(adj.Keys).ToList();
        foreach (var start in starts)
        {
            if (!visited.Add(start)) continue;
            var chain = new List<ForcePoint> { pts[start] };
            (int, int)? prev = null;
            var cur = start;
            while (true)
            {
                var next = adj[cur].Cast<(int, int)?>().FirstOrDefault(k => k != prev && !visited.Contains(k!.Value));
                if (next is null) break;
                visited.Add(next.Value);
                chain.Add(pts[next.Value]);
                prev = cur;
                cur = next.Value;
            }
            if (chain.Count >= 3) loops.Add(chain);
        }
        return loops;
    }

    /// <summary>
    /// Коэффициент использования по лучу B + t·D (режимы — <see cref="UtilizationMode"/>); F — при t = 1.
    /// Внутри (нечётно пересечений с t &gt; 1) — Кисп = 1/t ближайшего за точкой; снаружи — Кисп = 1/t
    /// последнего перед точкой; если до точки луч поверхность не пересекал — Кисп = ∞.
    /// </summary>
    public SurfaceUtilization Utilization(double n, double mx, double my, UtilizationMode mode)
    {
        var (b, dir) = mode switch
        {
            UtilizationMode.All => (new ForcePoint(0, 0, 0), new ForcePoint(n, mx, my)),
            UtilizationMode.Moment => (new ForcePoint(n, 0, 0), new ForcePoint(0, mx, my)),
            UtilizationMode.Axial => (new ForcePoint(0, mx, my), new ForcePoint(n, 0, 0)),
            _ => throw new ArgumentOutOfRangeException(nameof(mode)),
        };
        return Utilization(b, dir);
    }

    /// <summary>
    /// Коэффициент использования по произвольному лучу B + t·D, F = B + D (те же правила, что у режимов;
    /// <see cref="SurfaceUtilization.RayOriginInside"/> — лежит ли B внутри тела).
    /// </summary>
    public SurfaceUtilization Utilization(ForcePoint b, ForcePoint dir)
    {
        if (dir.Length == 0)
        {
            bool ins = RayHits(b, new ForcePoint(0.31, 0.53, 0.79)).Count(h => h.T > 0) % 2 == 1;
            return new SurfaceUtilization(ins, ins ? 0 : double.PositiveInfinity, ins, null, null, null);
        }

        var hits = RayHits(b, dir);
        bool originInside = hits.Count(h => h.T > 0) % 2 == 1;
        bool inside = hits.Count(h => h.T > 1) % 2 == 1;
        // Nullable: у пустой выборки MinBy/MaxBy вернут null, а не исключение
        Hit? cap = inside
            ? hits.Where(h => h.T > 1).Select(h => (Hit?)h).MinBy(h => h!.Value.T)
            : hits.Where(h => h.T > 0 && h.T <= 1).Select(h => (Hit?)h).MaxBy(h => h!.Value.T);
        if (cap is null)
            return new SurfaceUtilization(inside, inside ? 0 : double.PositiveInfinity, originInside, null, null, null);

        var h = cap.Value;
        var (ta, tb, tc) = Triangles[h.Tri];
        Kurvature? plane = null;
        ActiveConstraint? act = null;
        if (StrainPlanes.Count == Vertices.Count)
        {
            Kurvature ka = StrainPlanes[ta], kb = StrainPlanes[tb], kc = StrainPlanes[tc];
            plane = new Kurvature
            {
                e0 = h.Wa * ka.e0 + h.Wb * kb.e0 + h.Wc * kc.e0,
                ky = h.Wa * ka.ky + h.Wb * kb.ky + h.Wc * kc.ky,
                kz = h.Wa * ka.kz + h.Wb * kb.kz + h.Wc * kc.kz,
            };
        }
        if (Active.Count == Vertices.Count)
            act = h.Wa >= h.Wb && h.Wa >= h.Wc ? Active[ta] : h.Wb >= h.Wc ? Active[tb] : Active[tc];
        return new SurfaceUtilization(inside, 1.0 / h.T, originInside, b + h.T * dir, plane, act);
    }

    readonly record struct Hit(double T, int Tri, double Wa, double Wb, double Wc);

    /// <summary>
    /// Все пересечения прямой B + t·D с треугольниками (t любого знака). Проекция вдоль D на плоскость,
    /// попадание начала координат в проекцию треугольника — по знакам рёберных функций с
    /// символическим возмущением (Simulation of Simplicity): начало координат в проекции смещается
    /// на (δ, δ²), δ → 0. E(i,j) = −E(j,i) точно, а ноль решается знаками δ- и δ²-членов — одной и
    /// той же смещённой точкой для всех треугольников, поэтому проход через общее ребро/вершину
    /// засчитывается ровно один раз.
    /// </summary>
    List<Hit> RayHits(ForcePoint b, ForcePoint dir)
    {
        var dn = (1.0 / dir.Length) * dir;
        var axis = Math.Abs(dn.N) <= Math.Abs(dn.Mx) && Math.Abs(dn.N) <= Math.Abs(dn.My) ? new ForcePoint(1, 0, 0)
                 : Math.Abs(dn.Mx) <= Math.Abs(dn.My) ? new ForcePoint(0, 1, 0) : new ForcePoint(0, 0, 1);
        var e1 = dn.Cross(axis); e1 = (1.0 / e1.Length) * e1;
        var e2 = dn.Cross(e1);
        double d2 = dir.Dot(dir);

        // Проекции считаются только у вершин треугольников, чьи габариты задевает прямая (BVH), — те же
        // выражения, что и при полном проходе, и тот же порядок треугольников: результат не меняется.
        var qx = new double[3]; var qy = new double[3]; var ht = new double[3]; var id = new int[3];
        void Project(int slot, int v)
        {
            var r = Vertices[v] - b;
            qx[slot] = r.Dot(e1); qy[slot] = r.Dot(e2); ht[slot] = r.Dot(dir) / d2; id[slot] = v;
        }

        double E(int i, int j) => qx[i] * qy[j] - qy[i] * qx[j];
        // (qi − p)×(qj − p) при p = (δ, δ²) = E(i,j) − δ·(qj.y − qi.y) + δ²·(qj.x − qi.x)
        int Sgn(int i, int j)
        {
            double e = E(i, j);
            if (e != 0) return e > 0 ? 1 : -1;
            double d1 = qy[i] - qy[j];
            if (d1 != 0) return d1 > 0 ? 1 : -1;
            double d2 = qx[j] - qx[i];
            if (d2 != 0) return d2 > 0 ? 1 : -1;
            return id[i] < id[j] ? 1 : -1;   // совпавшие проекции вершин — вырожденное ребро
        }

        var hits = new List<Hit>();
        foreach (int k in _bvh.Value.LineCandidates(b, dir))
        {
            var (a, bb, c) = Triangles[k];
            Project(0, a); Project(1, bb); Project(2, c);
            int sa = Sgn(1, 2), sb = Sgn(2, 0), sc = Sgn(0, 1);
            if (sa != sb || sb != sc) continue;
            double wa = E(1, 2), wb = E(2, 0), wc = E(0, 1), sum = wa + wb + wc;
            if (sum == 0) { wa = wb = wc = 1.0 / 3; } else { wa /= sum; wb /= sum; wc /= sum; }
            hits.Add(new Hit(wa * ht[0] + wb * ht[1] + wc * ht[2], k, wa, wb, wc));
        }
        return hits;
    }

    /// <summary>Иерархия габаритов треугольников для запросов прямой; строится при первом запросе.</summary>
    readonly Lazy<TriangleBvh> _bvh;
}
