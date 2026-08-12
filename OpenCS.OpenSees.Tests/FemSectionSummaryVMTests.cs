using CScore;
using OpenCS.OpenSees.CScore;
using OpenCS.Utilites;
using OpenCS.ViewModels;

namespace OpenCS.OpenSees.Tests;

/// <summary>Проверки сводки состояния сечения по записанным волокнам.</summary>
public sealed class FemSectionSummaryVMTests
{
    static (FemSectionStateRequest Request, FemRecordedSectionSummary Summary) Build()
    {
        var request = new FemSectionStateRequest(
            new FemSectionLocationRow("M5", 10, 1, 3, 1.5, 3.0, 0.5, 0.5, true),
            42,
            "C",
            new Dictionary<int, (double StressPa, double Strain)>
            {
                [0] = (1_500_000.0, 0.0005),
            },
            "Шаг 7/10, λ=0.9 (сошёлся)",
            true,
            "1.5 м (50%)");
        var summary = new FemRecordedSectionSummary(
            new Kurvature { e0 = 0.001, ky = 0.0002, kz = 0.0003 },
            -12_345.6, 67.8, -9.1, 0.0002, 0.0011,
            [new FemRebarStateRow(1, -0.1, -0.2, 0.0009, 180.0)]);
        return (request, summary);
    }

    [Fact]
    public void Ctor_FormatsHeaderForcesAndRebar()
    {
        var (request, summary) = Build();

        var vm = new FemSectionSummaryVM(request, summary);

        // Вне приложения Loc.S возвращает сам ключ — поэтому сравниваем с тем же вызовом Loc.S.
        Assert.Equal(string.Format(Loc.S("FemSectionStateSummaryTitle"), "M5", 3), vm.TaskTag);
        Assert.Equal("Шаг 7/10, λ=0.9 (сошёлся)", vm.StatusText);
        Assert.StartsWith($"{(-12_345.6 / 1000):+0.000;-0.000}", vm.NText);
        Assert.StartsWith($"{(67.8 / 1000):+0.000;-0.000}", vm.MxText);
        Assert.True(vm.HasExtremes);
        Assert.True(vm.HasRebar);
        var row = Assert.Single(vm.RebarRows);
        Assert.Equal(1, row.Num);
        Assert.Equal($"{180.0:+0.0;-0.0}", row.Sigma);
        Assert.False(vm.EtaEnabled);
        Assert.False(vm.HasStiffness);
        Assert.False(vm.ShowConvergence);
    }

    [Fact]
    public void Ctor_EmptyRecorded_HidesExtremesAndRebar()
    {
        var (request, _) = Build();
        var empty = new FemSectionStateRequest(request.Location, request.SectionId,
            request.CalcTypeName, new Dictionary<int, (double StressPa, double Strain)>(),
            request.StepLabel, request.Converged, request.PositionLabel);
        var summary = new FemRecordedSectionSummary(
            new Kurvature(), 0, 0, 0, 0, 0, []);

        var vm = new FemSectionSummaryVM(empty, summary);

        Assert.False(vm.HasExtremes);
        Assert.False(vm.HasRebar);
    }
}
