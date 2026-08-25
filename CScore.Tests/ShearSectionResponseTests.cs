using CScore;
using Xunit;

namespace CScore.Tests;

/// <summary>Проверки расширенного ответа стержневого сечения с поперечным сдвигом.</summary>
public sealed class ShearSectionResponseTests
{
    const double G = 12_000_000.0;
    const double Avy = 0.09;
    const double Avz = 0.07;

    [Fact]
    public void ElasticTimoshenko_ComputesTwoIndependentShearForces()
    {
        var section = TestSections.RectWithBottomRebar();
        var deformation = new ShearSectionDeformation(0.0001, 0.0002, -0.00015, 0.0012, -0.0008);

        var actual = section.ComputeShearResponse(
            deformation,
            SectionResponseOptions.ElasticTimoshenko(G, Avy, Avz));
        var expected = section.Compute(new Kurvature
        {
            e0 = deformation.AxialStrain,
            ky = deformation.CurvatureY,
            kz = deformation.CurvatureZ
        });

        Assert.Equal(expected.N, actual.Forces.N, 6);
        Assert.Equal(expected.Mx, actual.Forces.Mx, 6);
        Assert.Equal(expected.My, actual.Forces.My, 6);
        Assert.Equal(G * Avy * deformation.ShearStrainY, actual.Forces.Vy, 6);
        Assert.Equal(G * Avz * deformation.ShearStrainZ, actual.Forces.Vz, 6);
    }

    [Fact]
    public void ElasticTimoshenko_ProducesBlockDiagonalFiveByFiveTangent()
    {
        var section = TestSections.RectWithBottomRebar();
        var deformation = new ShearSectionDeformation(0.0001, 0.0002, -0.00015, 0.0012, -0.0008);

        var actual = section.ComputeShearResponse(
            deformation,
            SectionResponseOptions.ElasticTimoshenko(G, Avy, Avz));
        var expected = section.Compute(new Kurvature
        {
            e0 = deformation.AxialStrain,
            ky = deformation.CurvatureY,
            kz = deformation.CurvatureZ
        });

        var tangent = Assert.IsType<double[,]>(actual.Tangent);
        Assert.Equal(5, tangent.GetLength(0));
        Assert.Equal(5, tangent.GetLength(1));
        var expectedTangent = Assert.IsType<double[,]>(expected.Tangent);
        for (int row = 0; row < 3; row++)
        for (int column = 0; column < 3; column++)
            Assert.Equal(expectedTangent[row, column], tangent[row, column], 6);

        Assert.Equal(G * Avy, tangent[3, 3], 6);
        Assert.Equal(G * Avz, tangent[4, 4], 6);
        for (int row = 0; row < 5; row++)
        for (int column = 0; column < 5; column++)
            if ((row >= 3 || column >= 3) && row != column)
                Assert.Equal(0.0, tangent[row, column]);
    }

    [Fact]
    public void ElasticTimoshenko_ComputeStiffnessFalse_StillReturnsShearForces()
    {
        var section = TestSections.RectWithBottomRebar();
        var deformation = new ShearSectionDeformation(0.0001, 0.0002, -0.00015, 0.0012, -0.0008);

        var actual = section.ComputeShearResponse(
            deformation,
            SectionResponseOptions.ElasticTimoshenko(G, Avy, Avz),
            computeStiffness: false);

        Assert.Null(actual.Tangent);
        Assert.Equal(G * Avy * deformation.ShearStrainY, actual.Forces.Vy, 6);
        Assert.Equal(G * Avz * deformation.ShearStrainZ, actual.Forces.Vz, 6);
    }

    [Fact]
    public void NormalPlaneSections_RejectsNonzeroShearStrains()
    {
        var section = TestSections.RectWithBottomRebar();
        var deformation = new ShearSectionDeformation(0.0, 0.0, 0.0, 0.0001, 0.0);

        Assert.Throws<ArgumentException>(() => section.ComputeShearResponse(
            deformation,
            SectionResponseOptions.NormalPlaneSections()));
    }

    [Fact]
    public void NormalPlaneSections_ZeroShearStrainsReturnsZeroShearForces()
    {
        var section = TestSections.RectWithBottomRebar();

        var actual = section.ComputeShearResponse(
            new ShearSectionDeformation(0.0001, 0.0002, -0.00015, 0.0, 0.0),
            SectionResponseOptions.NormalPlaneSections());

        Assert.Equal(0.0, actual.Forces.Vy);
        Assert.Equal(0.0, actual.Forces.Vz);
    }

    [Theory]
    [InlineData(SectionResponseTheory.UserDefinedShearDiagram)]
    [InlineData(SectionResponseTheory.Mcft)]
    public void UnsupportedTheories_AreRejectedExplicitly(SectionResponseTheory theory)
    {
        var section = TestSections.RectWithBottomRebar();

        Assert.Throws<NotSupportedException>(() => section.ComputeShearResponse(
            new ShearSectionDeformation(0.0, 0.0, 0.0, 0.0, 0.0),
            new SectionResponseOptions(theory)));
    }

    [Fact]
    public void NonfiniteDeformation_IsRejectedBeforeSectionCalculation()
    {
        var section = TestSections.RectWithBottomRebar();

        Assert.Throws<ArgumentOutOfRangeException>(() => section.ComputeShearResponse(
            new ShearSectionDeformation(double.NaN, 0.0, 0.0, 0.0, 0.0),
            SectionResponseOptions.NormalPlaneSections()));
    }

    public static IEnumerable<object[]> InvalidElasticOptions =>
    [
        [0.0, Avy, Avz],
        [-G, Avy, Avz],
        [double.NaN, Avy, Avz],
        [double.PositiveInfinity, Avy, Avz],
        [G, 0.0, Avz],
        [G, -Avy, Avz],
        [G, double.NaN, Avz],
        [G, Avy, 0.0],
        [G, Avy, -Avz],
        [G, Avy, double.PositiveInfinity]
    ];

    [Theory]
    [MemberData(nameof(InvalidElasticOptions))]
    public void ElasticTimoshenko_RejectsNonpositiveOrNonfiniteProperties(double g, double avy, double avz)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            SectionResponseOptions.ElasticTimoshenko(g, avy, avz));
    }

    [Fact]
    public void LegacyIntegralAndCompute_DoNotPopulateLoadShearAndTorsion()
    {
        var section = TestSections.RectWithBottomRebar();
        var curvature = new Kurvature { e0 = 0.0001, ky = 0.0002, kz = -0.00015 };

        var integral = section.Integral(curvature);
        var result = section.Compute(curvature);

        Assert.Equal(0.0, integral.Qy);
        Assert.Equal(0.0, integral.Qz);
        Assert.Equal(0.0, integral.T);
        Assert.Equal(integral.N, result.N, 6);
        Assert.Equal(integral.Mx, result.Mx, 6);
        Assert.Equal(integral.My, result.My, 6);
    }
}
