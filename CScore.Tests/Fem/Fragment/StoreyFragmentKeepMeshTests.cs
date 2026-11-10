using Xunit;
using System.Globalization;
using CScore.Fem;
using CScore.Fem.Fragment;
using static CScore.Tests.Fem.Fragment.StoreyFragmentFixtures;
using static CScore.Tests.Fem.Fragment.StoreyFragmentLoadFixtures;

namespace CScore.Tests.Fem.Fragment;

/// <summary>
/// Сохранение сетки родителя у необрезанных КонЭ (спека 2026-11-10-fragment-keep-parent-mesh, срез 2): плита этажа,
/// колонна и стена целиком в полосе — КЭ и теги родителя; обрезанные — без сетки; теги узлов схемы не пересекаются с
/// тегами сетки; нагрузки и ГУ сохранённой сетки — по тегам.
/// </summary>
public class StoreyFragmentKeepMeshTests
{
    const double Z1 = 1.5, Z2 = 4.5;

    /// <summary>Связная сетка родителя: узел в точке — один на все КЭ; теги узлов с 1 (пересекаются с тегами схемы).</summary>
    sealed class Mesh
    {
        readonly Dictionary<(long, long, long), FemMeshNode> _byKey = [];
        public List<FemMeshNode> Nodes { get; } = [];
        public List<FemElement> Elements { get; } = [];
        int _nextElement = 1;

        public string Node(double x, double y, double z)
        {
            var key = ((long)Math.Round(x * 1e6), (long)Math.Round(y * 1e6), (long)Math.Round(z * 1e6));
            if (_byKey.TryGetValue(key, out var n)) return n.NodeTag;
            n = new FemMeshNode { NodeTag = (Nodes.Count + 1).ToString(CultureInfo.InvariantCulture), X = x, Y = y, Z = z };
            Nodes.Add(n);
            _byKey[key] = n;
            return n.NodeTag;
        }

        string Element(string type, string member, params string[] nodes)
        {
            string tag = (_nextElement++).ToString(CultureInfo.InvariantCulture);
            Elements.Add(new FemElement
            {
                ElemTag = tag, ElemType = type, SourceMemberTag = member, ThicknessM = type == "shell" ? 0.2 : null,
                NodeIdsJson = "[" + string.Join(",", nodes) + "]",
            });
            return tag;
        }

        /// <summary>Q4 в хранении «1 2 4 3» по точке (u, v) → общие оси.</summary>
        public List<string> Grid(string member, Func<double, double, (double X, double Y, double Z)> at, double[] us, double[] vs)
        {
            var tags = new List<string>();
            string N(int i, int j) { var p = at(us[i], vs[j]); return Node(p.X, p.Y, p.Z); }
            for (int j = 0; j + 1 < vs.Length; j++)
                for (int i = 0; i + 1 < us.Length; i++)
                    tags.Add(Element("shell", member, N(i, j), N(i + 1, j), N(i, j + 1), N(i + 1, j + 1)));
            return tags;
        }

        public List<string> Bar(string member, double x, double y, double[] zs) =>
            Enumerable.Range(0, zs.Length - 1).Select(k => Element("beam", member, Node(x, y, zs[k]), Node(x, y, zs[k + 1]))).ToList();
    }

    static readonly double[] Steps6 = [-3, -1.5, 0, 1.5, 3];

    /// <summary>
    /// Родитель: плита S1 6×6 на z = 3 (Q4 1,5 м); колонна C1 (0, 0) z 0…6 — обрезается полосой; колонна C2 (1,5; 1,5)
    /// z 2…4 — целиком в полосе, делится плитой по узлу сетки; стена W1 по y = −3 на z 0…6 — обрезается; стена W2 по
    /// y = 3 на z 2…4 — целиком, делится плитой по ряду узлов.
    /// </summary>
    static (StoreyFragmentParentSnapshot Parent, Mesh Mesh) Floor(
        FemElementLoad[]? elementLoads = null, FemMeshNodeLoad[]? meshNodeLoads = null,
        FemMeshNodeSupport[]? supports = null, FemSpring[]? springs = null, Func<Mesh, FemRigidBody[]>? bodies = null)
    {
        var mesh = new Mesh();
        mesh.Grid("S1", (u, v) => (u, v, 3), Steps6, Steps6);
        mesh.Bar("C1", 0, 0, [0, 1.5, 3, 4.5, 6]);
        mesh.Bar("C2", 1.5, 1.5, [2, 3, 4]);
        mesh.Grid("W1", (u, v) => (-3 + u, -3, v), [0, 1.5, 3, 4.5, 6], [0, 1.5, 3, 4.5, 6]);
        mesh.Grid("W2", (u, v) => (-3 + u, 3, 2 + v), [0, 1.5, 3, 4.5, 6], [0, 1, 2]);

        var (slab, slabRegion) = Slab("S1", 1, 3);
        var w1 = Wall("W1", 2, -3, -3, 0, 6, 6);
        var w2 = Wall("W2", 3, -3, 3, 2, 6, 2);
        var c1 = Bar("C1", "1", "2");
        var c2 = Bar("C2", "3", "4");
        c1.Id = 1;
        c2.Id = 2;
        var parent = new StoreyFragmentParentSnapshot
        {
            Nodes = [N("1", 0, 0, 0), N("2", 0, 0, 6), N("3", 1.5, 1.5, 2), N("4", 1.5, 1.5, 4)],
            Members = [c1, c2, slab, w1.Member, w2.Member],
            Regions = [slabRegion!, w1.Region, w2.Region],
            MeshNodes = mesh.Nodes,
            MeshElements = mesh.Elements,
            LoadCases = [Lc(7, "Полезная")],
            ElementLoads = elementLoads ?? [],
            MeshNodeLoads = meshNodeLoads ?? [],
            MeshNodeSupports = supports ?? [],
            Springs = springs ?? [],
            RigidBodies = bodies?.Invoke(mesh) ?? [],
        };
        return (parent, mesh);
    }

