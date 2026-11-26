using CScore.Fem;
using CScore.Fem.Editing;
using CScore.Fem.Loads;
using CScore.Planar;
using CScore.PlateRebar;
using Xunit;

namespace CScore.Tests.Fem;

/// <summary>«Развернуть ось» и «Сменить нормаль» (<see cref="FemOrientationFlip"/>): два разворота — исходное,
/// физика (точки нагрузок, ось выдачи усилий, положение граней) — та же в пространстве.</summary>
public class FemOrientationFlipTests
{
    const double Tol = 1e-9;

    static FemMeshNode N(int tag, double x, double y, double z = 0) => new() { NodeTag = tag.ToString(), X = x, Y = y, Z = z };

    /// <summary>Схема: стержень 1→2 длиной 5 м с поворотом 30°, пластина-квадрат 2×2 (Q4 «1 2 4 3» и T3) с областью.</summary>
    static FemOrientationInput Input()
    {
        var nodes = new List<FemNode>
        {
            new() { NodeTag = "1", X = 0, Y = 0, Z = 0 },
            new() { NodeTag = "2", X = 5, Y = 0, Z = 0 },
        };
        var bar = new FemMember { Id = 1, ElemTag = "Б1", ElemType = "beam", NodeIdsJson = "[1,2]", RotationDeg = 30 };
        var region = PlanarRegion.CreateFromContour(new Contour { X = [0, 2, 2, 0], Y = [0, 0, 2, 2] }, frame: Frame3D.Identity, tag: "П1");
        region.Id = 7;
        region.RebarZones.Add(new RebarZone
        {
            Name = "z", Face = RebarFace.PlusN,
            Polygon = [new() { U = 0.2, V = 0.2 }, new() { U = 1, V = 0.2 }, new() { U = 1, V = 1 }],
            Layout = new PlateRebarLayer { Angle = 15 },
        });
        region.RecalcFingerprint();
        var shell = new FemMember { Id = 2, ElemTag = "П1", ElemType = "shell", PlanarRegionId = 7 };
        var meshNodes = new List<FemMeshNode>
        {
            N(1, 0, 0), N(2, 5, 0), N(11, 0, 0, 1), N(12, 2, 0, 1), N(13, 0, 2, 1), N(14, 2, 2, 1), N(15, 3, 0, 1),
            N(20, 0, 0), N(21, 2, 0), N(22, 2, 2), N(23, 0, 2),
        };
        var elements = new List<FemElement>
        {
            new() { Id = 101, ElemTag = "1", ElemType = "beam", NodeIdsJson = "[1,2]", SourceMemberTag = "Б1",
                Origin = FemMember.MeshSourceImported, ReleaseI = 0b110000, ReleaseJ = null, BeamRotationDeg = 30 },
            // Q4 в порядке «1 2 4 3»: контур 11 → 12 → 14 → 13.
            new() { Id = 102, ElemTag = "2", ElemType = "shell", NodeIdsJson = "[11,12,13,14]", SourceMemberTag = "П1",
                Origin = FemMember.MeshSourceImported, LocalAxisAngleDeg = 20 },
            new() { Id = 103, ElemTag = "3", ElemType = "shell", NodeIdsJson = "[12,15,14]", SourceMemberTag = "П1",
                Origin = FemMember.MeshSourceImported },
        };
        var memberLoads = new List<FemMemberLoad>
        {
            new() { Id = 1, MemberId = 1, CoordinateSystem = "local", DistributionType = "trapezoidal", StartOffsetM = 0.5,
                EndOffsetM = 1, QxStart = 1, QyStart = 2, QzStart = 3, QxEnd = 4, QyEnd = 5, QzEnd = 6 },
            new() { Id = 2, MemberId = 1, CoordinateSystem = "local", DistributionType = "point", StartOffsetM = 1.5,
                QxStart = 7, QyStart = 8, QzStart = 9, Mx = 1, My = 2, Mz = 3 },
            new() { Id = 3, MemberId = 1, CoordinateSystem = "global", DistributionType = "uniform", StartOffsetM = 0,
                EndOffsetM = 2, QzStart = -10, QzEnd = -10 },
        };
        FemElementLoad L(int id, string kind, string cs, string axis, string target, params double[] values)
        {
            var l = new FemElementLoad { Id = id, TargetKind = FemLoadTargetKinds.Elements, LoadKind = kind, CoordinateSystem = cs, Axis = axis };
            l.SetTargetTags([target]);
            l.SetValues(values);
            return l;
        }
        var elementLoads = new List<FemElementLoad>
        {
            L(1, FemElementLoadKinds.Point, "local", "z", "1", 100, 1.25),
            L(2, FemElementLoadKinds.Trapezoid, "local", "y", "1", 10, 0.5, 20, 3),
            L(3, FemElementLoadKinds.Nodal, "local", "z", "2", 1, 2, 3, 4),
            L(4, FemElementLoadKinds.Edge, "global", "z", "2", 2, 4, 5, 6),
            L(5, FemElementLoadKinds.Point, "local", "z", "2", 50, 0.5, 0.25),
            L(6, FemElementLoadKinds.Uniform, "local", "z", "3", -3),
        };
        var supports = new List<FemLineSupport>();
        var support = new FemLineSupport { MemberTag = "П1", Mask = 0b100 };
        support.AssignEdge(FemLineSupportGeometry.Edges(shell, region)[1]);
        supports.Add(support);
        return new FemOrientationInput
        {
            Nodes = nodes, Members = [bar, shell], MemberLoads = memberLoads, ElementLoads = elementLoads, LineSupports = supports,
            Regions = new Dictionary<int, PlanarRegion> { [7] = region }, MeshNodes = meshNodes, MeshElements = elements,
        };
    }

