using CScore;
using CScore.Fem;
using CScore.Import;
using OpenCS.Services;
using Xunit;

namespace OpenCS.Tests;

/// <summary>Недостающие материалы стержней схемы SCAD — по классам ЖБ-групп.</summary>
public sealed class BarMaterialCreatorScadTests
{
    static FemCheckScopeElement Elem(int num, string type = "beam") =>
        new(num, new FemElement { ElemTag = num.ToString(), ElemType = type }, null);

    [Fact]
    public void Scad_ClassesFromConcreteGroups()
    {
        var groups = new ScadConcreteGroupIndex(
        [
            new ScadConcreteGroup(2, "колонны", 1, [0.04, 0.04, 0, 0], "B15", "A500", "A240", false, [0.4, 0.3], [814, 818]),
            new ScadConcreteGroup(1, "плиты", 1, [0.03, 0.03, 0, 0], "B20", "A500", "A240", false, [0.4, 0.3], [23]),
        ]);
        var data = new FemCheckSchemaData { SourceType = "scad", ScadConcreteGroups = groups };
        var materials = new[] { new Material { Id = 1, Tag = "A500", Type = MatType.ReSteelF } };

        var missing = LiraBarMaterialCreator.MissingClasses(materials, data, [Elem(814), Elem(818), Elem(23, "shell"), Elem(5)]);

        Assert.Equal([("B15", true)], missing);
    }

    [Fact]
    public void Scad_WithoutGroups_Nothing() =>
        Assert.Empty(LiraBarMaterialCreator.MissingClasses([], new FemCheckSchemaData { SourceType = "scad" }, [Elem(814)]));
}
