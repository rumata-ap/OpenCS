using CScore;

namespace CSfea.CScoreBridge;

/// <summary>
/// Пофибровая секущая матрица стержневого сечения <see cref="CrossSection"/> в единицах CScore:
/// (N, Mx, My) = S·(e0, ky, kz), ε волокна = e0 + ky·Y + kz·X (конвенция <see cref="Kurvature"/>).
/// S = Σ E_sec·A·[1, Y, X]ᵀ[1, Y, X], E_sec = σ/ε волокна (бетон — с растяжением или без, арматура —
/// с ψs по (8.160) через <see cref="Curvature8232.ApplyPsiCorrection"/>). В отличие от диагонали
/// <c>IBeamSectionResponse.Secant</c> матрица сохраняет связь N–M при смещении нейтральной оси.
/// Порядок строк и столбцов совпадает с <see cref="CrossSectionBeamResponse"/>: в осях CSfea это
/// (N, M_y, M_z) по (ε₀, κ_y, κ_z) после умножения на <see cref="UnitScale"/>.
///
/// Сеточные и точечные волокна воспроизводят усилия <see cref="CrossSection.Integral"/> точно;
/// полигон без сетки интегрируется по контуру (<see cref="MaterialArea.ContourSecantStiffness"/>,
/// с разбиением по характерным деформациям диаграммы) — на кусочно-линейных диаграммах тоже до
/// округления. Преднапряжение и составные сечения не поддерживаются: секущая через начало
/// координат их не выражает.
/// </summary>
public static class SecantCrossSectionBuilder
{
    /// <summary>Секущая матрица и усилия (с ψs, если задана карта εs,crc).</summary>
    public static (double[,] S, Load Forces) Build(CrossSection section, Kurvature k, CalcType calc,
        bool ten, bool ca = true, IReadOnlyDictionary<Fiber, double>? epsCrcByFiber = null)
    {
        ArgumentNullException.ThrowIfNull(section);
        var load = section.Integral(k, calc, ten, ca);
        if (epsCrcByFiber != null)
            load = Curvature8232.ApplyPsiCorrection(section, k, load, epsCrcByFiber, calc);

        var s = new double[3, 3];
        foreach (var (area, ka) in section.EnumerateAreas(k))
        {
            if (ka.e0 != k.e0 || ka.ky != k.ky || ka.kz != k.kz)
                throw new NotSupportedException("Секущая матрица составного (многоэтапного) сечения не поддерживается.");
            if (!area.Diagramms.TryGetValue(calc, out var dgr)) continue;

            // Тот же выбор пути, что в CrossSection.IntegralOf.
            bool hasMesh = area.Fibers.Any(f => f.TypeFiber != FiberType.point);
            bool contour = !hasMesh && area.Hull != null;
            if (contour)
            {
                var c = area.ContourSecantStiffness(ka, calc, ten, ca);
                Add(s, c.D33, c.D13, c.D23, c.D11, c.D12, c.D22);
            }

            foreach (var f in area.Fibers)
            {
                if (contour && f.TypeFiber != FiberType.point) continue;
                if (f.Eps_p != 0.0)
                    throw new NotSupportedException("Секущая матрица сечения с преднапряжением не поддерживается.");
                double e = f.Eps != 0.0 ? f.Sig / f.Eps : InitialModulus(dgr, ten, ca);
                double w = e * f.Area;
                Add(s, w, w * f.Y, w * f.X, w * f.Y * f.Y, w * f.X * f.Y, w * f.X * f.X);
            }
        }
        return (s, load);
    }

    /// <summary>Секущая матрица в единицах CSfea (Н, Н·м), строки (N, M_y, M_z).</summary>
    public static double[,] ToCsfea(double[,] s)
    {
        var r = new double[3, 3];
        for (int i = 0; i < 3; i++)
            for (int j = 0; j < 3; j++)
                r[i, j] = s[i, j] * (i == 0 ? UnitScale.Force : UnitScale.Moment);
        return r;
    }

    /// <summary>
    /// Есть ли в сечении трещина при плоскости <paramref name="k"/>: растягивающая деформация бетона
    /// на контуре дошла до ε_bt,ult (критерий <see cref="CrackingSolver"/>). Целиком сжатое сечение
    /// (например, при внецентренном сжатии с малым эксцентриситетом) трещины не имеет — ψs к нему
    /// не применяется. Сечение без бетона (стальной профиль) не трескается.
    /// </summary>
    public static bool IsCracked(CrossSection section, Kurvature k, CalcType calcCrc)
    {
        if (!CrackingSolver.HasConcrete(section)) return false;
        var solver = new CrackingSolver(section, calcCrc);
        return solver.MaxTensionStrain(k) >= solver.TensionLimit();
    }

    /// <summary>
    /// εs,crc точечных стержней (деформация сразу после образования трещины, п. 8.2.32) — как в
    /// <see cref="TotalCurvatureSolver"/>: момент трещинообразования по лучу текущих моментов при
    /// неизменной N (растянутая зона — от внешней нагрузки), затем НДС сечения с трещиной (бетон
    /// без растяжения) при M = M_crc. Null — если поиск не сошёлся.
    /// </summary>
    public static Dictionary<Fiber, double>? EpsCrcAtCracking(CrossSection section, CalcType calcCrc,
        CalcType calcService, double n, double mx, double my)
    {
        ArgumentNullException.ThrowIfNull(section);
        var crc = new CrackingSolver(section, calcCrc,
            tensionZone: CrackingSolver.LoadedTensionZone(section, n, mx, my, nAtZeroMoment: n))
            .CrackingMoment(n, mx, my);
        if (!crc.Converged) return null;

        var solver = new StrainSolver(section, calcService, ten: false, ca: true);
        var plane = solver.Solve(n, crc.Mx, crc.My, section.Guess(new Load { N = n, Mx = mx, My = my }));
        if (!solver.Converged) return null;

        var map = new Dictionary<Fiber, double>(ReferenceEqualityComparer.Instance);
        foreach (var (area, ka) in section.EnumerateAreas(plane))
        {
            if (area.Material?.Type is not (MatType.ReSteelF or MatType.ReSteelU)) continue;
            foreach (var f in area.Fibers)
                if (f.TypeFiber == FiberType.point)
                    map[f] = ka.e0 + ka.ky * f.Y + ka.kz * f.X;
        }
        return map;
    }

    // Как MaterialArea.SecantModulus при ε → 0: секущая по пробной малой деформации. Если ветвь растяжения выключена
    // (бетон без растяжения) и проба на растяжение даёт ноль — модуль сжатой ветви: волокно при ε = 0 не «пустое»,
    // иначе начальная осевая жёсткость сечения без растяжения сводилась бы к одной арматуре.
    private static double InitialModulus(Diagramm dgr, bool ten, bool ca)
    {
        const double probe = 1e-12;
        double sigma = dgr.Sig(probe, out double tangent, ten, ca);
        if (Math.Abs(sigma) > 1e-20) return sigma / probe;
        double sigmaC = dgr.Sig(-probe, out double tangentC, ten, ca);
        if (Math.Abs(sigmaC) > 1e-20) return sigmaC / -probe;
        return tangent != 0.0 ? tangent : tangentC;
    }

    private static void Add(double[,] s, double a0, double ay, double ax, double ayy, double axy, double axx)
    {
        s[0, 0] += a0;
        s[0, 1] += ay; s[1, 0] += ay;
        s[0, 2] += ax; s[2, 0] += ax;
        s[1, 1] += ayy;
        s[1, 2] += axy; s[2, 1] += axy;
        s[2, 2] += axx;
    }
}
