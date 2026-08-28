using System;
using System.Globalization;
using System.IO;
using System.Linq;
using CScore;
using OpenCS.ViewModels;
using Xunit;

namespace OpenCS.Tests;

/// <summary>
/// Подвкладка «Полный момент» на графиках арматуры: по оси абсцисс — модуль результирующего
/// момента √(Mx²+My²), а не отдельная его компонента. Подвкладки Mx/My остаются как были.
/// </summary>
public sealed class MomentCurvatureRebarTotalMomentTests
{
    const double BarY = -0.16;
    const double Es = 200_000_000.0;

    static CrossSection BarSection()
    {
        var steel = new Material
        {
            Id = 1, Tag = "A400", Type = MatType.ReSteelF, E = Es,
            MaterialChars = new[] { CalcType.C, CalcType.CL, CalcType.N, CalcType.NL }
                .Select(c => new MaterialChars
                {
                    Type = MatType.ReSteelF, TypeCalc = c,
                    Fc = -350_000.0, Ft = 350_000.0, Ry = 350_000.0, E = Es, Ec2 = -0.0035, Et2 = 0.025
                })
                .ToList()
        };
        var bar = Fiber.CreatePoint(0.014, 0.0, BarY);
        bar.Area = 0.0001;
        var area = new MaterialArea
        {
            Id = 1, Tag = "Стержень", Category = AreaCategory.RebarGroup,
            Material = steel, MaterialId = steel.Id, DiagrammType = DiagrammType.L2
        };
        area.Fibers.Add(bar);
        return new CrossSection { Id = 1, Areas = [area] };
    }

    static string Ky(double eps) => (eps / BarY).ToString("R", CultureInfo.InvariantCulture);

    /// <summary>Точки подобраны так, чтобы √(Mx²+My²) считался «на пальцах»: 5, 25 и 50.</summary>
    static MomentCurvatureBiaxialResultVM BuildVM()
    {
        string json = $$"""
        {
          "has_mx": true, "has_my": true, "use_psi": false,
          "points": [
            {"mx": -3, "my":  4, "e0": 0, "ky": {{Ky(0.0002)}}, "kz": 0, "segment": 1, "converged": true},
            {"mx": -15, "my": 20, "e0": 0, "ky": {{Ky(0.0004)}}, "kz": 0, "segment": 3, "converged": true},
            {"mx": -30, "my": 40, "e0": 0, "ky": {{Ky(0.0010)}}, "kz": 0, "segment": 3, "converged": true}
          ]
        }
        """;
        return new MomentCurvatureBiaxialResultVM(
            new CalcResult { Status = "ok", DataJson = json }, BarSection(), CalcType.C);
    }

    [Fact]
    public void TotalAxis_UsesTheResultantMoment()
    {
        var vm = BuildVM();

        var total = vm.BuildRebarSeries(vm.RebarOptions[0], RebarMomentAxis.Total)!;

        Assert.Equal(new[] { 5.0, 25.0, 50.0 }, total.MomentEps);
        Assert.Equal(new[] { 5.0, 25.0, 50.0 }, total.MomentSigma);
    }

    [Fact]
    public void ComponentAxes_KeepTheirOwnComponent()
    {
        var vm = BuildVM();

        var mx = vm.BuildRebarSeries(vm.RebarOptions[0], RebarMomentAxis.Mx)!;
        var my = vm.BuildRebarSeries(vm.RebarOptions[0], RebarMomentAxis.My)!;

        Assert.Equal(new[] { 3.0, 15.0, 30.0 }, mx.MomentEps);
        Assert.Equal(new[] { 4.0, 20.0, 40.0 }, my.MomentEps);
    }

    [Fact]
    public void ControlMarker_UsesTheSameAxis()
    {
        var vm = BuildVM();
        var point = vm.Rows[2];

        var total = vm.RebarValueAt(vm.RebarOptions[0], point, RebarMomentAxis.Total);
        var mx = vm.RebarValueAt(vm.RebarOptions[0], point, RebarMomentAxis.Mx);

        Assert.NotNull(total);
        Assert.Equal(50.0, total!.Value.momentAbs, 9);
        Assert.Equal(30.0, mx!.Value.momentAbs, 9);
    }

    /// <summary>Подвкладка «Полный момент» обязана идти ПЕРЕД Mx/My — тогда она и выбрана по умолчанию.</summary>
    [Fact]
    public void TotalSubTab_ComesBeforeComponentsAndIsLocalized()
    {
        string root = FindWorkspaceRoot();
        string view = File.ReadAllText(Path.Combine(root, "OpenCS", "Views", "MomentCurvatureBiaxialResultView.xaml"));
        string ru = File.ReadAllText(Path.Combine(root, "OpenCS", "Resources", "Strings.ru-RU.xaml"));
        string en = File.ReadAllText(Path.Combine(root, "OpenCS", "Resources", "Strings.en-US.xaml"));

        // Обе вкладки арматуры (деформации и напряжения) получают подвкладку.
        Assert.Equal(2, CountOccurrences(view, "MomentCurvature_SubTabTotal"));

        int firstTotal = view.IndexOf("MomentCurvature_SubTabTotal", StringComparison.Ordinal);
        int firstMx = view.IndexOf("MomentCurvature_SubTabMx", StringComparison.Ordinal);
        Assert.True(firstTotal >= 0 && firstTotal < firstMx,
            "подвкладка «Полный момент» должна объявляться раньше подвкладки Mx");

        foreach (var key in new[] { "MomentCurvature_SubTabTotal", "MomentCurvature_AxisMomentTotal" })
        {
            Assert.Contains(key, ru);
            Assert.Contains(key, en);
        }
    }

    static int CountOccurrences(string text, string value)
    {
        int count = 0, index = 0;
        while ((index = text.IndexOf(value, index, StringComparison.Ordinal)) >= 0)
        {
            count++;
            index += value.Length;
        }
        return count;
    }

    static string FindWorkspaceRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "OpenCS.sln")))
                return directory.FullName;
            directory = directory.Parent;
        }
        throw new DirectoryNotFoundException("OpenCS.sln не найден");
    }
}
