using System;
using System.Collections.Generic;
using System.Linq;

namespace CScore;

/// <summary>
/// Метод вычисления коэффициента ψs (неравномерность деформаций растянутой арматуры
/// между трещинами) в задаче ширины раскрытия трещин.
/// </summary>
public enum PsiSMethod
{
    /// <summary>П. 8.2.18, формула 8.138: ψs = 1 − 0.8·σs,crc/σs (по отношению напряжений).</summary>
    Stress8138,
    /// <summary>
    /// П. 8.2.32, формула деформационной модели: ψs = 1 / (1 + 0.8·εs,crc/εs) (по отношению
    /// деформаций). Норма относит эту формулу к расчёту кривизны/жёсткости (многослойная
    /// деформационная модель), а не к формуле раскрытия трещин 8.2.15 — здесь применяется
    /// как внормативно допускаемая альтернатива к тому же "представительному" стержню,
    /// что и Stress8138 (без встраивания в саму итерацию равновесия).
    /// </summary>
    Strain8232
}

/// <summary>Результат расчёта ширины раскрытия нормальных трещин (СП 63.13330 п.8.2). Все ширины — в мм.</summary>
public sealed class CrackWidthResult
{
    public bool Cracked { get; set; }
    public double AcrcLong { get; set; }
    public double AcrcShort { get; set; }
    public double AcrcUltLong { get; set; }
    public double AcrcUltShort { get; set; }
    public bool PassedLong { get; set; } = true;
    public bool PassedShort { get; set; } = true;
    public double UtilLong { get; set; }
    public double UtilShort { get; set; }
    /// <summary>Момент трещинообразования, кН·м.</summary>
    public double Mcrc { get; set; }
    /// <summary>Напряжение в наиболее растянутом стержне от длительных нагрузок, кПа.</summary>
    public double SigmaS { get; set; }
    /// <summary>То же при образовании трещин (на той же диаграмме, что и SigmaS), кПа.</summary>
    public double SigmaSCrc { get; set; }
    /// <summary>
    /// Напряжение в арматуре при образовании трещин на базисе σs,total (для acrc2) —
    /// в общем случае отличается от <see cref="SigmaSCrc"/>, т.к. считается на другой диаграмме.
    /// </summary>
    public double SigmaSCrc2 { get; set; }
    /// <summary>ψs для acrc1/acrc3 (длительная нагрузка, п. 8.2.18).</summary>
    public double PsiS { get; set; }
    /// <summary>
    /// ψs для acrc2 (полная кратковременная нагрузка) — считается на паре σs,total/σs,crc,short,
    /// поэтому в общем случае отличается от <see cref="PsiS"/>.
    /// </summary>
    public double PsiS2 { get; set; }
    /// <summary>Базовое расстояние между трещинами, м.</summary>
    public double Ls { get; set; }
    /// <summary>Эквивалентный диаметр растянутой арматуры, м.</summary>
    public double DsEq { get; set; }
    /// <summary>
    /// Приведённая площадь растянутой арматуры, м² — с весом σi/σs (см.
    /// <see cref="CrackWidthSolver"/>.TensileRebarProps): слабо растянутый стержень (у
    /// нейтральной оси) входит в сумму не полной площадью, а пропорционально своей доле
    /// напряжения от самого растянутого стержня.
    /// </summary>
    public double AsTens { get; set; }
    /// <summary>Площадь растянутого бетона (ограниченная), м².</summary>
    public double Abt { get; set; }
    public double Acrc1 { get; set; }
    public double Acrc2 { get; set; }
    public double Acrc3 { get; set; }
    /// <summary>Компонента момента трещинообразования по X, кН·м (см. <see cref="Mcrc"/> — их модуль).</summary>
    public double MxCrc { get; set; }
    /// <summary>Компонента момента трещинообразования по Y, кН·м.</summary>
    public double MyCrc { get; set; }
    /// <summary>Сошёлся ли поиск момента трещинообразования (бисекция в <see cref="CrackingSolver"/>).</summary>
    public bool CrcConverged { get; set; }
    /// <summary>Максимальная растягивающая деформация бетона в момент трещинообразования.</summary>
    public double EpsMaxTension { get; set; }
    /// <summary>Предельная растягивающая деформация бетона (из диаграммы, п. Г.1 СП63.13330).</summary>
    public double EpsTensionLimit { get; set; }
    /// <summary>Эффективная высота сечения h0, м (используется при расчёте ls/Abt).</summary>
    public double H0 { get; set; }
    /// <summary>Пост-трещинная плоскость деформаций (ten=false) от длительной нагрузки, если Newton сошёлся.</summary>
    public Kurvature? PlaneLong { get; set; }
}

