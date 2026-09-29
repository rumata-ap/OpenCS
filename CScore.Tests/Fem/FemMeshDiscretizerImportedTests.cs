using System.Text.Json;
using CScore.Fem;
using Xunit;

namespace CScore.Tests.Fem;

public class FemMeshDiscretizerImportedTests
{
    static FemMeshNode Imported(string tag, double x, double y, double z, string? member = null) =>
        new() { Id = 100 + int.Parse(tag), NodeTag = tag, X = x, Y = y, Z = z, SourceMemberTag = member, Origin = FemMember.MeshSourceImported };

    static FemElement ImportedElement(string tag, string type, string? member, params int[] nodes) =>
        new() { Id = 500 + int.Parse(tag), ElemTag = tag, ElemType = type, NodeIdsJson = JsonSerializer.Serialize(nodes), SourceMemberTag = member, Origin = FemMember.MeshSourceImported };

    // Импортированная схема: колонна 1–2–3 (КЭ 1, 2, заблокирована как «КОЛОННА №1»), пластина 3-4-5-6 (КЭ 3, вне элементов).
    readonly List<FemMeshNode> _meshNodes =
    [
        Imported("1", 0, 0, 0), Imported("2", 0, 0, 1.5), Imported("3", 0, 0, 3),
        Imported("4", 2, 0, 3), Imported("5", 2, 2, 3), Imported("6", 0, 2, 3),
    ];
    readonly List<FemElement> _meshElements =
    [
        ImportedElement("1", "beam", "КОЛОННА №1", 1, 2),
        ImportedElement("2", "beam", "КОЛОННА №1", 2, 3),
        ImportedElement("3", "shell", null, 3, 4, 5, 6),
    ];
    readonly List<FemNode> _nodes =
    [
        new() { NodeTag = "1", X = 0, Y = 0, Z = 0 },
        new() { NodeTag = "3", X = 0, Y = 0, Z = 3 },
    ];
    readonly List<FemMember> _members =
    [
        new() { ElemTag = "КОЛОННА №1", ElemType = "beam", NodeIdsJson = "[1,3]", MeshSource = FemMember.MeshSourceImported },
    ];

    [Fact]
    public void LockedMemberAndOrphanElements_AreKeptAsIs()
    {
        var (nodes, elements) = FemMeshDiscretizer.DiscretizeKeepingImported(1, _nodes, _members, 0.5, _meshNodes, _meshElements);

        Assert.Equal(_meshNodes, nodes);
        Assert.Equal(_meshElements, elements);
        Assert.Equal(["1", "2", "3"], elements.Select(e => e.ElemTag));
        Assert.Equal(503, elements[2].Id);
    }

    [Fact]
    public void NewBeam_IsDiscretized_AndJoinsImportedNode()
    {
        // Новый стержень из верха колонны (узел 3 ЛИРЫ) вверх. Тег его верхнего узла «4» занят узлом
        // пластины ЛИРЫ в другой точке — в сетке узел получит новый номер 7 (выше импортированных).
        _nodes.Add(new FemNode { NodeTag = "4", X = 0, Y = 0, Z = 5 });
        _members.Add(new FemMember { ElemTag = "Б-1", ElemType = "beam", NodeIdsJson = "[3,4]" });

        var (nodes, elements) = FemMeshDiscretizer.DiscretizeKeepingImported(1, _nodes, _members, 1.0, _meshNodes, _meshElements);

        var generated = elements.Where(e => e.Origin == FemMember.MeshSourceGenerated).ToList();
        Assert.Equal(2, generated.Count);
        Assert.All(generated, e => Assert.Equal("Б-1", e.SourceMemberTag));
        Assert.Equal(["4", "5"], generated.Select(e => e.ElemTag).Order());

        var nodeByTag = nodes.ToDictionary(n => n.NodeTag);
        var chain = generated.SelectMany(e => JsonSerializer.Deserialize<int[]>(e.NodeIdsJson)!).ToList();
        Assert.Contains(3, chain); // начало — импортированный узел 3
        var top = nodes.Single(n => n.Origin == FemMember.MeshSourceGenerated && n.Z == 5);
        Assert.Equal("7", top.NodeTag); // тег 4 занят — новый номер выше импортированных
        Assert.Equal(2, nodeByTag["4"].X); // узел 4 ЛИРЫ не тронут
        Assert.Equal(_meshNodes.Count + 2, nodes.Count); // + верхний узел и промежуточный
        Assert.Empty(FemTopologyValidator.ValidateMesh(nodes, elements));
    }

    [Fact]
    public void WithoutImportedMesh_BehavesLikeDiscretize()
    {
        var (nodes, elements) = FemMeshDiscretizer.DiscretizeKeepingImported(1,
            [new() { NodeTag = "1" }, new() { NodeTag = "2", X = 2 }],
            [new() { ElemTag = "Б", NodeIdsJson = "[1,2]" }], 1.0, [], []);

        Assert.Equal(2, elements.Count);
        Assert.Equal(3, nodes.Count);
    }
}
