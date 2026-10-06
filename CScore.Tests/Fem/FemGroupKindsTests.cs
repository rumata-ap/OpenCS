using CScore.Fem;
using CScore.Fem.Import;
using Xunit;

namespace CScore.Tests.Fem;

/// <summary>Группы КЭ и группы КонЭ: теги, коды типов, состав и контракт импорта.</summary>
public class FemGroupKindsTests
{
    static FemElement Mesh(string tag, string? memberTag = null, string origin = FemMember.MeshSourceImported,
        string type = "beam", string nodes = "[1,2]") =>
        new() { ElemTag = tag, ElemType = type, SourceMemberTag = memberTag, Origin = origin, NodeIdsJson = nodes };

    [Theory]
    [InlineData("[1,2,3]", new[] { "1", "2", "3" })]
    [InlineData("[\"Колонна №5 · 1\",\"7\"]", new[] { "Колонна №5 · 1", "7" })]
    [InlineData("[]", new string[0])]
    [InlineData("не json", new string[0])]
    [InlineData("{\"a\":1}", new string[0])]
    public void ParseTags_ReadsStringsAndLegacyNumbers(string json, string[] expected)
    {
        Assert.Equal(expected, FemMemberGroup.ParseTags(json));
    }

    [Fact]
    public void SetTags_TrimsDropsEmptyAndDuplicates_WritesStrings()
    {
        var group = new FemMemberGroup();
        group.SetTags([" 7", "Колонна №5 · 1", "", "7"]);

        Assert.Equal(["7", "Колонна №5 · 1"], group.Tags);
        Assert.Equal("[\"7\",\"Колонна №5 · 1\"]", System.Text.RegularExpressions.Regex.Unescape(group.MemberTagsJson));
    }

    [Theory]
    [InlineData("Балка", FemMemberTypes.Beam)]
    [InlineData("колонна", FemMemberTypes.Column)]
    [InlineData("Плита", FemMemberTypes.Plate)]
    [InlineData("Стена", FemMemberTypes.Wall)]
    [InlineData("Раскос", FemMemberTypes.Diagonal)]
    [InlineData("Верхний пояс", FemMemberTypes.TopChord)]
    [InlineData("bottom_chord", FemMemberTypes.BottomChord)]
    [InlineData("shell", FemMemberTypes.Shell)]
    [InlineData("Ригель Р1", "Ригель Р1")]
    [InlineData("  ", null)]
    [InlineData(null, null)]
    public void MemberTypes_Normalize_MapsLegacyNamesToCodes(string? value, string? expected)
    {
        Assert.Equal(expected, FemMemberTypes.Normalize(value));
    }

    [Fact]
    public void NewGroups_KindOriginTypeAndDefaultTag()
    {
        var mesh = FemGroupComposition.NewMeshGroup(3, ["10", "11"], null, "Плита");
        var members = FemGroupComposition.NewMembersGroup(3, ["К1"], "Колонны", "column", FemMemberGroup.OriginAuto);

        Assert.Equal(FemMemberGroup.KindMesh, mesh.Kind);
        Assert.Equal("Группа КЭ (2)", mesh.Tag);
        Assert.Equal(FemMemberTypes.Plate, mesh.MemberType);
        Assert.Equal(FemMemberGroup.OriginManual, mesh.Origin);
        Assert.Equal(FemMemberGroup.KindMembers, members.Kind);
        Assert.Equal("Колонны", members.Tag);
        Assert.Equal(FemMemberGroup.OriginAuto, members.Origin);
        Assert.Equal(3, members.SchemaId);
    }

    [Fact]
    public void AddRemoveTags_CountChanges()
    {
        var group = FemGroupComposition.NewMeshGroup(1, ["1", "2"], "Г", null);

        Assert.Equal(1, FemGroupComposition.AddTags(group, ["2", "3"]));
        Assert.Equal(2, FemGroupComposition.RemoveTags(group, ["1", "3", "99"]));
        Assert.Equal(["2"], group.Tags);
    }

    [Fact]
    public void RemoveMemberTags_TouchesOnlyMembersGroups_AndReturnsUndoJson()
    {
        // Номер КЭ «5» совпадает с тегом удаляемого КонЭ: группа КЭ не должна его терять.
        var meshGroup = FemGroupComposition.NewMeshGroup(1, ["5", "6"], "КЭ", null);
        var membersGroup = FemGroupComposition.NewMembersGroup(1, ["5", "Б1"], "КонЭ", null);
        string before = membersGroup.MemberTagsJson;

        var edits = FemGroupComposition.RemoveMemberTags([meshGroup, membersGroup], ["5"]);

        Assert.Equal(["5", "6"], meshGroup.Tags);
        Assert.Equal(["Б1"], membersGroup.Tags);
        var (group, oldJson) = Assert.Single(edits);
        Assert.Same(membersGroup, group);
        Assert.Equal(before, oldJson);
    }