    /// <summary>Вход с результатом первого разворота: КЭ и области — новые.</summary>
    static FemOrientationInput Next(FemOrientationInput input, FemOrientationResult r) => new()
    {
        Nodes = input.Nodes, Members = input.Members, MemberLoads = input.MemberLoads, ElementLoads = input.ElementLoads,
        LineSupports = input.LineSupports, Regions = r.Regions.ToDictionary(x => x.Id),
        MeshNodes = input.MeshNodes, MeshElements = input.MeshElements.Select(e => r.Elements.FirstOrDefault(x => x.Id == e.Id) ?? e).ToList(),
    };

    [Fact]
    public void Bar_SwapsEndsNegatesRotationAndLocalLoads()
    {
        var input = Input();
        var r = FemOrientationFlip.Apply(input, ["Б1"]);
        Assert.Equal(["Б1"], r.Bars);
        Assert.Equal("[2,1]", input.Members[0].NodeIdsJson);
        Assert.Equal(-30, input.Members[0].RotationDeg);

        var trap = input.MemberLoads[0];
        Assert.Equal((1.0, 0.5), (trap.StartOffsetM, trap.EndOffsetM));
        Assert.Equal((-4.0, 5.0, -6.0), (trap.QxStart, trap.QyStart, trap.QzStart));
        Assert.Equal((-1.0, 2.0, -3.0), (trap.QxEnd, trap.QyEnd, trap.QzEnd));
        var point = input.MemberLoads[1];
        Assert.Equal(3.5, point.StartOffsetM, Tol);
        Assert.Equal((-7.0, 8.0, -9.0), (point.QxStart, point.QyStart, point.QzStart));
        Assert.Equal((-1.0, 2.0, -3.0), (point.Mx, point.My, point.Mz));
        var global = input.MemberLoads[2];
        Assert.Equal((2.0, 0.0, -10.0, -10.0), (global.StartOffsetM, global.EndOffsetM, global.QzStart, global.QzEnd));

        var e = r.Elements.Single(x => x.ElemTag == "1");
        Assert.Equal("[2,1]", e.NodeIdsJson);
        Assert.Equal((null, 0b110000), (e.ReleaseI, e.ReleaseJ));
        Assert.Equal(-30, e.BeamRotationDeg);
        // Нагрузки на КЭ стержня: точка — от нового начала, местные x/z — со знаком минус, y — без.
        Assert.Equal([-100, 3.75], input.ElementLoads[0].Values);
        Assert.Equal([10, 4.5, 20, 2], input.ElementLoads[1].Values);
    }

