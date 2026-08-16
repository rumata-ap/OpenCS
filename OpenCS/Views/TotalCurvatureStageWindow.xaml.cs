using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using CScore;
using OpenCS.Services;
using OpenCS.Utilites;
using OpenCS.ViewModels;

namespace OpenCS.Views;

/// <summary>Модальное окно графических полей одной стадии полной кривизны.</summary>
public partial class TotalCurvatureStageWindow : Window
{
    SectionCutWindowService? _cutWindow;

    public TotalCurvatureStageWindow(
        CrossSection section, TotalCurvatureStageVM stage,
        AppViewModel app, CalcTask task)
    {
        InitializeComponent();

        Title = $"{stage.Label} — {Loc.S("TotalCurvature_StagePlotTitle")}";
        section.ResolveAndBuildDiagramms(app.CalcSettings.Sp63DescEtaMin,
            pool: app.Diagrams,
            rebarDifferentialDiagram: app.CalcSettings.RebarDifferentialDiagram);
        section.SetEps(stage.Plane, stage.CalcType, stage.ConcreteTension);

        string? RebarTooltip(double xM, double yM)
        {
            var nearest = stage.PsiSByRebar
                .Select(value => (Value: value,
                    Distance: Math.Sqrt((value.X - xM) * (value.X - xM)
                        + (value.Y - yM) * (value.Y - yM))))
                .OrderBy(item => item.Distance)
                .FirstOrDefault();
            if (nearest.Value == null || nearest.Distance > 0.001)
                return Loc.S("TotalCurvature_PsiSNotApplicable");

            return nearest.Value.Applicable
                ? string.Format(Loc.S("TotalCurvature_PsiSFormat"), nearest.Value.PsiS)
                : Loc.S("TotalCurvature_PsiSNotApplicable");
        }

        var settings = app.CalcSettings;
        var stressVm = new SectionPlotVM(section, stage.Plane, stage.CalcType,
            SectionPlotMode.Stress, settings, stage.ConcreteTension,
            extraRebarTooltip: RebarTooltip);
        var strainVm = new SectionPlotVM(section, stage.Plane, stage.CalcType,
            SectionPlotMode.Strain, settings, stage.ConcreteTension,
            extraRebarTooltip: RebarTooltip);

        var cutVm = new SectionCutVM(section, stage.Plane, stage.CalcType,
            app.FileDialogService, stage.ConcreteTension)
        {
            WindowTitleSuffix = $"{task.Tag} — {stage.Label} — {section.Tag}"
        };
        stressVm.CutVM = cutVm;
        strainVm.CutVM = cutVm;

        StressView.DataContext = stressVm;
        StrainView.DataContext = strainVm;

        _cutWindow = new SectionCutWindowService(settings);
        _cutWindow.Bind(cutVm, SectionPlotMode.Stress);
        MainTabs.SelectionChanged += OnTabSelectionChanged;
        Closed += (_, _) => _cutWindow?.Dispose();
    }

    void OnTabSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (MainTabs.SelectedIndex == 0)
            _cutWindow?.UpdatePlotMode(SectionPlotMode.Stress);
        else if (MainTabs.SelectedIndex == 1)
            _cutWindow?.UpdatePlotMode(SectionPlotMode.Strain);
    }
}
