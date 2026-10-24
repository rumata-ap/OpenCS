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
    bool _showEdges = true, _translucent, _showSlicePlane = true;
    ScenePoint? _selected;
    SliceKind _sliceKind = SliceKind.AtN;
    double _sliceLevel;
    int _family = 1;
    bool _hideOthers;

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
        _sliceLevel = MidLevel(_sliceKind);
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
    /// <summary>Изменился только срез: перерисовать 2D-график и секущую плоскость.</summary>
    public event EventHandler? SliceChanged;

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
    /// <summary>Показывать меридианы и параллели (у старых результатов без углов — рёбра треугольников).</summary>
    public bool ShowEdges { get => _showEdges; set { _showEdges = value; OnPropertyChanged(); Raise(); } }
    /// <summary>Полупрозрачная поверхность.</summary>
    public bool Translucent { get => _translucent; set { _translucent = value; OnPropertyChanged(); Raise(); } }
    /// <summary>Показывать секущую плоскость текущего среза в 3D.</summary>
    public bool ShowSlicePlane { get => _showSlicePlane; set { _showSlicePlane = value; OnPropertyChanged(); Raise(); } }

    /// <summary>Выбранная точка — общая для всех вкладок.</summary>
    public ScenePoint? SelectedPoint { get => _selected; set { _selected = value; OnPropertyChanged(); Raise(); } }

    /// <summary>Вид среза.</summary>
    public SliceKind SliceKind
    {
        get => _sliceKind;
        set
        {
            if (_sliceKind == value) return;
            _sliceKind = value;
            _sliceLevel = MidLevel(value);
            OnPropertyChanged();
            OnPropertyChanged(nameof(SliceLevel));
            RaiseSlice();
        }
    }

    /// <summary>Уровень среза: кН (AtN), кН·м (AtMx, AtMy), ° (NM).</summary>
    public double SliceLevel
    {
        get => _sliceLevel;
        set { if (_sliceLevel == value) return; _sliceLevel = value; OnPropertyChanged(); RaiseSlice(); }
    }

    /// <summary>1 — только уровень; 2…9 — плюс столько уровней с равным шагом по диапазону.</summary>
    public int SliceFamilyCount
    {
        get => _family;
        set { _family = Math.Clamp(value, 1, 9); OnPropertyChanged(); RaiseSlice(); }
    }

    /// <summary>Скрыть на срезе все точки, кроме выбранной.</summary>
    public bool HideOtherPoints { get => _hideOthers; set { _hideOthers = value; OnPropertyChanged(); RaiseSlice(); } }

    /// <summary>Текущий уровень и семейство (первым — текущий).</summary>
    public IReadOnlyList<double> SliceLevels
    {
        get
        {
            if (Scene is null) return [];
            var list = new List<double> { _sliceLevel };
            if (_family > 1)
            {
                var (lo, hi) = Scene.LevelRange(_sliceKind);
                for (int i = 1; i <= _family; i++) list.Add(lo + (hi - lo) * i / (_family + 1));
            }
            return list;
        }
    }

    /// <summary>Уровень среза — через выбранную точку.</summary>
    public void SliceThroughSelected()
    {
        if (_selected is null) return;
        SliceLevel = LimitSurfaceScene.LevelThrough(_sliceKind, _selected.Source);
    }

    double MidLevel(SliceKind kind)
    {
        if (Scene is null) return 0;
        if (kind == SliceKind.NM) return 0;
        var (lo, hi) = Scene.LevelRange(kind);
        return 0.5 * (lo + hi);
    }

    void RaiseSlice() => SliceChanged?.Invoke(this, EventArgs.Empty);

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
