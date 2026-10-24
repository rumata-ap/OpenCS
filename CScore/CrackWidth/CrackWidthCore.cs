using System;
using System.Collections.Generic;
using System.Linq;

namespace CScore;

/// <summary>
/// Ядро ширины раскрытия нормальных трещин (СП 63.13330 пп. 8.2.15–8.2.18) при известной
/// точке трещинообразования: пост-трещинные плоскости, σs, ψs, ls, acrc1/2/3. Общее для
/// <see cref="CrackWidthSolver"/> (точка трещинообразования — <see cref="CrackingSolver"/>) и
/// поверхности ширины раскрытия (точка — по телу ПС2). Единицы: кН, кН·м, м, кПа; ширины — мм.
/// Требует, чтобы <see cref="CrossSection.ResolveAndBuildDiagramms"/> уже был вызван.
/// Не потокобезопасно: расчёт пишет деформации в волокна сечения.
/// </summary>
public sealed class CrackWidthCore
{
    readonly CrossSection _section;
    readonly CalcType _calcService;
    readonly CalcType _calcServiceLong;
    readonly double _phi2;
    readonly double _acrcUltLong;
    readonly double _acrcUltShort;
    readonly double _sp63EtaMin;
    readonly double _ekbEtaMin;
    readonly double _solverTol;
    readonly PsiSMethod _psiSMethod;

    public CrackWidthCore(CrossSection section, CalcType calcService, CalcType calcServiceLong, double phi2,
        double acrcUltLong, double acrcUltShort, PsiSMethod psiSMethod, double sp63EtaMin, double ekbEtaMin,
        double solverTol)
    {
        _section = section ?? throw new ArgumentNullException(nameof(section));
        _calcService = calcService;
        _calcServiceLong = calcServiceLong;
        _phi2 = phi2;
        _acrcUltLong = acrcUltLong;
        _acrcUltShort = acrcUltShort;
        _psiSMethod = psiSMethod;
        _sp63EtaMin = sp63EtaMin;
        _ekbEtaMin = ekbEtaMin;
        _solverTol = solverTol;
    }

    /// <summary>Предельная ширина продолжительного раскрытия, мм.</summary>
    public double AcrcUltLong => _acrcUltLong;
    /// <summary>Предельная ширина непродолжительного раскрытия, мм.</summary>
    public double AcrcUltShort => _acrcUltShort;

    /// <summary>
    /// Достраивает состояние трещинообразования по точке (N, Mx,crc, My,crc): пост-трещинные
    /// плоскости на длительной и кратковременной диаграммах и σs,crc по ним. <c>null</c> —
    /// длительная пост-трещинная плоскость не сошлась.
    /// </summary>
    /// <param name="crackPlane">Дотрещинная плоскость в точке трещинообразования (ten = true).</param>
    /// <param name="byAxialForce">Точка взята на луче полной нагрузки (трещина от продольной силы).</param>
    public CrackOnset? Onset(double n, double mxCrc, double myCrc, Kurvature crackPlane, bool byAxialForce)
    {
        // σs,crc (п.8.2.18) — напряжение в арматуре В СЕЧЕНИИ С ТРЕЩИНОЙ сразу после
        // образования трещин, т.е. на ПОСТ-трещинной (ten=false) плоскости деформаций при
        // M=Mcrc — а не на исходной (ещё не растрескавшейся, ten=true) плоскости
        // crackPlane, по которой найден сам момент Mcrc. При чтении σs,crc прямо с той
        // (дотрещинной) плоскости бетон продолжает "работать" на растяжение и берёт на себя
        // часть усилия — σs,crc получается заниженным, почти как напряжение ДО образования
        // трещины, а не сразу после (даёт завышенный, слишком оптимистичный ψs и заниженную acrc).
        var elasticGuessCrc = SafeGuess(n, mxCrc, myCrc);

        var planeCrcLong = SolveDamped(n, mxCrc, myCrc, elasticGuessCrc, _calcServiceLong, out var convCrcLong);
        if (!convCrcLong) return null;

        Kurvature planeCrcShort;
        bool converged = true;
        if (_calcServiceLong == _calcService)
        {
            planeCrcShort = planeCrcLong;
        }
        else
        {
            planeCrcShort = SolveDamped(n, mxCrc, myCrc, elasticGuessCrc, _calcService, out var convCrcShort);
            if (!convCrcShort) { planeCrcShort = planeCrcLong; converged = false; } // фолбэк
        }

        // σs,crc считается дважды — на той же диаграмме, что и σs, с которой её сравнивают
        // в формуле ψs = 1 − 0.8·σs,crc/σs (acrc1/acrc3 — длительная диаграмма,
        // acrc2 — кратковременная), иначе ψs сравнивало бы напряжения на разных базисах.
        var (sigmaCrcLong, epsCrcLong) = SigmaSFromPlane(planeCrcLong, _calcServiceLong);
        var (sigmaCrcShort, epsCrcShort) = SigmaSFromPlane(planeCrcShort, _calcService);

        return new CrackOnset(n, mxCrc, myCrc, crackPlane, planeCrcLong, planeCrcShort,
            sigmaCrcLong, epsCrcLong, sigmaCrcShort, epsCrcShort, byAxialForce, converged);
    }

