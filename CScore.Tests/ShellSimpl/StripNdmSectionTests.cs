using CScore;
using CScore.ShellSimpl;
using Xunit;
using static CScore.Tests.ShellSimpl.StripNdmFixture;

namespace CScore.Tests.ShellSimpl;

/// <summary>Интегрирование полосы <see cref="StripNdmSection"/>: аналитика упругой зоны, стержневой
/// НДМ (<see cref="CrossSection"/>) и касательная матрица против конечных разностей.</summary>
public sealed class StripNdmSectionTests
{
    [Fact]
    public void ElasticRange_MatchesAnalyticalStiffness()
    {
        // Все волокна сжаты и в упругой зоне L3 (|ε| < εb1 = 0,6·Rb/Eb = 2,9e-4).
        var s = Strip(CalcType.C);
        double e0 = -5e-5, k = 2e-4;
        var f = s.Integrate(e0, k, tensionConcrete: false);

        double eb = 30_000_000.0, es = 200_000_000.0;
        double n = eb * H * e0, m = eb * H * H * H / 12.0 * k;
        foreach (double z in new[] { Z, -Z })
        {
            n += es * AsPerMeter * (e0 + k * z);
            m += es * AsPerMeter * (e0 + k * z) * z;
        }
        Assert.Equal(n, f.N, Math.Abs(n) * 1e-9);
        Assert.Equal(m, f.M, Math.Abs(m) * 1e-9);
        Assert.Equal(eb * H + 2 * es * AsPerMeter, f.K00, 1e-6 * f.K00);
    }

    static CrossSection BarStrip()
    {
        var concreteMaterial = Concrete();
        var rebarMaterial = Rebar();
        double y0 = -H / 2.0, y1 = H / 2.0, x0 = -0.5, x1 = 0.5;
        var concrete = new MaterialArea
        {
            Tag = "concrete", Category = AreaCategory.Region,
            Material = concreteMaterial, MaterialId = concreteMaterial.Id,
            DiagrammType = DiagrammType.L2,
            Hull = new Contour([x0, x1, x1, x0, x0], [y0, y0, y1, y1, y0], "outer"),
        };
        concrete.SetWKT();
        concrete.SliceXY(nx: 1, ny: 400);
        var areas = new List<MaterialArea> { concrete };
        int i = 0;
        foreach (double y in new[] { Z, -Z })
        {
            var fiber = Fiber.CreatePoint(0.001, 0.0, y);
            fiber.Area = AsPerMeter;
            areas.Add(new MaterialArea
            {
                Tag = $"rebar_{i++}", Category = AreaCategory.RebarGroup,
                Material = rebarMaterial, MaterialId = rebarMaterial.Id,
                DiagrammType = DiagrammType.L2, Fibers = [fiber],
            });
        }
        var section = new CrossSection { Areas = areas };
        section.ResolveAndBuildDiagramms(rebarDifferentialDiagram: false);
        return section;
    }

    // Полоса 1 м × 0,2 с двумя рядами — то же, что стержневое сечение 1000 × 200 (ось y стержня = z полосы).
    [Theory]
    [InlineData(0.0003, 0.02)]      // изгиб, верх растянут, арматура потекла
    [InlineData(-0.0005, -0.015)]   // изгиб другого знака
    [InlineData(-0.0012, 0.005)]    // внецентренное сжатие, сжатие по всей высоте
    [InlineData(0.002, 0.01)]       // растяжение с малой сжатой зоной
    [InlineData(-0.0001, 0.03)]     // глубокая пластика: бетон на площадке εb1,red…εb2
    public void MatchesBarNdm_CrossSection(double e0, double k)
    {
        var bar = BarStrip();
        var load = bar.Integral(new Kurvature { e0 = e0, ky = k, kz = 0.0 }, CalcType.C, ten: false, ca: true);

        var strip = Strip(CalcType.C, DiagrammType.L2);
        var f = strip.Integrate(e0, k, tensionConcrete: false);

        double scale = Math.Max(Math.Abs(load.N), 50.0);
        Assert.InRange(f.N, load.N - 0.005 * scale, load.N + 0.005 * scale);
        double mScale = Math.Max(Math.Abs(load.Mx), 5.0);
        Assert.InRange(f.M, load.Mx - 0.005 * mScale, load.Mx + 0.005 * mScale);
    }

    [Theory]
    [InlineData(0.0004, 0.011, false, DiagrammType.L3)]
    [InlineData(-0.0011, 0.004, false, DiagrammType.L3)]
    [InlineData(-0.0006, -0.017, false, DiagrammType.SP63)]
    [InlineData(-0.00002, 0.0009, true, DiagrammType.L3)]
    public void Tangent_MatchesFiniteDifferences(double e0, double k, bool ten, DiagrammType type)
    {
        var s = Strip(CalcType.C, type);
        var f = s.Integrate(e0, k, ten);
        double de = 1e-9, dk = 1e-8;
        var fe = s.Integrate(e0 + de, k, ten); var fe_ = s.Integrate(e0 - de, k, ten);
        var fk = s.Integrate(e0, k + dk, ten); var fk_ = s.Integrate(e0, k - dk, ten);

        double k00 = (fe.N - fe_.N) / (2 * de), k10 = (fe.M - fe_.M) / (2 * de);
        double k01 = (fk.N - fk_.N) / (2 * dk), k11 = (fk.M - fk_.M) / (2 * dk);

        Assert.Equal(k00, f.K00, Math.Abs(k00) * 1e-3 + 1.0);
        Assert.Equal(k10, f.K01, Math.Abs(f.K00) * H * 1e-3);
        Assert.Equal(k01, f.K01, Math.Abs(f.K00) * H * 1e-3);
        Assert.Equal(k11, f.K11, Math.Abs(k11) * 1e-3 + 1e-3);
    }
}
