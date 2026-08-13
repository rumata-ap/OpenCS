using CScore;
using OpenCS.OpenSees.CScore;
using OpenCS.OpenSees.Tests.Fixtures;

namespace OpenCS.OpenSees.Tests;

/// <summary>Проверки МНК-подгонки плоскости и интегрирования усилий по записанным волокнам.</summary>
public sealed class FemRecordedSectionReducerTests
{
    static CrossSection BuildSection()
    {
        var (section, _, _) = CrossSectionFixtures.RectangularSection();
        section.ResolveAndBuildDiagramms(0.85, pool: null, rebarDifferentialDiagram: false);
        return section;
    }

    [Fact]
    public void Reduce_KnownPlane_FitsPlaneAndIntegratesForces()
    {
        var section = BuildSection();
        const double e0 = 0.001, kz = 0.0002, ky = 0.0003;
        var fibers = section.EnumerateRecordedFibers(new Kurvature(), CalcType.C).ToList();
        var recorded = new Dictionary<int, (double StressPa, double Strain)>();
        foreach (var (_, fiber, index) in fibers)
        {
            double eps = e0 + kz * fiber.X + ky * fiber.Y;
            recorded[index] = (30_000_000.0 * eps, eps);
        }

        var summary = FemRecordedSectionReducer.Reduce(section, CalcType.C, recorded);

        Assert.Equal(e0, summary.Plane.e0, 8);
        Assert.Equal(ky, summary.Plane.ky, 8);
        Assert.Equal(kz, summary.Plane.kz, 8);

        double expectedN = 0, expectedMx = 0, expectedMy = 0;
        foreach (var (_, fiber, index) in fibers)
        {
            double sig = recorded[index].StressPa;
            expectedN += sig * fiber.Area;
            expectedMx += sig * fiber.Area * fiber.Y;
            expectedMy += sig * fiber.Area * fiber.X;
        }
        Assert.Equal(expectedN, summary.N, 8);
        Assert.Equal(expectedMx, summary.Mx, 8);
        Assert.Equal(expectedMy, summary.My, 8);
    }

    [Fact]
    public void Reduce_PointFibers_ProduceRebarRows()
    {
        var section = BuildSection();
        var recorded = new Dictionary<int, (double StressPa, double Strain)>();
        foreach (var (_, fiber, index) in section.EnumerateRecordedFibers(new Kurvature(), CalcType.C))
            recorded[index] = (200_000_000.0, 0.001);

        var summary = FemRecordedSectionReducer.Reduce(section, CalcType.C, recorded);

        var row = Assert.Single(summary.Rebar);
        Assert.Equal(1, row.Num);
        Assert.Equal(200.0, row.SigmaMpa, 8);
        Assert.Equal(0.001, row.Eps, 12);
        Assert.Equal(0.001, summary.EpsMax, 12);
    }

    [Fact]
    public void Reduce_NoRecordedMatch_ReturnsZeroForces()
    {
        var section = BuildSection();

        var summary = FemRecordedSectionReducer.Reduce(section, CalcType.C,
            new Dictionary<int, (double StressPa, double Strain)>());

        Assert.Equal(0, summary.N, 8);
        Assert.Equal(0, summary.Mx, 8);
        Assert.Equal(0, summary.My, 8);
        Assert.Empty(summary.Rebar);
    }
}