    [Fact]
    public void BarFrame_KeepsLocalYAndReversesZ()
    {
        var i = new PlanarVector3(0, 0, 0);
        var j = new PlanarVector3(3, 4, 2);
        foreach (double beta in new[] { 0.0, 30, -75, 180 })
        {
            var (x0, y0, z0) = BeamLocalAxisConvention.Frame(i, j, beta);
            var (x1, y1, z1) = BeamLocalAxisConvention.Frame(j, i, beta == 0 ? 0 : -beta);
            Assert.True((x0 + x1).Length < Tol && (y0 - y1).Length < Tol && (z0 + z1).Length < Tol, $"β = {beta}");
        }
        // Вертикальный стержень: опорная ось — глобальная X, тоже не зависит от направления.
        var (_, yv0, _) = BeamLocalAxisConvention.Frame(i, new PlanarVector3(0, 0, 3), 40);
        var (_, yv1, _) = BeamLocalAxisConvention.Frame(new PlanarVector3(0, 0, 3), i, -40);
        Assert.True((yv0 - yv1).Length < Tol);
    }

    [Fact]
    public void Shell_ReversesWindingKeepingFirstNode_Q4StoredAs1243()
    {
        var input = Input();
        var r = FemOrientationFlip.Apply(input, ["П1"]);
        Assert.Equal("[11,13,12,14]", r.Elements.Single(e => e.ElemTag == "2").NodeIdsJson);
        Assert.Equal("[12,14,15]", r.Elements.Single(e => e.ElemTag == "3").NodeIdsJson);

        var nodes = input.MeshNodes.ToDictionary(n => n.NodeTag);
        foreach (var e in r.Elements)
        {
            var before = FemElementGeometry.Of(input.MeshElements.Single(x => x.Id == e.Id), nodes)!;
            var after = FemElementGeometry.Of(e, nodes)!;
            Assert.True((before.ShellFrame().Z + after.ShellFrame().Z).Length < Tol, $"нормаль КЭ {e.ElemTag}");
            Assert.Equal(before.Area, after.Area, Tol);
        }
    }

    [Fact]
    public void Shell_OutputAxisStaysInSpace()
    {
        var input = Input();
        var nodes = input.MeshNodes.ToDictionary(n => n.NodeTag);
        var before = input.MeshElements.Single(e => e.ElemTag == "2");
        var after = FemOrientationFlip.FlipElement(before, nodes)!;
        static PlanarVector3 Axis(FemElement e, FemElementGeometry g)
        {
            var (x, _, n) = g.ShellFrame();
            double a = e.LocalAxisAngleDeg!.Value * Math.PI / 180;
            return x * Math.Cos(a) + n.Cross(x) * Math.Sin(a);
        }
        var a0 = Axis(before, FemElementGeometry.Of(before, nodes)!);
        var a1 = Axis(after, FemElementGeometry.Of(after, nodes)!);
        Assert.True((a0 - a1).Length < 1e-9, $"{a0} → {a1}");
    }

