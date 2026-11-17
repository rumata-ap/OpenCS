using CScore.Import;
using Xunit;

namespace CScore.Tests.Import;

/// <summary>Строки РСУ и РСН ЛИРЫ → сочетания загружений: подбор по сечениям, исключение загружений, слияние повторов.</summary>
public class LiraCombinationRowsTests
{
    static readonly LiraBarSection Low = new(1, 2), Up = new(2, 1);

    // Усилия N, Mk, My, Mz, Qz, Qy загружений 1–4 в сечениях нижней и верхней колонн; загружение 4 на колонны не влияет.
    static readonly Dictionary<LiraBarSection, IReadOnlyDictionary<int, IReadOnlyList<double>>> Cases = new()
    {
        [Low] = new Dictionary<int, IReadOnlyList<double>>
        {
            [1] = [-40.1, 0.01, 2.3, -0.8, 0.5, -0.2],
            [2] = [-12.4, 0.00, 0.7, 2.1, 0.1, 0.7],
            [3] = [-0.3, 0.03, 3.4, 0.2, 1.2, 0.1],
            [4] = [0, 0, 0, 0, 0, 0],
        },
        [Up] = new Dictionary<int, IReadOnlyList<double>>
        {
            [1] = [-19.8, 0.02, -1.1, 0.4, -0.3, 0.1],
            [2] = [-6.0, 0.01, 0.2, -1.5, 0.0, -0.5],
            [3] = [-0.1, 0.02, 1.9, 0.1, 0.8, 0.0],
            [4] = [0, 0, 0, 0, 0, 0],
        },
    };

    static double[] Combine(LiraBarSection at, params (int Lc, double K)[] terms) =>
        Enumerable.Range(0, LiraCombinationRows.Components).Select(c => terms.Sum(t => t.K * Cases[at][t.Lc][c])).ToArray();

    [Fact]
    public void Rsu_RowsFitted_AndDuplicatesMerged()
    {
        var rows = new[]
        {
            new LiraRsuRow(Low, 1, 18, "Б1", [1, 2, 3], Combine(Low, (1, 1.0), (2, 0.95), (3, 0.9))),
            new LiraRsuRow(Up, 3, 18, "Б1", [1, 2, 3], Combine(Up, (1, 1.0), (2, 0.95), (3, 0.9))),
            new LiraRsuRow(Up, 4, 10, "", [1, 2], Combine(Up, (1, 1.0), (2, -0.95))),
        };
        var fitted = LiraCombinationRows.Rsu(rows, Cases);
        Assert.All(fitted, f => Assert.True(f.Fit.Ok, f.Fit.Failure));
        Assert.Equal("РСУ э.1 с2 к18 Б1", fitted[0].Label);
        Assert.Equal("РСУ э.2 с1 к10", fitted[2].Label);

        var distinct = LiraCombinationRows.Distinct(fitted);
        Assert.Equal(2, distinct.Count);
        Assert.Equal(1, distinct[0].Duplicates);
        Assert.Equal([(1, 1.0), (2, -0.95)], distinct[1].Fit.Terms);
    }

    [Fact]
    public void Rsu_LoadCaseMissingInSection_RowRejected()
    {
        var row = new LiraRsuRow(Low, 1, 1, "", [1, 7], Combine(Low, (1, 1.0)));
        var f = Assert.Single(LiraCombinationRows.Rsu([row], Cases));
        Assert.False(f.Fit.Ok);
        Assert.Contains("загружения 7", f.Fit.Failure);
        // Непринятые строки не сливаются — каждая попадает в предупреждения.
        Assert.Equal(2, LiraCombinationRows.Distinct([f, f]).Count);
    }

    [Fact]
    public void Rsu_DependentLoadCases_MatchedFromRowWithSameSet()
    {
        // В сечении Dep загружение 3 пропорционально загружению 2 — по своим компонентам коэффициенты не определяются.
        var dep = new LiraBarSection(5, 1);
        var cases = new Dictionary<LiraBarSection, IReadOnlyDictionary<int, IReadOnlyList<double>>>(Cases)
        {
            [dep] = new Dictionary<int, IReadOnlyList<double>>
            {
                [1] = [-30.0, 0.01, 1.2, 0.3, 0.4, 0.1],
                [2] = [-8.0, 0.02, 0.6, -0.9, 0.2, -0.3],
                [3] = [-4.0, 0.01, 0.3, -0.45, 0.1, -0.15],
            },
        };
        double[] depForces = Enumerable.Range(0, 6)
            .Select(c => cases[dep][1][c] + 0.95 * cases[dep][2][c] + 0.9 * cases[dep][3][c]).ToArray();
        var rows = new[]
        {
            new LiraRsuRow(dep, 1, 18, "Б1", [1, 2, 3], depForces),
            new LiraRsuRow(Low, 1, 18, "Б1", [1, 2, 3], Combine(Low, (1, 1.0), (2, 0.95), (3, 0.9))),
            new LiraRsuRow(Low, 2, 18, "А1", [1, 2, 3], Combine(Low, (1, 1.0), (2, 0.9), (3, 0.9))),
        };
        var fitted = LiraCombinationRows.Rsu(rows, cases);
        Assert.True(fitted[0].Fit.Ok, fitted[0].Fit.Failure);
        Assert.True(fitted[0].Fit.Matched);
        Assert.Equal([(1, 1.0), (2, 0.95), (3, 0.9)], fitted[0].Fit.Terms);
        Assert.False(fitted[1].Fit.Matched);
    }

    [Fact]
    public void Rsn_AllSections_FactorsRecovered_NoInfluenceIgnored()
    {
        var forces = new Dictionary<LiraBarSection, IReadOnlyList<double>>
        {
            [Low] = Combine(Low, (1, 1.1), (3, 1.2)),
            [Up] = Combine(Up, (1, 1.1), (3, 1.2)),
        };
        var f = Assert.Single(LiraCombinationRows.Rsn([new LiraRsnRow(3, "Осн. 1", forces)], Cases));
        Assert.True(f.Fit.Ok, f.Fit.Failure);
        Assert.Equal("РСН 3 «Осн. 1»", f.Label);
        Assert.Equal([(1, 1.1), (3, 1.2)], f.Fit.Terms);
        Assert.Equal([4], f.Fit.Ignored);
    }
}
