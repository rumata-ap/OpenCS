using System.Globalization;
using CScore.Fem;
using CScore.Fem.Fragment;
using CScore.Fem.Fragment.Masks;
using OpenCS.Services;
using OpenCS.Utilites;

namespace OpenCS.ViewModels;

/// <summary>
/// Шапка схемы-фрагмента на странице схемы: откуда и по каким отметкам собран, состояние родителя и масок (сверка
/// отпечатков при открытии — в фоне, без автодействий; расчёт не блокируется), отчёты сборки и снятия масок. У обычной
/// схемы не показывается.
/// </summary>
public sealed class StoreyFragmentBannerVM : ViewModelBase
{
    readonly DatabaseService _db;
    readonly FemSchema _schema;

    public StoreyFragmentBannerVM(DatabaseService db, FemSchema schema)
    {
        _db = db;
        _schema = schema;
        IsVisible = schema.ParentSchemaId is not null;
        if (!IsVisible) return;
        string parentTag = db.FemSchemas.FirstOrDefault(s => s.Id == schema.ParentSchemaId)?.Tag
            ?? $"#{schema.ParentSchemaId}";
        string F(double? v) => v?.ToString("0.###", CultureInfo.CurrentCulture) ?? "—";
        Title = string.Format(Loc.S("StoreyFragmentBannerTitle"), parentTag, F(schema.FragmentZ1), F(schema.FragmentZ2));
        PrismText = PrismLine(schema.FragmentPrismJson, CultureInfo.CurrentCulture);
        ReportRows = StoreyFragmentReportRows.From(StoreyFragmentBuildReport.FromJson(schema.FragmentBuildReportJson));
        _isChecking = true;
        _statusText = Loc.S("StoreyFragmentParentChecking");
        _masks = db.GetFragmentMasks(schema.Id, items: false);
        if (_masks != null)
        {
            MaskReportRows = StoreyFragmentMaskFlow.Rows(StoreyFragmentBuildReport.FromJson(_masks.ReportJson));
            _maskText = Loc.S("StoreyFragmentMasksChecking");
        }
        else _maskText = Loc.S("StoreyFragmentMasksBannerNone");
    }

    readonly FragmentMaskSet? _masks;

    public bool IsVisible { get; }
    public string Title { get; } = "";
    public string SchemaTag => _schema.Tag;
    /// <summary>Сведения о призме фрагмента вокруг колонн и стен; пусто — этажный фрагмент или битый JSON.</summary>
    public string PrismText { get; } = "";
    public bool HasPrism => PrismText.Length > 0;

    /// <summary>
    /// «Вокруг K1 +2 · призма 3,4 × 3,4 м · d = 1,5 м · перекрытия +3,300…+6,600» / «… · вся высота элементов»; без
    /// перекрытий в сведениях (старые сведения) — без хвоста.
    /// </summary>
    public static string PrismLine(string? json, CultureInfo culture)
    {
        FragmentPrismInfo? info;
        try { info = FragmentPrismCodec.Decode(json); }
        catch (Exception ex) when (ex is System.IO.InvalidDataException or System.Text.Json.JsonException) { return ""; }
        if (info is null) return "";
        string F(double v) => v.ToString("0.###", culture);
        string Z(double v) => StoreyFragmentForm.SignedLevel(v, culture);
        var tags = info.MemberTags ?? [];
        string who = tags.Count switch
        {
            0 => "—",
            1 => tags[0],
            _ => $"{tags[0]} +{tags.Count - 1}",
        };
        string height = info.FullHeight ? Loc.S("StoreyFragmentBannerPrismFull")
            : info is { FloorFromZ: double a, FloorToZ: double b }
                ? Math.Abs(a - b) < 1e-6 ? string.Format(Loc.S("StoreyFragmentBannerPrismFloor"), Z(a))
                : string.Format(Loc.S("StoreyFragmentBannerPrismFloors"), Z(a), Z(b))
            : "";
        string line = string.Format(Loc.S("StoreyFragmentBannerPrism"), who, F(2 * info.Prism.HalfLength), F(2 * info.Prism.HalfWidth),
            info.OffsetM is double d ? F(d) : "—", height);
        return height.Length > 0 ? line : line.TrimEnd(' ', '·');
    }
    public IReadOnlyList<StoreyFragmentReportRow> ReportRows { get; } = [];
    public bool HasReport => ReportRows.Count > 0;
    /// <summary>Отчёт снятия действующих масок.</summary>
    public IReadOnlyList<StoreyFragmentReportRow> MaskReportRows { get; } = [];
    public bool HasMaskReport => MaskReportRows.Count > 0;

