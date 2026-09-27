using System.Text.Json;
using CScore;
using CScore.ParametricSteel;
using CScore.Sp16;
using OpenCS.Services;
using OpenCS.Tasks;
using Xunit;

namespace OpenCS.Tests;

/// <summary>It по МКЭ для проверок СП 16: точность, контур параметрического сечения, выбор источника в задаче.</summary>
public sealed class SteelTorsionConstantServiceTests
{
    static CrossSection Generate(ParametricSteelSectionDefinition definition, bool bind)
    {
        var generated = ParametricSteelSectionGenerator.Generate(definition with { MaterialId = 1 });
        Assert.Empty(generated.Diagnostics);
        var section = generated.Section;
        section.Areas[0].Material = new Material
        {
            Id = 1, Type = MatType.Steel, E = 2.06e8,
            MaterialChars = [new MaterialChars(CalcType.C) { Ry = 240000, Ru = 360000 }]
        };
        if (bind)
            section.ParametricSteel = new ParametricSteelBinding(generated.Profile!,
                ParametricSteelSectionFingerprint.Compute(section, ParametricSteelSectionGenerator.GeneratorVersion),
                ParametricSteelSectionGenerator.GeneratorVersion, definition with { MaterialId = 1 });
        return section;
    }

    /// <summary>Точное решение для прямоугольника b×t (ряд Сен-Венана).</summary>
    static double RectangleIt(double b, double t)
    {
        double sum = 0;
        for (int n = 1; n < 200; n += 2) sum += Math.Tanh(n * Math.PI * b / (2 * t)) / Math.Pow(n, 5);
        return b * t * t * t / 3 * (1 - 192 / Math.Pow(Math.PI, 5) * t / b * sum);
    }

    [Fact]
    public void RectangleMatchesSaintVenantSeries()
    {
        var section = Generate(ParametricSteelSectionDefinition.Plate(0.1, 0.01), bind: false);
        var r = SteelTorsionConstantService.Get(section, section.Areas[0], "rect-100x10", null);
        Assert.Equal(RectangleIt(0.1, 0.01), r.Value, RectangleIt(0.1, 0.01) * 1e-3);
        Assert.False(string.IsNullOrWhiteSpace(r.Details));
    }

    [Fact]
    public void ParametricIBeamUsesFineArcs()
    {
        // 30Ш2 по ГОСТ Р 57837: сходимость по сегментам дуги (8 → 32) даёт It ≈ 62,25 см⁴ (сортамент — 62,73).
        var d = ParametricSteelSectionDefinition.RolledIBeam(0.300, 0.201, 0.009, 0.015, 0.018);
        var bound = Generate(d, bind: true);
        var unbound = Generate(d, bind: false);
        var fine = SteelTorsionConstantService.Get(bound, bound.Areas[0], "30Ш2|bound", null);
        var coarse = SteelTorsionConstantService.Get(unbound, unbound.Areas[0], "30Ш2|unbound", null);
        Assert.Equal(62.25e-8, fine.Value, 0.1e-8);
        Assert.True(coarse.Value > fine.Value);                                                 // ломаная 8 сегм. завышает
        Assert.Equal(fine.Value, coarse.Value, 0.005 * fine.Value);
    }

    [Fact]
    public void TaskWithFemSourceReportsFemItInPhiB()
    {
        var section = Generate(ParametricSteelSectionDefinition.RolledIBeam(0.300, 0.201, 0.009, 0.015, 0.018), bind: true);
        var p = new SteelDesignParams { LefB = 6, ItSource = TorsionConstantSource.Fem };
        var result = TaskRunner.Run(new CalcTask { Kind = "steel_bending", ParamsJson = p.ToJson() }, section,
            new LoadItem { Mx = -100 });
        Assert.NotEqual("error", result.Status);
        using var doc = JsonDocument.Parse(result.DataJson);
        var notes = doc.RootElement.GetProperty("details").EnumerateArray()
            .SelectMany(d => d.GetProperty("notes").EnumerateArray().Select(n => n.GetString() ?? "")).ToList();
        Assert.Contains(notes, n => n.StartsWith("It = 62.2") && n.Contains("по МКЭ"));
    }
}
