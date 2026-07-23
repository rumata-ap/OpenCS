using CScore;
using CScore.Fem;
using OpenCS.OpenSees.CScore;

namespace OpenCS.OpenSees.Tests;

public class FemLinearModelResolverTests
{
    static GeoProps Gp() => new()
    {
        A = 0.02, EA = 0.02 * 3e10,
        Ix = 2e-4, EIx = 2e-4 * 3e10,
        Iy = 5e-4, EIy = 5e-4 * 3e10
    };

    // Конструктивная консоль: узел 1 (заделка, dofMask=63) — узел 2 (свободен), 1 стержень.
    static (List<FemMeshNode>, List<FemElement>, List<FemNode>, List<FemMember>, List<FemNodeLoad>) Console()
    {
        var meshNodes = new List<FemMeshNode>
        {
            new() { Id = 10, NodeTag = "1", X = 0, Y = 0, Z = 0, SourceNodeTag = "1", SourceMemberTag = "1" },
            new() { Id = 11, NodeTag = "2", X = 3, Y = 0, Z = 0, SourceNodeTag = "2", SourceMemberTag = "1" },
        };
        var meshElems = new List<FemElement>
        {
            new() { Id = 20, ElemTag = "1", NodeIdsJson = "[1,2]", SourceMemberTag = "1",
                    CrossSectionId = 5, GjStrategy = "manual", GjManualValue = 1e6 },
        };
        var srcNodes = new List<FemNode>
        {
            new() { Id = 1, NodeTag = "1", X = 0, Y = 0, Z = 0, DofMask = 63 },
            new() { Id = 2, NodeTag = "2", X = 3, Y = 0, Z = 0, DofMask = 0 },
        };
        var srcMembers = new List<FemMember>
        {
            new() { Id = 1, ElemTag = "1", ElemType = "beam", NodeIdsJson = "[1,2]",
                    CrossSectionId = 5, GjStrategy = "manual", GjManualValue = 1e6 },
        };
        var loads = new List<FemNodeLoad>
        {
            new() { Id = 1, LoadCaseId = 1, NodeId = 2, Fz = -1000 },
        };
        return (meshNodes, meshElems, srcNodes, srcMembers, loads);
    }

    static Dictionary<int, GeoProps> Props() => new() { [5] = Gp() };

    [Fact]
    public void Resolve_ValidConsole_BuildsModel()
    {
        var (mn, me, sn, sm, ld) = Console();
        var r = new FemLinearModelResolver().Resolve(mn, me, sn, sm, ld, Props());

        Assert.True(r.Ok, string.Join("; ", r.Errors));
        Assert.Equal(2, r.Model!.Nodes.Count);
        Assert.Single(r.Model.Elements);

        var fixedNode = r.Model.Nodes.Single(n => n.Tag == 1);
        Assert.All(fixedNode.Fixed, f => Assert.True(f));           // dofMask=63 → все закреплены
        Assert.All(r.Model.Nodes.Single(n => n.Tag == 2).Fixed, f => Assert.False(f));

        var e = r.Model.Elements[0];
        Assert.Equal(0.02, e.A, 12);
        Assert.Equal(3e10, e.E, 3);
        Assert.Equal(5e-4, e.Iy, 12);
        Assert.Equal(2e-4, e.Iz, 12);
        Assert.Equal(1e6, e.G, 3);       // G=GjManual
        Assert.Equal(1.0, e.J, 12);      // J=1 → G·J=GJ
        Assert.Equal((0d, -1d, 0d), e.Vecxz);

        var load = Assert.Single(r.Model.Loads);
        Assert.Equal(2, load.NodeTag);   // перенесена на mesh-узел «2»
        Assert.Equal(-1000, load.Fz, 6);
    }

    [Fact]
    public void Resolve_MissingSection_ReportsError()
    {
        var (mn, me, sn, sm, ld) = Console();
        sm[0].CrossSectionId = null;
        var r = new FemLinearModelResolver().Resolve(mn, me, sn, sm, ld, Props());
        Assert.False(r.Ok);
        Assert.Contains(r.Errors, x => x.Contains("сечение", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Resolve_SectionNotReady_ReportsError()
    {
        var (mn, me, sn, sm, ld) = Console();
        var r = new FemLinearModelResolver().Resolve(mn, me, sn, sm, ld,
            new Dictionary<int, GeoProps>());   // нет props для id=5
        Assert.False(r.Ok);
        Assert.Contains(r.Errors, x => x.Contains("готов", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Resolve_ManualGjMissingValue_ReportsError()
    {
        var (mn, me, sn, sm, ld) = Console();
        sm[0].GjManualValue = null;
        var r = new FemLinearModelResolver().Resolve(mn, me, sn, sm, ld, Props());
        Assert.False(r.Ok);
        Assert.Contains(r.Errors, x => x.Contains("GJ", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Resolve_SaintVenantGj_ReportsDeferredError()
    {
        var (mn, me, sn, sm, ld) = Console();
        sm[0].GjStrategy = "saint_venant";
        var r = new FemLinearModelResolver().Resolve(mn, me, sn, sm, ld, Props());
        Assert.False(r.Ok);
        Assert.Contains(r.Errors, x => x.Contains("отложен", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Resolve_LoadedNodeNotInMesh_ReportsError()
    {
        var (mn, me, sn, sm, ld) = Console();
        ld[0].NodeId = 999;  // нет такого конструктивного узла
        var r = new FemLinearModelResolver().Resolve(mn, me, sn, sm, ld, Props());
        Assert.False(r.Ok);
    }

    [Fact]
    public void Resolve_TransfersMemberDistributedLoadToModel()
    {
        var (mn, me, sn, sm, ld) = Console();
        var memberLoad = new FemMemberLoad
        {
            LoadCaseId = 1, MemberId = 1, CoordinateSystem = "local",
            DistributionType = "trapezoidal", StartOffsetM = 0.5, EndOffsetM = 0.5,
            QyStart = -100, QyEnd = -300
        };

        var r = new FemLinearModelResolver().Resolve(mn, me, sn, sm, ld, Props(), [memberLoad]);

        Assert.True(r.Ok, string.Join("; ", r.Errors));
        var load = Assert.Single(r.Model!.DistributedLoads);
        Assert.Equal(-100, load.WyStart, 8);
        Assert.Equal(-300, load.WyEnd, 8);
        Assert.Equal(1d / 6, load.AOverL, 8);
        Assert.Equal(5d / 6, load.BOverL, 8);
    }
}
