using System.Collections.ObjectModel;
using System.Text.Json;
using System.Windows.Input;
using CScore;
using OpenCS.OpenSees.Analysis;
using OpenCS.Utilites;

namespace OpenCS.ViewModels;

/// <summary>
/// ViewModel результата пространственной диаграммы N–Mx–My.
/// </summary>
public sealed class OpenSeesSpatialInteractionResultVM : ViewModelBase
{
    private readonly List<ForceGroup> _groups = [];
    private double? _selectedAxialForceN;
    private double? _selectedAngle;

    /// <summary>Исходный результат анализа.</summary>
    public SectionSpatialInteractionResult Result { get; }

    /// <summary>Метка задачи.</summary>
    public string TaskTag { get; }

    /// <summary>Дата создания результата.</summary>
    public string CreatedText { get; }

    /// <summary>Доступные значения N в кН для выбора на диаграмме.</summary>
    public IReadOnlyList<double> AvailableAxialForces { get; }

    /// <summary>Выбранное значение N в кН.</summary>
    public double? SelectedAxialForce
    {
        get => _selectedAxialForceN.HasValue ? _selectedAxialForceN.Value / 1000.0 : null;
        set
        {
            if (!value.HasValue)
                return;

            ForceGroup? group = _groups.FirstOrDefault(item => item.AxialForceN == value.Value * 1000.0);
            if (group is null)
                return;

            double? oldAngle = _selectedAngle;
            _selectedAxialForceN = group.AxialForceN;
            _selectedAngle = group.Angles.Contains(oldAngle ?? double.NaN)
                ? oldAngle
                : group.Angles.FirstOrDefault();
            NotifySelectionChanged();
        }
    }

    /// <summary>Углы выбранного значения N в градусах.</summary>
    public IReadOnlyList<double> AvailableAngles => CurrentGroup?.Angles ?? [];

    /// <summary>Выбранный угол в градусах.</summary>
    public double? SelectedAngle
    {
        get => _selectedAngle;
        set
        {
            if (!value.HasValue || CurrentGroup is null || !CurrentGroup.Angles.Contains(value.Value))
                return;
            if (_selectedAngle == value.Value)
                return;
            _selectedAngle = value.Value;
            NotifySelectionChanged();
        }
    }

    /// <summary>Выбранная точка диаграммы.</summary>
    public SectionSpatialInteractionPoint? SelectedPoint =>
        CurrentGroup?.Points.FirstOrDefault(point => point.AngleDegrees == _selectedAngle);

    /// <summary>Момент Mx выбранной точки в кН·м для отображения.</summary>
    public double? SelectedMomentMxKnM => SelectedPoint?.MomentMxNm / 1000.0;

    /// <summary>Момент My выбранной точки в кН·м для отображения.</summary>
    public double? SelectedMomentMyKnM => SelectedPoint?.MomentMyNm / 1000.0;

    /// <summary>Кривизна Mx выбранной точки в 1/м.</summary>
    public double? SelectedCurvatureMx => SelectedPoint?.CurvatureMx;

    /// <summary>Кривизна My выбранной точки в 1/м.</summary>
    public double? SelectedCurvatureMy => SelectedPoint?.CurvatureMy;

    /// <summary>Количество строк истории выбранной точки.</summary>
    public int SelectedHistoryCount => SelectedPoint?.HistoryRows.Count ?? 0;

    /// <summary>Признак наличия выбранной точки.</summary>
    public bool HasSelectedPoint => SelectedPoint is not null;

    /// <summary>Локализованный статус выбранной точки.</summary>
    public string SelectedStatusText => LocalizeStatus(SelectedPoint?.Status);

    /// <summary>Локализованный итоговый статус расчёта.</summary>
    public string OverallStatusText => LocalizeStatus(Result.Status);

    /// <summary>Локализованный итог проверки исходных точек ForceSet.</summary>
    public string VerificationStatusText => Result.VerificationStatus switch
    {
        "ok" => Loc.S("OpenSeesSpatialVerificationOk"),
        "not_ok" => Loc.S("OpenSeesSpatialVerificationNotOk"),
        "indeterminate" => Loc.S("OpenSeesSpatialVerificationIndeterminate"),
        _ => Loc.S("OpenSeesSpatialVerificationUnavailable")
    };

    /// <summary>Локализованный тип геометрии результата.</summary>
    public string GeometryKindText => Result.GeometryKind switch
    {
        "plane" => Loc.S("OpenSeesSpatialGeometryPlane"),
        _ => Loc.S("OpenSeesSpatialGeometrySurface")
    };

    /// <summary>Минимальная рабочая продольная сила в кН.</summary>
    public double? EffectiveMinimumAxialForceKn => Result.EffectiveMinimumAxialForceN / 1000.0;

    /// <summary>Максимальная рабочая продольная сила в кН.</summary>
    public double? EffectiveMaximumAxialForceKn => Result.EffectiveMaximumAxialForceN / 1000.0;

