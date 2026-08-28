using System;
using System.Globalization;
using System.IO;
using System.Linq;
using CScore;
using OpenCS.ViewModels;
using Xunit;

namespace OpenCS.Tests;

/// <summary>
/// Режим отрисовки деформаций/напряжений арматуры при учёте ψs в равновесии:
/// средняя деформация / деформация в трещине по диаграмме / буквальное σ(εs)/ψs.
/// </summary>
public sealed class MomentCurvatureRebarStressModeTests
{
    const double BarY = -0.16;
    const double Rs = 350_000.0;          // кПа
    const double Es = 200_000_000.0;      // кПа
    const double EpsCrc = 0.0012;         // деформация стержня в точке 2
    const double Offset = 0.8 * EpsCrc;   // εs,crc = εs + 0,8·εcrc

    static CrossSection BarSection()
    {
        var steel = new Material
        {
            Id = 1,
            Tag = "A400",
            Type = MatType.ReSteelF,
            E = Es,
            MaterialChars = new[] { CalcType.C, CalcType.CL, CalcType.N, CalcType.NL }
                .Select(c => new MaterialChars
                {
                    Type = MatType.ReSteelF, TypeCalc = c,
                    Fc = -Rs, Ft = Rs, Ry = Rs, E = Es, Ec2 = -0.0035, Et2 = 0.025
                })
                .ToList()
        };
        var bar = Fiber.CreatePoint(0.014, 0.0, BarY);
        bar.Area = 0.0001;
        var area = new MaterialArea
        {
            Id = 1,
            Tag = "Стержень",
            Category = AreaCategory.RebarGroup,
            Material = steel,
            MaterialId = steel.Id,
            DiagrammType = DiagrammType.L2
        };
        area.Fibers.Add(bar);
        return new CrossSection { Id = 1, Areas = [area] };
    }

    /// <summary>Кривизна, дающая на стержне заданную деформацию при e0 = 0.
    /// Форматируется инвариантно — иначе в JSON попадёт десятичная запятая.</summary>
    static string KyFor(double eps) => (eps / BarY).ToString("R", CultureInfo.InvariantCulture);

    static MomentCurvatureBiaxialResultVM BuildVM()
    {
        string json = $$"""
        {
          "has_mx": true, "has_my": false, "use_psi": true,
          "crack_transition": {"mx": 15, "my": 0, "e0": 0.0004, "ky": -0.005, "kz": 0, "segment": 3, "converged": true},
          "points": [
            {"mx": 10, "my": 0, "e0": 0, "ky": {{KyFor(0.0002)}}, "kz": 0, "segment": 1, "converged": true, "psi_active": false},
            {"mx": 20, "my": 0, "e0": 0, "ky": {{KyFor(0.0004)}}, "kz": 0, "segment": 3, "converged": true, "psi_active": true},
            {"mx": 30, "my": 0, "e0": 0, "ky": {{KyFor(0.0030)}}, "kz": 0, "segment": 3, "converged": true, "psi_active": true}
          ]
        }
        """;
        var result = new CalcResult { Status = "ok", DataJson = json };
        return new MomentCurvatureBiaxialResultVM(result, BarSection(), CalcType.C);
    }

    static double[] Sigma(MomentCurvatureBiaxialResultVM vm, RebarStressMode mode)
    {
        vm.SelectedRebarStressMode = vm.RebarStressModes.First(m => m.Mode == mode);
        return vm.BuildRebarSeries(vm.RebarOptions[0], RebarMomentAxis.Mx)!.Sigma;
    }

    static double[] Strain(MomentCurvatureBiaxialResultVM vm, RebarStressMode mode)
    {
        vm.SelectedRebarStressMode = vm.RebarStressModes.First(m => m.Mode == mode);
        return vm.BuildRebarSeries(vm.RebarOptions[0], RebarMomentAxis.Mx)!.Eps;
    }

    [Fact]
    public void RebarStressModes_ExposeThreeOptionsAndDefaultToAverageStrain()
    {
        var vm = BuildVM();

        Assert.Equal(3, vm.RebarStressModes.Count);
        Assert.Equal(
            new[] { RebarStressMode.AverageStrain, RebarStressMode.CrackStrain, RebarStressMode.DividedByPsi },
            vm.RebarStressModes.Select(m => m.Mode));
        Assert.Equal(RebarStressMode.AverageStrain, vm.SelectedRebarStressMode.Mode);
        Assert.All(vm.RebarStressModes, m => Assert.False(string.IsNullOrWhiteSpace(m.Label)));
    }

