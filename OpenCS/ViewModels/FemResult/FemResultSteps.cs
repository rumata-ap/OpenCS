using System.ComponentModel;
using System.Windows.Media.Media3D;
using OpenCS.OpenSees.Structural;

namespace OpenCS.ViewModels.FemResult;

/// <summary>
/// Шаги и перемещения узлов сетки результата расчёта схемы. Реализации — адаптеры поверх VM прежних окон
/// результатов: логику чтения результата не повторяют.
/// </summary>
public interface IFemResultSteps
{
    /// <summary>Шагов больше одного (ползунок или список загружений).</summary>
    bool HasSteps { get; }
    /// <summary>Шаги — независимые загружения и комбинации линейного расчёта (выбор из списка, не ползунком).</summary>
    bool IsCaseList { get; }
    /// <summary>Имена шагов для списка загружений; пусто — не список.</summary>
    IReadOnlyList<string> StepNames { get; }
    int StepCount { get; }
    int SelectedStepIndex { get; set; }
    /// <summary>Подпись выбранного шага.</summary>
    string StepLabel { get; }
    event Action? StepChanged;

    /// <summary>На выбранном шаге есть перемещения узлов.</summary>
    bool HasDisplacements { get; }
    /// <summary>Перемещение узла сетки на выбранном шаге, м; null — нет данных по узлу.</summary>
    Vector3D? Displacement(int node);
    /// <summary>Наибольшее перемещение узла на выбранном шаге, м.</summary>
    double MaxDisplacement();

    /// <summary>Поля пластин результата (без перемещений); пусто — у результата их нет.</summary>
    IReadOnlyList<FemCsfeaOption<FemCsfeaShellField>> ShellFields { get; }
    /// <summary>Значение поля пластины на выбранном шаге; null — нет данных.</summary>
    double? ShellValue(int elem, FemCsfeaShellField field);
    /// <summary>Значения поля готовы (true) или досчитываются в фоне (false; по готовности — <see cref="FieldsUpdated"/>).</summary>
    bool PrepareShellField(FemCsfeaShellField field);
    /// <summary>Досчитаны значения полей выбранного шага.</summary>
    event Action? FieldsUpdated;

    /// <summary>У результата есть концевые силы стержней.</summary>
    bool HasBarForces { get; }
    /// <summary>Концевые силы стержня на выбранном шаге (местные оси, как localForce OpenSees); null — нет данных.</summary>
    FemElementEndForces? BarForces(int elem);
    /// <summary>Местные оси Ey, Ez стержня в расчёте; null — стержня нет.</summary>
    (Vector3D Ey, Vector3D Ez)? BarAxes(int elem);
}

/// <summary>Результат OpenSees поверх <see cref="FemAnalysisResultVM"/>: шаги λ нелинейного расчёта, перемещения шага.</summary>
public sealed class OpenSeesResultSteps : IFemResultSteps
{
    readonly Dictionary<int, Vector3D> _disp = new();

    public OpenSeesResultSteps(FemAnalysisResultVM vm)
    {
        Vm = vm;
        IndexStep();
        vm.PropertyChanged += OnVmPropertyChanged;
    }

    public FemAnalysisResultVM Vm { get; }
    public bool HasSteps => Vm.IsNonlinear && Vm.Steps.Count > 1;
    public bool IsCaseList => false;
    public IReadOnlyList<string> StepNames => [];
    public int StepCount => Vm.Steps.Count;
    public int SelectedStepIndex { get => Vm.SelectedStepIndex; set => Vm.SelectedStepIndex = value; }
    public string StepLabel => Vm.CurrentStepLabel;
    public event Action? StepChanged;

    public bool HasDisplacements => _disp.Count > 0;
    public Vector3D? Displacement(int node) => _disp.TryGetValue(node, out var d) ? d : null;
    public double MaxDisplacement() => _disp.Values.Select(v => v.Length).DefaultIfEmpty(0).Max();

