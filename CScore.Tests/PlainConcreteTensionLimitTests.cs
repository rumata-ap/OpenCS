using System;
using System.Linq;
using Xunit;

namespace CScore.Tests;

/// <summary>
/// Бетонное сечение с учётом растяжения: предел по п. 8.1.29 (ф. 8.52) — деформация наиболее
/// растянутой ВЕРШИНЫ контура εbt,max ≤ εbt,ult, а не центра крайней фибры. Раньше предел
/// наступал по потере равновесия фибр, и грань уходила за εbt2 на полтолщины крайней полосы.
/// </summary>
public class PlainConcreteTensionLimitTests
{
    static CrossSection PlainRect(int ny)
    {
        MaterialChars Ch(CalcType ct) => new(ct)
        {
            E = 30_000_000, Fc = -14_500, Ft = 1_050, Ec0 = -0.002, Ec2 = -0.0035,
            Ec1Red = -0.0015, Et0 = 0.0001, Et1Red = 0.00008, Et2 = 0.00015, Type = MatType.Concrete
        };
        var m = new Material { Id = 1, Tag = "B25", Type = MatType.Concrete, E = 30_000_000 };
        m.MaterialChars = [Ch(CalcType.C), Ch(CalcType.CL), Ch(CalcType.N), Ch(CalcType.NL)];
        var hull = new Contour(new[] { -0.15, 0.15, 0.15, -0.15, -0.15 }, new[] { -0.25, -0.25, 0.25, 0.25, -0.25 }, "r")
        { Type = ContourType.Hull };
        var area = new MaterialArea
        {
            Id = 1, Tag = "Бетон", Category = AreaCategory.Region, Material = m, MaterialId = 1,
            DiagrammType = DiagrammType.L2, Contours = [hull], NX = 1, NY = ny
        };
        area.Hull = hull;
        area.ResolveAndBuildDiagramms();
        area.SliceXY(1, ny);
        return new CrossSection { Tag = "plain", Areas = [area] };
    }

    [Theory]
    [InlineData("bisection", 10)]
    [InlineData("bisection", 50)]
    [InlineData("fast", 50)]
    public void Bending_EdgeStrainEqualsEpsBt2_GovernedByConcreteTension(string solver, int ny)
    {
        var s = LimitForceSolvers.Create(PlainRect(ny), CalcType.C, new LimitForceParams { Solver = solver });
        var r = s.AllFactor(0, 10, 0);

        Assert.True(r.Converged);
        Assert.Equal("concrete_tension", r.Governing);
        var k = r.StrainPlane!.Value;
        double edge = k.e0 + Math.Abs(k.ky) * 0.25;
        Assert.InRange(edge, 0.00015 * (1 - 1e-3), 0.00015 * (1 + 1e-4));
        Assert.Equal(0.00015, r.EpsBtMax!.Value, 7);
        Assert.Equal(0.00015, r.EpsBtUlt!.Value, 9);
    }

    [Fact]
    public void Bending_LimitFactorWeaklyDependsOnMesh()
    {
        double K(int ny) => LimitForceSolvers.Create(PlainRect(ny), CalcType.C).AllFactor(0, 10, 0).Factor;
        double k10 = K(10), k50 = K(50);
        Assert.True(Math.Abs(k10 - k50) / k50 < 0.03, $"k10={k10}, k50={k50}");
    }

    [Fact]
    public void SingleSignTension_UsesFormula854()
    {
        // ε1/ε2 = 0,5 → εbt,ult = εbt2 − (εbt2 − εbt0)·0,5 = 0,000125.
        var v = new[] { (0.0, 0.25, 0.0001, 0.00015), (0.0, -0.25, 0.0001, 0.00015) };
        var k = new Kurvature { e0 = 0.000075, ky = 0.0001, kz = 0 }; // верх 0,0001, низ 0,00005
        double u = PlainConcreteTension.Utilization(v, k, out double em, out double eu);
        Assert.Equal(0.0001, em, 9);
        Assert.Equal(0.000125, eu, 9);
        Assert.Equal(0.8, u, 6);
    }

    [Fact]
    public void ReinforcedSection_HasNoPlainTensionCriterion()
    {
        var sec = SectionCutFixtures.BuildReinforcedRectangle(0.3, 0.5);
        Assert.Empty(new CrossSectionLimitAdapter(sec).PlainConcreteTensionVertices);
    }
}
