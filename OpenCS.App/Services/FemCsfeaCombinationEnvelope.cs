using System.Numerics;

namespace OpenCS.Services;

/// <summary>
/// Огибающая величин по группе комбинаций: наименьшее и наибольшее значение каждой величины и номер комбинации
/// (в списке группы), которая его дала; −1 и NaN — у величины нет значений ни в одной комбинации.
/// </summary>
public sealed class FemCsfeaEnvelope
{
    public required double[] Min { get; init; }
    public required double[] Max { get; init; }
    public required int[] MinSource { get; init; }
    public required int[] MaxSource { get; init; }

    /// <summary>Сколько комбинаций вошло (= число комбинаций группы).</summary>
    public int Count { get; init; }
}

/// <summary>Досчёт нелинейных величин куска вектора комбинации после суммы: <paramref name="offset"/> — индекс начала куска.</summary>
public delegate void FemCsfeaEnvelopeDerive(Span<double> values, int offset);

/// <summary>
/// Огибающая группы комбинаций линейного результата без перерасчёта (спека 2026-10-16, раздел 3): величины загружений
/// (векторы одной длины) считаются один раз, комбинация — Σ k·величины, по каждой величине — min/max и комбинация-
/// источник. Время ∝ комбинации × слагаемые × длина вектора, память от числа комбинаций не зависит. Параллельно по
/// кускам вектора, внутри куска — все комбинации подряд (куски векторов загружений остаются в кеше), сумма векторная.
/// </summary>
public static class FemCsfeaCombinationEnvelope
{
    const int ChunkTarget = 1 << 14;

    /// <param name="cases">Величины загружений — векторы одной длины (NaN — величины у загружения нет).</param>
    /// <param name="combinations">Слагаемые комбинаций: индекс вектора в <paramref name="cases"/> и коэффициент.</param>
    /// <param name="derive">Досчёт нелинейных величин куска (|u|) после суммы; null — всё линейно.</param>
    /// <param name="recordSize">Длина записи, которую <paramref name="derive"/> читает целиком: куски не режут записи
    /// (начинаются с индекса, кратного ей).</param>
    /// <param name="progress">Сколько комбинаций «пройдено» (доля готовых кусков × число комбинаций), не чаще ~1 %.</param>
    public static FemCsfeaEnvelope Build(IReadOnlyList<double[]> cases,
        IReadOnlyList<IReadOnlyList<(int Case, double Factor)>> combinations, FemCsfeaEnvelopeDerive? derive = null,
        int recordSize = 1, IProgress<int>? progress = null, CancellationToken ct = default)
    {
        int len = cases.Count > 0 ? cases[0].Length : 0;
        if (cases.Any(c => c.Length != len)) throw new ArgumentException("Векторы величин загружений разной длины.", nameof(cases));
        ArgumentOutOfRangeException.ThrowIfLessThan(recordSize, 1);
        var min = new double[len];
        var max = new double[len];
        var minSrc = new int[len];
        var maxSrc = new int[len];
        Array.Fill(min, double.NaN);
        Array.Fill(max, double.NaN);
        Array.Fill(minSrc, -1);
        Array.Fill(maxSrc, -1);
        var terms = combinations.Select(c => c.Select(t => (Values: cases[t.Case], t.Factor)).ToArray()).ToArray();

        int chunk = Math.Max(1, ChunkTarget / recordSize) * recordSize;
        int chunks = (len + chunk - 1) / chunk;
        int done = 0, reported = 0;
        Parallel.For(0, chunks, () => new double[chunk], (k, _, sum) =>
        {
            int from = k * chunk, n = Math.Min(len, from + chunk) - from;
            var s = sum.AsSpan(0, n);
            for (int c = 0; c < terms.Length; c++)
            {
                if (ct.IsCancellationRequested) return sum;
                s.Clear();
                foreach (var (values, factor) in terms[c]) AddScaled(s, values.AsSpan(from, n), factor);
                derive?.Invoke(s, from);
                for (int i = 0; i < n; i++)
                {
                    double v = s[i];
                    if (double.IsNaN(v)) continue;
                    int g = from + i;
                    if (minSrc[g] < 0 || v < min[g]) { min[g] = v; minSrc[g] = c; }
                    if (maxSrc[g] < 0 || v > max[g]) { max[g] = v; maxSrc[g] = c; }
                }
            }
            if (progress != null)
            {
                int d = Interlocked.Increment(ref done);
                int combos = (int)((long)d * terms.Length / chunks);
                int last = Volatile.Read(ref reported);
                if ((d == chunks || combos - last >= Math.Max(1, terms.Length / 100))
                    && Interlocked.CompareExchange(ref reported, combos, last) == last)
                    progress.Report(combos);
            }
            return sum;
        }, _ => { });
        ct.ThrowIfCancellationRequested();
        return new FemCsfeaEnvelope { Min = min, Max = max, MinSource = minSrc, MaxSource = maxSrc, Count = combinations.Count };
    }

    /// <summary>acc += k·x (векторно).</summary>
    static void AddScaled(Span<double> acc, ReadOnlySpan<double> x, double k)
    {
        int i = 0;
        if (Vector.IsHardwareAccelerated && acc.Length >= Vector<double>.Count)
        {
            var kv = new Vector<double>(k);
            var a = System.Runtime.InteropServices.MemoryMarshal.Cast<double, Vector<double>>(acc);
            var b = System.Runtime.InteropServices.MemoryMarshal.Cast<double, Vector<double>>(x);
            for (int j = 0; j < a.Length; j++) a[j] += kv * b[j];
            i = a.Length * Vector<double>.Count;
        }
        for (; i < acc.Length; i++) acc[i] += k * x[i];
    }
}
