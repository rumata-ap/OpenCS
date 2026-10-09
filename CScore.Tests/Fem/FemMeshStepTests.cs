using CScore.Fem;
using CScore.Fem.Editing;
using CScore.Planar;
using Xunit;

namespace CScore.Tests.Fem;

/// <summary>Шаг сетки пластин: свой шаг КонЭ важнее общего шага схемы, общий — важнее размера из области.</summary>
public class FemMeshStepTests
{
    static PlanarRegion Region(int id, double size)
    {
        var r = PlanarRegion.CreateFromContour(new Contour { X = [0, 4, 4, 0], Y = [0, 0, 4, 4] });
        r.Id = id;
        r.MeshMaxElementSizeM = size;
        return r;
    }

    static FemMember Plate(int id, int regionId, double? local = null) =>
        new() { Id = id, ElemTag = $"P{id}", ElemType = "shell", PlanarRegionId = regionId, NodeIdsJson = "[]", TargetMeshLengthM = local };

    [Fact]
    public void MeshSize_LocalOverCommonOverRegion()
    {
        var region = Region(1, 0.7);
        Assert.Equal(0.7, FemPlanarMeshPlan.MeshSize(Plate(1, 1), region, null));
        Assert.Equal(0.4, FemPlanarMeshPlan.MeshSize(Plate(1, 1), region, 0.4));
        Assert.Equal(0.25, FemPlanarMeshPlan.MeshSize(Plate(1, 1, 0.25), region, 0.4));
    }

    [Fact]
    public void OrderedRegions_UseEffectiveSize()
    {
        // Без шагов первой идёт область 2 (0,3 < 0,8); общий шаг 0,5 уравнивает, свой 0,2 у первой ставит её вперёд.
        var regions = new[] { Region(1, 0.8), Region(2, 0.3) };
        Assert.Equal(["P2", "P1"], FemPlanarMeshPlan.OrderedRegions([Plate(1, 1), Plate(2, 2)], regions).Select(p => p.Member.ElemTag));
        Assert.Equal(["P1", "P2"], FemPlanarMeshPlan.OrderedRegions([Plate(1, 1), Plate(2, 2)], regions, 0.5).Select(p => p.Member.ElemTag));
        Assert.Equal(["P1", "P2"], FemPlanarMeshPlan.OrderedRegions([Plate(1, 1, 0.2), Plate(2, 2)], regions).Select(p => p.Member.ElemTag));
    }

    [Fact]
    public void SetMembersMeshStep_UndoRestoresEachStep()
    {
        var session = new FemSchemaEditSession(new FemSchema { Tag = "S" });
        var bar = new FemMember { ElemTag = "B", ElemType = "beam", TargetMeshLengthM = 0.5 };
        var plate = Plate(2, 2);
        session.Members.AddRange([bar, plate]);

        session.Execute(new SetMembersMeshStepCommand([bar, plate], 0.3));
        Assert.Equal(0.3, bar.TargetMeshLengthM);
        Assert.Equal(0.3, plate.TargetMeshLengthM);

        session.Undo();
        Assert.Equal(0.5, bar.TargetMeshLengthM);
        Assert.Null(plate.TargetMeshLengthM);
    }
}
