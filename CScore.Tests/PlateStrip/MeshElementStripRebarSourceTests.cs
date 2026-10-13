using CScore.Fem;
using CScore.Planar;
using CScore.PlateRebar;
using CScore.PlateStrip;
using Xunit;

namespace CScore.Tests.PlateStrip;

/// <summary>Мост импорта → полоса, шаг (а): армирование вдоль полосы по КЭ сетки региона.</summary>
public sealed class MeshElementStripRebarSourceTests
{
    // Две ячейки 1×1 м рядом: «лево» x ∈ [0,1], «право» x ∈ [1,2]; y ∈ [0,1].
    static List<FemMeshNode> Nodes() =>
    [
        new() { NodeTag = "1", X = 0, Y = 0 }, new() { NodeTag = "2", X = 1, Y = 0 }, new() { NodeTag = "3", X = 2, Y = 0 },
        new() { NodeTag = "4", X = 0, Y = 1 }, new() { NodeTag = "5", X = 1, Y = 1 }, new() { NodeTag = "6", X = 2, Y = 1 },
    ];

    /// <summary>Q4 хранится «1 2 4 3» (как в ЛИРЕ/SCAD): [A, B, D, C] для обхода A-B-C-D против часовой.</summary>
    static FemCheckScopeElement Quad(string tag, int a, int b, int c, int d, double? axisDeg = null) =>
        new(int.Parse(tag), new FemElement
        {
            ElemTag = tag, ElemType = "shell", NodeIdsJson = $"[{a},{b},{d},{c}]", LocalAxisAngleDeg = axisDeg,
            ThicknessM = 0.2
        }, null);

    static PlateSection Section(double asx) => new()
    {
        H = 0.2,
        RebarLayers = [new() { InputMode = "direct", Asx = asx, Zsx = -0.08, Face = RebarFace.MinusN }]
    };

    /// <summary>Сечение по номеру КЭ: Asx = 0,001·номер.</summary>
    sealed class ByNumberSource(double force = 0) : IPlateElementSectionSource
    {
        public string Key => FemCheckRebarSource.Assigned;

        public PlateElementSection Resolve(FemCheckScopeElement element) => element.ElemNum == 99
            ? PlateElementSection.Missing("нет в группах")
            : new(Section(0.001 * element.ElemNum!.Value), "test", "k" + element.ElemNum, null) { ForceAngleDeg = force };
    }

    static MeshElementStripRebarSource Make(IPlateElementSectionSource? source = null,
        double? leftAxis = null, bool mirroredRight = false)
    {
        var left = Quad("1", 1, 2, 5, 4, leftAxis);
        // Зеркальный КЭ: обход по часовой — нормаль против нормали региона.
        var right = mirroredRight
            ? new FemCheckScopeElement(2, new FemElement
            {
                ElemTag = "2", ElemType = "shell", NodeIdsJson = "[2,5,3,6]", ThicknessM = 0.2
            }, null)
            : Quad("2", 2, 3, 6, 5);
        return MeshElementStripRebarSource.Create(Frame3D.Identity, [left, right], Nodes(), source ?? new ByNumberSource());
    }

    [Fact]
    public void Point_FallsIntoItsElement()
    {
        var source = Make();

        var left = source.Resolve(0.4, 0.5);
        var right = source.Resolve(1.6, 0.5);

        Assert.Empty(left.Diagnostics);
        Assert.Equal(0.001, Assert.Single(left.Layers).Asx, 12);
        Assert.Equal(0.002, Assert.Single(right.Layers).Asx, 12);
        Assert.Equal(0.2, left.ThicknessM, 12);
        Assert.Equal(2, source.ElementCount);
        Assert.Equal(0, source.SkippedElements);
    }

    [Fact]
    public void PointOutsideMesh_IsError_ButSnapsWithinTolerance()
    {
        var source = Make();

        var far = source.Resolve(0.5, 1.5);
        var snapped = source.Resolve(2.0 + 1e-7, 0.5);

        Assert.Contains(far.Diagnostics, d => d.Code == "plate_strip_point_outside_mesh" && d.IsError);
        Assert.Empty(snapped.Diagnostics);
        Assert.Equal(0.002, Assert.Single(snapped.Layers).Asx, 12);
    }