    [Fact]
    public void CheckMeshTags_AcceptsOnlyImported_ReportsGeneratedOwnersAndUnknown()
    {
        var mesh = new[]
        {
            Mesh("1"), Mesh("2"),
            Mesh("101", "Б1", FemMember.MeshSourceGenerated), Mesh("102", "Б1", FemMember.MeshSourceGenerated),
            Mesh("103", "Б2", FemMember.MeshSourceGenerated),
        };

        var check = FemGroupComposition.CheckMeshTags(["1", "101", "102", "103", "2", "1", "999"], mesh);

        Assert.Equal(["1", "2"], check.Accepted);
        Assert.Equal(["101", "102", "103"], check.Generated);
        Assert.Equal(["Б1", "Б2"], check.GeneratedOwners);
        Assert.Equal(["999"], check.Unknown);
        Assert.False(check.AllAccepted);
    }

    /// <summary>Импорт программы с моделью из КонЭ (как Robot): элементы со своей сеткой и группа КонЭ.</summary>
    static FemImportResult MemberProgramImport()
    {
        var meshNodes = Enumerable.Range(1, 4).Select(i => new FemMeshNode { NodeTag = i.ToString(), X = i - 1 }).ToList();
        var meshElements = new List<FemElement> { Mesh("1", nodes: "[1,2]"), Mesh("2", nodes: "[2,3]"), Mesh("3", nodes: "[3,4]") };
        var nodes = new List<FemNode> { new() { NodeTag = "1" }, new() { NodeTag = "3" }, new() { NodeTag = "4" } };
        var b1 = new FemMember { ElemTag = "Бар 1", ElemType = "beam", NodeIdsJson = "[1,3]", MeshSource = FemMember.MeshSourceImported };
        var b2 = new FemMember { ElemTag = "Бар 2", ElemType = "beam", NodeIdsJson = "[3,4]", MeshSource = FemMember.MeshSourceImported };
        var group = FemGroupComposition.NewMembersGroup(0, ["Бар 1", "Бар 2"], "Ригели", FemMemberTypes.Beam,
            FemMemberGroup.ImportOrigin("robot"));
        return new FemImportResult(meshNodes, meshElements, nodes,
            [new FemImportMember(b1, null, ["1", "2"]), new FemImportMember(b2, null, ["3"])], [group]);
    }

    [Fact]
    public void ImportResult_MemberProgram_IsValid_AndGroupScopeFindsMesh()
    {
        var import = MemberProgramImport();

        Assert.Empty(import.Validate());

        // Как после сохранения: КЭ сетки знают свой элемент.
        foreach (var (member, _, tags) in import.Members)
            foreach (var e in import.MeshElements.Where(e => tags.Contains(e.ElemTag)))
                e.SourceMemberTag = member.ElemTag;
        var scope = FemCheckScope.ForGroup(import.Groups[0], import.Members.Select(m => m.Member), import.MeshElements);

        Assert.Equal(2, scope.Members.Count);
        Assert.Equal([1, 2, 3], scope.ElementNumbers);
    }

    [Fact]
    public void ImportResult_Validate_ReportsBrokenReferences()
    {
        var import = MemberProgramImport();
        var broken = import with
        {
            Members =
            [
                .. import.Members,
                new FemImportMember(new FemMember { ElemTag = "Бар 1", NodeIdsJson = "[1,9]" }, null, ["2", "77"]),
            ],
            Groups =
            [
                .. import.Groups,
                FemGroupComposition.NewMeshGroup(0, ["1", "55"], "КЭ", null),
                new FemMemberGroup { Tag = "Странная", Kind = "x" },
            ],
        };

        var codes = broken.Validate().Select(e => e.Code).ToHashSet();

        Assert.Contains("import_member_duplicate", codes);
        Assert.Contains("import_member_node_missing", codes);
        Assert.Contains("import_member_element_missing", codes);
        Assert.Contains("import_element_two_members", codes);
        Assert.Contains("import_group_tag_missing", codes);
        Assert.Contains("import_group_kind", codes);
        Assert.All(broken.Validate(), d => Assert.True(d.IsError));
    }

    [Fact]
    public void ImportResult_PruneMissingGroupTags_RemovesOnlyUnknown()
    {
        var meshGroup = FemGroupComposition.NewMeshGroup(0, ["1", "2", "56"], "кБ", null);
        var import = FemImportResult.MeshOnly(
            [new FemMeshNode { NodeTag = "1" }, new FemMeshNode { NodeTag = "2" }],
            [Mesh("1"), Mesh("2", nodes: "[2,1]")], [meshGroup]);

        Assert.Equal(1, import.PruneMissingGroupTags());
        Assert.Equal(["1", "2"], meshGroup.Tags);
        Assert.Empty(import.Validate());
    }
}
