using CScore;
using OpenCS.Services;
using OpenCS.ViewModels;
using System.Linq;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;

namespace OpenCS.Views;

public partial class LimitForceResultView : UserControl
{
    SectionCutWindowService? _cutWindow;

    public LimitForceResultView(CalcResult result, AppViewModel app, CalcTask task)
    {
        InitializeComponent();

        var section = app.CrossSections.FirstOrDefault(s => s.Id == task.SectionId);
        if (section == null)
            return;

        section.ResolveAndBuildDiagramms(app.CalcSettings.Sp63DescEtaMin, pool: app.Diagrams,
            rebarDifferentialDiagram: app.CalcSettings.RebarDifferentialDiagram);
        var settings = app.CalcSettings;
        bool ten = settings.ResolveConcreteTension(task.CalcType);
        var k = ParseKurvature(result.DataJson);
        section.SetEps(k, task.CalcType, ten);

        var summaryVm = new LimitForceSummaryVM(result, section, task.CalcType, settings);
        SummaryView.DataContext = summaryVm;

        var stressVm = new SectionPlotVM(section, k, task.CalcType, SectionPlotMode.Stress, settings, ten);
        var strainVm = new SectionPlotVM(section, k, task.CalcType, SectionPlotMode.Strain, settings, ten);

        var cutVm = new SectionCutVM(section, k, task.CalcType, app.FileDialogService)
        {
            EpsCu = summaryVm.EpsCu,
            WindowTitleSuffix = $"{task.Tag} — {section.Tag}"
        };
        stressVm.CutVM = cutVm;
        strainVm.CutVM = cutVm;

        StressView.DataContext = stressVm;
        StrainView.DataContext = strainVm;

        _cutWindow = new SectionCutWindowService(settings);
        _cutWindow.Bind(cutVm, SectionPlotMode.Stress);
        MainTabs.SelectionChanged += OnTabSelectionChanged;
        Unloaded += (_, _) => _cutWindow?.Dispose();
    }

    void OnTabSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (MainTabs.SelectedIndex == 1) _cutWindow?.UpdatePlotMode(SectionPlotMode.Stress);
        else if (MainTabs.SelectedIndex == 2) _cutWindow?.UpdatePlotMode(SectionPlotMode.Strain);
    }

    static Kurvature ParseKurvature(string dataJson)
    {
        try
        {
            if (string.IsNullOrEmpty(dataJson)) return new Kurvature();
            var doc  = JsonDocument.Parse(dataJson);
            var root = doc.RootElement;
            return new Kurvature
            {
                e0 = root.TryGetProperty("e0", out var v) ? v.GetDouble() : 0,
                ky = root.TryGetProperty("ky", out v)     ? v.GetDouble() : 0,
                kz = root.TryGetProperty("kz", out v)     ? v.GetDouble() : 0,
            };
        }
        catch { return new Kurvature(); }
    }
}
