using System.Text.Json;
using CScore.Fem;
using CScore.Fem.Fragment;
using Xunit;

namespace CScore.Tests.Fem.Fragment;

/// <summary>Снятие замка сохранённой сетки с КонЭ фрагмента (срез 4): удаление КЭ и узлов, перенос ГУ, тел, нагрузок.</summary>
public sealed class FemKeptMeshUnlockTests
{
    /// <summary>
    /// Сохранённая плита P1 2×2 КЭ (1 м, z = 0, узлы 1001…1009, КЭ 2001…2004) и балка B1 по кромке y = 0 двумя КЭ
    /// (3001, 3002). Узлы схемы: концы балки 101 (0,0), 102 (2,0) и 103 (0,2) — у узлов сетки 1001, 1003, 1007
    /// <c>SourceNodeTag</c>.
    /// </summary>
    sealed class Fixture
    {
        public readonly List<FemNode> Nodes =
        [
            new() { Id = 1, NodeTag = "101", X = 0, Y = 0, Z = 0 },
            new() { Id = 2, NodeTag = "102", X = 2, Y = 0, Z = 0 },
            new() { Id = 3, NodeTag = "103", X = 0, Y = 2, Z = 0 },
        ];
        public readonly List<FemMember> Members;
        public readonly List<FemMeshNode> MeshNodes = [];
        public readonly List<FemElement> Elements = [];
        public readonly List<FemMeshNodeSupport> Supports = [];
        public readonly List<FemSpring> Springs = [];
        public readonly List<FemRigidBody> Bodies = [];
        public readonly List<FemElementLoad> ElementLoads = [];
        public readonly List<FemMeshNodeLoad> MeshNodeLoads = [];
        public readonly HashSet<string> RefTags = new(StringComparer.Ordinal);

        public Fixture(bool slabRegion = true)
        {
            Members =
            [
                new() { Id = 1, ElemTag = "P1", ElemType = "shell", PlanarRegionId = slabRegion ? 1 : null, MeshSource = FemMember.MeshSourceImported },
                new() { Id = 2, ElemTag = "B1", ElemType = "beam", NodeIdsJson = "[101,102]", MeshSource = FemMember.MeshSourceImported },
            ];
            string? Source(int tag) => tag switch { 1001 => "101", 1003 => "102", 1007 => "103", _ => null };
            for (int j = 0; j <= 2; j++)
                for (int i = 0; i <= 2; i++)
                {
                    int tag = 1001 + j * 3 + i;
                    MeshNodes.Add(new FemMeshNode
                    {
                        NodeTag = tag.ToString(), X = i, Y = j, Z = 0, Origin = FemMember.MeshSourceImported, SourceNodeTag = Source(tag),
                    });
                }
            for (int j = 0; j < 2; j++)
                for (int i = 0; i < 2; i++)
                {
                    int n = 1001 + j * 3 + i;
                    Elements.Add(new FemElement
                    {
                        ElemTag = (2001 + Elements.Count).ToString(), ElemType = "shell", SourceMemberTag = "P1",
                        Origin = FemMember.MeshSourceImported, ThicknessM = 0.2,
                        NodeIdsJson = JsonSerializer.Serialize(new[] { n, n + 1, n + 3, n + 4 }),
                    });
                }
            Elements.Add(new FemElement { ElemTag = "3001", ElemType = "beam", NodeIdsJson = "[1001,1002]", SourceMemberTag = "B1", Origin = FemMember.MeshSourceImported });
            Elements.Add(new FemElement { ElemTag = "3002", ElemType = "beam", NodeIdsJson = "[1002,1003]", SourceMemberTag = "B1", Origin = FemMember.MeshSourceImported });
        }

        public FemKeptMeshUnlockPlan Plan(params string[] tags) => FemKeptMeshUnlock.Plan(new FemKeptMeshUnlockInput(1, Nodes, Members,
            MeshNodes, Elements, Supports, Springs, Bodies, ElementLoads, MeshNodeLoads, RefTags), tags);
    }

    static FemElementLoad Load(string kind, IEnumerable<string> tags, params double[] values)
    {
        var load = new FemElementLoad { Id = 7, LoadCaseId = 5, LoadKind = kind, CoordinateSystem = "global", Axis = "z" };
        load.SetTargetTags(tags);
        load.SetValues(values);
        return load;
    }

    [Fact]
    public void Beam_RemovedElements_SlabNodesKept_InputUntouched()
    {
        var f = new Fixture();
        var plan = f.Plan("B1");

        Assert.Equal(["B1"], plan.UnlockedMemberTags);
        Assert.Equal(["3001", "3002"], plan.RemovedElementTags);
        Assert.Empty(plan.RemovedNodeTags);
        Assert.Equal(9, plan.MeshNodes.Count);
        Assert.Equal(4, plan.MeshElements.Count);
        Assert.Equal(FemMember.MeshSourceGenerated, plan.Members.Single(m => m.ElemTag == "B1").MeshSource);
        Assert.True(f.Members.Single(m => m.ElemTag == "B1").IsMeshLocked);
        Assert.Same(f.Members[0], plan.Members.Single(m => m.ElemTag == "P1"));
    }

