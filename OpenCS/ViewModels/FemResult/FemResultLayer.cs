using System.Globalization;
using System.Windows.Input;
using System.Windows.Media.Media3D;
using CScore.Fem;
using OpenCS.Services;
using OpenCS.Utilites;

namespace OpenCS.ViewModels.FemResult;

/// <summary>Величина мозаики результата расчёта.</summary>
public enum FemResultComponent { Ux, Uy, Uz, U }

/// <summary>
/// Величины расчётных длин на стержнях (постановка «Расчётные длины — CSfea»): КЗУ элемента (наименьший по осям),
/// μ и lef по осям СП 16, источник μ по осям (категории).
/// </summary>
public enum FemBucklingQuantity { Kzu, MuX, MuY, LefX, LefY, SourceX, SourceY }

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
                else if (e.PropertyName == nameof(FemCsfeaResultVM.CrackByPair))
                {
                    OnPropertyChanged(nameof(CrackByPair));
                    OnPropertyChanged(nameof(CrackByShare));
                    OnPropertyChanged(nameof(CrackLongText));
                }
                // Ход расчёта огибающей — в подписи шага.
                else if (e.PropertyName == nameof(FemCsfeaResultVM.FieldsNote))
                {
                    OnPropertyChanged(nameof(StepLabel));
                    OnPropertyChanged(nameof(HeaderText));
                }
            };
        }
        if (steps is OpenSeesResultSteps openSees) OpenSees = openSees.Vm;
        if (steps is BucklingResultSteps buckling)
        {
            // Формы потери устойчивости — суть результата: деформированная схема сразу.
            Buckling = buckling;
            _showDeformed = true;
            foreach (var u in buckling.Summary.Units)
                foreach (string tag in u.ElemTags) _bucklingUnits[tag] = u;
        }
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

    /// <summary>Комбинация или огибающая, показанная в линейном результате CSfea; null — загружение или шаг.</summary>
    public FemCsfeaCaseKey? ShownKey
    {
        get => Csfea?.ShownKey;
        set { if (Csfea != null) Csfea.ShownKey = value; }
    }

    /// <summary>Показана огибающая группы комбинаций (см. <see cref="IFemResultSteps.IsEnvelope"/>).</summary>
    public bool IsEnvelope => _steps.IsEnvelope;

    void OnStepChanged()
    {
        OnPropertyChanged(nameof(SelectedStepIndex));
        OnPropertyChanged(nameof(StepLabel));
        OnPropertyChanged(nameof(HeaderText));
        OnPropertyChanged(nameof(HasDisplacements));
        OnPropertyChanged(nameof(CanShowDeformed));
        OnPropertyChanged(nameof(DeformedShown));
        OnPropertyChanged(nameof(IsEnvelope));
        OnPropertyChanged(nameof(HasReactions));
        OnPropertyChanged(nameof(ReactionSummaryText));
        RebuildNodeMap();
        Changed?.Invoke();
    }

    // ------------------------------------------------------------ реакции опор (CSfea)

    /// <summary>На показанном шаге есть реакции опор.</summary>
    public bool HasReactions => Csfea?.HasReactions == true && !IsEnvelope;

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

    bool _showReactionValues = true;
    /// <summary>Подписи значений у стрелок реакций (|R|, кН), как у эпюр.</summary>
    public bool ShowReactionValues
    {
        get => _showReactionValues;
        set
        {
            if (value == _showReactionValues) return;
            _showReactionValues = value;
            OnPropertyChanged();
            if (_showReactions) Changed?.Invoke();
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
        => ReactionGlyphs(positions, shown).Arrows;

    /// <summary>
    /// Стрелки реакций (<see cref="ReactionArrows"/>) и подписи |R| (кН) у их хвостов — по убыванию силы (порядок
    /// отбора подписей без наложения); подписей нет, если выключены <see cref="ShowReactionValues"/>.
    /// </summary>
    public (Point3DCollection? Arrows, IReadOnlyList<(Point3D Position, double ValueKn)> Labels) ReactionGlyphs(
        IReadOnlyDictionary<string, Point3D> positions, Func<int, bool>? shown = null)
    {
        if (!_showReactions || !HasReactions || Csfea is not { } vm) return (null, []);
        var forces = vm.ReactionForces().Where(r => shown?.Invoke(r.Node) != false).ToList();
        double max = forces.Count > 0 ? forces.Max(r => r.Force.Length) : 0;
        if (!(max > 0)) return (null, []);
        double size = Mesh.Diagonal * ReactionArrowRatio * _reactionScale;
        var pts = new Point3DCollection();
        var labels = new List<(Point3D, double)>();
        foreach (var (node, f) in forces)
        {
            double len = f.Length;
            if (len < 1e-6 * max
                || !positions.TryGetValue(node.ToString(CultureInfo.InvariantCulture), out var tip)) continue;
            var d = f / len;
            double l = size * len / max;
            var tail = tip - d * l;
            if (_showReactionValues) labels.Add((tail, len / 1e3));
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
        return pts.Count > 0 ? (pts, labels.OrderByDescending(x => x.Item2).ToList()) : (null, []);
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

    /// <summary>Деформированную схему можно показать: есть перемещения и это не огибающая.</summary>
    public bool CanShowDeformed => HasDisplacements && !IsEnvelope;

    /// <summary>Деформированная схема показывается: включена, есть перемещения и это не огибающая.</summary>
    public bool DeformedShown => _showDeformed && CanShowDeformed;

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
            _ => _steps.DisplacementLength(node) ?? d.Length,
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
    /// <summary>min и max показанной величины мозаики с местом (узел, КЭ) — подпись внизу 3D-вида.</summary>
    public string SummaryText { get => _summaryText; private set { if (_summaryText == value) return; _summaryText = value; OnPropertyChanged(); } }

    /// <summary>Шапка 3D-вида: расчёт и показанное (шаг, загружение, комбинация или огибающая).</summary>
    public string HeaderText => string.Format(Loc.S("FemResultHeader"), Label, StepLabel)
        + (Csfea?.StiffnessHeader is { } stiffness ? Environment.NewLine + stiffness : "");

    /// <summary>Линейный на жёсткостях секущего: источник пересчитан или пропал после расчёта; иначе null.</summary>
    public string? StiffnessStaleNote => Csfea?.StiffnessStaleNote;

    /// <summary>
    /// Наименьшее и наибольшее значение с местом: «label: min v (место) · max v (место)». Источник — комбинация
    /// огибающей, давшая значение (пусто — не огибающая).
    /// </summary>
    static string MinMaxText<T>(string label, IEnumerable<(T Where, double Min, string MinSource, double Max, string MaxSource)> values,
        Func<T, string> place)
    {
        (T Where, double V, string Source)? min = null, max = null;
        foreach (var x in values)
        {
            if (min == null || x.Min < min.Value.V) min = (x.Where, x.Min, x.MinSource);
            if (max == null || x.Max > max.Value.V) max = (x.Where, x.Max, x.MaxSource);
        }
        if (min is not { } lo || max is not { } hi) return "";
        string At((T Where, double V, string Source) e) => e.Source == "" ? place(e.Where) : $"{place(e.Where)}, {e.Source}";
        return string.Format(CultureInfo.CurrentCulture, Loc.S("FemResultMinMax"), label, Num(lo.V), At(lo), Num(hi.V), At(hi));
    }

    static string AtNode(int node) => string.Format(Loc.S("FemResultAtNode"), node);
    static string AtElement(string tag) => string.Format(Loc.S("FemResultAtElement"), tag);

    /// <summary>
    /// Пересчитать min/max перемещения по узлам КЭ показанного фрагмента. У огибающей min — по ветви min, max — по
    /// ветви max (с комбинацией); выбор |max| / max / min влияет только на раскраску.
    /// </summary>
    /// <param name="elemTags">КЭ показанного фрагмента; null — вся сетка.</param>
    public void UpdateSummary(FemResultComponent component, IEnumerable<string>? elemTags = null)
    {
        IEnumerable<int> nodes = elemTags == null
            ? Mesh.UsedNodes
            : elemTags.SelectMany(t => Mesh.NodesOf(t) ?? []).Distinct().Order();
        var values = IsEnvelope
            ? nodes.Select(n => (n, NodeEnvelope(n, component))).Where(x => x.Item2 != null)
                .Select(x => (x.n, x.Item2!.Value.Min, x.Item2.Value.MinSource, x.Item2.Value.Max, x.Item2.Value.MaxSource))
            : nodes.Select(n => (n, NodeValue(n, component))).Where(x => x.Item2 != null)
                .Select(x => (x.n, x.Item2!.Value, "", x.Item2.Value, ""));
        string text = MinMaxText($"{ComponentLabel(component)}, {Loc.S("UnitMm")}", values, AtNode);
        SummaryText = text != "" ? text : Loc.S("FemResultNoDisplacements");
    }

    /// <summary>Огибающая перемещения узла в мм (ux, uy, uz, |u|); null — огибающая не показана.</summary>
    FemCsfeaEnvelopeValue? NodeEnvelope(int node, FemResultComponent component) =>
        Csfea?.EnvelopeNode(node, component switch
        {
            FemResultComponent.Ux => 0,
            FemResultComponent.Uy => 1,
            FemResultComponent.Uz => 2,
            _ => 6,
        })?.Scale(1e3);

    /// <summary>
    /// Огибающая усилия на концах стержня, кН или кН·м, знаки — как у <see cref="BarEndValues"/> (при смене знака ветви
    /// меняются местами); null — огибающая не показана.
    /// </summary>
    public (FemCsfeaEnvelopeValue I, FemCsfeaEnvelopeValue J)? BarEndEnvelope(string elemTag, FemForceComponent component)
    {
        if (Csfea is not { IsEnvelopeShown: true } vm
            || !int.TryParse(elemTag, NumberStyles.Integer, CultureInfo.InvariantCulture, out int n)) return null;
        int c = component switch
        {
            FemForceComponent.N => 0,
            FemForceComponent.Qy => 1,
            FemForceComponent.Qz => 2,
            FemForceComponent.Mx => 3,
            FemForceComponent.My => 4,
            _ => 5,
        };
        var p = FemForceEndpointSignPolicy.OpenSeesDefault;
        double[] si = [p.NI, p.QyI, p.QzI, p.MxI, p.MyI, p.MzI], sj = [p.NJ, p.QyJ, p.QzJ, p.MxJ, p.MyJ, p.MzJ];
        if (vm.EnvelopeBeam(n, c) is not { } i || vm.EnvelopeBeam(n, c + 6) is not { } j) return null;
        return (i.Scale(si[c] / 1e3), j.Scale(sj[c] / 1e3));
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

    /// <summary>Итог по величине КЭ: min и max с номером КЭ (у огибающей — по ветвям, с комбинацией).</summary>
    /// <param name="label">Подпись величины с единицей.</param>
    /// <param name="values">Значения по КЭ показанного фрагмента.</param>
    /// <param name="field">Поле пластины CSfea — для ветвей огибающей; null — без них.</param>
    public void UpdateElementSummary(string label, IEnumerable<KeyValuePair<string, double>> values, FemCsfeaShellField? field = null)
    {
        IEnumerable<(string, double, string, double, string)> rows =
            IsEnvelope && field is { } f && Csfea is { } vm
                ? values.Select(kv => (kv.Key, int.TryParse(kv.Key, NumberStyles.Integer, CultureInfo.InvariantCulture, out int n)
                        ? vm.EnvelopeShell(n, f) : null))
                    .Where(x => x.Item2 != null)
                    .Select(x => (x.Key, x.Item2!.Value.Min, x.Item2.Value.MinSource, x.Item2.Value.Max, x.Item2.Value.MaxSource))
                : values.Select(kv => (kv.Key, kv.Value, "", kv.Value, ""));
        string text = MinMaxText(label, rows, AtElement);
        SummaryText = text != "" ? text : Loc.S("FemResultNoValues");
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

    /// <summary>
    /// Итог по усилию стержней: min и max по концам КЭ с номером КЭ (у огибающей — по ветвям, с комбинацией).
    /// </summary>
    /// <param name="ends">Значения на концах КЭ показанного фрагмента.</param>
    public void UpdateBarSummary(string label, IEnumerable<KeyValuePair<string, (double I, double J)>> ends, FemForceComponent component)
    {
        IEnumerable<(string, double, string, double, string)> rows = IsEnvelope
            ? ends.Select(kv => (kv.Key, BarEndEnvelope(kv.Key, component))).Where(x => x.Item2 != null)
                .SelectMany(x => new[] { x.Item2!.Value.I, x.Item2.Value.J }
                    .Select(e => (x.Key, e.Min, e.MinSource, e.Max, e.MaxSource)))
            : ends.SelectMany(kv => new[] { kv.Value.I, kv.Value.J }.Select(v => (kv.Key, v, "", v, "")));
        BarSummaryText = MinMaxText(string.Format(Loc.S("FemResultBarsLabel"), label), rows, AtElement);
    }

    public void ClearBarSummary() => BarSummaryText = "";

    // ------------------------------------------------------------ CSfea: жёсткости КЭ

    /// <summary>На показанном шаге есть жёсткости КЭ (CSfea, записи с 17.10.2026, не огибающая).</summary>
    public bool HasStiffness => Csfea?.HasStiffness == true;

    /// <summary>Жёсткость пластины; null — нет данных.</summary>
    public double? ShellStiffness(string elemTag, FemCsfeaStiffness q, FemCsfeaStiffnessMode mode) =>
        Csfea != null && int.TryParse(elemTag, NumberStyles.Integer, CultureInfo.InvariantCulture, out int n)
            ? Csfea.ShellStiffness(n, q, mode) : null;

    /// <summary>Жёсткость стержня в точках ξ = 0, ½, 1; null — нет данных.</summary>
    public (double I, double M, double J)? BarStiffness(string elemTag, FemCsfeaStiffness q, FemCsfeaStiffnessMode mode) =>
        Csfea != null && int.TryParse(elemTag, NumberStyles.Integer, CultureInfo.InvariantCulture, out int n)
            ? Csfea.BarStiffness(n, q, mode) : null;

    /// <summary>Подпись величины жёсткости в режиме: «Изгиб: E1, МПа», «EIy/EIy₀».</summary>
    public static string StiffnessLabel(FemCsfeaStiffness q, FemCsfeaStiffnessMode mode)
    {
        string symbol = FemCsfeaStiffnessValues.Symbol(q, mode);
        string unit = FemCsfeaStiffnessValues.Unit(q, mode);
        string group = q switch
        {
            <= FemCsfeaStiffness.BendG => Loc.S("FemStiffnessBend"),
            <= FemCsfeaStiffness.MembG => Loc.S("FemStiffnessMemb"),
            _ => "",
        };
        string s = group == "" ? symbol : $"{group}: {symbol}";
        return unit == "" ? s : $"{s}, {Loc.S(unit)}";
    }

    /// <summary>Итог по величине стержней в точках i, ½, j (жёсткость, ширина раскрытия): min и max с номером КЭ.</summary>
    public void UpdateBarPointSummary(string label, IEnumerable<KeyValuePair<string, (double I, double M, double J)>> values) =>
        BarSummaryText = MinMaxText(string.Format(Loc.S("FemResultBarsLabel"), label),
            values.SelectMany(kv => new[] { kv.Value.I, kv.Value.M, kv.Value.J }.Select(v => (kv.Key, v, "", v, ""))), AtElement);

    /// <summary>Строки карточки КЭ: секущие жёсткости и отношение к начальным (у стержня — в точках i · ½ · j).</summary>
    void StiffnessRows(string tag, bool bar, List<FemResultCardRow> rows)
    {
        if (!HasStiffness) return;
        foreach (var q in bar ? FemCsfeaStiffnessValues.BarQuantities : FemCsfeaStiffnessValues.ShellQuantities)
        {
            string label = StiffnessLabel(q, FemCsfeaStiffnessMode.Secant);
            if (bar)
            {
                if (BarStiffness(tag, q, FemCsfeaStiffnessMode.Secant) is not { } s) continue;
                string text = $"{Num(s.I)} · {Num(s.M)} · {Num(s.J)}";
                if (BarStiffness(tag, q, FemCsfeaStiffnessMode.Ratio) is { } r)
                    text += string.Format(Loc.S("FemResultCardStiffnessRatio"), $"{Num(r.I)} · {Num(r.M)} · {Num(r.J)}");
                rows.Add(new(label, text));
            }
            else if (ShellStiffness(tag, q, FemCsfeaStiffnessMode.Secant) is double s)
            {
                string text = Num(s);
                if (ShellStiffness(tag, q, FemCsfeaStiffnessMode.Ratio) is double r)
                    text += string.Format(Loc.S("FemResultCardStiffnessRatio"), Num(r));
                rows.Add(new(label, text));
            }
        }
    }

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

    bool _barCrackShown;
    /// <summary>Показана ширина раскрытия трещин стержней (эпюры или мозаика своего списка).</summary>
    public bool BarCrackShown
    {
        get => _barCrackShown;
        set
        {
            if (_barCrackShown == value) return;
            _barCrackShown = value;
            OnPropertyChanged(nameof(IsCrackFieldShown));
        }
    }

    /// <summary>Показана ширина раскрытия трещин (пластин или стержней) — в строке шага доля длительной части.</summary>
    public bool IsCrackFieldShown => _shownCrackField != null || _barCrackShown;

    /// <summary>
    /// Ширина раскрытия стержней доступна: вид расчёта N или NL, есть стержни с усилиями; у огибающей — нет (не
    /// огибается).
    /// </summary>
    public bool HasBarCrackWidths => Csfea is { HasCrackWidths: true, IsEnvelopeShown: false } && HasBarForces;

    /// <summary>Значения готовы; false — досчитываются в фоне (по готовности — <see cref="Changed"/>).</summary>
    public bool PrepareBarCracks() => Csfea?.PrepareBarCracks() ?? true;

    /// <summary>Ширина раскрытия стержней — по записанным деформациям (иначе упрощённой проверкой по усилиям).</summary>
    public bool BarCrackByStrains => Csfea?.BarCrackByStrains == true;

    /// <summary>
    /// Ширина раскрытия стержня в точках i, ½, j, мм: продолжительная a_crc,1 или непродолжительная a_crc (точка без
    /// значения при значениях в других — 0: изгиба нет); null — нет данных.
    /// </summary>
    public (double I, double M, double J)? BarCrack(string elemTag, bool shortTerm)
    {
        if (Csfea == null || !int.TryParse(elemTag, NumberStyles.Integer, CultureInfo.InvariantCulture, out int n)
            || Csfea.BarCrackWidth(n) is not { } w) return null;
        var v = shortTerm ? w.Short : w.Long;
        if (v.All(x => x == null)) return null;
        return (v[0] ?? 0, v[1] ?? 0, v[2] ?? 0);
    }

    /// <summary>Подпись ширины раскрытия стержней: «a_crc,1, мм» или «a_crc, мм».</summary>
    public static string BarCrackLabel(bool shortTerm) =>
        $"{(shortTerm ? "a_crc" : "a_crc,1")}, {Loc.S("UnitMm")}";

    /// <summary>Строки карточки стержня: ширина раскрытия в точках i · ½ · j (если посчитана для показанного шага).</summary>
    void BarCrackRows(string tag, List<FemResultCardRow> rows)
    {
        if (!HasBarCrackWidths) return;
        foreach (bool shortTerm in new[] { false, true })
            if (BarCrack(tag, shortTerm) is { } v)
                rows.Add(new(BarCrackLabel(shortTerm), $"{Num(v.I)} · {Num(v.M)} · {Num(v.J)}"));
    }

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

    /// <summary>Длительная часть ширины раскрытия — из расчёта пакета (пара NL варианта), доля не задаётся.</summary>
    public bool CrackByPair => Csfea?.CrackByPair == true;
    public bool CrackByShare => !CrackByPair;
    public string CrackLongText => Csfea?.CrackLongText ?? "";
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
        if (Buckling != null) return tags.Count == 0 ? null : BucklingCard(tags);
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
            if (NodeEnvelope(node, c) is { } env) rows.Add(new(DisplacementLabel(c), EnvelopeText(env)));
            else if (NodeValue(node, c) is double v) rows.Add(new(DisplacementLabel(c), Num(v)));
        if (Csfea?.Reaction(node) is { } r)
        {
            rows.Add(new(Loc.S("FemResultCardSupports"), FemCsfeaResultVM.ReactionKindText(r.Kinds)));
            string[] labels = ["Rx", "Ry", "Rz", "Mx", "My", "Mz"];
            for (int c = 0; c < 6; c++)
            {
                string label = $"{labels[c]}, {Loc.S(c < 3 ? "UnitKN" : "UnitKNm")}";
                if (IsEnvelope)
                {
                    if (Csfea.EnvelopeReaction(node, c)?.Scale(1e-3) is { } env && (env.Max != 0 || env.Min != 0))
                        rows.Add(new(label, EnvelopeText(env)));
                }
                else if (r.Values[c] != 0) rows.Add(new(label, Num(r.Values[c] / 1e3)));
            }
        }
        return new FemResultCard(string.Format(Loc.S("FemResultCardNode"), node), rows);
    }

    FemResultCard ElementCard(string tag)
    {
        bool bar = Mesh.IsBar(tag);
        var rows = new List<FemResultCardRow>();
        if (Mesh.OwnerOf(tag) is { } owner) rows.Add(new(Loc.S("FemResultCardOwner"), owner));
        if (IsEnvelope && Csfea is { } vm && int.TryParse(tag, NumberStyles.Integer, CultureInfo.InvariantCulture, out int elem))
        {
            // Огибающая: обе ветви с комбинациями; перемещения — средние по узлам пластины (у стержня их нет в огибающей).
            if (bar && HasBarForces)
                foreach (var c in Forces)
                    if (BarEndEnvelope(tag, c) is { } e)
                        rows.Add(new(ForceLabel(c), string.Format(Loc.S("FemResultCardEnvelopeEnds"), Num(e.I.Max), e.I.MaxSource,
                            Num(e.I.Min), e.I.MinSource, Num(e.J.Max), e.J.MaxSource, Num(e.J.Min), e.J.MinSource)));
            if (!bar)
            {
                foreach (var c in Displacements.Where(c => c != FemResultComponent.U))
                    if (vm.EnvelopeShell(elem, FemCsfeaShellField.Ux + (int)c) is { } env)
                        rows.Add(new(DisplacementLabel(c), EnvelopeText(env)));
                foreach (var f in ShellFields)
                    if (vm.EnvelopeShell(elem, f.Value) is { } env) rows.Add(new(f.Label, EnvelopeText(env)));
            }
            return new FemResultCard(string.Format(Loc.S(bar ? "FemResultCardBar" : "FemResultCardShell"), tag), rows);
        }
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
        StiffnessRows(tag, bar, rows);
        if (bar) BarCrackRows(tag, rows);
        return new FemResultCard(string.Format(Loc.S(bar ? "FemResultCardBar" : "FemResultCardShell"), tag), rows);
    }

    static string EnvelopeText(FemCsfeaEnvelopeValue v) =>
        string.Format(Loc.S("FemResultCardEnvelope"), Num(v.Max), v.MaxSource, Num(v.Min), v.MinSource);

    static string StateLabel(double v)
    {
        var styles = FemCsfeaResultVM.StateStyles;
        int k = (int)v;
        return k >= 0 && k < styles.Count ? Loc.S(styles[k].Key) : Num(v);
    }

    // ------------------------------------------------------------ расчётные длины

    readonly Dictionary<string, FemBucklingUnitSummary> _bucklingUnits = new(StringComparer.Ordinal);

    /// <summary>Результат постановки «Расчётные длины — CSfea»; null — другой результат.</summary>
    public BucklingResultSteps? Buckling { get; }
    public bool IsBuckling => Buckling != null;

    /// <summary>Расчётный элемент (КонЭ или цепочка), в который входит КЭ; null — у КЭ нет сжатия ни в одном сочетании.</summary>
    public FemBucklingUnitSummary? BucklingUnit(string elemTag) => _bucklingUnits.GetValueOrDefault(elemTag);

    /// <summary>
    /// Величина расчётных длин на КЭ (его расчётного элемента): КЗУ, μ, lef, м, или номер категории источника
    /// (<see cref="BucklingSources"/>); null — нет данных.
    /// </summary>
    public double? BucklingValue(string elemTag, FemBucklingQuantity q)
    {
        if (BucklingUnit(elemTag) is not { } u) return null;
        return q switch
        {
            FemBucklingQuantity.Kzu => u.Kzu(),
            FemBucklingQuantity.MuX => u.X.Mu,
            FemBucklingQuantity.MuY => u.Y.Mu,
            FemBucklingQuantity.LefX => u.X.Mu * u.Length,
            FemBucklingQuantity.LefY => u.Y.Mu * u.Length,
            FemBucklingQuantity.SourceX => SourceIndex(u.X),
            _ => SourceIndex(u.Y),
        };
    }

    /// <summary>Источники μ — категории мозаики: ключ строки и цвет; последний — μ не назначен.</summary>
    public static IReadOnlyList<(string Key, System.Windows.Media.Color Color)> BucklingSources { get; } =
    [
        ("FemBucklingSourceMode", System.Windows.Media.Color.FromRgb(70, 110, 190)),
        ("FemBucklingSourceRocking", System.Windows.Media.Color.FromRgb(230, 150, 40)),
        ("FemBucklingSourceLocal", System.Windows.Media.Color.FromRgb(60, 160, 90)),
        ("FemBucklingSourceNotInvolved", System.Windows.Media.Color.FromRgb(150, 120, 170)),
        ("FemBucklingSourceNone", System.Windows.Media.Color.FromRgb(200, 60, 60)),
    ];

    static double SourceIndex(FemBucklingAxisSummary a) => a.Source switch
    {
        nameof(CSfea.CScoreBridge.Structural.RcBucklingLengthSource.Mode) => 0,
        nameof(CSfea.CScoreBridge.Structural.RcBucklingLengthSource.Rocking) => 1,
        nameof(CSfea.CScoreBridge.Structural.RcBucklingLengthSource.Local) => 2,
        nameof(CSfea.CScoreBridge.Structural.RcBucklingLengthSource.NotInvolved) => 3,
        _ => 4,
    };

    public static string BucklingLabel(FemBucklingQuantity q) => Loc.S("FemBucklingQuantity" + q);

    /// <summary>
    /// Стержень на деформированной схеме формы ломаной через точки дробления (от I к J); null — схема не деформирована,
    /// это не результат расчётных длин или стержень не дробился.
    /// </summary>
    public IReadOnlyList<Point3D>? BarCurve(string elemTag)
    {
        if (Buckling == null || !DeformedShown || Mesh.NodesOf(elemTag) is not [int i, int j]
            || !int.TryParse(elemTag, NumberStyles.Integer, CultureInfo.InvariantCulture, out int elem)
            || Buckling.BarPoints(elem) is not { Count: > 0 } points)
            return null;
        Point3D a = Mesh.Nodes[i], b = Mesh.Nodes[j];
        var list = new List<Point3D>(points.Count + 2) { Position(i) };
        for (int k = 0; k < points.Count; k++)
        {
            double t = (k + 1.0) / (points.Count + 1);
            list.Add(a + (b - a) * t + points[k] * _deformScale);
        }
        list.Add(Position(j));
        return list;
    }

    /// <summary>Карточка расчётных длин: по каждому расчётному элементу выбора — μ, lef, N_cr, КЗУ, источник и ход вычисления.</summary>
    FemResultCard BucklingCard(IReadOnlyList<string> tags)
    {
        var units = tags.Select(BucklingUnit).OfType<FemBucklingUnitSummary>().Distinct().ToList();
        var rows = new List<FemResultCardRow>();
        foreach (var u in units.Take(MaxCardUnits))
        {
            if (units.Count > 1) rows.Add(new(Loc.S("FemBucklingCardUnit"), u.Label));
            rows.Add(new(Loc.S("FemBucklingCardCombination"), u.Combination));
            rows.Add(new(Loc.S("FemBucklingCardLength"), Num(u.Length)));
            rows.Add(new($"N, {Loc.S("UnitKN")}", Num(u.N / 1e3)));
            foreach (var (axis, a) in new[] { ("x", u.X), ("y", u.Y) })
            {
                string value = a.Mu is double mu
                    ? string.Format(CultureInfo.CurrentCulture, Loc.S("FemBucklingCardAxis"), Num(mu), Num(mu * u.Length),
                        a.Ncr is double ncr ? Num(ncr / 1e3) : "—", u.Kzu(a) is double k ? Num(k) : "—",
                        Loc.S(BucklingSources[(int)SourceIndex(a)].Key))
                    : Loc.S("FemBucklingSourceNone");
                rows.Add(new($"μ{axis}", value));
            }
            if (units.Count == 1)
                foreach (var (axis, a) in new[] { ("x", u.X), ("y", u.Y) })
                    foreach (string line in new[] { a.Note }.Concat(a.Details).Where(l => l != ""))
                        rows.Add(new(string.Format(Loc.S("FemBucklingCardNotes"), axis), line));
        }
        if (units.Count > MaxCardUnits)
            rows.Add(new("…", string.Format(Loc.S("FemBucklingCardMore"), units.Count - MaxCardUnits)));
        if (units.Count == 0) rows.Add(new(Loc.S("FemBucklingCardUnit"), Loc.S("FemBucklingCardNoUnit")));
        string title = units.Count == 1 ? string.Format(Loc.S("FemBucklingCardTitle"), units[0].Label)
            : string.Format(Loc.S("FemResultCardElements"), tags.Count);
        return new FemResultCard(title, rows);
    }

    const int MaxCardUnits = 5;

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