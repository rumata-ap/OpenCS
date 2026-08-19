using CScore;
using Xunit;

namespace CScore.Tests;

public class TensionPinSolverFastTests
{
    // Уточнение по ревью плана (P1-2): epsPin=0 при ненулевом целевом моменте в общем случае НЕ
    // ИМЕЕТ решения для пин-Ньютона (весь контур по одну сторону нуля) — точка "0" диаграммы
    // решается ОТДЕЛЬНЫМ одномерным равновесием (см. BiaxialCurvatureCurveSolver, Task 6), не
    // через этот класс. Тест ниже проверяет Solve на МАЛОМ, но НЕНУЛЕВОМ epsPin (внутри
    // диапазона (0, TensionLimit())), где решение определённо существует.
    [Fact]
    public void Solve_AtSmallNonZeroEpsPin_Converges()
    {
        var section = TestSections.Example47();
        var solver = new TensionPinSolverFast(section, CalcType.N);
        var legacy = new CrackingSolver(section, CalcType.N);
        double limit = legacy.TensionLimit();

        var result = solver.Solve(epsPin: limit * 0.3, n: 0.0, mx: -60.0, my: 0.0, dNdk: 0.0, seed: null);

        Assert.True(result.Converged);
    }

    [Fact]
    public void Solve_AtTensionLimit_MatchesLegacyCrackingSolver()
    {
        var section = TestSections.Example47();
        var solver = new TensionPinSolverFast(section, CalcType.N);
        var legacy = new CrackingSolver(section, CalcType.N);

        double limit = legacy.TensionLimit();
        var fast = solver.Solve(epsPin: limit, n: 0.0, mx: -60.0, my: 0.0, dNdk: 0.0, seed: null);
        var slow = legacy.CrackingCurvature(0.0, -60.0, 0.0);

        Assert.True(fast.Converged);
        Assert.True(slow.Converged);
        // Пин-Ньютон и бисекционный CrackingSolver сходятся с разными допусками
        // (относительный ResidualTol=1e-3 у PinnedEquilibriumNewton vs абсолютный bisectTol
        // у CrackingSolver) — сравнение по относительной невязке, не по фиксированным знакам.
        Assert.True(Math.Abs(slow.Mx - fast.Load.Mx) <= Math.Abs(slow.Mx) * 0.01);
        Assert.True(Math.Abs(slow.My - fast.Load.My) <= Math.Max(Math.Abs(slow.My) * 0.01, 0.5));
    }

    [Fact]
    public void Solve_WhenNewtonDiverges_FallsBackToLegacySolver()
    {
        var section = TestSections.Example47();
        var solver = new TensionPinSolverFast(section, CalcType.N, newtonMaxIter: 1); // форсируем срыв
        var legacy = new CrackingSolver(section, CalcType.N);
        double limit = legacy.TensionLimit(); // epsPin строго внутри диапазона (0, TensionLimit()),
                                                // а не произвольное число — иначе fallback-бисекция
                                                // тоже может не сойтись.

        var result = solver.Solve(epsPin: limit * 0.5, n: 0.0, mx: -60.0, my: 0.0, dNdk: 0.0, seed: null);

        Assert.True(result.Converged);
        Assert.True(result.UsedFallback);
    }
}