    FragmentMaskStatus _maskStatus = FragmentMaskStatus.None;
    public FragmentMaskStatus MaskStatus { get => _maskStatus; private set { _maskStatus = value; OnPropertyChanged(); OnPropertyChanged(nameof(IsMaskWarning)); } }

    /// <summary>Маски устарели или их источник удалён.</summary>
    public bool IsMaskWarning => MaskStatus is FragmentMaskStatus.Stale or FragmentMaskStatus.SourceDeleted;

    string _maskText = "";
    /// <summary>Строка масок: не сняты / из «источник» от дата / устарели / источник удалён.</summary>
    public string MaskText { get => _maskText; private set { _maskText = value; OnPropertyChanged(); } }

    FragmentParentStatus _status = FragmentParentStatus.NotFragment;
    public FragmentParentStatus Status { get => _status; private set { _status = value; OnPropertyChanged(); OnPropertyChanged(nameof(IsWarning)); } }

    /// <summary>Родитель изменён после сборки или удалён.</summary>
    public bool IsWarning => Status is FragmentParentStatus.Changed or FragmentParentStatus.ParentDeleted;

    bool _isChecking;
    public bool IsChecking { get => _isChecking; private set { _isChecking = value; OnPropertyChanged(); } }

    string? _statusText;
    public string? StatusText { get => _statusText; private set { _statusText = value; OnPropertyChanged(); } }

    /// <summary>
    /// Сверка родителя (<see cref="StoreyFragmentService.CheckParent"/>) и масок
    /// (<see cref="StoreyFragmentMaskService.CheckMasks"/>) в фоне; не фрагмент — без запроса.
    /// </summary>
    public async Task<FragmentParentStatus> LoadAsync()
    {
        if (!IsVisible) return FragmentParentStatus.NotFragment;
        try
        {
            var (parent, masks) = await Task.Run(() => (StoreyFragmentService.CheckParent(_db, _schema),
                _masks != null ? StoreyFragmentMaskService.CheckMasks(_db, _schema) : FragmentMaskStatus.None));
            Status = parent;
            MaskStatus = masks;
            if (_masks != null)
                MaskText = string.Format(Loc.S(masks switch
                {
                    FragmentMaskStatus.Stale => "StoreyFragmentMasksBannerStale",
                    FragmentMaskStatus.SourceDeleted => "StoreyFragmentMasksBannerSourceDeleted",
                    _ => "StoreyFragmentMasksBannerCurrent",
                }), _masks.SourceLabel, StoreyFragmentMaskFlow.Short(_masks.CreatedAt));
            StatusText = Loc.S(Status switch
            {
                FragmentParentStatus.Changed => "StoreyFragmentParentChanged",
                FragmentParentStatus.ParentDeleted => "StoreyFragmentParentDeleted",
                _ => "StoreyFragmentParentUnchanged",
            });
        }
        catch (Exception ex)
        {
            StatusText = string.Format(Loc.S("StoreyFragmentParentCheckFailed"), ex.Message);
            if (_masks != null) MaskText = "";
        }
        finally
        {
            IsChecking = false;
        }
        return Status;
    }
}