    public IReadOnlyList<FemCsfeaOption<FemCsfeaShellField>> ShellFields => [];
    public double? ShellValue(int elem, FemCsfeaShellField field) => null;
    public bool PrepareShellField(FemCsfeaShellField field) => true;
    public event Action? FieldsUpdated { add { } remove { } }

    public bool HasBarForces => true;
    public FemElementEndForces? BarForces(int elem) => Vm.ElementForce(elem);
    public (Vector3D Ey, Vector3D Ez)? BarAxes(int elem) => Vm.ElementAxes(elem);

    void IndexStep()
    {
        _disp.Clear();
        foreach (var d in Vm.Displacements) _disp[d.NodeTag] = new Vector3D(d.Ux, d.Uy, d.Uz);
    }

    void OnVmPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(FemAnalysisResultVM.SelectedStepIndex)) return;
        IndexStep();
        StepChanged?.Invoke();
    }
}

/// <summary>Результат CSfea (секущий и линейный) поверх <see cref="FemCsfeaResultVM"/>: шаги или загружения, перемещения полей шага.</summary>
public sealed class CsfeaResultSteps : IFemResultSteps
{
    public CsfeaResultSteps(FemCsfeaResultVM vm)
    {
        Vm = vm;
        var stages = vm.Summary.Stages;
        StepNames = vm.IsLinear
            ? vm.Summary.Steps.Select(s => s.Stage >= 0 && s.Stage < stages.Count ? stages[s.Stage].Tag
                : s.N.ToString(System.Globalization.CultureInfo.CurrentCulture)).ToList()
            : [];
        vm.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(FemCsfeaResultVM.SelectedStepIndex)) StepChanged?.Invoke();
        };
        vm.CrackWidthsReady += () => FieldsUpdated?.Invoke();
        // Перемещения показаны отдельными величинами — из полей пластин их убираем.
        ShellFields = vm.HasShells
            ? vm.ShellFields.Where(f => f.Value is not (FemCsfeaShellField.None or FemCsfeaShellField.Ux or FemCsfeaShellField.Uy
                or FemCsfeaShellField.Uz)).ToList()
            : [];
    }

    public FemCsfeaResultVM Vm { get; }
    public bool HasSteps => Vm.Summary.Steps.Count > 1;
    public bool IsCaseList => Vm.IsLinear;
    public IReadOnlyList<string> StepNames { get; }
    public int StepCount => Vm.Summary.Steps.Count;
    public int SelectedStepIndex { get => Vm.SelectedStepIndex; set => Vm.SelectedStepIndex = value; }
    /// <summary>Подпись шага и, если поля шага не записаны, — пояснение, чьи поля показаны.</summary>
    public string StepLabel => Vm.HasFieldsNote ? $"{Vm.CurrentStepLabel} · {Vm.FieldsNote}" : Vm.CurrentStepLabel;
    public event Action? StepChanged;

    public bool HasDisplacements => Vm.Fields != null;
    public Vector3D? Displacement(int node) => Vm.Fields != null ? Vm.Displacement(node) : null;

    public double MaxDisplacement()
    {
        if (Vm.Fields is not { } f) return 0;
        double max = 0;
        var d = f.Displacements;
        for (int i = 0; i < f.NodeIds.Length; i++)
            max = Math.Max(max, Math.Sqrt(d[6 * i] * d[6 * i] + d[6 * i + 1] * d[6 * i + 1] + d[6 * i + 2] * d[6 * i + 2]));
        return max;
    }

    public IReadOnlyList<FemCsfeaOption<FemCsfeaShellField>> ShellFields { get; }
    public double? ShellValue(int elem, FemCsfeaShellField field) => Vm.ShellFieldValue(elem, field);
    public bool PrepareShellField(FemCsfeaShellField field) => Vm.PrepareShellField(field);
    public event Action? FieldsUpdated;

    public bool HasBarForces => Vm.HasBeams;
    public FemElementEndForces? BarForces(int elem) => Vm.BeamForces(elem);
    public (Vector3D Ey, Vector3D Ez)? BarAxes(int elem) => Vm.BeamAxes(elem);
}