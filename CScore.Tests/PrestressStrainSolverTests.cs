using CScore;
using Xunit;

namespace CScore.Tests;

/// <summary>
/// Тесты сходимости решателя плоскости деформаций для преднапряжённых сечений:
/// начальное приближение обязано учитывать ε_p, а сам Ньютон — не расходиться
/// при заведомо плохом старте.
/// </summary>
public sealed class PrestressStrainSolverTests
{
    [Fact]
    public void Guess_ForZeroLoad_BalancesPrestressWithCompression()
    {
        var section = TestSections.RectWithCentralPrestressedRebar(sigSp: 500.0);
        var strands = section.Areas.Single(area => area.SigSp != 0);
        double prestressForce = strands.Fibers
            .Where(fiber => fiber.TypeFiber == FiberType.point)
            .Sum(fiber => strands.Material!.E * fiber.Area * fiber.Eps_p);
        double ea = new GeoProps(section).EA;

        var guess = section.Guess(new Load { N = 0, Mx = 0, My = 0 });

        Assert.Equal(-prestressForce / ea, guess.e0, 12);
        Assert.Equal(0.0, guess.ky, 12);
        Assert.Equal(0.0, guess.kz, 12);
    }

    [Fact]
    public void Solve_ForEccentricallyPrestressedSection_ConvergesWithProjectNewtonSettings()
    {
        var section = TestSections.RectWithEccentricPrestressedRebar();
        var solver = new StrainSolver(section, CalcType.C, ten: false,
            tol: 0.1, maxIter: 25, h: 1e-7, centralJacobian: false);

        var plane = solver.Solve(nTarget: -500.0, mxTarget: -100.0, myTarget: 25.0);
        var response = section.Integral(plane, CalcType.C, ten: false);

        Assert.True(solver.Converged, $"не сошлось: невязка {solver.Residual:F3} за {solver.Iterations} итераций");
        Assert.InRange(response.N, -500.1, -499.9);
        Assert.InRange(response.Mx, -100.1, -99.9);
        Assert.InRange(response.My, 24.9, 25.1);
    }

    [Fact]
    public void Solve_FromPoorInitialGuess_StillConverges()
    {
        var section = TestSections.RectWithEccentricPrestressedRebar();
        var solver = new StrainSolver(section, CalcType.C, ten: false,
            tol: 0.1, maxIter: 25, h: 1e-7, centralJacobian: false);
        var poorGuess = new Kurvature { e0 = 0.004, ky = -0.02, kz = 0.02 };

        var plane = solver.Solve(nTarget: -500.0, mxTarget: -100.0, myTarget: 25.0, initialGuess: poorGuess);
        var response = section.Integral(plane, CalcType.C, ten: false);

        Assert.True(solver.Converged, $"не сошлось: невязка {solver.Residual:F3} за {solver.Iterations} итераций");
        Assert.InRange(response.N, -500.1, -499.9);
        Assert.InRange(response.Mx, -100.1, -99.9);
        Assert.InRange(response.My, 24.9, 25.1);
    }
}
