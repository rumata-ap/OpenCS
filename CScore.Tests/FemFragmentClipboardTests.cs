using CScore.Fem;
using CScore.Fem.Editing;
using Xunit;

namespace CScore.Tests;

public sealed class FemFragmentClipboardTests
{
    [Fact]
    public void Copy_CapturesOnlySelectedNodesMembersAndFullyContainedGroups()
    {
        var session = new FemSchemaEditSession(new FemSchema { Id = 1 });
        session.Nodes.Add(new FemNode { Id = 1, NodeTag = "1", X = 0 });
        session.Nodes.Add(new FemNode { Id = 2, NodeTag = "2", X = 1 });
        session.Nodes.Add(new FemNode { Id = 3, NodeTag = "3", X = 2 });
        session.Members.Add(new FemMember { Id = 1, ElemTag = "1", NodeIdsJson = "[1,2]" });
        session.Members.Add(new FemMember { Id = 2, ElemTag = "2", NodeIdsJson = "[2,3]" });
        session.MemberGroups.Add(new FemMemberGroup { Id = 1, Tag = "M1", MemberTagsJson = "[1]" });

        var snapshot = FemFragmentClipboard.Copy(session,
            nodeTags: new HashSet<string> { "1", "2" },
            memberTags: new HashSet<string> { "1" });

        Assert.Equal(2, snapshot.Nodes.Count);
        Assert.Single(snapshot.Members);
        Assert.Single(snapshot.MemberGroups);
    }

    [Fact]
    public void Copy_PullsInMemberNodesEvenWhenNotExplicitlySelected()
    {
        var session = new FemSchemaEditSession(new FemSchema { Id = 1 });
        session.Nodes.Add(new FemNode { Id = 1, NodeTag = "1", X = 0 });
        session.Nodes.Add(new FemNode { Id = 2, NodeTag = "2", X = 1 });
        session.Members.Add(new FemMember { Id = 1, ElemTag = "1", NodeIdsJson = "[1,2]" });

        // Пользователь выделил только стержень, ни один узел явно не выбран.
        var snapshot = FemFragmentClipboard.Copy(session,
            nodeTags: new HashSet<string>(),
            memberTags: new HashSet<string> { "1" });

        Assert.Equal(2, snapshot.Nodes.Count);
        Assert.Single(snapshot.Members);
    }

    [Fact]
    public void Paste_GeneratesNewTagsAppliesOffsetAndRemapsReferences()
    {
        var session = new FemSchemaEditSession(new FemSchema { Id = 1 });
        // Id намеренно НЕ совпадает с NodeTag/ElemTag: ссылки в JSON идут по Tag, а не по БД-Id.
        session.Nodes.Add(new FemNode { Id = 101, NodeTag = "1", X = 0, Y = 0, Z = 0 });
        session.Nodes.Add(new FemNode { Id = 102, NodeTag = "2", X = 1, Y = 0, Z = 0 });
        session.Members.Add(new FemMember { Id = 201, ElemTag = "1", NodeIdsJson = "[1,2]",
            CrossSectionId = 5, GjStrategy = "manual", GjManualValue = 100 });
        session.MemberGroups.Add(new FemMemberGroup { Id = 1, Tag = "M1", MemberTagsJson = "[1]" });

        var snapshot = FemFragmentClipboard.Copy(session,
            new HashSet<string> { "1", "2" }, new HashSet<string> { "1" });

        session.Execute(new PasteFragmentCommand(snapshot, dx: 10, dy: 0, dz: 0));

        Assert.Equal(4, session.Nodes.Count);
        var pastedNode = session.Nodes.Single(n => n.NodeTag == "3");
        Assert.Equal(10, pastedNode.X);
        var pastedMember = session.Members.Single(m => m.ElemTag == "2");
        Assert.Equal(5, pastedMember.CrossSectionId);
        Assert.Equal("manual", pastedMember.GjStrategy);
        var pastedGroup = session.MemberGroups.Single(g => g.Tag != "M1");
        Assert.Equal("M1 (копия)", pastedGroup.Tag);

        session.Undo();
        Assert.Equal(2, session.Nodes.Count);
        Assert.Single(session.Members);
        Assert.Single(session.MemberGroups);
    }

    [Fact]
    public void Paste_PreservesSaintVenantTaskReference()
    {
        var session = new FemSchemaEditSession(new FemSchema { Id = 1 });
        session.Nodes.Add(new FemNode { Id = 1, NodeTag = "1", X = 0 });
        session.Nodes.Add(new FemNode { Id = 2, NodeTag = "2", X = 1 });
        session.Members.Add(new FemMember
        {
            Id = 1, ElemTag = "1", NodeIdsJson = "[1,2]",
            GjStrategy = "saint_venant", GjTorsionTaskId = 42
        });

        var snapshot = FemFragmentClipboard.Copy(
            session, new HashSet<string> { "1", "2" }, new HashSet<string> { "1" });
        session.Execute(new PasteFragmentCommand(snapshot, 0, 0, 0));

        var pasted = session.Members.Single(member => member.ElemTag == "2");
        Assert.Equal("saint_venant", pasted.GjStrategy);
        Assert.Null(pasted.GjManualValue);
        Assert.Equal(42, pasted.GjTorsionTaskId);
    }

