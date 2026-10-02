using CScore.Fem;
using CScore.Import;
using Xunit;

namespace CScore.Tests;

public sealed class ScadSchemaConverterTests
{
    static ScadSchemaData BuildData() => new()
    {
        Nodes = { new ScadNodeRecord(1, 0, 0, 0), new ScadNodeRecord(2, 1, 0, 0), new ScadNodeRecord(3, 1, 1, 0), new ScadNodeRecord(4, 0, 1, 0) },
        Elements =
        {
            new ScadElementRecord(1, 2, 5, [1, 2]),
            new ScadElementRecord(2, 44, 6, [1, 2, 3, 4]),
        },
        Stiffnesses =
        {
            new ScadStiffnessRecord(5, "Балка 20x30", ScadStiffnessKind.Bar),
            new ScadStiffnessRecord(6, "Плита 200", ScadStiffnessKind.Shell, ThicknessM: 0.2),
        },
    };

    [Fact]
    public void ToFemMeshNodesMapsAllNodesWithSchemaId()
    {
        var nodes = ScadSchemaConverter.ToFemMeshNodes(BuildData(), schemaId: 3);

        Assert.Equal(4, nodes.Length);
        Assert.All(nodes, n => Assert.Equal(3, n.SchemaId));
        Assert.Equal("1", nodes[0].NodeTag);
    }

    [Fact]
    public void ToFemMeshElementsMapsBeamAndShellByNodeCount()
    {
        var elements = ScadSchemaConverter.ToFemMeshElements(BuildData(), schemaId: 3);

        Assert.Equal(2, elements.Length);
        var bar = Assert.Single(elements, e => e.ElemType == "beam");
        Assert.Equal("1", bar.ElemTag);
        Assert.Equal("Балка 20x30", bar.SectionTag);
        var shell = Assert.Single(elements, e => e.ElemType == "shell");
        Assert.Equal("2", shell.ElemTag);
        Assert.Equal("Плита 200", shell.SectionTag);
        Assert.Equal(0.2, shell.ThicknessM);
        Assert.Equal(4, shell.Node4);
    }

    [Fact]
    public void ToFemMeshElements_ImportedOriginStiffnessNumAndAngleOnlyForShells()
    {
        var data = BuildData();
        data.PlateAxisAngles[1] = 30; // стержню угол не переносится
        data.PlateAxisAngles[2] = -45;

        var elements = ScadSchemaConverter.ToFemMeshElements(data, schemaId: 3);
        var nodes = ScadSchemaConverter.ToFemMeshNodes(data, schemaId: 3);

        Assert.All(elements, e => Assert.Equal(FemMember.MeshSourceImported, e.Origin));
        Assert.All(nodes, n => Assert.Equal(FemMember.MeshSourceImported, n.Origin));
        var bar = elements.Single(e => e.ElemTag == "1");
        Assert.Equal(5, bar.StiffnessNum);
        Assert.Null(bar.LocalAxisAngleDeg);
        var shell = elements.Single(e => e.ElemTag == "2");
        Assert.Equal(6, shell.StiffnessNum);
        Assert.Equal(-45, shell.LocalAxisAngleDeg);
    }

    [Fact]
    public void ToFemMeshElements_ZeroStiffness_NoStiffnessNum()
    {
        var data = BuildData();
        data.Elements.Add(new ScadElementRecord(3, 10, 0, [3, 4]));
        var e = ScadSchemaConverter.ToFemMeshElements(data, 3).Single(x => x.ElemTag == "3");
        Assert.Null(e.StiffnessNum);
        Assert.Equal("beam", e.ElemType);
    }

    [Fact]
    public void GroupsByBlocksAndConcreteGroups()
    {
        var data = BuildData();
        data.Blocks.Add(new ScadGroupRecord("Этаж 1", [1, 2, 99]));
        data.Blocks.Add(new ScadGroupRecord("", [2]));
        data.Blocks.Add(new ScadGroupRecord("Пустой", [99]));
        data.ConcreteGroups.Add(new ScadConcreteGroup(1, "колонны", 5, [0.04, 0.04, 0.04, 0.04],
            "B25", "A500", "A240", true, [0.4, 0.3], [1]));
        data.ConcreteGroups.Add(new ScadConcreteGroup(2, "", 1, [0.03, 0.03, 0.03, 0.03],
            "B30", "A500", "A240", false, [0.4, 0.3], [2]));

        var blocks = ScadSchemaConverter.ToFemMemberGroupsByBlocks(data, 3);
        Assert.Equal(["Блок: Этаж 1", "Блок 2"], blocks.Select(g => g.Tag));
        Assert.Equal("[1,2]", blocks[0].MemberTagsJson);
        Assert.Null(blocks[0].MemberType);   // стержень + оболочка
        Assert.Equal("shell", blocks[1].MemberType);

        var concrete = ScadSchemaConverter.ToFemMemberGroupsByConcreteGroups(data, 3);
        Assert.Equal(["ЖБ: колонны", "ЖБ: 2"], concrete.Select(g => g.Tag));
        Assert.Equal("beam", concrete[0].MemberType);
        Assert.All(concrete, g => Assert.Equal(3, g.SchemaId));
    }

    [Fact]
    public void ToSchemaStiffnesses_ScadKindCodeSourceTextAndSectionUnit()
    {
        var data = new ScadSchemaData { SectionUnitM = 0.01 };
        data.Stiffnesses.Add(ScadStiffnessParams.Parse(6, "S0 900000 60 60 NU 0.2", "Колонны", 1, 0.01));
        data.Stiffnesses.Add(ScadStiffnessParams.Parse(18, "", null, 1, 0.01));
        data.Stiffnesses.Add(new ScadStiffnessRecord(7, "Без строки", ScadStiffnessKind.Shell, 0.2));

        var rows = ScadSchemaConverter.ToSchemaStiffnesses(data);

        Assert.Equal([6, 18], rows.Select(r => r.Id));
        var col = rows[0];
        Assert.Equal(ScadStiffnessParams.ScadKindCode, col.KindCode);
        Assert.Equal("Колонны", col.Name);
        Assert.Equal("S0 900000 60 60 NU 0.2", col.Params);
        Assert.Equal(0.01, col.SectionUnitM);
        Assert.Equal(new LiraBarRect(0.6, 0.6), ScadStiffnessParams.BarRect(col));
        Assert.Equal("", rows[1].Name);
    }
}
