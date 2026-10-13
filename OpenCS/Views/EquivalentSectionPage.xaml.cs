using CScore.PlateStrip;
using OpenCS.ViewModels;
using OpenCS.Views.Helpers;

namespace OpenCS.Views;

/// <summary>Страница контроля сохранённого эквивалентного сечения.</summary>
public partial class EquivalentSectionPage : System.Windows.Controls.UserControl
{
    readonly StripNonlinearRunVM _run;

    public EquivalentSectionPage(EquivalentSection section, AppViewModel app)
    {
        InitializeComponent();
        var vm = new EquivalentSectionVM(section, app);
        DataContext = vm;
        _run = vm.Run;

        // Эпюра одного знака ставит ось у края холста: растянутая сторона читается сразу.
        stripCanvas.FitAxis = true;
        stripCanvas.MarkerClicked += key => { if (key is int index) _run.SelectedStationIndex = index; };

        _run.DiagramChanged += OnDiagramChanged;
        _run.PropertyChanged += OnRunPropertyChanged;
        Unloaded += (_, _) =>
        {
            _run.DiagramChanged -= OnDiagramChanged;
            _run.PropertyChanged -= OnRunPropertyChanged;
        };
        RefreshDiagram();
    }

    void OnDiagramChanged(object? sender, EventArgs e) => RefreshDiagram();

    void OnRunPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(StripNonlinearRunVM.SelectedStationIndex))
            stripCanvas.SelectMarker(_run.SelectedStationIndex >= 0 ? _run.SelectedStationIndex : null);
    }

    void RefreshDiagram()
    {
        static List<FemMemberForceCanvas.Segment> ToCanvas(FemDiagramSeries series) => series.Segments
            .Select(s => new FemMemberForceCanvas.Segment(s.S0, s.S1, s.Value0, s.Value1))
            .ToList();

        var secondary = _run.DiagramFiber.Segments.Count > 0 ? ToCanvas(_run.DiagramFiber) : null;
        stripCanvas.SetData(ToCanvas(_run.DiagramPrimary), _run.DiagramTitle, secondary);

        var markers = new List<FemMemberForceCanvas.Marker>();
        for (int i = 0; i < _run.StationPositions.Count; i++)
        {
            double x = _run.StationPositions[i];
            markers.Add(new FemMemberForceCanvas.Marker(x, true, i, $"x = {x:F2}"));
        }
        stripCanvas.SetMarkers(markers);
        if (_run.SelectedStationIndex >= 0) stripCanvas.SelectMarker(_run.SelectedStationIndex);
    }
}
