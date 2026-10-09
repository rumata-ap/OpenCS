using System.Diagnostics;
using CSfea.Sparse;
using Xunit.Abstractions;

namespace CSfea.Tests;

/// <summary>
/// Ручной пробник суперузлового Холецкого на портрете большой схемы. Портрет Kff пишет
/// <c>OpenCS.Tests/CsfeaSizeProbeManualTests</c> (int32: n, colPtr[n+1], rowIdx[nnz]); путь — в CSFEA_KFF_BIN
/// (несколько через «;»). Без переменной тест сразу выходит.
/// </summary>
public class SupernodalProbeManualTests(ITestOutputHelper output)
{
    [Fact]
    public void AnalyzeDumpedPatterns()
    {
        string? paths = Environment.GetEnvironmentVariable("CSFEA_KFF_BIN");
        if (string.IsNullOrWhiteSpace(paths)) return;
        foreach (string path in paths.Split(';', StringSplitOptions.RemoveEmptyEntries))
        {
            var a = LoadPattern(path);
            output.WriteLine($"{Path.GetFileName(path)}: n = {a.Cols}, nnz(A) = {a.Nnz}");
            var sw = Stopwatch.StartNew();
            var an = SupernodalAnalysis.Analyze(a);
            output.WriteLine($"  анализ {sw.Elapsed.TotalSeconds:0.0} с; nnz(L) {an.NnzL:E3}, хранится {an.StoredL:E3} " +
                             $"({an.StoredL * 8 / 1048576.0:0} МБ, +{(an.StoredL / (double)an.NnzL - 1) * 100:0}% нулей), " +
                             $"флоп {an.Flops:E3}");
            output.WriteLine($"  суперузлов {an.SupernodeCount} (фундаментальных {an.FundamentalSupernodeCount}), " +
                             $"наиб. {an.MaxSupernodeCols} столбцов × {an.MaxSupernodeRows} строк, " +
                             $"обновлений {an.UpdSuper.Length}");
            // Распределение работы по размеру суперузла: где окажется основная часть флопов.
            var hist = new double[6];
            for (int s = 0; s < an.SupernodeCount; s++)
            {
                int k = an.SuperStart[s + 1] - an.SuperStart[s];
                double work = 0;
                for (int j = an.SuperStart[s]; j < an.SuperStart[s + 1]; j++) work += (double)an.ColCount[j] * an.ColCount[j];
                hist[k switch { < 8 => 0, < 32 => 1, < 128 => 2, < 512 => 3, < 2048 => 4, _ => 5 }] += work;
            }
            output.WriteLine("  флоп по ширине суперузла (<8, <32, <128, <512, <2048, ≥2048): " +
                             string.Join(", ", hist.Select(h => $"{h / an.Flops * 100:0.0}%")));
            if (Environment.GetEnvironmentVariable("CSFEA_PROBE_FACTORIZE") == "1") Factorize(a);
            output.WriteLine($"  пик рабочего набора {Process.GetCurrentProcess().PeakWorkingSet64 / 1048576.0:0} МБ");
        }
    }

