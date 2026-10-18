using System.Globalization;
using System.Windows.Media.Media3D;
using OpenCS.OpenSees.Structural;
using OpenCS.Services;
using OpenCS.Utilites;

namespace OpenCS.ViewModels.FemResult;

/// <summary>
/// Результат постановки «Расчётные длины — CSfea» (<see cref="FemBucklingSummary"/>): шаги — формы потери устойчивости
/// по сочетаниям списком «сочетание · форма k · λ», перемещения — форма, нормированная на max |u| = 1 (без единиц), и
/// промежуточные точки дробления стержней (<see cref="BarPoints"/>) — стержень из одного КЭ изгибается ломаной. Форма
/// читается из записи результата при выборе шага.
/// </summary>
public sealed class BucklingResultSteps : IFemResultSteps
{
    readonly Func<int, byte[]?> _load;
    readonly List<int> _records = [];
    int _index;
    FemBucklingMode? _mode;

    /// <param name="load">Запись формы по номеру; null — не записана.</param>
    public BucklingResultSteps(FemBucklingSummary summary, Func<int, byte[]?> load)
    {
        Summary = summary;
        _load = load;
        var names = new List<string>();
        var ci = CultureInfo.CurrentCulture;
        foreach (var c in summary.Cases)
            for (int k = 0; k < c.Lambda.Length; k++)
            {
                _records.Add(c.FirstRecord + k);
                names.Add(string.Format(ci, Loc.S("FemBucklingStepName"), c.Tag, k + 1, c.Lambda[k]));
            }
        StepNames = names;
        Load();
    }

    public FemBucklingSummary Summary { get; }

    public bool HasSteps => _records.Count > 1;
    public bool IsCaseList => true;
    public IReadOnlyList<string> StepNames { get; }
    public int StepCount => _records.Count;

    public int SelectedStepIndex
    {
        get => _index;
        set
        {
            int i = Math.Clamp(value, 0, Math.Max(0, _records.Count - 1));
            if (i == _index) return;
            _index = i;
            Load();
            StepChanged?.Invoke();
        }
    }

    public string StepLabel => _index < StepNames.Count ? StepNames[_index] : Loc.S("FemBucklingNoModes");
    public event Action? StepChanged;

    void Load() => _mode = _index < _records.Count && _load(_records[_index]) is { } data ? FemBucklingModeCodec.Decode(data) : null;

    public bool HasDisplacements => _mode is { Nodes.Count: > 0 };

    public Vector3D? Displacement(int node) =>
        _mode != null && _mode.Nodes.TryGetValue(node, out var u) ? new Vector3D(u.X, u.Y, u.Z) : null;

    /// <summary>Перемещения промежуточных точек дробления стержня по порядку от I к J; null — стержень не дробился.</summary>
    public IReadOnlyList<Vector3D>? BarPoints(int elem) =>
        _mode != null && _mode.BarPoints.TryGetValue(elem, out var p) ? p.Select(u => new Vector3D(u.X, u.Y, u.Z)).ToList() : null;

    public double MaxDisplacement() => _mode == null ? 0
        : _mode.Nodes.Values.Concat(_mode.BarPoints.Values.SelectMany(p => p))
            .Select(u => Math.Sqrt(u.X * u.X + u.Y * u.Y + u.Z * u.Z)).DefaultIfEmpty(0).Max();

    public bool IsEnvelope => false;
    public double? DisplacementLength(int node) => null;

    public IReadOnlyList<FemCsfeaOption<FemCsfeaShellField>> ShellFields => [];
    public double? ShellValue(int elem, FemCsfeaShellField field) => null;
    public bool PrepareShellField(FemCsfeaShellField field) => true;
    public event Action? FieldsUpdated { add { } remove { } }

    public bool HasBarForces => false;
    public FemElementEndForces? BarForces(int elem) => null;
    public (Vector3D Ey, Vector3D Ez)? BarAxes(int elem) => null;
}
