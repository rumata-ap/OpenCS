using CScore.Fem;
using CScore.Fem.Editing;
using Xunit;

namespace CScore.Tests;

public sealed class MergeCoincidentNodesCommandTests
{
    static FemSchemaEditSession NewSession(int schemaId = 1) => new(new FemSchema { Id = schemaId });

    [Fact]
    public void Do_TwoCoincidentNodes_MergesIntoLowerTagAndRewritesMemberReferences()
    {
        var session = NewSession();
        var n1 = new FemNode { Id = 1, NodeTag = "1", X = 0, Y = 0, Z = 0 };
        var n2 = new FemNode { Id = 2, NodeTag = "2", X = 5, Y = 0, Z = 0 };
        var n3 = new FemNode { Id = 3, NodeTag = "3", X = 5, Y = 0, Z = 0 }; // совпадает с n2, тег больше
        session.Nodes.AddRange([n1, n2, n3]);
        session.Members.Add(new FemMember { Id = 1, ElemTag = "1", NodeIdsJson = "[1,3]" }); // ссылается на "лишний"

        var command = new MergeCoincidentNodesCommand();
        session.Execute(command);

        Assert.Equal(2, session.Nodes.Count);
        Assert.DoesNotContain(session.Nodes, n => n.NodeTag == "3");
        Assert.Equal("[1,2]", session.Members[0].NodeIdsJson);

        var group = Assert.Single(command.LastResult);
        Assert.Equal("2", group.SurvivorTag);
        Assert.Equal(new[] { "3" }, group.MergedTags);
    }

    [Fact]
    public void Do_ConflictingDofMask_MergesByBitwiseOr()
    {
        var session = NewSession();
        var n1 = new FemNode { Id = 1, NodeTag = "1", X = 0, Y = 0, Z = 0, DofMask = 0 };
        var n2 = new FemNode { Id = 2, NodeTag = "2", X = 0, Y = 0, Z = 0, DofMask = 15 };
        session.Nodes.AddRange([n1, n2]);

        session.Execute(new MergeCoincidentNodesCommand());

        Assert.Equal(15, session.Nodes.Single().DofMask);
    }

    [Fact]
    public void Do_LoadsOnSameCase_AreSummedIntoOneRecord()
    {
        var session = NewSession();
        var n1 = new FemNode { Id = 1, NodeTag = "1", X = 0, Y = 0, Z = 0 };
        var n2 = new FemNode { Id = 2, NodeTag = "2", X = 0, Y = 0, Z = 0 };
        session.Nodes.AddRange([n1, n2]);
        session.LoadCases.Add(new FemLoadCase { Id = 1, Tag = "G" });
        session.NodeLoads.Add(new FemNodeLoad { Id = 1, LoadCaseId = 1, NodeId = 1, Fz = -100 });
        session.NodeLoads.Add(new FemNodeLoad { Id = 2, LoadCaseId = 1, NodeId = 2, Fz = -50 });

        session.Execute(new MergeCoincidentNodesCommand());

        var load = Assert.Single(session.NodeLoads);
        Assert.Equal(1, load.NodeId);
        Assert.Equal(-150, load.Fz);
    }

    [Fact]
    public void Do_LoadsOnDifferentCases_AreReassignedWithoutSumming()
    {
        var session = NewSession();
        var n1 = new FemNode { Id = 1, NodeTag = "1", X = 0, Y = 0, Z = 0 };
        var n2 = new FemNode { Id = 2, NodeTag = "2", X = 0, Y = 0, Z = 0 };
        session.Nodes.AddRange([n1, n2]);
        session.LoadCases.Add(new FemLoadCase { Id = 1, Tag = "G" });
        session.LoadCases.Add(new FemLoadCase { Id = 2, Tag = "Q" });
        session.NodeLoads.Add(new FemNodeLoad { Id = 1, LoadCaseId = 1, NodeId = 1, Fz = -100 });
        session.NodeLoads.Add(new FemNodeLoad { Id = 2, LoadCaseId = 2, NodeId = 2, Fz = -50 });

        session.Execute(new MergeCoincidentNodesCommand());

        Assert.Equal(2, session.NodeLoads.Count);
        Assert.All(session.NodeLoads, load => Assert.Equal(1, load.NodeId));
    }

