using CScore.Planar;
using CScore.Submodel;

namespace CScore.Fem.Fragment.Masks;

/// <summary>
/// Поправка маски кромки к заданной равнодействующей (путь МНК по центрам КЭ): МНК патча сглаживает пики усилий у
/// краёв кромки и проёмов и теряет часть равнодействующей («Мирная», стена 28 у проёма, ветер: 37,6 кН против
/// 40,8 кН прямой суммы «центр КЭ × длина хорды» и 41,1 кН по узловым силам — ЛИРА с усилиями в узлах КЭ даёт
/// 40,7 кН). К силам сэмплов добавляется наименьшая поправка δf(r) = α + β × (r − r₀), к моментам —
/// δm = P(β)/L² (P — проекция на плоскость пластины, моментов «вокруг нормали» у пластины нет): форма распределения
/// МНК сохраняется, ΣF и ΣM маски (<see cref="FragmentMaskBalance.Resultant"/>) равны целевым.
/// </summary>
public static class FragmentMaskResultantCorrection
{
    /// <summary>
    /// Сэмплы <paramref name="samples"/> кромки <paramref name="cut"/> с равнодействующей <paramref name="targetF"/>,
    /// <paramref name="targetM"/> (момент — относительно начала координат); нормаль пластины — <paramref name="normal"/>.
    /// Сэмплы без сил не меняются; система вырождена — исходные сэмплы.
    /// </summary>
    public static IReadOnlyList<FragmentMaskSample> Correct(FragmentMaskCutInput cut, IReadOnlyList<FragmentMaskSample> samples,
        PlanarVector3 targetF, PlanarVector3 targetM, PlanarVector3 normal)
    {
        if (FragmentMaskBalance.Resultant(cut, samples, PlanarVector3.Zero) is not { } now) return samples;
        var positions = Positions(cut, samples);
        var loaded = Enumerable.Range(0, samples.Count).Where(i => samples[i].F != null).ToList();
        if (loaded.Count < 2) return samples;
        var r0 = loaded.Aggregate(PlanarVector3.Zero, (a, i) => a + positions[i]) * (1.0 / loaded.Count);
        double length = cut.Cut.Segments.Sum(s => (s.Q - s.P).Length);
        double kappa = Math.Max(length * length, 1e-6);
        var n = normal.Normalize();
        PlanarVector3[] axes = [new(1, 0, 0), new(0, 1, 0), new(0, 0, 1)];

        Dof6 Mode(int k, int i)
        {
            if (k < 3) return Dof6.FromParts(axes[k], PlanarVector3.Zero);
            var e = axes[k - 3];
            return Dof6.FromParts(e.Cross(positions[i] - r0), (e - n * n.Dot(e)) * (1.0 / kappa));
        }

        // Столбец k — равнодействующая (ΣF; ΣM относительно r₀) моды k.
        var a = new double[6, 6];
        for (int k = 0; k < 6; k++)
        {
            var mode = samples.Select((s, i) => s with { F = s.F == null ? null : Mode(k, i) }).ToList();
            if (FragmentMaskBalance.Resultant(cut, mode, r0) is not { } r) return samples;
            double[] col = [r.F.X, r.F.Y, r.F.Z, r.M.X, r.M.Y, r.M.Z];
            for (int row = 0; row < 6; row++) a[row, k] = col[row];
        }
        var dF = targetF - now.F;
        // ΣM относительно r₀: M₀ = M − r₀ × F.
        var dM = targetM - r0.Cross(targetF) - (now.M - r0.Cross(now.F));
        if (Solve(a, [dF.X, dF.Y, dF.Z, dM.X, dM.Y, dM.Z]) is not { } c) return samples;

        var result = new List<FragmentMaskSample>(samples.Count);
        for (int i = 0; i < samples.Count; i++)
        {
            if (samples[i].F is not { } f) { result.Add(samples[i]); continue; }
            double x = f.X, y = f.Y, z = f.Z, rx = f.Rx, ry = f.Ry, rz = f.Rz;
            for (int k = 0; k < 6; k++)
            {
                var m = Mode(k, i);
                x += c[k] * m.X; y += c[k] * m.Y; z += c[k] * m.Z;
                rx += c[k] * m.Rx; ry += c[k] * m.Ry; rz += c[k] * m.Rz;
            }
            result.Add(samples[i] with { F = new Dof6(x, y, z, rx, ry, rz) });
        }
        return result;
    }

    /// <summary>Точки сэмплов: сквозная координата s по отрезкам кромки (как в <see cref="FragmentMaskBalance.Resultant"/>).</summary>
    static PlanarVector3[] Positions(FragmentMaskCutInput cut, IReadOnlyList<FragmentMaskSample> samples)
    {
        var result = new PlanarVector3[samples.Count];
        var bySegment = FragmentMaskDistribution.SplitBySegments(cut.Cut.Segments, samples);
        double offset = 0;
        for (int j = 0; j < cut.Cut.Segments.Count; j++)
        {
            var (p, q) = cut.Cut.Segments[j];
            double len = (q - p).Length;
            var axis = len > 0 ? (q - p) * (1.0 / len) : PlanarVector3.Zero;
            foreach (var s in bySegment[j])
            {
                int i = IndexOf(samples, s);
                if (i >= 0) result[i] = p + axis * (s.S - offset);
            }
            offset += len;
        }
        return result;
    }

    static int IndexOf(IReadOnlyList<FragmentMaskSample> samples, FragmentMaskSample s)
    {
        for (int i = 0; i < samples.Count; i++)
            if (ReferenceEquals(samples[i], s)) return i;
        return -1;
    }

    /// <summary>Гаусс с выбором главного элемента; вырожденная система — null.</summary>
    static double[]? Solve(double[,] a, double[] b)
    {
        int n = b.Length;
        var m = (double[,])a.Clone();
        var x = (double[])b.Clone();
        double scale = 0;
        foreach (double v in m) scale = Math.Max(scale, Math.Abs(v));
        if (scale == 0) return null;
        for (int col = 0; col < n; col++)
        {
            int piv = col;
            for (int r = col + 1; r < n; r++)
                if (Math.Abs(m[r, col]) > Math.Abs(m[piv, col])) piv = r;
            if (Math.Abs(m[piv, col]) < 1e-12 * scale) return null;
            if (piv != col)
            {
                for (int k = 0; k < n; k++) (m[col, k], m[piv, k]) = (m[piv, k], m[col, k]);
                (x[col], x[piv]) = (x[piv], x[col]);
            }
            for (int r = col + 1; r < n; r++)
            {
                double f = m[r, col] / m[col, col];
                if (f == 0) continue;
                for (int k = col; k < n; k++) m[r, k] -= f * m[col, k];
                x[r] -= f * x[col];
            }
        }
        for (int r = n - 1; r >= 0; r--)
        {
            double s = x[r];
            for (int k = r + 1; k < n; k++) s -= m[r, k] * x[k];
            x[r] = s / m[r, r];
        }
        return x;
    }
}
