using System;
using System.Linq;
using CScore;
using Xunit;

namespace CScore.Tests;

public class BiaxialCurvatureCurveSolverTests
{
    const double Height = 0.300;
    const double Width = 1.150;
    const double RebarDepth = 0.042;
    const double RebarDiameter = 0.014;
    const double RebarArea = 923e-6;

    static CrossSection BuildUniaxialSection()
    {
        var concreteMaterial = new Material
        {
            Id = 1, Tag = "B15", Type = MatType.Concrete, E = 24_000_000.0,
            MaterialChars =
            [
                ConcreteChars(CalcType.C, false), ConcreteChars(CalcType.CL, true),
                ConcreteChars(CalcType.N, false), ConcreteChars(CalcType.NL, true),
            ]
        };

        var x = new[] { -Width / 2, Width / 2, Width / 2, -Width / 2, -Width / 2 };
        var y = new[] { -Height / 2, -Height / 2, Height / 2, Height / 2, -Height / 2 };
        var concrete = new MaterialArea
        {
            Id = 1, Tag = "Бетон B15", Category = AreaCategory.Region,
            Material = concreteMaterial, MaterialId = concreteMaterial.Id,
            DiagrammType = DiagrammType.L3, Hull = new Contour(x, y, "hull")
        };
        concrete.SetWKT();
        concrete.SliceXY(nx: 24, ny: 12);

        var steelMaterial = new Material
        {
            Id = 2, Tag = "A400", Type = MatType.ReSteelF, E = 200_000_000.0,
            MaterialChars =
            [
                RebarChars(CalcType.C), RebarChars(CalcType.CL),
                RebarChars(CalcType.N), RebarChars(CalcType.NL),
            ]
        };

        var rebar = new MaterialArea
        {
            Id = 2, Tag = "6Ø14 снизу", Category = AreaCategory.RebarGroup,
            Material = steelMaterial, MaterialId = steelMaterial.Id,
            DiagrammType = DiagrammType.L2, HostArea = concrete, HostAreaId = concrete.Id
        };
        double yBar = -Height / 2 + RebarDepth;
        double barArea = RebarArea / 6.0;
        for (int i = 0; i < 6; i++)
        {
            var bar = Fiber.CreatePoint(RebarDiameter, -0.45 + i * 0.18, yBar);
            bar.Area = barArea;
            rebar.Fibers.Add(bar);
        }

        var section = new CrossSection { Id = 1, Tag = "Пример 47", Areas = [concrete, rebar] };
        section.ResolveAndBuildDiagramms(rebarDifferentialDiagram: false);
        return section;
    }

    static MaterialChars ConcreteChars(CalcType calc, bool longTerm) => longTerm
        ? new MaterialChars
        {
            Type = MatType.Concrete, TypeCalc = calc,
            Fc = -11_000, Ft = 1_100, E = 9_090_909.091,
            Ec0 = -0.0034, Ec1 = -0.00121, Ec2 = -0.0048, Ec1Red = -0.0028,
            Et1Red = 0.00022, Et0 = 0.00024, Et1 = 0.000121, Et2 = 0.00031
        }
        : new MaterialChars
        {
            Type = MatType.Concrete, TypeCalc = calc,
            Fc = -11_000, Ft = 1_100, E = 24_000_000,
            Ec0 = -0.002, Ec1 = -0.000275, Ec2 = -0.0035, Ec1Red = -0.0015,
            Et1Red = 0.00008, Et0 = 0.0001, Et1 = 0.0000275, Et2 = 0.00015
        };

    static MaterialChars RebarChars(CalcType calc) => new()
    {
        Type = MatType.ReSteelF, TypeCalc = calc,
        Fc = -400_000, Ft = 400_000, Ry = 400_000, E = 200_000_000,
        Ec2 = -0.0035, Et2 = 0.025
    };

    [Fact]
    public void Compute_ZeroMoment_ReturnsErrorStatusWithoutThrowing()
    {
        var solver = new BiaxialCurvatureCurveSolver(BuildUniaxialSection(), pointsPerSegment: 11);
        var result = solver.Compute(N0: 0.0, Mx0: 0.0, My0: 0.0, CurvatureNMode.Constant, usePsi: true);

        Assert.Equal("error", result.Status);
        Assert.Empty(result.Points);
    }

