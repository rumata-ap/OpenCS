using System.Globalization;
using CScore.Fem;
using CScore.Fem.Fragment;
using OpenCS.Services;
using OpenCS.Utilites;

namespace OpenCS.ViewModels;

/// <summary>
/// Шапка схемы-фрагмента на странице схемы: откуда и по каким отметкам собран, состояние родителя (сверка отпечатка
/// при открытии — в фоне, без автодействий) и отчёт сборки. У обычной схемы не показывается.
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
        ReportRows = StoreyFragmentReportRows.From(StoreyFragmentBuildReport.FromJson(schema.FragmentBuildReportJson));
        _isChecking = true;
        _statusText = Loc.S("StoreyFragmentParentChecking");
    }

    public bool IsVisible { get; }
    public string Title { get; } = "";
    public string SchemaTag => _schema.Tag;
    public IReadOnlyList<StoreyFragmentReportRow> ReportRows { get; } = [];
    public bool HasReport => ReportRows.Count > 0;

    FragmentParentStatus _status = FragmentParentStatus.NotFragment;
    public FragmentParentStatus Status { get => _status; private set { _status = value; OnPropertyChanged(); OnPropertyChanged(nameof(IsWarning)); } }

    /// <summary>Родитель изменён после сборки или удалён.</summary>
    public bool IsWarning => Status is FragmentParentStatus.Changed or FragmentParentStatus.ParentDeleted;

    bool _isChecking;
    public bool IsChecking { get => _isChecking; private set { _isChecking = value; OnPropertyChanged(); } }

    string? _statusText;
    public string? StatusText { get => _statusText; private set { _statusText = value; OnPropertyChanged(); } }

    /// <summary>Сверка родителя (<see cref="StoreyFragmentService.CheckParent"/>) в фоне; не фрагмент — без запроса.</summary>
    public async Task<FragmentParentStatus> LoadAsync()
    {
        if (!IsVisible) return FragmentParentStatus.NotFragment;
        try
        {
            Status = await Task.Run(() => StoreyFragmentService.CheckParent(_db, _schema));
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
        }
        finally
        {
            IsChecking = false;
        }
        return Status;
    }
}
