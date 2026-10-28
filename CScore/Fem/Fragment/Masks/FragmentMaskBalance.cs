using CScore.Planar;
using CScore.Submodel;

namespace CScore.Fem.Fragment.Masks;

/// <summary>Равнодействующие масок и равновесие масок с нагрузками полосы (спека разд. 4.4).</summary>
public static class FragmentMaskBalance
{
    /// <summary>
    /// Равнодействующая действия маски сечения: сила и момент относительно <paramref name="origin"/>. Кромка —
    /// интеграл кусочно-линейной функции по сэмплам внутри каждого отрезка (Симпсон для момента силы — точен); конец
    /// стержня — сила и момент сэмпла. null — нет ни одного сэмпла с силой.
    /// </summary>
    public static (PlanarVector3 F, PlanarVector3 M)? Resultant(FragmentMaskCutInput cut,
        IReadOnlyList<FragmentMaskSample> samples, PlanarVector3 origin)
    {
        if (samples.All(s => s.F == null)) return null;
        if (cut.Cut.Kind == FragmentCutKind.End)
        {
            var f = samples[0].F;
            if (f == null || cut.EndPoint is not { } p) return null;
            return (f.Force, f.Moment + (p - origin).Cross(f.Force));
        }
        var sumF = PlanarVector3.Zero;
        var sumM = PlanarVector3.Zero;
        double offset = 0;
        int k = 0;
        foreach (var (a, b) in cut.Cut.Segments)
        {
            double len = (b - a).Length;
            var axis = (b - a).Normalize();
            var segment = new List<FragmentMaskSample>();
            while (k < samples.Count && samples[k].S <= offset + len + 1e-9)
            {
                segment.Add(samples[k]);
                k++;
                if (segment.Count > 1 && samples[k - 1].S >= offset + len - 1e-9) break;
            }
            for (int i = 0; i + 1 < segment.Count; i++)
            {
                if (segment[i].F is not { } f0 || segment[i + 1].F is not { } f1) continue;
                double s0 = segment[i].S - offset, s1 = segment[i + 1].S - offset, h = s1 - s0;
                PlanarVector3 X(double s) => a + axis * s - origin;
                var fm = (f0.Force + f1.Force) * 0.5;
                sumF += fm * h;
                sumM += (f0.Moment + f1.Moment) * (h / 2);
                sumM += (X(s0).Cross(f0.Force) + X((s0 + s1) / 2).Cross(fm) * 4 + X(s1).Cross(f1.Force)) * (h / 6);
            }
            offset += len;
        }
        return (sumF, sumM);
    }

    /// <summary>Допуск равновесия масок с нагрузками полосы (спека 4.4).</summary>
    public const double BalanceTolerance = 0.02;

    /// <summary>
    /// Равновесие по ключам: ΣF, ΣM масок всех сечений + нагрузки полосы (<paramref name="loads"/>, относительно
    /// <paramref name="origin"/>) ≈ 0. Расхождение больше <see cref="BalanceTolerance"/> — предупреждение
    /// <c>fragment_mask_balance</c> с разбивкой по сечениям. Подгонки нет.
    /// </summary>
    public static List<FemValidationDiagnostic> Check(IReadOnlyList<FragmentMaskCutInput> cuts,
        IReadOnlyList<FragmentCutMask> masks, IReadOnlyDictionary<FragmentMaskKey, (PlanarVector3 F, PlanarVector3 M)> loads,
        PlanarVector3 origin)
    {
        var result = new List<FemValidationDiagnostic>();
        foreach (var group in masks.GroupBy(m => m.Key))
        {
            var sumF = PlanarVector3.Zero;
            var sumM = PlanarVector3.Zero;
            double absF = 0, radius = 0;
            var parts = new List<string>();
            foreach (var m in group)
            {
                var cut = cuts[m.CutIndex];
                if (Resultant(cut, m.Samples, origin) is not { } r) continue;
                var (f, mo) = r;
                sumF += f;
                sumM += mo;
                absF += f.Length;
                var at = cut.EndPoint ?? cut.Cut.Segments[0].P;
                radius = Math.Max(radius, (at - origin).Length);
                parts.Add(string.Create(System.Globalization.CultureInfo.CurrentCulture,
                    $"{cut.Cut.MemberTag} {(cut.Cut.Side == FragmentCutSide.Bottom ? "низ" : "верх")}: Fz {f.Z / 1e3:0.##} кН"));
            }
            var (lf, lm) = loads.TryGetValue(group.Key, out var l) ? l : (PlanarVector3.Zero, PlanarVector3.Zero);
            double scaleF = lf.Length > 0 ? lf.Length : absF;
            if (scaleF <= 1e-9) continue;
            double dF = (sumF + lf).Length / scaleF;
            double dM = (sumM + lm).Length / (lm.Length + scaleF * Math.Max(radius, 1e-9));
            if (dF <= BalanceTolerance && dM <= BalanceTolerance) continue;
            string key = group.Key.Kind == FragmentMaskKeyKind.LoadCase
                ? $"загружение #{group.Key.LoadCaseId}" : $"сочетание {group.Key.StageExpressionJson}";
            result.Add(new FemValidationDiagnostic("fragment_mask_balance", string.Create(
                System.Globalization.CultureInfo.CurrentCulture,
                $"{key}: маски сечений не уравновешивают нагрузки полосы — по силе {dF:P1}, по моменту {dM:P1} ({string.Join("; ", parts)})."),
                false));
        }
        return result;
    }
}
