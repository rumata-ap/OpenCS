using System.Text.Json;
using CScore;
using OpenCS.Tasks;
using OpenCS.Utilites;
using Xunit;

namespace OpenCS.Tests;

/// <summary>
/// Новые поля результата crack_width: σs ≤ Rs,ser (п. 8.2.16), геометрия по направлению плоскости,
/// случай «длительная ниже Mcrc» (плоскость — от полной нагрузки).
/// </summary>
public class CrackWidthHandlerSteelCheckTests
{
    /// <summary>Балка 300×600 (B25, A500 с Rs,ser = 435 МПа во всех видах расчёта), 2Ø28 понизу.</summary>
    static CrossSection Beam()
    {
        var section = ReportFixtures.BuildBeam();
        var rebar = section.Areas[1];
        rebar.Fibers.Clear();
        rebar.Fibers.Add(Fiber.CreatePoint(0.028, -0.08, -0.25));
        rebar.Fibers.Add(Fiber.CreatePoint(0.028, 0.08, -0.25));
        section.ResolveAndBuildDiagramms();
        return section;
    }

    static JsonElement Run(string paramsJson, double mx)
    {
        var result = new CrackWidthHandler().Run(
            new CalcTask { Id = 1, Kind = "crack_width", Tag = "acrc", CalcType = CalcType.N, ParamsJson = paramsJson },
            Beam(), new LoadItem { N = 0.0, Mx = mx, My = 0.0 }, CalcSettings.Default);
        Assert.NotEqual("error", result.Status);
        return JsonDocument.Parse(result.DataJson).RootElement.Clone();
    }

    [Fact]
    public void Cracked_WritesSteelCheckAndGeometry()
    {
        var root = Run("{}", -150.0);

        Assert.True(root.GetProperty("cracked").GetBoolean());
        Assert.Equal(435.0, root.GetProperty("rs_ser").GetDouble(), 6);
        double util = root.GetProperty("util_steel").GetDouble();
        double sigma = root.GetProperty("sigma_s_total").GetDouble();
        Assert.Equal(sigma / 435.0, util, 3);
        Assert.Equal(util <= 1.0, root.GetProperty("passed_steel").GetBoolean());
        Assert.Equal(600.0, root.GetProperty("h").GetDouble(), 6);
        Assert.True(root.GetProperty("util_concrete").GetDouble() > 0.0);
        Assert.False(root.GetProperty("onset_by_axial_force").GetBoolean());
        Assert.False(root.GetProperty("plane_is_total").GetBoolean());
    }

    [Fact]
    public void LongBelowMcrc_PlaneFromTotalLoad()
    {
        var root = Run("{\"ForcesMode\":\"share\",\"LongShare\":0.05}", -150.0);

        Assert.True(root.GetProperty("cracked").GetBoolean());
        Assert.Equal(0.0, root.GetProperty("acrc_long").GetDouble());
        Assert.True(root.GetProperty("acrc_short").GetDouble() > 0.0);
        Assert.True(root.GetProperty("plane_is_total").GetBoolean());
        Assert.True(root.GetProperty("plane_converged").GetBoolean());
    }
}
