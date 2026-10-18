using CScore;
using CSfea.CScoreBridge;
using CSfea.CScoreBridge.Structural;

namespace OpenCS.Services;

/// <summary>
/// Точка детальной эпюры стержня: доля длины КЭ <see cref="T"/> (0 — узел i, 1 — узел j), усилия сечения в осях сечения
/// CScore (кН, кН·м: N, Mx, My) по равновесию КЭ; <see cref="Recorded"/> — точка Лобатто (ξ = 0, ½, 1) с записанной
/// плоскостью деформаций, иначе сечение досчитано по усилиям. <see cref="State"/> — null, если досчёт не сошёлся.
/// </summary>
public sealed record FemCsfeaBarDetailPoint(double T, double N, double Mx, double My, bool Recorded, FemCsfeaBarSectionPoint? State)
{
    /// <summary>Номер точки Лобатто (0 — i, 1 — ½, 2 — j); null — промежуточная точка.</summary>
    public int? LobattoPoint => !Recorded ? null : T < 0.25 ? 0 : T > 0.75 ? 2 : 1;
}

/// <summary>
/// Детальные эпюры вдоль стержня по результату CSfea (спека НДС сечений, п. 2.4): НДС сечения в n равноотстоящих
/// точках КЭ. Усилия между концами — по равновесию КЭ: пролётная нагрузка принимается равномерной и восстанавливается
/// по сумме сил концов (q = −(f_i + f_j)/L), моменты от левого конца исправляются линейно по длине до записанных
/// моментов конца j (сосредоточенные пролётные моменты и неравномерная нагрузка не теряют замыкания на концах).
/// В точках Лобатто ξ = 0, ½, 1 сечение — по записанной плоскости деформаций (<see cref="FemCsfeaBarSectionState"/>,
/// трещины — с флагом КЭ), между ними — досчёт <see cref="StrainSolver"/> на тех же диаграммах без памяти трещин:
/// сначала с работой растянутого бетона (если разрешена постановкой), при трещине по полученной плоскости или
/// несходимости — без неё, с ψs по εs,crc при соотношении усилий точки (постановка с ψs). Начальное приближение —
/// линейная интерполяция записанных плоскостей соседних точек Лобатто.
/// </summary>
public static class FemCsfeaBarDetail
{
    /// <summary>Число точек по умолчанию.</summary>
    public const int DefaultPoints = 11;

    /// <summary>
    /// Усилия сечения в точке <paramref name="t"/> (0…1) КЭ длиной <paramref name="length"/>, м, с индексом
    /// <paramref name="beamIndex"/> полей: Н и Н·м в местных осях (N, My, Mz).
    /// </summary>
    public static (double N, double My, double Mz) Forces(RcSecantStepFields f, int beamIndex, double length, double t)
    {
        int o = beamIndex * RcSecantStepFields.BeamForceComponents;
        var b = f.BeamForces;
        double x = t * length;
        // Равномерная нагрузка из равновесия сил: f_i + f_j + q·L = 0.
        double qx = -(b[o] + b[o + 6]) / length, qy = -(b[o + 1] + b[o + 7]) / length, qz = -(b[o + 2] + b[o + 8]) / length;
        // Равновесие левой части [0, x]: силы конца i, нагрузка и усилие сечения.
        (double My, double Mz) Left(double s) =>
            (-b[o + 4] - s * b[o + 2] - s * s / 2 * qz, -b[o + 5] + s * b[o + 1] + s * s / 2 * qy);
        var (myL, mzL) = Left(x);
        var (myEnd, mzEnd) = Left(length);
        double n = -b[o] - x * qx;
        return (n, myL + t * (b[o + 10] - myEnd), mzL + t * (b[o + 11] - mzEnd));
    }

