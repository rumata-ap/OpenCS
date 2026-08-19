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

    // Compute ещё не полностью реализован дальше точки 2/петли (NotImplementedException после
    // них, конкретно с текстом "Task 8-9" — см. BiaxialCurvatureCurveSolver.Compute). Партиальный
    // result недоступен через try/catch (брошенное исключение означает, что вызов Compute не
    // завершился), поэтому здесь фиксируется факт долетания конвейера именно до конца петли
    // (не раньше — иначе сообщение исключения было бы другим/его не было бы вовсе). В Task 8
    // тест переписывается на проверку полного результата без Assert.Throws.
    [Fact]
    public void Compute_NormalCase_ReachesTransitionPointBeforeThrowing()
    {
        var section = TestSections.Example47();
        var solver = new BiaxialCurvatureCurveSolver(section, calcCrc: CalcType.N, calcService: CalcType.N);

        var ex = Assert.Throws<NotImplementedException>(() =>
            solver.Compute(0.0, -60.0, 0.0, CurvatureNMode.Constant, usePsi: false));
        Assert.Contains("Task 8-9", ex.Message);
    }

    [Fact]
    public void Compute_ProportionalMode_ReachesTransitionPointBeforeThrowing()
    {
        var section = TestSections.Example47();
        var solver = new BiaxialCurvatureCurveSolver(section, calcCrc: CalcType.N, calcService: CalcType.N);

        var ex = Assert.Throws<NotImplementedException>(() =>
            solver.Compute(0.0, -60.0, 0.0, CurvatureNMode.Proportional, usePsi: false));
        Assert.Contains("Task 8-9", ex.Message);
    }
}
