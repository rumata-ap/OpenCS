using CScore;
using CScore.Sp63.CrackWidth;
using CScore.Sp63.Normal;
using Xunit;

namespace CScore.Tests.Sp63CrackWidth;

/// <summary>Новые формы нормального сечения не протекают в расчёт ширины раскрытия трещин.</summary>
public sealed class Sp63CrackWidthCircularGuardTests
{
    [Theory]
    [InlineData("circular")]
    [InlineData("annular")]
    public void CrackWidthParams_RejectRoundShapes(string shape)
    {
        var parameters = new Sp63CrackWidthTaskParams { ShapeKind = shape };

        Assert.False(parameters.TryToOptions(out _, out var errorCode));
        Assert.Equal("invalid_shape_kind", errorCode);
    }

    [Theory]
    [InlineData(Sp63NormalShapeKind.Circular)]
    [InlineData(Sp63NormalShapeKind.Annular)]
    public void CrackWidthChecker_ReturnsNotApplicableForRoundShapes(Sp63NormalShapeKind shape)
    {
        var valid = new Sp63CrackWidthTaskParams();
        Assert.True(valid.TryToOptions(out var options, out var errorCode), errorCode);

        var result = Sp63CrackWidthChecker.Check(
            CScore.Tests.Sp63Normal.Sp63NormalFixtures.CircleSection(0.25),
            new LoadItem { Mx = 10.0 }, CalcType.N, options with { ShapeKind = shape });

        Assert.Equal("unsupported_shape", Assert.Single(result.ApplicabilityMessages).Code);
    }
}