    [Fact]
    public void Paste_CopiesAllMemberProperties_ExceptElementBindings()
    {
        var session = TwoNodeBar(out _);
        var source = session.Members[0];
        source.ElemType = "shell";
        source.SectionTag = "S1"; source.MaterialTag = "B25"; source.ThicknessM = 0.2;
        source.TargetMeshLengthM = 0.4; source.PlateSectionId = 7; source.DesignParamsJson = "{\"a\":1}";
        source.RotationDeg = 30; source.Kind = FemMemberTypes.Shell; source.KindSource = "manual";
        source.ForceSetId = 9; source.MeshSource = FemMember.MeshSourceImported;

        var snapshot = FemFragmentClipboard.Copy(session, new HashSet<string>(), new HashSet<string> { "1" });
        session.Execute(new PasteFragmentCommand(snapshot, 0, 0, 1));

        var copy = session.Members.Single(m => m.ElemTag == "2");
        Assert.Equal(("shell", "S1", "B25", 0.2, 0.4, 7), (copy.ElemType, copy.SectionTag, copy.MaterialTag,
            copy.ThicknessM, copy.TargetMeshLengthM, copy.PlateSectionId));
        Assert.Equal(("{\"a\":1}", 30.0, FemMemberTypes.Shell, "manual"),
            (copy.DesignParamsJson, copy.RotationDeg, copy.Kind, copy.KindSource));
        Assert.Null(copy.ForceSetId);
        Assert.Null(copy.PlanarRegionId);
        Assert.Equal(FemMember.MeshSourceGenerated, copy.MeshSource);
    }

    [Fact]
    public void Paste_SeveralCopies_StepsOffsetAndUndoesAtOnce()
    {
        var session = TwoNodeBar(out _);
        var snapshot = FemFragmentClipboard.Copy(session, new HashSet<string>(), new HashSet<string> { "1" });

        session.Execute(new PasteFragmentCommand(snapshot, 0, 3, 0, copies: 3));

        Assert.Equal(8, session.Nodes.Count);
        Assert.Equal(4, session.Members.Count);
        Assert.Equal([0.0, 3.0, 6.0, 9.0], session.Nodes.Where(n => n.X == 0).Select(n => n.Y).Order());
        // Новые узлы получают временные отрицательные Id, как при обычном создании узла, — не нулевые и не повторяющиеся.
        var newIds = session.Nodes.Skip(2).Select(n => n.Id).ToList();
        Assert.All(newIds, id => Assert.True(id < 0));
        Assert.Equal(newIds.Count, newIds.Distinct().Count());

        session.Undo();
        Assert.Equal(2, session.Nodes.Count);
        Assert.Single(session.Members);
    }

    [Fact]
    public void Copy_SkipsPlanarRegionShells()
    {
        var session = TwoNodeBar(out _);
        session.Nodes.Add(new FemNode { Id = 3, NodeTag = "3", X = 0, Y = 1 });
        session.Members.Add(new FemMember { Id = 2, ElemTag = "2", ElemType = "shell", NodeIdsJson = "[1,2,3]",
            PlanarRegionId = 5 });

        var snapshot = FemFragmentClipboard.Copy(session, new HashSet<string>(), new HashSet<string> { "1", "2" });

        Assert.Equal("1", Assert.Single(snapshot.Members).ElemTag);
        Assert.Equal(1, snapshot.SkippedPlanarMembers);
        Assert.Equal(2, snapshot.Nodes.Count);
    }

    [Fact]
    public void MoveNodes_ShiftsSelectionAndMemberNodes_OneUndo()
    {
        var session = TwoNodeBar(out _);
        session.Nodes.Add(new FemNode { Id = 3, NodeTag = "3", X = 5 });

        var move = FemFragmentTargets.ForMove(session, nodeTags: ["3"], memberTags: ["1"]);
        Assert.Empty(move.Blockers);
        session.Execute(new MoveNodesCommand(move.Nodes, 1, 2, 3));

        Assert.Equal([(1.0, 2.0, 3.0), (2.0, 2.0, 3.0), (6.0, 2.0, 3.0)],
            session.Nodes.Select(n => (n.X, n.Y, n.Z)));
        session.Undo();
        Assert.Equal([(0.0, 0.0, 0.0), (1.0, 0.0, 0.0), (5.0, 0.0, 0.0)],
            session.Nodes.Select(n => (n.X, n.Y, n.Z)));
    }

    [Fact]
    public void ForMove_ReportsPlanarAndImportedMeshMembersOnMovedNodes()
    {
        var session = TwoNodeBar(out _);
        session.Nodes.Add(new FemNode { Id = 3, NodeTag = "3", X = 0, Y = 1 });
        session.Nodes.Add(new FemNode { Id = 4, NodeTag = "4", X = 9, Y = 9 });
        session.Members.Add(new FemMember { Id = 2, ElemTag = "2", ElemType = "shell", NodeIdsJson = "[2,3,1]",
            PlanarRegionId = 5 });
        session.Members.Add(new FemMember { Id = 3, ElemTag = "3", NodeIdsJson = "[2,4]",
            MeshSource = FemMember.MeshSourceImported });
        session.Members.Add(new FemMember { Id = 4, ElemTag = "4", NodeIdsJson = "[3,4]",
            MeshSource = FemMember.MeshSourceImported, PlanarRegionId = null });

        var move = FemFragmentTargets.ForMove(session, nodeTags: [], memberTags: ["1"]);

        Assert.Equal(["2", "3"], move.Blockers.Select(m => m.ElemTag));
    }

    static FemSchemaEditSession TwoNodeBar(out FemMember bar)
    {
        var session = new FemSchemaEditSession(new FemSchema { Id = 1 });
        session.Nodes.Add(new FemNode { Id = 1, NodeTag = "1", X = 0 });
        session.Nodes.Add(new FemNode { Id = 2, NodeTag = "2", X = 1 });
        bar = new FemMember { Id = 1, ElemTag = "1", NodeIdsJson = "[1,2]" };
        session.Members.Add(bar);
        return session;
    }
}
