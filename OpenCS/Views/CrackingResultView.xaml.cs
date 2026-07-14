using System.Linq;
using System.Text.Json;
using System.Windows.Controls;
using CScore;
using OpenCS.Services;
using OpenCS.Utilites;
using OpenCS.ViewModels;

namespace OpenCS.Views;

public partial class CrackingResultView : UserControl
{
    SectionCutWindowService? _cutWindow;

    public CrackingResultView(CalcResult result, AppViewModel app, CalcTask task)
    {
        InitializeComponent();

        SummaryView.DataContext = new CrackingSummaryVM(result);

        var section = app.CrossSections.FirstOrDefault(s => s.Id == task.SectionId);
        var k = ParsePlane(result.DataJson);
        if (section == null || k == null)
        {
            MainTabs.Items.RemoveAt(2);
            MainTabs.Items.RemoveAt(1);
            return;
        }

        section.ResolveAndBuildDiagramms(app.CalcSettings.Sp63DescEtaMin, pool: app.Diagrams,
            rebarDifferentialDiagram: app.CalcSettings.RebarDifferentialDiagram);
        section.SetEps(k.Value, CalcType.CL, ten: true);

        var settings = app.CalcSettings;
        var stressVm = new SectionPlotVM(section, k.Value, CalcType.CL, SectionPlotMode.Stress, settings, ten: true);
        var strainVm = new SectionPlotVM(section, k.Value, CalcType.CL, SectionPlotMode.Strain, settings, ten: true);

        var cutVm = new SectionCutVM(section, k.Value, CalcType.CL, app.FileDialogService, ten: true)
        {
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

    static Kurvature? ParsePlane(string dataJson)
    {
        try
        {
            if (string.IsNullOrEmpty(dataJson)) return null;
            var doc = JsonDocument.Parse(dataJson);
            var root = doc.RootElement;
            if (!root.TryGetProperty("plane_converged", out var pcEl) || !pcEl.GetBoolean()) return null;
            if (!root.TryGetProperty("e0", out var e0El) || e0El.ValueKind != JsonValueKind.Number) return null;
            if (!root.TryGetProperty("ky", out var kyEl) || kyEl.ValueKind != JsonValueKind.Number) return null;
            if (!root.TryGetProperty("kz", out var kzEl) || kzEl.ValueKind != JsonValueKind.Number) return null;
            return new Kurvature { e0 = e0El.GetDouble(), ky = kyEl.GetDouble(), kz = kzEl.GetDouble() };
        }
        catch { return null; }
    }
}
