using CScore.Sp63Punching;
using CScore.Sp63Punching.Fem;
using Xunit;
using static CScore.Tests.Sp63Punching.Fem.PunchingFemTestMesh;

namespace CScore.Tests.Sp63Punching.Fem;

/// <summary>Срез 5в: поиск торцов и углов стен в сетке (СП 430 п. 6.3.8) и узлов стен «нет проверки».</summary>
public class PunchingFemWallFinderTests
{
    const double Z = 3.0;
    const double H0 = 0.2 - 0.5 * (0.035 + 0.045);   // плита 200, привязки по умолчанию теста

    static PunchingFemTestMesh Plate()
    {
        var m = new PunchingFemTestMesh();
        m.Slab(0, 0, 6, 6, Z, 0.2);
        return m;
    }

    static List<PunchingFemJoint> Find(PunchingFemTestMesh m, PunchingFemBarSection? section = null) =>
        PunchingFemJointFinder.Find(m.Model(section));

    static double Area(IReadOnlyList<PlanPoint> p) =>
        Math.Abs(Enumerable.Range(0, p.Count).Sum(i => p[i].Cross(p[(i + 1) % p.Count]))) / 2;

    [Fact]
    public void StraightWall_TwoEnds_AxesOutward()
    {
        var m = Plate();
        m.WallXZ(1.0, 4.0, 3.0, 0, Z, 0.2, h: 0.2);     // стена 3 м × 200 под плитой
        var joints = Find(m);
        Assert.Equal(2, joints.Count);
        Assert.All(joints, j =>
        {
            Assert.Null(j.NoCheck);
            Assert.Equal(PunchingFemJointKind.WallEnd, j.Kind);
            Assert.Equal(PunchingWallKind.End, j.Wall!.Kind);
            Assert.Equal(0.2, j.Wall.A1, 9);
            Assert.Equal(3.0, j.Origin.Y, 9);
            // Основание пирамиды торца: (a1 + 2h0)², без краёв.
            Assert.Equal(Math.Pow(0.2 + 2 * H0, 2), Area(j.PyramidBase), 9);
            Assert.NotEmpty(j.BaseShells);
            Assert.Empty(j.Edges);
            Assert.Contains(j.NodeTag, j.JunctionNodes);
        });
        var right = joints.Single(j => j.Origin.X > 2);
        Assert.Equal(4.0, right.Origin.X, 9);
        Assert.Equal(1.0, right.AxisX.X, 9);                 // ось x — наружу от стены
        var left = joints.Single(j => j.Origin.X < 2);
        Assert.Equal(1.0, left.Origin.X, 9);
        Assert.Equal(-1.0, left.AxisX.X, 9);
        Assert.Equal(m.Node(4.0, 3.0, Z), right.NodeTag);
    }

    [Fact]
    public void LWall_CornerAndTwoEnds()
    {
        var m = Plate();
        m.WallXZ(2.0, 4.0, 2.0, 0, Z, 0.2, h: 0.2);     // a1 = 200 вдоль X
        m.WallYZ(2.0, 2.0, 4.0, 0, Z, 0.2, h: 0.25);    // a2 = 250 вдоль Y
        var joints = Find(m);
        Assert.Equal(3, joints.Count);
        var c = joints.Single(j => j.Kind == PunchingFemJointKind.WallCorner);
        Assert.Null(c.NoCheck);
        Assert.Equal(0.2, c.Wall!.A1, 9);
        Assert.Equal(0.25, c.Wall.T2, 9);
        // Начало — наружный угол: ось стены вдоль X смещена на a1/2, вдоль Y — на a2/2.
        Assert.Equal(2.0 - 0.125, c.Origin.X, 9);
        Assert.Equal(2.0 - 0.1, c.Origin.Y, 9);
        Assert.Equal(1.0, c.AxisX.X, 9);
        Assert.Equal(1.0, c.AxisY.Y, 9);
        double l = 0.2 + 0.25 + 2 * H0;
        Assert.Equal(l * l - 0.2 * 0.25 / 2, Area(c.PyramidBase), 9);
        Assert.Contains(c.Warnings, w => w.Code == "fem_wall_corner_thickness");
        Assert.Equal(2, joints.Count(j => j.Kind == PunchingFemJointKind.WallEnd && j.NoCheck == null));
    }

    [Fact]
    public void TJunction_NoCheck_EndsChecked()
    {
        var m = Plate();
        m.WallXZ(1.0, 5.0, 2.0, 0, Z, 0.2);             // полка Т
        m.WallYZ(3.0, 2.0, 4.0, 0, Z, 0.2);             // стенка Т примыкает к середине полки
        var joints = Find(m);
        var tee = Assert.Single(joints, j => j.NoCheck == PunchingFemNoCheck.WallTee);
        Assert.Equal(m.Node(3.0, 2.0, Z), tee.NodeTag);
        Assert.Equal(3, joints.Count(j => j.Kind == PunchingFemJointKind.WallEnd && j.NoCheck == null));
    }

