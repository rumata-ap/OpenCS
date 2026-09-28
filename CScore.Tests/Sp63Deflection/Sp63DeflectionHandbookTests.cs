using CScore;
using CScore.Sp63.CrackWidth;
using CScore.Sp63.Deflection;
using CScore.Sp63.Normal;
using Xunit;
using Xunit.Abstractions;

namespace CScore.Tests.Sp63Deflection;

/// <summary>
/// Срез 7.5: сверка прогиба по формулам (sp63_deflection) с примером 49 Пособия к
/// СП 63.13330.2018 и с полной кривизной по деформационной модели (<see cref="TotalCurvatureSolver"/>)
/// на том же сечении. Плита b = 1000, h = 200 мм, h0 = 173 мм, B15 (Eb = 24000, Rb,ser = 11,
/// Rbt,ser = 1,1 МПа), A400, As = 769 мм² (5⌀14), пролёт 5,6 м, q = ql = 6,5 кН/м →
/// M = Ml = 25,5 кН·м, влажность 40–75 %, S = 5/48.
/// Пособие: (1/r)max = 9,99·10⁻⁶ 1/мм по приближённой (4.48) → f = 32,6 мм; уточнённый прогиб
/// с переменной жёсткостью по длине (4.37) — 25,94 мм.
/// </summary>
public sealed class Sp63DeflectionHandbookTests(ITestOutputHelper output)
{
    const double Width = 1.0, Height = 0.2, Cover = 0.027, Span = 5.6, Moment = 25.5;

    static MaterialChars ConcreteChars(CalcType calc, bool longTerm) => longTerm
        ? new MaterialChars
        {
            Type = MatType.Concrete, TypeCalc = calc, Class = 15,
            Fc = -11_000, Ft = 1_100, E = 24_000_000 / 4.4,
            Ec0 = -0.0034, Ec1 = -0.00121, Ec2 = -0.0048, Ec1Red = -0.0028,
            Et1Red = 0.00022, Et0 = 0.00024, Et1 = 0.000121, Et2 = 0.00031
        }
        : new MaterialChars
        {
            Type = MatType.Concrete, TypeCalc = calc, Class = 15,
            Fc = -11_000, Ft = 1_100, E = 24_000_000,
            Ec0 = -0.002, Ec1 = -0.000275, Ec2 = -0.0035, Ec1Red = -0.0015,
            Et1Red = 0.00008, Et0 = 0.0001, Et1 = 0.0000275, Et2 = 0.00015
        };

    static MaterialChars RebarChars(CalcType calc) => new()
    {
        Type = MatType.ReSteelF, TypeCalc = calc,
        Fc = -400_000, Ft = 400_000, E = 200_000_000, Ec2 = -0.0035, Et2 = 0.025
    };

    /// <summary>
    /// Сечение примера 49 с сеткой фибр для НДМ. Верхний стержень номинальный (1 мм²): формульный
    /// путь требует двух уровней арматуры (п. 8.1.8), на результат он практически не влияет.
    /// </summary>
    static CrossSection Example49Section()
    {
        var concreteMaterial = new Material
        {
            Id = 1, Tag = "B15", Type = MatType.Concrete, E = 24_000_000,
            MaterialChars =
            [
                ConcreteChars(CalcType.C, false), ConcreteChars(CalcType.CL, true),
                ConcreteChars(CalcType.N, false), ConcreteChars(CalcType.NL, true)
            ]
        };
        var concrete = new MaterialArea
        {
            Id = 1, Tag = "Плита", Category = AreaCategory.Region,
            Material = concreteMaterial, MaterialId = 1, DiagrammType = DiagrammType.L3,
            Hull = new Contour([-Width / 2, Width / 2, Width / 2, -Width / 2, -Width / 2],
                [-Height / 2, -Height / 2, Height / 2, Height / 2, -Height / 2], "hull")
        };
        concrete.SetWKT();
        concrete.SliceXY(nx: 50, ny: 40);

        var steelMaterial = new Material
        {
            Id = 2, Tag = "A400", Type = MatType.ReSteelF, E = 200_000_000,
            MaterialChars =
            [
                RebarChars(CalcType.C), RebarChars(CalcType.CL),
                RebarChars(CalcType.N), RebarChars(CalcType.NL)
            ]
        };
        var rebar = new MaterialArea
        {
            Id = 2, Tag = "5⌀14 + номинальный верх", Category = AreaCategory.RebarGroup,
            Material = steelMaterial, MaterialId = 2, DiagrammType = DiagrammType.L2,
            HostArea = concrete, HostAreaId = 1
        };
        for (int i = 0; i < 5; i++)
        {
            var bar = Fiber.CreatePoint(0.014, -0.4 + i * 0.2, -Height / 2 + Cover);
            bar.Area = 769e-6 / 5.0;
            rebar.Fibers.Add(bar);
        }
        var top = Fiber.CreatePoint(0.001, 0.0, Height / 2 - Cover);
        top.Area = 1e-6;
        rebar.Fibers.Add(top);

        var section = new CrossSection { Id = 1, Tag = "Пример 49", Areas = [concrete, rebar] };
        section.ResolveAndBuildDiagramms(rebarDifferentialDiagram: false);
        return section;
    }

