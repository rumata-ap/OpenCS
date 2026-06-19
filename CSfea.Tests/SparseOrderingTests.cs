using CSfea.Sparse;

namespace CSfea.Tests;

/// <summary>Тесты RCM-переупорядочивания.</summary>
public static class SparseOrderingTests
{
    public static void RunAll()
    {
        TestHarness.Section("SparseOrdering: RCM");
        Rcm_IsValidPermutation();
        Rcm_ReducesBandwidthOnReversedPath();
    }

    static void Rcm_IsValidPermutation()
    {
        // Цепочка 0-1-2-3-4 (трёхдиагональная структура).
        var (colPtr, rowIdx) = PathPattern(5);
        int[] perm = ReverseCuthillMcKee.ComputeOrdering(5, colPtr, rowIdx);
        bool bijection = perm.Length == 5 && perm.OrderBy(i => i).SequenceEqual(Enumerable.Range(0, 5));
        TestHarness.Check("Rcm_IsValidPermutation", bijection);
    }

    static void Rcm_ReducesBandwidthOnReversedPath()
    {
        // Граф-цепочка, занумерованная «вразнобой», чтобы натуральная ширина была большой.
        // Рёбра: 0-4, 4-1, 1-3, 3-2 (путь 0-4-1-3-2). Натуральная полуширина = 4 (ребро 0-4).
        int n = 5;
        var edges = new (int, int)[] { (0, 4), (4, 1), (1, 3), (3, 2) };
        var (colPtr, rowIdx) = PatternFromEdges(n, edges);
        int natBw = Bandwidth(n, edges, Enumerable.Range(0, n).ToArray());

        int[] perm = ReverseCuthillMcKee.ComputeOrdering(n, colPtr, rowIdx);
        int[] iperm = new int[n];
        for (int i = 0; i < n; i++) iperm[perm[i]] = i;
        int rcmBw = BandwidthIperm(edges, iperm);

        TestHarness.Check("Rcm_ReducesBandwidthOnReversedPath", rcmBw < natBw,
            $"natural={natBw}, rcm={rcmBw}");
    }

    static int Bandwidth(int n, (int, int)[] edges, int[] iperm)
        => BandwidthIperm(edges, iperm);

    static int BandwidthIperm((int, int)[] edges, int[] iperm)
    {
        int bw = 0;
        foreach (var (a, b) in edges)
            bw = Math.Max(bw, Math.Abs(iperm[a] - iperm[b]));
        return bw;
    }

    static (int[] colPtr, int[] rowIdx) PathPattern(int n)
    {
        var edges = new List<(int, int)>();
        for (int i = 0; i < n - 1; i++) edges.Add((i, i + 1));
        return PatternFromEdges(n, edges.ToArray());
    }

    static (int[] colPtr, int[] rowIdx) PatternFromEdges(int n, (int, int)[] edges)
    {
        var cols = new List<int>[n];
        for (int i = 0; i < n; i++) cols[i] = new List<int> { i };
        foreach (var (a, b) in edges)
        {
            cols[a].Add(b);
            cols[b].Add(a);
        }
        var colPtr = new int[n + 1];
        var rows = new List<int>();
        for (int c = 0; c < n; c++)
        {
            foreach (int r in cols[c].Distinct().OrderBy(x => x)) rows.Add(r);
            colPtr[c + 1] = rows.Count;
        }
        return (colPtr, rows.ToArray());
    }
}
