using netDxf;
using OpenCS.Services;
using OpenCS.Utilites;
using System.Collections.ObjectModel;
using System.Windows.Input;

namespace OpenCS.ViewModels;

/// <summary>Строка списка кандидатов диалога импорта геометрии PlanarRegion из DXF. IsHull/IsHole
/// взаимоисключающие на уровне строки; уникальность IsHull среди строк обеспечивает WPF
/// RadioButton.GroupName в самом диалоге (см. PlanarGeometryDxfImportDialog) — здесь только
/// локальная непротиворечивость.</summary>
public sealed class PlanarGeometryDxfImportRowVM : ViewModelBase
{
    public PlanarDxfPolygonCandidate Candidate { get; }

    public PlanarGeometryDxfImportRowVM(PlanarDxfPolygonCandidate candidate) => Candidate = candidate;

    public string Layer => Candidate.Layer;
    public int PointCount => Candidate.X.Length;
    public bool IsAccepted => Candidate.IsAccepted;
    public string StatusText => Candidate.StatusText;

    bool _isHull;
    public bool IsHull
    {
        get => _isHull;
        set
        {
            if (!IsAccepted) return;
            _isHull = value;
            OnPropertyChanged();
            if (value) IsHole = false;
        }
    }

    bool _isHole;
    public bool IsHole
    {
        get => _isHole;
        set
        {
            if (!IsAccepted) return;
            _isHole = value;
            OnPropertyChanged();
            if (value) IsHull = false;
        }
    }
}

/// <summary>ViewModel диалога прямого DXF-импорта геометрии региона (Hull/Holes, вкладка
/// «Геометрия» PlanarRegionMemberDialog). Не знает о PlanarRegionMemberVM — вызывающая сторона
/// забирает SelectedHull/SelectedHoles после подтверждения диалога.</summary>
public sealed class PlanarGeometryDxfImportVM : ViewModelBase
{
    readonly IFileDialogService _fileDialogService;

    public List<string> Units { get; } = ["мм", "см", "м"];

    int _unitIdx;
    public int UnitIdx { get => _unitIdx; set { _unitIdx = value; OnPropertyChanged(); } }

    string? _fileName;
    public string? FileName { get => _fileName; private set { _fileName = value; OnPropertyChanged(); } }

    public ObservableCollection<PlanarGeometryDxfImportRowVM> Rows { get; } = [];

    public int AcceptedCount => Rows.Count(r => r.IsAccepted);
    public string SummaryText => string.Format(Loc.S("PlanarDxfImportSummary"), AcceptedCount, Rows.Count - AcceptedCount);
    public bool CanImport => Rows.Any(r => r.IsHull);

    public ICommand OpenFileCommand { get; }

    public PlanarGeometryDxfImportVM(IFileDialogService fileDialogService)
    {
        _fileDialogService = fileDialogService;
        OpenFileCommand = new RelayCommand(_ => OpenFile());
    }

    void OpenFile()
    {
        string? path = _fileDialogService.OpenFile(
            filter: "Файл обмена чертежами (*.dxf)|*.dxf",
            title: Loc.S("PlanarDxfImportGeometryTitle"));
        if (string.IsNullOrEmpty(path)) return;

        DxfDocument dxf;
        try
        {
            dxf = DxfDocument.Load(path);
        }
        catch (Exception ex)
        {
            System.Windows.MessageBox.Show(
                string.Format(Loc.S("PlanarDxfImportLoadError"), ex.Message),
                Loc.S("Warning"), System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
            return;
        }

        Rows.Clear();
        FileName = System.IO.Path.GetFileName(path);
        double scale = UnitIdx == 0 ? 0.001 : UnitIdx == 1 ? 0.01 : 1.0;

        foreach (var candidate in PlanarDxfPolygonReader.Read(dxf, scale))
        {
            var row = new PlanarGeometryDxfImportRowVM(candidate);
            row.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == nameof(PlanarGeometryDxfImportRowVM.IsHull))
                    OnPropertyChanged(nameof(CanImport));
            };
            Rows.Add(row);
        }

        OnPropertyChanged(nameof(AcceptedCount));
        OnPropertyChanged(nameof(SummaryText));
        OnPropertyChanged(nameof(CanImport));
    }

    public PlanarGeometryDxfImportRowVM? SelectedHull => Rows.FirstOrDefault(r => r.IsHull);

    public IReadOnlyList<PlanarDxfPolygonCandidate> SelectedHoles =>
        [.. Rows.Where(r => r.IsHole).Select(r => r.Candidate)];
}
