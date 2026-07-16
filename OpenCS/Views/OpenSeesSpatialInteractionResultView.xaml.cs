using System.ComponentModel;
using System.Windows.Controls;
using CScore;
using OpenCS.Services;
using OpenCS.Utilites;
using OpenCS.ViewModels;

namespace OpenCS.Views;

/// <summary>Представление полярной диаграммы и истории пространственного OpenSees-результата.</summary>
public partial class OpenSeesSpatialInteractionResultView : UserControl
{
    private WpfPlotService? _polarPlot;
    private WpfPlotService? _historyPlot;
    private OpenSeesSpatialInteractionResultVM? _vm;

    public OpenSeesSpatialInteractionResultView(CalcResult result)
    {
        InitializeComponent();
        _vm = new OpenSeesSpatialInteractionResultVM(result);
        DataContext = _vm;
        _vm.PropertyChanged += OnVmPropertyChanged;
        _polarPlot = new WpfPlotService(PolarPlot);
        _historyPlot = new WpfPlotService(HistoryPlot);
        Loaded += (_, _) => Redraw();
        Unloaded += (_, _) => _vm.PropertyChanged -= OnVmPropertyChanged;
    }

    private void OnVmPropertyChanged(object? sender, PropertyChangedEventArgs args) => Redraw();

    private void Redraw()
    {
        if (_vm is null || _polarPlot is null || _historyPlot is null)
            return;

        _polarPlot.Clear();
        _polarPlot.EnableSquareAxes();
        _polarPlot.SetTitle(Loc.S("OpenSeesSpatialPolarTitle"));
        _polarPlot.SetXLabel(Loc.S("OpenSeesSpatialMxAxis"));
        _polarPlot.SetYLabel(Loc.S("OpenSeesSpatialMyAxis"));

        var polar = _vm.PolarMxKnM.Zip(_vm.PolarMyKnM, (mx, my) => (mx, my))
            .Where(item => item.mx.HasValue && item.my.HasValue)
            .Select(item => (X: item.mx!.Value, Y: item.my!.Value))
            .ToArray();
        if (polar.Length > 1)
        {
            double[] xs = polar.Select(item => item.X).Append(polar[0].X).ToArray();
            double[] ys = polar.Select(item => item.Y).Append(polar[0].Y).ToArray();
            _polarPlot.AddScatter(xs, ys, color: "#2F5597");
        }
        if (polar.Length > 0)
            _polarPlot.AddMarkers(polar.Select(item => item.X).ToArray(), polar.Select(item => item.Y).ToArray(), color: "#2F5597");

        if (_vm.SelectedPoint?.MomentMxNm is double selectedMx && _vm.SelectedPoint.MomentMyNm is double selectedMy)
            _polarPlot.AddMarkers([selectedMx / 1000.0], [selectedMy / 1000.0], markerSize: 8, color: "#C00000");
        _polarPlot.Refresh();

        _historyPlot.Clear();
        _historyPlot.SetTitle(Loc.S("OpenSeesSpatialHistoryTitle"));
        _historyPlot.SetXLabel(Loc.S("OpenSeesSpatialCurvatureAxis"));
        _historyPlot.SetYLabel(Loc.S("OpenSeesSpatialMomentAxis"));
        double[] kx = _vm.HistoryCurvatureMx.ToArray();
        double[] ky = _vm.HistoryCurvatureMy.ToArray();
        double[] mx = _vm.HistoryMomentMxKnM.ToArray();
        double[] my = _vm.HistoryMomentMyKnM.ToArray();
        _historyPlot.AddScatter(kx, mx, color: "#2F5597", label: Loc.S("OpenSeesSpatialMxSeries"));
        _historyPlot.AddScatter(ky, my, color: "#70AD47", label: Loc.S("OpenSeesSpatialMySeries"));
        _historyPlot.ShowLegend(true);
        _historyPlot.Refresh();
    }
}
