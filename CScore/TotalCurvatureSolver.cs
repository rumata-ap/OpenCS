using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

namespace CScore;

/// <summary>Результат одной стадии расчёта полной кривизны.</summary>
public sealed class CurvatureStageResult
{
    /// <summary>Целевая нормальная сила стадии, кН.</summary>
    public double N { get; set; }

    /// <summary>Целевой момент относительно X стадии, кН·м.</summary>
    public double Mx { get; set; }

    /// <summary>Целевой момент относительно Y стадии, кН·м.</summary>
    public double My { get; set; }

    /// <summary>Найденная плоскость деформаций.</summary>
    public Kurvature Plane { get; set; }

    /// <summary>Признак сходимости решателя стадии.</summary>
    public bool Converged { get; set; }
}

/// <summary>
/// Результат расчёта полной кривизны по формулам 8.140/8.141 СП 63.13330.
/// </summary>
public sealed class TotalCurvatureResult
{
    public bool Cracked { get; set; }
    public double Mcrc { get; set; }
    public double MxCrc { get; set; }
    public double MyCrc { get; set; }
    public bool CrcConverged { get; set; }

    public CurvatureStageResult? Stage1 { get; set; }
    public CurvatureStageResult? Stage2 { get; set; }
    public CurvatureStageResult? Stage3 { get; set; }

    public double KyFull { get; set; }
    public double KzFull { get; set; }
    public double KFull => Math.Sqrt(KyFull * KyFull + KzFull * KzFull);
    public bool AllConverged { get; set; }
}

/// <summary>
/// Рассчитывает полную кривизну железобетонного сечения по п. 8.2.23–8.2.32
/// СП 63.13330. Работает через общий интеграл сечения и не зависит от типа диаграммы.
/// </summary>
public sealed class TotalCurvatureSolver
{
    readonly CrossSection _section;
    readonly CalcType _calcCrc;
    readonly CalcType _calcService;
    readonly CalcType _calcServiceLong;
    readonly double _solverTol;
    readonly int _solverMaxIter;
    readonly double _solverH;
    readonly bool _centralJacobian;

    public TotalCurvatureSolver(
        CrossSection section,
        CalcType calcCrc = CalcType.N,
        CalcType calcService = CalcType.N,
        CalcType calcServiceLong = CalcType.NL,
        double solverTol = 0.5,
        int solverMaxIter = 80,
        double solverH = 1e-7,
        bool centralJacobian = true)
    {
        _section = section ?? throw new ArgumentNullException(nameof(section));
        _calcCrc = calcCrc;
        _calcService = calcService;
        _calcServiceLong = calcServiceLong;
        _solverTol = solverTol;
        _solverMaxIter = solverMaxIter;
        _solverH = solverH;
        _centralJacobian = centralJacobian;
    }

