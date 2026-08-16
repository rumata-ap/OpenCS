using CScore;
using Xunit;

namespace CScore.Tests;

/// <summary>Проверки коэффициента работы растянутой арматуры по п. 8.2.32 СП 63.</summary>
public sealed class Curvature8232Tests
{
    [Fact]
    public void PsiS_UsesStrainRatioFromFormula8232()
    {
        double epsCrc = 0.00010;
        double eps = 0.00050;
        double expected = 1.0 / (1.0 + 0.8 * epsCrc / eps);

        Assert.Equal(expected, Curvature8232.PsiS(epsCrc, eps), 12);
    }

    [Fact]
    public void PsiS_DoesNotReduceCompressionOrUnstrainedBar()
    {
        Assert.Equal(1.0, Curvature8232.PsiS(0.00010, 0.0));
        Assert.Equal(1.0, Curvature8232.PsiS(0.00010, -0.00050));
        Assert.Equal(1.0, Curvature8232.PsiS(-0.00010, 0.00050));
    }

    [Fact]
    public void CorrectedStress_DividesDiagramStressByPsiS()
    {
        Assert.Equal(613.079, Curvature8232.CorrectedStress(450.0, 0.734), 3);
    }

    [Fact]
    public void PsiSFromStrainRatio_UsesFormulaFromClause8218()
    {
        double expected = 1.0 - 0.8 * 0.00010 / 0.00050;

        Assert.Equal(expected,
            Curvature8232.PsiSFromStrainRatio(0.00010, 0.00050), 12);
    }

    [Fact]
    public void PsiSFromStrainRatio_DoesNotApplyBeforeCrackStrain()
    {
        Assert.Equal(1.0,
            Curvature8232.PsiSFromStrainRatio(0.00010, 0.00005));
    }

    [Fact]
    public void CrackWidth_UsesAverageStrainWithoutPsi()
    {
        double expected = 1.4 * 0.5 * 1.0 * 0.002 * 0.4;

        Assert.Equal(expected,
            CrackWidth8232.FromAverageStrain(0.002, 0.4, phi1: 1.4, phi2: 0.5, phi3: 1.0),
            12);
    }

    [Fact]
    public void AxialEquilibrium_FindsNeutralStrainAtTargetN()
    {
        var result = CurvatureEquilibrium8232.Solve(
            e0 => new Load { N = 2_000_000.0 * (e0 - 0.00125), Mx = 17.0 },
            targetN: 0.0);

        Assert.True(result.Converged);
        Assert.Equal(0.00125, result.E0, 10);
        Assert.Equal(17.0, result.Load.Mx, 12);
        Assert.Equal(0.0, result.Residual, 8);
    }
}
