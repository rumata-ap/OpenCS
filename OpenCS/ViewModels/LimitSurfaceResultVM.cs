using System.IO;
using System.Text.Json;
using CScore;
using CScore.LimitSurface;
using OpenCS.LimitSurface;
using OpenCS.Utilites;

namespace OpenCS.ViewModels;

/// <summary>VM окна поверхности предельных усилий: выбор пользователя → модель сцены; WPF-типов нет.</summary>
public sealed class LimitSurfaceResultVM : ViewModelBase
{
    UtilizationMode _mode = UtilizationMode.All;
    AxisMode _axes = AxisMode.Relative;
    SurfaceColoring _coloring = SurfaceColoring.Active;
    bool _showEdges, _translucent, _showSlicePlane = true;
    ScenePoint? _selected;

    public LimitSurfaceResultVM(CalcResult result)
    {
        if (result.Status == "error")
        {
            ErrorText = ReadError(result.DataJson);
            return;
        }
        try { Scene = new LimitSurfaceScene(LimitSurfaceResultData.Parse(result.DataJson)); }
        catch (Exception ex) when (ex is LimitSurfaceFormatException or JsonException or FormatException
                                       or InvalidDataException or EndOfStreamException or InvalidOperationException)
        {
            ErrorText = Loc.S("LimitSurfaceFormatOutdated");
            return;
        }
        ColoringItems = Scene.SupportsSingleSign
            ? [SurfaceColoring.Active, SurfaceColoring.SingleSign, SurfaceColoring.ByN, SurfaceColoring.Mono]
            : [SurfaceColoring.Active, SurfaceColoring.ByN, SurfaceColoring.Mono];
        EtaWarning = Scene.Data.Warnings.Contains(LimitSurfaceWarnings.EtaNotApplied);
        Recompute();
    }

    /// <summary>Ошибка расчёта или устаревший формат результата; null — сцена построена.</summary>
    public string? ErrorText { get; }
    /// <summary>Модель сцены; null при ошибке.</summary>
    public LimitSurfaceScene? Scene { get; }
    /// <summary>Доступные раскраски («зона (8.53)» — только у ПС1).</summary>
    public IReadOnlyList<SurfaceColoring> ColoringItems { get; } = [];
    /// <summary>Точки набора с Кисп по текущему режиму.</summary>
    public IReadOnlyList<ScenePoint> Points { get; private set; } = [];
    /// <summary>Сводка по точкам.</summary>
    public SceneSummary Summary { get; private set; } = new(0, 0, 0);
    /// <summary>Точки набора первого порядка нанесены без η продольного изгиба.</summary>
    public bool EtaWarning { get; }
    /// <summary>Есть точки с началом луча вне тела поверхности.</summary>
    public bool HasAmbiguous => Summary.Ambiguous > 0;
    /// <summary>Нужно перерисовать 3D, срез и таблицу.</summary>
    public event EventHandler? SceneChanged;

    /// <summary>Режим луча Кисп.</summary>
    public UtilizationMode Mode
    {
        get => _mode;
        set { if (_mode == value) return; _mode = value; OnPropertyChanged(); Recompute(); }
    }

    /// <summary>Оси: абсолютные или относительные 0…1.</summary>
    public AxisMode Axes { get => _axes; set { _axes = value; OnPropertyChanged(); Raise(); } }
    /// <summary>Раскраска поверхности.</summary>
    public SurfaceColoring Coloring { get => _coloring; set { _coloring = value; OnPropertyChanged(); Raise(); } }
    /// <summary>Показывать рёбра сетки.</summary>
    public bool ShowEdges { get => _showEdges; set { _showEdges = value; OnPropertyChanged(); Raise(); } }
    /// <summary>Полупрозрачная поверхность.</summary>
    public bool Translucent { get => _translucent; set { _translucent = value; OnPropertyChanged(); Raise(); } }
    /// <summary>Показывать секущую плоскость текущего среза в 3D.</summary>
    public bool ShowSlicePlane { get => _showSlicePlane; set { _showSlicePlane = value; OnPropertyChanged(); Raise(); } }

    /// <summary>Выбранная точка — общая для всех вкладок.</summary>
    public ScenePoint? SelectedPoint { get => _selected; set { _selected = value; OnPropertyChanged(); Raise(); } }

    void Recompute()
    {
        if (Scene is null) return;
        int? sel = _selected?.Index;
        Points = Scene.EvaluatePoints(_mode);
        Summary = LimitSurfaceScene.Summarize(Points);
        _selected = sel is int i ? Points[i] : null;
        OnPropertyChanged(nameof(Points));
        OnPropertyChanged(nameof(Summary));
        OnPropertyChanged(nameof(HasAmbiguous));
        OnPropertyChanged(nameof(SelectedPoint));
        Raise();
    }

    void Raise() => SceneChanged?.Invoke(this, EventArgs.Empty);

    static string ReadError(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            return doc.RootElement.TryGetProperty("error", out var e) ? e.GetString() ?? json : json;
        }
        catch (JsonException) { return json; }
    }
}
