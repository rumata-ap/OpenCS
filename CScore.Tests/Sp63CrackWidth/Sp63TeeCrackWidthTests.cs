using CScore;
using CScore.Sp63.CrackWidth;
using CScore.Sp63.Normal;
using CScore.Tests.Sp63Fixtures;
using Xunit;

namespace CScore.Tests.Sp63CrackWidth;

/// <summary>
/// Тесты формульной кривизны тавра/двутавра через полосовой профиль (новый overload
/// <see cref="Sp63Curvature.Compute(Sp63CurvatureSectionInput, Sp63CurvatureLoad, Sp63CurvatureLoad, bool, double, double)"/>).
/// </summary>
public sealed class Sp63TeeCurvatureTests
{
    static readonly Material Concrete = Sp63SlsTestSections.B25();
    static readonly Material Rebar = Sp63SlsTestSections.A400();
    static MaterialChars CChars => Concrete.GetChars(CalcType.N)!;
    static MaterialChars RChars => Rebar.GetChars(CalcType.N)!;

    static Sp63CurvatureSectionInput TeeInput(double concreteClass = 25)
    {
        var geometry = Sp63SlsTestSections.GeometryOrFail(
            Sp63SlsTestSections.TopTee(), Sp63NormalShapeKind.Tee,
            Sp63NormalAxis.Mx, tensionDirection: -1);
        return new Sp63CurvatureSectionInput(geometry,
            Concrete.E, Math.Abs(CChars.Fc), Rebar.E, concreteClass, Sp63Humidity.From40To75);
    }

    static Sp63CurvatureSectionInput SymmetricIInput(int tensionDirection)
    {
        var geometry = Sp63SlsTestSections.GeometryOrFail(
            Sp63SlsTestSections.ISection(topLayerArea: 20.36e-4, bottomLayerArea: 20.36e-4),
            Sp63NormalShapeKind.Tee, Sp63NormalAxis.Mx, tensionDirection);
        return new Sp63CurvatureSectionInput(geometry,
            Concrete.E, Math.Abs(CChars.Fc), Rebar.E, 25, Sp63Humidity.From40To75);
    }

    [Fact]
    public void Tee_Uncracked_TwoTermsPositiveStiffness()
    {
        var result = Sp63Curvature.Compute(TeeInput(),
            new Sp63CurvatureLoad(80.0, 10.0), new Sp63CurvatureLoad(20.0, 4.0),
            cracked: false, mcrcFull: 30.0, mcrcLong: 10.0);

        Assert.NotNull(result);
        Assert.Equal(2, result!.Terms.Count);
        Assert.All(result.Terms, term => Assert.True(term.D > 0));
        Assert.True(double.IsFinite(result.Total));
    }

    [Fact]
    public void Tee_Cracked_ThreeTerms()
    {
        var result = Sp63Curvature.Compute(TeeInput(),
            new Sp63CurvatureLoad(80.0, 10.0), new Sp63CurvatureLoad(20.0, 4.0),
            cracked: true, mcrcFull: 30.0, mcrcLong: 10.0);

        Assert.NotNull(result);
        Assert.Equal(3, result!.Terms.Count);
        Assert.All(result.Terms, term =>
        {
            Assert.True(term.Xm > 0);
            Assert.True(term.IRed > 0);
        });
    }

    [Fact]
    public void Tee_CrackedStiffness_NotAboveUncracked()
    {
        var loads = (new Sp63CurvatureLoad(80.0, 10.0), new Sp63CurvatureLoad(20.0, 4.0));
        var uncracked = Sp63Curvature.Compute(TeeInput(), loads.Item1, loads.Item2,
            cracked: false, mcrcFull: 30.0, mcrcLong: 10.0);
        var cracked = Sp63Curvature.Compute(TeeInput(), loads.Item1, loads.Item2,
            cracked: true, mcrcFull: 30.0, mcrcLong: 10.0);

        Assert.NotNull(uncracked);
        Assert.NotNull(cracked);
        double dUncracked = uncracked!.Terms.Min(term => term.D);
        double dCracked = cracked!.Terms.Min(term => term.D);
        Assert.True(dCracked <= dUncracked + 1e-9,
            $"D с трещинами ({dCracked}) выше D без трещин ({dUncracked}).");
    }

    [Fact]
    public void Tee_AxialForce_EntersThroughMredNotLost()
    {
        var result = Sp63Curvature.Compute(TeeInput(),
            new Sp63CurvatureLoad(80.0, 10.0), new Sp63CurvatureLoad(20.0, 4.0),
            cracked: true, mcrcFull: 30.0, mcrcLong: 10.0);
        var noAxial = Sp63Curvature.Compute(TeeInput(),
            new Sp63CurvatureLoad(80.0, 0.0), new Sp63CurvatureLoad(20.0, 0.0),
            cracked: true, mcrcFull: 30.0, mcrcLong: 10.0);

        Assert.NotNull(result);
        Assert.NotNull(noAxial);
        Assert.NotEqual(noAxial!.Total, result!.Total);
        double height = TeeInput().Geometry.Height;
        foreach (var term in result.Terms)
            Assert.Equal(term.M + term.N * (height / 2.0 - term.Yc), term.MRed, 9);
    }

    [Fact]
    public void ISection_SignReversal_KeepsCurvatureModulus()
    {
        var plus = Sp63Curvature.Compute(SymmetricIInput(+1),
            new Sp63CurvatureLoad(80.0, 10.0), new Sp63CurvatureLoad(20.0, 4.0),
            cracked: false, mcrcFull: 30.0, mcrcLong: 10.0);
        var minus = Sp63Curvature.Compute(SymmetricIInput(-1),
            new Sp63CurvatureLoad(80.0, 10.0), new Sp63CurvatureLoad(20.0, 4.0),
            cracked: false, mcrcFull: 30.0, mcrcLong: 10.0);

        Assert.NotNull(plus);
        Assert.NotNull(minus);
        Assert.Equal(Math.Abs(plus!.Total), Math.Abs(minus!.Total), 9);
    }

    [Fact]
    public void MissingConcreteClass_ReturnsNull()
    {
        Assert.Null(Sp63Curvature.Compute(TeeInput(concreteClass: 0),
            new Sp63CurvatureLoad(80.0, 10.0), new Sp63CurvatureLoad(20.0, 4.0),
            cracked: true, mcrcFull: 30.0, mcrcLong: 10.0));
    }

    [Fact]
    public void CurvatureThroughTension_ReturnsNull()
    {
        Assert.Null(Sp63Curvature.Compute(TeeInput(),
            new Sp63CurvatureLoad(10.0, 2000.0), new Sp63CurvatureLoad(4.0, 800.0),
            cracked: true, mcrcFull: 30.0, mcrcLong: 10.0));
    }
}
