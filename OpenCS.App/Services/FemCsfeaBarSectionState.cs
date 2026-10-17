using CScore;
using CSfea.CScoreBridge;
using CSfea.CScoreBridge.Structural;

namespace OpenCS.Services;

/// <summary>Арматурная фибра в состоянии сечения: координаты (м), ε, σ (МПа).</summary>
public sealed record FemCsfeaRebarFiber(int Num, double X, double Y, double Eps, double SigmaMpa);

/// <summary>
/// Состояние фибрового сечения стержня в точке ξ = 0, ½, 1 по записанным деформациям (спека НДС сечений, п. 2.3).
/// <see cref="Plane"/> — плоскость деформаций в осях сечения CScore (ε = e0 + ky·Y + kz·X); усилия — в осях сечения
/// (кН, кН·м: N, Mx, My) по интегралу фибр и записанные усилия точки, переведённые в те же оси.
/// <see cref="CompressedHeight"/> — высота сжатой зоны бетона по нормали к нейтральной оси, м (вся высота — сечение сжато
/// целиком; 0 — сжатия нет). Крайние значения: <see cref="EpsB"/>, <see cref="SigmaB"/> — наибольшее сжатие бетона (ε,
/// МПа; 0 — нет), <see cref="EpsS"/>, <see cref="SigmaS"/> — наибольшее растяжение арматуры.
/// </summary>
public sealed record FemCsfeaBarSectionPoint(
    Kurvature Plane, double N, double Mx, double My, double RecordedN, double RecordedMx, double RecordedMy,
    double CompressedHeight, double EpsB, double SigmaB, double EpsS, double SigmaS, double EpsMin, double EpsMax,
    IReadOnlyList<FemCsfeaRebarFiber> Rebar)
{
    /// <summary>По показанной плоскости в точке есть трещина.</summary>
    public bool Cracked { get; init; }

    /// <summary>Растянутый бетон работает (разрешено постановкой и трещины нет) — так строятся эпюры окна.</summary>
    public bool Tension { get; init; }

    /// <summary>Наибольший размер сечения по осям X, Y, м — приводит продольную силу к моменту в мере расхождения.</summary>
    public double Size { get; init; } = 1.0;

    /// <summary>
    /// Расхождение усилий интеграла фибр с записанными: max(|ΔN|·h, |ΔMx|, |ΔMy|) / max(|N|·h, |Mx|, |My|) по записанным,
    /// h — <see cref="Size"/>; масштаб снизу — 0,1 кН·м.
    /// </summary>
    public double Discrepancy =>
        Math.Max(Math.Abs(N - RecordedN) * Size, Math.Max(Math.Abs(Mx - RecordedMx), Math.Abs(My - RecordedMy)))
        / Math.Max(0.1, Math.Max(Math.Abs(RecordedN) * Size, Math.Max(Math.Abs(RecordedMx), Math.Abs(RecordedMy))));
}

/// <summary>
/// Фибровое сечение стержня по записанным в полях шага CSfea деформациям (ε₀, κy, κz в осях КЭ) — без памяти трещин:
/// трещина в точке — если она есть по показанной плоскости (<see cref="SecantCrossSectionBuilder.IsCracked"/>), тогда
/// растянутый бетон точки не работает, как в расчёте; ψs не применяется (εs,crc не записываются). При монотонном
/// нагружении без ψs совпадает с расчётом. Оси сечения в осях КЭ — как у <see cref="CrossSectionSecantBeamState"/>: по умолчанию
/// ε = ε₀ + κy·Y + κz·X, при <c>yAlongLocalY</c> — ε = ε₀ − κz·Y + κy·X.
/// </summary>
public static class FemCsfeaBarSectionState
{
    /// <summary>Плоскость деформаций сечения CScore по деформациям КЭ.</summary>
    public static Kurvature Plane(double eps0, double kappaY, double kappaZ, bool yAlongLocalY) => yAlongLocalY
        ? new Kurvature { e0 = eps0, ky = -kappaZ, kz = kappaY }
        : new Kurvature { e0 = eps0, ky = kappaY, kz = kappaZ };

    /// <summary>
    /// Усилия сечения в точке <paramref name="point"/> (0 — i, 1 — ½, 2 — j) по силам концов КЭ, Н и Н·м в осях КЭ
    /// (N, My, Mz): в начале — силы конца i с обратным знаком, в конце — силы конца j, в середине — среднее (без
    /// пролётной нагрузки КЭ).
    /// </summary>
    public static (double N, double My, double Mz) ElementForces(RcSecantStepFields f, int beamIndex, int point)
    {
        int o = beamIndex * RcSecantStepFields.BeamForceComponents;
        var b = f.BeamForces;
        (double N, double My, double Mz) i = (-b[o], -b[o + 4], -b[o + 5]), j = (b[o + 6], b[o + 10], b[o + 11]);
        return point switch
        {
            0 => i,
            2 => j,
            _ => ((i.N + j.N) / 2, (i.My + j.My) / 2, (i.Mz + j.Mz) / 2),
        };
    }

