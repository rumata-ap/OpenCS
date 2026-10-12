using CScore.Fem;
using OpenCS.ViewModels;
using Xunit;

namespace OpenCS.Tests.FemResult;

/// <summary>Эпюры на стержнях в 3D: заливка по знаку без «перекрута», прореживание ординат, подписи экстремумов.</summary>
public class BarDiagramSceneTests
{
    static readonly (double, double, double) Up = (0, 0, 1);

    static BarDiagramInput Bar(string tag, double x1, double x2, params (double T, double V)[] profile) =>
        new(tag, (x1, 0, 0), (x2, 0, 0), profile, Up);

    static double Area(IEnumerable<BarDiagramTriangle> triangles) => triangles.Sum(t =>
    {
        var u = (X: t.B.X - t.A.X, Y: t.B.Y - t.A.Y, Z: t.B.Z - t.A.Z);
        var v = (X: t.C.X - t.A.X, Y: t.C.Y - t.A.Y, Z: t.C.Z - t.A.Z);
        var c = (u.Y * v.Z - u.Z * v.Y, u.Z * v.X - u.X * v.Z, u.X * v.Y - u.Y * v.X);
        return 0.5 * Math.Sqrt(c.Item1 * c.Item1 + c.Item2 * c.Item2 + c.Item3 * c.Item3);
    });

    [Fact]
    public void SignChange_SplitsAtZeroWithoutTwist()
    {
        // 2 → −6 на длине 4: ноль при x = 1; площади 1 и 9.
        var scene = BarDiagramScene.Build([Bar("1", 0, 4, (0, 2), (1, -6))], BarDiagramPlane.Z1, 1, 0);

        Assert.Equal(1, Area(scene.PositiveFill), 9);
        Assert.Equal(9, Area(scene.NegativeFill), 9);
        Assert.Equal([new BarDiagramLine((0, 0, 2), (1, 0, 0))], scene.PositiveOutline);
        Assert.Equal([new BarDiagramLine((1, 0, 0), (4, 0, -6))], scene.NegativeOutline);
        Assert.Equal([new BarDiagramLine((0, 0, 0), (0, 0, 2))], scene.PositiveOrdinates);
        Assert.Equal([new BarDiagramLine((4, 0, 0), (4, 0, -6))], scene.NegativeOrdinates);
        // Подписи по убыванию |значения|.
        Assert.Equal([-6.0, 2.0], scene.Labels.Select(l => l.Value));
        Assert.Equal((4.0, 0.0, -6.0), scene.Labels[0].Position);
    }

    [Fact]
    public void Ordinates_ThinnedAlongChain_KeepEndsAndExtremum()
    {
        // Парабола x(2 − x) на 20 КЭ по 0,1: ординаты — по шагу 0,7 и в экстремуме x = 1; нулевые на концах не рисуются.
        static double M(double x) => x * (2 - x);
        var bars = Enumerable.Range(0, 20)
            .Select(i => Bar((i + 1).ToString(), 0.1 * i, 0.1 * (i + 1), (0, M(0.1 * i)), (1, M(0.1 * (i + 1)))))
            .ToList();
        var scene = BarDiagramScene.Build(bars, BarDiagramPlane.Z1, 1, 0.7);

        var xs = scene.PositiveOrdinates.Select(l => Math.Round(l.A.X, 6)).ToList();
        Assert.Equal([0.7, 1.0, 1.7], xs);
        Assert.Empty(scene.NegativeOrdinates);
        // Одна цепочка — одна подпись наибольшего.
        var label = Assert.Single(scene.Labels);
        Assert.Equal(1, label.Value, 9);
        Assert.Equal("10", label.Tag);
        // Заливка — без пропусков: площадь ломаной под параболой.
        double area = Enumerable.Range(0, 20).Sum(i => 0.05 * (M(0.1 * i) + M(0.1 * (i + 1))));
        Assert.Equal(area, Area(scene.PositiveFill), 9);
    }

    [Fact]
    public void Jump_KeepsBothOrdinates()
    {
        var scene = BarDiagramScene.Build([Bar("1", 0, 1, (0, 1), (1, 1)), Bar("2", 1, 2, (0, 3), (1, 3))],
            BarDiagramPlane.Z1, 1, 10);

        Assert.Equal([(0.0, 1.0), (1.0, 1.0), (1.0, 3.0), (2.0, 3.0)],
            scene.PositiveOrdinates.Select(l => (l.A.X, l.B.Z)));
        Assert.Equal([3.0], scene.Labels.Select(l => l.Value));
    }

    [Fact]
    public void Chain_ReversedElement_FollowsAxis()
    {
        // Второй КЭ задан от x = 2 к x = 1: цепочка одна, подпись наибольшего — в x = 2.
        var scene = BarDiagramScene.Build(
            [Bar("1", 0, 1, (0, 1), (1, 1)), new BarDiagramInput("2", (2, 0, 0), (1, 0, 0), [(0, 5), (1, 1)], Up)],
            BarDiagramPlane.Z1, 1, 10);

        Assert.Equal([0.0, 2.0], scene.PositiveOrdinates.Select(l => l.A.X).Order());
        var label = Assert.Single(scene.Labels);
        Assert.Equal((2.0, 0.0, 5.0), label.Position);
    }

    [Fact]
    public void Chain_BreaksAtCorner()
    {
        var scene = BarDiagramScene.Build(
        [
            Bar("1", 0, 1, (0, 1), (1, 1)),
            new BarDiagramInput("2", (1, 0, 0), (1, 1, 0), [(0, 1), (1, 1)], Up),
        ], BarDiagramPlane.Z1, 1, 10);

        Assert.Equal(4, scene.PositiveOrdinates.Count);
        Assert.Equal(2, scene.Labels.Count);
    }

    [Fact]
    public void Labels_BelowShareOfMaximum_AreSkipped()
    {
        var scene = BarDiagramScene.Build(
        [
            Bar("1", 0, 1, (0, 100), (1, 100)),
            new BarDiagramInput("2", (0, 5, 0), (1, 5, 0), [(0, 1), (1, 1)], Up),
        ], BarDiagramPlane.Z1, 0.01, 10);

        Assert.Equal([100.0], scene.Labels.Select(l => l.Value));
        Assert.True(BarDiagramScene.Build([Bar("1", 0, 1, (0, 0), (1, 0))], BarDiagramPlane.Z1, 1, 0).IsEmpty);
    }

    [Fact]
    public void MosaicDiagramScaleAndLabels()
    {
        var vm = new PlateRebarMosaicVM();
        int changed = 0;
        vm.Changed += (_, _) => changed++;
        vm.BarDiagramScale = 2.5;
        vm.BarDiagramScale = 0;   // недопустимое — не принимается
        Assert.Equal(2.5, vm.BarDiagramScale);
        vm.ResetBarDiagramScaleCommand.Execute(null);
        Assert.Equal(1, vm.BarDiagramScale);
        vm.ShowBarDiagramLabels = false;
        Assert.Equal(3, changed);

        var c = System.Globalization.CultureInfo.CurrentCulture;
        Assert.Equal((-1235.4).ToString("0", c), PlateRebarMosaicVM.FormatDiagramValue(-1235.4));
        Assert.Equal(12.34.ToString("0.#", c), PlateRebarMosaicVM.FormatDiagramValue(12.34));
        Assert.Equal(0.123.ToString("0.##", c), PlateRebarMosaicVM.FormatDiagramValue(0.123));
    }
}
