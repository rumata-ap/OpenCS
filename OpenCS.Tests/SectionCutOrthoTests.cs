using CScore;
using OpenCS.ViewModels;
using Xunit;

namespace OpenCS.Tests;

/// <summary>Ортогональный разрез с Shift: привязка точек «Свободного» режима и оси в режиме «По градиенту».</summary>
public class SectionCutOrthoTests
{
    static CrossSection Rectangle(double w, double h)
    {
        MaterialChars Ch(CalcType ct) => new(ct)
        {
            E = 30_000_000, Fc = -14_500, Ft = 1_050, Ec2 = -0.0035,
            Ec1Red = -0.0015, Et1Red = 0.00008, Et2 = 0.00015, Type = MatType.Concrete
        };
        var m = new Material { Id = 1, Tag = "B25", Type = MatType.Concrete, E = 30_000_000 };
        m.MaterialChars = [Ch(CalcType.C), Ch(CalcType.CL), Ch(CalcType.N), Ch(CalcType.NL)];
        double x0 = -w / 2, x1 = w / 2, y0 = -h / 2, y1 = h / 2;
        var hull = new Contour(new[] { x0, x1, x1, x0, x0 }, new[] { y0, y0, y1, y1, y0 }, "r")
        { Type = ContourType.Hull };
        var area = new MaterialArea
        {
            Id = 1, Tag = "Бетон", Category = AreaCategory.Region, Material = m, MaterialId = 1,
            DiagrammType = DiagrammType.L2, Contours = [hull], NX = 5, NY = 5
        };
        area.Hull = hull;
        area.ResolveAndBuildDiagramms();
        area.SliceXY(5, 5);
        return new CrossSection { Tag = "rect", Areas = [area] };
    }

    [Theory]
    [InlineData(0.10, 0.03, 0.10, 0.0)]   // больше по X — горизонталь
    [InlineData(0.02, -0.09, 0.0, -0.09)] // больше по Y — вертикаль
    public void SnapOrtho_KeepsLargerDelta(double px, double py, double ex, double ey)
    {
        var s = SectionCutVM.SnapOrtho((0.0, 0.0), (px, py));
        Assert.Equal(ex, s.X, 9);
        Assert.Equal(ey, s.Y, 9);
    }

    [Fact]
    public void FreeMode_ShiftSecondPoint_GivesHorizontalCut()
    {
        var vm = new SectionCutVM(Rectangle(0.3, 0.5), new Kurvature { ky = 0.001 }, CalcType.C,
            new NullFileDialogService()) { Mode = CutMode.Free };

        vm.AddPoint((-0.1, 0.05));
        vm.AddPoint((0.1, 0.08), ortho: true);

        Assert.NotNull(vm.Result);
        Assert.Equal(0.05, vm.Result!.Start.Y, 9);
        Assert.Equal(0.05, vm.Result.End.Y, 9);
    }

    [Fact]
    public void GradientMode_Shift_SnapsObliqueGradientToNearestAxis()
    {
        // Градиент наклонный (ky преобладает) — без Shift разрез косой, с Shift вертикальный.
        var vm = new SectionCutVM(Rectangle(0.3, 0.5), new Kurvature { ky = 0.001, kz = 0.0004 }, CalcType.C,
            new NullFileDialogService());

        vm.AddPoint((0.05, 0.0));
        Assert.NotEqual(vm.Result!.Start.X, vm.Result.End.X, 3);

        vm.AddPoint((0.05, 0.0), ortho: true);
        Assert.Equal(0.05, vm.Result!.Start.X, 9);
        Assert.Equal(0.05, vm.Result.End.X, 9);
        Assert.True(vm.Result.Start.Y > vm.Result.End.Y); // сверху вниз
    }
}