    /// <summary>Точки Mx выбранной группы в кН·м.</summary>
    public IReadOnlyList<double?> PolarMxKnM => CurrentGroup?.Points
        .Select(point => point.MomentMxNm / 1000.0)
        .ToArray() ?? [];

    /// <summary>Точки My выбранной группы в кН·м.</summary>
    public IReadOnlyList<double?> PolarMyKnM => CurrentGroup?.Points
        .Select(point => point.MomentMyNm / 1000.0)
        .ToArray() ?? [];

    /// <summary>Кривизна Mx выбранной точки.</summary>
    public IReadOnlyList<double> HistoryCurvatureMx => SelectedPoint?.HistoryRows
        .Select(row => row.CurvatureMx).ToArray() ?? [];

    /// <summary>Кривизна My выбранной точки.</summary>
    public IReadOnlyList<double> HistoryCurvatureMy => SelectedPoint?.HistoryRows
        .Select(row => row.CurvatureMy).ToArray() ?? [];

    /// <summary>Момент Mx истории выбранной точки в кН·м.</summary>
    public IReadOnlyList<double> HistoryMomentMxKnM => SelectedPoint?.HistoryRows
        .Select(row => row.MomentMxNm / 1000.0).ToArray() ?? [];

    /// <summary>Момент My истории выбранной точки в кН·м.</summary>
    public IReadOnlyList<double> HistoryMomentMyKnM => SelectedPoint?.HistoryRows
        .Select(row => row.MomentMyNm / 1000.0).ToArray() ?? [];

    /// <summary>Строки таблицы углов выбранной группы.</summary>
    public ObservableCollection<PointRow> PointRows { get; } = [];

    /// <summary>Строки проверки исходных точек ForceSet.</summary>
    public ObservableCollection<DemandCheckRow> DemandCheckRows { get; } = [];

    /// <summary>Признак наличия проверяемых точек ForceSet.</summary>
    public bool HasDemandChecks => DemandCheckRows.Count > 0;

    /// <summary>Диагностика расчёта.</summary>
    public IReadOnlyList<string> Diagnostics => Result.Diagnostics;

    /// <summary>Статус расчёта.</summary>
    public string StatusText { get; }

    /// <summary>Признак ошибки или отсутствия пригодных точек.</summary>
    public bool HasError { get; }

    /// <summary>Текст ошибки/пустого результата.</summary>
    public string ErrorText { get; }

    /// <summary>Команда обновления графика.</summary>
    public ICommand RedrawCommand { get; }

    /// <summary>Команда подгонки масштаба графика.</summary>
    public ICommand FitCommand { get; }

    /// <summary>Строка одной точки для таблицы.</summary>
    public sealed record PointRow(
        double AngleDegrees,
        double? MomentMxKnM,
        double? MomentMyKnM,
        double? CurvatureMx,
        double? CurvatureMy,
        bool IsConverged,
        string Status,
        string StatusText,
        string ArtifactDirectory);

    /// <summary>Строка отчёта по проверке одной точки ForceSet.</summary>
    public sealed record DemandCheckRow(
        int Num,
        string Label,
        double AxialForceKn,
        double MomentMxKnM,
        double MomentMyKnM,
        bool IsInside,
        double? Utilization,
        string Status,
        string StatusText,
        string Diagnostic);

    /// <summary>Создаёт VM из сохранённого CalcResult.</summary>
    public OpenSeesSpatialInteractionResultVM(CalcResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        TaskTag = result.TaskTag;
        CreatedText = result.Created;
        StatusText = result.Status;
        RedrawCommand = new RelayCommand(_ => NotifySelectionChanged());
        FitCommand = new RelayCommand(_ => NotifySelectionChanged());

        try
        {
            SectionSpatialInteractionResult? parsed = JsonSerializer.Deserialize<SectionSpatialInteractionResult>(result.DataJson);
            bool hasTypedResult = parsed is not null &&
                (parsed.Points.Count > 0 ||
                 parsed.Slices.Count > 0 ||
                 parsed.DemandChecks.Count > 0 ||
                 parsed.Diagnostics.Count > 0 ||
                 parsed.EffectiveMinimumAxialForceN.HasValue ||
                 parsed.EffectiveMaximumAxialForceN.HasValue);

            if (result.Status == "error" && !hasTypedResult)
            {
                Result = new SectionSpatialInteractionResult { Status = result.Status };
                HasError = true;
                ErrorText = ReadError(result.DataJson);
            }
            else
            {
                Result = parsed
                    ?? new SectionSpatialInteractionResult { Status = result.Status };
                HasError = Result.Points.Count == 0;
                ErrorText = HasError ? Loc.S("OpenSeesSpatialResultEmpty") : "";
            }
        }
        catch (Exception exception)
        {
            Result = new SectionSpatialInteractionResult { Status = "error" };
            HasError = true;
            ErrorText = exception.Message;
        }

        foreach (SectionSpatialInteractionPoint point in Result.Points)
        {
            ForceGroup? group = _groups.FirstOrDefault(item => item.AxialForceN == point.AxialForceN);
            if (group is null)
            {
                group = new ForceGroup(point.AxialForceN);
                _groups.Add(group);
            }
            group.Points.Add(point);
            if (!group.Angles.Contains(point.AngleDegrees))
                group.Angles.Add(point.AngleDegrees);
        }

        AvailableAxialForces = _groups.Select(group => group.AxialForceN / 1000.0).ToArray();
        _selectedAxialForceN = _groups.FirstOrDefault()?.AxialForceN;
        _selectedAngle = CurrentGroup?.Angles.FirstOrDefault();
        RebuildPointRows();
        foreach (SectionSpatialInteractionDemandCheck check in Result.DemandChecks)
        {
            DemandCheckRows.Add(new DemandCheckRow(
                check.Num,
                check.Label,
                check.AxialForceN / 1000.0,
                check.MomentMxNm / 1000.0,
                check.MomentMyNm / 1000.0,
                check.IsInside,
                check.Utilization,
                check.Status,
                LocalizeCheckStatus(check.Status),
                check.Diagnostic));
        }
    }

