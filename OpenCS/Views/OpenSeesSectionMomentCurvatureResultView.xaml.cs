using OpenCS.Services;
using OpenCS.Utilites;
using OpenCS.ViewModels;
using System.Windows.Controls;

namespace OpenCS.Views;

/// <summary>Показывает историю и график одноосного OpenSees moment–curvature результата.</summary>
public partial class OpenSeesSectionMomentCurvatureResultView : UserControl
{
    private readonly OpenSeesSectionMomentCurvatureResultVM _viewModel;
    private readonly WpfPlotService _plot;

    public OpenSeesSectionMomentCurvatureResultView(CScore.CalcResult result)
    {
        InitializeComponent();
        _viewModel = new OpenSeesSectionMomentCurvatureResultVM(result);
        DataContext = _viewModel;
        _plot = new WpfPlotService(MomentCurvaturePlot);
        _plot.SetOriginReferenceAxesVisibility(showXAxis: true, showYAxis: false);
        Loaded += (_, _) => Redraw();
    }

    private void Redraw()
    {
         _plot.Clear();
         _plot.SetTitle(Loc.S("OpenSeesMomentCurvaturePlotTitle"));

        OpenSeesSectionMomentCurvatureRowVM[] rows = _viewModel.ConvergedRows.ToArray();
        double[] curvature = rows.Select(row => row.Curvature).ToArray();
        double[] moments = rows.Select(row => row.MomentKnM).ToArray();
        if (rows.Length > 1)
            _plot.AddScatter(curvature, moments, color: "#2F5597");
        if (rows.Length > 0)
            _plot.AddMarkers(curvature, moments, color: "#2F5597");
        _plot.Refresh();
    }
}
