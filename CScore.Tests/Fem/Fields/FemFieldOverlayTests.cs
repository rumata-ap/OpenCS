using CScore.Fem;
using CScore.Fem.Fields;
using Xunit;

namespace CScore.Tests.Fem.Fields;

public class FemFieldOverlayTests
{
    static FemField Field(int id, string quantity, int order, bool enabled = true) =>
        new() { Id = id, Name = $"F{id}", Quantity = quantity, SortOrder = order, Enabled = enabled };

    static FemFieldValue C1(int field, string tag, double v) =>
        new(field, FemFieldTargetKind.Shell, tag, "c1", v, FieldHow.Interpolated, 0);

    static FemFieldValue K(int field, string node, string comp, double v) =>
        new(field, FemFieldTargetKind.MeshNode, node, comp, v, FieldHow.Matched, 0);

    readonly List<FemElement> _elements =
    [
        new() { ElemTag = "1", ElemType = "shell", FoundationC1 = 1000 },
        new() { ElemTag = "2", ElemType = "shell" },
        new() { ElemTag = "3", ElemType = "beam" },
    ];

    [Fact]
    public void C1_Priority_ImportThenFieldsByOrderThenManual()
    {
        var f1 = Field(1, FemFieldQuantities.FoundationC1, order: 0);
        var f2 = Field(2, FemFieldQuantities.FoundationC1, order: 1);
        var r = FemFieldOverlay.Apply(_elements, [],
            [new(f2, [C1(2, "1", 3000)]), new(f1, [C1(1, "1", 2000), C1(1, "2", 2500), C1(1, "3", 9)])],
            [new FemParamOverride(FemFieldTargetKind.Shell, "2", FemFieldQuantities.FoundationC1, "c1", 777)]);
        var byTag = r.Elements.ToDictionary(e => e.ElemTag);
        Assert.Equal(3000, byTag["1"].FoundationC1);   // поле 2 позже поля 1
        Assert.Equal(777, byTag["2"].FoundationC1);    // ручная правка поверх
        Assert.Null(byTag["3"].FoundationC1);          // стержню C1 не назначается
        Assert.Equal(1000, _elements[0].FoundationC1); // вход не изменён
        Assert.Equal("field:2", r.Sources[new(FemFieldTargetKind.Shell, "1", "c1")]);
        Assert.Equal("manual", r.Sources[new(FemFieldTargetKind.Shell, "2", "c1")]);
    }

    [Fact]
    public void DisabledField_DoesNothing_NegativeReported()
    {
        var off = Field(1, FemFieldQuantities.FoundationC1, 0, enabled: false);
        var neg = Field(2, FemFieldQuantities.FoundationC1, 1);
        var r = FemFieldOverlay.Apply(_elements, [], [new(off, [C1(1, "1", 5)]), new(neg, [C1(2, "2", -5)])], []);
        Assert.Equal(1000, r.Elements[0].FoundationC1);
        Assert.Null(r.Elements[1].FoundationC1);
        Assert.Contains(r.Diagnostics, d => d.Code == "field_negative" && d.Tags.SequenceEqual(["2"]));
    }

    [Fact]
    public void Spring_PerDof_KeepsImportedOtherDofs_CreatesMissing_SkipsManual()
    {
        var imported = new FemSpring { NodeTag = "10", Kx = 1, Ky = 2, Kz = 3, Origin = FemLoadOrigin.Import("lira"), SourceElemTag = "56" };
        var manual = new FemSpring { NodeTag = "12", Kz = 9, Origin = FemLoadOrigin.Manual };
        var f = Field(4, FemFieldQuantities.Spring, 0);
        var r = FemFieldOverlay.Apply([], [imported, manual],
            [new(f, [K(4, "10", "kz", 30), K(4, "11", "kz", 40), K(4, "12", "kz", 50)])], []);
        var byNode = r.Springs.ToDictionary(s => s.NodeTag);
        Assert.Equal([1, 2, 30, 0, 0, 0], byNode["10"].Stiffnesses);
        Assert.Equal("56", byNode["10"].SourceElemTag);
        Assert.Equal(3, imported.Kz);                               // вход не изменён
        Assert.Equal([0, 0, 40, 0, 0, 0], byNode["11"].Stiffnesses);
        Assert.Equal(FemFieldOverlay.FieldOrigin, byNode["11"].Origin);
        Assert.Equal(9, byNode["12"].Kz);                           // ручная пружина не тронута
        Assert.Contains(r.Diagnostics, d => d.Code == "field_manual_spring" && d.Tags.SequenceEqual(["12"]));
        Assert.Equal("field:4", r.Sources[new(FemFieldTargetKind.MeshNode, "10", "kz")]);
    }

    [Fact]
    public void NoLayers_ReturnsSameInstances()
    {
        var springs = new List<FemSpring> { new() { NodeTag = "1", Kz = 1 } };
        var r = FemFieldOverlay.Apply(_elements, springs, [], []);
        Assert.Same(_elements[0], r.Elements[0]);
        Assert.Same(springs[0], r.Springs[0]);
        Assert.Empty(r.Sources);
    }
}
