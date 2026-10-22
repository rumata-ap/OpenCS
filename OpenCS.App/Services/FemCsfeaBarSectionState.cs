using CScore;
using CSfea.CScoreBridge;
using CSfea.CScoreBridge.Structural;

namespace OpenCS.Services;

/// <summary>Арматурная фибра в состоянии сечения: координаты (м), ε, σ (МПа, с ψs) и ψs (1 — без поправки).</summary>
public sealed record FemCsfeaRebarFiber(int Num, double X, double Y, double Eps, double SigmaMpa, double Psi = 1.0);

/// <summary>
/// Состояние фибрового сечения стержня в точке ξ = 0, ½, 1 по записанным деформациям (спека НДС сечений, п. 2.3).
/// <see cref="Plane"/> — плоскость деформаций в осях сечения CScore (ε = e0 + ky·Y + kz·X); усилия — в осях сечения
/// (кН, кН·м: N, Mx, My) по интегралу фибр и записанные усилия точки, переведённые в те же оси.
/// <see cref="CompressedHeight"/> — высота сжатой зоны бетона по нормали к нейтральной оси, м (вся высота — сечение сжато
/// целиком; 0 — сжатия нет). Крайние значения: <see cref="EpsB"/>, <see cref="SigmaB"/> — наибольшее сжатие бетона (ε,
/// МПа; 0 — нет), <see cref="EpsS"/>, <see cref="SigmaS"/> — наибольшее растяжение арматуры (σ — с ψs).
/// </summary>
public sealed record FemCsfeaBarSectionPoint(
    Kurvature Plane, double N, double Mx, double My, double RecordedN, double RecordedMx, double RecordedMy,
    double CompressedHeight, double EpsB, double SigmaB, double EpsS, double SigmaS, double EpsMin, double EpsMax,
    IReadOnlyList<FemCsfeaRebarFiber> Rebar)
{
    /// <summary>В точке есть трещина (по показанной плоскости или по флагу КЭ — <see cref="CrackFromFlag"/>).</summary>
    public bool Cracked { get; init; }

    /// <summary>
    /// Трещина взята по флагу трещины КЭ: по показанной плоскости её нет, но с ней интеграл фибр ближе к записанным
    /// усилиям (после трещины ψs ужесточает сечение, и деформация опускается ниже ε_bt,ult).
    /// </summary>
    public bool CrackFromFlag { get; init; }

    /// <summary>Растянутый бетон работает (разрешено постановкой и трещины нет) — так строятся эпюры окна.</summary>
    public bool Tension { get; init; }

    /// <summary>ψs учтён (постановка с ψs, трещина, εs,crc найдена).</summary>
    public bool HasPsi => RebarStressKpa != null;

    /// <summary>Наименьший ψs растянутых стержней (1 — поправки нет).</summary>
    public double PsiMin => Rebar.Count > 0 ? Math.Min(1.0, Rebar.Min(r => r.Psi)) : 1.0;

    /// <summary>σ арматурных фибр с ψs, кПа (для мозаики и разреза окна); null — ψs не учтён.</summary>
    public IReadOnlyDictionary<Fiber, double>? RebarStressKpa { get; init; }

    /// <summary>
    /// σ арматурных фибр с ψs по собственной диаграмме стали, кПа (показ в разрезе); у разностной диаграммы
    /// <see cref="RebarStressKpa"/> — равновесная σs − σb. null — ψs не учтён.
    /// </summary>
    public IReadOnlyDictionary<Fiber, double>? RebarStressOwnKpa { get; init; }

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
/// σ арматуры с ψs для разреза окна НДС сечения (<see cref="FemCsfeaBarSectionPoint.RebarStressOwnKpa"/>); бетон и стержни
/// без записи — с диаграммы.
/// </summary>
public sealed class FemCsfeaRebarStressSource(IReadOnlyDictionary<Fiber, double> stressKpa) : ISectionCutStressSource
{
    public double? ConcreteSigMpa(MaterialArea area, double x, double y, double eps, CalcType calcType) => null;

    public double? RebarSigMpa(Fiber fiber, double eps, CalcType calcType) =>
        stressKpa.TryGetValue(fiber, out double s) ? s / 1e3 : null;
}

/// <summary>
/// Фибровое сечение стержня по записанным в полях шага CSfea деформациям (ε₀, κy, κz в осях КЭ). Память трещин точек не
/// записывается — трещина восстанавливается: по показанной плоскости (<see cref="SecantCrossSectionBuilder.IsCracked"/>),
/// а у КЭ с записанным флагом трещины точка без трещины по плоскости считается треснувшей, если так интеграл фибр ближе к
/// записанным усилиям. С трещиной растянутый бетон точки не работает, ψs (постановка с ψs) — по εs,crc при соотношении
/// записанных усилий точки (<see cref="SecantCrossSectionBuilder.EpsCrcAtCracking"/>; в расчёте — при соотношении в
/// момент трещины) и поправке <see cref="Curvature8232.ApplyPsiCorrection"/>, как в расчёте. При монотонном
/// пропорциональном нагружении совпадает с расчётом. Оси сечения в осях КЭ — как у <see cref="CrossSectionSecantBeamState"/>:
/// по умолчанию ε = ε₀ + κy·Y + κz·X, при <c>yAlongLocalY</c> — ε = ε₀ − κz·Y + κy·X.
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
    /// деформаций стержней (<see cref="RcSecantStepFields.HasBeamStrains"/>, с 18.10.2026 пишутся всегда); флаг трещины КЭ — из полей. Сечение CScore мутирует
    /// фибры — вызывать из одного потока.
    /// </summary>
    public static FemCsfeaBarSectionPoint Compute(CrossSection section, CalcType calc, bool tension, bool yAlongLocalY,
        RcSecantStepFields f, int beamIndex, int point, bool psi = false)
    {
        if (!f.HasBeamStrains) throw new InvalidOperationException("В полях шага нет деформаций стержней.");
        int so = beamIndex * RcSecantStepFields.BeamStrainComponents + 3 * point;
        var plane = Plane(f.BeamStrains[so], f.BeamStrains[so + 1], f.BeamStrains[so + 2], yAlongLocalY);
        var recorded = ToSection(ElementForces(f, beamIndex, point), yAlongLocalY);
        bool flag = beamIndex < f.BeamFlags.Length && (f.BeamFlags[beamIndex] & RcSecantStepFields.Cracked) != 0;
        return Compute(section, calc, tension, plane, recorded, psi, flag);
    }

    /// <summary>
    /// Состояние сечения по плоскости деформаций и записанным усилиям в осях сечения (кН, кН·м);
    /// <paramref name="elementCracked"/> — записанный флаг трещины КЭ.
    /// </summary>
    public static FemCsfeaBarSectionPoint Compute(CrossSection section, CalcType calc, bool tension, Kurvature plane,
        (double N, double Mx, double My) recorded, bool psi = false, bool elementCracked = false)
    {
        bool byPlane = SecantCrossSectionBuilder.IsCracked(section, plane, calc);
        if (byPlane || !elementCracked) return Evaluate(section, calc, tension, plane, recorded, byPlane, psi);
        // Флаг КЭ без трещины по плоскости: из двух состояний — то, что ближе к записанным усилиям. Последним вычисляется
        // выбранное (фибры сечения хранят его — по ним строятся мозаики окна).
        var cracked = Evaluate(section, calc, tension, plane, recorded, true, psi);
        var open = Evaluate(section, calc, tension, plane, recorded, false, psi);
        return cracked.Discrepancy < open.Discrepancy
            ? Evaluate(section, calc, tension, plane, recorded, true, psi) with { CrackFromFlag = true }
            : open;
    }

    static FemCsfeaBarSectionPoint Evaluate(CrossSection section, CalcType calc, bool tension, Kurvature plane,
        (double N, double Mx, double My) recorded, bool cracked, bool psi)
    {
        bool ten = tension && !cracked;
        // εs,crc — до интеграла показанной плоскости: решатель сечения с трещиной меняет поля фибр.
        var epsCrc = psi && cracked ? SecantCrossSectionBuilder.EpsCrcAtCracking(section, calc, calc, recorded.N, recorded.Mx, recorded.My) : null;
        var load = section.Integral(plane, calc, ten, true);
        if (epsCrc != null) load = Curvature8232.ApplyPsiCorrection(section, plane, load, epsCrc, calc);
        double cMin = double.PositiveInfinity, cMax = double.NegativeInfinity, epsMin = double.PositiveInfinity,
            epsMax = double.NegativeInfinity, epsB = 0, sigB = 0, epsS = 0, sigS = 0;
        double xMin = double.PositiveInfinity, xMax = double.NegativeInfinity, yMin = double.PositiveInfinity, yMax = double.NegativeInfinity;
        var rebar = new List<FemCsfeaRebarFiber>();
        var rebarStress = epsCrc != null ? new Dictionary<Fiber, double>(ReferenceEqualityComparer.Instance) : null;
        var rebarStressOwn = epsCrc != null ? new Dictionary<Fiber, double>(ReferenceEqualityComparer.Instance) : null;
        foreach (var area in section.Areas)
        {
            if (area.Material == null || !area.Diagramms.ContainsKey(calc)) continue;
            bool concrete = area.Material.Type == MatType.Concrete;
            foreach (var fiber in area.Fibers)
            {
                // Стержень — σ по собственной диаграмме стали, а не разностная σs − σb равновесия.
                double eps = fiber.Eps, sig = (concrete ? fiber.Sig : fiber.SigOwn) / 1e3;
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
                if (fiber.TypeFiber != FiberType.point) continue;
                double fiberPsi = epsCrc != null && epsCrc.TryGetValue(fiber, out double crc) && eps + fiber.Eps_p > 0
                    ? Curvature8232.PsiS(crc, eps + fiber.Eps_p) : 1.0;
                rebar.Add(new FemCsfeaRebarFiber(rebar.Count + 1, fiber.X, fiber.Y, eps, sig, fiberPsi));
                if (rebarStress != null) rebarStress[fiber] = fiber.Sig;
                if (rebarStressOwn != null) rebarStressOwn[fiber] = fiber.SigOwn;
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
            epsB, sigB, epsS, sigS, epsMin, epsMax, rebar)
        {
            Cracked = cracked, Tension = ten, RebarStressKpa = rebarStress, RebarStressOwnKpa = rebarStressOwn,
            Size = double.IsFinite(xMax) ? Math.Max(1e-3, Math.Max(xMax - xMin, yMax - yMin)) : 1.0,
        };
    }
}