    /// <summary>
    /// Детальная эпюра стержня с индексом <paramref name="beamIndex"/> полей (нужны деформации стержней —
    /// <see cref="RcSecantStepFields.HasBeamStrains"/>): <paramref name="points"/> точек (нечётное, не меньше 3 —
    /// иначе округляется вверх до нечётного), <paramref name="section"/> — сечение CScore с диаграммами вида
    /// <paramref name="calc"/> (фибры мутируются — своё на поток), <paramref name="tension"/> и <paramref name="psi"/> —
    /// работа растянутого бетона и ψs постановки.
    /// </summary>
    public static IReadOnlyList<FemCsfeaBarDetailPoint> Compute(CrossSection section, CalcType calc, bool tension, bool psi,
        bool yAlongLocalY, RcSecantStepFields f, int beamIndex, double length, int points = DefaultPoints)
    {
        ArgumentNullException.ThrowIfNull(section);
        ArgumentNullException.ThrowIfNull(f);
        if (!f.HasBeamStrains) throw new InvalidOperationException("В полях шага нет деформаций стержней.");
        if (length <= 0) throw new ArgumentOutOfRangeException(nameof(length));
        points = Math.Max(3, points);
        if (points % 2 == 0) points++;
        bool flag = beamIndex < f.BeamFlags.Length && (f.BeamFlags[beamIndex] & RcSecantStepFields.Cracked) != 0;
        int so = beamIndex * RcSecantStepFields.BeamStrainComponents;
        var planes = Enumerable.Range(0, 3)
            .Select(p => FemCsfeaBarSectionState.Plane(f.BeamStrains[so + 3 * p], f.BeamStrains[so + 3 * p + 1], f.BeamStrains[so + 3 * p + 2], yAlongLocalY))
            .ToArray();

        var result = new List<FemCsfeaBarDetailPoint>(points);
        int half = (points - 1) / 2;
        for (int k = 0; k < points; k++)
        {
            double t = (double)k / (points - 1);
            var forces = FemCsfeaBarSectionState.ToSection(Forces(f, beamIndex, length, t), yAlongLocalY);
            FemCsfeaBarSectionPoint? state;
            bool recorded = k == 0 || k == half || k == points - 1;
            if (recorded)
                state = FemCsfeaBarSectionState.Compute(section, calc, tension, planes[k == 0 ? 0 : k == half ? 1 : 2], forces, psi, flag);
            else
            {
                // Приближение — между соседними записанными плоскостями.
                var (a, b, w) = k < half ? (planes[0], planes[1], (double)k / half) : (planes[1], planes[2], (double)(k - half) / half);
                var seed = new Kurvature { e0 = a.e0 + w * (b.e0 - a.e0), ky = a.ky + w * (b.ky - a.ky), kz = a.kz + w * (b.kz - a.kz) };
                state = Solve(section, calc, tension, psi, forces, seed);
            }
            result.Add(new FemCsfeaBarDetailPoint(t, forces.N, forces.Mx, forces.My, recorded, state));
        }
        return result;
    }

    /// <summary>
    /// Сечение по усилиям (кН, кН·м, оси сечения) без памяти трещин: с растянутым бетоном, при трещине или
    /// несходимости — без него (с ψs); null — не сошлось.
    /// </summary>
    static FemCsfeaBarSectionPoint? Solve(CrossSection s, CalcType calc, bool tension, bool psi,
        (double N, double Mx, double My) forces, Kurvature seed)
    {
        // Допуск решателя (кН, кН·м) — по величине усилий: штатные 0,5 грубы у опор.
        double tol = 1e-3 + 1e-4 * (Math.Abs(forces.N) + Math.Abs(forces.Mx) + Math.Abs(forces.My));
        if (tension)
        {
            var solver = new StrainSolver(s, calc, ten: true, ca: true, tol: tol);
            var plane = solver.Solve(forces.N, forces.Mx, forces.My, seed);
            if (solver.Converged && !SecantCrossSectionBuilder.IsCracked(s, plane, calc))
                return FemCsfeaBarSectionState.Compute(s, calc, tension, plane, forces, psi);
        }
        var epsCrc = psi ? SecantCrossSectionBuilder.EpsCrcAtCracking(s, calc, calc, forces.N, forces.Mx, forces.My) : null;
        var cracked = new StrainSolver(s, calc, ten: false, ca: true, tol: tol,
            evaluate: epsCrc == null ? null : k => Curvature8232.ApplyPsiCorrection(s, k, s.Integral(k, calc, false, true), epsCrc, calc));
        var p = cracked.Solve(forces.N, forces.Mx, forces.My, seed);
        if (!cracked.Converged) return null;
        // Флаг трещины — из двух состояний окна выбирается ближнее к усилиям: трещина и ψs, с которыми найдена плоскость.
        return FemCsfeaBarSectionState.Compute(s, calc, tension, p, forces, psi, elementCracked: true);
    }
}