    /// <summary>
    /// Ширина раскрытия трещин при длительных (N, Mx,long, My,long) и полных (N, Mx,total,
    /// My,total) усилиях по готовому состоянию трещинообразования. Поля трещинообразования
    /// результата (Mcrc, CrcConverged, EpsMaxTension…) заполняет вызывающая сторона.
    /// </summary>
    /// <param name="phi3">Коэффициент φ3 (п. 8.2.15); null — по знаку N (N &gt; 0 — растяжение, 1,2).</param>
    /// <param name="seedLong">Начальное приближение длительной плоскости (продолжение по нагрузке); null — упругое.</param>
    /// <param name="seedTotal">То же для полной плоскости.</param>
    public CrackWidthResult Evaluate(CrackOnset onset, double n, double mxLong, double myLong,
        double mxTotal, double myTotal, double? phi3 = null, Kurvature? seedLong = null, Kurvature? seedTotal = null)
    {
        ArgumentNullException.ThrowIfNull(onset);
        double phi3Eff = phi3 ?? (n > 1e-3 ? 1.2 : 1.0);
        var zero = new CrackWidthResult { Cracked = false, AcrcUltLong = _acrcUltLong, AcrcUltShort = _acrcUltShort };

        // Длительная нагрузка ниже Mcrc (трещины образует только полная): по (8.120) при
        // acrc1 = acrc3 = 0 непродолжительное раскрытие равно acrc2. Трещина от продольной силы
        // (ByAxialForce) растрескивает сечение при любом моменте той же N.
        double mcrcOnset = Math.Sqrt(onset.MxCrc * onset.MxCrc + onset.MyCrc * onset.MyCrc);
        bool longCracked = onset.ByAxialForce || Math.Sqrt(mxLong * mxLong + myLong * myLong) > mcrcOnset;

        // Упругая начальная догадка (учитывает армирование — приведённое сечение, см.
        // CrossSection.ElasticProps), затем один демпфированный Ньютон прямо к цели с
        // ten=false — так же, как это делает референсный python-солвер (solve_biaxial):
        // без промежуточной рампы, полагаясь на backtracking line search.
        Kurvature? planeLong = null;
        if (longCracked)
        {
            planeLong = SolveDamped(n, mxLong, myLong, seedLong ?? SafeGuess(n, mxLong, myLong),
                _calcServiceLong, out bool convLong);
            if (!convLong) return zero;
        }

        Kurvature planeTotal;
        if (planeLong.HasValue && Math.Abs(mxTotal - mxLong) < 1e-12 && Math.Abs(myTotal - myLong) < 1e-12)
        {
            planeTotal = planeLong.Value;
        }
        else
        {
            planeTotal = SolveDamped(n, mxTotal, myTotal, seedTotal ?? SafeGuess(n, mxTotal, myTotal),
                _calcService, out var convTotal);
            if (!convTotal)
            {
                if (!planeLong.HasValue) return zero;
                planeTotal = planeLong.Value; // фолбэк для σs,total
            }
        }

        // Растянутая арматура, ls и Abt — по длительной плоскости, а без трещин от длительной
        // нагрузки — по полной.
        var tensPlane = planeLong ?? planeTotal;
        var tensCalc = planeLong.HasValue ? _calcServiceLong : _calcService;
        var (sigmaTensKPa, epsTens, asTens, dsEq, tensCentroid) = TensileRebarProps(_section, tensPlane, tensCalc);
        if (asTens < 1e-15 || dsEq < 1e-15) return zero;
        double sigmaLongKPa = planeLong.HasValue ? sigmaTensKPa : 0.0;
        double epsLong = planeLong.HasValue ? epsTens : 0.0;

        var geom = CrackSectionGeometry.Compute(_section, onset.CrackPlane, tensPlane, tensCentroid);
        double abt = geom.Abt;
        if (abt < 1e-15) return zero;

        double lsM = 0.5 * abt / asTens * dsEq;
        double lsMin = Math.Max(10.0 * dsEq, 0.10);
        double lsMax = Math.Min(40.0 * dsEq, 0.40);
        lsM = Math.Max(lsMin, Math.Min(lsMax, lsM));

        var (sigmaTotalKPa, epsTotal) = SigmaSFromPlane(planeTotal, _calcService);

        double esKPa = RebarEsKPa(_section);

        var (acrc1, psiS) = planeLong.HasValue
            ? SingleAcrc(sigmaLongKPa, onset.SigmaCrcLong, epsLong, onset.EpsCrcLong, lsM, phi1: 1.4, phi3: phi3Eff, esKPa: esKPa)
            : (0.0, 0.0);
        var (acrc3, _) = planeLong.HasValue
            ? SingleAcrc(sigmaLongKPa, onset.SigmaCrcLong, epsLong, onset.EpsCrcLong, lsM, phi1: 1.0, phi3: phi3Eff, esKPa: esKPa)
            : (0.0, 0.0);
        var (acrc2, psiS2) = SingleAcrc(sigmaTotalKPa, onset.SigmaCrcShort, epsTotal, onset.EpsCrcShort, lsM, phi1: 1.0, phi3: phi3Eff, esKPa: esKPa);

        var acrcByRebar = ComputeAcrcByRebar(
            planeLong, onset.PlaneCrcLong, _calcServiceLong,
            planeTotal, onset.PlaneCrcShort, _calcService,
            lsM, phi3: phi3Eff, esKPa: esKPa);

        double acrcLong = acrc1;
        double acrcShort = acrc1 + acrc2 - acrc3;

        return new CrackWidthResult
        {
            Cracked = true,
            AcrcLong = acrcLong,
            AcrcShort = acrcShort,
            AcrcUltLong = _acrcUltLong,
            AcrcUltShort = _acrcUltShort,
            PassedLong = acrcLong <= _acrcUltLong,
            PassedShort = acrcShort <= _acrcUltShort,
            UtilLong = _acrcUltLong > 0 ? acrcLong / _acrcUltLong : 0.0,
            UtilShort = _acrcUltShort > 0 ? acrcShort / _acrcUltShort : 0.0,
            H0 = geom.H0,
            H = geom.H,
            Xt = geom.Xt,
            PlaneLong = planeLong,
            PlaneTotal = planeTotal,
            SigmaS = sigmaLongKPa,
            SigmaSCrc = onset.SigmaCrcLong,
            SigmaSCrc2 = onset.SigmaCrcShort,
            PsiS = psiS,
            PsiS2 = psiS2,
            Ls = lsM,
            DsEq = dsEq,
            AsTens = asTens,
            Abt = abt,
            Acrc1 = acrc1,
            Acrc2 = acrc2,
            Acrc3 = acrc3,
            AcrcByRebar = acrcByRebar
        };
    }