    [Fact]
    public void ShellLoads_SamePointsAndDirectionsInSpace()
    {
        var input = Input();
        var nodes = input.MeshNodes.ToDictionary(n => n.NodeTag);
        var g0 = FemElementGeometry.Of(input.MeshElements.Single(e => e.ElemTag == "2"), nodes)!;
        var p0 = FemElementLoadNodalizer.ShellPointAt(g0, 0.5, 0.25);
        var r = FemOrientationFlip.Apply(input, ["П1"]);
        var g1 = FemElementGeometry.Of(r.Elements.Single(e => e.ElemTag == "2"), nodes)!;

        var nodal = input.ElementLoads[2];
        Assert.Equal([-1, -3, -2, -4], nodal.Values);   // узлы [11,13,12,14], местная z — со знаком минус
        var edge = input.ElementLoads[3];
        Assert.Equal([3, 4, 5, 6], edge.Values);         // ребро между узлами 12 и 14: были номера 2 и 4, стали 3 и 4
        var point = input.ElementLoads[4];
        Assert.Equal(-50, point.Values[0]);
        var p1 = FemElementLoadNodalizer.ShellPointAt(g1, point.Values[1], point.Values[2]);
        Assert.True((p0 - p1).Length < Tol);
        Assert.Equal([3], input.ElementLoads[5].Values);
    }

    [Fact]
    public void Region_MirroredFrameContoursZonesAndLineSupports()
    {
        var input = Input();
        var region = input.Regions[7];
        var r = FemOrientationFlip.Apply(input, ["П1"]);
        var mirrored = r.Regions.Single();
        mirrored.Frame.Validate();
        Assert.Equal(new PlanarVector3(0, 0, -1), mirrored.Frame.LocalZ);
        Assert.Equal(region.Frame.LocalX, mirrored.Frame.LocalX);

        // Внешний контур — против часовой в новых осях, та же первая вершина и те же точки в пространстве.
        var (x, y) = PlanarRegionTopologyValidator.ToOpenLoop(mirrored.Hull!.X, mirrored.Hull.Y);
        Assert.True(PlanarRegionTopologyValidator.SignedArea(mirrored.Hull.X, mirrored.Hull.Y) > 0);
        var (x0, y0) = PlanarRegionTopologyValidator.ToOpenLoop(region.Hull!.X, region.Hull.Y);
        var global0 = x0.Select((u, k) => Global(region.Frame, u, y0[k])).ToList();
        var global1 = x.Select((u, k) => Global(mirrored.Frame, u, y[k])).ToList();
        Assert.True((global0[0] - global1[0]).Length < Tol);
        Assert.All(global0, p => Assert.Contains(global1, q => (p - q).Length < Tol));

        var zone = mirrored.RebarZones.Single();
        Assert.Equal(-15, zone.Layout.Angle);
        Assert.Equal(RebarFace.PlusN, zone.Face);
        Assert.Equal(-0.2, zone.Polygon[0].V);

        var support = input.LineSupports.Single();
        Assert.Equal(1, r.LineSupports);
        var edge = FemLineSupportGeometry.Edges(input.Members[1], mirrored).Single(e => e.Key == support.Key);
        Assert.True((edge.A - new PlanarVector3(2, 2, 0)).Length < Tol || (edge.B - new PlanarVector3(2, 2, 0)).Length < Tol);
        Assert.True((edge.A - new PlanarVector3(2, 0, 0)).Length < Tol || (edge.B - new PlanarVector3(2, 0, 0)).Length < Tol);
    }

    [Fact]
    public void Wall_KeepsVerticalY()
    {
        var frame = new Frame3D(PlanarVector3.Zero, new PlanarVector3(1, 0, 0), new PlanarVector3(0, 0, 1), new PlanarVector3(0, -1, 0));
        var region = PlanarRegion.CreateFromContour(new Contour { X = [0, 3, 3, 0], Y = [0, 0, 2.8, 2.8] }, frame: frame, tag: "С1");
        var (mirrored, _) = FemOrientationFlip.MirrorRegion(region);
        mirrored.Frame.Validate();
        Assert.Equal(new PlanarVector3(0, 0, 1), mirrored.Frame.LocalY);
        Assert.Equal(new PlanarVector3(0, 1, 0), mirrored.Frame.LocalZ);
        var (back, _) = FemOrientationFlip.MirrorRegion(mirrored);
        AssertSameRegion(region, back);
    }