    [Fact]
    public void Example49_FormulaDeflection_MatchesHandbookAndDeformationModel()
    {
        var section = Example49Section();
        // Арматура снизу (y < 0): растягивающий её момент — отрицательный Mx.
        var load = new LoadItem { Mx = -Moment };
        var options = new Sp63DeflectionOptions(Sp63NormalShapeKind.Rectangular, Sp63NormalAxis.Mx,
            Sp63DeflectionStaticScheme.SimplySupportedUniform, Span, 28.7, Sp63Humidity.From40To75,
            Sp63DeflectionForcesMode.Manual, 1.0);

        var result = Sp63DeflectionChecker.Check(section, load, load, CalcType.N, options);

        Assert.Equal(Sp63DeflectionStatus.Calculated, result.Status);
        Assert.True(result.Curvature!.Cracked);
        double f = result.DeflectionMm;

        var ndm = new TotalCurvatureSolver(section, solverTol: 0.05)
            .Compute(N: 0.0, mxLong: -Moment, myLong: 0.0, mxTotal: -Moment, myTotal: 0.0);
        Assert.True(ndm.AllConverged);
        Assert.True(ndm.Cracked);
        double fNdm = 1000.0 * 5.0 / 48.0 * Span * Span * ndm.KFull;

        output.WriteLine($"1/r формулы = {result.Curvature.Total:E4} 1/м, f = {f:F2} мм");
        output.WriteLine($"1/r НДМ = {ndm.KFull:E4} 1/м, f = {fNdm:F2} мм, Mcrc НДМ = {ndm.Mcrc:F2}");
        output.WriteLine($"f/32,6 = {f / 32.6:F3}; f/fНДМ = {f / fNdm:F3}");

        // Результаты 29.09.2026: f = 33,29 мм (+2,1 % к 32,6 мм Пособия), fНДМ = 25,80 мм.
        // Mcrc = 10,24 кН·м по (8.122) Wpl = 1,3·Wred действующей нормы (сверено с HTML); Пособие
        // берёт γ = 1,75 из старой табл. 4.1 (Mcrc = 13,51), НДМ — 14,68. Меньший Mcrc даёт больший
        // ψs, поэтому формулы в запас относительно НДМ (~29 %) при почти точном совпадении с f
        // Пособия, где запас набирают табличные φ1, φ2 формулы (4.48).
        Assert.Equal(10.24, result.Variables["McrcFull"], 1);
        Assert.InRange(f / 32.6, 1.00, 1.05);
        // Постоянная жёсткость по пролёту (8.2.21) не меньше уточнённого прогиба (4.37) Пособия.
        Assert.True(f > 25.94);
        // Вердикт совпадает с Пособием по (4.36): f > fult = 28,7 мм.
        Assert.False(result.DeflectionPassed);
        // Формулы консервативнее деформационной модели, но в разумных пределах.
        Assert.InRange(fNdm / f, 0.72, 0.85);
        Assert.InRange(ndm.Mcrc, 13.5, 15.5);
    }
}