    static StoreyFragmentDraft Build(StoreyFragmentParentSnapshot parent, bool keep = true, string[]? rebuild = null) =>
        StoreyFragmentBuilder.Build(parent, new StoreyFragmentRequest(Z1, Z2, "Этаж 2", KeepParentMesh: keep, MeshRebuildMemberTags: rebuild));

    static StoreyFragmentMember Part(StoreyFragmentDraft d, string tag) => d.Members.Single(m => m.Member.ElemTag == tag);

    static List<string> ParentElements(Mesh mesh, string member) =>
        mesh.Elements.Where(e => e.SourceMemberTag == member).Select(e => e.ElemTag).ToList();

    [Fact]
    public void KeepOff_AsBefore()
    {
        var d = Build(Floor().Parent, keep: false);

        Assert.False(d.HasErrors, string.Join("\n", d.Diagnostics));
        Assert.False(d.HasKeptMesh);
        Assert.Empty(d.KeptMeshNodes);
        Assert.All(d.Members, m => Assert.False(m.Member.IsMeshLocked));
    }

    [Fact]
    public void Slab_KeptWithParentTags()
    {
        var (parent, mesh) = Floor();
        var d = Build(parent);

        Assert.False(d.HasErrors, string.Join("\n", d.Diagnostics));
        var slab = Part(d, "S1");
        Assert.Equal(FemMember.MeshSourceImported, slab.Member.MeshSource);
        Assert.Equal(ParentElements(mesh, "S1"), slab.KeptElements!.Select(e => e.ElemTag));
        Assert.All(slab.KeptElements!, e =>
        {
            Assert.Equal(FemMember.MeshSourceImported, e.Origin);
            Assert.Equal("S1", e.SourceMemberTag);
            Assert.Equal(0, e.Id);
        });
        var parentNodes = mesh.Nodes.ToDictionary(n => n.NodeTag);
        foreach (var n in d.KeptMeshNodes)
        {
            Assert.Equal(FemMember.MeshSourceImported, n.Origin);
            Assert.Equal(parentNodes[n.NodeTag].X, n.X);
            Assert.Equal(parentNodes[n.NodeTag].Z, n.Z);
        }
        // Узлы КЭ сохранённых КонЭ есть среди сохранённых узлов.
        var kept = d.KeptMeshNodes.Select(n => n.NodeTag).ToHashSet();
        Assert.All(d.Members.SelectMany(m => m.KeptElements ?? []), e =>
            Assert.All(FemMeshTopology.ReadNodeTags(e)!, t => Assert.Contains(t, kept)));
    }

    [Fact]
    public void ClippedWallAndColumn_NotKept()
    {
        var d = Build(Floor().Parent);

        Assert.All(d.Members.Where(m => m.ParentMemberTag is "W1" or "C1"), m =>
        {
            Assert.Null(m.KeptElements);
            Assert.False(m.Member.IsMeshLocked);
        });
    }

    [Fact]
    public void WholeColumnAndWall_SplitBySlab_KeptByPieces()
    {
        var (parent, mesh) = Floor();
        var d = Build(parent);

        var c2 = ParentElements(mesh, "C2");
        Assert.Equal([c2[0]], Part(d, "C2 · низ").KeptElements!.Select(e => e.ElemTag));
        Assert.Equal([c2[1]], Part(d, "C2 · верх").KeptElements!.Select(e => e.ElemTag));
        Assert.True(Part(d, "C2 · низ").Member.IsMeshLocked);

        var w2 = ParentElements(mesh, "W2");
        Assert.Equal(w2.Take(4), Part(d, "W2 · низ").KeptElements!.Select(e => e.ElemTag));
        Assert.Equal(w2.Skip(4), Part(d, "W2 · верх").KeptElements!.Select(e => e.ElemTag));
    }

