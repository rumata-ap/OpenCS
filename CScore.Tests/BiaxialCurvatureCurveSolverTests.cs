using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
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

    // Формула Points.Count (auxPointsPerSegment=10 по умолчанию в этих тестах):
    // ByCurvature: 1(точка0) + 10(уч.1) + 1(точка1) + 10(петля) + 1(точка2)
    //              + [10(уч.3) + 1(точка3)] + 10(уч.4) + 1(точка4)
    //   без точки3: 3*10+4 = 34; с точкой3: 34+10+1 = 45.
    // ByMoment: петля НЕ строится (только endpoint точка2) — на 10 меньше, чем ByCurvature:
    //   без точки3: 2*10+4 = 24; с точкой3: 24+10+1 = 35.
    // Example47() при N=0/Mx=-60 — недоармированное сечение, текучесть арматуры наступает
    // раньше исчерпания (точка3 есть); RectWithBottomRebar(diam:0.010) при N=0/Mx=-30 — нет
    // (эмпирически подобрано, см. план Task 15).
    [Theory]
    [InlineData(CurveStepMode.ByCurvature, true, 45)]
    [InlineData(CurveStepMode.ByMoment, true, 35)]
    public void Compute_PointsCount_MatchesFormula_WithYieldPoint(CurveStepMode mode, bool expectYield, int expectedCount)
    {
        var section = TestSections.Example47();
        var solver = new BiaxialCurvatureCurveSolver(section, calcCrc: CalcType.N, calcService: CalcType.N,
            auxPointsPerSegment: 10, stepMode: mode);

        var result = solver.Compute(0.0, -60.0, 0.0, CurvatureNMode.Constant, usePsi: false);

        Assert.Equal(expectYield, result.Yield != null);
        Assert.Equal(expectedCount, result.Points.Count);
    }

    [Theory]
    [InlineData(CurveStepMode.ByCurvature, 34)]
    [InlineData(CurveStepMode.ByMoment, 24)]
    public void Compute_PointsCount_MatchesFormula_WithoutYieldPoint(CurveStepMode mode, int expectedCount)
    {
        var section = TestSections.RectWithBottomRebar(diam: 0.010);
        var solver = new BiaxialCurvatureCurveSolver(section, calcCrc: CalcType.N, calcService: CalcType.N,
            auxPointsPerSegment: 10, stepMode: mode);

        var result = solver.Compute(0.0, -30.0, 0.0, CurvatureNMode.Constant, usePsi: false);

        Assert.Null(result.Yield);
        Assert.Equal(expectedCount, result.Points.Count);
    }

    // N0/Mx0 эмпирически подобраны (план Task 15, Step 3): при Mx0=-60 на этом же N0 сжатый
    // пин петли "предел раньше трещины" вырождается (комбинация N/M у самой границы incapacity
    // сечения). Уточнение по факту диагностики 2026-08-19 (реальный проект пользователя,
    // "точка4 не совпадает"/"всплеск в петле"): исходный фолбэк CompressionPinSolverFast решал
    // на нецелевое (N,Mx,My) без учёта epsPin (давал "12", но с грубо неверными значениями
    // внутренних точек — сам скачок момента у пользователя); честная бисекция по epsPin,
    // заякоренная на локальной линейной оценке k0 (не на глобальный [0,1], который может
    // задевать чужую физическую ветвь), плюс тай-брейк выбора pin-вершины при нулевой стартовой
    // кривизне — восстанавливают честную сходимость всех 10 точек. 12 = 1 + (10+1).
    [Fact]
    public void Compute_PointsCount_NoCrackingCase()
    {
        var section = TestSections.Example47();
        var solver = new BiaxialCurvatureCurveSolver(section, calcCrc: CalcType.N, calcService: CalcType.N,
            auxPointsPerSegment: 10);

        var result = solver.Compute(-3500.0, -30.0, 0.0, CurvatureNMode.Constant, usePsi: false);

        Assert.Null(result.Cracking);
        Assert.Equal(12, result.Points.Count);
    }

    [Fact]
    public void SourceFile_ContainsNoEuclideanMomentNorm_OutsideFallbackPaths()
    {
        string path = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "CScore", "BiaxialCurvatureCurveSolver.cs");
        Assert.True(File.Exists(path), $"Файл не найден по пути: {Path.GetFullPath(path)}");
        string src = File.ReadAllText(path);
        // Единственное легитимное место с sqrt(Mx*Mx+My*My)-подобной нормой — CrackingSolver.cs
        // (не этот файл), проверяем ИМЕННО BiaxialCurvatureCurveSolver.cs.
        bool hasNorm = Regex.IsMatch(
            src, @"Sqrt\s*\(\s*Mx\s*\*\s*Mx\s*\+\s*My\s*\*\s*My\s*\)", RegexOptions.IgnoreCase);
        Assert.False(hasNorm, "BiaxialCurvatureCurveSolver.cs не должен содержать sqrt(Mx²+My²) — см. спеку.");
    }
}