/// <summary>
/// Ширина раскрытия нормальных трещин по СП 63.13330 п.8.2.15-8.2.18.
/// Единицы: кН, кН·м, м, кПа на входе/внутри. Требует, чтобы
/// <see cref="CrossSection.ResolveAndBuildDiagramms"/> уже был вызван вызывающей стороной.
/// </summary>
public sealed class CrackWidthSolver
{
    readonly CrossSection _section;
    readonly CalcType _calcCrc;
    readonly CalcType _calcService;
    readonly CalcType _calcServiceLong;
    readonly double _phi2;
    readonly double _acrcUltLong;
    readonly double _acrcUltShort;
    readonly double _sp63EtaMin;
    readonly double _solverTol;
    readonly PsiSMethod _psiSMethod;

    public CrackWidthSolver(
        CrossSection section,
        CalcType calcCrc = CalcType.N,
        CalcType calcService = CalcType.N,
        CalcType? calcServiceLong = null,
        double phi2 = 0.5,
        double acrcUltLong = 0.3,
        double acrcUltShort = 0.4,
        double sp63EtaMin = 0.85,
        double solverTol = 0.5,
        PsiSMethod psiSMethod = PsiSMethod.Stress8138)
    {
        _section = section ?? throw new ArgumentNullException(nameof(section));
        _calcCrc = calcCrc;
        _calcService = calcService;
        _calcServiceLong = calcServiceLong ?? calcService;
        _phi2 = phi2;
        _acrcUltLong = acrcUltLong;
        _acrcUltShort = acrcUltShort;
        _sp63EtaMin = sp63EtaMin;
        _solverTol = solverTol;
        _psiSMethod = psiSMethod;
    }

    /// <summary>
    /// Демпфированный метод Ньютона (backtracking line search) с АНАЛИТИЧЕСКИМ Якобианом,
    /// построенным из касательного модуля E2 каждого волокна (<see cref="EvalWithTangent"/>).
    /// Численный (конечно-разностный) Якобиан ненадёжен на кусочно-линейной диаграмме σ(ε):
    /// фиксированный шаг h может случайно перевести часть волокон через излом при вычислении
    /// f(k+h), но не f(k−h) (или наоборот), давая ложный наклон и расходящийся шаг. Аналитический
    /// Якобиан использует касательный модуль ИМЕННО в точке k — корректен по обе стороны излома.
    /// </summary>
    Kurvature SolveDamped(double nTarget, double mxTarget, double myTarget, Kurvature seed,
        CalcType calcType, out bool converged, int maxIter = 60)
    {
        if (HasMeshlessArea(calcType))
        {
            // EvalWithTangent суммирует только area.Fibers и не поддерживает контурный
            // (безсеточный, теорема Грина) путь CrossSection.Integral — на сечениях без
            // построенной сетки бетона он "не видит" бетон вовсе и расходится в абсурдные
            // кривизны. StrainSolver с численным Якобианом идёт через Integral() и корректно
            // работает в обоих режимах (сеточном и контурном).
            var strainSolver = new StrainSolver(_section, calcType, ten: false, ca: true,
                tol: _solverTol, maxIter: maxIter, centralJacobian: true);
            var kMeshless = strainSolver.Solve(nTarget, mxTarget, myTarget, seed);
            converged = strainSolver.Converged;
            return kMeshless;
        }

        Kurvature k = seed;
        double bestResidual = double.MaxValue;
        Kurvature bestK = seed;

        for (int iter = 0; iter < maxIter; iter++)
        {
            var (f0, jac) = EvalWithTangent(k, calcType);

            double r0 = f0.N - nTarget, r1 = f0.Mx - mxTarget, r2 = f0.My - myTarget;
            double residual = Math.Sqrt(r0 * r0 + r1 * r1 + r2 * r2);
            if (residual < bestResidual) { bestResidual = residual; bestK = k; }
            if (residual < _solverTol) { converged = true; return k; }

            if (!GaussSolve3(jac, [r0, r1, r2], out var dk))
            {
                // Вырожденный якобиан — не сдаёмся сразу (как в python-прототипе): маленький
                // шаг в сторону уменьшения невязки и продолжаем итерации.
                k.e0 -= 1e-6 * r0;
                k.ky -= 1e-6 * r1;
                k.kz -= 1e-6 * r2;
                continue;
            }

            // Backtracking line search: половиним шаг, пока невязка не перестанет расти.
            double lambda = 1.0;
            bool accepted = false;
            for (int ls = 0; ls < 20; ls++)
            {
                var kNext = new Kurvature
                {
                    e0 = k.e0 - lambda * dk[0],
                    ky = k.ky - lambda * dk[1],
                    kz = k.kz - lambda * dk[2]
                };
                var (fNext, _) = EvalWithTangent(kNext, calcType);
                double rn0 = fNext.N - nTarget, rn1 = fNext.Mx - mxTarget, rn2 = fNext.My - myTarget;
                double residualNext = Math.Sqrt(rn0 * rn0 + rn1 * rn1 + rn2 * rn2);
                if (residualNext < residual)
                {
                    k = kNext;
                    accepted = true;
                    break;
                }
                lambda *= 0.5;
            }
            if (!accepted)
            {
                converged = bestResidual < _solverTol;
                return bestK;
            }
        }

        converged = bestResidual < _solverTol;
        return bestK;
    }

