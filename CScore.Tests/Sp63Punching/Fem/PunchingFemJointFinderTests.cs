using CScore.Sp63Punching;
using CScore.Sp63Punching.Fem;
using Xunit;
using static CScore.Tests.Sp63Punching.Fem.PunchingFemTestMesh;

namespace CScore.Tests.Sp63Punching.Fem;

/// <summary>Поиск узлов «колонна — плита» и их геометрия в осях узла (срез 3, задача 3а).</summary>
public class PunchingFemJointFinderTests
{
    const double Z = 3.0;

    static PunchingFemTestMesh Plate(Func<double, double, bool>? skip = null, Func<double, double, double>? h = null)
    {
        var m = new PunchingFemTestMesh();
        m.Slab(0, 0, 6, 6, Z, 0.2, skip: skip, thickness: h);
        return m;
    }

    static PunchingFemJoint Single(PunchingFemTestMesh m, PunchingFemBarSection? section = null, PunchingFemOptions? o = null)
        => Assert.Single(PunchingFemJointFinder.Find(m.Model(section), o));

    [Fact]
    public void InteriorColumn_RectangleArea_NoEdges()
    {
        var m = Plate();
        var col = m.Bar(m.Node(3, 3, 0), m.Node(3, 3, Z));
        var j = Single(m, Rect(0.4, 0.6));

        Assert.Null(j.NoCheck);
        Assert.Equal(m.Node(3, 3, Z), j.NodeTag);
        var bar = Assert.Single(j.Bars);
        Assert.Equal(col.ElemTag, bar.ElemTag);
        Assert.True(bar.Below);
        var r = Assert.IsType<PunchingLoadArea.Rectangle>(j.Area);
        // Вертикальный стержень: местная Y — общая X, Z — общая Y.
        Assert.Equal(0.4, r.Bx, 9);
        Assert.Equal(0.6, r.By, 9);
        Assert.Equal(1.0, j.AxisX.X, 9);
        Assert.Equal(1.0, j.Normal.Z, 9);
        Assert.Empty(j.Edges);
        Assert.Empty(j.Holes);
        Assert.Equal(0.2, j.H, 12);
        Assert.Equal(0.2 - 0.5 * (0.035 + 0.045), j.H0, 12);
        Assert.Equal(3.0, j.Origin.X, 9);
        Assert.Equal(3.0, j.Origin.Y, 9);
        // Основание пирамиды — площадь, смещённая на h0: (0,4 + 2h0)·(0,6 + 2h0).
        Assert.Equal(4, j.PyramidBase.Count);
        double h0 = j.H0;
        double area = Math.Abs(Enumerable.Range(0, 4).Sum(i => j.PyramidBase[i].Cross(j.PyramidBase[(i + 1) % 4]))) / 2;
        Assert.Equal((0.4 + 2 * h0) * (0.6 + 2 * h0), area, 9);
        Assert.NotEmpty(j.BaseShells);
    }

    [Fact]
    public void ColumnsBelowAndAbove_OneJoint()
    {
        var m = Plate();
        m.Bar(m.Node(3, 3, 0), m.Node(3, 3, Z));
        m.Bar(m.Node(3, 3, Z), m.Node(3, 3, 6));
        var j = Single(m);
        Assert.Null(j.NoCheck);
        Assert.Equal(2, j.Bars.Count);
        Assert.Single(j.Bars, b => b.Below);
        Assert.Single(j.Bars, b => !b.Below);
    }

    [Fact]
    public void OnlyColumnAbove_BottomCover()
    {
        var m = Plate();
        m.Bar(m.Node(3, 3, Z), m.Node(3, 3, 6));
        var model = m.Model(Rect(0.4, 0.4), props: e => new PunchingFemShellProps("s", 0.3, 1,
            new PunchingFemCover(0.03, 0.04), new PunchingFemCover(0.05, 0.06), "Ф"));
        var j = Assert.Single(PunchingFemJointFinder.Find(model));
        Assert.Equal(0.05, j.Ax, 12);
        Assert.Equal(0.06, j.Ay, 12);
    }

