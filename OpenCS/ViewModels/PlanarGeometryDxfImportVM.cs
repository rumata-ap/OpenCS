using netDxf;
using OpenCS.Services;
using OpenCS.Services.Cad;
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
    readonly AcadImportSettings? _settings;
    readonly Action? _saveSettings;
    string? _path;

    public List<string> Units { get; } = DxfFile.UnitLabels();

    int _unitIdx;
    /// <summary>Единицы чертежа; выбор запоминается и перечитывает открытый файл.</summary>
    public int UnitIdx
    {
        get => _unitIdx;
        set
        {
            if (value < 0 || value == _unitIdx) return;
            _unitIdx = value;
            OnPropertyChanged();
            if (_settings != null)
            {
                _settings.ScaleFactor = DxfFile.UnitMeters[value];
                _saveSettings?.Invoke();
            }
            if (_path != null) Load(_path, keepUnits: true);
        }
    }

    string? _fileName;
    public string? FileName { get => _fileName; private set { _fileName = value; OnPropertyChanged(); } }

    public ObservableCollection<PlanarGeometryDxfImportRowVM> Rows { get; } = [];

    public int AcceptedCount => Rows.Count(r => r.IsAccepted);
    public string SummaryText => string.Format(Loc.S("PlanarDxfImportSummary"), AcceptedCount, Rows.Count - AcceptedCount);
    public bool CanImport => Rows.Any(r => r.IsHull);

    public ICommand OpenFileCommand { get; }

    /// <param name="settings">Запомненные единицы (общий множитель импорта AutoCAD/DXF); null — мм.</param>
    /// <param name="saveSettings">Запись настроек после смены единиц.</param>
    public PlanarGeometryDxfImportVM(IFileDialogService fileDialogService, AcadImportSettings? settings = null,
        Action? saveSettings = null)
    {
        _fileDialogService = fileDialogService;
        _settings = settings;
        _saveSettings = saveSettings;
        _unitIdx = Math.Max(0, DxfFile.UnitIndexOf(settings?.ScaleFactor ?? 0.001));
        OpenFileCommand = new RelayCommand(_ => OpenFile());
    }

    void OpenFile()
    {
        string? path = _fileDialogService.OpenFile(
            filter: Loc.S("DxfFileFilter"),
            title: Loc.S("PlanarDxfImportGeometryTitle"));
        if (string.IsNullOrEmpty(path)) return;
        Load(path, keepUnits: false);
    }

    /// <summary>Читает файл: единицы — из <c>$INSUNITS</c>, если заданы, иначе выбранные (<paramref name="keepUnits"/> —
    /// всегда выбранные).</summary>
    void Load(string path, bool keepUnits)
    {
        DxfDocument dxf;
        try
        {
            dxf = DxfFile.Load(path);
        }
        catch (DxfFileException ex)
        {
            System.Windows.MessageBox.Show(ex.Message, Loc.S("Warning"), System.Windows.MessageBoxButton.OK,
                System.Windows.MessageBoxImage.Error);
            return;
        }
        if (!keepUnits && DxfFile.FileUnitMeters(dxf) is double fileUnits)
        {
            _unitIdx = DxfFile.UnitIndexOf(fileUnits);
            OnPropertyChanged(nameof(UnitIdx));
        }
        _path = path;

        Rows.Clear();
        FileName = System.IO.Path.GetFileName(path);
        double scale = DxfFile.UnitMeters[_unitIdx];

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
