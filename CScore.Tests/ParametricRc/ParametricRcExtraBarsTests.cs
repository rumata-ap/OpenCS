using System.Text.Json;
using System.Text.Json.Serialization;
using CScore;
using CScore.ParametricRc;
using Xunit;

namespace CScore.Tests.ParametricRc;

/// <summary>Отдельные стержни параметрического сечения (армирование по данным схемы-источника).</summary>
public sealed class ParametricRcExtraBarsTests
{
    static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter() },
    };

    static ParametricRcSectionDefinition Beam(params ParametricRebarPoint[] bars) =>
        ParametricRcSectionDefinition.Rectangle(0.30, 0.50) with { LongitudinalMaterialId = 7, ExtraBars = bars };

    [Fact]
    public void Generate_ExtraBarsArea_WithLongitudinalMaterialAndGivenAreas()
    {
        var result = ParametricRcSectionGenerator.Generate(Beam(
            new ParametricRebarPoint(-0.10, -0.20, 3.14e-4, 0.020),
            new(0.10, -0.20, 1.0e-4, 0.0113),
            new(0.0, 0.20, 2.0e-4, 0.016)));

        Assert.Empty(result.Diagnostics);
        var rebar = Assert.Single(result.Section.Areas, a => a.Category == AreaCategory.RebarGroup);
        Assert.Equal("Арматура по данным схемы", rebar.Tag);
        Assert.Equal(7, rebar.MaterialId);
        Assert.Equal(RebarRepresentation.PhysicalBars, rebar.RebarRepresentation);
        Assert.Equal(3, rebar.Fibers.Count);
        Assert.Equal(6.14e-4, rebar.Fibers.Sum(f => f.Area), 12);
        Assert.Contains(rebar.Fibers, f => f.X == 0.10 && f.Y == -0.20 && f.Diameter == 0.0113);
    }

    [Fact]
    public void Generate_NoExtraBars_NoArea()
    {
        var result = ParametricRcSectionGenerator.Generate(Beam());
        Assert.Empty(result.Diagnostics);
        Assert.Single(result.Section.Areas);
    }

    [Theory]
    [InlineData(0.145, 0.0, 0.016)]   // центр внутри, но стержень выходит за грань
    [InlineData(0.20, 0.0, 0.010)]    // вне контура
    public void Generate_BarOutsideContour_Diagnostic(double x, double y, double d)
    {
        var result = ParametricRcSectionGenerator.Generate(Beam(new ParametricRebarPoint(x, y, 1e-4, d)));
        Assert.Contains(result.Diagnostics, m => m.Contains("выходят за контур"));
    }

    [Fact]
    public void Generate_ZeroArea_Diagnostic_AndCircleRejected()
    {
        Assert.Contains(ParametricRcSectionGenerator.Generate(Beam(new ParametricRebarPoint(0, 0, 0, 0.01))).Diagnostics,
            m => m.Contains("положительными"));
        var circle = ParametricRcSectionDefinition.Circle(0.6) with
        {
            PolarRebar = new ParametricPolarRebar(8, 0.016, 0.24),
            ExtraBars = [new(0, 0, 1e-4, 0.01)],
        };
        Assert.Contains(ParametricRcSectionGenerator.Generate(circle).Diagnostics, m => m.Contains("круга и кольца"));
    }

    [Fact]
    public void Json_RoundTrip_AndOldRecordWithoutField()
    {
        var definition = Beam(new ParametricRebarPoint(-0.1, -0.2, 3.14e-4, 0.02));
        var back = JsonSerializer.Deserialize<ParametricRcSectionDefinition>(JsonSerializer.Serialize(definition, Json), Json)!;
        Assert.Equal(definition.ExtraBars, back.ExtraBars);

        string old = JsonSerializer.Serialize(ParametricRcSectionDefinition.Rectangle(0.3, 0.5), Json)
            .Replace(",\"extraBars\":[]", "");
        Assert.DoesNotContain("extraBars", old);
        var legacy = JsonSerializer.Deserialize<ParametricRcSectionDefinition>(old, Json)!;
        Assert.Empty(legacy.ExtraBars);
    }
}