    [Fact]
    public void TwoFlips_RestoreEverything()
    {
        var input = Input();
        var members = input.Members.Select(m => m.ShallowCopy()).ToList();
        string Loads(FemOrientationInput i) => string.Join("|", i.MemberLoads.Select(l =>
            $"{l.StartOffsetM:R},{l.EndOffsetM:R},{l.QxStart},{l.QyStart},{l.QzStart},{l.QxEnd},{l.QyEnd},{l.QzEnd},{l.Mx},{l.My},{l.Mz}"))
            + "#" + string.Join("|", i.ElementLoads.Select(l => $"{l.Axis}:{string.Join(",", l.Values.Select(v => Math.Round(v, 9)))}"))
            + "#" + string.Join("|", i.LineSupports.Select(s => $"{s.Key}:{s.X1:0.###},{s.Y1:0.###},{s.X2:0.###},{s.Y2:0.###}"));
        string before = Loads(input);
        var region = input.Regions[7];

        var r1 = FemOrientationFlip.Apply(input, ["Б1", "П1"]);
        var second = Next(input, r1);
        var r2 = FemOrientationFlip.Apply(second, ["Б1", "П1"]);

        Assert.Equal(before, Loads(input));
        for (int k = 0; k < members.Count; k++)
        {
            Assert.Equal(members[k].NodeIdsJson, input.Members[k].NodeIdsJson);
            Assert.Equal(members[k].RotationDeg, input.Members[k].RotationDeg);
        }
        foreach (var e in input.MeshElements)
        {
            var back = r2.Elements.Single(x => x.Id == e.Id);
            Assert.Equal(e.NodeIdsJson, back.NodeIdsJson);
            Assert.Equal((e.ReleaseI, e.ReleaseJ, e.BeamRotationDeg), (back.ReleaseI, back.ReleaseJ, back.BeamRotationDeg));
            Assert.Equal(e.LocalAxisAngleDeg ?? 0, back.LocalAxisAngleDeg ?? 0, 1e-9);
        }
        AssertSameRegion(region, r2.Regions.Single());
    }

    [Fact]
    public void Snapshot_MirroredAndRestored()
    {
        var region = PlanarRegion.CreateFromContour(new Contour { X = [0, 2, 2, 0], Y = [0, 0, 1, 1] }, frame: Frame3D.Identity, tag: "П");
        var snapshot = new PlanarMeshSnapshot
        {
            RegionId = 1, InputFingerprint = "x", IsCalculable = true,
            Nodes = [new(0, 0, 0, 0, 0, 0), new(1, 1, 0, 1, 0, 0), new(2, 2, 0, 2, 0, 0), new(3, 2, 1, 2, 1, 0), new(4, 1, 1, 1, 1, 0), new(5, 0, 1, 0, 1, 0)],
            Elements = [new(0, PlanarMeshElementKind.Quadrangle4, [0, 1, 4, 5]), new(1, PlanarMeshElementKind.Triangle3, [1, 2, 3])],
            BoundaryMappings = [new PlanarMeshBoundaryMapping { Key = new(BoundaryLoop.Outer, 0, 0, 1), NodeIndices = [0, 1, 2] }],
        };
        var (mirrored, map) = FemOrientationFlip.MirrorRegion(region);
        var m = FemOrientationFlip.MirrorSnapshot(snapshot, region, map);
        Assert.Equal("", m.InputFingerprint);
        Assert.Equal([0, 5, 4, 1], m.Elements[0].NodeIndices);
        Assert.Equal([1, 3, 2], m.Elements[1].NodeIndices);
        Assert.Equal(-1, m.Nodes[5].V);
        // Сторона 0→1 (y = 0) — в новой нумерации 3→0 (вершины 1, 3 меняются местами).
        Assert.Equal(new PlanarBoundaryKey(BoundaryLoop.Outer, 0, 3, 0), m.BoundaryMappings[0].Key);
        Assert.Equal([2, 1, 0], m.BoundaryMappings[0].NodeIndices);
        // Новая сторона 3 → 0 в новой области — тот же отрезок y = 0.
        var (x, y) = PlanarRegionTopologyValidator.ToOpenLoop(mirrored.Hull!.X, mirrored.Hull.Y);
        Assert.Equal((2.0, 0.0), (x[3], y[3]));
        Assert.Equal((0.0, 0.0), (x[0], y[0]));

        var (back, map2) = FemOrientationFlip.MirrorRegion(mirrored);
        var b = FemOrientationFlip.MirrorSnapshot(m, mirrored, map2);
        Assert.Equal(snapshot.Elements.Select(e => string.Join(",", e.NodeIndices)), b.Elements.Select(e => string.Join(",", e.NodeIndices)));
        Assert.Equal(snapshot.Nodes, b.Nodes);
        Assert.Equal(snapshot.BoundaryMappings[0].Key, b.BoundaryMappings[0].Key);
        Assert.Equal(snapshot.BoundaryMappings[0].NodeIndices, b.BoundaryMappings[0].NodeIndices);
    }

