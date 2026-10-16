using System.Diagnostics;
using OpenCS.Services;
using Xunit;
using Xunit.Abstractions;

namespace OpenCS.Tests;

/// <summary>Огибающая группы комбинаций по величинам загружений: min/max, комбинация-источник, NaN, отмена.</summary>
public sealed class FemCsfeaCombinationEnvelopeTests(ITestOutputHelper output)
{
    [Fact]
    public void Build_MinMaxAndSources()
    {
        double[] a = [1, double.NaN, 2, 0];
        double[] b = [-1, 3, 0, 0];
        var combinations = new IReadOnlyList<(int, double)>[]
        {
            [(0, 1)],            // a
            [(0, 1), (1, 1)],    // a + b
            [(1, -1)],           // −b
        };
        // Последняя величина — нелинейная (досчёт по записи из 4 величин): |v0| + |v2|.
        var env = FemCsfeaCombinationEnvelope.Build([a, b], combinations,
            (v, offset) => v[3] = Math.Abs(v[0]) + Math.Abs(v[2]), recordSize: 4);

        Assert.Equal(3, env.Count);
        // v0: 1, 0, 1 → max 1 (первая), min 0 (a + b).
        Assert.Equal((1.0, 0, 0.0, 1), (env.Max[0], env.MaxSource[0], env.Min[0], env.MinSource[0]));
        // v1: NaN у «a» — комбинации с ним пропускаются; остаётся −b = −3.
        Assert.Equal((-3.0, 2, -3.0, 2), (env.Max[1], env.MaxSource[1], env.Min[1], env.MinSource[1]));
        // v2: 2, 2, 0 → max 2 (первая из равных), min 0 (−b).
        Assert.Equal((2.0, 0, 0.0, 2), (env.Max[2], env.MaxSource[2], env.Min[2], env.MinSource[2]));
        // Досчёт: 3, 2, 1.
        Assert.Equal((3.0, 0, 1.0, 2), (env.Max[3], env.MaxSource[3], env.Min[3], env.MinSource[3]));
    }

    [Fact]
    public void Build_NoValues_NaNAndMinusOne()
    {
        var env = FemCsfeaCombinationEnvelope.Build([[double.NaN]], [[(0, 1.0)]]);
        Assert.True(double.IsNaN(env.Max[0]));
        Assert.Equal(-1, env.MaxSource[0]);
        Assert.Equal(-1, env.MinSource[0]);
    }

    [Fact]
    public void Build_Cancelled_Throws()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        Assert.ThrowsAny<OperationCanceledException>(() =>
            FemCsfeaCombinationEnvelope.Build([[1.0]], [[(0, 1.0)]], ct: cts.Token));
    }

    [Fact]
    public void Build_ProgressThinned_EndsWithCount()
    {
        var reports = new List<int>();
        var combinations = Enumerable.Range(0, 1000).Select(_ => (IReadOnlyList<(int, double)>)[(0, 1.0)]).ToList();
        FemCsfeaCombinationEnvelope.Build([new double[200_000]], combinations, progress: new SyncProgress(r => { lock (reports) reports.Add(r); }));
        Assert.InRange(reports.Count, 1, 101);
        Assert.Equal(1000, reports.Max());
    }

    [Fact]
    public void Build_LongVector_SameAsScalarAndRecordsNotSplit()
    {
        // Длина не кратна куску и вектору SIMD; запись из 7 величин: последняя — сумма первых трёх.
        const int records = 5000, size = 7;
        var rnd = new Random(3);
        var cases = Enumerable.Range(0, 3).Select(_ => Enumerable.Range(0, records * size).Select(_ => rnd.NextDouble() - 0.5).ToArray()).ToList();
        var combinations = new IReadOnlyList<(int, double)>[] { [(0, 1.1), (1, -0.7)], [(2, 1.4)], [(0, 1), (1, 1), (2, 1)] };
        var env = FemCsfeaCombinationEnvelope.Build(cases, combinations, (v, offset) =>
        {
            Assert.Equal(0, offset % size);
            for (int o = 0; o < v.Length; o += size) v[o + 6] = v[o] + v[o + 1] + v[o + 2];
        }, size);
        for (int i = 0; i < records * size; i += 997)
        {
            double Value(int c, int j) => combinations[c].Sum(t => t.Item2 * cases[t.Item1][j]);
            double Combo(int c) => i % size == 6 ? Value(c, i - 6) + Value(c, i - 5) + Value(c, i - 4) : Value(c, i);
            var all = Enumerable.Range(0, 3).Select(Combo).ToList();
            Assert.Equal(all.Max(), env.Max[i], 12);
            Assert.Equal(all.Min(), env.Min[i], 12);
            Assert.Equal(all.IndexOf(all.Max()), env.MaxSource[i]);
        }
    }

    /// <summary>
    /// Масштаб: «Мирная»-подобный вектор (≈ 470 тыс. величин), 8 загружений, 2000 комбинаций по 8 слагаемых — время в
    /// журнал теста (ориентир — несколько секунд на рабочем ПК; память от числа комбинаций не зависит). Ручной:
    /// OPENCS_CSFEA_ENVELOPE_TIMING=1, без переменной тест сразу выходит.
    /// </summary>
    [Fact]
    public void Build_LargeGroup_Timing()
    {
        if (Environment.GetEnvironmentVariable("OPENCS_CSFEA_ENVELOPE_TIMING") != "1") return;
        const int len = 470_000, cases = 8, count = 2000;
        var rnd = new Random(1);
        var vectors = Enumerable.Range(0, cases).Select(_ => Enumerable.Range(0, len).Select(_ => rnd.NextDouble() - 0.5).ToArray()).ToList();
        var combinations = Enumerable.Range(0, count)
            .Select(_ => (IReadOnlyList<(int, double)>)Enumerable.Range(0, cases).Select(c => (c, rnd.NextDouble() * 1.5)).ToList())
            .ToList();
        var sw = Stopwatch.StartNew();
        var env = FemCsfeaCombinationEnvelope.Build(vectors, combinations);
        output.WriteLine($"{count} комбинаций × {cases} слагаемых × {len} величин: {sw.Elapsed.TotalSeconds:0.00} с");
        Assert.Equal(count, env.Count);
    }

    sealed class SyncProgress(Action<int> report) : IProgress<int>
    {
        public void Report(int value) => report(value);
    }
}
