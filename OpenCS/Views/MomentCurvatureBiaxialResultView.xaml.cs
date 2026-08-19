using System.Windows.Controls;
using CScore;
using OpenCS.Services;
using OpenCS.Utilites;
using OpenCS.ViewModels;

namespace OpenCS.Views;

/// <summary>Показывает составную диаграмму кривизна-момент и графики жёсткости.</summary>
public partial class MomentCurvatureBiaxialResultView : UserControl
{
    readonly MomentCurvatureBiaxialResultVM _viewModel;
    readonly WpfPlotService _curvatureXPlot;
    readonly WpfPlotService _curvatureYPlot;
    readonly WpfPlotService _stiffnessNPlot;
    readonly WpfPlotService _stiffnessMxPlot;
    readonly WpfPlotService _stiffnessMyPlot;

    public MomentCurvatureBiaxialResultView(CalcResult result, AppViewModel app, CalcTask task)
    {
        InitializeComponent();
        _viewModel = new MomentCurvatureBiaxialResultVM(result);
        DataContext = _viewModel;

        _curvatureXPlot = new WpfPlotService(CurvatureMomentXPlot);
        _curvatureYPlot = new WpfPlotService(CurvatureMomentYPlot);
        _stiffnessNPlot = new WpfPlotService(StiffnessNPlot);
        _stiffnessMxPlot = new WpfPlotService(StiffnessMxPlot);
        _stiffnessMyPlot = new WpfPlotService(StiffnessMyPlot);

        // Опорные линии начала координат (X/Y) мешают чтению графиков — данные здесь
        // всегда в одном (положительном) квадранте, отдельная разметка нуля избыточна.
        _curvatureXPlot.SetOriginReferenceAxesVisibility(showXAxis: false, showYAxis: false);
        _curvatureYPlot.SetOriginReferenceAxesVisibility(showXAxis: false, showYAxis: false);
        _stiffnessNPlot.SetOriginReferenceAxesVisibility(showXAxis: false, showYAxis: false);
        _stiffnessMxPlot.SetOriginReferenceAxesVisibility(showXAxis: false, showYAxis: false);
        _stiffnessMyPlot.SetOriginReferenceAxesVisibility(showXAxis: false, showYAxis: false);

        Loaded += (_, _) => Redraw();
    }

