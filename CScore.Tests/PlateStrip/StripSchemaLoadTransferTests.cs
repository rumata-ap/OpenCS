using CScore.Fem;
using CScore.Fem.Loads;
using CScore.Planar;
using CScore.PlateStrip;
using Xunit;

namespace CScore.Tests.PlateStrip;

/// <summary>Перенос нагрузок схемы на полосу плиты: сетка 4 × 2 КЭ по 1 × 1 м в плоскости XY, полоса шириной 1 м
/// вдоль X на середине (y = 0,5 … 1,5), то есть через половину КЭ обоих рядов.</summary>
public sealed class StripSchemaLoadTransferTests
{
    const string Member = "P1";

    sealed class UnitWeights(double gamma) : IFemSelfWeightSource
    {
        public double? UnitWeight(FemElement element) => gamma;
        public double? BarArea(FemElement element) => null;
    }

    static FemLoadMeshContext Mesh(IFemSelfWeightSource? weight = null)
    {
        var nodes = new List<FemMeshNode>();
        for (int j = 0; j <= 2; j++)
            for (int i = 0; i <= 4; i++)
                nodes.Add(new FemMeshNode { NodeTag = (j * 5 + i + 1).ToString(), X = i, Y = j, Z = 0 });

        var elements = new List<FemElement>();
        for (int j = 0; j < 2; j++)
            for (int i = 0; i < 4; i++)
            {
                int a = j * 5 + i + 1, b = a + 1, c = a + 5, d = c + 1;
                elements.Add(new FemElement
                {
                    ElemTag = (j * 4 + i + 1).ToString(), ElemType = "shell", ThicknessM = 0.2,
                    NodeIdsJson = System.Text.Json.JsonSerializer.Serialize(new[] { a, b, c, d }),
                    SourceMemberTag = Member
                });
            }
        return new FemLoadMeshContext(nodes, elements, null, weight);
    }

    static PlateStripBeamAnalogy Strip(double zSign = 1.0)
    {
        var frame = zSign > 0
            ? new Frame3D(new PlanarVector3(0, 1, 0), new PlanarVector3(1, 0, 0), new PlanarVector3(0, 1, 0), new PlanarVector3(0, 0, 1))
            : new Frame3D(new PlanarVector3(0, 1, 0), new PlanarVector3(1, 0, 0), new PlanarVector3(0, -1, 0), new PlanarVector3(0, 0, -1));
        return new PlateStripBeamAnalogy
        {
            StripFrame = frame,
            ExplicitWidthM = 1.0,
            Geometry = new PlateStripGeometry { LengthM = 4.0 }
        };
    }

    static FemElementLoad Uniform(double pa, params string[] elements)
    {
        var load = new FemElementLoad { LoadCaseId = 1, LoadKind = FemElementLoadKinds.Uniform, Axis = "z" };
        load.SetTargetTags(elements);
        load.SetValues([pa]);
        return load;
    }

    static StripSchemaLoadTransferResult Run(
        FemLoadMeshContext mesh, PlateStripBeamAnalogy strip, IReadOnlyList<FemElementLoad> loads,
        IReadOnlyList<FemMeshNodeLoad>? nodeLoads = null, double? selfWeight = null, int intervals = 8,
        string member = Member) =>
        StripSchemaLoadTransfer.Transfer(
            strip, [(new FemLoadCase { Id = 1, Tag = "q", SelfWeightFactor = selfWeight }, 1.0)],
            loads, nodeLoads ?? [], mesh, [member], intervals);

    [Fact]
    public void Uniform_OnWholeRegion_GivesOneRowQTimesWidth()
    {
        var mesh = Mesh();
        var result = Run(mesh, Strip(), [Uniform(-10_000, "1", "2", "3", "4", "5", "6", "7", "8")]);

        var row = Assert.Single(result.Loads);
        Assert.Equal(StripNonlinearLoadKind.UniformQz, row.Kind);
        Assert.Equal(0.0, row.FromFraction, 12);
        Assert.Equal(1.0, row.ToFraction, 12);
        Assert.Equal(-10.0, row.Value, 9); // −10 кН/м² × 1 м
        Assert.Equal(-40.0, result.TransferredKn, 9);
        Assert.Equal(-80.0, result.RegionKn, 9); // вся нагрузка региона: 8 м² × 10 кПа
    }

    [Fact]
    public void Uniform_OnLeftHalf_GivesRowOnFirstHalfOfSpan()
    {
        var mesh = Mesh();
        // КЭ 1, 2 (нижний ряд, x < 2) и 5, 6 (верхний ряд, x < 2).
        var result = Run(mesh, Strip(), [Uniform(-20_000, "1", "2", "5", "6")]);

        var row = Assert.Single(result.Loads);
        Assert.Equal(0.0, row.FromFraction, 12);
        Assert.Equal(0.5, row.ToFraction, 12);
        Assert.Equal(-20.0, row.Value, 9);
    }

    [Fact]
    public void Uniform_StripFrameLooksDown_FlipsSign()
    {
        var mesh = Mesh();
        var result = Run(mesh, Strip(zSign: -1.0), [Uniform(-10_000, "1", "2", "3", "4", "5", "6", "7", "8")]);

        // Нормаль полосы направлена вниз, нагрузка вниз — по +Z полосы.
        Assert.Equal(10.0, Assert.Single(result.Loads).Value, 9);
    }

    [Fact]
    public void SelfWeight_UsesUnitWeightAndThickness()
    {
        var mesh = Mesh(new UnitWeights(25_000));
        var result = Run(mesh, Strip(), [], selfWeight: 1.0);

        // γ·h = 25 · 0,2 = 5 кПа, ширина 1 м → −5 кН/м.
        Assert.Equal(-5.0, Assert.Single(result.Loads).Value, 9);
    }

    [Fact]
    public void MeshNodeLoad_InsideStrip_BecomesPointForce()
    {
        var mesh = Mesh();
        // Узел 8 = (i = 2, j = 1): на оси полосы, посередине пролёта; узел 3 = (2, 0) — вне полосы.
        var inside = new FemMeshNodeLoad { LoadCaseId = 1, MeshNodeTag = "8", Fz = -20_000 };
        var outside = new FemMeshNodeLoad { LoadCaseId = 1, MeshNodeTag = "3", Fz = -50_000 };
        var result = Run(mesh, Strip(), [], [inside, outside]);

        var row = Assert.Single(result.Loads);
        Assert.Equal(StripNonlinearLoadKind.PointPz, row.Kind);
        Assert.Equal(0.5, row.FromFraction, 12);
        Assert.Equal(-20.0, row.Value, 9);
    }

    [Fact]
    public void ElementsOfOtherMember_AreNotTransferred()
    {
        var mesh = Mesh();
        var result = Run(mesh, Strip(), [Uniform(-10_000, "1", "2")], member: "OTHER");

        Assert.Empty(result.Loads);
        Assert.Contains(result.Diagnostics, d => d.Code == "strip_load_no_region_mesh");
    }

    [Fact]
    public void InPlaneLoad_IsReportedAndNotTransferred()
    {
        var mesh = Mesh();
        var load = new FemElementLoad { LoadCaseId = 1, LoadKind = FemElementLoadKinds.Uniform, Axis = "x" };
        load.SetTargetTags(["1", "2", "3", "4", "5", "6", "7", "8"]);
        load.SetValues([10_000]);
        var result = Run(mesh, Strip(), [load]);

        Assert.Empty(result.Loads);
        Assert.Contains(result.Diagnostics, d => d.Code == "strip_load_in_plane");
    }
}