    [Fact]
    public void ObliqueWalls_AngleNoCheck()
    {
        var m = Plate();
        m.WallXZ(2.0, 4.0, 2.0, 0, Z, 0.2);
        // Вторая стена под 45° от узла (2; 2): пластины по диагонали сетки плиты.
        for (int i = 0; i < 8; i++)
        {
            double a = 2.0 + 0.2 * i, b = a + 0.2;
            for (int k = 0; k < 15; k++)
                m.Quad(m.Node(a, a, 0.2 * k), m.Node(b, b, 0.2 * k), m.Node(b, b, 0.2 * (k + 1)), m.Node(a, a, 0.2 * (k + 1)), 0.2);
        }
        var joints = Find(m);
        Assert.Contains(joints, j => j.NoCheck == PunchingFemNoCheck.WallAngle && j.NodeTag == m.Node(2.0, 2.0, Z));
    }

    [Fact]
    public void WallsBelowAndAbove_Merged_MixedThicknessNoCheck()
    {
        var m = Plate();
        m.WallXZ(1.0, 4.0, 3.0, 0, Z, 0.2, h: 0.2);
        m.WallXZ(1.0, 4.0, 3.0, Z, 6.0, 0.2, h: 0.2);    // та же стена выше — тот же узел
        Assert.Equal(2, Find(m).Count(j => j.NoCheck == null));

        var mixed = Plate();
        mixed.WallXZ(1.0, 4.0, 3.0, 0, Z, 0.2, h: 0.2);
        mixed.WallXZ(1.0, 4.0, 3.0, Z, 6.0, 0.2, h: 0.16);
        var joints = Find(mixed);
        Assert.Equal(2, joints.Count);
        Assert.All(joints, j => Assert.Equal(PunchingFemNoCheck.WallThicknessMixed, j.NoCheck));
    }

    [Fact]
    public void DoorOpening_FourEnds()
    {
        var m = Plate();
        m.WallXZ(0.6, 2.4, 3.0, 0, Z, 0.2);
        m.WallXZ(3.4, 5.2, 3.0, 0, Z, 0.2);              // проём 1 м между x = 2,4 и 3,4
        var joints = Find(m);
        Assert.Equal(4, joints.Count(j => j.Kind == PunchingFemJointKind.WallEnd && j.NoCheck == null));
    }

    [Fact]
    public void EndAtSlabEdge_EdgeBehindEnd()
    {
        var m = Plate();
        m.WallXZ(3.0, 6.0, 3.0, 0, Z, 0.2);              // стена доходит до края плиты x = 6
        var joints = Find(m);
        var atEdge = joints.Single(j => j.Origin.X > 5);
        Assert.Null(atEdge.NoCheck);
        var e = Assert.Single(atEdge.Edges);
        // Край за торцом: внутренняя нормаль в осях узла — к стене (−x), проходит через торец.
        Assert.Equal(-1.0, e.N.X, 9);
        Assert.Equal(0.0, e.SignedDistance(new PlanPoint(0, 0)), 9);
        var r = PunchingChecker.Check(new PunchingInput(atEdge.Area!, atEdge.H0, 1050, new PunchingForces(100, 0, 0),
            Edges: atEdge.Edges, Wall: atEdge.Wall));
        Assert.Contains(r.Variants, v => v.Variant.Kind == PunchingVariantKind.WallEndEdge);
    }

    [Fact]
    public void BarWall_TwoEnds_AtSectionEnds()
    {
        var m = Plate();
        m.Bar(m.Node(3, 3, 0), m.Node(3, 3, Z));
        var joints = Find(m, Rect(0.2, 1.2));            // местная Y — общая X: 200 вдоль X, 1200 вдоль Y
        Assert.Equal(2, joints.Count);
        Assert.All(joints, j =>
        {
            Assert.Null(j.NoCheck);
            Assert.Equal(PunchingFemJointKind.WallEnd, j.Kind);
            Assert.Equal(0.2, j.Wall!.A1, 9);
            Assert.Equal(3.0, j.Origin.X, 9);
            Assert.Equal(0.6, Math.Abs(j.Origin.Y - 3.0), 9);
            Assert.Equal(Math.Sign(j.Origin.Y - 3.0), Math.Sign(j.AxisX.Y));
            Assert.Contains(j.Warnings, w => w.Code == "fem_wall_bar");
        });
        Assert.Equal(new[] { "/1", "/2" }, joints.Select(j => j.NodeTag[^2..]).Order());
    }
}
