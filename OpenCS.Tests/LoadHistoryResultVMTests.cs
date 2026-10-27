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

    internal static (CalcResult Result, StagedSection Section) Run()
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

    /// <summary>Ползунок вкладки «Поля»: позиция — индекс шага текущего пути, дробная округляется,
    /// отметки — начала этапов, переключение пути меняет диапазон.</summary>
    [Fact]
    public void StepSlider_SelectsStepOfCurrentPath()
    {
        var (result, section) = Run();
        var vm = new LoadHistoryResultVM(result, section, CalcType.C, CalcSettings.Default);

        Assert.Equal(vm.Rows.Count - 1, vm.MaxIndex);
        Assert.Equal(vm.Rows.Count - 1, vm.StepPosition); // по умолчанию — последний шаг
        Assert.Equal([0.0, 3.0, 6.0], vm.StageTicks);    // этапы 1, 2 и разрушение

        vm.StepPosition = 3.6;
        Assert.Same(vm.Rows[4], vm.SelectedRow);
        Assert.Equal(4, vm.StepPosition);
        Assert.NotNull(vm.StressPlot);
        Assert.NotEmpty(vm.SelectedStepText);

        vm.ShowNoHistory = true;
        Assert.Equal(vm.RowsNoHistory.Count - 1, vm.MaxIndex);
        vm.StepPosition = 1;
        Assert.Same(vm.RowsNoHistory[1], vm.SelectedRow);
    }

    [Fact]
    public void Hysteresis_SummaryResidualsAndReplayedFields()
    {
        var p = new LoadHistoryParams
        {
            Stages =
            [
                new() { Tag = "1", Steps = 3, Force = new StageForce { Mode = "manual", Mx = -60 } },
                new() { Tag = "2", ActivatePart = 1, Steps = 3, Force = new StageForce { Mode = "manual", Mx = -110 } },
                new() { Tag = "3", Steps = 3, Force = new StageForce { Mode = "manual", Mx = -20 } },
            ],
            Hysteresis = new CScore.Hysteresis.HysteresisOptions(),
        };
        var section = Section();
        var settings = CalcSettings.Default;
        var result = TaskRunner.Run(new CalcTask { Id = 1, Kind = "load_history", CalcType = CalcType.C, SectionId = 5, ParamsJson = p.ToJson() },
            section, new LoadItem(), settings);
        section.ResolveAndBuildDiagramms(settings.Sp63DescEtaMin, rebarDifferentialDiagram: settings.RebarDifferentialDiagram,
            ekbEtaMin: settings.EkbDescEtaMin);
        var vm = new LoadHistoryResultVM(result, section, CalcType.C, settings);

        Assert.Equal("ok", result.Status);
        Assert.NotNull(vm.HysteresisText);
        Assert.Contains(vm.HysteresisText!, vm.SummaryText);
        Assert.False(vm.ShowUnloadNote);
        var last = vm.Rows[^1];
        Assert.True(last.ResS > 0 || last.ResB > 0);
        vm.SelectedRow = last;
        Assert.NotNull(vm.StressPlot);
        Assert.NotNull(vm.StrainPlot);

        // σ волокон после воспроизведения пути отличаются от нелинейно-упругих на той же плоскости
        var d = vm.Data;
        var replay = OpenCS.LoadHistory.LoadHistoryReplay.Section(section, d, CalcType.C, false, last.Index);
        replay.SetEps(last.K, CalcType.C, d.ConcreteTension);
        var plain = (StagedSection)section.CloneForCalc();
        plain.ResetHistory();
        plain.StrainMode = StageStrainMode.Fibers;
        plain.PrepareFiberMode();
        var a1 = d.ActivationPlanes[1];
        plain.ActivatePart(1, new Kurvature { e0 = a1[0], ky = a1[1], kz = a1[2] });
        plain.SetEps(last.K, CalcType.C, d.ConcreteTension);
        var fr = replay.Parts.SelectMany(x => x.Areas).SelectMany(x => x.Fibers).ToList();
        var fp = plain.Parts.SelectMany(x => x.Areas).SelectMany(x => x.Fibers).ToList();
        Assert.Equal(fp.Count, fr.Count);
        Assert.True(fr.Zip(fp).Max(t => Math.Abs(t.First.Sig - t.Second.Sig)) > 100, "σ после разгрузки не отличаются");
    }

    [Fact]
    public void NoSection_FieldsEmpty()
    {
        var (result, _) = Run();
        var vm = new LoadHistoryResultVM(result, null, CalcType.C, CalcSettings.Default);
        Assert.False(vm.HasSection);
        Assert.Null(vm.StressPlot);
    }

    [Fact]
    public void FieldPlots_HaveSectionCutTool()
    {
        var (result, section) = Run();
        var vm = new LoadHistoryResultVM(result, section, CalcType.C, CalcSettings.Default, new NullFileDialogService());
        Assert.NotNull(vm.StressPlot!.CutVM);
        Assert.Same(vm.StressPlot.CutVM, vm.StrainPlot!.CutVM);
        vm.FieldsRelative = false;
        Assert.NotNull(vm.StrainPlot!.CutVM);
        Assert.Null(new LoadHistoryResultVM(result, section, CalcType.C, CalcSettings.Default).StressPlot!.CutVM);
    }
}
