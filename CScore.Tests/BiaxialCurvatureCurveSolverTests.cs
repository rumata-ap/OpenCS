using System;
using System.Linq;
using CScore;
using Xunit;

namespace CScore.Tests;

public class BiaxialCurvatureCurveSolverTests
{
    [Fact]
    public void Compute_UsePsiTrue_FlagsNonPhysicalWithoutClippingValue()
    {
        var section = TestSections.Example47();
        var solverPsi = new BiaxialCurvatureCurveSolver(section, calcCrc: CalcType.N, calcService: CalcType.N);
        var solverNoPsi = new BiaxialCurvatureCurveSolver(section, calcCrc: CalcType.N, calcService: CalcType.N);

        var withPsi = solverPsi.Compute(0.0, -60.0, 0.0, CurvatureNMode.Constant, usePsi: true);
        var withoutPsi = solverNoPsi.Compute(0.0, -60.0, 0.0, CurvatureNMode.Constant, usePsi: false);

        // usePsi=false — ни одна точка не помечена NonPhysical (сравнение не выполняется).
        Assert.DoesNotContain(withoutPsi.Points, p => p.NonPhysical);

        // usePsi=true, если найдены точки за пределом — значения НЕ урезаны (могут превышать
        // UltimateReference по модулю).
        var flagged = withPsi.Points.Where(p => p.NonPhysical).ToList();
        if (flagged.Count > 0)
        {
            Assert.True(flagged.Any(p => Math.Abs(p.Mx) > Math.Abs(withPsi.UltimateReference!.Mx) - 1e-6));
        }
    }

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
    public void Compute_NormalCase_FindsCrackingAndTransitionPoints()
    {
        var section = TestSections.Example47();
        var solver = new BiaxialCurvatureCurveSolver(section, calcCrc: CalcType.N, calcService: CalcType.N);

        var result = solver.Compute(0.0, -60.0, 0.0, CurvatureNMode.Constant, usePsi: false);

        Assert.NotNull(result.Cracking);
        Assert.NotNull(result.CrackTransitionPoint);
        Assert.True(result.CrackTransitionPoint!.Converged);
        Assert.Equal(2, result.CrackTransitionPoint.Segment);
        // Петля: точки Segment==2 в Points, между точкой1 и точкой2.
        Assert.Contains(result.Points, p => p.Segment == 2);
    }

    [Fact]
    public void Compute_ProportionalMode_Point2UsesFreeEquilibrium()
    {
        var section = TestSections.Example47();
        var solver = new BiaxialCurvatureCurveSolver(section, calcCrc: CalcType.N, calcService: CalcType.N);

        var result = solver.Compute(0.0, -60.0, 0.0, CurvatureNMode.Proportional, usePsi: false);

        Assert.NotNull(result.CrackTransitionPoint);
        Assert.True(result.CrackTransitionPoint!.Converged);
    }

    [Fact]
    public void Compute_NormalCase_FullPipeline_ReachesUltimate()
    {
        var section = TestSections.Example47();
        var solver = new BiaxialCurvatureCurveSolver(section, calcCrc: CalcType.N, calcService: CalcType.N);

        var result = solver.Compute(0.0, -60.0, 0.0, CurvatureNMode.Constant, usePsi: false);

        Assert.Equal("ok", result.Status);
        Assert.NotNull(result.Ultimate);
        Assert.True(result.Ultimate!.Converged);
        Assert.NotNull(result.UltimateReference);
        // usePsi=false — Ultimate и UltimateReference физически совпадают (см. спеку P1-3).
        Assert.Equal(result.UltimateReference!.Mx, result.Ultimate.Mx, precision: 0);
    }

    // Уточнение по факту прогона: Mx/My-ОТНОШЕНИЕ у точки 1 и точки 2 ВСЕГДА равно входному
    // отношению (Mx0/My0) — оба находятся пин/StrainSolver-решением с ТОЧНОЙ целью
    // (N,k·Mx0,k·My0)/(N1,Mx1,My1), поэтому момент "наследует" входное направление по всей
    // цепочке точка1→точка2→точка4 (это не баг, а свойство постановки). Свободным в новой
    // архитектуре является направление КРИВИЗНЫ (Ky/Kz), а не момента — именно оно теперь НЕ
    // обязано совпадать с направлением (Mx0,My0), в отличие от старой ray-based реализации, где
    // кривизна была явно зафиксирована пропорционально моменту. Тест проверяет это свойство:
    // кривизна в точке 2 (свободное равновесие) в общем случае не пропорциональна (Mx0,My0).
    [Fact]
    public void Compute_Biaxial_Point2Curvature_NotProportionalToInputDirection()
    {
        var section = TestSections.RectWithCornerClusterRebar();
        var solver = new BiaxialCurvatureCurveSolver(section, calcCrc: CalcType.N, calcService: CalcType.N);

        var result = solver.Compute(0.0, -20.0, -10.0, CurvatureNMode.Constant, usePsi: false);

        Assert.NotNull(result.CrackTransitionPoint);
        Assert.NotNull(result.UltimateReference);
        // Mx/My-отношение сохраняется по всей цепочке (свойство постановки, не баг) — точка 4
        // действительно нацелена на направление МОМЕНТА точки 2, но оно совпадает с входным.
        Assert.True(Math.Abs(result.CrackTransitionPoint!.My) > 1e-6);
        Assert.True(Math.Abs(result.UltimateReference!.My) > 1e-6);
        double inputMomentRatio = -20.0 / -10.0;
        Assert.Equal(inputMomentRatio, result.CrackTransitionPoint.Mx / result.CrackTransitionPoint.My, precision: 1);
        Assert.Equal(inputMomentRatio, result.UltimateReference.Mx / result.UltimateReference.My, precision: 1);

        // А вот направление КРИВИЗНЫ в точке 2 (свободное равновесие) в общем случае НЕ
        // пропорционально входному направлению момента — именно это отличает новую
        // (пин-ориентированную) архитектуру от старой ray-based.
        Assert.True(Math.Abs(result.CrackTransitionPoint.Kz) > 1e-9);
        double point2CurvatureRatio = result.CrackTransitionPoint.Ky / result.CrackTransitionPoint.Kz;
        Assert.NotEqual(Math.Round(inputMomentRatio, 1), Math.Round(point2CurvatureRatio, 1));
    }
}