    [Fact]
    public void Slab_OnlyOwnNodesRemoved_SupportsAndSpringsToSchemaNodes()
    {
        var f = new Fixture();
        f.Supports.Add(new FemMeshNodeSupport { NodeTag = "1005", Mask = 0b111 });
        f.Supports.Add(new FemMeshNodeSupport { NodeTag = "1007", Mask = 0b1 });
        f.Supports.Add(new FemMeshNodeSupport { NodeTag = "1002", Mask = 0b1 });
        var spring = new FemSpring { TargetKind = FemSpringTargetKinds.MeshNode, NodeTag = "1005", Origin = "lira" };
        spring.SetStiffnesses([0, 0, 1000, 0, 0, 0]);
        f.Springs.Add(spring);

        var plan = f.Plan("P1");

        // Узлы балки (1001…1003) остаются, остальные узлы плиты удаляются.
        Assert.Equal(["1004", "1005", "1006", "1007", "1008", "1009"], plan.RemovedNodeTags);
        // 1005 — новый свободный узел схемы выше всех тегов (схемы и сетки, включая удаляемые).
        var free = Assert.Single(plan.NewNodes);
        Assert.Equal("1010", free.NodeTag);
        Assert.Equal((1.0, 1.0, 0.0), (free.X, free.Y, free.Z));
        Assert.Equal(0b111, free.DofMask);
        var s = Assert.Single(plan.NodeSprings);
        Assert.Equal((FemSpringTargetKinds.Node, "1010", 1000.0, "lira"), (s.TargetKind, s.NodeTag, s.Kz, s.Origin));
        // 1007 — существующий узел схемы 103 по SourceNodeTag; вход не меняется.
        Assert.Equal(0b1, plan.NodeMasks["103"]);
        Assert.Equal(0b1, plan.Nodes.Single(n => n.NodeTag == "103").DofMask);
        Assert.Equal(0, f.Nodes.Single(n => n.NodeTag == "103").DofMask);
        // Закрепление оставшегося узла 1002 не трогается.
        Assert.DoesNotContain("102", plan.NodeMasks.Keys);
        Assert.Equal(4, plan.Nodes.Count);
    }

    [Fact]
    public void RigidBodiesOnRemovedNodes_Removed()
    {
        var f = new Fixture();
        var onRemoved = new FemRigidBody { Id = 11, MasterNodeTag = "1002", Origin = "fragment:auto:junction" };
        onRemoved.SetSlaveNodeTags(["1005"]);
        var kept = new FemRigidBody { Id = 12, MasterNodeTag = "1001", Origin = "fragment:lira" };
        kept.SetSlaveNodeTags(["1002"]);
        f.Bodies.AddRange([onRemoved, kept]);

        var plan = f.Plan("P1");

        Assert.Equal([11], plan.RemovedRigidBodies.Select(b => b.Id));
        Assert.Contains(plan.Diagnostics, d => d.Code == "fragment_mesh_unlock_rigid_removed" && !d.IsError);
        Assert.Empty(plan.NodeRigidBodies); // авто-АЖТ строится сборкой заново
    }

    /// <summary>Импортное тело (SCAD) на удаляемых узлах переводится на узлы схемы «по точкам»: ведущий — узел схемы 103
    /// по <c>SourceNodeTag</c>, ведомые — новый свободный узел (1005 удаляется) и узел на сохранённом 1002.</summary>
    [Fact]
    public void ImportedRigidBodyOnRemovedNodes_ToSchemaNodePoints()
    {
        var f = new Fixture();
        var scad = new FemRigidBody { Id = 13, MasterNodeTag = "1007", Origin = "fragment:import:scad", Mask = 63, SourceElemTag = "A1" };
        scad.SetSlaveNodeTags(["1005", "1002"]);
        f.Bodies.Add(scad);

        var plan = f.Plan("P1");

        Assert.Equal([13], plan.RemovedRigidBodies.Select(b => b.Id));
        var body = Assert.Single(plan.NodeRigidBodies);
        Assert.Equal(("103", "import:scad", 63, "A1"), (body.MasterNodeTag, body.Origin, body.Mask, body.SourceElemTag));
        var slaves = body.SlaveNodeTags.Select(t => plan.Nodes.Single(n => n.NodeTag == t)).Select(n => (n.X, n.Y)).Order().ToList();
        Assert.Equal([(1.0, 0.0), (1.0, 1.0)], slaves);
        Assert.All(body.SlaveNodeTags, t => Assert.Contains(plan.NewNodes, n => n.NodeTag == t));
        Assert.Contains(plan.Diagnostics, d => d.Code == "fragment_mesh_unlock_rigid_removed" && d.Message.Contains("по точкам"));
    }