    /// <summary>
    /// Вычисляет усилия (N, Mx, My) и аналитический Якобиан ∂(N,Mx,My)/∂(e0,ky,kz) в точке k,
    /// суммируя по всем волокнам (mesh + точечные) касательный модуль E2, полученный из
    /// <see cref="Diagramm.Sig(Fiber, bool, bool)"/> (текущая, а не конечно-разностная, точка).
    /// </summary>
    (Load load, double[,] jacobian) EvalWithTangent(Kurvature k, CalcType calcType)
    {
        double n = 0, mx = 0, my = 0;
        double dN_de0 = 0, dN_dky = 0, dN_dkz = 0;
        double dMx_dky = 0, dMx_dkz = 0;
        double dMy_dkz = 0;

        foreach (var (area, ka) in _section.EnumerateAreas(k))
        {
            // ten: false — сервисная (пост-трещинная) плоскость деформаций (п.8.2.14): бетон
            // на растяжение не работает совсем (не "до Et2" — иначе разрыв σ→0 при ε>Et2,
            // общий для L2/L3/SP63, дестабилизирует Ньютона). До и в момент трещинообразования
            // растяжение бетона уже учтено в CrackingSolver (calcCrc, ten=true по умолчанию) —
            // именно оттуда SolveRamped стартует, поэтому здесь мы всегда уже "за" трещиной.
            area.SetEps(ka, calcType, false, true);
            foreach (var f in area.Fibers)
            {
                n += f.N;
                mx += f.Mx;
                my += f.My;

                double et = f.E2 * f.Area;
                dN_de0 += et;
                dN_dky += et * f.Y;
                dN_dkz += et * f.X;
                dMx_dky += et * f.Y * f.Y;
                dMx_dkz += et * f.Y * f.X;
                dMy_dkz += et * f.X * f.X;
            }
        }

        var load = new Load { N = n, Mx = mx, My = my };
        var jac = new double[3, 3]
        {
            { dN_de0, dN_dky, dN_dkz },
            { dN_dky, dMx_dky, dMx_dkz },
            { dN_dkz, dMx_dkz, dMy_dkz }
        };
        return (load, jac);
    }

    /// <summary>
    /// Есть ли в сечении область без сеточных волокон, работающая через контурный
    /// (безсеточный) путь <see cref="CrossSection.Integral"/> — та же проверка, что и там.
    /// </summary>
    bool HasMeshlessArea(CalcType calcType) =>
        _section.Areas.Any(a =>
            !a.Fibers.Any(f => f.TypeFiber != FiberType.point) &&
            a.Hull != null &&
            a.Diagramms.ContainsKey(calcType));

