using CScore;
using CScore.History;
using OpenCS.Tasks;
using OpenCS.Utilites;
using OpenCS.ViewModels;
using Xunit;

namespace OpenCS.Tests;

/// <summary>Модель окна результата load_history на реальном прогоне задачи.</summary>
public sealed class LoadHistoryResultVMTests
{
    /// <summary>Балка ReportFixtures.BuildBeam — часть 0, её бетон, сдвинутый вверх, — часть 1.</summary>
    static StagedSection Section()
    {
        var beam = ReportFixtures.BuildBeam();
        var top = ReportFixtures.BuildBeam().Areas[0];
        double h = top.Hull!.Y.Max() - top.Hull.Y.Min();
        top.Hull = new Contour(top.Hull.X.ToArray(), top.Hull.Y.Select(y => y + h).ToArray(), "outer");
        top.SetWKT();
        var s = new StagedSection { Id = 5, Tag = "усиление",
            Parts = [new SectionPart { Index = 0, Tag = "балка", Areas = beam.Areas },
                     new SectionPart { Index = 1, Tag = "наращивание", Areas = [top] }] };
        s.SyncAreas();
        return s;
    }

    static (CalcResult Result, StagedSection Section) Run()
    {
        var p = new LoadHistoryParams
        {
            Stages =
            [
                new() { Tag = "1", Steps = 3, Force = new StageForce { Mode = "manual", Mx = -60 } },
                new() { Tag = "2", ActivatePart = 1, Steps = 3, Force = new StageForce { Mode = "manual", Mx = -90 } },
                new() { Tag = "р", Kind = StageKind.Failure, Force = new StageForce { Mode = "manual", Mx = -1 } },
            ],
            Failure = new FailureOptions { Steps = 20 },
        };
        var s = Section();
        var r = TaskRunner.Run(new CalcTask { Id = 1, Kind = "load_history", CalcType = CalcType.C, SectionId = 5, ParamsJson = p.ToJson() },
            s, new LoadItem(), CalcSettings.Default);
        s.ResolveAndBuildDiagramms(0.85, rebarDifferentialDiagram: false);
        return (r, s);
    }

    [Fact]
    public void Rows_Curves_AndFieldsOfEveryStep()
    {
        var (result, section) = Run();
        var vm = new LoadHistoryResultVM(result, section, CalcType.C, CalcSettings.Default);

        Assert.NotEmpty(vm.Rows);
        Assert.True(vm.HasNoHistory);
        Assert.Equal(vm.Rows.Count + 1, vm.Curves.WithHistory.Count);
        Assert.Single(vm.Curves.Activations);
        Assert.True(vm.Rows[0].IsStageStart);
        Assert.True(vm.Rows[2].IsStageEnd);
        Assert.Contains("M_peak", vm.SummaryText);
        Assert.True(vm.Curves.PeakWith!.M > 0 && vm.Curves.PeakWith.C > 0); // Mx < 0 растягивает низ: проекции на u положительны

        foreach (var row in new[] { vm.Rows[0], vm.Rows[4], vm.Rows[^1] })
        {
            vm.SelectedRow = row;
            Assert.NotNull(vm.StressPlot);
            Assert.NotNull(vm.StrainPlot);
        }
        vm.FieldsRelative = false;
        Assert.NotNull(vm.StrainPlot);

        vm.SelectStep(noHistory: true, 0);
        Assert.True(vm.ShowNoHistory);
        Assert.Same(vm.RowsNoHistory[0], vm.SelectedRow);
    }

    [Fact]
    public void NoSection_FieldsEmpty()
    {
        var (result, _) = Run();
        var vm = new LoadHistoryResultVM(result, null, CalcType.C, CalcSettings.Default);
        Assert.False(vm.HasSection);
        Assert.Null(vm.StressPlot);
    }
}