    [Fact]
    public void EdgeColumn_OneEdgeOnMinusX()
    {
        var m = Plate();
        m.Bar(m.Node(0.4, 3, 0), m.Node(0.4, 3, Z));
        var j = Single(m);
        Assert.Null(j.NoCheck);
        var e = Assert.Single(j.Edges);
        Assert.Equal(new PlanPoint(-1, 0), PunchingFemJointFinder.SideOf(e.InwardNormal));
        // Расстояние от грани колонны (x = −0,2) до края (x = −0,4) — 0,2 м.
        Assert.Equal(0.2, -0.2 - e.PointOnEdge.X, 9);
        Assert.Equal(1.0, e.InwardNormal.X, 9);
    }

    [Fact]
    public void CornerColumn_TwoEdges()
    {
        var m = Plate();
        m.Bar(m.Node(5.8, 5.6, 0), m.Node(5.8, 5.6, Z));
        var j = Single(m);
        Assert.Null(j.NoCheck);
        Assert.Equal(2, j.Edges.Count);
        var sides = j.Edges.Select(e => PunchingFemJointFinder.SideOf(e.InwardNormal)).ToHashSet();
        Assert.Contains(new PlanPoint(1, 0), sides);
        Assert.Contains(new PlanPoint(0, 1), sides);
    }

    [Fact]
    public void FarEdge_NotCounted()
    {
        var m = Plate();
        // h0 = 0,16: 6h0 = 0,96; до края 2,8 м.
        m.Bar(m.Node(3, 3, 0), m.Node(3, 3, Z));
        Assert.Empty(Single(m).Edges);
    }

    [Fact]
    public void HoleNearColumn_Counted_FarHoleIgnored()
    {
        var m = Plate(skip: (x, y) => x > 3.4 && x < 3.8 && y > 2.8 && y < 3.2 || x > 1.0 && x < 1.4 && y > 1.0 && y < 1.4);
        m.Bar(m.Node(3, 3, 0), m.Node(3, 3, Z));
        var j = Single(m);
        Assert.Null(j.NoCheck);
        var hole = Assert.Single(j.Holes);
        Assert.Equal(0.4 * 0.4, Math.Abs(Area(hole.Polygon)), 9);
        Assert.True(Area(hole.Polygon) > 0, "Отверстие — против часовой стрелки.");
        Assert.All(hole.Polygon, p => Assert.InRange(p.X, 0.4 - 1e-9, 0.8 + 1e-9));
    }

    [Fact]
    public void RotatedColumn_AxesFollowSection()
    {
        var m = Plate();
        m.Bar(m.Node(3, 3, 0), m.Node(3, 3, Z), rotationDeg: 30);
        var j = Single(m, Rect(0.3, 0.5));
        var r = Assert.IsType<PunchingLoadArea.Rectangle>(j.Area);
        Assert.Equal(0.3, r.Bx, 9);
        Assert.Equal(0.5, r.By, 9);
        Assert.Equal(Math.Cos(Math.PI / 6), j.AxisX.X, 9);
        Assert.Equal(Math.Sin(Math.PI / 6), j.AxisX.Y, 9);
    }

    [Fact]
    public void RoundColumn_Circle()
    {
        var m = Plate();
        m.Bar(m.Node(3, 3, 0), m.Node(3, 3, Z));
        var c = Assert.IsType<PunchingLoadArea.Circle>(Single(m, Round(0.5)).Area);
        Assert.Equal(0.5, c.D, 9);
    }

    [Fact]
    public void LongSection_IsWall()
    {
        var m = Plate();
        m.Bar(m.Node(3, 3, 0), m.Node(3, 3, Z));
        var j = Single(m, Rect(0.2, 1.0));
        Assert.Equal(PunchingFemNoCheck.Wall, j.NoCheck);
        // Пилон 0,25×0,8 (отношение 3,2) — проверяется.
        Assert.Null(Single(m, Rect(0.25, 0.8)).NoCheck);
    }

    [Fact]
    public void NoSection_AreaUnknown()
    {
        var m = Plate();
        m.Bar(m.Node(3, 3, 0), m.Node(3, 3, Z));
        var j = Assert.Single(PunchingFemJointFinder.Find(m.Model(sections: _ => null)));
        Assert.Equal(PunchingFemNoCheck.AreaUnknown, j.NoCheck);
        Assert.False(string.IsNullOrEmpty(j.NoCheckText));
    }