    /// <summary>Метод Гаусса с выбором ведущего элемента для системы 3×3. Возвращает false при сингулярности.</summary>
    static bool GaussSolve3(double[,] a, double[] b, out double[] x)
    {
        x = new double[3];
        double[,] m = (double[,])a.Clone();
        double[] v = (double[])b.Clone();
        const int n = 3;

        for (int col = 0; col < n; col++)
        {
            int pivot = col;
            for (int row = col + 1; row < n; row++)
                if (Math.Abs(m[row, col]) > Math.Abs(m[pivot, col])) pivot = row;

            double pivVal = m[pivot, col];
            if (!double.IsFinite(pivVal) || Math.Abs(pivVal) < 1e-15) return false;

            if (pivot != col)
            {
                for (int k2 = 0; k2 < n; k2++) (m[col, k2], m[pivot, k2]) = (m[pivot, k2], m[col, k2]);
                (v[col], v[pivot]) = (v[pivot], v[col]);
            }

            for (int row = col + 1; row < n; row++)
            {
                double factor = m[row, col] / m[col, col];
                for (int k2 = col; k2 < n; k2++) m[row, k2] -= factor * m[col, k2];
                v[row] -= factor * v[col];
            }
        }

        for (int row = n - 1; row >= 0; row--)
        {
            double sum = v[row];
            for (int k2 = row + 1; k2 < n; k2++) sum -= m[row, k2] * x[k2];
            x[row] = sum / m[row, row];
        }

        return true;
    }

    static bool IsRebar(MaterialArea area) =>
        area.Material != null &&
        (area.Material.Type == MatType.ReSteelF || area.Material.Type == MatType.ReSteelU);

    /// <summary>
    /// Свойства растянутой арматуры при заданной плоскости деформаций.
    /// Использует СОБСТВЕННУЮ (не разностную) диаграмму материала стержня на диаграмме
    /// <paramref name="calcType"/> — не зависит от того, построены ли в MaterialArea.Diagramms
    /// разностные (сталь-бетон) кривые.
    /// <para>
    /// As_tens/ds_eq считаются НЕ "в лоб" (полная площадь любого стержня с eps&gt;0), а с
    /// весом σi/σ_max — инженерное уточнение сверх буквы п.8.2.17 (там просто "площадь
    /// растянутой арматуры"), актуальное при косом изгибе: стержень у самой нейтральной оси
    /// формально в растяжении, но почти не работает, и учёт его площади наравне со стержнем
    /// у растянутой грани занижал бы ls (и, как следствие, acrc) без физических оснований.
    /// Центр тяжести растянутой арматуры (yTensCentroid, для a_cover/h0) — геометрическая
    /// величина, взвешиванию не подлежит, считается по полной площади.
    /// </para>
    /// </summary>
    (double sigmaMaxKPa, double epsAtSigmaMax, double asTens, double dsEq, double aCoverEff, double yTensCentroid)
        TensileRebarProps(CrossSection section, Kurvature k, CalcType calcType)
    {
        double sigmaMax = 0.0, epsAtSigmaMax = 0.0;

        // Первый проход: найти σ_max среди растянутых стержней — база для веса σi/σ_max.
        foreach (var (area, ka) in section.EnumerateAreas(k))
        {
            if (!IsRebar(area)) continue;
            var ownDiagrams = area.Material!.GetDiagramms(area.DiagrammType, _sp63EtaMin);
            if (ownDiagrams == null || !ownDiagrams.TryGetValue(calcType, out var ownDgr)) continue;

            foreach (var f in area.Fibers)
            {
                if (f.TypeFiber != FiberType.point) continue;
                double eps = ka.e0 + ka.ky * f.Y + ka.kz * f.X;
                if (eps <= 0.0) continue;

                double sig = ownDgr.Sig(eps, out _);
                if (sig > sigmaMax) { sigmaMax = sig; epsAtSigmaMax = eps; }
            }
        }

        if (sigmaMax < 1e-9) return (0.0, 0.0, 0.0, 0.0, 0.0, 0.0);

        // Второй проход: взвешенные As_tens/ds_eq (вес σi/σ_max) и "чистый" (без
        // взвешивания) центр тяжести растянутой арматуры.
        double asWeighted = 0.0, asDWeighted = 0.0;
        double asPlain = 0.0, ayPlain = 0.0;

        foreach (var (area, ka) in section.EnumerateAreas(k))
        {
            if (!IsRebar(area)) continue;
            var ownDiagrams = area.Material!.GetDiagramms(area.DiagrammType, _sp63EtaMin);
            if (ownDiagrams == null || !ownDiagrams.TryGetValue(calcType, out var ownDgr)) continue;

            foreach (var f in area.Fibers)
            {
                if (f.TypeFiber != FiberType.point) continue;
                double eps = ka.e0 + ka.ky * f.Y + ka.kz * f.X;
                if (eps <= 0.0) continue;

                double sig = ownDgr.Sig(eps, out _);
                if (sig <= 0.0) continue;

                double w = sig / sigmaMax;
                asWeighted += f.Area * w;
                asDWeighted += f.Area * w * f.Diameter;
                asPlain += f.Area;
                ayPlain += f.Area * f.Y;
            }
        }

        if (asWeighted < 1e-15) return (0.0, 0.0, 0.0, 0.0, 0.0, 0.0);

        double dsEq = asDWeighted / asWeighted;
        double yTensCentroid = ayPlain / asPlain;

        var concreteVertices = ConcreteVertices();
        double aCoverEff;
        if (k.ky >= 0)
            aCoverEff = Math.Abs(concreteVertices.Max(p => p.Y) - yTensCentroid);
        else
            aCoverEff = Math.Abs(yTensCentroid - concreteVertices.Min(p => p.Y));

        return (sigmaMax, epsAtSigmaMax, asTens: asWeighted, dsEq, aCoverEff, yTensCentroid);
    }

