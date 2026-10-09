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
            output.WriteLine($"  пик рабочего набора {Process.GetCurrentProcess().PeakWorkingSet64 / 1048576.0:0} МБ");
        }
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
