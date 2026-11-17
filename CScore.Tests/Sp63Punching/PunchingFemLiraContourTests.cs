using CScore.Import;
using CScore.Planar;
using CScore.Sp63Punching;
using CScore.Sp63Punching.Fem;
using Xunit;

namespace CScore.Tests.Sp63Punching;

/// <summary>Контур ЛИРЫ из *.asp → контур пользователя ядра в осях узла.</summary>
public class PunchingFemLiraContourTests
{
    // Узел в (5; 3; 2), ось x узла — вдоль общей Y, ось y — вдоль −X.
    static readonly PunchingFemJoint Joint = new()
    {
        NodeTag = "4940", Origin = new PlanarVector3(5, 3, 2), AxisX = new PlanarVector3(0, 1, 0),
        AxisY = new PlanarVector3(-1, 0, 0), Normal = new PlanarVector3(0, 0, 1),
    };

    static LiraAspSegment S(double x1, double y1, double x2, double y2) => new(5 + x1, 3 + y1, 2, 5 + x2, 3 + y2, 2);

    static double Perimeter(IReadOnlyList<PlanPoint> p, IReadOnlyList<int> inactive) =>
        Enumerable.Range(0, p.Count).Where(i => !inactive.Contains(i))
            .Sum(i => Math.Sqrt(Math.Pow(p[(i + 1) % p.Count].X - p[i].X, 2) + Math.Pow(p[(i + 1) % p.Count].Y - p[i].Y, 2)));

    [Fact]
    public void Closed_ShuffledAndReversed_AllSidesActive()
    {
        // Квадрат 0,6×0,6 в общих осях: отрезки вразнобой, два — в обратном направлении, концы с погрешностью float.
        var segs = new[]
        {
            S(0.3, -0.3, 0.3, 0.3), S(-0.3, 0.3, -0.3, -0.3), S(0.3, 0.3, -0.3, 0.3001), S(0.3, -0.3, -0.3, -0.3),
        };
        var c = PunchingFemLiraContour.ToLocal(Joint, segs);
        Assert.NotNull(c);
        Assert.Equal(4, c!.Value.Points.Count);
        Assert.Empty(c.Value.Inactive);
        Assert.Equal(2.4, Perimeter(c.Value.Points, c.Value.Inactive), 3);
        // Ядро считает тот же u.
        var r = PunchingChecker.Check(new PunchingInput(new PunchingLoadArea.Rectangle(0.4, 0.4), 0.2, 1050, new PunchingForces(300, 0, 0),
            CustomContours: [new PunchingCustomContour(c.Value.Points, c.Value.Inactive)], Options: new PunchingOptions(CustomReplacesMain: true)));
        Assert.Equal(2.4, Assert.Single(r.Variants).Geometry.U, 3);
    }

    [Fact]
    public void OpenU_ClosingSideInactive_InNodeAxes()
    {
        // П у края y = −0,5 (общие оси): три отрезка.
        var segs = new[] { S(-0.3, -0.5, -0.3, 0.3), S(-0.3, 0.3, 0.3, 0.3), S(0.3, 0.3, 0.3, -0.5) };
        var c = PunchingFemLiraContour.ToLocal(Joint, segs)!.Value;
        Assert.Equal(4, c.Points.Count);
        Assert.Equal([3], c.Inactive);
        Assert.Equal(0.8 + 0.6 + 0.8, Perimeter(c.Points, c.Inactive), 9);
        // Общая точка (5 − 0,3; 3 − 0,5) в осях узла: x = −0,5, y = 0,3.
        Assert.Contains(c.Points, p => Math.Abs(p.X + 0.5) < 1e-9 && Math.Abs(p.Y - 0.3) < 1e-9);
    }

    [Fact]
    public void Broken_OrSingleSegment_Null()
    {
        Assert.Null(PunchingFemLiraContour.ToLocal(Joint, [S(-0.3, 0, 0.3, 0)]));
        Assert.Null(PunchingFemLiraContour.ToLocal(Joint, [S(0, 0, 1, 0), S(2, 0, 3, 0), S(3, 0, 3, 1)]));
    }
}