    (double sigmaMaxKPa, double epsAtSigmaMax) SigmaSFromPlane(Kurvature k, CalcType calcType)
    {
        double sigmaMax = 0.0, epsAtSigmaMax = 0.0;
        foreach (var (area, ka) in _section.EnumerateAreas(k))
        {
            if (!IsRebar(area)) continue;
            var ownDiagrams = area.Material!.GetDiagramms(area.DiagrammType, _sp63EtaMin);
            if (ownDiagrams == null || !ownDiagrams.TryGetValue(calcType, out var ownDgr)) continue;

            foreach (var f in area.Fibers)
            {
                if (f.TypeFiber != FiberType.point) continue;
                double eps = ka.e0 + ka.ky * f.Y + ka.kz * f.X;
                if (eps <= 0.0) continue;
                double sig = ownDgr.Sig(eps, out _);
                if (sig > sigmaMax) { sigmaMax = sig; epsAtSigmaMax = eps; }
            }
        }
        return (sigmaMax, epsAtSigmaMax);
    }

    List<(double X, double Y)> ConcreteVertices() =>
        _section.Areas
            .Where(a => a.Material?.Type == MatType.Concrete && a.Hull != null)
            .SelectMany(a => Enumerable.Range(0, a.Hull!.X.Count).Select(i => (a.Hull.X[i], a.Hull.Y[i])))
            .ToList();

    /// <summary>Площадь растянутого бетона Abt (м²) с ограничениями СП63 п.8.2.17 (2a ≤ xt ≤ 0.5h0).</summary>
    double ComputeAbt(Kurvature crcPlane, double aCoverEff, double h0)
    {
        double kx = crcPlane.ky;
        if (Math.Abs(kx) < 1e-12) return 0.0;

        double yNeutral = -crcPlane.e0 / kx;
        int direction = kx > 0 ? +1 : -1;

        double xtMin = 2.0 * aCoverEff;
        double xtMax = 0.5 * h0;

        double abtTotal = 0.0;
        foreach (var area in _section.Areas)
        {
            if (area.Material?.Type != MatType.Concrete || area.Hull == null) continue;
            var verts = Enumerable.Range(0, area.Hull.X.Count)
                .Select(i => (area.Hull.X[i], area.Hull.Y[i])).ToList();
            if (verts.Count < 3) continue;

            double yExtreme = direction == +1 ? verts.Max(p => p.Item2) : verts.Min(p => p.Item2);
            double xtActual = direction == +1 ? yExtreme - yNeutral : yNeutral - yExtreme;
            xtActual = Math.Max(xtActual, 0.0);
            double xtEff = Math.Max(xtMin, Math.Min(xtActual, xtMax));
            if (xtEff <= 1e-12) continue;

            List<(double X, double Y)> clipped;
            if (direction == +1)
            {
                // Оставить полосу [y_extreme - xtEff, y_extreme] => y >= (y_extreme - xtEff)
                clipped = GridSplit.ClipByHalfPlane(verts, 0.0, yExtreme - xtEff, 0.0, 1.0);
            }
            else
            {
                // Оставить полосу [y_extreme, y_extreme + xtEff] => y <= (y_extreme + xtEff)
                clipped = GridSplit.ClipByHalfPlane(verts, 0.0, yExtreme + xtEff, 0.0, -1.0);
            }

            if (clipped.Count < 3) continue;
            abtTotal += WktHelper.PolygonArea(
                clipped.Select(p => p.X).ToList(),
                clipped.Select(p => p.Y).ToList());
        }

        return abtTotal;
    }

