using CScore.Fem;
using Xunit;

namespace CScore.Tests.Fem;

/// <summary>Состав цели проверки: КЭ сетки группы и конструктивного элемента.</summary>
public class FemCheckScopeTests
{
    static FemElement Mesh(string tag, string? memberTag = null, string type = "shell") =>
        new() { ElemTag = tag, ElemType = type, SourceMemberTag = memberTag };

    [Fact]
    public void ForMember_TakesMeshElementsBoundToMember()
    {
        var wall = new FemMember { ElemTag = "кБ5", ElemType = "shell" };
        var mesh = new[] { Mesh("12", "кБ5"), Mesh("10", "кБ5"), Mesh("11", "кБ6"), Mesh("13") };

        var scope = FemCheckScope.ForMember(wall, mesh);

        Assert.False(scope.RefersToMeshElements);
        Assert.Same(wall, Assert.Single(scope.Members));
        Assert.Equal([10, 12], scope.ElementNumbers);
        Assert.All(scope.Elements, e => Assert.Same(wall, e.Member));
    }

    [Fact]
    public void ForMember_NotDiscretized_IsEmpty()
    {
        var scope = FemCheckScope.ForMember(new FemMember { ElemTag = "7" }, [Mesh("1", "8")]);

        Assert.Empty(scope.Elements);
        Assert.Empty(scope.ElementNumbers);
    }

    [Fact]
    public void ForGroup_ImportedSchema_RefersToMeshElementNumbers()
    {
        // Группа кБ ЛИРЫ ссылается на номера КЭ; часть КЭ уже привязана к элементу из кБ.
        var wall = new FemMember { ElemTag = "кБ5", ElemType = "shell" };
        var group = new FemMemberGroup { Tag = "СТЕНА №5", MemberTagsJson = "[10,11,99]" };
        var mesh = new[] { Mesh("10", "кБ5"), Mesh("11"), Mesh("12", "кБ5") };

        var scope = FemCheckScope.ForGroup(group, [wall], mesh);

        Assert.True(scope.RefersToMeshElements);
        Assert.Empty(scope.Members);
        Assert.Equal([10, 11], scope.ElementNumbers); // КЭ 99 в сетке нет
        Assert.Same(wall, scope.Elements.Single(e => e.ElemNum == 10).Member);
        Assert.Null(scope.Elements.Single(e => e.ElemNum == 11).Member);
    }

    [Fact]
    public void ForGroup_ConstructiveMembers_TakesTheirMesh()
    {
        var b7 = new FemMember { ElemTag = "7" };
        var b8 = new FemMember { ElemTag = "8" };
        var group = new FemMemberGroup { Tag = "Балки", MemberTagsJson = "[7,8]" };
        var mesh = new[] { Mesh("101", "7", "beam"), Mesh("102", "7", "beam"), Mesh("103", "8", "beam"), Mesh("104", "9", "beam") };

        var scope = FemCheckScope.ForGroup(group, [b7, b8, new FemMember { ElemTag = "9" }], mesh);

        Assert.False(scope.RefersToMeshElements);
        Assert.Equal([b7, b8], scope.Members);
        Assert.Equal([101, 102, 103], scope.ElementNumbers);
        Assert.Same(b8, scope.Elements.Single(e => e.ElemNum == 103).Member);
    }

    [Fact]
    public void ForGroup_TagCollisionWithOwnMember_StaysMeshGroupUnlessAllTagsAreMembers()
    {
        // Импортированная схема, к которой добавлен свой элемент с тегом «1»: группа ЛИРЫ [1,2]
        // по-прежнему ссылается на КЭ 1 и 2, а не на элемент «1».
        var own = new FemMember { ElemTag = "1" };
        var group = new FemMemberGroup { Tag = "Колонна", MemberTagsJson = "[1,2]" };
        var mesh = new[] { Mesh("1", type: "beam"), Mesh("2", type: "beam"), Mesh("500", "1", "beam") };

        var scope = FemCheckScope.ForGroup(group, [own], mesh);

        Assert.True(scope.RefersToMeshElements);
        Assert.Equal([1, 2], scope.ElementNumbers);
    }

    [Theory]
    [InlineData("[]")]
    [InlineData("не json")]
    public void ForGroup_EmptyOrBrokenTags_IsEmpty(string json)
    {
        var scope = FemCheckScope.ForGroup(new FemMemberGroup { MemberTagsJson = json }, [], [Mesh("1")]);

        Assert.Empty(scope.Elements);
        Assert.Empty(scope.Members);
    }
}