    [Fact]
    public void AverageStrainMode_KeepsPlainDiagramStressAtPlaneStrain()
    {
        var sigma = Sigma(BuildVM(), RebarStressMode.AverageStrain);

        Assert.Equal(0.0002 * Es / 1000.0, sigma[0], 6);
        Assert.Equal(0.0004 * Es / 1000.0, sigma[1], 6);
        Assert.Equal(Rs / 1000.0, sigma[2], 6);   // 0,003 > Ft/E — площадка текучести
    }

    [Fact]
    public void ElasticPoint_CrackStrainAndDividedByPsiAgree()
    {
        // Тождество σ(εs)/ψs ≡ σ(εs + 0,8·εcrc) на линейной ветви диаграммы.
        var crack = Sigma(BuildVM(), RebarStressMode.CrackStrain);
        var divided = Sigma(BuildVM(), RebarStressMode.DividedByPsi);

        Assert.Equal((0.0004 + Offset) * Es / 1000.0, crack[1], 6);
        Assert.Equal(crack[1], divided[1], 6);
    }

    [Fact]
    public void YieldedPoint_CrackStrainClampsAtRsWhileDividedByPsiExceedsIt()
    {
        var crack = Sigma(BuildVM(), RebarStressMode.CrackStrain);
        var divided = Sigma(BuildVM(), RebarStressMode.DividedByPsi);

        double psi = 1.0 / (1.0 + 0.8 * EpsCrc / 0.0030);
        Assert.Equal(Rs / 1000.0, crack[2], 6);
        Assert.Equal(Rs / 1000.0 / psi, divided[2], 6);
        Assert.True(divided[2] > Rs / 1000.0 + 1.0, "буквальная формула обязана уходить выше Rs");
    }

    [Fact]
    public void PointWithoutPsi_IsIdenticalInEveryMode()
    {
        // Уч. 1 (до трещины): ψs не применяется ни в равновесии, ни на графике.
        var average = Sigma(BuildVM(), RebarStressMode.AverageStrain);
        var crack = Sigma(BuildVM(), RebarStressMode.CrackStrain);
        var divided = Sigma(BuildVM(), RebarStressMode.DividedByPsi);

        Assert.Equal(average[0], crack[0], 9);
        Assert.Equal(average[0], divided[0], 9);
    }

    [Fact]
    public void StrainSeries_FollowsModeOnBothPsiModes()
    {
        var average = Strain(BuildVM(), RebarStressMode.AverageStrain);
        var crack = Strain(BuildVM(), RebarStressMode.CrackStrain);
        var divided = Strain(BuildVM(), RebarStressMode.DividedByPsi);

        AssertSeries([0.0002, 0.0004, 0.0030], average);
        AssertSeries([0.0002, 0.0004 + Offset, 0.0030 + Offset], crack);
        AssertSeries(crack, divided);
    }

    static void AssertSeries(double[] expected, double[] actual)
    {
        Assert.Equal(expected.Length, actual.Length);
        for (int i = 0; i < expected.Length; i++)
            Assert.Equal(expected[i], actual[i], 9);
    }

    [Fact]
    public void ModeSelector_IsBoundInViewAndLocalized()
    {
        string root = FindWorkspaceRoot();
        string view = File.ReadAllText(Path.Combine(root, "OpenCS", "Views", "MomentCurvatureBiaxialResultView.xaml"));
        string viewCode = File.ReadAllText(Path.Combine(root, "OpenCS", "Views", "MomentCurvatureBiaxialResultView.xaml.cs"));
        string ru = File.ReadAllText(Path.Combine(root, "OpenCS", "Resources", "Strings.ru-RU.xaml"));
        string en = File.ReadAllText(Path.Combine(root, "OpenCS", "Resources", "Strings.en-US.xaml"));

        // Один выбор на обе вкладки — два ComboBox на одно свойство VM.
        Assert.Equal(2, CountOccurrences(view, "{Binding SelectedRebarStressMode"));
        Assert.Equal(2, CountOccurrences(view, "{Binding RebarStressModes}"));
        Assert.Contains("MomentCurvature_RebarStressModeHeader", view);
        Assert.Contains("SelectedRebarStressMode", viewCode);

        foreach (var key in new[]
        {
            "MomentCurvature_RebarStressModeHeader",
            "MomentCurvature_RebarStressModeAverage",
            "MomentCurvature_RebarStressModeCrack",
            "MomentCurvature_RebarStressModePsi"
        })
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