    (double acrcMm, double psiS) SingleAcrc(double sigmaSKPa, double sigmaCrcKPa,
        double epsSRaw, double epsCrcRaw, double lsM, double phi1, double phi3, double esKPa)
    {
        if (sigmaSKPa < 1.0) return (0.0, 1.0);

        double psiS = ComputePsiS(sigmaSKPa, sigmaCrcKPa, epsSRaw, epsCrcRaw);

        double epsS = sigmaSKPa / esKPa;
        double acrcM = phi1 * _phi2 * phi3 * psiS * epsS * lsM;
        return (acrcM * 1000.0, psiS);
    }

    /// <summary>ψs по выбранному методу (<see cref="_psiSMethod"/>) — п. 8.2.18 (по напряжениям) или п. 8.2.32 (по деформациям).</summary>
    double ComputePsiS(double sigmaSKPa, double sigmaCrcKPa, double epsS, double epsCrc)
    {
        if (_psiSMethod == PsiSMethod.Strain8232)
        {
            // ψs = 1 / (1 + 0.8·εs,crc/εs). Отрицательную/нулевую εs,crc (стержень ещё не
            // был растянут в момент трещинообразования сечения) приводим к 0 — снижения нет,
            // ψs = 1, как и предписывает норма для случая "трещины ещё нет у этого стержня".
            double ratio = epsS > 1e-12 ? Math.Max(epsCrc, 0.0) / epsS : 0.0;
            return Math.Clamp(1.0 / (1.0 + 0.8 * ratio), 0.0, 1.0);
        }

        double psiS = 1.0 - 0.8 * sigmaCrcKPa / sigmaSKPa;
        return Math.Clamp(psiS, 0.2, 1.0);
    }

    static double RebarEsKPa(CrossSection section)
    {
        foreach (var area in section.Areas)
            if (IsRebar(area) && area.Material != null && area.Material.E > 0)
                return area.Material.E;
        return 2.0e8; // фолбэк 200 ГПа (кПа), если в сечении почему-то не нашли арматуру
    }