    private ForceGroup? CurrentGroup => _groups.FirstOrDefault(
        group => group.AxialForceN == _selectedAxialForceN);

    private void NotifySelectionChanged()
    {
        RebuildPointRows();
        OnPropertyChanged(nameof(SelectedAxialForce));
        OnPropertyChanged(nameof(AvailableAngles));
        OnPropertyChanged(nameof(SelectedAngle));
        OnPropertyChanged(nameof(SelectedPoint));
        OnPropertyChanged(nameof(SelectedMomentMxKnM));
        OnPropertyChanged(nameof(SelectedMomentMyKnM));
        OnPropertyChanged(nameof(SelectedCurvatureMx));
        OnPropertyChanged(nameof(SelectedCurvatureMy));
        OnPropertyChanged(nameof(SelectedHistoryCount));
        OnPropertyChanged(nameof(HasSelectedPoint));
        OnPropertyChanged(nameof(SelectedStatusText));
        OnPropertyChanged(nameof(PolarMxKnM));
        OnPropertyChanged(nameof(PolarMyKnM));
        OnPropertyChanged(nameof(HistoryCurvatureMx));
        OnPropertyChanged(nameof(HistoryCurvatureMy));
        OnPropertyChanged(nameof(HistoryMomentMxKnM));
        OnPropertyChanged(nameof(HistoryMomentMyKnM));
    }

    private void RebuildPointRows()
    {
        PointRows.Clear();
        if (CurrentGroup is null)
            return;

        foreach (SectionSpatialInteractionPoint point in CurrentGroup.Points)
        {
            PointRows.Add(new PointRow(
                point.AngleDegrees,
                point.MomentMxNm / 1000.0,
                point.MomentMyNm / 1000.0,
                point.CurvatureMx,
                point.CurvatureMy,
                point.TerminalRow?.Converged == true,
                point.Status,
                point.Status == "ok"
                    ? Loc.S("OpenSeesSpatialStatusOk")
                    : point.Status == "not_converged"
                        ? Loc.S("OpenSeesSpatialStatusNotConverged")
                        : Loc.S("OpenSeesSpatialStatusError"),
                point.ArtifactDirectory));
        }
    }

    private static string ReadError(string json)
    {
        try
        {
            JsonElement root = JsonDocument.Parse(json).RootElement;
            return root.TryGetProperty("error", out JsonElement error)
                ? error.GetString() ?? json
                : json;
        }
        catch
        {
            return json;
        }
    }

    private static string LocalizeStatus(string? status) => status switch
    {
        "ok" => Loc.S("OpenSeesSpatialStatusOk"),
        "not_converged" => Loc.S("OpenSeesSpatialStatusNotConverged"),
        "error" => Loc.S("OpenSeesSpatialStatusError"),
        _ => status ?? ""
    };

    private static string LocalizeCheckStatus(string? status) => status switch
    {
        "inside" => Loc.S("OpenSeesSpatialCheckInside"),
        "outside" => Loc.S("OpenSeesSpatialCheckOutside"),
        "outside_axial_range" => Loc.S("OpenSeesSpatialCheckOutsideRange"),
        "not_converged" => Loc.S("OpenSeesSpatialCheckIndeterminate"),
        "no_surface" => Loc.S("OpenSeesSpatialCheckNoSurface"),
        _ => status ?? ""
    };

    private sealed class ForceGroup(double axialForceN)
    {
        public double AxialForceN { get; } = axialForceN;
        public List<double> Angles { get; } = [];
        public List<SectionSpatialInteractionPoint> Points { get; } = [];
    }
}