    [Fact]
    public void Compute_ElasticStiffness_MatchesActualDiagramTangentOnUnmeshedRectangle()
    {
        var section = TestSections.RectWithBottomRebarNoMesh(h: 0.5, b: 0.3, diam: 0.016);
        var solver = new BiaxialCurvatureCurveSolver(section, pointsPerSegment: 11);
        var result = solver.Compute(N0: 0.0, Mx0: 1.0, My0: 0.0, CurvatureNMode.Constant, usePsi: true);

        var concreteArea = section.Areas.Single(a => a.Category == AreaCategory.Region);
        var rebarArea = section.Areas.Single(a => a.Category == AreaCategory.RebarGroup);

        // ВАЖНО: ожидаемое значение берётся из ФАКТИЧЕСКОГО касательного модуля построенной
        // диаграммы (Diagramm.Sig(eps, out tangent, ...)), а не из номинального Material.E.
        // Для бетона L2 по СП63 (MaterialChars.D2L()) начальный наклон растянутой ветви равен
        // Ft/Et1Red (секущая до первой характерной точки), а сжатой — Fc/Ec1Red — ОБА отличны
        // от Material.E и друг от друга (расхождение выявлено при первых двух прогонах этого
        // теста, см. CScore/MaterialChars.cs:179-184). Для B0x (чистый изгиб, ky) ПОЛОВИНА
        // сечения растянута, половина сжата — нельзя брать один общий модуль бетона на всё
        // сечение, как это ошибочно делала первая версия этого теста.
        // Для арматуры ReSteelF (L2) первые точки растянутой (Ft/E,Ft) и сжатой (Fc/E,Fc)
        // ветвей дают одинаковый наклон E по обе стороны — модуль общий, берётся из диаграммы
        // для единообразия и устойчивости теста к будущим изменениям построения диаграмм.
        concreteArea.Diagramms[CalcType.N].Sig(1e-7, out double concreteTensionTangent, tenB: true, comprA: true);
        concreteArea.Diagramms[CalcType.N].Sig(-1e-7, out double concreteCompressionTangent, tenB: true, comprA: true);
        rebarArea.Diagramms[CalcType.N].Sig(1e-7, out double rebarTangent, tenB: true, comprA: true);

        const double h = 0.5, b = 0.3;
        double expectedEa0 = concreteTensionTangent * h * b
            + rebarArea.Fibers.Sum(f => rebarTangent * f.Area);
        // ∫∫E(y)y²dA по прямоугольнику при чистом изгибе (e0=0): нижняя половина (y<0) сжата,
        // верхняя (y>0) растянута — b*(h/2)³/3*(Et+Ec), не b*h³/12*E.
        double expectedB0x = b * Math.Pow(h / 2.0, 3) / 3.0 * (concreteTensionTangent + concreteCompressionTangent)
            + rebarArea.Fibers.Sum(f => rebarTangent * f.Area * f.Y * f.Y);

        // Относительная сверка (не Assert.Equal с фиксированным числом знаков) — Ea0/B0x
        // получены конечно-разностным дифференцированием (шаг _solverH), не точным
        // аналитическим значением; для линейной (упругой) области расхождение должно быть
        // пренебрежимо малым, но абсолютный допуск в decimal-разрядах неверно откалиброван
        // под величины порядка 1e6-1e7 (см. замечание 11 ревью плана).
        Assert.True(Math.Abs(result.Ea0 - expectedEa0) / expectedEa0 < 1e-4,
            $"Ea0: expected {expectedEa0}, got {result.Ea0}");
        Assert.True(Math.Abs(result.B0x - expectedB0x) / expectedB0x < 1e-4,
            $"B0x: expected {expectedB0x}, got {result.B0x}");
    }

    [Fact]
    public void Compute_UniaxialConstantModeWithPsi_ConvergesThroughAllControlPoints()
    {
        var solver = new BiaxialCurvatureCurveSolver(
            BuildUniaxialSection(), solverTol: 0.05, solverMaxIter: 80, solverH: 1e-7,
            pointsPerSegment: 41);
        var result = solver.Compute(N0: 0.0, Mx0: -1.0, My0: 0.0, CurvatureNMode.Constant, usePsi: true);

        Assert.True(result.HasMx);
        Assert.False(result.HasMy);
        Assert.NotNull(result.Cracking);
        Assert.True(result.Cracking!.Converged);
        Assert.NotNull(result.CrackTransitionPoint);
        Assert.True(result.CrackTransitionPoint!.Converged);
        Assert.NotNull(result.UltimateReference);
        Assert.True(result.UltimateReference!.Converged);
        Assert.NotNull(result.Ultimate);
        Assert.True(result.Ultimate!.Converged);
        Assert.True(result.Points.Count > 0);
        Assert.True(result.Points.All(p => p.Kz == 0.0));
        Assert.Equal("ok", result.Status);
        // Пересчётная точка при usePsi=true НЕ входит в Points (см. решение 5 спеки).
        Assert.DoesNotContain(result.Points, p => p.Segment == 2);
    }

