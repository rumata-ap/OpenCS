using netDxf;
using OpenCS.Services;
using OpenCS.Utilites;
using System.Collections.ObjectModel;
using System.Windows.Input;

namespace OpenCS.ViewModels;

/// <summary>Строка списка кандидатов диалога импорта зон армирования из DXF.</summary>
public sealed class RebarZoneDxfImportRowVM : ViewModelBase
{
    public PlanarDxfPolygonCandidate Candidate { get; }

    public RebarZoneDxfImportRowVM(PlanarDxfPolygonCandidate candidate)
    {
        Candidate = candidate;
        _isIncluded = candidate.IsAccepted;
    }

    public string Layer => Candidate.Layer;
    public int PointCount => Candidate.X.Length;
    public bool IsAccepted => Candidate.IsAccepted;
    public string StatusText => Candidate.StatusText;

    bool _isIncluded;
    /// <summary>Включить кандидата в импорт. Для отклонённых (!IsAccepted) игнорируется —
    /// UI дополнительно дизейблит чекбокс, это защита на уровне модели.</summary>
    public bool IsIncluded
    {
        get => _isIncluded;
        set
        {
            if (!IsAccepted) return;
            _isIncluded = value;
            OnPropertyChanged();
        }
    }
}

/// <summary>ViewModel диалога прямого DXF-импорта зон армирования (вкладка «Армирование»
/// PlanarRegionMemberDialog). Не знает о PlanarRegionMemberVM — вызывающая сторона забирает
/// результат через GetIncludedCandidates() после подтверждения диалога.</summary>
public sealed class RebarZoneDxfImportVM : ViewModelBase
{
    readonly IFileDialogService _fileDialogService;

    public List<string> Units { get; } = ["мм", "см", "м"];

    int _unitIdx;
    public int UnitIdx { get => _unitIdx; set { _unitIdx = value; OnPropertyChanged(); } }

    string? _fileName;
    public string? FileName { get => _fileName; private set { _fileName = value; OnPropertyChanged(); } }

    public ObservableCollection<RebarZoneDxfImportRowVM> Rows { get; } = [];

    public int AcceptedCount => Rows.Count(r => r.IsAccepted);
    public bool HasAcceptedRows => AcceptedCount > 0;
    public int IncludedCount => Rows.Count(r => r.IsIncluded);
    public string SummaryText => string.Format(Loc.S("PlanarDxfImportSummary"), AcceptedCount, Rows.Count - AcceptedCount);
    public string AddButtonLabel => string.Format(Loc.S("PlanarDxfImportAddZonesButton"), IncludedCount);

    public ICommand OpenFileCommand { get; }

    public RebarZoneDxfImportVM(IFileDialogService fileDialogService)
    {
        _fileDialogService = fileDialogService;
        OpenFileCommand = new RelayCommand(_ => OpenFile());
    }

    void OpenFile()
    {
        string? path = _fileDialogService.OpenFile(
            filter: "Файл обмена чертежами (*.dxf)|*.dxf",
            title: Loc.S("PlanarDxfImportZonesTitle"));
        if (string.IsNullOrEmpty(path)) return;

        DxfDocument dxf;
        try
        {
            dxf = DxfDocument.Load(path);
        }
        catch (Exception ex)
        {
            UiServices.Dialogs.ShowErrorFormatted("PlanarDxfImportLoadError", "Warning", ex.Message);
            return;
        }

        Rows.Clear();
        FileName = System.IO.Path.GetFileName(path);
        double scale = UnitIdx == 0 ? 0.001 : UnitIdx == 1 ? 0.01 : 1.0;

        foreach (var candidate in PlanarDxfPolygonReader.Read(dxf, scale))
        {
            var row = new RebarZoneDxfImportRowVM(candidate);
            row.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == nameof(RebarZoneDxfImportRowVM.IsIncluded))
                {
                    OnPropertyChanged(nameof(IncludedCount));
                    OnPropertyChanged(nameof(AddButtonLabel));
                }
            };
            Rows.Add(row);
        }

        OnPropertyChanged(nameof(AcceptedCount));
        OnPropertyChanged(nameof(HasAcceptedRows));
        OnPropertyChanged(nameof(IncludedCount));
        OnPropertyChanged(nameof(SummaryText));
        OnPropertyChanged(nameof(AddButtonLabel));
    }

    public IReadOnlyList<PlanarDxfPolygonCandidate> GetIncludedCandidates() =>
        [.. Rows.Where(r => r.IsIncluded).Select(r => r.Candidate)];
}
