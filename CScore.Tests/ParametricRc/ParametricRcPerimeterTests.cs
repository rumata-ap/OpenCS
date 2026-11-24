using System.Text.Json;
using System.Text.Json.Serialization;
using CScore;
using CScore.ParametricRc;
using Xunit;

namespace CScore.Tests.ParametricRc;

/// <summary>Контурная раскладка «угловые + промежуточные стержни» прямоугольного сечения.</summary>
public sealed class ParametricRcPerimeterTests
{
    static ParametricRcSectionDefinition Column(ParametricPerimeterRebar p) =>
        ParametricRcSectionDefinition.Rectangle(0.40, 0.60) with { LongitudinalMaterialId = 7, PerimeterRebar = p };

    [Fact]
    public void Generate_CornersAndIntermediate_CountsAreaAndPositions()
    {
        // 4 угла Ø20 + по 1 Ø16 сверху/снизу + по 2 Ø16 слева/справа = 4 + 2 + 4 = 10 стержней
        var def = Column(new(0.020, 0.016, 0.04, 1, 2));
        var result = ParametricRcSectionGenerator.Generate(def);

        Assert.Empty(result.Diagnostics);
        var rebar = Assert.Single(result.Section.Areas, a => a.Category == AreaCategory.RebarGroup);
        Assert.Equal(10, rebar.Fibers.Count);
        Assert.Equal(7, rebar.MaterialId);
        double expected = 4 * Math.PI * 0.020 * 0.020 / 4 + 6 * Math.PI * 0.016 * 0.016 / 4;
        Assert.Equal(expected, rebar.Fibers.Sum(f => f.Area), 12);
        Assert.Contains(rebar.Fibers, f => Math.Abs(f.X + 0.16) < 1e-12 && Math.Abs(f.Y + 0.26) < 1e-12 && f.Diameter == 0.020);
        Assert.Contains(rebar.Fibers, f => Math.Abs(f.X) < 1e-12 && Math.Abs(f.Y - 0.26) < 1e-12 && f.Diameter == 0.016);
        Assert.Contains(rebar.Fibers, f => Math.Abs(f.X - 0.16) < 1e-12 && Math.Abs(f.Y + 0.26 / 3) < 1e-12);
    }

    [Fact]
    public void Generate_ZeroIntermediate_OnlyFourCorners()
    {
        var result = ParametricRcSectionGenerator.Generate(Column(new(0.025, 0.016, 0.05, 0, 0)));
        Assert.Empty(result.Diagnostics);
        Assert.Equal(4, result.Section.Areas.Single(a => a.Category == AreaCategory.RebarGroup).Fibers.Count);
    }

    [Fact]
    public void Generate_WithEnabledLayer_Diagnostic()
    {
        var def = Column(new(0.020, 0.016, 0.04, 1, 1)) with
        {
            LowerRebar = ParametricLongitudinalLayer.Physical(2, 0.016, -0.26)
        };
        Assert.Contains(ParametricRcSectionGenerator.Generate(def).Diagnostics, m => m.Contains("несовместима"));
    }

    [Theory]
    [InlineData(0.020, 0.016, 0.005, 1, 1)]   // стержень выходит за грань
    [InlineData(0.020, 0.016, 0.04, 20, 1)]   // не помещаются по ширине
    [InlineData(0.0, 0.016, 0.04, 1, 1)]      // нулевой диаметр
    public void Generate_BadInput_Diagnostic(double dc, double di, double cover, int nw, int nh)
    {
        Assert.NotEmpty(ParametricRcSectionGenerator.Generate(Column(new(dc, di, cover, nw, nh))).Diagnostics);
    }

    [Fact]
    public void Generate_NotRectangle_Diagnostic()
    {
        var tee = ParametricRcSectionDefinition.Tee(0.5, 0.6, 0.2, 0.1) with
        {
            PerimeterRebar = new(0.02, 0.016, 0.04, 1, 1)
        };
        Assert.Contains(ParametricRcSectionGenerator.Generate(tee).Diagnostics, m => m.Contains("прямоугольного"));
    }

    [Fact]
    public void Json_RoundTrip_AndOldRecordWithoutField()
    {
        var opt = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            Converters = { new JsonStringEnumConverter() },
        };
        var def = Column(new(0.020, 0.016, 0.04, 1, 2));
        var back = JsonSerializer.Deserialize<ParametricRcSectionDefinition>(JsonSerializer.Serialize(def, opt), opt)!;
        Assert.Equal(def.PerimeterRebar, back.PerimeterRebar);

        var legacy = JsonSerializer.Deserialize<ParametricRcSectionDefinition>(
            JsonSerializer.Serialize(ParametricRcSectionDefinition.Rectangle(0.3, 0.5), opt)
                .Replace(",\"perimeterRebar\":null", ""), opt)!;
        Assert.Null(legacy.PerimeterRebar);
    }
}