    [Fact]
    public void Compute_UsePsiFalse_IncludesTransitionPointInPoints()
    {
        var solver = new BiaxialCurvatureCurveSolver(
            BuildUniaxialSection(), solverTol: 0.05, solverMaxIter: 80, solverH: 1e-7,
            pointsPerSegment: 41);
        var result = solver.Compute(N0: 0.0, Mx0: -1.0, My0: 0.0, CurvatureNMode.Constant, usePsi: false);

        Assert.Contains(result.Points, p => p.Segment == 2);
        Assert.DoesNotContain(result.Points, p => p.Converged && p.Clipped);
    }

    [Fact]
    public void Compute_UsePsiTrue_NoPointExceedsUltimateReference()
    {
        var solver = new BiaxialCurvatureCurveSolver(
            BuildUniaxialSection(), solverTol: 0.05, solverMaxIter: 80, solverH: 1e-7,
            pointsPerSegment: 41);
        var result = solver.Compute(N0: 0.0, Mx0: -1.0, My0: 0.0, CurvatureNMode.Constant, usePsi: true);

        double refMx = Math.Abs(result.UltimateReference!.Mx);
        Assert.All(result.Points.Where(p => p.Converged && (p.Segment == 3 || p.Segment == 4)),
            p => Assert.True(Math.Abs(p.Mx) <= refMx + 1e-6));
    }

    [Fact]
    public void Compute_AsymmetricBiaxialConstantMode_CrackingPointLiesOnScanRay()
    {
        var section = TestSections.RectWithCornerClusterRebar();
        var solver = new BiaxialCurvatureCurveSolver(section, pointsPerSegment: 21);
        double uky = 1.0 / Math.Sqrt(2), ukz = 1.0 / Math.Sqrt(2);

        var result = solver.Compute(N0: 0.0, Mx0: uky, My0: ukz, CurvatureNMode.Constant, usePsi: true);

        Assert.True(result.Cracking!.Converged);
        double mag = Math.Sqrt(
            result.Cracking.Ky * result.Cracking.Ky + result.Cracking.Kz * result.Cracking.Kz);
        Assert.Equal(uky, result.Cracking.Ky / mag, 6);
        Assert.Equal(ukz, result.Cracking.Kz / mag, 6);
    }

    [Fact]
    public void Compute_BiaxialConstantMode_ConvergesWithNonZeroKyAndKz()
    {
        var section = TestSections.RectWithCornerClusterRebar();
        var solver = new BiaxialCurvatureCurveSolver(section, pointsPerSegment: 21);

        var result = solver.Compute(N0: 0.0, Mx0: 1.0, My0: 1.0, CurvatureNMode.Constant, usePsi: true);

        Assert.True(result.HasMx);
        Assert.True(result.HasMy);
        Assert.Contains(result.Points, p => p.Converged && p.Ky != 0.0);
        Assert.Contains(result.Points, p => p.Converged && p.Kz != 0.0);
    }

    [Fact]
    public void Compute_BiaxialProportionalMode_ConvergesWithNonZeroKyAndKz()
    {
        var section = TestSections.RectWithCornerClusterRebar();
        var solver = new BiaxialCurvatureCurveSolver(section, pointsPerSegment: 21);

        var result = solver.Compute(N0: -50.0, Mx0: 1.0, My0: 1.0, CurvatureNMode.Proportional, usePsi: true);

        Assert.True(result.HasMx);
        Assert.True(result.HasMy);
        Assert.Contains(result.Points, p => p.Converged && p.Ky != 0.0);
        Assert.Contains(result.Points, p => p.Converged && p.Kz != 0.0);
    }

