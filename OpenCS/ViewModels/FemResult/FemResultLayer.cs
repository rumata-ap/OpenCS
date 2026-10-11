using System.Globalization;
using System.Windows.Input;
using System.Windows.Media.Media3D;
using CScore.Fem;
using OpenCS.Utilites;

namespace OpenCS.ViewModels.FemResult;

/// <summary>Величина мозаики результата расчёта.</summary>
public enum FemResultComponent { Ux, Uy, Uz, U }

/// <summary>
/// Показанный в основном 3D-виде результат расчёта постановки: шаг, деформированная схема и её масштаб, значения
/// величин по КЭ сетки, итог-экстремум. DataContext строки шага под 3D-видом.
/// </summary>
public sealed class FemResultLayer : ViewModelBase
{
    readonly IFemResultSteps _steps;
    readonly Dictionary<string, Point3D> _nodeMap = new(StringComparer.Ordinal);

    public FemResultLayer(FemAnalysis analysis, string label, IFemResultSteps steps, FemResultMesh mesh)
    {
        Analysis = analysis;
        Label = label;
        _steps = steps;
        Mesh = mesh;
        OriginalLines = mesh.EdgeLines(n => mesh.Nodes[n]);
        ResetDeformScaleCommand = new RelayCommand(_ => DeformScale = SuggestDeformScale(), _ => HasDisplacements);
        _deformScale = SuggestDeformScale();
        steps.StepChanged += OnStepChanged;
        RebuildNodeMap();
    }

    public FemAnalysis Analysis { get; }
    public string Label { get; }
    public FemResultMesh Mesh { get; }

    /// <summary>Шаг, масштаб или показ деформаций изменились — раскраску и геометрию нужно пересчитать.</summary>
    public event Action? Changed;

    // ------------------------------------------------------------ шаг

    public bool HasSteps => _steps.HasSteps;
    /// <summary>Шаги нелинейного расчёта — ползунком.</summary>
    public bool ShowStepSlider => _steps.HasSteps && !_steps.IsCaseList;
    /// <summary>Загружения и комбинации линейного расчёта — списком.</summary>
    public bool ShowCaseList => _steps.HasSteps && _steps.IsCaseList;
    public IReadOnlyList<string> StepNames => _steps.StepNames;
    public int MaxStepIndex => Math.Max(0, _steps.StepCount - 1);

    public int SelectedStepIndex
    {
        get => _steps.SelectedStepIndex;
        set => _steps.SelectedStepIndex = value;
    }

    public string StepLabel => _steps.StepLabel;

    void OnStepChanged()
    {
        OnPropertyChanged(nameof(SelectedStepIndex));
        OnPropertyChanged(nameof(StepLabel));
        OnPropertyChanged(nameof(HasDisplacements));
        OnPropertyChanged(nameof(DeformedShown));
        RebuildNodeMap();
        Changed?.Invoke();
    }

    // ------------------------------------------------------------ деформированная схема

    public bool HasDisplacements => _steps.HasDisplacements;

    // Деформированная схема по умолчанию выключена (решение пользователя 11.10).
    bool _showDeformed;
    public bool ShowDeformed
    {
        get => _showDeformed;
        set
        {
            if (value == _showDeformed) return;
            _showDeformed = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(DeformedShown));
            RebuildNodeMap();
            Changed?.Invoke();
        }
    }

    /// <summary>Деформированная схема показывается: включена и есть перемещения.</summary>
    public bool DeformedShown => _showDeformed && HasDisplacements;

    double _deformScale = 1;
    public double DeformScale
    {
        get => _deformScale;
        set
        {
            if (!FemScaleInput.IsValid(value) || value == _deformScale) return;
            _deformScale = value;
            OnPropertyChanged();
            RebuildNodeMap();
            Changed?.Invoke();
        }
    }

    public ICommand ResetDeformScaleCommand { get; }

    double SuggestDeformScale() => FemResultMesh.SuggestDeformScale(Mesh.Diagonal, _steps.MaxDisplacement());

    /// <summary>Положение узла на виде: деформированное (если показано) или исходное.</summary>
    public Point3D Position(int node)
    {
        var p = Mesh.Nodes[node];
        return DeformedShown && _steps.Displacement(node) is { } d ? p + d * _deformScale : p;
    }

    /// <summary>Положения узлов КЭ на виде по тегу узла (как карта узлов 3D-вида).</summary>
    public IReadOnlyDictionary<string, Point3D> NodeMap => _nodeMap;

    /// <summary>Рёбра исходной сетки — фоном под деформированной.</summary>
    public Point3DCollection OriginalLines { get; }

    void RebuildNodeMap()
    {
        _nodeMap.Clear();
        foreach (int n in Mesh.UsedNodes) _nodeMap[n.ToString(CultureInfo.InvariantCulture)] = Position(n);
    }

    // ------------------------------------------------------------ величины

    /// <summary>Величина в узле на выбранном шаге, мм; null — нет данных.</summary>
    public double? NodeValue(int node, FemResultComponent component)
    {
        if (_steps.Displacement(node) is not { } d) return null;
        return 1e3 * component switch
        {
            FemResultComponent.Ux => d.X,
            FemResultComponent.Uy => d.Y,
            FemResultComponent.Uz => d.Z,
            _ => d.Length,
        };
    }

    /// <summary>Величина на КЭ — среднее по его узлам, мм; null — нет данных ни в одном узле.</summary>
    public double? Value(string elemTag, FemResultComponent component)
    {
        if (Mesh.NodesOf(elemTag) is not { } nodes) return null;
        double sum = 0;
        int count = 0;
        foreach (int n in nodes)
            if (NodeValue(n, component) is double v) { sum += v; count++; }
        return count > 0 ? sum / count : null;
    }

    string _summaryText = "";
    /// <summary>Экстремум показанной величины с номером узла.</summary>
    public string SummaryText { get => _summaryText; private set { if (_summaryText == value) return; _summaryText = value; OnPropertyChanged(); } }

    /// <summary>Пересчитать экстремум величины: наибольшая по модулю (|u| — наибольшая) по узлам КЭ.</summary>
    public void UpdateSummary(FemResultComponent component)
    {
        int? best = null;
        double bestValue = 0;
        foreach (int n in Mesh.UsedNodes)
            if (NodeValue(n, component) is double v && (best == null || Math.Abs(v) > Math.Abs(bestValue)))
            {
                best = n;
                bestValue = v;
            }
        SummaryText = best is int node
            ? string.Format(CultureInfo.CurrentCulture, Loc.S("FemResultExtremum"), ComponentLabel(component), bestValue, node)
            : Loc.S("FemResultNoDisplacements");
    }

    public static string ComponentLabel(FemResultComponent component) => component switch
    {
        FemResultComponent.Ux => "ux",
        FemResultComponent.Uy => "uy",
        FemResultComponent.Uz => "uz",
        _ => "|u|",
    };
}
