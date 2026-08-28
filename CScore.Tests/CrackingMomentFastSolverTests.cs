using System;
using CScore;
using Xunit;

namespace CScore.Tests;

/// <summary>
/// Момент трещинообразования ищется быстрым Ньютоном с пином на управляющей точке
/// (<see cref="TensionPinSolverFast"/>), а внешняя бисекция по масштабу момента осталась
/// запасным путём. Прежняя схема опиралась на сходимость дотрещинной модели в КАЖДОЙ пробной
/// точке диапазона: у сильно обжатых сечений часть проб не сходится, а правило «не сошлось →
/// предел уже превышен» сжимало интервал вниз. Итог был либо отказом, либо — что хуже — молча
/// неверным почти нулевым моментом при <c>Converged = true</c>.
/// </summary>
public class CrackingMomentFastSolverTests
{
    const double N = -100.0, Mx = -100.0, My = 20.0;

    static (CrackingSolver Solver, CrossSection Section) Build(double sigSp)
    {
        var section = TestSections.RectWithEccentricPrestressedRebar(sigSp: sigSp);
        var solver = new CrackingSolver(section, CalcType.N, solverTol: 0.1, solverMaxIter: 25,
            tensionZone: CrackingSolver.LoadedTensionZone(section, N, Mx, My, nAtZeroMoment: N));
        return (solver, section);
    }

    /// <summary>
    /// σsp = 1000 МПа — случай тихо неверного ответа: бисекция рапортовала Converged = true при
    /// Mx = −1,99, хотя деформация в найденной плоскости была −5,0e-4 при пределе +1,5e-4, то
    /// есть грань вообще сжата.
    /// </summary>
    [Fact]
    public void HeavyPrestress_DoesNotReportNearZeroMoment()
    {
        var (solver, _) = Build(1000.0);

        var result = solver.CrackingMoment(N, Mx, My);

        Assert.True(result.Converged);
        Assert.True(Math.Abs(result.Mx) > 100.0, $"Mx={result.Mx:F3}");
        Assert.Equal(solver.TensionLimit(), result.EpsMaxTension, 6);
    }

    /// <summary>Уровни обжатия, на которых прежняя схема просто отказывала.</summary>
    [Theory]
    [InlineData(900.0)]
    [InlineData(1200.0)]
    public void PrestressLevelsThatUsedToFail_NowConverge(double sigSp)
    {
        var (solver, _) = Build(sigSp);

        var result = solver.CrackingMoment(N, Mx, My);

        Assert.True(result.Converged, $"σsp={sigSp}: расчёт по-прежнему не даёт ответа");
        Assert.True(Math.Abs(result.Mx) > 100.0, $"Mx={result.Mx:F3}");
        Assert.Equal(solver.TensionLimit(), result.EpsMaxTension, 6);
    }

    /// <summary>
    /// На сечениях, где работали оба пути, ответ обязан остаться прежним по существу: предел
    /// достигается в зоне догружения, а расхождение с бисекцией — доли процента.
    /// </summary>
    [Theory]
    [InlineData(0.0, -47.68)]
    [InlineData(300.0, -95.12)]
    [InlineData(600.0, -141.45)]
    public void HealthySections_AgreeWithTheBisectionAnswer(double sigSp, double bisectionMx)
    {
        var (solver, _) = Build(sigSp);

        var result = solver.CrackingMoment(N, Mx, My);

        Assert.True(result.Converged);
        Assert.Equal(solver.TensionLimit(), result.EpsMaxTension, 6);
        Assert.True(Math.Abs(result.Mx - bisectionMx) <= Math.Abs(bisectionMx) * 0.01,
            $"σsp={sigSp}: {result.Mx:F3} против {bisectionMx:F3} у прежней бисекции");
    }

    /// <summary>Результат не зависит от того, какой магнитудой передано направление момента.</summary>
    [Fact]
    public void Result_IsIndependentOfTheDirectionMagnitude()
    {
        var (solver, _) = Build(600.0);

        var unit = solver.CrackingMoment(N, -1.0, 0.2);
        var raw = solver.CrackingMoment(N, -1000.0, 200.0);

        Assert.True(unit.Converged && raw.Converged);
        Assert.Equal(unit.Mx, raw.Mx, 4);
        Assert.Equal(unit.My, raw.My, 4);
    }

    /// <summary>
    /// Запасной путь обязан остаться рабочим сам по себе: экземпляр без быстрого решателя —
    /// ровно тот, что создаёт <see cref="TensionPinSolverFast"/> в своём fallback, и рекурсии
    /// между ними быть не должно.
    /// </summary>
    [Fact]
    public void BisectionOnly_StillWorksOnHealthySections()
    {
        var section = TestSections.RectWithEccentricPrestressedRebar(sigSp: 300.0);
        var solver = new CrackingSolver(section, CalcType.N, solverTol: 0.1, solverMaxIter: 25,
            tensionZone: CrackingSolver.LoadedTensionZone(section, N, Mx, My, nAtZeroMoment: N),
            allowPinSolver: false);

        var result = solver.CrackingMoment(N, Mx, My);

        Assert.True(result.Converged);
        Assert.True(Math.Abs(result.Mx - (-95.12)) <= 1.0, $"Mx={result.Mx:F3}");
    }
}
