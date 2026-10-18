using System.Text.Json;
using CScore.Fem;
using Xunit;

namespace CScore.Tests.Fem;

/// <summary>Расчётные элементы для автоматических расчётных длин: КонЭ и цепочки КЭ между раскреплениями.</summary>
public class FemBucklingUnitsTests
{
    static FemMeshNode Node(int tag, double x, double y, double z) =>
        new() { NodeTag = tag.ToString(), X = x, Y = y, Z = z };

    static FemElement Bar(int tag, int i, int j, string? member = null) =>
        new() { ElemTag = tag.ToString(), ElemType = "beam", NodeIdsJson = $"[{i},{j}]", SourceMemberTag = member };

    static FemElement Shell(int tag, params int[] nodes) =>
        new() { ElemTag = tag.ToString(), ElemType = "shell", NodeIdsJson = JsonSerializer.Serialize(nodes) };

    /// <summary>КЭ одного КонЭ — один элемент, даже если к его промежуточному узлу примыкает перекрытие.</summary>
    [Fact]
    public void Member_IsOneUnit()
    {
        FemMeshNode[] nodes = [Node(1, 0, 0, 0), Node(2, 0, 0, 3), Node(3, 0, 0, 6), Node(10, 3, 0, 3), Node(11, 3, 3, 3), Node(12, 0, 3, 3)];
        FemElement[] mesh = [Bar(1, 1, 2, "K1"), Bar(2, 2, 3, "K1"), Shell(100, 2, 10, 11, 12)];
        var units = FemBucklingUnits.Build(mesh, nodes);

        var u = Assert.Single(units);
        Assert.Equal("K1", u.MemberTag);
        Assert.Equal(["1", "2"], u.ElemTags);
        Assert.Equal("КонЭ K1", u.Label);
    }

    /// <summary>
    /// Схема без КонЭ: колонна из четырёх КЭ, на отметке 3 м перекрытие — две цепочки по этажу; ригель — своя цепочка;
    /// каждый КЭ — ровно в одном элементе.
    /// </summary>
    [Fact]
    public void MeshOnly_ChainsBetweenBracings()
    {
        FemMeshNode[] nodes =
        [
            Node(1, 0, 0, 0), Node(2, 0, 0, 1.5), Node(3, 0, 0, 3), Node(4, 0, 0, 4.5), Node(5, 0, 0, 6),
            Node(10, 3, 0, 3), Node(11, 3, 3, 3), Node(12, 0, 3, 3), Node(20, 3, 0, 6),
        ];
        FemElement[] mesh =
        [
            Bar(1, 1, 2), Bar(2, 2, 3), Bar(3, 3, 4), Bar(4, 4, 5),
            Shell(100, 3, 10, 11, 12), Bar(200, 5, 20),
        ];
        var units = FemBucklingUnits.Build(mesh, nodes);

        Assert.Equal(3, units.Count);
        Assert.All(units, u => Assert.Null(u.MemberTag));
        Assert.Equal(["1", "2"], units[0].ElemTags.Order());
        Assert.Equal(["3", "4"], units[1].ElemTags.Order());
        Assert.Equal(["200"], units[2].ElemTags);
        Assert.Equal("КЭ 200", units[2].Label);
    }

    /// <summary>Смешанная схема: КЭ КонЭ в цепочки без КонЭ не попадают.</summary>
    [Fact]
    public void Mixed_MemberElementsNotInChains()
    {
        FemMeshNode[] nodes = [Node(1, 0, 0, 0), Node(2, 0, 0, 3), Node(3, 0, 0, 6)];
        FemElement[] mesh = [Bar(1, 1, 2, "K1"), Bar(2, 2, 3)];
        var units = FemBucklingUnits.Build(mesh, nodes);

        Assert.Equal(2, units.Count);
        Assert.Equal(["1"], units[0].ElemTags);
        Assert.Equal(["2"], units[1].ElemTags);
    }
}