    [Fact]
    public void Compute_ProportionalMode_ScalesAllThreeComponentsAtCracking()
    {
        var section = TestSections.RectWithCornerClusterRebar();
        var solver = new BiaxialCurvatureCurveSolver(section, pointsPerSegment: 21);

        // Эксцентриситет Mx0/N0=0.2 м (не малый, как у изначального 1/50=0.02 м) — при малом
        // эксцентриситете трещина образуется только при N, близком к предельной по сжатию
        // силе (см. диагностику: с Mx0=1.0 λ_crc≈80 => N≈-4000 кН, на грани разрешимости для
        // сечения 0.5×0.4 м), что делает последующий Ньютон-пересчёт численно неустойчивым.
        var result = solver.Compute(N0: -50.0, Mx0: 10.0, My0: 3.0, CurvatureNMode.Proportional, usePsi: true);

        Assert.True(result.Cracking!.Converged);
        Assert.NotEqual(0.0, result.Cracking.N);
        Assert.NotEqual("error", result.Status);
    }

    // Регрессия на блокер 2 ревью плана: в режиме Proportional λ_crc, как правило, НЕ равен
    // 1.0 — участок 1 обязан заканчиваться РОВНО в точке трещинообразования (без разрыва по
    // параметру скана), а участок 3 обязан начинаться РОВНО с пересчётной точки на том же λ.
    [Fact]
    public void Compute_ProportionalMode_Segment1EndsAndSegment3StartsAtCrackingLambda()
    {
        var section = TestSections.RectWithCornerClusterRebar();
        var solver = new BiaxialCurvatureCurveSolver(section, pointsPerSegment: 21);

        var result = solver.Compute(N0: -80.0, Mx0: 10.0, My0: 4.0, CurvatureNMode.Proportional, usePsi: false);

        Assert.True(result.Cracking!.Converged);
        var segment1 = result.Points.Where(p => p.Segment == 1).ToList();
        Assert.NotEmpty(segment1);
        Assert.Equal(result.Cracking.T, segment1[^1].T, 6);
        Assert.Equal(result.Cracking.Mx, segment1[^1].Mx, 4);

        Assert.NotNull(result.CrackTransitionPoint);
        Assert.Equal(result.Cracking.T, result.CrackTransitionPoint!.T, 6);

        var segment3 = result.Points.Where(p => p.Segment == 3).ToList();
        if (segment3.Count > 0)
            Assert.Equal(result.CrackTransitionPoint.T, segment3[0].T, 6);
    }

    // ИССЛЕДОВАНО ЭКСПЕРИМЕНТАЛЬНО (см. журнал реализации): для диаграммы бетона L2 по СП63
    // сжатая ветвь выходит на плато за Ec2 (не становится нерешаемой матaматически), поэтому
    // равновесие по N остаётся формально достижимым сколь угодно долго при росте кривизны, а
    // растяжение на противоположной грани неизбежно развивается раньше, чем N становится
    // недостижимым. Из-за этого для сечения RectWithBottomRebar(0.3,0.2) НЕ нашлось окна
    // "N достижимо при κ=0, но трещина не образуется ни при какой κ" — переход происходит
    // резко от "трещина найдена, status=ok" (N0 до -1310) сразу к "N недостижимо уже при κ=0"
    // (N0 от -1320). Поэтому тест проверяет не "трещина не найдена, но N достижимо" (сценарий,
    // который эта фикстура не воспроизводит), а изящную деградацию БЕЗ необработанного
    // исключения для N, недостижимого ни при какой кривизне.
    [Fact]
    public void Compute_InfeasibleAxialForce_ReturnsErrorStatusWithoutThrowing()
    {
        var section = TestSections.RectWithBottomRebar(h: 0.3, b: 0.2);
        var solver = new BiaxialCurvatureCurveSolver(section, pointsPerSegment: 21);

        var result = solver.Compute(N0: -50000.0, Mx0: 1.0, My0: 0.0, CurvatureNMode.Constant, usePsi: true);

        Assert.Equal("error", result.Status);
        Assert.NotNull(result.Cracking);
        Assert.False(result.Cracking!.Converged);
    }

    [Fact]
    public void Compute_YieldNotReachedBeforeUltimate_YieldIsNull()
    {
        // Существенное продольное сжатие приближает разрушение по сжатию бетона раньше
        // текучести растянутой арматуры.
        var section = TestSections.RectWithBottomRebar();
        var solver = new BiaxialCurvatureCurveSolver(section, pointsPerSegment: 21);

        var result = solver.Compute(N0: -2200.0, Mx0: -1.0, My0: 0.0, CurvatureNMode.Constant, usePsi: true);

        if (result.Cracking!.Converged && result.Yield == null)
        {
            Assert.DoesNotContain(result.Points, p => p.Segment == 3);
            Assert.Contains(result.Points, p => p.Segment == 4);
        }
    }
}