    [Fact]
    public void MeshRebuildMemberTags_ColumnNotKept()
    {
        var d = Build(Floor().Parent, rebuild: ["C2"]);

        Assert.All(d.Members.Where(m => m.ParentMemberTag == "C2"), m =>
        {
            Assert.Null(m.KeptElements);
            Assert.False(m.Member.IsMeshLocked);
        });
        Assert.NotNull(Part(d, "S1").KeptElements);
    }

    [Fact]
    public void SchemaNodeTags_DoNotCollideWithKeptMesh()
    {
        var (parent, mesh) = Floor();
        var d = Build(parent);

        var meshByTag = mesh.Nodes.ToDictionary(n => n.NodeTag);
        foreach (var n in d.Nodes)
            if (meshByTag.TryGetValue(n.NodeTag, out var m))
                Assert.True(Math.Abs(m.X - n.X) < 1e-12 && Math.Abs(m.Y - n.Y) < 1e-12 && Math.Abs(m.Z - n.Z) < 1e-12,
                    $"Узел схемы {n.NodeTag} с тегом сетки стоит не в её точке.");
        // Концы сохранённой колонны — узлы сетки родителя (теги и связь SourceNodeTag).
        var station = mesh.Nodes.Single(n => n.X == 1.5 && n.Y == 1.5 && n.Z == 3);
        Assert.Contains(d.Nodes, n => n.NodeTag == station.NodeTag);
        Assert.Equal(station.NodeTag, d.KeptMeshNodes.Single(n => n.NodeTag == station.NodeTag).SourceNodeTag);
        // Обрезанная колонна C1: узлы на z₁/z₂ — новые, их теги не из сетки.
        Assert.All(d.Nodes.Where(n => n.X == 0 && n.Y == 0 && (n.Z == Z1 || n.Z == Z2)),
            n => Assert.DoesNotContain(n.NodeTag, meshByTag.Keys));
    }

    [Fact]
    public void KeptSlabLoads_CopiedByTag()
    {
        var (_, mesh0) = Floor();
        var slabTags = ParentElements(mesh0, "S1");
        var corner = mesh0.Nodes.Single(n => n.X == -1.5 && n.Y == -1.5 && n.Z == 3).NodeTag;
        var load = ElementLoad(7, FemElementLoadKinds.Uniform, "z", [-5000], [.. slabTags]);
        var (parent, _) = Floor(
            elementLoads: [load],
            meshNodeLoads: [new FemMeshNodeLoad { LoadCaseId = 7, MeshNodeTag = corner, Fz = -1000 }]);
        var d = Build(parent);

        var copy = Assert.Single(d.ElementLoads);
        Assert.Equal(FemLoadTargetKinds.Elements, copy.TargetKind);
        Assert.Equal(slabTags, copy.TargetTags);
        Assert.Equal([-5000.0], copy.Values);
        Assert.Empty(d.FieldLoads);
        var nodeLoad = Assert.Single(d.MeshNodeLoads);
        Assert.Equal(corner, nodeLoad.MeshNodeTag);
        Assert.Empty(d.NodeLoads);
        Assert.DoesNotContain(d.Diagnostics, x => x.Code == "fragment_load_resultant_mismatch");
    }

    [Fact]
    public void KeptNodeSupportAndSpring_CopiedByTag()
    {
        var (_, mesh0) = Floor();
        var a = mesh0.Nodes.Single(n => n.X == -1.5 && n.Y == 0 && n.Z == 3).NodeTag;
        var b = mesh0.Nodes.Single(n => n.X == 1.5 && n.Y == 0 && n.Z == 3).NodeTag;
        var spring = new FemSpring { TargetKind = FemSpringTargetKinds.MeshNode, NodeTag = b };
        spring.SetStiffnesses([0, 0, 1e6, 0, 0, 0]);
        var (parent, _) = Floor(supports: [new FemMeshNodeSupport { NodeTag = a, Mask = 0b111 }], springs: [spring]);
        int nodesBefore = Build(Floor().Parent).Nodes.Count;

        var d = Build(parent);

        var support = Assert.Single(d.MeshNodeSupports);
        Assert.Equal((a, 0b111), (support.NodeTag, support.Mask));
        var s = Assert.Single(d.Springs);
        Assert.Equal((FemSpringTargetKinds.MeshNode, b), (s.TargetKind, s.NodeTag));
        Assert.Equal(1e6, s.Kz);
        Assert.Equal(nodesBefore, d.Nodes.Count);
    }

