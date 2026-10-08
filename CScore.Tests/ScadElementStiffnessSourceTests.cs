using CScore.Fem;
using CScore.Import;
using Xunit;

namespace CScore.Tests;

public sealed class ScadElementStiffnessSourceTests
{
    // Проект SCAD в тс и м (сила 9810 Н), сечения в см.
    const double Fu = 9810, Lu = 1, Cm = 0.01;

    static ScadElementStiffnessSource Source(params (int Id, string Text)[] rows) =>
        new(rows.ToDictionary(r => r.Id, r => new LiraStiffnessRecord(r.Id, ScadStiffnessParams.ScadKindCode, "", r.Text, Cm)),
            Fu, Lu);

    static FemElement E(int stiffness, string type = "beam", double? h = null) =>
        new() { ElemTag = "1", ElemType = type, StiffnessNum = stiffness, ThicknessM = h };

    [Fact]
    public void BarS0_UsesProjectUnitsAndDefaultPoisson()
    {
        var src = Source((1, "S0 3e6 30 50 RO 2.5"));
        var bar = src.Bar(E(1))!;
        Assert.Equal(3e6 * Fu, bar.E, 6);
        Assert.Equal(bar.E / 2.4, bar.G, 6);
        Assert.Equal(0.3 * 0.5, bar.A, 12);
        Assert.Equal(0.3 * Math.Pow(0.5, 3) / 12, bar.Iy, 12); // h ‖ Z1 — изгиб вокруг Y1
        Assert.Equal(0.5 * Math.Pow(0.3, 3) / 12, bar.Iz, 12);
        Assert.Equal(2.5 * Fu, src.UnitWeight(E(1))!.Value, 9);
        Assert.Equal(0.15, src.BarArea(E(1))!.Value, 12);
    }

    [Fact]
    public void BarPoisson_FromNuToken()
    {
        var bar = Source((1, "S0 3e6 30 50 NU 0.25")).Bar(E(1))!;
        Assert.Equal(bar.E / 2.5, bar.G, 6);
    }

    [Fact]
    public void Shell_GeEnuH_ThicknessFromElementFirst()
    {
        var src = Source((2, "GE 3e6 0.2 0.18 RO 2.5"));
        var s = src.Shell(E(2, "shell"))!;
        Assert.Equal(3e6 * Fu, s.E, 6);
        Assert.Equal(0.2, s.Nu, 12);
        Assert.Equal(0.18, s.H, 12);
        Assert.Equal(0.2, src.Shell(E(2, "shell", h: 0.2))!.H, 12);
        Assert.Null(src.Bar(E(2)));
    }

    [Fact]
    public void Unknown_ReturnsNull()
    {
        var src = Source((3, "STZ RUSSIAN 1 5"));
        Assert.Null(src.Bar(E(3)));
        Assert.Null(src.UnitWeight(E(3)));
        Assert.Null(src.Shell(E(9, "shell")));
    }
}