    /// <summary>Усилия КЭ (Н, Н·м) в осях сечения CScore (кН, кН·м: N, Mx, My).</summary>
    public static (double N, double Mx, double My) ToSection((double N, double My, double Mz) e, bool yAlongLocalY) => yAlongLocalY
        ? (e.N / 1e3, -e.Mz / 1e3, e.My / 1e3)
        : (e.N / 1e3, e.My / 1e3, e.Mz / 1e3);

    /// <summary>
    /// Состояние сечения в точке <paramref name="point"/> стержня с индексом <paramref name="beamIndex"/> полей: нужен блок
    /// деформаций (<see cref="RcSecantStepFields.HasStrains"/>). Сечение CScore мутирует фибры — вызывать из одного потока.
    /// </summary>
    public static FemCsfeaBarSectionPoint Compute(CrossSection section, CalcType calc, bool tension, bool yAlongLocalY,
        RcSecantStepFields f, int beamIndex, int point)
    {
        if (!f.HasStrains) throw new InvalidOperationException("В полях шага нет деформаций сечений.");
        int so = beamIndex * RcSecantStepFields.BeamStrainComponents + 3 * point;
        var plane = Plane(f.BeamStrains[so], f.BeamStrains[so + 1], f.BeamStrains[so + 2], yAlongLocalY);
        var recorded = ToSection(ElementForces(f, beamIndex, point), yAlongLocalY);
        return Compute(section, calc, tension, plane, recorded);
    }

    /// <summary>Состояние сечения по плоскости деформаций и записанным усилиям в осях сечения (кН, кН·м).</summary>
    public static FemCsfeaBarSectionPoint Compute(CrossSection section, CalcType calc, bool tension, Kurvature plane,
        (double N, double Mx, double My) recorded)
    {
        bool cracked = SecantCrossSectionBuilder.IsCracked(section, plane, calc);
        bool ten = tension && !cracked;
        var load = section.Integral(plane, calc, ten, true);
        double cMin = double.PositiveInfinity, cMax = double.NegativeInfinity, epsMin = double.PositiveInfinity,
            epsMax = double.NegativeInfinity, epsB = 0, sigB = 0, epsS = 0, sigS = 0;
        double xMin = double.PositiveInfinity, xMax = double.NegativeInfinity, yMin = double.PositiveInfinity, yMax = double.NegativeInfinity;
        var rebar = new List<FemCsfeaRebarFiber>();
        foreach (var area in section.Areas)
        {
            if (area.Material == null || !area.Diagramms.ContainsKey(calc)) continue;
            bool concrete = area.Material.Type == MatType.Concrete;
            foreach (var fiber in area.Fibers)
            {
                double eps = fiber.Eps, sig = fiber.Sig / 1e3;
                xMin = Math.Min(xMin, fiber.X); xMax = Math.Max(xMax, fiber.X);
                yMin = Math.Min(yMin, fiber.Y); yMax = Math.Max(yMax, fiber.Y);
                epsMin = Math.Min(epsMin, eps);
                epsMax = Math.Max(epsMax, eps);
                if (concrete)
                {
                    cMin = Math.Min(cMin, eps);
                    cMax = Math.Max(cMax, eps);
                    if (eps < epsB) { epsB = eps; sigB = sig; }
                }
                else if (eps > epsS) { epsS = eps; sigS = sig; }
                if (fiber.TypeFiber == FiberType.point) rebar.Add(new FemCsfeaRebarFiber(rebar.Count + 1, fiber.X, fiber.Y, eps, sig));
            }
        }
        // Сжатая зона — по нормали к нейтральной оси: от наиболее сжатой фибры бетона до нуля деформаций, не больше
        // высоты бетона по этому направлению.
        double grad = Math.Sqrt(plane.ky * plane.ky + plane.kz * plane.kz), height = 0;
        if (cMin < 0 && double.IsFinite(cMax))
            height = grad > 1e-12 ? Math.Min(-cMin, cMax - cMin) / grad : double.PositiveInfinity;
        if (double.IsPositiveInfinity(height)) height = 0;   // равномерное сжатие без градиента — высота не определена
        if (!double.IsFinite(epsMin)) epsMin = epsMax = 0;
        return new FemCsfeaBarSectionPoint(plane, load.N, load.Mx, load.My, recorded.N, recorded.Mx, recorded.My, height,
            epsB, sigB, epsS, sigS, epsMin, epsMax, rebar) { Cracked = cracked, Tension = ten, Size = double.IsFinite(xMax) ? Math.Max(1e-3, Math.Max(xMax - xMin, yMax - yMin)) : 1.0 };
    }
}