    [Fact]
    public void ElementWithoutRebar_ReportsSourceReason()
    {
        var elements = new[] { Quad("99", 1, 2, 5, 4) };
        var source = MeshElementStripRebarSource.Create(Frame3D.Identity, elements, Nodes(), new ByNumberSource());

        var sample = source.Resolve(0.5, 0.5);

        var d = Assert.Single(sample.Diagnostics);
        Assert.Equal("plate_strip_mesh_rebar_missing", d.Code);
        Assert.True(d.IsError);
        Assert.Contains("нет в группах", d.Message);
        Assert.Empty(sample.Layers);
    }

    [Fact]
    public void OutputAxis_IsLocalAxisAngleInRegionPlane()
    {
        // Узел 1 → узел 2 вдоль +X региона; угол согласования 90° → ось армирования вдоль +Y.
        var source = Make(leftAxis: 90);

        Assert.Equal(90.0, source.Resolve(0.5, 0.5).RebarAxisDeg, 6);
        Assert.Equal(0.0, source.Resolve(1.5, 0.5).RebarAxisDeg, 6);
    }

    [Fact]
    public void ForceAngle_IsSubtractedFromOutputAxis()
    {
        // Усилия повёрнуты на 30° в оси армирования: ось армирования — ось выдачи минус 30°.
        var elements = new[] { Quad("1", 1, 2, 5, 4, axisDeg: 50) };
        var source = MeshElementStripRebarSource.Create(Frame3D.Identity, elements, Nodes(), new ByNumberSource(30));

        Assert.Equal(20.0, source.Resolve(0.5, 0.5).RebarAxisDeg, 6);
    }

    [Fact]
    public void MirroredElement_LayersAreFlippedIntoRegionFaces()
    {
        var source = Make(mirroredRight: true);

        var layer = Assert.Single(source.Resolve(1.5, 0.5).Layers);

        // Слой был у грани «минус» КЭ; у зеркального КЭ она — грань «плюс» региона.
        Assert.Equal(+0.08, layer.Zsx, 12);
        Assert.Equal(RebarFace.PlusN, layer.Face);
        Assert.Equal(-0.08, Assert.Single(source.Resolve(0.5, 0.5).Layers).Zsx, 12);
    }

    [Fact]
    public void ThicknessChange_AlongStrip_IsWarnedOnce()
    {
        var thick = Quad("2", 2, 3, 6, 5);
        thick.Element.ThicknessM = 0.3;
        var section = new Func<FemCheckScopeElement, PlateElementSection>(e =>
        {
            var s = Section(0.001);
            s.H = e.Element.ThicknessM!.Value;
            return new(s, "t", "k" + e.ElemNum, null);
        });
        var source = MeshElementStripRebarSource.Create(
            Frame3D.Identity, [Quad("1", 1, 2, 5, 4), thick], Nodes(), new Delegating(section));

        source.Resolve(0.5, 0.5);
        var second = source.Resolve(1.5, 0.5);
        var third = source.Resolve(1.6, 0.5);

        Assert.Contains(second.Diagnostics, d => d.Code == "plate_strip_mesh_thickness_varies" && !d.IsError);
        Assert.DoesNotContain(third.Diagnostics, d => d.Code == "plate_strip_mesh_thickness_varies");
    }

    sealed class Delegating(Func<FemCheckScopeElement, PlateElementSection> resolve) : IPlateElementSectionSource
    {
        public string Key => FemCheckRebarSource.Section;
        public PlateElementSection Resolve(FemCheckScopeElement element) => resolve(element);
    }

    [Fact]
    public void StripAlongMesh_TakesRebarElementByElement()
    {
        var analogy = new PlateStripBeamAnalogy
        {
            Id = "s", SourceRegionId = 1, ExplicitWidthM = 0.5, Fingerprint = "x",
            Geometry = new PlateStripGeometry
            {
                CenterLine = [new PlanarPoint2D(0, 0.5), new PlanarPoint2D(2, 0.5)],
                LeftBoundary = [new PlanarPoint2D(0, 0.75), new PlanarPoint2D(2, 0.75)],
                RightBoundary = [new PlanarPoint2D(0, 0.25), new PlanarPoint2D(2, 0.25)],
                Polygon = [new(0, 0.25), new(2, 0.25), new(2, 0.75), new(0, 0.75)],
                LengthM = 2
            }
        };

        var layouts = PlateStripFiberLayouts.Resolve(Make(), analogy, [0.0, 0.25, 0.6, 1.0], widthPointCount: 2);

        Assert.True(layouts.IsCalculable, string.Join("; ", layouts.Diagnostics.Select(d => d.Message)));
        double[] asx = layouts.PerElement.Select(e => e[0].Sum(l => l.Asx)).ToArray();
        Assert.Equal([0.001, 0.001, 0.002], asx);
    }

