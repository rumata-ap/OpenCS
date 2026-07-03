using CScore;
using OpenCS.ViewModels;
using System.Linq;
using System.Text.Json;
using System.Windows.Controls;

namespace OpenCS.Views;

public partial class LimitForceResultView : UserControl
{
    public LimitForceResultView(CalcResult result, AppViewModel app, CalcTask task)
    {
        InitializeComponent();

        var section = app.CrossSections.FirstOrDefault(s => s.Id == task.SectionId);
        if (section == null)
            return;

        section.ResolveAndBuildDiagramms(app.CalcSettings.Sp63DescEtaMin, pool: app.Diagrams);
        var k = ParseKurvature(result.DataJson);
        section.SetEps(k, task.CalcType);

        SummaryView.DataContext = new LimitForceSummaryVM(
            result, section, task.CalcType, app.CalcSettings.GridDensity);

        var settings = app.CalcSettings;
        StressView.DataContext = new SectionPlotVM(section, k, task.CalcType, SectionPlotMode.Stress, settings);
        StrainView.DataContext = new SectionPlotVM(section, k, task.CalcType, SectionPlotMode.Strain, settings);
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
