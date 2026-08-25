using System;
using System.IO;
using System.Linq;
using System.Reflection;
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
            Assert.Contains(flagged, p => Math.Abs(p.Mx) > Math.Abs(withPsi.UltimateReference!.Mx) - 1e-6);
        }
    }

    [Fact]
    public void Compute_UsePsiTrue_MarksPointByMomentMagnitudeNotByComponent()
    {
        var section = TestSections.Example47();
        var solver = new BiaxialCurvatureCurveSolver(section, calcCrc: CalcType.N, calcService: CalcType.N);

        var result = solver.Compute(0.0, -60.0, -20.0, CurvatureNMode.Constant, usePsi: true);

        Assert.NotNull(result.UltimateReference);
        var reference = result.UltimateReference!;
        double referenceMagnitude = Math.Sqrt(
            reference.Mx * reference.Mx + reference.My * reference.My);

        Assert.All(result.Points.Where(p => p.Converged), point =>
        {
            double magnitude = Math.Sqrt(point.Mx * point.Mx + point.My * point.My);
            Assert.Equal(magnitude > referenceMagnitude * 1.01, point.NonPhysical);
        });
    }

    [Theory]
    [InlineData(-60.0, 0.0)]
    [InlineData(0.0, -20.0)]
    [InlineData(-60.0, -20.0)]
    public void Compute_UsePsiTrue_DoesNotMarkWholeCurveNonPhysical(double mx0, double my0)
    {
        // Регрессия: покомпонентное сравнение с нулевым эталоном (одноосный вход) помечало
        // нефизичной КАЖДУЮ точку — численный шум ~1e-13 в обнулённой компоненте всегда
        // «превышал» ровно нулевой эталон, и весь график уходил в серое.
        var section = TestSections.Example47();
        var solver = new BiaxialCurvatureCurveSolver(section, calcCrc: CalcType.N, calcService: CalcType.N,
            auxPointsPerSegment: 10, stepMode: CurveStepMode.ByCurvature);

        var result = solver.Compute(0.0, mx0, my0, CurvatureNMode.Constant, usePsi: true);

        var converged = result.Points.Where(p => p.Converged).ToList();
        Assert.NotEmpty(converged);
        Assert.DoesNotContain(converged, p => p.NonPhysical);
    }

    [Theory]
    [InlineData(-60.0, 0.0)]
    [InlineData(-60.0, -20.0)]
    public void Compute_UsePsiTrue_MomentStaysWithinUltimateReference(double mx0, double my0)
    {
        // ψs-поправка берёт напряжение с диаграммы при εs,crc, поэтому за площадкой текучести
        // она затухает и кривая не может превысить предельную несущую способность сечения.
        var section = TestSections.Example47();
        var solver = new BiaxialCurvatureCurveSolver(section, calcCrc: CalcType.N, calcService: CalcType.N,
            auxPointsPerSegment: 10, stepMode: CurveStepMode.ByCurvature);

        var result = solver.Compute(0.0, mx0, my0, CurvatureNMode.Constant, usePsi: true);

        Assert.Equal("ok", result.Status);
        var reference = result.UltimateReference!;
        double referenceMagnitude = Math.Sqrt(
            reference.Mx * reference.Mx + reference.My * reference.My);
        foreach (var point in result.Points.Where(p => p.Converged))
        {
            double magnitude = Math.Sqrt(point.Mx * point.Mx + point.My * point.My);
            Assert.True(magnitude <= referenceMagnitude * 1.01,
                $"|M| = {magnitude:F3} превышает предел {referenceMagnitude:F3} (уч. {point.Segment})");
        }
    }

    [Fact]
    public void Compute_UsePsiTrue_MainCurveDoesNotIncludeTransitionLoop()
    {
        var section = TestSections.Example47();
        var solver = new BiaxialCurvatureCurveSolver(section, calcCrc: CalcType.N, calcService: CalcType.N,
            auxPointsPerSegment: 10, stepMode: CurveStepMode.ByCurvature);

        var result = solver.Compute(0.0, -60.0, 0.0, CurvatureNMode.Constant, usePsi: true);

        Assert.DoesNotContain(result.Points, p => p.Segment == 2);
        Assert.Contains(result.Points, p => p.PsiActive && (p.Segment is 3 or 4));

        var firstPsiPoint = result.Points.First(p => p.PsiActive && (p.Segment is 3 or 4));
        Assert.True(Math.Abs(firstPsiPoint.Ky) > Math.Abs(result.Cracking!.Ky));
        Assert.True(Math.Abs(firstPsiPoint.Ky) < Math.Abs(result.CrackTransitionPoint!.Ky));
    }

    [Fact]
    public void Compute_ByCurvature_CrackLoopWalksCurvatureForward()
    {
        // Регрессия «перехлёст на уч. 1-2»: петля разворачивалась по деформации сжатия
        // бетона. В зоне раскрытия трещины этот параметр почти стационарен и многозначен —
        // точки выходили не по порядку (первая сразу у конца петли, следующая ЛЕВЕЕ точки
        // трещинообразования), и ломаная графика пересекала сама себя. Слоистая сетка
        // (1×50) — та же, что в реальном сечении пользователя, на ней эффект воспроизводится.
        var section = TestSections.Example47();
        section.Areas.First(a => a.Material?.Type == MatType.Concrete).SliceXY(nx: 1, ny: 50);
        var solver = new BiaxialCurvatureCurveSolver(section, calcCrc: CalcType.C, calcService: CalcType.C,
            auxPointsPerSegment: 10, stepMode: CurveStepMode.ByCurvature);

        var result = solver.Compute(0.0, -50.0, 0.0, CurvatureNMode.Constant, usePsi: false);

        var loop = result.Points.Where(p => p.Segment == 2 && p.Converged).ToList();
        Assert.NotEmpty(loop);

        double transitionCurvature = Math.Abs(result.CrackTransitionPoint!.Ky);
        double previous = Math.Abs(result.Cracking!.Ky);
        foreach (var point in loop)
        {
            double curvature = Math.Abs(point.Ky);
            Assert.True(curvature > previous,
                $"|κ| = {curvature:E4} не больше предыдущей {previous:E4} — точки петли не по порядку");
            Assert.True(curvature <= transitionCurvature * (1.0 + 1e-9),
                $"|κ| = {curvature:E4} выходит за точку восстановления момента {transitionCurvature:E4}");
            previous = curvature;
        }

        Assert.Equal(result.CrackTransitionPoint!.Ky, loop[^1].Ky, 12);
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
    public void Compute_ByCurvature_Segment1DoesNotRunBackward()
    {
        var section = TestSections.RectWithBottomRebar(diam: 0.020);
        var solver = new BiaxialCurvatureCurveSolver(section, calcCrc: CalcType.N, calcService: CalcType.N,
            auxPointsPerSegment: 10, stepMode: CurveStepMode.ByCurvature);

        var result = solver.Compute(-1000.0, -100.0, 20.0, CurvatureNMode.Proportional, usePsi: false);
        var curvature = result.Points
            .Where(p => p.Segment == 1)
            .Select(p => Math.Abs(p.Ky))
            .ToList();

        Assert.True(curvature.Count >= 3);
        for (int i = 1; i < curvature.Count; i++)
            Assert.True(curvature[i] >= curvature[i - 1] - 1e-9,
                $"Участок 1 пошёл назад на индексе {i}: {curvature[i - 1]} -> {curvature[i]}");
    }

    [Fact]
    public void Compute_ByCurvature_Segment4DoesNotStartBelowTransitionPoint()
    {
        var section = TestSections.RectWithBottomRebar(diam: 0.010);
        var solver = new BiaxialCurvatureCurveSolver(section, calcCrc: CalcType.N, calcService: CalcType.N,
            auxPointsPerSegment: 10, stepMode: CurveStepMode.ByCurvature);

        var result = solver.Compute(0.0, -30.0, 0.0, CurvatureNMode.Constant, usePsi: false);
        var transitionIndex = result.Points.FindLastIndex(p => p.Segment == 2);
        var firstSegment4Index = result.Points.FindIndex(p => p.Segment == 4);

        Assert.True(transitionIndex >= 0);
        Assert.True(firstSegment4Index > transitionIndex);
        Assert.True(
            Math.Abs(result.Points[firstSegment4Index].Ky) >= Math.Abs(result.Points[transitionIndex].Ky) - 1e-9,
            $"Участок 4 начинается ниже точки 2: {result.Points[transitionIndex].Ky} -> {result.Points[firstSegment4Index].Ky}");
    }

    [Fact]
    public void Compute_SymmetricSection_GoverningPinDoesNotCreateCrossCurvature()
    {
        var section = TestSections.Example47();
        var solver = new BiaxialCurvatureCurveSolver(section, calcCrc: CalcType.N, calcService: CalcType.N,
            auxPointsPerSegment: 10, stepMode: CurveStepMode.ByCurvature);

        var result = solver.Compute(0.0, -60.0, 0.0, CurvatureNMode.Constant, usePsi: false);
        var segment4 = result.Points.Where(p => p.Segment == 4).ToList();

        Assert.NotEmpty(segment4);
        Assert.All(segment4, point => Assert.True(Math.Abs(point.Kz) <= 1e-9,
            $"Симметричное сечение получило kz={point.Kz} при My=0"));
    }

    [Fact]
    public void Compute_SymmetricSection_Segment4DoesNotBacktrack()
    {
        var section = TestSections.Example47();
        var solver = new BiaxialCurvatureCurveSolver(section, calcCrc: CalcType.N, calcService: CalcType.N,
            auxPointsPerSegment: 10, stepMode: CurveStepMode.ByCurvature);

        var result = solver.Compute(0.0, -60.0, 0.0, CurvatureNMode.Constant, usePsi: false);
        var curvature = result.Points
            .Where(p => p.Segment == 4)
            .Select(p => Math.Abs(p.Ky))
            .ToList();

        Assert.True(curvature.Count >= 3);
        for (int i = 1; i < curvature.Count; i++)
            Assert.True(curvature[i] >= curvature[i - 1] - 1e-9,
                $"Участок 4 пошёл назад на индексе {i}: {curvature[i - 1]} -> {curvature[i]}");
    }

    [Fact]
    public void Compute_CalcC_RebarWithFtButNoRyStillFindsYieldPoint()
    {
        var section = TestSections.Example47();
        var rebar = section.Areas.Single(area => area.Material?.Type == MatType.ReSteelF);
        var chars = rebar.Material!.C!;
        chars.Ry = 0.0;
        chars.Et0 = 0.0;
        section.ResolveAndBuildDiagramms(0.85, pool: null, rebarDifferentialDiagram: false);

        var solver = new BiaxialCurvatureCurveSolver(section, calcCrc: CalcType.C, calcService: CalcType.C,
            auxPointsPerSegment: 10, stepMode: CurveStepMode.ByCurvature);

        var result = solver.Compute(0.0, -60.0, 0.0, CurvatureNMode.Constant, usePsi: false);

        Assert.NotNull(result.Yield);
        Assert.Equal(3, result.Yield!.Segment);
    }

    [Fact]
    public void Compute_YieldPoint_IsAtActualRebarYieldStrain()
    {
        var section = TestSections.Example47();
        var rebar = section.Areas.Single(area => area.Material?.Type == MatType.ReSteelF);
        var chars = rebar.Material!.N!;
        var solver = new BiaxialCurvatureCurveSolver(section, calcCrc: CalcType.N, calcService: CalcType.N,
            auxPointsPerSegment: 10, stepMode: CurveStepMode.ByCurvature);

        var result = solver.Compute(0.0, -60.0, 0.0, CurvatureNMode.Constant, usePsi: false);

        Assert.NotNull(result.Yield);
        double maxRebarStrain = section.EnumerateAreas(new Kurvature
            {
                e0 = result.Yield!.E0,
                ky = result.Yield.Ky,
                kz = result.Yield.Kz
            })
            .Where(pair => pair.area.Material?.Type is MatType.ReSteelF or MatType.ReSteelU)
            .SelectMany(pair => pair.area.Fibers
                .Where(fiber => fiber.TypeFiber == FiberType.point)
                .Select(fiber => pair.k.e0 + pair.k.ky * fiber.Y + pair.k.kz * fiber.X + fiber.Eps_p))
            .Max();

        // Допуск относительный: точка текучести ищется бисекцией вдоль луча нагружения, и
        // каждая проба — решение равновесия с допуском solverTol (по усилию, не по
        // деформации). Поэтому граница ε = Ft/E воспроизводится с точностью решателя, а не
        // машинной. 0.5% от Ft/E — заведомо меньше шага вспомогательных точек участка.
        double yieldStrain = chars.Ft / chars.E;
        Assert.InRange(maxRebarStrain, yieldStrain * 0.995, yieldStrain * 1.005);
    }

    [Fact]
    public void RebarYieldStrain_UsesTensileResistanceOverElasticModulus()
    {
        var method = typeof(BiaxialCurvatureCurveSolver).GetMethod(
            "RebarYieldStrain", BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(method);

        var chars = new MaterialChars
        {
            Ft = 348_000.0,
            Ry = 400_000.0,
            E = 200_000_000.0
        };

        var strain = (double)method!.Invoke(null, [MatType.ReSteelF, chars])!;

        Assert.Equal(chars.Ft / chars.E, strain, precision: 12);
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
    // раньше исчерпания (точка3 есть). Для RectWithBottomRebar(diam:0.010) при N=0/Mx=-30
    // после перехода на Ft/E точка3 также есть: арматура A500 достигает Ft/E до предела.
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
    [InlineData(CurveStepMode.ByCurvature, 45)]
    [InlineData(CurveStepMode.ByMoment, 35)]
    public void Compute_PointsCount_MatchesFormula_WithFtOverEYieldPoint(CurveStepMode mode, int expectedCount)
    {
        var section = TestSections.RectWithBottomRebar(diam: 0.010);
        var solver = new BiaxialCurvatureCurveSolver(section, calcCrc: CalcType.N, calcService: CalcType.N,
            auxPointsPerSegment: 10, stepMode: mode);

        var result = solver.Compute(0.0, -30.0, 0.0, CurvatureNMode.Constant, usePsi: false);

        Assert.NotNull(result.Yield);
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