    [Fact]
    public void StripAcrossRebarAxis_IsBlockedByAxisGuard()
    {
        // Ось армирования КЭ — вдоль +Y (угол 90°), а полоса идёт вдоль U региона.
        var elements = new[] { Quad("1", 1, 2, 5, 4, axisDeg: 90) };
        var source = MeshElementStripRebarSource.Create(Frame3D.Identity, elements, Nodes(), new ByNumberSource());
        var analogy = new PlateStripBeamAnalogy
        {
            Id = "s", SourceRegionId = 1, ExplicitWidthM = 0.5, Fingerprint = "x",
            Geometry = new PlateStripGeometry
            {
                CenterLine = [new PlanarPoint2D(0.1, 0.5), new PlanarPoint2D(0.9, 0.5)],
                LeftBoundary = [new PlanarPoint2D(0.1, 0.75), new PlanarPoint2D(0.9, 0.75)],
                RightBoundary = [new PlanarPoint2D(0.1, 0.25), new PlanarPoint2D(0.9, 0.25)],
                Polygon = [new(0.1, 0.25), new(0.9, 0.25), new(0.9, 0.75), new(0.1, 0.75)],
                LengthM = 0.8
            }
        };

        var layouts = PlateStripFiberLayouts.Resolve(source, analogy, [0.0, 0.5, 1.0], widthPointCount: 1);

        Assert.False(layouts.IsCalculable);
        Assert.Contains(layouts.Diagnostics, d => d.Code == "plate_strip_rebar_axis_mismatch");
    }

    [Fact]
    public void StripAcrossHole_PointsInHoleTakeRebarFromMaterialEdge()
    {
        // Сетка 3×3 по 1 м без центральной ячейки (отверстие 1…2 × 1…2): кольцо из 8 КЭ.
        FemMeshNode N(int i, int j) => new() { NodeTag = (j * 4 + i + 1).ToString(), X = i, Y = j, Z = 0 };
        var nodes = new List<FemMeshNode>();
        for (int j = 0; j <= 3; j++)
            for (int i = 0; i <= 3; i++) nodes.Add(N(i, j));
        var elements = new List<FemCheckScopeElement>();
        int num = 1;
        for (int j = 0; j < 3; j++)
            for (int i = 0; i < 3; i++)
            {
                if (i == 1 && j == 1) continue;
                int n1 = j * 4 + i + 1, n2 = n1 + 1, n3 = n1 + 5, n4 = n1 + 4;
                elements.Add(new FemCheckScopeElement(num, new FemElement
                {
                    ElemTag = num.ToString(), ElemType = "shell", NodeIdsJson = $"[{n1},{n2},{n4},{n3}]", ThicknessM = 0.2
                }, null));
                num++;
            }
        var source = MeshElementStripRebarSource.Create(Frame3D.Identity, elements, nodes, new ByNumberSource());

        var region = PlanarRegion.CreateFromContour(
            new Contour { X = [0, 3, 3, 0], Y = [0, 0, 3, 3] },
            [new Contour { X = [1, 2, 2, 1], Y = [1, 1, 2, 2] }]);
        SupportLocus At(double u, double v) => new() { Frame = Frame3D.Identity with { Origin = new PlanarVector3(u, v, 0) } };
        var built = PlateStripGeometryBuilder.Build("hole", region, At(0, 1.5), At(3, 1.5), 1.2);
        Assert.True(built.IsCalculable, string.Join("; ", built.Diagnostics.Select(d => d.Message)));
        Assert.Contains(built.Diagnostics, d => d.Code == "plate_strip_narrowed_by_hole");

        var layouts = PlateStripFiberLayouts.Resolve(source, built.Analogy!, [0.0, 0.25, 0.5, 0.75, 1.0], widthPointCount: 2);

        // Точки Гаусса (y = 1,5 ± 0,208) внутри отверстия не дают «точка вне сетки» — берётся кромка материала.
        Assert.True(layouts.IsCalculable, string.Join("; ", layouts.Diagnostics.Select(d => d.Message)));
        Assert.Equal(4, layouts.PerElement.Count);
    }
}