    public TotalCurvatureResult Compute(
        double N, double mxLong, double myLong, double mxTotal, double myTotal)
    {
        var direction = MomentDirection(mxLong, myLong, mxTotal, myTotal);
        var crcSolver = new CrackingSolver(_section, _calcCrc,
            solverTol: _solverTol,
            solverMaxIter: _solverMaxIter,
            solverH: _solverH);
        var crcRes = crcSolver.CrackingMoment(N, direction.mx, direction.my);

        var result = new TotalCurvatureResult
        {
            Mcrc = Math.Sqrt(crcRes.Mx * crcRes.Mx + crcRes.My * crcRes.My),
            MxCrc = crcRes.Mx,
            MyCrc = crcRes.My,
            CrcConverged = crcRes.Converged
        };

        if (!crcRes.Converged)
            return result;

        double totalMoment = Math.Sqrt(mxTotal * mxTotal + myTotal * myTotal);
        result.Cracked = totalMoment > result.Mcrc;

        if (!result.Cracked)
        {
            result.Stage1 = SolveUncrackedStage(N, mxTotal - mxLong, myTotal - myLong, _calcService);
            result.Stage2 = SolveUncrackedStage(N, mxLong, myLong, _calcServiceLong);
            result.KyFull = result.Stage1.Plane.ky + result.Stage2.Plane.ky;
            result.KzFull = result.Stage1.Plane.kz + result.Stage2.Plane.kz;
            result.AllConverged = result.Stage1.Converged && result.Stage2.Converged;
            return result;
        }

        var elasticGuess = FiniteGuess(_section.Guess(new Load { N = N, Mx = mxTotal, My = myTotal }));
        const bool ten = false;
        const bool ca = true;

        var shortCrcSolver = new StrainSolver(_section, _calcService, ten, ca,
            tol: _solverTol, maxIter: _solverMaxIter, h: _solverH,
            centralJacobian: _centralJacobian);
        var planeCrcShort = shortCrcSolver.Solve(N, crcRes.Mx, crcRes.My, elasticGuess);
        if (!shortCrcSolver.Converged)
            return result;

        var longCrcSolver = new StrainSolver(_section, _calcServiceLong, ten, ca,
            tol: _solverTol, maxIter: _solverMaxIter, h: _solverH,
            centralJacobian: _centralJacobian);
        var planeCrcLong = longCrcSolver.Solve(N, crcRes.Mx, crcRes.My, elasticGuess);
        if (!longCrcSolver.Converged)
            return result;

        var epsCrcShort = EpsCrcByFiber(_section, planeCrcShort);
        var epsCrcLong = EpsCrcByFiber(_section, planeCrcLong);

        result.Stage2 = SolveCrackedStage(N, mxLong, myLong, planeCrcShort,
            _calcService, epsCrcShort);
        if (!result.Stage2.Converged)
            return result;

        result.Stage1 = SolveCrackedStage(N, mxTotal, myTotal, result.Stage2.Plane,
            _calcService, epsCrcShort);
        if (!result.Stage1.Converged)
            return result;

        result.Stage3 = SolveCrackedStage(N, mxLong, myLong, planeCrcLong,
            _calcServiceLong, epsCrcLong);
        if (!result.Stage3.Converged)
            return result;

        result.KyFull = result.Stage1.Plane.ky - result.Stage2.Plane.ky + result.Stage3.Plane.ky;
        result.KzFull = result.Stage1.Plane.kz - result.Stage2.Plane.kz + result.Stage3.Plane.kz;
        result.AllConverged = true;
        return result;
    }

    CurvatureStageResult SolveUncrackedStage(double n, double mx, double my, CalcType calc)
    {
        var solver = new StrainSolver(_section, calc, ten: true, ca: true,
            tol: _solverTol, maxIter: _solverMaxIter, h: _solverH,
            centralJacobian: _centralJacobian);
        var plane = solver.Solve(n, mx, my);
        return new CurvatureStageResult
        {
            N = n,
            Mx = mx,
            My = my,
            Plane = plane,
            Converged = solver.Converged
        };
    }

    CurvatureStageResult SolveCrackedStage(
        double n, double mx, double my, Kurvature seed, CalcType calc,
        IReadOnlyDictionary<Fiber, double> epsCrc)
    {
        const bool ten = false;
        const bool ca = true;
        var solver = new StrainSolver(_section, calc, ten, ca,
            tol: _solverTol, maxIter: _solverMaxIter, h: _solverH,
            centralJacobian: _centralJacobian,
            evaluate: k => Curvature8232.ApplyPsiCorrection(
                _section, k, _section.Integral(k, calc, ten, ca), epsCrc));
        var plane = solver.Solve(n, mx, my, seed);
        return new CurvatureStageResult
        {
            N = n,
            Mx = mx,
            My = my,
            Plane = plane,
            Converged = solver.Converged
        };
    }

    static (double mx, double my) MomentDirection(
        double mxLong, double myLong, double mxTotal, double myTotal)
    {
        if (Math.Abs(mxTotal) > 1e-12 || Math.Abs(myTotal) > 1e-12)
            return (mxTotal, myTotal);
        if (Math.Abs(mxLong) > 1e-12 || Math.Abs(myLong) > 1e-12)
            return (mxLong, myLong);
        return (1.0, 0.0);
    }

    static Kurvature FiniteGuess(Kurvature guess)
    {
        if (!double.IsFinite(guess.e0)) guess.e0 = 0.0;
        if (!double.IsFinite(guess.ky)) guess.ky = 0.0;
        if (!double.IsFinite(guess.kz)) guess.kz = 0.0;
        return guess;
    }

    static Dictionary<Fiber, double> EpsCrcByFiber(CrossSection section, Kurvature plane)
    {
        var map = new Dictionary<Fiber, double>(ReferenceEqualityComparer.Instance);
        foreach (var (area, ka) in section.EnumerateAreas(plane))
        {
            if (area.Material?.Type is not (MatType.ReSteelF or MatType.ReSteelU))
                continue;

            foreach (var fiber in area.Fibers)
            {
                if (fiber.TypeFiber != FiberType.point)
                    continue;
                map[fiber] = ka.e0 + ka.ky * fiber.Y + ka.kz * fiber.X + fiber.Eps_p;
            }
        }
        return map;
    }
}
