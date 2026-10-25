namespace CScore.LimitSurface;

/// <summary>
/// Иерархия осевых габаритов треугольников сетки (N, Mx, My) для запросов «какие треугольники может пересечь
/// прямая B + t·D, t любого знака». Габариты расширены на 1e-7 размаха сетки по оси, поэтому отбор
/// консервативен: треугольник, замкнутую проекцию которого задевает прямая, не теряется из-за округления.
/// Потокобезопасна (только чтение после построения).
/// </summary>
internal sealed class TriangleBvh
{
    const int LeafSize = 4;
    const double Pad = 1e-7;

    // Узел: габарит [Lo, Hi]; лист — диапазон [Start, Start + Count) в _order, иначе дети Left и Left + 1.
    readonly double[] _lo, _hi;
    readonly int[] _left, _start, _count;
    readonly int[] _order;
    int _nodes;

    public TriangleBvh(IReadOnlyList<ForcePoint> vertices, IReadOnlyList<(int A, int B, int C)> triangles)
    {
        int nt = triangles.Count;
        var tlo = new double[3 * nt];
        var thi = new double[3 * nt];
        var cen = new double[3 * nt];
        double[] gLo = [double.PositiveInfinity, double.PositiveInfinity, double.PositiveInfinity];
        double[] gHi = [double.NegativeInfinity, double.NegativeInfinity, double.NegativeInfinity];
        for (int k = 0; k < nt; k++)
        {
            var (a, b, c) = triangles[k];
            for (int ax = 0; ax < 3; ax++)
            {
                double va = Coord(vertices[a], ax), vb = Coord(vertices[b], ax), vc = Coord(vertices[c], ax);
                double lo = Math.Min(va, Math.Min(vb, vc)), hi = Math.Max(va, Math.Max(vb, vc));
                tlo[3 * k + ax] = lo; thi[3 * k + ax] = hi; cen[3 * k + ax] = 0.5 * (lo + hi);
                gLo[ax] = Math.Min(gLo[ax], lo); gHi[ax] = Math.Max(gHi[ax], hi);
            }
        }
        double spanMax = 0;
        for (int ax = 0; ax < 3; ax++) spanMax = Math.Max(spanMax, nt > 0 ? gHi[ax] - gLo[ax] : 0);
        var pad = new double[3];
        for (int ax = 0; ax < 3; ax++)
        {
            double span = nt > 0 ? gHi[ax] - gLo[ax] : 0;
            pad[ax] = Pad * (span > 0 ? span : spanMax > 0 ? spanMax : 1.0);
        }
        for (int k = 0; k < nt; k++)
            for (int ax = 0; ax < 3; ax++) { tlo[3 * k + ax] -= pad[ax]; thi[3 * k + ax] += pad[ax]; }

        _order = Enumerable.Range(0, nt).ToArray();
        int cap = 2 * nt + 1;   // листья не меньше 2 треугольников — узлов не больше 2·nt
        _lo = new double[3 * cap]; _hi = new double[3 * cap];
        _left = new int[cap]; _start = new int[cap]; _count = new int[cap];
        _nodes = 1;
        var stack = new Stack<int>();
        Setup(0, 0, nt);
        stack.Push(0);
        while (stack.Count > 0)
        {
            int node = stack.Pop();
            int start = _start[node], count = _count[node];
            if (count <= LeafSize) continue;
            int axis = 0;
            double best = -1;
            for (int ax = 0; ax < 3; ax++)
            {
                double ext = _hi[3 * node + ax] - _lo[3 * node + ax];
                if (ext > best) { best = ext; axis = ax; }
            }
            Array.Sort(_order, start, count, Comparer<int>.Create((p, q) => cen[3 * p + axis].CompareTo(cen[3 * q + axis])));
            int half = count / 2;
            int left = _nodes;
            _nodes += 2;
            Setup(left, start, half);
            Setup(left + 1, start + half, count - half);
            _left[node] = left;
            _count[node] = 0;
            stack.Push(left);
            stack.Push(left + 1);
        }

        void Setup(int node, int start, int count)
        {
            _start[node] = start; _count[node] = count; _left[node] = -1;
            for (int ax = 0; ax < 3; ax++) { _lo[3 * node + ax] = double.PositiveInfinity; _hi[3 * node + ax] = double.NegativeInfinity; }
            for (int i = start; i < start + count; i++)
            {
                int k = _order[i];
                for (int ax = 0; ax < 3; ax++)
                {
                    _lo[3 * node + ax] = Math.Min(_lo[3 * node + ax], tlo[3 * k + ax]);
                    _hi[3 * node + ax] = Math.Max(_hi[3 * node + ax], thi[3 * k + ax]);
                }
            }
        }
    }

    static double Coord(ForcePoint p, int axis) => axis switch { 0 => p.N, 1 => p.Mx, _ => p.My };

    /// <summary>Номера треугольников, габарит которых пересекает прямая B + t·D, — по возрастанию.</summary>
    public List<int> LineCandidates(ForcePoint b, ForcePoint dir)
    {
        var res = new List<int>();
        if (_order.Length == 0) return res;
        double[] o = [b.N, b.Mx, b.My], d = [dir.N, dir.Mx, dir.My];
        var stack = new Stack<int>();
        stack.Push(0);
        while (stack.Count > 0)
        {
            int node = stack.Pop();
            if (!Hits(node, o, d)) continue;
            if (_left[node] < 0)
            {
                for (int i = _start[node]; i < _start[node] + _count[node]; i++) res.Add(_order[i]);
                continue;
            }
            stack.Push(_left[node]);
            stack.Push(_left[node] + 1);
        }
        res.Sort();
        return res;
    }

    bool Hits(int node, double[] o, double[] d)
    {
        double tMin = double.NegativeInfinity, tMax = double.PositiveInfinity;
        for (int ax = 0; ax < 3; ax++)
        {
            double lo = _lo[3 * node + ax], hi = _hi[3 * node + ax];
            if (d[ax] == 0)
            {
                if (o[ax] < lo || o[ax] > hi) return false;
                continue;
            }
            double t1 = (lo - o[ax]) / d[ax], t2 = (hi - o[ax]) / d[ax];
            if (t1 > t2) (t1, t2) = (t2, t1);
            if (t1 > tMin) tMin = t1;
            if (t2 < tMax) tMax = t2;
            if (tMin > tMax) return false;
        }
        return true;
    }
}
