using CScore;
using OpenCS.Services;
using OpenCS.ViewModels;
using System.Windows.Controls;

namespace OpenCS.Views;

/// <summary>Просмотр результата R-проверки огнестойкости.</summary>
public partial class FireRCheckResultView : UserControl
{
    SectionCutWindowService? _cutWindow;

    public FireRCheckResultView(CalcResult result, AppViewModel app, CalcTask? task)
    {
        InitializeComponent();
        var host = new FireRCheckResultHostVM(result, app, task);
        DataContext = host;

        // Окно эпюры разреза — как у обычной задачи НДС (CalcResultView): одно на вкладки σ и ε.
        if (host.Plots.CutVM is { } cutVm)
        {
            _cutWindow = new SectionCutWindowService(app.CalcSettings);
            _cutWindow.Bind(cutVm, SectionPlotMode.Stress);
            Tabs.SelectionChanged += OnTabSelectionChanged;
            Unloaded += (_, _) => _cutWindow?.Dispose();
        }
    }

    void OnTabSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (e.Source != Tabs) return;
        if (Tabs.SelectedIndex == 1) _cutWindow?.UpdatePlotMode(SectionPlotMode.Stress);
        else if (Tabs.SelectedIndex == 2) _cutWindow?.UpdatePlotMode(SectionPlotMode.Strain);
    }

    sealed class FireRCheckResultHostVM
    {
        public FireRCheckSummaryVM Summary { get; }
        public FireRCheckPlotsVM Plots { get; }

        public FireRCheckResultHostVM(CalcResult result, AppViewModel app, CalcTask? task)
        {
            Summary = new FireRCheckSummaryVM(result);
            Plots = new FireRCheckPlotsVM(result, task, app);
        }
    }
}