    /// <summary>Вычислить ширину раскрытия нормальных трещин.</summary>
    /// <param name="phi3">
    /// Коэффициент вида нагружения (п. 8.2.15): 1.0 — изгибаемые и внецентренно сжатые,
    /// 1.2 — растянутые элементы. При null определяется автоматически по знаку N
    /// (N > 0 — растяжение, тем же способом, что и в FemCheckRunner для плит).
    /// </param>
    public CrackWidthResult Compute(
        double N,
        double mxLong,
        double? mxTotal = null,
        double myLong = 0.0,
        double? myTotal = null,
        double? phi3 = null)
    {
        double phi3Eff = phi3 ?? (N > 1e-3 ? 1.2 : 1.0);
        double mxTot = mxTotal ?? mxLong;
        double myTot = myTotal ?? myLong;

        var crcSolver = new CrackingSolver(_section, _calcCrc, solverTol: 0.5);
        double mxDir = Math.Abs(mxLong) > 1e-12 ? mxLong : (Math.Abs(mxTot) > 1e-12 ? mxTot : 1.0);
        double myDir = Math.Abs(mxLong) > 1e-12 || Math.Abs(myLong) > 1e-12 ? myLong : myTot;

        var crcRes = crcSolver.CrackingMoment(N, mxDir, myDir);
        double mcrc = Math.Sqrt(crcRes.Mx * crcRes.Mx + crcRes.My * crcRes.My);
        double epsTensionLimit = crcSolver.TensionLimit();

        double mLong = Math.Sqrt(mxLong * mxLong + myLong * myLong);

        var zero = new CrackWidthResult
        {
            Cracked = false,
            AcrcUltLong = _acrcUltLong,
            AcrcUltShort = _acrcUltShort,
            Mcrc = mcrc,
            MxCrc = crcRes.Mx,
            MyCrc = crcRes.My,
            CrcConverged = crcRes.Converged,
            EpsMaxTension = crcRes.EpsMaxTension,
            EpsTensionLimit = epsTensionLimit
        };

        if (mLong <= mcrc || !crcRes.StrainPlane.HasValue) return zero;

        // Упругая начальная догадка (учитывает армирование — приведённое сечение, см.
        // CrossSection.ElasticProps), затем один демпфированный Ньютон прямо к цели с
        // ten=false — так же, как это делает референсный python-солвер (solve_biaxial):
        // без промежуточной рампы, полагаясь на backtracking line search.
        var elasticGuess = _section.Guess(new Load { N = N, Mx = mxLong, My = myLong });
        if (!double.IsFinite(elasticGuess.e0)) elasticGuess.e0 = 0;
        if (!double.IsFinite(elasticGuess.ky)) elasticGuess.ky = 0;
        if (!double.IsFinite(elasticGuess.kz)) elasticGuess.kz = 0;

        bool convLong;
        var planeLong = SolveDamped(N, mxLong, myLong, elasticGuess, _calcServiceLong, out convLong);
        if (!convLong) return zero;

        Kurvature planeTotal;
        if (Math.Abs(mxTot - mxLong) < 1e-12 && Math.Abs(myTot - myLong) < 1e-12)
        {
            planeTotal = planeLong;
        }
        else
        {
            var elasticGuessTotal = _section.Guess(new Load { N = N, Mx = mxTot, My = myTot });
            if (!double.IsFinite(elasticGuessTotal.e0)) elasticGuessTotal.e0 = 0;
            if (!double.IsFinite(elasticGuessTotal.ky)) elasticGuessTotal.ky = 0;
            if (!double.IsFinite(elasticGuessTotal.kz)) elasticGuessTotal.kz = 0;
            planeTotal = SolveDamped(N, mxTot, myTot, elasticGuessTotal, _calcService, out var convTotal);
            if (!convTotal) planeTotal = planeLong; // фолбэк для σs,total
        }

        var (sigmaLongKPa, epsLong, asTens, dsEq, aCoverEff, yTensCg) = TensileRebarProps(_section, planeLong, _calcServiceLong);
        if (asTens < 1e-15 || dsEq < 1e-15) return zero;

        var concreteVertices = ConcreteVertices();
        double yExtremeComp = planeLong.ky >= 0
            ? concreteVertices.Min(p => p.Y)
            : concreteVertices.Max(p => p.Y);
        double h0 = Math.Abs(yTensCg - yExtremeComp);

        double abt = ComputeAbt(crcRes.StrainPlane.Value, aCoverEff, h0);
        if (abt < 1e-15) return zero;

        double lsM = 0.5 * abt / asTens * dsEq;
        double lsMin = Math.Max(10.0 * dsEq, 0.10);
        double lsMax = Math.Min(40.0 * dsEq, 0.40);
        lsM = Math.Max(lsMin, Math.Min(lsMax, lsM));

        // σs,crc (п.8.2.18) — напряжение в арматуре В СЕЧЕНИИ С ТРЕЩИНОЙ сразу после
        // образования трещин, т.е. на ПОСТ-трещинной (ten=false) плоскости деформаций при
        // M=Mcrc — а не на исходной (ещё не растрескавшейся, ten=true) плоскости
        // crcRes.StrainPlane, которую CrackingSolver использовал только чтобы найти сам
        // момент Mcrc. При чтении σs,crc прямо с той (дотрещинной) плоскости бетон
        // продолжает "работать" на растяжение и берёт на себя часть усилия — σs,crc
        // получается заниженным, почти как напряжение ДО образования трещины, а не сразу
        // после (даёт завышенный, слишком оптимистичный ψs и заниженную acrc).
        var elasticGuessCrc = _section.Guess(new Load { N = N, Mx = crcRes.Mx, My = crcRes.My });
        if (!double.IsFinite(elasticGuessCrc.e0)) elasticGuessCrc.e0 = 0;
        if (!double.IsFinite(elasticGuessCrc.ky)) elasticGuessCrc.ky = 0;
        if (!double.IsFinite(elasticGuessCrc.kz)) elasticGuessCrc.kz = 0;

        var planeCrcLong = SolveDamped(N, crcRes.Mx, crcRes.My, elasticGuessCrc, _calcServiceLong, out var convCrcLong);
        if (!convCrcLong) return zero;

        Kurvature planeCrcShort;
        if (_calcServiceLong == _calcService)
        {
            planeCrcShort = planeCrcLong;
        }
        else
        {
            planeCrcShort = SolveDamped(N, crcRes.Mx, crcRes.My, elasticGuessCrc, _calcService, out var convCrcShort);
            if (!convCrcShort) planeCrcShort = planeCrcLong; // фолбэк, как и для planeTotal
        }

        // σs,crc считается дважды — на той же диаграмме, что и σs, с которой её сравнивают
        // в формуле ψs = 1 − 0.8·σs,crc/σs (acrc1/acrc3 — длительная диаграмма,
        // acrc2 — кратковременная), иначе ψs сравнивало бы напряжения на разных базисах.
        var (sigmaCrcLongKPa, epsCrcLong) = SigmaSFromPlane(planeCrcLong, _calcServiceLong);
        var (sigmaCrcShortKPa, epsCrcShort) = SigmaSFromPlane(planeCrcShort, _calcService);
        var (sigmaTotalKPa, epsTotal) = SigmaSFromPlane(planeTotal, _calcService);

        double esKPa = RebarEsKPa(_section);

        var (acrc1, psiS) = SingleAcrc(sigmaLongKPa, sigmaCrcLongKPa, epsLong, epsCrcLong, lsM, phi1: 1.4, phi3: phi3Eff, esKPa: esKPa);
        var (acrc3, _) = SingleAcrc(sigmaLongKPa, sigmaCrcLongKPa, epsLong, epsCrcLong, lsM, phi1: 1.0, phi3: phi3Eff, esKPa: esKPa);
        var (acrc2, psiS2) = SingleAcrc(sigmaTotalKPa, sigmaCrcShortKPa, epsTotal, epsCrcShort, lsM, phi1: 1.0, phi3: phi3Eff, esKPa: esKPa);

        double acrcLong = acrc1;
        double acrcShort = acrc1 + acrc2 - acrc3;

        bool passedLong = acrcLong <= _acrcUltLong;
        bool passedShort = acrcShort <= _acrcUltShort;

        return new CrackWidthResult
        {
            Cracked = true,
            AcrcLong = acrcLong,
            AcrcShort = acrcShort,
            AcrcUltLong = _acrcUltLong,
            AcrcUltShort = _acrcUltShort,
            PassedLong = passedLong,
            PassedShort = passedShort,
            UtilLong = _acrcUltLong > 0 ? acrcLong / _acrcUltLong : 0.0,
            UtilShort = _acrcUltShort > 0 ? acrcShort / _acrcUltShort : 0.0,
            Mcrc = mcrc,
            MxCrc = crcRes.Mx,
            MyCrc = crcRes.My,
            CrcConverged = crcRes.Converged,
            EpsMaxTension = crcRes.EpsMaxTension,
            EpsTensionLimit = epsTensionLimit,
            H0 = h0,
            PlaneLong = planeLong,
            SigmaS = sigmaLongKPa,
            SigmaSCrc = sigmaCrcLongKPa,
            SigmaSCrc2 = sigmaCrcShortKPa,
            PsiS = psiS,
            PsiS2 = psiS2,
            Ls = lsM,
            DsEq = dsEq,
            AsTens = asTens,
            Abt = abt,
            Acrc1 = acrc1,
            Acrc2 = acrc2,
            Acrc3 = acrc3
        };
    }
}
