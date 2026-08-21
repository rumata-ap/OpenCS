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
    readonly WpfPlotService _rebarStrainMxPlot;
    readonly WpfPlotService _rebarStrainMyPlot;
    readonly WpfPlotService _rebarStressMxPlot;
    readonly WpfPlotService _rebarStressMyPlot;

    public MomentCurvatureBiaxialResultView(CalcResult result, AppViewModel app, CalcTask task)
    {
        InitializeComponent();
        var section = app.CrossSections.FirstOrDefault(s => s.Id == task.SectionId);
        _viewModel = new MomentCurvatureBiaxialResultVM(result, section, task.CalcType, app.CalcSettings, app.Diagrams);
        DataContext = _viewModel;

        _curvatureXPlot = new WpfPlotService(CurvatureMomentXPlot);
        _curvatureYPlot = new WpfPlotService(CurvatureMomentYPlot);
        _stiffnessNPlot = new WpfPlotService(StiffnessNPlot);
        _stiffnessMxPlot = new WpfPlotService(StiffnessMxPlot);
        _stiffnessMyPlot = new WpfPlotService(StiffnessMyPlot);
        _rebarStrainMxPlot = new WpfPlotService(RebarStrainMxPlot);
        _rebarStrainMyPlot = new WpfPlotService(RebarStrainMyPlot);
        _rebarStressMxPlot = new WpfPlotService(RebarStressMxPlot);
        _rebarStressMyPlot = new WpfPlotService(RebarStressMyPlot);

        // Опорные линии начала координат (X/Y) мешают чтению графиков — данные здесь
        // всегда в одном (положительном) квадранте, отдельная разметка нуля избыточна.
        _curvatureXPlot.SetOriginReferenceAxesVisibility(showXAxis: false, showYAxis: false);
        _curvatureYPlot.SetOriginReferenceAxesVisibility(showXAxis: false, showYAxis: false);
        _stiffnessNPlot.SetOriginReferenceAxesVisibility(showXAxis: false, showYAxis: false);
        _stiffnessMxPlot.SetOriginReferenceAxesVisibility(showXAxis: false, showYAxis: false);
        _stiffnessMyPlot.SetOriginReferenceAxesVisibility(showXAxis: false, showYAxis: false);
        _rebarStrainMxPlot.SetOriginReferenceAxesVisibility(showXAxis: false, showYAxis: false);
        _rebarStrainMyPlot.SetOriginReferenceAxesVisibility(showXAxis: false, showYAxis: false);
        _rebarStressMxPlot.SetOriginReferenceAxesVisibility(showXAxis: false, showYAxis: false);
        _rebarStressMyPlot.SetOriginReferenceAxesVisibility(showXAxis: false, showYAxis: false);

        // Перерисовка при переключении чекбоксов стержней (общая коллекция для обеих вкладок).
        foreach (var option in _viewModel.RebarOptions)
            option.PropertyChanged += (_, _) => Redraw();

        Loaded += (_, _) => Redraw();
    }

    void Redraw()
    {
        var (mainRows, auxiliaryRows) = SplitPointRows(_viewModel.Rows);

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
            AddPointMarkers(_curvatureXPlot, mainRows, p => p.Ky, p => p.Mx, "#2F5597", "#BFBFBF", 9);
            AddPointMarkers(_curvatureXPlot, auxiliaryRows, p => p.Ky, p => p.Mx, "#2F5597", "#BFBFBF", 6);
            AddControlMarker(_curvatureXPlot, _viewModel.Cracking, p => p.Ky, p => p.Mx, "#ED7D31", 9);
            AddControlMarker(_curvatureXPlot, _viewModel.CrackTransition, p => p.Ky, p => p.Mx, "#ED7D31", 9);
            AddControlMarker(_curvatureXPlot, _viewModel.Yield, p => p.Ky, p => p.Mx, "#FFC000", 9);
            AddControlMarker(_curvatureXPlot, _viewModel.Ultimate, p => p.Ky, p => p.Mx, "#C00000", 9);
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
            AddPointMarkers(_curvatureYPlot, mainRows, p => p.Kz, p => p.My, "#548235", "#BFBFBF", 9);
            AddPointMarkers(_curvatureYPlot, auxiliaryRows, p => p.Kz, p => p.My, "#548235", "#BFBFBF", 6);
            AddControlMarker(_curvatureYPlot, _viewModel.Cracking, p => p.Kz, p => p.My, "#ED7D31", 9);
            AddControlMarker(_curvatureYPlot, _viewModel.CrackTransition, p => p.Kz, p => p.My, "#ED7D31", 9);
            AddControlMarker(_curvatureYPlot, _viewModel.Yield, p => p.Kz, p => p.My, "#FFC000", 9);
            AddControlMarker(_curvatureYPlot, _viewModel.Ultimate, p => p.Kz, p => p.My, "#C00000", 9);
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

        var selected = _viewModel.RebarOptions.Where(o => o.IsSelected).ToList();

        if (_viewModel.HasMx)
        {
            RedrawRebarPlot(_rebarStrainMxPlot, selected, useMx: true, useStress: false,
                title: Loc.S("MomentCurvature_PlotRebarStrainXTitle"), xLabel: Loc.S("MomentCurvature_AxisRebarStrain"),
                yLabel: Loc.S("MomentCurvature_AxisMomentX"));
            RedrawRebarPlot(_rebarStressMxPlot, selected, useMx: true, useStress: true,
                title: Loc.S("MomentCurvature_PlotRebarStressXTitle"), xLabel: Loc.S("MomentCurvature_AxisRebarStress"),
                yLabel: Loc.S("MomentCurvature_AxisMomentX"));
        }

        if (_viewModel.HasMy)
        {
            RedrawRebarPlot(_rebarStrainMyPlot, selected, useMx: false, useStress: false,
                title: Loc.S("MomentCurvature_PlotRebarStrainYTitle"), xLabel: Loc.S("MomentCurvature_AxisRebarStrain"),
                yLabel: Loc.S("MomentCurvature_AxisMomentY"));
            RedrawRebarPlot(_rebarStressMyPlot, selected, useMx: false, useStress: true,
                title: Loc.S("MomentCurvature_PlotRebarStressYTitle"), xLabel: Loc.S("MomentCurvature_AxisRebarStress"),
                yLabel: Loc.S("MomentCurvature_AxisMomentY"));
        }
    }

    void RedrawRebarPlot(WpfPlotService plot, List<RebarOption> selected, bool useMx, bool useStress,
        string title, string xLabel, string yLabel)
    {
        plot.Clear();
        plot.SetTitle(title);
        plot.SetXLabel(xLabel);
        plot.SetYLabel(yLabel);

        foreach (var option in selected)
        {
            var series = _viewModel.BuildRebarSeries(option, useMx);
            if (series == null) continue;

            string color = ((System.Windows.Media.SolidColorBrush)option.ColorBrush).Color.ToString();
            var (mainMoment, mainValue, fadedMoment, fadedValue) = useStress
                ? (series.MomentSigma, series.Sigma, series.MomentSigmaFaded, series.SigmaFaded)
                : (series.MomentEps, series.Eps, series.MomentEpsFaded, series.EpsFaded);

            // Деформация/напряжение — ось X, момент — ось Y (по просьбе пользователя).
            if (mainMoment.Length > 1)
                plot.AddScatter(mainValue, mainMoment, color: color);
            if (fadedMoment.Length > 1)
                plot.AddScatter(fadedValue, fadedMoment, color: "#BFBFBF");

            AddRebarControlMarker(plot, option, _viewModel.Cracking, useMx, useStress, color);
            AddRebarControlMarker(plot, option, _viewModel.CrackTransition, useMx, useStress, color);
            AddRebarControlMarker(plot, option, _viewModel.Yield, useMx, useStress, color);
            AddRebarControlMarker(plot, option, _viewModel.Ultimate, useMx, useStress, color);
        }

        plot.Refresh();
    }

    void AddRebarControlMarker(WpfPlotService plot, RebarOption option, MomentCurvatureBiaxialPointRow? point,
        bool useMx, bool useStress, string color)
    {
        var value = _viewModel.RebarValueAt(option, point, useMx);
        if (value == null) return;
        double x = useStress ? value.Value.sigmaMPa : value.Value.eps;
        plot.AddMarkers([x], [value.Value.momentAbs], markerSize: 7, color: color);
    }

    static (List<MomentCurvatureBiaxialPointRow> Main, List<MomentCurvatureBiaxialPointRow> Auxiliary)
        SplitPointRows(IReadOnlyList<MomentCurvatureBiaxialPointRow> rows)
    {
        var converged = rows.Where(row => row.Converged).ToList();
        var main = new List<MomentCurvatureBiaxialPointRow>();
        var auxiliary = new List<MomentCurvatureBiaxialPointRow>();

        for (int i = 0; i < converged.Count; i++)
        {
            bool isSegmentEnd = i == converged.Count - 1 ||
                converged[i].Segment != converged[i + 1].Segment;
            if (i == 0 || isSegmentEnd)
                main.Add(converged[i]);
            else
                auxiliary.Add(converged[i]);
        }

        return (main, auxiliary);
    }

    static void AddPointMarkers(
        WpfPlotService plot,
        IEnumerable<MomentCurvatureBiaxialPointRow> rows,
        Func<MomentCurvatureBiaxialPointRow, double> x,
        Func<MomentCurvatureBiaxialPointRow, double> y,
        string color,
        string fadedColor,
        float size)
    {
        Add(false, color);
        Add(true, fadedColor);

        void Add(bool faded, string brush)
        {
            var points = rows.Where(row => row.NonPhysical == faded).ToList();
            if (points.Count == 0) return;
            plot.AddMarkers(
                points.Select(row => Math.Abs(x(row))).ToArray(),
                points.Select(row => Math.Abs(y(row))).ToArray(),
                markerSize: size, color: brush);
        }
    }

    static void AddControlMarker(
        WpfPlotService plot, MomentCurvatureBiaxialPointRow? point,
        Func<MomentCurvatureBiaxialPointRow, double> curvature, Func<MomentCurvatureBiaxialPointRow, double> moment,
        string color, float markerSize)
    {
        if (point == null) return;
        plot.AddMarkers([Math.Abs(curvature(point))], [Math.Abs(moment(point))], markerSize: markerSize, color: color);
    }
}
