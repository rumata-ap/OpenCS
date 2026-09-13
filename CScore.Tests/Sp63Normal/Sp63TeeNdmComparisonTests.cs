using CScore;
using CScore.Sp63.Normal;
using Xunit;

namespace CScore.Tests.Sp63Normal;

/// <summary>Сверяет формульный расчёт тавра/двутавра с существующим НДМ (чистый изгиб).</summary>
public sealed class Sp63TeeNdmComparisonTests
{
    const double SpanLength = 6.3;

    static Sp63NormalResult Formula(CrossSection section, LoadItem load,
        Sp63NormalAxis axis = Sp63NormalAxis.Mx) =>
        Sp63NormalChecker.Check(section, load, CalcType.C,
            Sp63NormalFixtures.MemberOptions() with
            {
                ShapeKind = Sp63NormalShapeKind.Tee,
                Axis = axis
            }, SpanLength);

    [Theory]
    [InlineData(true, -20.0)]
    [InlineData(false, 20.0)]
    public void FormulaAndNdm_AgreeOnTeeWithCompressionFlange(bool flangeOnTop,
        double moment)
    {
        var section = flangeOnTop
            ? SectionCutFixtures.BuildReinforcedTee(0.4, 0.8, 2.0, 0.2, 0.0, 0.0)
            : SectionCutFixtures.BuildReinforcedTee(0.4, 0.8, 0.0, 0.0, 2.0, 0.2);
        var load = new LoadItem { N = 0.0, Mx = moment, My = 0.0 };

        var formula = Formula(section, load);
        var ndm = LimitForceSolver.ForCrossSection(section, CalcType.C, ten: false)
            .MomentFactor(n: 0.0, mx: moment, my: 0.0);

        Assert.Equal(Sp63NormalStatus.Calculated, formula.Status);
        Assert.True(ndm.Converged, $"НДМ не сошёлся: factor={ndm.Factor}");
        Assert.InRange(formula.StrengthDetails.Single().Allowable,
            Math.Abs(ndm.MxLimit) * 0.90, Math.Abs(ndm.MxLimit) * 1.10);
    }

    [Theory]
    [InlineData(20.0)]
    [InlineData(-20.0)]
    public void FormulaAndNdm_AgreeOnIBeam(double moment)
    {
        var section = SectionCutFixtures.BuildReinforcedTee(0.4, 0.8, 2.0, 0.2,
            2.0, 0.2);
        var load = new LoadItem { N = 0.0, Mx = moment, My = 0.0 };

        var formula = Formula(section, load);
        var ndm = LimitForceSolver.ForCrossSection(section, CalcType.C, ten: false)
            .MomentFactor(n: 0.0, mx: moment, my: 0.0);

        Assert.Equal(Sp63NormalStatus.Calculated, formula.Status);
        Assert.True(ndm.Converged);
        Assert.InRange(formula.StrengthDetails.Single().Allowable,
            Math.Abs(ndm.MxLimit) * 0.90, Math.Abs(ndm.MxLimit) * 1.10);
    }

    [Fact]
    public void FormulaAndNdm_AgreeOnTeeRotatedForMy()
    {
        var section = SectionCutFixtures.BuildReinforcedTee(0.4, 0.8, 0.0, 0.0,
            2.0, 0.2, rotateForMy: true);
        var load = new LoadItem { N = 0.0, Mx = 0.0, My = 20.0 };

        var formula = Formula(section, load, Sp63NormalAxis.My);
        var ndm = LimitForceSolver.ForCrossSection(section, CalcType.C, ten: false)
            .MomentFactor(n: 0.0, mx: 0.0, my: 20.0);

        Assert.Equal(Sp63NormalStatus.Calculated, formula.Status);
        Assert.True(ndm.Converged);
        Assert.InRange(formula.StrengthDetails.Single().Allowable,
            Math.Abs(ndm.MyLimit) * 0.90, Math.Abs(ndm.MyLimit) * 1.10);
    }
}
