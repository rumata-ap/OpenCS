using CScore.Import;
using Xunit;

namespace CScore.Tests.Import;

/// <summary>
/// Раскладка сочетания ЛИРЫ на загружения МНК по компонентам усилий стержня: строка РСУ отдаёт только номера
/// загружений, коэффициенты (ψ, знак знакопеременного) восстанавливаются по шести компонентам сечения.
/// </summary>
public class LiraCombinationFitTests
{
    // Компоненты N, Mk, My, Mz, Qz, Qy сечения колонны в трёх загружениях (т, т·м).
    static readonly double[][] Cases =
    [
        [-20.995, 0.012, 1.84, -0.73, 0.41, -0.18],
        [-11.870, 0.004, 0.62, 1.95, 0.12, 0.66],
        [-0.214, 0.031, 3.10, 0.22, 1.05, 0.07],
    ];

    static List<LiraFitEquation> Equations(double[] k, Func<double, double>? round = null)
    {
        var list = new List<LiraFitEquation>();
        for (int c = 0; c < 6; c++)
        {
            double t = 0;
            for (int j = 0; j < k.Length; j++) t += k[j] * Cases[j][c];
            list.Add(new LiraFitEquation(round?.Invoke(t) ?? t, Cases.Take(k.Length).Select(x => x[c]).ToList()));
        }
        return list;
    }

    [Fact]
    public void ExactCombination_FactorsRecovered()
    {
        var fit = LiraCombinationFit.Fit([1, 2, 7], Equations([1.0, 0.95, -0.9]));
        Assert.True(fit.Ok, fit.Failure);
        Assert.Equal([(1, 1.0), (2, 0.95), (7, -0.9)], fit.Terms);
        Assert.True(fit.Residual < 1e-9);
    }

    [Fact]
    public void RowRoundedToFloat_StillAccepted()
    {
        var fit = LiraCombinationFit.Fit([1, 2, 7], Equations([1.0, 0.9, 1.0], t => (float)Math.Round(t, 3)));
        Assert.True(fit.Ok, fit.Failure);
        Assert.Equal(0.9, fit.Terms[1].Factor, 2);
        Assert.Equal(1.0, fit.Terms[2].Factor, 2);
    }

    [Fact]
    public void MissingLoadCase_ResidualAboveTolerance()
    {
        // В строке три загружения, а подбирается по двум — сочетание не раскладывается.
        var eq = Equations([1.0, 0.95, 1.0]);
        var two = eq.Select(e => new LiraFitEquation(e.Target, e.Cases.Take(2).ToList())).ToList();
        var fit = LiraCombinationFit.Fit([1, 2], two);
        Assert.False(fit.Ok);
        Assert.Contains("невязка", fit.Failure);
    }

    [Fact]
    public void LoadCaseWithoutEffect_FactorUndetermined()
    {
        var eq = Equations([1.0, 0.95]).Select(e => new LiraFitEquation(e.Target, [.. e.Cases, 0.0])).ToList();
        var fit = LiraCombinationFit.Fit([1, 2, 9], eq);
        Assert.False(fit.Ok);
        Assert.Contains("загружение 9", fit.Failure);
    }

    [Fact]
    public void NoiseComponents_Ignored()
    {
        // Компонента на уровне шума с «неправильным» значением не портит подбор.
        var eq = Equations([1.0, 0.95, -0.9]);
        eq.Add(new LiraFitEquation(1e-7, [3e-8, -2e-8, 1e-8]));
        var fit = LiraCombinationFit.Fit([1, 2, 7], eq);
        Assert.True(fit.Ok, fit.Failure);
        Assert.Equal(-0.9, fit.Terms[2].Factor, 4);
    }

    [Fact]
    public void FewerComponentsThanLoadCases_Rejected()
    {
        var eq = Equations([1.0, 0.95, -0.9]).Take(2).ToList();
        var fit = LiraCombinationFit.Fit([1, 2, 7], eq);
        Assert.False(fit.Ok);
        Assert.Empty(fit.Terms);
    }
}
