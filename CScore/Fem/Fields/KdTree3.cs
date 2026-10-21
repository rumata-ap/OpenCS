namespace CScore.Fem.Fields;

/// <summary>k-d дерево точек: ближайшая и k ближайших. Индексы — во входном списке.</summary>
public sealed class KdTree3
{
    readonly FieldVec3[] _p;
    readonly int[] _order;   // узел дерева — середина отрезка [lo, hi) этой перестановки

    public KdTree3(IReadOnlyList<FieldVec3> points)
    {
        _p = points.ToArray();
        _order = Enumerable.Range(0, _p.Length).ToArray();
        Build(0, _p.Length, 0);
    }

    public int Count => _p.Length;

    void Build(int lo, int hi, int axis)
    {
        if (hi - lo <= 1) return;
        int mid = (lo + hi) / 2;
        Array.Sort(_order, lo, hi - lo, Comparer<int>.Create((a, b) => Coord(_p[a], axis).CompareTo(Coord(_p[b], axis))));
        Build(lo, mid, (axis + 1) % 3);
        Build(mid + 1, hi, (axis + 1) % 3);
    }

    static double Coord(FieldVec3 v, int axis) => axis == 0 ? v.X : axis == 1 ? v.Y : v.Z;

    public (int Index, double Distance) Nearest(FieldVec3 q) =>
        _p.Length == 0 ? (-1, double.PositiveInfinity) : KNearest(q, 1)[0];

    public IReadOnlyList<(int Index, double Distance)> KNearest(FieldVec3 q, int k)
    {
        var best = new List<(int Index, double D2)>(k + 1);
        if (k > 0) Search(q, k, 0, _p.Length, 0, best);
        return best.Select(b => (b.Index, Math.Sqrt(b.D2))).ToList();
    }

    void Search(FieldVec3 q, int k, int lo, int hi, int axis, List<(int Index, double D2)> best)
    {
        if (lo >= hi) return;
        int mid = (lo + hi) / 2, i = _order[mid];
        var d = _p[i] - q;
        double d2 = d.Dot(d);
        if (best.Count < k || d2 < best[^1].D2)
        {
            int pos = best.FindIndex(b => b.D2 > d2);
            best.Insert(pos < 0 ? best.Count : pos, (i, d2));
            if (best.Count > k) best.RemoveAt(best.Count - 1);
        }
        double diff = Coord(q, axis) - Coord(_p[i], axis);
        int next = (axis + 1) % 3;
        var (nearLo, nearHi, farLo, farHi) = diff < 0 ? (lo, mid, mid + 1, hi) : (mid + 1, hi, lo, mid);
        Search(q, k, nearLo, nearHi, next, best);
        if (best.Count < k || diff * diff < best[^1].D2) Search(q, k, farLo, farHi, next, best);
    }
}