    void Redraw()
    {
        if (_viewModel.HasMx)
        {
            _curvatureXPlot.Clear();
            _curvatureXPlot.SetTitle(Loc.S("MomentCurvature_PlotCurvatureXTitle"));
            _curvatureXPlot.SetXLabel(Loc.S("MomentCurvature_AxisCurvature"));
            _curvatureXPlot.SetYLabel(Loc.S("MomentCurvature_AxisMomentX"));
            if (_viewModel.CurvatureYSeries.Length > 1)
                _curvatureXPlot.AddScatter(_viewModel.CurvatureYSeries, _viewModel.MomentXSeries, color: "#2F5597");
            if (_viewModel.CurvatureYSeriesFaded.Length > 1)
                _curvatureXPlot.AddScatter(_viewModel.CurvatureYSeriesFaded, _viewModel.MomentXSeriesFaded, color: "#BFBFBF");
            AddControlMarker(_curvatureXPlot, _viewModel.Cracking, p => p.Ky, p => p.Mx, "#ED7D31");
            AddControlMarker(_curvatureXPlot, _viewModel.CrackTransition, p => p.Ky, p => p.Mx, "#ED7D31");
            AddControlMarker(_curvatureXPlot, _viewModel.Yield, p => p.Ky, p => p.Mx, "#FFC000");
            AddControlMarker(_curvatureXPlot, _viewModel.Ultimate, p => p.Ky, p => p.Mx, "#C00000");
            _curvatureXPlot.Refresh();
        }

        if (_viewModel.HasMy)
        {
            _curvatureYPlot.Clear();
            _curvatureYPlot.SetTitle(Loc.S("MomentCurvature_PlotCurvatureYTitle"));
            _curvatureYPlot.SetXLabel(Loc.S("MomentCurvature_AxisCurvature"));
            _curvatureYPlot.SetYLabel(Loc.S("MomentCurvature_AxisMomentY"));
            if (_viewModel.CurvatureZSeries.Length > 1)
                _curvatureYPlot.AddScatter(_viewModel.CurvatureZSeries, _viewModel.MomentYSeries, color: "#548235");
            if (_viewModel.CurvatureZSeriesFaded.Length > 1)
                _curvatureYPlot.AddScatter(_viewModel.CurvatureZSeriesFaded, _viewModel.MomentYSeriesFaded, color: "#BFBFBF");
            AddControlMarker(_curvatureYPlot, _viewModel.Cracking, p => p.Kz, p => p.My, "#ED7D31");
            AddControlMarker(_curvatureYPlot, _viewModel.CrackTransition, p => p.Kz, p => p.My, "#ED7D31");
            AddControlMarker(_curvatureYPlot, _viewModel.Yield, p => p.Kz, p => p.My, "#FFC000");
            AddControlMarker(_curvatureYPlot, _viewModel.Ultimate, p => p.Kz, p => p.My, "#C00000");
            _curvatureYPlot.Refresh();
        }

        _stiffnessNPlot.Clear();
        _stiffnessNPlot.SetTitle(Loc.S("MomentCurvature_PlotStiffnessNTitle"));
        _stiffnessNPlot.SetXLabel(Loc.S("MomentCurvature_AxisN"));
        _stiffnessNPlot.SetYLabel(Loc.S("MomentCurvature_AxisStiffnessRatio"));
        if (_viewModel.NStiffnessAxis.Length > 1)
            _stiffnessNPlot.AddScatter(_viewModel.NStiffnessAxis, _viewModel.NStiffnessRatio, color: "#7030A0");
        _stiffnessNPlot.Refresh();

        if (_viewModel.HasMx)
        {
            _stiffnessMxPlot.Clear();
            _stiffnessMxPlot.SetTitle(Loc.S("MomentCurvature_PlotStiffnessXTitle"));
            _stiffnessMxPlot.SetXLabel(Loc.S("MomentCurvature_AxisMomentX"));
            _stiffnessMxPlot.SetYLabel(Loc.S("MomentCurvature_AxisStiffnessRatio"));
            if (_viewModel.MxStiffnessAxis.Length > 1)
                _stiffnessMxPlot.AddScatter(_viewModel.MxStiffnessAxis, _viewModel.MxStiffnessRatio, color: "#2F5597");
            _stiffnessMxPlot.Refresh();
        }

        if (_viewModel.HasMy)
        {
            _stiffnessMyPlot.Clear();
            _stiffnessMyPlot.SetTitle(Loc.S("MomentCurvature_PlotStiffnessYTitle"));
            _stiffnessMyPlot.SetXLabel(Loc.S("MomentCurvature_AxisMomentY"));
            _stiffnessMyPlot.SetYLabel(Loc.S("MomentCurvature_AxisStiffnessRatio"));
            if (_viewModel.MyStiffnessAxis.Length > 1)
                _stiffnessMyPlot.AddScatter(_viewModel.MyStiffnessAxis, _viewModel.MyStiffnessRatio, color: "#548235");
            _stiffnessMyPlot.Refresh();
        }
    }

    static void AddControlMarker(
        WpfPlotService plot, MomentCurvatureBiaxialPointRow? point,
        Func<MomentCurvatureBiaxialPointRow, double> curvature, Func<MomentCurvatureBiaxialPointRow, double> moment,
        string color)
    {
        if (point == null) return;
        plot.AddMarkers([Math.Abs(curvature(point))], [Math.Abs(moment(point))], markerSize: 6, color: color);
    }
}
