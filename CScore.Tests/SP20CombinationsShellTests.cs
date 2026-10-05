using CScore.Combinations;
using Xunit;

namespace CScore.Tests;

public sealed class SP20CombinationsShellTests
{
    static ForceSet Shell(string tag, params ShellLoadItem[] rows) =>
        new() { Tag = tag, Kind = "shell", ShellItems = [.. rows] };

    [Fact]
    public void ForceSetsToLoadings_Shell_TakesComponentsFromShellRows()
    {
        var g = Shell("G: Вес",
            new ShellLoadItem { Label = "Э1", Nx = 10, Ny = 20, Nxy = 3, Mx = 4, My = 5, Mxy = 6, Qx = 7, Qy = 8 },
            new ShellLoadItem { Label = "Э2", Nx = -1 });
        var q = Shell("Q: Полезная",
            new ShellLoadItem { Label = "Э1", Mx = 2 },
            new ShellLoadItem { Label = "Э2", Qy = 9 });

        var (loadings, warnings) = SP20Combinations.ForceSetsToLoadings([g, q]);

        Assert.Empty(warnings);
        Assert.Equal(["Nx", "Ny", "Nxy", "Mx", "My", "Mxy", "Qx", "Qy"], loadings[0].ComponentNames);
        Assert.Equal(2, loadings[0].NSections);
        Assert.Equal(new double[] { 10, 20, 3, 4, 5, 6, 7, 8 },
            Enumerable.Range(0, 8).Select(j => loadings[0].Forces[0, j]));
        Assert.Equal(-1, loadings[0].Forces[1, 0]);
        Assert.Equal(9, loadings[1].Forces[1, 7]);
    }

    [Fact]
    public void ForceSetsToLoadings_Shell_RejectsStressesNotConvertedToForces()
    {
        var g = Shell("G: ЛИРА", new ShellLoadItem { Label = "Э1", SigmaX = 500, Mx = 1 });

        var ex = Assert.Throws<InvalidOperationException>(() => SP20Combinations.ForceSetsToLoadings([g]));
        Assert.Contains("Напряжения → усилия", ex.Message);
    }

    [Fact]
    public void ForceSetsToLoadings_Shell_ConvertedStressesGiveWarning()
    {
        var g = Shell("G: ЛИРА", new ShellLoadItem { Label = "Э1", SigmaX = 500, Nx = 100 });

        var (loadings, warnings) = SP20Combinations.ForceSetsToLoadings([g]);

        Assert.Equal(100, loadings[0].Forces[0, 0]);
        Assert.Single(warnings);
    }

    [Fact]
    public void EnvelopeAndCases_Shell_ProduceShellRows()
    {
        var g = Shell("G: Вес", new ShellLoadItem { Label = "Э1", Nx = 10, Mx = 4 });
        var q = Shell("Q: Полезная", new ShellLoadItem { Label = "Э1", Nx = 5, Mx = 2 });

        var (env, cases, _, _) = SP20Combinations.SP20EnvelopeAndCasesFromForceSets([g, q], CombType.Fundamental);
        var envSet = SP20Combinations.EnvelopeToForceSet(env, "shell", "env", "Cm");
        var caseSet = SP20Combinations.CasesToForceSet(cases, "shell", "cases");

        Assert.Empty(envSet.Items);
        Assert.Equal(16, envSet.ShellItems.Count);   // max и min по 8 компонентам
        Assert.True(envSet.ShellItems.Max(i => i.Nx) > 15);
        Assert.Equal(Enumerable.Range(1, 16), envSet.ShellItems.Select(i => i.Num));
        Assert.Empty(caseSet.Items);
        Assert.NotEmpty(caseSet.ShellItems);
    }

    [Fact]
    public void EnvelopeAndCases_LabelRowsWithSourceLabels()
    {
        static ForceSet Bar(string tag, double mSupport, double mSpan) => new()
        {
            Tag = tag, Kind = "bar",
            Items = [new LoadItem { Label = "Опора", Mx = mSupport }, new LoadItem { Label = "Пролёт", Mx = mSpan }]
        };
        var g = Bar("G: Вес", -45, 38);
        var q = Bar("Q: Полезная", -20, 15);
        string[] labels = ["Опора", "Пролёт"];

        var (env, cases, _, _) = SP20Combinations.SP20EnvelopeAndCasesFromForceSets([g, q], CombType.Fundamental);
        var envSet = SP20Combinations.EnvelopeToForceSet(env, "bar", "env", "Cm", labels);
        var caseSet = SP20Combinations.CasesToForceSet(cases, "bar", "cases", labels);

        Assert.Contains(envSet.Items, i => i.Label == "Cm max Mx (Пролёт)");
        Assert.Contains(envSet.Items, i => i.Label == "Cm min Mx (Опора)");
        Assert.DoesNotContain(envSet.Items, i => i.Label.Contains("sec="));
        Assert.All(caseSet.Items, i => Assert.Matches(@"^Cm (Опора|Пролёт) (max|min) ", i.Label));

        // Без меток — прежний номер сечения.
        var bare = SP20Combinations.EnvelopeToForceSet(env, "bar", "env", "Cm");
        Assert.Contains(bare.Items, i => i.Label == "Cm max Mx (sec=1)");
    }
}