    [Fact]
    public void MixedThicknessInBase_NoCheck()
    {
        var m = Plate(h: (x, y) => x > 3.2 ? 0.25 : 0.2);
        m.Bar(m.Node(3, 3, 0), m.Node(3, 3, Z));
        Assert.Equal(PunchingFemNoCheck.SlabSectionMixed, Single(m).NoCheck);
    }

    [Fact]
    public void JunctionWithWall_NoCheck()
    {
        var m = Plate();
        string master = m.Node(3, 3, Z);
        m.Bar(m.Node(3, 3, 0), master);
        m.WallXZ(3.2, 4.0, 3.0, 0, Z, 0.2);
        var slaves = m.NodesIn(2.8, 2.8, 3.2, 3.2, Z).Where(t => t != master).ToList();
        m.Body(master, slaves);
        Assert.Equal(PunchingFemNoCheck.JunctionWallAndColumn, Single(m).NoCheck);
    }

    [Fact]
    public void ColumnViaRigidBody_MasterOffMesh()
    {
        var m = Plate();
        // Ведущий — узел колонны на отметке плиты вне сетки плиты; ведомые — узлы плиты в следе.
        string master = m.Node(3.1, 3.1, Z);
        m.Bar(m.Node(3.1, 3.1, 0), master);
        m.Body(master, m.NodesIn(2.8, 2.8, 3.4, 3.4, Z).Where(t => t != master));
        var j = Single(m);
        Assert.Null(j.NoCheck);
        Assert.Equal(master, j.NodeTag);
        Assert.Equal(3.1, j.Origin.X, 9);
        Assert.Contains(master, j.JunctionNodes);
        Assert.True(j.JunctionNodes.Count > 4);
    }

    [Fact]
    public void OtherColumnInsideBase_NoCheck()
    {
        var m = Plate();
        m.Bar(m.Node(3, 3, 0), m.Node(3, 3, Z));
        m.Bar(m.Node(3.4, 3, 0), m.Node(3.4, 3, Z));
        var joints = PunchingFemJointFinder.Find(m.Model());
        Assert.Equal(2, joints.Count);
        Assert.All(joints, j => Assert.Equal(PunchingFemNoCheck.SupportInsideBase, j.NoCheck));
    }

    [Fact]
    public void InPlaneBeam_Warning()
    {
        var m = Plate();
        m.Bar(m.Node(3, 3, 0), m.Node(3, 3, Z));
        m.Bar(m.Node(3, 3, Z), m.Node(3.6, 3, Z));
        var j = Single(m);
        Assert.Null(j.NoCheck);
        Assert.True(j.Ribs);
        Assert.Contains(j.Warnings, w => w.Code == "fem_beam_in_base");
    }

    [Fact]
    public void InclinedSlab_NoCheck()
    {
        var m = new PunchingFemTestMesh();
        double k = Math.Tan(10 * Math.PI / 180);
        for (int i = 0; i < 20; i++)
            for (int jj = 0; jj < 20; jj++)
            {
                double xa = i * 0.2, ya = jj * 0.2, xb = xa + 0.2, yb = ya + 0.2;
                m.Quad(m.Node(xa, ya, Z + k * xa), m.Node(xb, ya, Z + k * xb), m.Node(xb, yb, Z + k * xb), m.Node(xa, yb, Z + k * xa), 0.2);
            }
        m.Bar(m.Node(2, 2, 0), m.Node(2, 2, Z + k * 2));
        Assert.Equal(PunchingFemNoCheck.SlabInclined, Single(m).NoCheck);
    }

    [Fact]
    public void BarWithoutSlab_NotAJoint()
    {
        var m = Plate();
        m.Bar(m.Node(10, 10, 0), m.Node(10, 10, Z));
        Assert.Empty(PunchingFemJointFinder.Find(m.Model()));
    }

    static double Area(IReadOnlyList<PlanPoint> p) =>
        Enumerable.Range(0, p.Count).Sum(i => p[i].Cross(p[(i + 1) % p.Count])) / 2;
}