    [Fact]
    public void Do_KinematicLoads_AreReassignedToSurvivor()
    {
        var session = NewSession();
        var n1 = new FemNode { Id = 1, NodeTag = "1", X = 0, Y = 0, Z = 0 };
        var n2 = new FemNode { Id = 2, NodeTag = "2", X = 0, Y = 0, Z = 0 };
        session.Nodes.AddRange([n1, n2]);
        session.KinematicLoads.Add(new FemKinematicLoad
        {
            Id = 1, LoadCaseId = 1, NodeId = 2, Dof = 1, Value = 0.015
        });

        session.Execute(new MergeCoincidentNodesCommand());

        var load = Assert.Single(session.KinematicLoads);
        Assert.Equal(1, load.NodeId);
        Assert.Equal(0.015, load.Value);

        session.Undo();
        Assert.Equal(2, session.Nodes.Count);
        Assert.Equal(2, session.KinematicLoads.Single().NodeId);
    }

    [Fact]
    public void Do_DuplicateKinematicLoads_KeepsSurvivorAndUndoRestoresDuplicate()
    {
        var session = NewSession();
        session.Nodes.AddRange([
            new FemNode { Id = 1, NodeTag = "1", X = 0, Y = 0, Z = 0 },
            new FemNode { Id = 2, NodeTag = "2", X = 0, Y = 0, Z = 0 },
        ]);
        session.KinematicLoads.Add(new FemKinematicLoad
        {
            Id = 1, LoadCaseId = 1, NodeId = 1, Dof = 1, Value = 0.015
        });
        session.KinematicLoads.Add(new FemKinematicLoad
        {
            Id = 2, LoadCaseId = 1, NodeId = 2, Dof = 1, Value = 0.015
        });

        session.Execute(new MergeCoincidentNodesCommand());
        Assert.Single(session.KinematicLoads);
        Assert.Equal(1, session.KinematicLoads[0].NodeId);

        session.Undo();
        Assert.Equal(2, session.KinematicLoads.Count);
        Assert.Contains(session.KinematicLoads, load => load.NodeId == 1);
        Assert.Contains(session.KinematicLoads, load => load.NodeId == 2);
    }

    [Fact]
    public void Undo_FullyRestoresNodesMembersLoadsAndDofMask()
    {
        var session = NewSession();
        var n1 = new FemNode { Id = 1, NodeTag = "1", X = 0, Y = 0, Z = 0, DofMask = 0 };
        var n2 = new FemNode { Id = 2, NodeTag = "2", X = 0, Y = 0, Z = 0, DofMask = 63 };
        session.Nodes.AddRange([n1, n2]);
        session.Members.Add(new FemMember { Id = 1, ElemTag = "1", NodeIdsJson = "[1,2]" });
        session.LoadCases.Add(new FemLoadCase { Id = 1, Tag = "G" });
        session.NodeLoads.Add(new FemNodeLoad { Id = 1, LoadCaseId = 1, NodeId = 2, Fz = -50 });

        session.Execute(new MergeCoincidentNodesCommand());
        Assert.Single(session.Nodes);

        session.Undo();

        Assert.Equal(2, session.Nodes.Count);
        Assert.Equal("[1,2]", session.Members[0].NodeIdsJson);
        Assert.Equal(0, session.Nodes.Single(n => n.NodeTag == "1").DofMask);
        Assert.Equal(63, session.Nodes.Single(n => n.NodeTag == "2").DofMask);
        Assert.Single(session.NodeLoads);
        Assert.Equal(2, session.NodeLoads[0].NodeId);
    }

    [Fact]
    public void Do_NoCoincidentNodes_ReportsEmptyResultAndChangesNothing()
    {
        var session = NewSession();
        session.Nodes.AddRange([
            new FemNode { Id = 1, NodeTag = "1", X = 0, Y = 0, Z = 0 },
            new FemNode { Id = 2, NodeTag = "2", X = 5, Y = 0, Z = 0 },
        ]);

        var command = new MergeCoincidentNodesCommand();
        session.Execute(command);

        Assert.Empty(command.LastResult);
        Assert.Equal(2, session.Nodes.Count);
    }

    [Fact]
    public void Do_ThreeTransitivelyCoincidentNodes_MergeIntoOneGroup()
    {
        var session = NewSession();
        // A-B и B-C — каждая пара в пределах допуска (0.0009 м), но A-C (0.0018 м) — уже нет.
        // Транзитивное объединение через B должно всё равно слить все три в одну группу.
        var a = new FemNode { Id = 3, NodeTag = "3", X = 0, Y = 0, Z = 0 };
        var b = new FemNode { Id = 1, NodeTag = "1", X = 0.0009, Y = 0, Z = 0 };
        var c = new FemNode { Id = 2, NodeTag = "2", X = 0.0018, Y = 0, Z = 0 };
        session.Nodes.AddRange([a, b, c]);

        session.Execute(new MergeCoincidentNodesCommand());

        Assert.Single(session.Nodes);
        Assert.Equal("1", session.Nodes[0].NodeTag);
    }
}
