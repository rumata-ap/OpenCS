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
        steps.FieldsUpdated += () => Changed?.Invoke();
        if (steps is CsfeaResultSteps csfea)
        {
            Csfea = csfea.Vm;
            Csfea.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == nameof(FemCsfeaResultVM.LongShare)) { OnPropertyChanged(nameof(LongShare)); Changed?.Invoke(); }
                else if (e.PropertyName == nameof(FemCsfeaResultVM.LongShareNote)) OnPropertyChanged(nameof(LongShareNote));
            };
        }
        if (steps is OpenSeesResultSteps openSees) OpenSees = openSees.Vm;
        RebuildNodeMap();
        Selection = new FemResultSelection(mesh);
        Selection.Changed += RefreshCard;
        // Шаг, поля шага, доля длительности — значения карточки меняются.
        Changed += RefreshCard;
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
    /// <param name="elemTags">КЭ показанного фрагмента; null — вся сетка.</param>
    public void UpdateSummary(FemResultComponent component, IEnumerable<string>? elemTags = null)
    {
        IEnumerable<int> nodes = elemTags == null
            ? Mesh.UsedNodes
            : elemTags.SelectMany(t => Mesh.NodesOf(t) ?? []).Distinct().Order();
        int? best = null;
        double bestValue = 0;
        foreach (int n in nodes)
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

    // ------------------------------------------------------------ величины КЭ: поля пластин, усилия стержней

    /// <summary>Поля пластин результата (CSfea), без перемещений; пусто — их нет.</summary>
    public IReadOnlyList<FemCsfeaOption<FemCsfeaShellField>> ShellFields => _steps.ShellFields;

    /// <summary>У результата есть усилия стержней и в сетке есть стержни.</summary>
    public bool HasBarForces => _steps.HasBarForces && Mesh.Bars.Count > 0;

    /// <summary>Значения поля готовы; false — досчитываются в фоне (по готовности — <see cref="Changed"/>).</summary>
    public bool PrepareShellField(FemCsfeaShellField field) => _steps.PrepareShellField(field);

    /// <summary>Поле пластины на выбранном шаге; null — нет данных.</summary>
    public double? ShellValue(string elemTag, FemCsfeaShellField field) =>
        int.TryParse(elemTag, NumberStyles.Integer, CultureInfo.InvariantCulture, out int n) ? _steps.ShellValue(n, field) : null;

    /// <summary>Усилие на концах стержня, кН или кН·м (знаки — как в окне результата OpenSees); null — нет данных.</summary>
    public (double I, double J)? BarEndValues(string elemTag, FemForceComponent component)
    {
        if (!int.TryParse(elemTag, NumberStyles.Integer, CultureInfo.InvariantCulture, out int n) || _steps.BarForces(n) is not { } f)
            return null;
        var pair = FemForceEndpointConverter.Convert(f, FemForceEndpointSignPolicy.OpenSeesDefault);
        return (FemForceEndpointConverter.ReadComponent(pair.Start, component) / 1e3,
            FemForceEndpointConverter.ReadComponent(pair.End, component) / 1e3);
    }

    /// <summary>Направление ординат эпюры на стержне: Qy и Mz — вдоль местной Ey, остальное — вдоль Ez.</summary>
    public Vector3D? BarDiagramAxis(string elemTag, FemForceComponent component) =>
        int.TryParse(elemTag, NumberStyles.Integer, CultureInfo.InvariantCulture, out int n) && _steps.BarAxes(n) is { } axes
            ? component is FemForceComponent.Qy or FemForceComponent.Mz ? axes.Ey : axes.Ez
            : null;

    public static string Unit(FemForceComponent component) =>
        component is FemForceComponent.N or FemForceComponent.Qy or FemForceComponent.Qz ? Loc.S("UnitKN") : Loc.S("UnitKNm");

    /// <summary>Итог по величине КЭ: наибольшая по модулю (или наибольшая у неотрицательных) с номером КЭ.</summary>
    /// <param name="label">Подпись величины с единицей.</param>
    /// <param name="values">Значения по КЭ показанного фрагмента.</param>
    public void UpdateElementSummary(string label, IEnumerable<KeyValuePair<string, double>> values)
    {
        string? best = null;
        double bestValue = 0;
        foreach (var (tag, v) in values)
            if (best == null || Math.Abs(v) > Math.Abs(bestValue)) { best = tag; bestValue = v; }
        SummaryText = best != null
            ? string.Format(CultureInfo.CurrentCulture, Loc.S("FemResultElementExtremum"), label, bestValue, best)
            : Loc.S("FemResultNoValues");
    }

    /// <summary>Итога нет (величина без экстремума — например, состояние).</summary>
    public void ClearSummary() => SummaryText = "";

    // ------------------------------------------------------------ CSfea: ширина раскрытия, наборы усилий

    /// <summary>VM результата CSfea; null — результат OpenSees.</summary>
    public FemCsfeaResultVM? Csfea { get; }
    public bool IsCsfea => Csfea != null;

    bool _isCrackFieldShown;
    /// <summary>Показана ширина раскрытия трещин — в строке шага доля длительной части.</summary>
    public bool IsCrackFieldShown
    {
        get => _isCrackFieldShown;
        set { if (_isCrackFieldShown == value) return; _isCrackFieldShown = value; OnPropertyChanged(); }
    }

    /// <summary>Доля длительной части усилий шага для ширины раскрытия (CSfea).</summary>
    public double LongShare
    {
        get => Csfea?.LongShare ?? 0;
        set { if (Csfea != null) Csfea.LongShare = value; }
    }

    public string LongShareNote => Csfea?.LongShareNote ?? "";
    public ICommand? LongShareFromSetsCommand => Csfea?.LongShareFromSetsCommand;

    /// <summary>VM результата OpenSees (пункты меню прежнего окна: 2D-эпюра, сечение, набор усилий, узел); null — CSfea.</summary>
    public FemAnalysisResultVM? OpenSees { get; }

    // ------------------------------------------------------------ выбор и карточка значений

    /// <summary>Выбранное в 3D-виде: КЭ сетки результата или узел.</summary>
    public FemResultSelection Selection { get; }

    FemResultCard? _card;
    /// <summary>Карточка значений выбранного на текущем шаге; null — ничего не выбрано.</summary>
    public FemResultCard? Card { get => _card; private set { _card = value; OnPropertyChanged(); } }

    void RefreshCard() => Card = BuildCard();

    static readonly FemResultComponent[] Displacements = [FemResultComponent.Ux, FemResultComponent.Uy, FemResultComponent.Uz, FemResultComponent.U];
    static readonly FemForceComponent[] Forces = Enum.GetValues<FemForceComponent>();

    static string Num(double v) => v.ToString("0.###", CultureInfo.CurrentCulture);

    static string DisplacementLabel(FemResultComponent c) => $"{ComponentLabel(c)}, {Loc.S("UnitMm")}";

    static string ForceLabel(FemForceComponent c) => $"{c}, {Unit(c)}";

    /// <summary>Карточка значений выбора: узел, один КЭ или группа КЭ (КонЭ).</summary>
    public FemResultCard? BuildCard()
    {
        if (Selection.Node is int node) return NodeCard(node);
        var tags = Selection.Elements.OrderBy(t => int.TryParse(t, out int n) ? n : int.MaxValue).ThenBy(t => t, StringComparer.Ordinal).ToList();
        return tags.Count switch
        {
            0 => null,
            1 when Selection.Mode == FemResultPickMode.Elements || Mesh.OwnerOf(tags[0]) == null => ElementCard(tags[0]),
            _ => GroupCard(tags),
        };
    }

    FemResultCard? NodeCard(int node)
    {
        if (!Mesh.Nodes.TryGetValue(node, out var p)) return null;
        var rows = new List<FemResultCardRow>
        {
            new(Loc.S("FemResultCardCoordinates"), $"{Num(p.X)}; {Num(p.Y)}; {Num(p.Z)}"),
        };
        foreach (var c in Displacements)
            if (NodeValue(node, c) is double v) rows.Add(new(DisplacementLabel(c), Num(v)));
        return new FemResultCard(string.Format(Loc.S("FemResultCardNode"), node), rows);
    }

    FemResultCard ElementCard(string tag)
    {
        bool bar = Mesh.IsBar(tag);
        var rows = new List<FemResultCardRow>();
        if (Mesh.OwnerOf(tag) is { } owner) rows.Add(new(Loc.S("FemResultCardOwner"), owner));
        foreach (var c in Displacements)
            if (Value(tag, c) is double v) rows.Add(new(DisplacementLabel(c), Num(v)));
        if (bar && HasBarForces)
            foreach (var c in Forces)
                if (BarEndValues(tag, c) is { } ends)
                    rows.Add(new(ForceLabel(c), string.Format(Loc.S("FemResultCardEnds"), Num(ends.I), Num(ends.J))));
        if (!bar)
            foreach (var f in ShellFields)
                if (ShellValue(tag, f.Value) is double v)
                    rows.Add(new(f.Label, f.Value == FemCsfeaShellField.State ? StateLabel(v) : Num(v)));
        return new FemResultCard(string.Format(Loc.S(bar ? "FemResultCardBar" : "FemResultCardShell"), tag), rows);
    }

    static string StateLabel(double v)
    {
        var styles = FemCsfeaResultVM.StateStyles;
        int k = (int)v;
        return k >= 0 && k < styles.Count ? Loc.S(styles[k].Key) : Num(v);
    }

    /// <summary>Несколько КЭ (КонЭ): по каждой величине — наибольшая по модулю с номером КЭ.</summary>
    FemResultCard GroupCard(IReadOnlyList<string> tags)
    {
        var rows = new List<FemResultCardRow>();
        void Extremum(string label, IEnumerable<(string Tag, double? Value)> values)
        {
            string? best = null;
            double bestValue = 0;
            foreach (var (t, value) in values)
                if (value is double v && (best == null || Math.Abs(v) > Math.Abs(bestValue))) { best = t; bestValue = v; }
            if (best != null) rows.Add(new(label, string.Format(Loc.S("FemResultCardExtremum"), Num(bestValue), best)));
        }

        foreach (var c in Displacements)
            Extremum(DisplacementLabel(c), tags.Select(t => (t, Value(t, c))));
        var bars = tags.Where(Mesh.IsBar).ToList();
        var shells = tags.Where(t => !Mesh.IsBar(t)).ToList();
        if (bars.Count > 0 && HasBarForces)
            foreach (var c in Forces)
                Extremum(ForceLabel(c), bars.Select(t => (t, BarEndValues(t, c) is { } e ? Math.Abs(e.I) >= Math.Abs(e.J) ? e.I : e.J : (double?)null)));
        if (shells.Count > 0)
            foreach (var f in ShellFields.Where(f => f.Value != FemCsfeaShellField.State))
                Extremum(f.Label, shells.Select(t => (t, ShellValue(t, f.Value))));

        var members = Selection.Members;
        string title = Selection.Mode == FemResultPickMode.Members && members.Count == 1
                       && members[0] is var m && tags.All(t => Mesh.OwnerOf(t) == m)
            ? string.Format(Loc.S("FemResultCardMember"), m, tags.Count)
            : members.Count > 0 && Selection.Mode == FemResultPickMode.Members
                ? string.Format(Loc.S("FemResultCardMembers"), members.Count, tags.Count)
                : string.Format(Loc.S("FemResultCardElements"), tags.Count);
        return new FemResultCard(title, rows);
    }
}