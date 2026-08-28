using System.Windows.Controls;
using System.IO;
using System.Windows;
using CScore;
using Microsoft.Win32;
using OpenCS.Services;
using OpenCS.Utilites;
using OpenCS.ViewModels;

namespace OpenCS.Views;

/// <summary>Показывает составную диаграмму кривизна-момент и графики жёсткости.</summary>
public partial class MomentCurvatureBiaxialResultView : UserControl
{
    const string NonPhysicalColor = "#BFBFBF";

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
    readonly AppViewModel _app;

    public MomentCurvatureBiaxialResultView(CalcResult result, AppViewModel app, CalcTask task)
    {
        InitializeComponent();
        _app = app;
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
        ConfigureExportMenus();

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

        // Селектор режима общий для вкладок деформаций и напряжений — перерисовываем обе.
        _viewModel.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(MomentCurvatureBiaxialResultVM.SelectedRebarStressMode))
                Redraw();
        };

        Loaded += (_, _) => Redraw();
    }

    /// <summary>Подключает единое контекстное меню экспорта ко всем графикам результата.</summary>
    void ConfigureExportMenus()
    {
        CurvatureMomentXPlot.ConfigureExportMenu($"{_viewModel.TaskTag}_curvature_mx.png", exportCsv: ExportPointsCsv);
        CurvatureMomentYPlot.ConfigureExportMenu($"{_viewModel.TaskTag}_curvature_my.png", exportCsv: ExportPointsCsv);
        StiffnessNPlot.ConfigureExportMenu($"{_viewModel.TaskTag}_stiffness_n.png");
        StiffnessMxPlot.ConfigureExportMenu($"{_viewModel.TaskTag}_stiffness_mx.png");
        StiffnessMyPlot.ConfigureExportMenu($"{_viewModel.TaskTag}_stiffness_my.png");
        RebarStrainMxPlot.ConfigureExportMenu($"{_viewModel.TaskTag}_rebar_strain_mx.png");
        RebarStrainMyPlot.ConfigureExportMenu($"{_viewModel.TaskTag}_rebar_strain_my.png");
        RebarStressMxPlot.ConfigureExportMenu($"{_viewModel.TaskTag}_rebar_stress_mx.png");
        RebarStressMyPlot.ConfigureExportMenu($"{_viewModel.TaskTag}_rebar_stress_my.png");
    }

    /// <summary>Экспортирует полный набор расчётных точек κ–M с глобальными настройками CSV.</summary>
    void ExportPointsCsv()
    {
        var dialog = new SaveFileDialog
        {
            Filter = Loc.S("PlotCsvFilter"),
            DefaultExt = ".csv",
            FileName = $"{_viewModel.TaskTag}_moment_curvature_points.csv"
        };
        if (dialog.ShowDialog() != true) return;

        try
        {
            string[] headers =
            [
                Loc.S("MomentCurvature_ColSegment"), Loc.S("MomentCurvature_ColN"),
                Loc.S("MomentCurvature_ColMx"), Loc.S("MomentCurvature_ColMy"),
                Loc.S("MomentCurvature_ColE0"), Loc.S("MomentCurvature_CsvColKy"),
                Loc.S("MomentCurvature_CsvColKz"), Loc.S("MomentCurvature_CsvColNStiffnessRatio"),
                Loc.S("MomentCurvature_CsvColMxStiffnessRatio"), Loc.S("MomentCurvature_CsvColMyStiffnessRatio"),
                Loc.S("MomentCurvature_ColConverged"),
                Loc.S("MomentCurvature_CsvColPsiActive"), Loc.S("MomentCurvature_ColNonPhysical")
            ];
            using var writer = new StreamWriter(dialog.FileName, false,
                MomentCurvatureCsvExporter.ResolveEncoding(_app.CsvSettings.Encoding));
            MomentCurvatureCsvExporter.Write(writer, _viewModel.Rows, _app.CsvSettings, headers);
        }
        catch (Exception ex)
        {
            MessageBox.Show(string.Format(Loc.S("PlotExportError"), ex.Message), Loc.S("Error"),
                MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    void Redraw()
    {
        var (mainRows, auxiliaryRows) = SplitPointRows(_viewModel.Rows, _viewModel.UsePsi);

        if (_viewModel.HasMx)
        {
            _curvatureXPlot.Clear();
            _curvatureXPlot.SetTitle(Loc.S("MomentCurvature_PlotCurvatureXTitle"));
            _curvatureXPlot.SetXLabel(Loc.S("MomentCurvature_AxisCurvature"));
            _curvatureXPlot.SetYLabel(Loc.S("MomentCurvature_AxisMomentX"));
            AddPlotSeries(_curvatureXPlot, _viewModel.CurvatureYSeriesParts, "#2F5597");
            AddPlotSeries(_curvatureXPlot, _viewModel.CurvatureYSeriesFadedParts, NonPhysicalColor);
            AddPointMarkers(_curvatureXPlot, mainRows, useMx: true, "#2F5597", NonPhysicalColor, 9);
            AddPointMarkers(_curvatureXPlot, auxiliaryRows, useMx: true, "#2F5597", NonPhysicalColor, 6);
            AddControlMarker(_curvatureXPlot, _viewModel.Cracking, useMx: true, "#ED7D31", 9);
            if (!_viewModel.UsePsi)
                AddControlMarker(_curvatureXPlot, _viewModel.CrackTransition, useMx: true, "#ED7D31", 9);
            AddControlMarker(_curvatureXPlot, _viewModel.Yield, useMx: true, "#FFC000", 9);
            AddControlMarker(_curvatureXPlot, _viewModel.Ultimate, useMx: true, "#C00000", 9);
            _curvatureXPlot.Refresh();
        }

        if (_viewModel.HasMy)
        {
            _curvatureYPlot.Clear();
            _curvatureYPlot.SetTitle(Loc.S("MomentCurvature_PlotCurvatureYTitle"));
            _curvatureYPlot.SetXLabel(Loc.S("MomentCurvature_AxisCurvature"));
            _curvatureYPlot.SetYLabel(Loc.S("MomentCurvature_AxisMomentY"));
            AddPlotSeries(_curvatureYPlot, _viewModel.CurvatureZSeriesParts, "#548235");
            AddPlotSeries(_curvatureYPlot, _viewModel.CurvatureZSeriesFadedParts, NonPhysicalColor);
            AddPointMarkers(_curvatureYPlot, mainRows, useMx: false, "#548235", NonPhysicalColor, 9);
            AddPointMarkers(_curvatureYPlot, auxiliaryRows, useMx: false, "#548235", NonPhysicalColor, 6);
            AddControlMarker(_curvatureYPlot, _viewModel.Cracking, useMx: false, "#ED7D31", 9);
            if (!_viewModel.UsePsi)
                AddControlMarker(_curvatureYPlot, _viewModel.CrackTransition, useMx: false, "#ED7D31", 9);
            AddControlMarker(_curvatureYPlot, _viewModel.Yield, useMx: false, "#FFC000", 9);
            AddControlMarker(_curvatureYPlot, _viewModel.Ultimate, useMx: false, "#C00000", 9);
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
                plot.AddScatter(fadedValue, fadedMoment, color: NonPhysicalColor);

            AddRebarControlMarker(plot, option, _viewModel.Cracking, useMx, useStress, color);
            if (!_viewModel.UsePsi)
                AddRebarControlMarker(plot, option, _viewModel.CrackTransition, useMx, useStress, color);
            AddRebarControlMarker(plot, option, _viewModel.Yield, useMx, useStress, color);
            AddRebarControlMarker(plot, option, _viewModel.Ultimate, useMx, useStress, color);
        }

        plot.Refresh();
    }

    static void AddPlotSeries(WpfPlotService plot,
        IReadOnlyList<MomentCurvaturePlotSeries> seriesParts, string color)
    {
        foreach (var series in seriesParts)
            if (series.X.Length > 1)
                plot.AddScatter(series.X, series.Y, color: color);
    }

    void AddRebarControlMarker(WpfPlotService plot, RebarOption option, MomentCurvatureBiaxialPointRow? point,
        bool useMx, bool useStress, string color)
    {
        var value = _viewModel.RebarValueAt(option, point, useMx);
        if (value == null) return;
        double x = useStress ? value.Value.sigmaMPa : value.Value.eps;
        plot.AddMarkers([x], [value.Value.momentAbs], markerSize: 7,
            color: point?.NonPhysical == true ? NonPhysicalColor : color);
    }

    static (List<MomentCurvatureBiaxialPointRow> Main, List<MomentCurvatureBiaxialPointRow> Auxiliary)
        SplitPointRows(IReadOnlyList<MomentCurvatureBiaxialPointRow> rows, bool usePsi)
    {
        var converged = rows.Where(row => row.Converged && (!usePsi || row.Segment != 2)).ToList();
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

    // Маркеры строятся ТОЙ ЖЕ знаковой проекцией, что и линия кривой
    // (MomentCurvatureBiaxialResultVM.PlotPoint), а не по модулю: на преднапряжённом сечении
    // кривизна меняет знак по ходу нагружения, и Math.Abs отражал начало кривой в соседний
    // квадрант — маркеры повисали отдельной «гроздью» рядом с линией.
    void AddPointMarkers(
        WpfPlotService plot,
        IEnumerable<MomentCurvatureBiaxialPointRow> rows,
        bool useMx,
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
                points.Select(row => _viewModel.PlotPoint(row, useMx).X).ToArray(),
                points.Select(row => _viewModel.PlotPoint(row, useMx).Y).ToArray(),
                markerSize: size, color: brush);
        }
    }

    void AddControlMarker(
        WpfPlotService plot, MomentCurvatureBiaxialPointRow? point, bool useMx,
        string color, float markerSize)
    {
        if (point == null) return;
        var (x, y) = _viewModel.PlotPoint(point, useMx);
        plot.AddMarkers([x], [y], markerSize: markerSize,
            color: point.NonPhysical ? NonPhysicalColor : color);
    }
}
