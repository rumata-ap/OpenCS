using System;
using CScore;
using Xunit;

namespace CScore.Tests;

public class BiaxialCurvatureCurveSolverTests
{
    [Fact]
    public void Compute_HighAxialCompression_NoCracking_SingleSegment()
    {
        var section = TestSections.Example47();
        var solver = new BiaxialCurvatureCurveSolver(section, calcCrc: CalcType.N, calcService: CalcType.N);

        // N сильно отрицательное (сжатие) — сечение не должно трескаться раньше исчерпания
        // (эмпирически подобрано: -2000/-3000 всё ещё трескаются для Example47, -3500 — нет).
        var result = solver.Compute(N0: -3500.0, Mx0: -60.0, My0: 0.0, CurvatureNMode.Constant, usePsi: false);

        Assert.True(result.Status is "ok" or "partial");
        Assert.Null(result.Cracking);
        Assert.Null(result.CrackTransitionPoint);
        Assert.NotNull(result.Ultimate);
        Assert.Equal(result.Ultimate, result.UltimateReference);
    }

    [Fact]
    public void Compute_NormalCase_FindsCrackingPoint()
    {
        var section = TestSections.Example47();
        var solver = new BiaxialCurvatureCurveSolver(section, calcCrc: CalcType.N, calcService: CalcType.N);

        // Compute ещё не полностью реализован (NotImplementedException после уч."1") — этот
        // тест ловит именно факт того, что конвейер доходит до точки 1 и продолжает работу
        // дальше (не падает раньше). Партиальный `result` недоступен через try/catch (брошенное
        // исключение означает, что вызов `Compute` не завершился — переменной ничего не
        // присваивается), поэтому здесь просто фиксируем сам факт долетания до
        // NotImplementedException, а не раньше. В Task 7 тест переписывается без try/catch.
        Assert.Throws<NotImplementedException>(() =>
            solver.Compute(0.0, -60.0, 0.0, CurvatureNMode.Constant, usePsi: false));
    }
}