    // ── Срез 3: АЖТ родителя ──────────────────────────────────────────────────────────────────────────────────────

    /// <summary>Тело у станции C2 на плите: ведущий — узел (1,5; 1,5; 3), ведомые — четыре соседних узла плиты.</summary>
    static FemRigidBody StationBody(Mesh mesh, string origin)
    {
        var body = new FemRigidBody
        {
            MasterNodeTag = mesh.Node(1.5, 1.5, 3), Mask = 0b110111, Kind = "beam", Origin = origin, SourceElemTag = "J1",
        };
        body.SetSlaveNodeTags([mesh.Node(0, 1.5, 3), mesh.Node(3, 1.5, 3), mesh.Node(1.5, 0, 3), mesh.Node(1.5, 3, 3)]);
        return body;
    }

    /// <summary>Тело на стыке: ведущий — узел плиты (−1,5; −1,5; 3), ведомые — узел плиты и узел обрезанной W1 на z₁.</summary>
    static FemRigidBody EdgeBody(Mesh mesh, string origin)
    {
        var body = new FemRigidBody { MasterNodeTag = mesh.Node(-1.5, -1.5, 3), Mask = 0b111111, Origin = origin, SourceElemTag = "J2" };
        body.SetSlaveNodeTags([mesh.Node(-1.5, 0, 3), mesh.Node(-1.5, -3, 1.5)]);
        return body;
    }

    [Fact]
    public void KeptBody_CopiedByTags()
    {
        FemRigidBody? source = null;
        var (parent, _) = Floor(bodies: m => [source = StationBody(m, "auto:junction")]);
        var d = Build(parent);

        Assert.False(d.HasErrors, string.Join("\n", d.Diagnostics));
        var copy = Assert.Single(d.MeshRigidBodies);
        Assert.Equal(source!.MasterNodeTag, copy.MasterNodeTag);
        Assert.Equal(source.SlaveNodeTags, copy.SlaveNodeTags);
        Assert.Equal((0b110111, "beam", "J1"), (copy.Mask, copy.Kind, copy.SourceElemTag));
        Assert.Equal("fragment:auto:junction", copy.Origin);
        Assert.Empty(d.NodeRigidBodies);
        Assert.Contains(d.Diagnostics, x => x.Code == "fragment_rigid_body_kept");
        Assert.DoesNotContain(d.Diagnostics, x => x.Code == "fragment_rigid_from_trace");
    }

    [Fact]
    public void KeptImportBody_NoColumnTrace()
    {
        var (parent, _) = Floor(bodies: m => [StationBody(m, "import:lira")]);
        var d = Build(parent);

        Assert.Equal("fragment:import:lira", Assert.Single(d.MeshRigidBodies).Origin);
        Assert.All(d.Members.Where(m => m.ParentMemberTag == "C2"), m => Assert.Null(m.Member.RigidJunctionTraceJson));
        Assert.Empty(d.NodeRigidBodies);
    }

    [Fact]
    public void PartlyKeptImportBody_ByPointsWithWarning()
    {
        string lostSlave = "";
        var (parent, _) = Floor(bodies: m =>
        {
            var b = EdgeBody(m, "import:lira");
            lostSlave = b.SlaveNodeTags[^1];
            return [b];
        });
        var d = Build(parent);

        Assert.False(d.HasErrors, string.Join("\n", d.Diagnostics));
        Assert.Empty(d.MeshRigidBodies);
        var byPoints = Assert.Single(d.NodeRigidBodies);
        Assert.Equal("import:lira", byPoints.Origin);
        Assert.Equal(2, byPoints.SlaveNodeTags.Count);
        var row = Assert.Single(d.Diagnostics, x => x.Code == "fragment_rigid_from_trace");
        Assert.Contains("J2", row.SourceKeys!);
        Assert.Contains(lostSlave, row.Message);
    }

    [Fact]
    public void PartlyKeptAutoBody_Warning()
    {
        var (parent, _) = Floor(bodies: m => [EdgeBody(m, "auto:junction")]);
        var d = Build(parent);

        Assert.Empty(d.MeshRigidBodies);
        Assert.Empty(d.NodeRigidBodies);
        Assert.Single(d.Diagnostics, x => x.Code == "fragment_rigid_from_trace");
    }

    [Fact]
    public void KeepOff_BodiesAsBefore()
    {
        var (parent, _) = Floor(bodies: m => [StationBody(m, "auto:junction"), EdgeBody(m, "fragment:auto:junction")]);
        var d = Build(parent, keep: false);

        Assert.Empty(d.MeshRigidBodies);
        Assert.Empty(d.NodeRigidBodies);
        Assert.DoesNotContain(d.Diagnostics, x => x.Code is "fragment_rigid_from_trace" or "fragment_rigid_body_kept" or "fragment_rigid_body_lost");
    }
}