    // Численная факторизация на синтетических значениях (SPD диагональным преобладанием) и невязка решения;
    // CSFEA_PROBE_OLD=1 — то же up-looking решателем для сравнения времени.
    void Factorize(CscMatrix pattern)
    {
        var a = SyntheticSpd(pattern);
        var b = Enumerable.Range(0, a.Cols).Select(i => Math.Sin(0.001 * i) + 1.0).ToArray();
        var sw = Stopwatch.StartNew();
        var sn = new SupernodalCholeskySolver();
        sn.AnalyzePattern(a);
        double tAn = sw.Elapsed.TotalSeconds;
        sn.Factorize(a);
        double tF = sw.Elapsed.TotalSeconds - tAn;
        var x = sn.Solve(b);
        double tS = sw.Elapsed.TotalSeconds - tAn - tF;
        output.WriteLine($"  суперузловой: анализ {tAn:0.0} с, факторизация {tF:0.0} с ({sn.Analysis!.Flops / tF / 1e9:0.0} GFLOPS), " +
                         $"решение {tS:0.00} с; SPD {sn.LastFactorizationSpd}, невязка {Residual(a, x, b):E2}");
        if (Environment.GetEnvironmentVariable("CSFEA_PROBE_X") is { Length: > 0 } xPath)
            File.WriteAllBytes(xPath, System.Runtime.InteropServices.MemoryMarshal.AsBytes(x.AsSpan()).ToArray());
        if (Environment.GetEnvironmentVariable("CSFEA_PROBE_OLD") != "1") return;
        sw.Restart();
        var old = new SparseCholeskySolver();
        old.AnalyzePattern(a);
        tAn = sw.Elapsed.TotalSeconds;
        old.Factorize(a);
        tF = sw.Elapsed.TotalSeconds - tAn;
        var xo = old.Solve(b);
        double d = 0, nx = 0;
        for (int i = 0; i < x.Length; i++) { d = Math.Max(d, Math.Abs(x[i] - xo[i])); nx = Math.Max(nx, Math.Abs(xo[i])); }
        output.WriteLine($"  up-looking: анализ {tAn:0.0} с, факторизация {tF:0.0} с; max|Δx|/max|x| = {d / nx:E2}");
    }

    // Относительная невязка ‖A·x − b‖∞ / ‖b‖∞.
    static double Residual(CscMatrix a, double[] x, double[] b)
    {
        var ax = a.Multiply(x);
        double r = 0;
        for (int i = 0; i < b.Length; i++) r = Math.Max(r, Math.Abs(ax[i] - b[i]));
        return r / b.Max(Math.Abs);
    }

    // Симметричные значения по паре (min, max) индексов (односторонние элементы — нули) — какой бы элемент пары ни попал в верхний треугольник после
    // перестановки, значение одно; диагональное преобладание по полной строке → SPD.
    static CscMatrix SyntheticSpd(CscMatrix pattern)
    {
        int n = pattern.Cols;
        var v = new double[pattern.Nnz];
        var diag = new double[n];
        var sorted = (int[])pattern.RowIdx.Clone();
        for (int k = 0; k < n; k++) Array.Sort(sorted, pattern.ColPtr[k], pattern.ColPtr[k + 1] - pattern.ColPtr[k]);
        for (int k = 0; k < n; k++)
            for (int p = pattern.ColPtr[k]; p < pattern.ColPtr[k + 1]; p++)
            {
                int i = pattern.RowIdx[p];
                if (i == k) continue;
                // Элемент без симметричной пары — ноль (Холецкий видит только один из пары).
                if (Array.BinarySearch(sorted, pattern.ColPtr[i], pattern.ColPtr[i + 1] - pattern.ColPtr[i], k) < 0) continue;
                int lo = Math.Min(i, k), hi = Math.Max(i, k);
                double h = ((uint)(lo * 73856093 ^ hi * 19349663) % 1000) / 1000.0 - 0.5;
                v[p] = h;
                diag[k] += Math.Abs(h);
                diag[i] += Math.Abs(h);
            }
        for (int k = 0; k < n; k++)
            for (int p = pattern.ColPtr[k]; p < pattern.ColPtr[k + 1]; p++)
                if (pattern.RowIdx[p] == k) v[p] = diag[k] + 1.0;
        return new CscMatrix(n, n, pattern.ColPtr, pattern.RowIdx, v);
    }

    /// <summary>Портрет из файла пробника; значения — единицы (для символики не нужны).</summary>
    internal static CscMatrix LoadPattern(string path)
    {
        using var br = new BinaryReader(File.OpenRead(path));
        int n = br.ReadInt32();
        var colPtr = new int[n + 1];
        for (int i = 0; i <= n; i++) colPtr[i] = br.ReadInt32();
        var rowIdx = new int[colPtr[n]];
        for (int i = 0; i < rowIdx.Length; i++) rowIdx[i] = br.ReadInt32();
        var values = new double[rowIdx.Length];
        Array.Fill(values, 1.0);
        return new CscMatrix(n, n, colPtr, rowIdx, values);
    }
}
