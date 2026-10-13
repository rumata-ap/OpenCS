using CScore.Planar;
using CScore.PlateStrip;
using Xunit;

namespace CScore.Tests.PlateStrip;

/// <summary>Отверстия региона: ширина материала полосы, доли в ячейках квадратуры и масштабирование отклика.</summary>
public sealed class StripMaterialProfileTests
{
    /// <summary>Полоса вдоль U на отметке V = 5, ширина 2 м, регион 10×10 с отверстием 4…6 × 4.5…5.5.</summary>
    static PlateStripBeamAnalogy StripWithHole(bool withHole = true)
    {
        var region = PlanarRegion.CreateFromContour(
            new Contour { X = [0, 10, 10, 0], Y = [0, 0, 10, 10] },
            withHole ? [new Contour { X = [4, 6, 6, 4], Y = [4.5, 4.5, 5.5, 5.5] }] : null);
        SupportLocus At(double u, double v) => new() { Frame = Frame3D.Identity with { Origin = new PlanarVector3(u, v, 0) } };
        return PlateStripGeometryBuilder.Build("s", region, At(1, 5), At(9, 5), 2.0).Analogy!;
    }

    [Fact]
    public void WithoutHole_MaterialIsWholeWidth()
    {
        var strip = StripWithHole(withHole: false);

        Assert.Equal(2.0, StripMaterialProfile.MaterialWidth(strip, 0.5), 9);
        Assert.All(StripMaterialProfile.CellFractions(strip, 0.5, 4), f => Assert.Equal(1.0, f, 12));
    }

    [Fact]
    public void HoleInMiddle_RemovesItsWidthOnlyAtItsStations()
    {
        var strip = StripWithHole();

        // Отверстие: u ∈ [4,6] → доля оси (4−1)/8 … (6−1)/8 = 0,375…0,625.
        Assert.Equal(2.0, StripMaterialProfile.MaterialWidth(strip, 0.2), 9);
        Assert.Equal(1.0, StripMaterialProfile.MaterialWidth(strip, 0.5), 9);
        Assert.Equal(2.0, StripMaterialProfile.MaterialWidth(strip, 0.8), 9);

        var intervals = StripMaterialProfile.Intervals(strip, 0.5);
        Assert.Equal(2, intervals.Count);
        Assert.Equal((-1.0, -0.5), (intervals[0].V0, intervals[0].V1));
        Assert.Equal((0.5, 1.0), (intervals[1].V0, intervals[1].V1));
    }

    [Theory]
    [InlineData(2)]
    [InlineData(4)]
    [InlineData(8)]
    public void CellFractions_SumToMaterialWidth(int points)
    {
        var strip = StripWithHole();
        var (_, weights) = EquivalentSectionCalculator.WidthGaussPoints(strip.ExplicitWidthM, points);

        var fractions = StripMaterialProfile.CellFractions(strip, 0.5, points);

        double material = fractions.Zip(weights, (f, w) => f * w).Sum();
        Assert.Equal(1.0, material, 9);
        Assert.Contains(fractions, f => f < 1.0);
    }

    [Fact]
    public void ElementFractions_AverageOverElementLength()
    {
        var strip = StripWithHole();

        // Элемент 0,25…0,75 длины: отверстие (0,375…0,625) занимает половину его длины, по ширине — половину.
        var fractions = StripMaterialProfile.ElementFractions(strip, 0.25, 0.75, 2, samples: 200);
        var (_, weights) = EquivalentSectionCalculator.WidthGaussPoints(2.0, 2);

        double meanWidth = fractions.Zip(weights, (f, w) => f * w).Sum();
        Assert.Equal(1.5, meanWidth, 2);   // 2 − (0,5 длины × 1 м ширины) = 1,5
    }

    static ConstantLinearPlateSectionResponse Source(double a00 = 1000.0, double d00 = 300.0)
    {
        var a = new double[3, 3];
        var b = new double[3, 3];
        var d = new double[3, 3];
        var ass = new double[2, 2];
        a[0, 0] = a00;
        d[0, 0] = d00;
        a[1, 1] = 500.0;
        d[1, 1] = 100.0;
        ass[0, 0] = ass[1, 1] = 400.0;
        return new ConstantLinearPlateSectionResponse(a, b, d, ass, "source-fp");
    }

    [Fact]
    public void ScaledResponse_ScalesForcesTangentAndFingerprint()
    {
        var inner = Source();
        var scaled = ScaledPlateSectionResponse.Apply(inner, 0.25);

        var state = new ShellStrainState(0.001, 0, 0, 0.002, 0, 0);
        var f0 = inner.Forces(state);
        var f1 = scaled.Forces(state);
        var t0 = inner.Tangent(state);
        var t1 = scaled.Tangent(state);

        Assert.Equal(0.25 * f0.Nx, f1.Nx, 12);
        Assert.Equal(0.25 * f0.Mx, f1.Mx, 12);
        Assert.Equal(0.25 * t0.A[0, 0], t1.A[0, 0], 12);
        Assert.Equal(0.25 * t0.D[0, 0], t1.D[0, 0], 12);
        Assert.Equal(0.25 * t0.As[0, 0], t1.As[0, 0], 12);
        Assert.NotEqual(inner.Fingerprint, scaled.Fingerprint);
        Assert.Same(inner, ScaledPlateSectionResponse.Apply(inner, 1.0));
    }

    [Fact]
    public void EquivalentSection_AtHole_HasStiffnessOfMaterialWidth()
    {
        var strip = StripWithHole();
        var source = Source();

        EquivalentSection Build(double station, int points)
        {
            var fractions = StripMaterialProfile.CellFractions(strip, station, points);
            var sources = fractions.Select(f => ScaledPlateSectionResponse.Apply(source, f)).ToArray();
            double width = StripMaterialProfile.MaterialWidth(strip, station);
            return EquivalentSectionCalculator.Build(
                strip, source, sources, ReductionPolicy.ConstitutiveIntegration, points,
                StripBeamFormulation.EulerBernoulli, width).Section!;
        }

        var solid = Build(0.1, 4);    // сечение вне отверстия: ширина 2 м
        var holed = Build(0.5, 4);    // в отверстии: 1 м материала из 2 м

        // Мембранная и изгибная жёсткости пропорциональны ширине материала.
        Assert.Equal(0.5, holed.EA / solid.EA, 9);
        Assert.Equal(0.5, holed.EIy / solid.EIy, 9);
        // Изгиб в плоскости (EIz ~ ∫v² dA): материал убран из середины — жёсткость падает слабее ширины.
        Assert.True(holed.EIz / solid.EIz > 0.5);
        Assert.True(holed.EIz < solid.EIz);
    }
}
