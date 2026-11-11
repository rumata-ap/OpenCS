using CScore;
using CScore.Fem;
using CScore.Planar;
using Xunit;

namespace CScore.Tests.Fem;

/// <summary>Следы АЖТ у стыка с сохранённой сеткой фрагмента: обрезка в <c>inset</c> от кромки стыка.</summary>
public sealed class FemRigidJunctionSeamsTests
{
    /// <summary>Область 4×3 в плоскости z = 0; сохранённая сетка — узлы на кромке y = 3 (x = 0, 1, …, 4).</summary>
    static PlanarRegion Region() => PlanarRegion.CreateFromContour(new Contour { X = [0, 4, 4, 0], Y = [0, 0, 3, 3] },
        frame: new Frame3D(new PlanarVector3(0, 0, 0), new(1, 0, 0), new(0, 1, 0), new(0, 0, 1)), tag: "W");

    static List<PlanarVector3> SeamY3() => Enumerable.Range(0, 5).Select(i => new PlanarVector3(i, 3, 0)).ToList();

    static RigidJunctionTrace Trace(string id, params (double U, double V)[] pts) => new(id, RigidJunctionCase.Rib, null, null, [id],
        [pts.Select(p => new PlanarPoint2D(p.U, p.V)).ToList()], (PlanarDofMask)63, false);

    [Fact]
    public void StripToSeam_ClippedAtInset_OtherTracesUntouched()
    {
        // Полоса 0,1 м вдоль колонны x = 2 до кромки y = 3; квадрат вдали от стыка.
        var strip = Trace("C1", (1.95, 0), (2.05, 0), (2.05, 3), (1.95, 3));
        var far = Trace("C2", (0.5, 0.5), (0.6, 0.5), (0.6, 0.6), (0.5, 0.6));
        var set = new RigidJunctionTraceSet([strip, far], []);

        var result = FemRigidJunctionSeams.AwayFromKeptSeams(set, Region(), SeamY3(), inset: 0.25);

        var piece = result.Traces.Single(t => t.Id == "C1").Pieces.Single();
        Assert.Equal(2.75, piece.Max(p => p.V), 9);
        Assert.Equal(0, piece.Min(p => p.V), 9);
        Assert.Same(far, result.Traces.Single(t => t.Id == "C2"));
        Assert.Contains(result.Diagnostics, d => d.Code == "rigid_junction_trace_kept_seam" && !d.IsError);
    }

    [Fact]
    public void TraceAlongSeam_Dropped()
    {
        var rib = Trace("R1", (0.5, 2.95), (3.5, 2.95), (3.5, 3), (0.5, 3));
        var result = FemRigidJunctionSeams.AwayFromKeptSeams(new RigidJunctionTraceSet([rib], []), Region(), SeamY3(), inset: 0.25);

        Assert.Empty(result.Traces);
        Assert.Contains(result.Diagnostics, d => d.Code == "rigid_junction_trace_kept_seam_empty");
    }

    [Fact]
    public void NoKeptNodesOnEdges_SetUnchanged()
    {
        var set = new RigidJunctionTraceSet([Trace("C1", (1.95, 0), (2.05, 0), (2.05, 3), (1.95, 3))], []);
        // Узлы вне плоскости и одиночный узел на кромке — стыка нет.
        var kept = new List<PlanarVector3> { new(2, 3, 0.5), new(1, 3, 0.5), new(2, 0, 0) };
        Assert.Same(set, FemRigidJunctionSeams.AwayFromKeptSeams(set, Region(), kept, inset: 0.25));
    }
}