    [Fact]
    public void ElementList_PartlyFlipped_IsSplit()
    {
        var input = Input();
        var load = new FemElementLoad { Id = 9, TargetKind = FemLoadTargetKinds.Elements, LoadKind = FemElementLoadKinds.Uniform, CoordinateSystem = "local", Axis = "z" };
        load.SetTargetTags(["1", "2"]);
        load.SetValues([5]);
        input.ElementLoads.Clear();
        input.ElementLoads.Add(load);
        FemOrientationFlip.Apply(input, ["П1"]);
        Assert.Equal(2, input.ElementLoads.Count);
        Assert.Equal(["1"], input.ElementLoads[0].TargetTags);
        Assert.Equal([5], input.ElementLoads[0].Values);
        Assert.Equal(["2"], input.ElementLoads[1].TargetTags);
        Assert.Equal([-5], input.ElementLoads[1].Values);
    }

    [Fact]
    public void MembersTarget_WiderThanFlipped_IsSkipped()
    {
        var input = Input();
        var load = new FemElementLoad { Id = 9, TargetKind = FemLoadTargetKinds.Members, LoadKind = FemElementLoadKinds.Uniform, CoordinateSystem = "local", Axis = "z" };
        load.SetTargetTags(["Б1", "П1"]);
        load.SetValues([5]);
        input.ElementLoads.Clear();
        input.ElementLoads.Add(load);
        var r = FemOrientationFlip.Apply(input, ["П1"]);
        Assert.Equal([5], load.Values);
        Assert.Equal(FemOrientationLoadSkip.PartialTarget, r.SkippedLoads.Single().Reason);
    }

    static PlanarVector3 Global(Frame3D f, double u, double v) => f.Origin + f.LocalX * u + f.LocalY * v;

    static void AssertSameRegion(PlanarRegion a, PlanarRegion b)
    {
        Assert.True((a.Frame.LocalX - b.Frame.LocalX).Length < Tol && (a.Frame.LocalY - b.Frame.LocalY).Length < Tol
                    && (a.Frame.LocalZ - b.Frame.LocalZ).Length < Tol);
        Assert.Equal(a.Contours.Count, b.Contours.Count);
        for (int k = 0; k < a.Contours.Count; k++)
        {
            Assert.Equal(a.Contours[k].X, b.Contours[k].X);
            Assert.Equal(a.Contours[k].Y, b.Contours[k].Y);
        }
        Assert.Equal(a.RebarZones.Select(z => z.Layout.Angle), b.RebarZones.Select(z => z.Layout.Angle));
        Assert.Equal(a.GeometryFingerprint, b.GeometryFingerprint);
    }
}