    [Fact]
    public void UniformOnWholeSlab_LiftedToMember()
    {
        var f = new Fixture();
        f.ElementLoads.Add(Load(FemElementLoadKinds.Uniform, ["2001", "2002", "2003", "2004"], -5));

        var plan = f.Plan("P1");

        Assert.Equal([7], plan.ReplacedElementLoads.Select(l => l.Id));
        var lift = Assert.Single(plan.AddedElementLoads);
        Assert.Equal((FemLoadTargetKinds.Members, FemElementLoadKinds.Uniform, "global", "z", 5), (lift.TargetKind, lift.LoadKind, lift.CoordinateSystem, lift.Axis, lift.LoadCaseId));
        Assert.Equal(["P1"], lift.TargetTags);
        Assert.Equal([-5.0], lift.Values);
        Assert.Empty(plan.FieldLoads);
    }

    [Fact]
    public void UniformOnPartOfSlab_FieldAndRestOfTarget()
    {
        var f = new Fixture();
        f.RefTags.Add("2002");
        f.ElementLoads.Add(Load(FemElementLoadKinds.Uniform, ["2001", "2002", "3001"], -5));

        var plan = f.Plan("P1");

        var rest = Assert.Single(plan.AddedElementLoads);
        Assert.Equal((FemLoadTargetKinds.Elements, FemElementLoadKinds.Uniform), (rest.TargetKind, rest.LoadKind));
        Assert.Equal(["3001"], rest.TargetTags);
        var field = Assert.Single(plan.FieldLoads);
        Assert.Equal((5, FemElementLoadKinds.Uniform, "z"), (field.LoadCaseId, field.LoadKind, field.Axis));
        Assert.Equal(["2001", "2002"], field.Entries.Select(e => e.RefTag));
        Assert.All(field.Entries, e => Assert.Equal([-5.0], e.Values));
        // Опорная сетка: недостающий 2001 добавляется, 2002 уже есть у фрагмента.
        Assert.Equal(["2001"], plan.RefElements.Select(r => r.Tag));
    }

    [Fact]
    public void SelfWeight_WholeLifted_PartialLost()
    {
        var f = new Fixture();
        f.ElementLoads.Add(Load(FemElementLoadKinds.SelfWeight, ["2001", "2002", "2003", "2004"], 1.1));
        var whole = f.Plan("P1");
        var lift = Assert.Single(whole.AddedElementLoads);
        Assert.Equal((FemLoadTargetKinds.Members, FemElementLoadKinds.SelfWeight), (lift.TargetKind, lift.LoadKind));
        Assert.Equal([1.1], lift.Values);

        var g = new Fixture();
        g.ElementLoads.Add(Load(FemElementLoadKinds.SelfWeight, ["2001"], 1.1));
        var partial = g.Plan("P1");
        Assert.Empty(partial.AddedElementLoads);
        Assert.Contains(partial.Diagnostics, d => d.Code == "fragment_mesh_unlock_load_lost" && d.SourceKeys!.Contains("2001"));
    }

    [Fact]
    public void ShellPointLoad_NodeLoadAtPoint()
    {
        var f = new Fixture();
        // Сосредоточенная в местных координатах КЭ 2004 (0,5; 0,5) от его первого узла (1,1).
        f.ElementLoads.Add(Load(FemElementLoadKinds.Point, ["2004"], -10, 0.5, 0.5));

        var plan = f.Plan("P1");

        var (tag, load) = Assert.Single(plan.NodeLoads);
        var node = plan.NewNodes.Single(n => n.NodeTag == tag);
        Assert.Equal((1.5, 1.5), (node.X, node.Y));
        Assert.Equal((-10.0, 5), (load.Fz, load.LoadCaseId));
    }

    [Fact]
    public void BarElementLoad_MemberLoadOnSameSpan()
    {
        var f = new Fixture();
        f.ElementLoads.Add(Load(FemElementLoadKinds.Uniform, ["3002"], -2));

        var plan = f.Plan("B1");

        var (tag, load) = Assert.Single(plan.MemberLoads);
        Assert.Equal("B1", tag);
        Assert.Equal(("global", "uniform", 1.0, 0.0, -2.0, -2.0), (load.CoordinateSystem, load.DistributionType,
            load.StartOffsetM, load.EndOffsetM, load.QzStart, load.QzEnd), new ToleranceComparer());
        Assert.Empty(plan.AddedElementLoads);
    }

    [Fact]
    public void ShellWithoutRegion_NotUnlocked()
    {
        var f = new Fixture(slabRegion: false);
        var plan = f.Plan("P1");

        Assert.True(plan.IsEmpty);
        Assert.Contains(plan.Diagnostics, d => d.Code == "fragment_mesh_unlock_no_region");
        Assert.Equal(f.Elements.Count, plan.MeshElements.Count);
    }

    sealed class ToleranceComparer : IEqualityComparer<(string, string, double, double, double, double)>
    {
        public bool Equals((string, string, double, double, double, double) a, (string, string, double, double, double, double) b) =>
            a.Item1 == b.Item1 && a.Item2 == b.Item2 && Math.Abs(a.Item3 - b.Item3) < 1e-9 && Math.Abs(a.Item4 - b.Item4) < 1e-9
            && Math.Abs(a.Item5 - b.Item5) < 1e-9 && Math.Abs(a.Item6 - b.Item6) < 1e-9;

        public int GetHashCode((string, string, double, double, double, double) obj) => 0;
    }
}
