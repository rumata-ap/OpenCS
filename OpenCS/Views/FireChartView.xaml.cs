using OpenCS.Services;
using OpenCS.ViewModels;
using System.Windows.Controls;

namespace OpenCS.Views;

/// <summary>Линейный график на чистом WPF <see cref="PlotCanvas"/>.</summary>
public partial class FireChartView : UserControl
{
    WpfPlotService? _plot;

    public FireChartView()
    {
        InitializeComponent();
        DataContextChanged += (_, _) => BindVm();
    }

    void BindVm()
    {
        if (DataContext is FireLineChartVM old)
            old.RedrawRequested -= Redraw;

        if (DataContext is not FireLineChartVM vm)
            return;

        _plot ??= new WpfPlotService(Plot);
        vm.RedrawRequested += Redraw;
        Redraw();
    }

    void Redraw()
    {
        if (DataContext is not FireLineChartVM vm || _plot is null) return;
        _plot.Clear();
        if (vm.Series.Count == 0) return;

        foreach (var s in vm.Series)
        {
            if (s.X.Length < 2) continue;
            double[] ys = vm.LogY
                ? s.Y.Select(v => Math.Log10(Math.Max(v, 1e-12))).ToArray()
                : s.Y;
            _plot.AddScatter(s.X, ys, color: s.ColorHex, label: s.Label);
        }

        _plot.SetTitle(vm.Title);
        _plot.SetXLabel(vm.XLabel);
        _plot.SetYLabel(vm.YLabel);
        _plot.Refresh();
    }
}
