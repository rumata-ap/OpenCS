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
        ResetDeformScaleCommand = new RelayCommand(_ =>
        {
            DeformScale = SuggestDeformScale();
            _autoScale = true;
        }, _ => HasDisplacements);
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
        OnPropertyChanged(nameof(HasReactions));
        OnPropertyChanged(nameof(ReactionSummaryText));
        RebuildNodeMap();
        Changed?.Invoke();
    }

    // ------------------------------------------------------------ реакции опор (CSfea)

    /// <summary>На показанном шаге есть реакции опор.</summary>
    public bool HasReactions => Csfea?.HasReactions == true;

    /// <summary>Итог реакций шага: ΣR по типам опор, ΣF и небаланс; пусто — нет данных.</summary>
    public string ReactionSummaryText => Csfea?.ReactionSummaryText ?? "";

    bool _showReactions;
    /// <summary>Стрелки сил реакций опор на виде.</summary>
    public bool ShowReactions
    {
        get => _showReactions;
        set
        {
            if (value == _showReactions) return;
            _showReactions = value;
            OnPropertyChanged();
            Changed?.Invoke();
        }
    }

    double _reactionScale = 1;
    /// <summary>Множитель длины стрелок реакций (1 — наибольшая реакция в 8 % диагонали сетки).</summary>
    public double ReactionScale
    {
        get => _reactionScale;
        set
        {
            if (!FemScaleInput.IsValid(value) || value == _reactionScale) return;
            _reactionScale = value;
            OnPropertyChanged();
            if (_showReactions) Changed?.Invoke();
        }
    }

    /// <summary>Длина стрелки наибольшей реакции — доля диагонали сетки.</summary>
    public const double ReactionArrowRatio = 0.08;

    /// <summary>
    /// Отрезки стрелок сил реакций (пары точек): остриё — в узле на положении слоя, стрелка — вдоль силы, действующей на
    /// схему; длина пропорциональна силе (наибольшая — <see cref="ReactionArrowRatio"/> диагонали × множитель). Узлы вне
    /// <paramref name="positions"/> и силы меньше 1e-6 наибольшей не рисуются. Null — стрелки выключены или реакций нет.
    /// </summary>
    public Point3DCollection? ReactionArrows(IReadOnlyDictionary<string, Point3D> positions, Func<int, bool>? shown = null)
    {
        if (!_showReactions || Csfea is not { } vm) return null;
        var forces = vm.ReactionForces().Where(r => shown?.Invoke(r.Node) != false).ToList();
        double max = forces.Count > 0 ? forces.Max(r => r.Force.Length) : 0;
        if (!(max > 0)) return null;
        double size = Mesh.Diagonal * ReactionArrowRatio * _reactionScale;
        var pts = new Point3DCollection();
        foreach (var (node, f) in forces)
        {
            double len = f.Length;
            if (len < 1e-6 * max
                || !positions.TryGetValue(node.ToString(CultureInfo.InvariantCulture), out var tip)) continue;
            var d = f / len;
            double l = size * len / max;
            var tail = tip - d * l;
            pts.Add(tail);
            pts.Add(tip);
            // Остриё — два штриха под ~25° к оси стрелки.
            var w = Vector3D.CrossProduct(d, Math.Abs(d.Z) < 0.9 ? new Vector3D(0, 0, 1) : new Vector3D(1, 0, 0));
            w.Normalize();
            double h = Math.Min(0.25 * l, 0.3 * size);
            pts.Add(tip); pts.Add(tip - d * h + w * (0.45 * h));
            pts.Add(tip); pts.Add(tip - d * h - w * (0.45 * h));
        }
        pts.Freeze();
        return pts.Count > 0 ? pts : null;
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

    bool _showOriginal = true;
    /// <summary>Под деформированной схемой — контур исходной сетки.</summary>
    public bool ShowOriginal
    {
        get => _showOriginal;
        set
        {
            if (value == _showOriginal) return;
            _showOriginal = value;
            OnPropertyChanged();
            Changed?.Invoke();
        }
    }

    double _deformScale = 1;
    public double DeformScale
    {
        get => _deformScale;
        set
        {
            if (!FemScaleInput.IsValid(value) || value == _deformScale) return;
            _deformScale = value;
            _autoScale = false;
            OnPropertyChanged();
            RebuildNodeMap();
            Changed?.Invoke();
        }
    }

    public ICommand ResetDeformScaleCommand { get; }

    // Масштаб предложен (при создании или сбросом) и не задавался вручную — следует за фрагментом.
    bool _autoScale = true;
    // Узлы КЭ показанного фрагмента; null — вся сетка.
    IReadOnlyList<int>? _scaleNodes;

    /// <summary>
    /// Показанный фрагмент: предложенный масштаб деформаций считается по узлам его КЭ. Если масштаб не задавался
    /// вручную, он пересчитывается сразу — вместе с положениями узлов, но без <see cref="Changed"/>: вызывается
    /// при сборке сцены, которая затем читает <see cref="NodeMap"/>.
    /// </summary>
    /// <param name="elemTags">КЭ фрагмента; null — вся сетка.</param>
    public void SetScaleScope(IEnumerable<string>? elemTags)
    {
        IReadOnlyList<int>? nodes = elemTags?.SelectMany(t => Mesh.NodesOf(t) ?? []).Distinct().Order().ToArray();
        if (nodes == null ? _scaleNodes == null : _scaleNodes != null && nodes.SequenceEqual(_scaleNodes)) return;
        _scaleNodes = nodes;
        if (!_autoScale) return;
        double scale = SuggestDeformScale();
        if (scale == _deformScale) return;
        _deformScale = scale;
        OnPropertyChanged(nameof(DeformScale));
        RebuildNodeMap();
    }

    /// <summary>5 % диагонали показанных узлов к их наибольшему перемещению на выбранном шаге.</summary>
    double SuggestDeformScale()
    {
        if (_scaleNodes == null) return FemResultMesh.SuggestDeformScale(Mesh.Diagonal, _steps.MaxDisplacement());
        if (_scaleNodes.Count == 0) return _deformScale;
        double max = 0;
        foreach (int n in _scaleNodes)
            if (_steps.Displacement(n) is { } d) max = Math.Max(max, d.Length);
        return FemResultMesh.SuggestDeformScale(FemResultMesh.DiagonalOf(_scaleNodes.Select(n => Mesh.Nodes[n])), max);
    }

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

    /// <summary>
    /// Направление ординат эпюры на стержне (см. <see cref="DiagramAxis"/>). На деформированной схеме местные оси
    /// поворачиваются вместе со стержнем — кратчайшим поворотом исходной хорды в показанную, — и ординаты остаются
    /// перпендикулярны показанному стержню (при коротационном расчёте CSfea это и есть оси, в которых выданы силы концов).
    /// </summary>
    public Vector3D? BarDiagramAxis(string elemTag, FemForceComponent component)
    {
        if (!int.TryParse(elemTag, NumberStyles.Integer, CultureInfo.InvariantCulture, out int n) || _steps.BarAxes(n) is not { } axes)
            return null;
        var axis = DiagramAxis(axes.Ey, axes.Ez, component);
        return DeformedShown && Mesh.NodesOf(elemTag) is [int i, int j]
            ? FollowChord(axis, Mesh.Nodes[j] - Mesh.Nodes[i], Position(j) - Position(i))
            : axis;
    }

    /// <summary>Вектор <paramref name="v"/>, повёрнутый кратчайшим поворотом направления <paramref name="from"/> в <paramref name="to"/>.</summary>
    public static Vector3D FollowChord(Vector3D v, Vector3D from, Vector3D to)
    {
        if (from.Length < 1e-300 || to.Length < 1e-300) return v;
        from.Normalize();
        to.Normalize();
        double c = Vector3D.DotProduct(from, to);
        if (c < -1 + 1e-9) return v;   // разворот на 180° неоднозначен — оставить как есть
        var w = Vector3D.CrossProduct(from, to);
        // Формула Родрига для оси w (|w| = sin θ) и cos θ = c.
        return v * c + Vector3D.CrossProduct(w, v) + w * (Vector3D.DotProduct(w, v) / (1 + c));
    }

    /// <summary>
    /// Направление, в которое откладывается положительная ордината эпюры: Qy и Mz — вдоль местной Ey, My — против Ez,
    /// остальное — вдоль Ez. Моменты при знаках OpenSees (<see cref="FemForceEndpointSignPolicy.OpenSeesDefault"/>)
    /// так строятся на растянутых волокнах: положительный Mz растягивает волокна со стороны +Ey, положительный My —
    /// со стороны −Ez.
    /// </summary>
    public static Vector3D DiagramAxis(Vector3D ey, Vector3D ez, FemForceComponent component) => component switch
    {
        FemForceComponent.Qy or FemForceComponent.Mz => ey,
        FemForceComponent.My => -ez,
        _ => ez,
    };

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

    string _barSummaryText = "";
    /// <summary>Экстремум усилия стержней, показанного эпюрами (свой список мозаики); пусто — эпюр нет.</summary>
    public string BarSummaryText
    {
        get => _barSummaryText;
        private set
        {
            if (_barSummaryText == value) return;
            _barSummaryText = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(HasBarSummary));
        }
    }

    public bool HasBarSummary => _barSummaryText != "";

    /// <summary>Итог по усилию стержней: наибольшее по модулю с номером КЭ.</summary>
    public void UpdateBarSummary(string label, IEnumerable<KeyValuePair<string, double>> values)
    {
        string? best = null;
        double bestValue = 0;
        foreach (var (tag, v) in values)
            if (best == null || Math.Abs(v) > Math.Abs(bestValue)) { best = tag; bestValue = v; }
        BarSummaryText = best != null
            ? string.Format(CultureInfo.CurrentCulture, Loc.S("FemResultBarExtremum"), label, bestValue, best)
            : "";
    }

    public void ClearBarSummary() => BarSummaryText = "";

    // ------------------------------------------------------------ CSfea: ширина раскрытия, наборы усилий

    /// <summary>VM результата CSfea; null — результат OpenSees.</summary>
    public FemCsfeaResultVM? Csfea { get; }
    public bool IsCsfea => Csfea != null;

    FemCsfeaShellField? _shownCrackField;
    /// <summary>Показанная мозаика ширины раскрытия трещин; null — показано другое.</summary>
    public FemCsfeaShellField? ShownCrackField
    {
        get => _shownCrackField;
        set
        {
            if (_shownCrackField == value) return;
            _shownCrackField = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(IsCrackFieldShown));
        }
    }

    /// <summary>Показана ширина раскрытия трещин — в строке шага доля длительной части.</summary>
    public bool IsCrackFieldShown => _shownCrackField != null;

    /// <summary>
    /// Штрихи линий трещин (пары точек) у пластин показанной мозаики ширины раскрытия — на положениях узлов слоя;
    /// null — мозаика ширины раскрытия не показана.
    /// </summary>
    /// <param name="shellTags">Пластины показанного фрагмента.</param>
    public Point3DCollection? CrackStrokes(IEnumerable<string> shellTags) =>
        Csfea != null && _shownCrackField is { } field
            ? Csfea.CrackStrokes(field,
                shellTags.Select(t => int.TryParse(t, NumberStyles.Integer, CultureInfo.InvariantCulture, out int n) ? n : -1),
                Position)
            : null;

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
        if (Csfea?.Reaction(node) is { } r)
        {
            rows.Add(new(Loc.S("FemResultCardSupports"), FemCsfeaResultVM.ReactionKindText(r.Kinds)));
            string[] labels = ["Rx", "Ry", "Rz", "Mx", "My", "Mz"];
            for (int c = 0; c < 6; c++)
                if (r.Values[c] != 0)
                    rows.Add(new($"{labels[c]}, {Loc.S(c < 3 ? "UnitKN" : "UnitKNm")}", Num(r.Values[c] / 1e3)));
        }
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