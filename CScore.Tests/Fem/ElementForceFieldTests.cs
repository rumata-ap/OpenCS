using System.Text.Json;
using CScore.Fem;
using Xunit;

namespace CScore.Tests.Fem;

/// <summary>Поля мозаики по КЭ: импортированные усилия набора и итог проверки по КЭ.</summary>
public class ElementForceFieldTests
{
    static ShellLoadItem Shell(int? elem, double mx = 0, double nx = 0, double? sigmaX = null) =>
        new() { SourceElementNum = elem, Mx = mx, Nx = nx, SigmaX = sigmaX };

    [Fact]
    public void Shell_SeveralRowsOfElement_AggregateChoosesValue()
    {
        var set = new ForceSet { Kind = "shell", ShellItems = [Shell(7, mx: 5), Shell(7, mx: -12), Shell(7, mx: 8), Shell(9, mx: 3)] };

        Assert.Equal(-12, ElementForceField.Shell(set, ShellForceComponent.Mx, ForceRowAggregate.MaxAbs, _ => null)[7]);
        Assert.Equal(8, ElementForceField.Shell(set, ShellForceComponent.Mx, ForceRowAggregate.Max, _ => null)[7]);
        Assert.Equal(-12, ElementForceField.Shell(set, ShellForceComponent.Mx, ForceRowAggregate.Min, _ => null)[7]);
        Assert.Equal(3, ElementForceField.Shell(set, ShellForceComponent.Mx, ForceRowAggregate.MaxAbs, _ => null)[9]);
    }

    [Fact]
    public void Shell_RowWithoutElementNumber_IsSkipped()
    {
        var set = new ForceSet { Kind = "shell", ShellItems = [Shell(null, mx: 5), Shell(3, mx: 1)] };

        var field = ElementForceField.Shell(set, ShellForceComponent.Mx, ForceRowAggregate.MaxAbs, _ => null);

        Assert.Equal([3], field.Keys);
        Assert.True(ElementForceField.HasShellRows(set));
        Assert.False(ElementForceField.HasBarRows(set));
    }

    /// <summary>Усилия ЛИРЫ приходят напряжениями: погонное усилие — σ·h по толщине своего КЭ.</summary>
    [Fact]
    public void Shell_StressRow_MembraneForceNeedsThickness()
    {
        var set = new ForceSet { Kind = "shell", ShellItems = [Shell(1, sigmaX: 1500), Shell(2, sigmaX: 1500), Shell(3, nx: 40)] };

        var nx = ElementForceField.Shell(set, ShellForceComponent.Nx, ForceRowAggregate.MaxAbs, num => num == 1 ? 0.2 : null);
        var sigma = ElementForceField.Shell(set, ShellForceComponent.SigmaX, ForceRowAggregate.MaxAbs, _ => null);

        Assert.Equal(300, nx[1], 9);
        Assert.False(nx.ContainsKey(2));     // толщина неизвестна
        Assert.Equal(40, nx[3], 9);          // строка уже с погонным усилием
        Assert.Equal(1500, sigma[1], 9);
        Assert.Equal(1500, sigma[2], 9);
        Assert.False(sigma.ContainsKey(3));  // напряжений в строке нет
        Assert.True(ElementForceField.HasStresses(set));
    }

    [Fact]
    public void Bar_ComponentsAndSections()
    {
        var set = new ForceSet
        {
            Items =
            [
                new LoadItem { SourceElementNum = 4, SourceSectionNum = 1, N = -100, Mx = 10, Vy = 5 },
                new LoadItem { SourceElementNum = 4, SourceSectionNum = 2, N = -100, Mx = -30, Vy = -7 },
                new LoadItem { N = 999 },
            ],
        };

        Assert.Equal(-100, ElementForceField.Bar(set, BarForceComponent.N, ForceRowAggregate.MaxAbs)[4]);
        Assert.Equal(-30, ElementForceField.Bar(set, BarForceComponent.Mx, ForceRowAggregate.MaxAbs)[4]);
        Assert.Equal(5, ElementForceField.Bar(set, BarForceComponent.Vy, ForceRowAggregate.Max)[4]);
        Assert.Single(ElementForceField.Bar(set, BarForceComponent.N, ForceRowAggregate.MaxAbs));
    }

    [Fact]
    public void CheckResults_ReadPerElementSummary()
    {
        string json = JsonSerializer.Serialize(new
        {
            perElement = true,
            elements = new object[]
            {
                new { elemNum = 1, elemTag = "1", rebarSource = "assigned", status = "ok", utilMax = 0.62 },
                new { elemNum = 2, elemTag = " 2 ", rebarSource = "assigned", status = "failed", utilMax = (double?)null },
                new { elemNum = 3, elemTag = "3", rebarSource = "selected", status = "no_forces", utilMax = (double?)null },
            },
        });

        var rows = FemCheckElementResults.Parse(json);

        Assert.Equal(3, rows.Count);
        Assert.Equal(new FemCheckElementResult("1", "assigned", "ok", 0.62), rows[0]);
        Assert.Equal("2", rows[1].ElemTag);
        Assert.True(rows[1].IsChecked);
        Assert.Null(rows[1].UtilMax);
        Assert.False(rows[2].IsChecked);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("{\"rows\":[]}")]
    [InlineData("не json")]
    public void CheckResults_NotPerElement_Empty(string? json) => Assert.Empty(FemCheckElementResults.Parse(json));
}
