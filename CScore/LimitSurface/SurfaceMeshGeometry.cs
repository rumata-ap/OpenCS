namespace CScore.LimitSurface;

/// <summary>Геометрия сетки поверхности: сшивка меридианов, якоря в изломах, расстояния, объём.</summary>
public static class SurfaceMeshGeometry
{
    /// <summary>
    /// Сшивает два меридиана, упорядоченных по параметру t, полосой треугольников («слияние» по t).
    /// Треугольники с повторяющимися индексами (у общих полюсов) отбрасываются.
    /// </summary>
    public static List<(int A, int B, int C)> Loft(
        IReadOnlyList<int> aIdx, IReadOnlyList<double> aT, IReadOnlyList<int> bIdx, IReadOnlyList<double> bT)
    {
        var tris = new List<(int A, int B, int C)>();
        int i = 0, j = 0, na = aIdx.Count - 1, nb = bIdx.Count - 1;
        while (i < na || j < nb)
        {
            (int A, int B, int C) tri;
            if (j >= nb || (i < na && aT[i + 1] <= bT[j + 1]))
            {
                tri = (aIdx[i], aIdx[i + 1], bIdx[j]);
                i++;
            }
            else
            {
                tri = (aIdx[i], bIdx[j + 1], bIdx[j]);
                j++;
            }
            if (tri.A != tri.B && tri.B != tri.C && tri.A != tri.C)
                tris.Add(tri);
        }
        return tris;
    }

    /// <summary>Пары позиций наибольшей общей подпоследовательности.</summary>
    public static List<(int I, int J)> LcsPairs<T>(IReadOnlyList<T> a, IReadOnlyList<T> b) where T : IEquatable<T>
    {
        int n = a.Count, m = b.Count;
        var L = new int[n + 1, m + 1];
        for (int i = n - 1; i >= 0; i--)
            for (int j = m - 1; j >= 0; j--)
                L[i, j] = a[i].Equals(b[j]) ? L[i + 1, j + 1] + 1 : Math.Max(L[i + 1, j], L[i, j + 1]);
        var pairs = new List<(int I, int J)>();
        int p = 0, q = 0;
        while (p < n && q < m)
        {
            if (a[p].Equals(b[q])) { pairs.Add((p, q)); p++; q++; }
            else if (L[p + 1, q] >= L[p, q + 1]) p++;
            else q++;
        }
        return pairs;
    }

    /// <summary>
    /// Сшивка с якорями: изломы с одинаковым переходом на обоих меридианах — образ одного ребра
    /// многогранника деформаций — соединяются ребром сетки (сопоставление — LCS переходов,
    /// полюса — крайние якоря); между якорями — <see cref="Loft"/> по параметру, нормированному на участок.
    /// </summary>
    public static List<(int A, int B, int C)> LoftAnchored(
        IReadOnlyList<int> aIdx, IReadOnlyList<double> aT, IReadOnlyList<KinkTransition?> aTr,
        IReadOnlyList<int> bIdx, IReadOnlyList<double> bT, IReadOnlyList<KinkTransition?> bTr)
    {
        var pa = Enumerable.Range(0, aTr.Count).Where(k => aTr[k] is not null).ToList();
        var pb = Enumerable.Range(0, bTr.Count).Where(k => bTr[k] is not null).ToList();
        var anchors = new List<(int A, int B)> { (0, 0) };
        foreach (var (i, j) in LcsPairs(pa.Select(k => aTr[k]!.Value).ToList(), pb.Select(k => bTr[k]!.Value).ToList()))
            anchors.Add((pa[i], pb[j]));
        anchors.Add((aIdx.Count - 1, bIdx.Count - 1));

        var tris = new List<(int A, int B, int C)>();
        for (int k = 0; k + 1 < anchors.Count; k++)
        {
            var (a0, b0) = anchors[k];
            var (a1, b1) = anchors[k + 1];
            tris.AddRange(Loft(Range(aIdx, a0, a1), Norm(aT, a0, a1), Range(bIdx, b0, b1), Norm(bT, b0, b1)));
        }
        return tris;
    }

    static List<int> Range(IReadOnlyList<int> v, int lo, int hi) => v.Skip(lo).Take(hi - lo + 1).ToList();

    static List<double> Norm(IReadOnlyList<double> t, int lo, int hi)
    {
        double t0 = t[lo], d = t[hi] - t0;
        if (d == 0) d = 1;
        return Enumerable.Range(lo, hi - lo + 1).Select(k => (t[k] - t0) / d).ToList();
    }

    /// <summary>Расстояние от точки до отрезка.</summary>
    public static double PointSegmentDistance(ForcePoint p, ForcePoint a, ForcePoint b)
    {
        var ab = b - a;
        double l2 = ab.Dot(ab);
        double u = l2 > 0 ? Math.Clamp((p - a).Dot(ab) / l2, 0, 1) : 0;
        return (p - (a + u * ab)).Length;
    }

    /// <summary>Расстояние от точки до треугольника (Эриксон, Real-Time Collision Detection, 5.1.5).</summary>
    public static double PointTriangleDistance(ForcePoint p, ForcePoint a, ForcePoint b, ForcePoint c)
    {
        // Совпавшие вершины (усилия на площадках диаграмм) — знаменатели ниже равны |ребро|² = 0 → NaN
        if (a == b || b == c || c == a)
            return Math.Min(PointSegmentDistance(p, a, b), Math.Min(PointSegmentDistance(p, b, c), PointSegmentDistance(p, c, a)));
        var ab = b - a; var ac = c - a; var ap = p - a;
        double d1 = ab.Dot(ap), d2 = ac.Dot(ap);
        if (d1 <= 0 && d2 <= 0) return (p - a).Length;
        var bp = p - b;
        double d3 = ab.Dot(bp), d4 = ac.Dot(bp);
        if (d3 >= 0 && d4 <= d3) return (p - b).Length;
        double vc = d1 * d4 - d3 * d2;
        if (vc <= 0 && d1 >= 0 && d3 <= 0) return (p - (a + d1 / (d1 - d3) * ab)).Length;
        var cp = p - c;
        double d5 = ab.Dot(cp), d6 = ac.Dot(cp);
        if (d6 >= 0 && d5 <= d6) return (p - c).Length;
        double vb = d5 * d2 - d1 * d6;
        if (vb <= 0 && d2 >= 0 && d6 <= 0) return (p - (a + d2 / (d2 - d6) * ac)).Length;
        double va = d3 * d6 - d5 * d4;
        if (va <= 0 && d4 - d3 >= 0 && d5 - d6 >= 0)
            return (p - (b + (d4 - d3) / ((d4 - d3) + (d5 - d6)) * (c - b))).Length;
        double sum = va + vb + vc;
        if (sum == 0)   // вырожденный треугольник — ближайшее из рёбер
            return Math.Min(PointSegmentDistance(p, a, b), Math.Min(PointSegmentDistance(p, b, c), PointSegmentDistance(p, c, a)));
        double v = vb / sum, w = vc / sum;
        return (p - (a + v * ab + w * ac)).Length;
    }

    /// <summary>Ориентированный объём замкнутой сетки (положителен при нормалях наружу).</summary>
    public static double SignedVolume(IReadOnlyList<ForcePoint> v, IReadOnlyList<(int A, int B, int C)> tris)
    {
        var o = new ForcePoint(v.Average(p => p.N), v.Average(p => p.Mx), v.Average(p => p.My));
        double vol = 0;
        foreach (var (a, b, c) in tris)
            vol += (v[a] - o).Dot((v[b] - o).Cross(v[c] - o));
        return vol / 6.0;
    }
}
