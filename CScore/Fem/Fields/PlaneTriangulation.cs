namespace CScore.Fem.Fields;

/// <summary>Триангуляция Делоне точек участка в его плоскости (U, V) и поиск треугольника, содержащего точку.</summary>
sealed class PlaneTriangulation
{
    readonly (double U, double V)[] _p;
    readonly int[] _tri;                 // тройки индексов точек
    readonly List<int>[] _cells;         // треугольники по клеткам равномерной сетки
    readonly int _nx, _ny;
    readonly double _u0, _v0, _cu, _cv;

    PlaneTriangulation((double U, double V)[] p, int[] tri)
    {
        _p = p;
        _tri = tri;
        int nt = tri.Length / 3;
        _nx = _ny = Math.Max(1, (int)Math.Sqrt(nt));
        _u0 = p.Min(x => x.U); _v0 = p.Min(x => x.V);
        _cu = Math.Max((p.Max(x => x.U) - _u0) / _nx, 1e-12);
        _cv = Math.Max((p.Max(x => x.V) - _v0) / _ny, 1e-12);
        _cells = Enumerable.Range(0, _nx * _ny).Select(_ => new List<int>()).ToArray();
        for (int t = 0; t < nt; t++)
        {
            var (a, b, c) = (p[tri[3 * t]], p[tri[3 * t + 1]], p[tri[3 * t + 2]]);
            int i0 = Cell(Math.Min(a.U, Math.Min(b.U, c.U)), _u0, _cu, _nx), i1 = Cell(Math.Max(a.U, Math.Max(b.U, c.U)), _u0, _cu, _nx);
            int j0 = Cell(Math.Min(a.V, Math.Min(b.V, c.V)), _v0, _cv, _ny), j1 = Cell(Math.Max(a.V, Math.Max(b.V, c.V)), _v0, _cv, _ny);
            for (int i = i0; i <= i1; i++)
                for (int j = j0; j <= j1; j++) _cells[j * _nx + i].Add(t);
        }
    }

    static int Cell(double x, double x0, double h, int n) => Math.Clamp((int)((x - x0) / h), 0, n - 1);

    /// <summary>Null — меньше трёх точек или все на одной прямой.</summary>
    public static PlaneTriangulation? Build(IReadOnlyList<(double U, double V)> points)
    {
        var tri = CSTriangulation.PointDelaunay.Triangulate(points);
        return tri == null ? null : new PlaneTriangulation(points.ToArray(), tri);
    }

    /// <summary>Треугольник, содержащий (u, v), и барицентрические веса его вершин.</summary>
    public bool TryLocate(double u, double v, out (int A, int B, int C) ids, out (double A, double B, double C) w)
    {
        ids = default;
        w = default;
        double eps = 1e-9 * Math.Max(_cu * _nx, _cv * _ny);
        if (u < _u0 - eps || v < _v0 - eps || u > _u0 + _cu * _nx + eps || v > _v0 + _cv * _ny + eps) return false;
        foreach (int t in _cells[Cell(v, _v0, _cv, _ny) * _nx + Cell(u, _u0, _cu, _nx)])
        {
            int ia = _tri[3 * t], ib = _tri[3 * t + 1], ic = _tri[3 * t + 2];
            var (a, b, c) = (_p[ia], _p[ib], _p[ic]);
            double det = (b.V - c.V) * (a.U - c.U) + (c.U - b.U) * (a.V - c.V);
            if (Math.Abs(det) < 1e-14) continue;
            double wa = ((b.V - c.V) * (u - c.U) + (c.U - b.U) * (v - c.V)) / det;
            double wb = ((c.V - a.V) * (u - c.U) + (a.U - c.U) * (v - c.V)) / det;
            double wc = 1 - wa - wb;
            if (wa < -1e-9 || wb < -1e-9 || wc < -1e-9) continue;
            ids = (ia, ib, ic);
            w = (wa, wb, wc);
            return true;
        }
        return false;
    }
}