    Kurvature SafeGuess(double n, double mx, double my)
    {
        var g = _section.Guess(new Load { N = n, Mx = mx, My = my });
        if (!double.IsFinite(g.e0)) g.e0 = 0;
        if (!double.IsFinite(g.ky)) g.ky = 0;
        if (!double.IsFinite(g.kz)) g.kz = 0;
        return g;
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
            // растяжение бетона уже учтено при поиске точки трещинообразования (ten=true).
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

    internal static bool IsRebar(MaterialArea area) =>
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
    /// Центр тяжести растянутой арматуры (для a и h0, см. <see cref="CrackSectionGeometry"/>) —
    /// геометрическая величина, взвешиванию не подлежит, считается по полной площади.
    /// </para>
    /// </summary>
    (double sigmaMaxKPa, double epsAtSigmaMax, double asTens, double dsEq, (double X, double Y) centroid)
        TensileRebarProps(CrossSection section, Kurvature k, CalcType calcType)
    {
        double sigmaMax = 0.0, epsAtSigmaMax = 0.0;

        // Первый проход: найти σ_max среди растянутых стержней — база для веса σi/σ_max.
        foreach (var (area, ka) in section.EnumerateAreas(k))
        {
            if (!IsRebar(area)) continue;
            var ownDiagrams = area.Material!.GetDiagramms(area.DiagrammType, _sp63EtaMin, _ekbEtaMin);
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

        if (sigmaMax < 1e-9) return (0.0, 0.0, 0.0, 0.0, (0.0, 0.0));

        // Второй проход: взвешенные As_tens/ds_eq (вес σi/σ_max) и "чистый" (без
        // взвешивания) центр тяжести растянутой арматуры.
        double asWeighted = 0.0, asDWeighted = 0.0;
        double asPlain = 0.0, axPlain = 0.0, ayPlain = 0.0;

        foreach (var (area, ka) in section.EnumerateAreas(k))
        {
            if (!IsRebar(area)) continue;
            var ownDiagrams = area.Material!.GetDiagramms(area.DiagrammType, _sp63EtaMin, _ekbEtaMin);
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
                axPlain += f.Area * f.X;
                ayPlain += f.Area * f.Y;
            }
        }

        if (asWeighted < 1e-15) return (0.0, 0.0, 0.0, 0.0, (0.0, 0.0));

        double dsEq = asDWeighted / asWeighted;
        return (sigmaMax, epsAtSigmaMax, asTens: asWeighted, dsEq, (axPlain / asPlain, ayPlain / asPlain));
    }

    (double sigmaMaxKPa, double epsAtSigmaMax) SigmaSFromPlane(Kurvature k, CalcType calcType)
    {
        double sigmaMax = 0.0, epsAtSigmaMax = 0.0;
        foreach (var (area, ka) in _section.EnumerateAreas(k))
        {
            if (!IsRebar(area)) continue;
            var ownDiagrams = area.Material!.GetDiagramms(area.DiagrammType, _sp63EtaMin, _ekbEtaMin);
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

    /// <summary>ε (без диаграммы, чисто по плоскости) для каждого стержня — вспомогательный проход для <see cref="ComputeAcrcByRebar"/>.</summary>
    Dictionary<Fiber, double> EpsByFiber(Kurvature plane)
    {
        var map = new Dictionary<Fiber, double>(ReferenceEqualityComparer.Instance);
        foreach (var (area, ka) in _section.EnumerateAreas(plane))
        {
            if (!IsRebar(area)) continue;
            foreach (var f in area.Fibers)
            {
                if (f.TypeFiber != FiberType.point) continue;
                map[f] = ka.e0 + ka.ky * f.Y + ka.kz * f.X;
            }
        }
        return map;
    }

    /// <summary>
    /// Ширина раскрытия трещины (длительная и кратковременная) по КАЖДОМУ растянутому
    /// стержню отдельно — внормативное расширение сверх п.8.2.15 (см. <see cref="RebarAcrcEntry"/>).
    /// Общий <paramref name="lsM"/>, но собственные σs/εs/ψs в точке каждого стержня — для
    /// длительной и кратковременной части на СВОИХ плоскостях/диаграммах, зеркалируя пары
    /// (planeLong,planeCrcLong,calcServiceLong) / (planeTotal,planeCrcShort,calcService),
    /// которыми на уровне сечения считаются Acrc1/Acrc3 (φ1=1.4/1.0) и Acrc2 (φ1=1.0).
    /// Переиспользует <see cref="SingleAcrc"/>, поэтому уважает выбранный <see cref="_psiSMethod"/>.
    /// EnumerateAreas даёt локально скорректированную под КОНКРЕТНУЮ плоскость ka, поэтому
    /// каждая из 4 плоскостей требует своего прохода — ε сопоставляются по ссылке на Fiber.
    /// </summary>
    List<RebarAcrcEntry> ComputeAcrcByRebar(
        Kurvature? planeLong, Kurvature planeCrcLong, CalcType calcServiceLong,
        Kurvature planeTotal, Kurvature planeCrcShort, CalcType calcService,
        double lsM, double phi3, double esKPa)
    {
        if (!planeLong.HasValue)
            return ComputeShortAcrcByRebar(planeTotal, planeCrcShort, calcService, lsM, phi3, esKPa);

        var epsCrcLongByFiber = EpsByFiber(planeCrcLong);
        var epsTotalByFiber = EpsByFiber(planeTotal);
        var epsCrcShortByFiber = EpsByFiber(planeCrcShort);

        var result = new List<RebarAcrcEntry>();
        foreach (var (area, ka) in _section.EnumerateAreas(planeLong.Value))
        {
            if (!IsRebar(area)) continue;
            var ownDiagrams = area.Material!.GetDiagramms(area.DiagrammType, _sp63EtaMin, _ekbEtaMin);
            if (ownDiagrams == null || !ownDiagrams.TryGetValue(calcServiceLong, out var ownDgrLong)) continue;
            bool hasShortDgr = ownDiagrams.TryGetValue(calcService, out var ownDgrShort);

            foreach (var f in area.Fibers)
            {
                if (f.TypeFiber != FiberType.point) continue;
                double epsLong = ka.e0 + ka.ky * f.Y + ka.kz * f.X;
                if (epsLong <= 0.0) continue; // сжат при длительной нагрузке — ширина трещины не определена

                double sigLong = ownDgrLong.Sig(epsLong, out _);
                if (sigLong <= 0.0) continue;

                double epsCrcLongRaw = epsCrcLongByFiber.TryGetValue(f, out var ecl) ? ecl : 0.0;
                double epsCrcLong = Math.Max(epsCrcLongRaw, 0.0);
                double sigCrcLong = epsCrcLong > 0.0 ? ownDgrLong.Sig(epsCrcLong, out _) : 0.0;

                var (acrcLongMm, psiSj) = SingleAcrc(sigLong, sigCrcLong, epsLong, epsCrcLong, lsM, phi1: 1.4, phi3, esKPa);
                var (acrc3Mm, _) = SingleAcrc(sigLong, sigCrcLong, epsLong, epsCrcLong, lsM, phi1: 1.0, phi3, esKPa);

                double acrc2Mm = 0.0, psiS2j = 1.0;
                if (hasShortDgr)
                {
                    double epsTotal = epsTotalByFiber.TryGetValue(f, out var et) ? et : 0.0;
                    if (epsTotal > 0.0)
                    {
                        double sigTotal = ownDgrShort!.Sig(epsTotal, out _);
                        if (sigTotal > 0.0)
                        {
                            double epsCrcShortRaw = epsCrcShortByFiber.TryGetValue(f, out var ecs) ? ecs : 0.0;
                            double epsCrcShort = Math.Max(epsCrcShortRaw, 0.0);
                            double sigCrcShort = epsCrcShort > 0.0 ? ownDgrShort.Sig(epsCrcShort, out _) : 0.0;
                            (acrc2Mm, psiS2j) = SingleAcrc(sigTotal, sigCrcShort, epsTotal, epsCrcShort, lsM, phi1: 1.0, phi3, esKPa);
                        }
                    }
                }

                double acrcShortMm = acrcLongMm + acrc2Mm - acrc3Mm;
                result.Add(new RebarAcrcEntry(f.X, f.Y, f.Diameter, epsLong, epsCrcLong, sigLong, sigCrcLong,
                    psiSj, acrcLongMm, psiS2j, acrcShortMm));
            }
        }
        return result;
    }

    /// <summary>
    /// По-стержневая ширина раскрытия, когда трещины образует только полная нагрузка: длительная
    /// часть нулевая (σ, ε, ψs длительной — 0), непродолжительная — acrc2 стержня.
    /// </summary>
    List<RebarAcrcEntry> ComputeShortAcrcByRebar(Kurvature planeTotal, Kurvature planeCrcShort, CalcType calcService,
        double lsM, double phi3, double esKPa)
    {
        var epsCrcShortByFiber = EpsByFiber(planeCrcShort);
        var result = new List<RebarAcrcEntry>();
        foreach (var (area, ka) in _section.EnumerateAreas(planeTotal))
        {
            if (!IsRebar(area)) continue;
            var ownDiagrams = area.Material!.GetDiagramms(area.DiagrammType, _sp63EtaMin, _ekbEtaMin);
            if (ownDiagrams == null || !ownDiagrams.TryGetValue(calcService, out var ownDgrShort)) continue;

            foreach (var f in area.Fibers)
            {
                if (f.TypeFiber != FiberType.point) continue;
                double epsTotal = ka.e0 + ka.ky * f.Y + ka.kz * f.X;
                if (epsTotal <= 0.0) continue;
                double sigTotal = ownDgrShort.Sig(epsTotal, out _);
                if (sigTotal <= 0.0) continue;

                double epsCrcShort = Math.Max(epsCrcShortByFiber.TryGetValue(f, out var ecs) ? ecs : 0.0, 0.0);
                double sigCrcShort = epsCrcShort > 0.0 ? ownDgrShort.Sig(epsCrcShort, out _) : 0.0;
                var (acrc2Mm, psiS2j) = SingleAcrc(sigTotal, sigCrcShort, epsTotal, epsCrcShort, lsM, phi1: 1.0, phi3, esKPa);
                result.Add(new RebarAcrcEntry(f.X, f.Y, f.Diameter, 0.0, 0.0, 0.0, 0.0, 0.0, 0.0, psiS2j, acrc2Mm));
            }
        }
        return result;
    }

    static double RebarEsKPa(CrossSection section)
    {
        foreach (var area in section.Areas)
            if (IsRebar(area) && area.Material != null && area.Material.E > 0)
                return area.Material.E;
        return 2.0e8; // фолбэк 200 ГПа (кПа), если в сечении почему-то не нашли арматуру
    }
}
