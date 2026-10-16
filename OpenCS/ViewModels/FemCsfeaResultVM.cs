using System.Globalization;
using System.Windows.Media;
using System.Windows.Media.Media3D;
using CScore;
using CScore.Fem;
using CScore.Fem.Loads;
using CScore.PlateRebar;
using CSfea.CScoreBridge.Structural;
using OpenCS.OpenSees.Structural;
using OpenCS.Services;
using OpenCS.Utilites;

namespace OpenCS.ViewModels;

/// <summary>Поле мозаики пластин в результате CSfea.</summary>
public enum FemCsfeaShellField
{
    None, Ux, Uy, Uz, Nx, Ny, Nxy, Mx, My, Mxy, Qx, Qy, CrackBottom, CrackTop, Psi, SigmaRatio, EpsRatio, State,
    AcrcLongBottom, AcrcLongTop, AcrcShortBottom, AcrcShortTop,
    /// <summary>Отпор упругого основания C1 под КЭ, кПа (+ — основание сжато).</summary>
    FoundationPressure,
}

/// <summary>Пункт выпадающего списка: значение и подпись.</summary>
public sealed record FemCsfeaOption<T>(T Value, string Label)
{
    public override string ToString() => Label;
}

/// <summary>
/// Строка журнала шагов: сквозной номер, стадия, λ, номер по плану, сходимость, итерации, невязка, контрольный узел,
/// ΣRz опор (кН; пусто — нет данных).
/// </summary>
public sealed record FemCsfeaStepRow(int N, string Stage, double LoadFactor, string Planned, string Converged, int Iterations,
    string Residual, string Control, string Fields, string ReactionZ = "");

/// <summary>
/// Строка таблицы реакций опор узла на показанном шаге (глобальные оси, действуют на схему): Rx, Ry, Rz — кН; Mx, My,
/// Mz — кН·м; типы опор узла словами.
/// </summary>
/// <summary>Реакция линейной связи по грани: суммы, кН; погонная Rz — средняя, наименьшая, наибольшая, кН/м.</summary>
public sealed record FemCsfeaLineReactionRow(string Edge, double Length, double Rx, double Ry, double Rz,
    double QzMean, double QzMin, double QzMax);

public sealed record FemCsfeaReactionRow(int Node, double Rx, double Ry, double Rz, double Mx, double My, double Mz, string Kind,
    string Ext = "", IReadOnlyDictionary<string, string>? Sources = null)
{
    public string Combos => FemCsfeaEnvelopeRows.Combos(Sources);
}

/// <summary>
/// Строка таблицы перемещений узла сетки на показанном шаге: ux, uy, uz — мм; rx, ry, rz — мрад. У огибающей —
/// строка ветви (<c>Ext</c> = max/min), <c>Sources</c> — комбинация-источник по имени свойства.
/// </summary>
public sealed record FemCsfeaNodeRow(int Node, double Ux, double Uy, double Uz, double Rx, double Ry, double Rz,
    string Ext = "", IReadOnlyDictionary<string, string>? Sources = null)
{
    public string Combos => FemCsfeaEnvelopeRows.Combos(Sources);
}

/// <summary>Строка таблицы концевых сил стержня (Н, Н·м, местные оси; в таблице — кН, кН·м); у огибающей — ветвь.</summary>
public sealed record FemCsfeaBeamRow(int ElemTag,
    double Ni, double Qyi, double Qzi, double Mxi, double Myi, double Mzi,
    double Nj, double Qyj, double Qzj, double Mxj, double Myj, double Mzj,
    string Ext = "", IReadOnlyDictionary<string, string>? Sources = null)
{
    public string Combos => FemCsfeaEnvelopeRows.Combos(Sources);
}

/// <summary>Строки таблиц огибающей: столбец «Комбинации».</summary>
public static class FemCsfeaEnvelopeRows
{
    /// <summary>Комбинации-источники ячеек строки без повторов, по частоте; пусто — строка не огибающей.</summary>
    public static string Combos(IReadOnlyDictionary<string, string>? sources) => sources == null ? ""
        : string.Join(", ", sources.Values.Where(v => v != "").GroupBy(v => v).OrderByDescending(g => g.Count()).Select(g => g.Key));
}

/// <summary>
/// Строка таблицы усилий пластины на показанном шаге (оси выдачи, центр КЭ): Nx, Ny, Nxy, Qx, Qy — кН/м; Mx, My, Mxy —
/// кН·м/м; состояние сечения словом (у линейного расчёта — пусто).
/// </summary>
public sealed record FemCsfeaShellRow(int Elem, double Nx, double Ny, double Nxy, double Mx, double My, double Mxy,
    double Qx, double Qy, string State, string Ext = "", IReadOnlyDictionary<string, string>? Sources = null)
{
    public string Combos => FemCsfeaEnvelopeRows.Combos(Sources);
}

/// <summary>
/// Пункт списка показа линейного результата: загружение (шаг сводки) или комбинация схемы (суперпозиция шагов
/// загружений). Недоступная комбинация — серая, причина в подсказке.
/// </summary>
public sealed class FemCsfeaCaseItem
{
    public required FemLoadSource Source { get; init; }

    /// <summary>Загружение — индекс шага сводки; комбинация — null.</summary>
    public int? StepIndex { get; init; }

    /// <summary>Комбинация — слагаемые по шагам загружений; загружение — null.</summary>
    public FemCsfeaCombinationCase? Combination { get; init; }

    /// <summary>Огибающая группы комбинаций одного типа; иначе null.</summary>
    public FemCsfeaEnvelopeGroup? Envelope { get; init; }

    public bool IsCombination => Combination != null;
    public bool IsEnvelope => Envelope != null;
    public bool IsAvailable => Source.IsEnabled;
    public string Badge => Source.TypeBadge;
    public string Name => Source.Tag;

    /// <summary>Формула комбинации серым («1·ЗГ1 + 0,9·ЗГ8»); у загружения — код «ЗГ3»; у огибающей — число комбинаций.</summary>
    public string Formula => Source.Formula;
    public string ToolTip => ToolTipText ?? Source.ToolTipText;

    /// <summary>Своя подсказка пункта (огибающая); null — подсказка источника.</summary>
    public string? ToolTipText { get; init; }
}

/// <summary>Огибающая группы: тип комбинаций (C, CL, N, NL, A), доступные комбинации группы и число недоступных.</summary>
public sealed record FemCsfeaEnvelopeGroup(string Type, IReadOnlyList<FemCsfeaCaseItem> Combinations, int Unavailable);

/// <summary>
/// Что показано в линейном результате CSfea поверх шага — для перехода окно результата ↔ 3D-вид: комбинация схемы
/// (Id) или огибающая группы (тип) и выбор |max| / max / min.
/// </summary>
public sealed record FemCsfeaCaseKey(int? CombinationId, string? EnvelopeType, ForceRowAggregate Aggregate);

/// <summary>Значение огибающей величины: ветви max и min с именами комбинаций-источников (в единицах показа).</summary>
public readonly record struct FemCsfeaEnvelopeValue(double Max, string MaxSource, double Min, string MinSource)
{
    /// <summary>Значение по выбору |max| / max / min.</summary>
    public double Pick(ForceRowAggregate aggregate) => aggregate switch
    {
        ForceRowAggregate.Max => Max,
        ForceRowAggregate.Min => Min,
        _ => Math.Abs(Min) > Math.Abs(Max) ? Min : Max,
    };

    /// <summary>Комбинация, давшая значение <see cref="Pick"/>.</summary>
    public string PickSource(ForceRowAggregate aggregate) => aggregate switch
    {
        ForceRowAggregate.Max => MaxSource,
        ForceRowAggregate.Min => MinSource,
        _ => Math.Abs(Min) > Math.Abs(Max) ? MinSource : MaxSource,
    };

    /// <summary>Значение со знаком ×s (у s &lt; 0 ветви меняются местами).</summary>
    public FemCsfeaEnvelopeValue Scale(double s) => s >= 0 ? new(Max * s, MaxSource, Min * s, MinSource)
        : new(Min * s, MinSource, Max * s, MaxSource);
}

/// <summary>
/// Результат расчёта схемы CSfea (<see cref="FemCsfeaRunner.TaskKind"/>): сводка шагов (<see cref="FemCsfeaResultSummary"/>)
/// и полные поля записанных шагов (<c>fem_result_steps</c>). Формат общий для секущего и (будущего) линейного
/// расчёта: линейный — одна стадия из одного шага. Ползунок ходит по всем шагам сводки; поля показываются ближайшего
/// записанного шага. 3D: деформированная схема (стержни и контуры пластин), мозаика пластин, эпюры стержней.
/// </summary>
public sealed class FemCsfeaResultVM : ViewModelBase
{
    readonly DatabaseService _db;
    readonly CalcResult _result;
    readonly List<int> _recorded;
    readonly Dictionary<int, RcSecantStepFields> _cache = new();

    // Геометрия сетки: узлы по номеру, стержни (номер, узлы, оси эпюр), пластины (номер, контур).
    readonly Dictionary<int, Point3D> _nodes = new();
    readonly List<(int Tag, int I, int J, Vector3D Ey, Vector3D Ez)> _beams = new();
    readonly List<(int Tag, int[] Contour)> _shells = new();
    readonly Dictionary<int, int[]> _contours = new();
    // Пластины на основании C1: коэффициент постели (Н/м³) и единичная нормаль (n_z ≥ 0).
    readonly Dictionary<int, (double C1, Vector3D Normal)> _foundation = new();

    /// <param name="analysis">Постановка результата: у линейного расчёта строки названы по загружениям схемы
    /// (<see cref="Tasks.FemAnalysisCaseNames"/>); null — имена из сводки.</param>
    public FemCsfeaResultVM(CalcResult result, DatabaseService db, FemSchema schema, FemAnalysis? analysis = null)
    {
        _db = db;
        _result = result;
        Schema = schema;
        Analysis = analysis;
        Summary = FemCsfeaResultSummary.Parse(result.DataJson) ?? new FemCsfeaResultSummary();
        if (analysis != null && Summary.Kind == FemCsfeaRunner.LinearAnalysisKind
            && Tasks.FemAnalysisCaseNames.Resolve(schema, analysis) is { } names && names.Count == Summary.Stages.Count)
            Summary.Stages = Summary.Stages.Select((s, i) => s with { Tag = names[i] }).ToList();
        Status = result.Status;
        _recorded = result.Id > 0 ? db.GetFemResultStepNumbers(result.Id) : [];

        var stageTags = Summary.Stages.Select(s => s.Tag).ToList();
        string Stage(int i) => i >= 0 && i < stageTags.Count ? stageTags[i] : (i + 1).ToString(CultureInfo.CurrentCulture);
        double controlScale = Summary.ControlDof < 3 ? 1e3 : 1;
        StepRows = Summary.Steps.Select(s => new FemCsfeaStepRow(s.N, Stage(s.Stage), s.LoadFactor,
            s.Planned?.ToString(CultureInfo.CurrentCulture) ?? "—",
            s.Converged ? Loc.S("FemResultConverged") : Loc.S("FemResultNotConverged"), s.Iterations,
            double.IsFinite(s.Residual) ? s.Residual.ToString("0.##e+0", CultureInfo.CurrentCulture) : "",
            s.Control is { } c ? (c * controlScale).ToString("0.###", CultureInfo.CurrentCulture) : "",
            s.Fields ? "✓" : "",
            s.ReactionTotal is { } r ? (r[2] / 1e3).ToString("0.###", CultureInfo.CurrentCulture) : "")).ToList();
        var lines = new List<string>();
        lines.AddRange(Summary.Errors.Select(e => Loc.S("FemCsfeaResultErrorPrefix") + e));
        lines.AddRange(Summary.Describe().Where(l => !Summary.Errors.Any(e => l.EndsWith(e, StringComparison.Ordinal))));
        lines.AddRange(Summary.Report);
        ReportLines = lines;

        LambdaPoints = Summary.Steps.Select(s => ((double)s.N, s.Stage + s.LoadFactor, s.Converged, 0)).ToList();
        ControlPoints = Summary.Steps.Select(s => (s.Control is { } c ? c * controlScale : double.NaN, s.Stage + s.LoadFactor,
            s.Converged, 0)).ToList();
        ControlAxisLabel = Summary.ControlNodeTag == "" ? "" : string.Format(Loc.S("FemCsfeaControlAxis"),
            FemCsfeaRunner.DescribeControlNode(Summary.ControlSchemaNodeTag, Summary.ControlNodeTag),
            DofLabel(Summary.ControlDof), Summary.ControlDof < 3 ? Loc.S("UnitMm") : Loc.S("UnitRad"));

        // Ширина раскрытия — только для нормативных видов расчёта (N, NL) постановки этого результата.
        var analyses = schema.Analyses.Count > 0 ? schema.Analyses.ToList() : result.Id > 0 ? db.GetFemAnalyses(schema.Id) : [];
        _analysis = analyses.FirstOrDefault(a => a.ResultId == result.Id) ?? analyses.FirstOrDefault(a => a.Tag == result.TaskTag);
        var calc = _analysis != null ? Tasks.FemAnalysisParams.Parse(_analysis.ParamsJson).CalcType ?? CalcType.N : CalcType.C;
        HasCrackWidths = calc is CalcType.N or CalcType.NL;
        _longShare = calc == CalcType.NL ? 1.0 : 0.9;

        var shellFields = new List<FemCsfeaOption<FemCsfeaShellField>>
        {
            new(FemCsfeaShellField.None, Loc.S("FemCsfeaFieldNone")),
            new(FemCsfeaShellField.Ux, "ux, " + Loc.S("UnitMm")), new(FemCsfeaShellField.Uy, "uy, " + Loc.S("UnitMm")),
            new(FemCsfeaShellField.Uz, "uz, " + Loc.S("UnitMm")),
            new(FemCsfeaShellField.Nx, "Nx, " + Loc.S("UnitKNPerM")), new(FemCsfeaShellField.Ny, "Ny, " + Loc.S("UnitKNPerM")),
            new(FemCsfeaShellField.Nxy, "Nxy, " + Loc.S("UnitKNPerM")),
            new(FemCsfeaShellField.Mx, "Mx, " + Loc.S("UnitKNmPerM")), new(FemCsfeaShellField.My, "My, " + Loc.S("UnitKNmPerM")),
            new(FemCsfeaShellField.Mxy, "Mxy, " + Loc.S("UnitKNmPerM")),
            new(FemCsfeaShellField.Qx, "Qx, " + Loc.S("UnitKNPerM")), new(FemCsfeaShellField.Qy, "Qy, " + Loc.S("UnitKNPerM")),
            new(FemCsfeaShellField.CrackBottom, Loc.S("FemCsfeaFieldCrackBottom")),
            new(FemCsfeaShellField.CrackTop, Loc.S("FemCsfeaFieldCrackTop")),
            new(FemCsfeaShellField.Psi, Loc.S("FemCsfeaFieldPsi")),
            new(FemCsfeaShellField.SigmaRatio, Loc.S("FemCsfeaFieldSigmaRatio")),
            new(FemCsfeaShellField.EpsRatio, Loc.S("FemCsfeaFieldEpsRatio")),
            new(FemCsfeaShellField.State, Loc.S("FemCsfeaFieldState")),
        };
        if (HasCrackWidths)
            shellFields.AddRange(
            [
                new(FemCsfeaShellField.AcrcLongBottom, Loc.S("FemCsfeaFieldAcrcLongBottom")),
                new(FemCsfeaShellField.AcrcLongTop, Loc.S("FemCsfeaFieldAcrcLongTop")),
                new(FemCsfeaShellField.AcrcShortBottom, Loc.S("FemCsfeaFieldAcrcShortBottom")),
                new(FemCsfeaShellField.AcrcShortTop, Loc.S("FemCsfeaFieldAcrcShortTop")),
            ]);
        ShellFields = shellFields;

        // Геометрия — из сетки схемы (номера узлов и КЭ — теги сетки, как в модели CSfea).
        var meshNodes = db.GetFemMeshNodes(schema.Id);
        var byTag = new Dictionary<string, FemMeshNode>(StringComparer.Ordinal);
        foreach (var n in meshNodes)
            if (FemMeshTopology.CanonicalNodeTag(n.NodeTag) is { } tag && int.TryParse(tag, out int id))
            {
                byTag.TryAdd(tag, n);
                _nodes[id] = new Point3D(n.X, n.Y, n.Z);
            }
        var members = db.GetFemMembers(schema.Id).GroupBy(m => m.ElemTag).ToDictionary(g => g.Key, g => g.First());
        foreach (var e in db.GetFemMeshElements(schema.Id))
        {
            if (!int.TryParse(e.ElemTag, NumberStyles.Integer, CultureInfo.InvariantCulture, out int tag)) continue;
            if (FemElementGeometry.Of(e, byTag) is not { } g) continue;
            var ids = g.NodeTags.Select(t => int.Parse(t, CultureInfo.InvariantCulture)).ToArray();
            if (g.IsBar)
            {
                double rot = e.BeamRotationDeg ?? (e.SourceMemberTag is { } m && members.TryGetValue(m, out var mm) ? mm.RotationDeg : 0);
                var (ey, ez) = LocalFrame(_nodes[ids[0]], _nodes[ids[1]], rot);
                _beams.Add((tag, ids[0], ids[1], ey, ez));
            }
            else if (g.IsShell)
            {
                var contour = g.Contour.Select(i => ids[i]).ToArray();
                _shells.Add((tag, contour));
                _contours[tag] = contour;
                if (e.FoundationC1 is double c1 && c1 > 0) _foundation[tag] = (c1, UpNormal(contour.Select(n => _nodes[n]).ToArray()));
            }
        }
        if (_foundation.Count > 0)
            shellFields.Add(new(FemCsfeaShellField.FoundationPressure, Loc.S("FemCsfeaFieldFoundationPressure")));
        GoToFieldsStepCommand = new RelayCommand(_ => GoToFieldsStep(), _ => FieldsStepIndex is int i && i != SelectedStepIndex);
        CreateForceSetsCommand = new RelayCommand(_ => CreateForceSetsRequested?.Invoke(this), _ => CanCreateForceSets);
        CreateLoadCaseForceSetsCommand = new RelayCommand(_ => CreateLoadCaseForceSetsRequested?.Invoke(this),
            _ => CanCreateLoadCaseForceSets);
        LongShareFromSetsCommand = new RelayCommand(_ => LongShareTask = LongShareFromSetsAsync(), _ => HasCrackWidths && LongShareTask.IsCompleted);
        PrevCaseCommand = new RelayCommand(_ => StepCase(-1), _ => NeighbourCase(-1) != null);
        NextCaseCommand = new RelayCommand(_ => StepCase(+1), _ => NeighbourCase(+1) != null);
        BuildCaseItems();

        int last = Summary.Steps.FindLastIndex(s => s.Converged);
        _selectedStepIndex = Math.Max(0, last);
        ApplyStep();
    }

    public FemSchema Schema { get; }

    /// <summary>Постановка результата; null — не передана (имена стадий из сводки).</summary>
    public FemAnalysis? Analysis { get; }
    public FemCsfeaResultSummary Summary { get; }
    public string Status { get; }
    public int ResultId => _result.Id;
    public string Title => string.Format(Loc.S("FemCsfeaResultTitle"), _result.TaskTag, Schema.Tag);

    public IReadOnlyList<FemCsfeaStepRow> StepRows { get; }
    public IReadOnlyList<string> ReportLines { get; }
    public IReadOnlyList<(double X, double Y, bool Converged, int SegmentId)> LambdaPoints { get; }
    public IReadOnlyList<(double X, double Y, bool Converged, int SegmentId)> ControlPoints { get; }
    public bool HasControlChart => ControlPoints.Any(p => double.IsFinite(p.X));
    public string ControlAxisLabel { get; }
    public bool HasShells => _shells.Count > 0;
    public bool HasBeams => _beams.Count > 0;

    // ------------------------------------------------------------ шаг

    public int MaxStepIndex => Math.Max(0, Summary.Steps.Count - 1);

    int _selectedStepIndex;
    public int SelectedStepIndex
    {
        get => _selectedStepIndex;
        set
        {
            int v = Summary.Steps.Count == 0 ? 0 : Math.Clamp(value, 0, Summary.Steps.Count - 1);
            if (v == _selectedStepIndex && _shownCombination == null && _shownEnvelope == null) return;
            // Шаг (загружение) выбран явно — комбинация и огибающая больше не показаны.
            _shownCombination = null;
            _shownEnvelope = null;
            _showCombinations = false;
            _showEnvelopes = false;
            _selectedStepIndex = v;
            OnPropertyChanged();
            ApplyStep();
        }
    }

    /// <summary>
    /// Шаг сводки под ползунком; у показанной комбинации — сводка суперпозиции (N = 0, стадия −1, ΣR и ΣF суммой);
    /// null — шагов нет (ошибка до расчёта).
    /// </summary>
    public FemCsfeaStepSummary? SelectedStep => _shownEnvelope != null ? null
        : _shownCombination != null ? _combinationStep
        : Summary.Steps.Count > 0 ? Summary.Steps[_selectedStepIndex] : null;

    FemCsfeaStepSummary? _combinationStep;

    /// <summary>Линейный расчёт: шаги сводки — независимые загружения (без λ и итераций).</summary>
    public bool IsLinear => Summary.Kind == FemCsfeaRunner.LinearAnalysisKind;

    public string CurrentStepLabel => _shownEnvelope is { Envelope: { } env } shown
        ? string.Format(Loc.S("FemCsfeaEnvelopeLabel"), shown.Name, env.Combinations.Count, AggregateLabel(EnvelopeAggregate))
        : _shownCombination is { Combination: { } comb } item
        ? string.Format(Loc.S("FemCsfeaCombinationLabel"), CombinationItems.ToList().IndexOf(item) + 1, CombinationItems.Count,
            item.Name, comb.Terms.Count)
        : SelectedStep is not { } s ? Loc.S("FemCsfeaNoSteps")
        : IsLinear ? string.Format(Loc.S("FemCsfeaCaseLabel"), s.N, Summary.Steps.Count,
            s.Stage < Summary.Stages.Count ? Summary.Stages[s.Stage].Tag : "")
        : string.Format(Loc.S("FemCsfeaStepLabel"), s.N, Summary.Steps.Count,
            s.Stage < Summary.Stages.Count ? Summary.Stages[s.Stage].Tag : "", s.LoadFactor,
            s.Converged ? Loc.S("FemResultConverged") : Loc.S("FemResultNotConverged"));

    /// <summary>Индекс шага, поля которого показаны (ближайший записанный); null — полей нет.</summary>
    public int? FieldsStepIndex { get; private set; }

    /// <summary>Поля показанного шага; null — не записаны ни у одного шага.</summary>
    public RcSecantStepFields? Fields { get; private set; }

    public string FieldsNote { get; private set; } = "";
    public bool HasFieldsNote => FieldsNote != "";

    public bool HasFields => Fields != null;

    /// <summary>
    /// Поля шага по сквозному номеру (кеш); null — не записаны или запись повреждена. Вызывается и из фона
    /// («Наборы по загружениям»), поэтому кеш под блокировкой.
    /// </summary>
    public RcSecantStepFields? LoadFields(int stepN)
    {
        lock (_cache)
            if (_cache.TryGetValue(stepN, out var cached)) return cached;
        if (_result.Id <= 0 || _db.GetFemResultStep(_result.Id, stepN) is not { } data) return null;
        RcSecantStepFields f;
        try { f = FemCsfeaStepFieldsCodec.Unpack(data); }
        catch (Exception ex) when (ex is System.IO.InvalidDataException or System.IO.EndOfStreamException) { return null; }
        lock (_cache)
        {
            // Комбинации линейного расчёта складываются из всех загружений — их поля держатся все (до 32).
            if (_cache.Count >= (IsLinear ? Math.Clamp(Summary.Steps.Count, 4, 32) : 4)) _cache.Clear();
            return _cache[stepN] = f;
        }
    }

    void ApplyStep()
    {
        FieldsStepIndex = null;
        Fields = null;
        FieldsNote = "";
        _combinationStep = null;
        _envelope = null;
        // Огибающая другого типа больше не нужна — её расчёт прерывается.
        if (_envelopeTaskType != null && _shownEnvelope?.Envelope?.Type != _envelopeTaskType)
        {
            _envelopeCts?.Cancel();
            _envelopeTaskType = null;
        }
        if (_shownEnvelope is { Envelope: { } env })
        {
            if (_envelopes.TryGetValue(env.Type, out var ready))
            {
                _envelope = ready;
                Fields = PickEnvelope(ready, EnvelopeAggregate);
            }
            else
            {
                _envelope = null;
                FieldsNote = EnvelopeProgressNote(env, 0);
                StartEnvelope(_shownEnvelope);
            }
        }
        else if (_shownCombination is { Combination: { } comb })
        {
            try
            {
                var terms = comb.Terms.Select(t => (LoadFields(t.StepN) ?? throw new InvalidOperationException(
                    string.Format(Loc.S("FemCsfeaCombinationNoFields"), t.LoadCase.Tag)), t.Factor)).ToList();
                Fields = FemCsfeaCombinationFields.Superpose(terms);
                _combinationStep = new FemCsfeaStepSummary(0, -1, 1, 1.0, false, true, 1, 0.0, null, null, true,
                    Fields.ReactionSums.Length > 0 ? Fields.ReactionSums : null, Fields.LoadSum.Length > 0 ? Fields.LoadSum : null);
            }
            catch (InvalidOperationException ex) { FieldsNote = ex.Message; }
        }
        else if (SelectedStep is { } s && _recorded.Count > 0)
        {
            int n = _recorded.MinBy(r => (Math.Abs(r - s.N), r))!;
            FieldsStepIndex = Summary.Steps.FindIndex(x => x.N == n);
            Fields = LoadFields(n);
            if (n != s.N) FieldsNote = string.Format(Loc.S("FemCsfeaFieldsNotRecorded"), n);
        }
        else if (_recorded.Count == 0 && Summary.Steps.Count > 0) FieldsNote = Loc.S("FemCsfeaNoFieldsRecorded");
        IndexFields();
        OnPropertyChanged(nameof(SelectedStep));
        OnPropertyChanged(nameof(CurrentStepLabel));
        OnPropertyChanged(nameof(FieldsStepIndex));
        OnPropertyChanged(nameof(Fields));
        OnPropertyChanged(nameof(HasFields));
        OnPropertyChanged(nameof(FieldsNote));
        OnPropertyChanged(nameof(HasFieldsNote));
        OnPropertyChanged(nameof(NodeRows));
        OnPropertyChanged(nameof(ShellRows));
        OnPropertyChanged(nameof(BeamRows));
        OnPropertyChanged(nameof(ReactionRows));
        OnPropertyChanged(nameof(LineReactionRows));
        OnPropertyChanged(nameof(HasLineReactions));
        OnPropertyChanged(nameof(HasReactions));
        OnPropertyChanged(nameof(ShowNoReactionsNote));
        OnPropertyChanged(nameof(ReactionSummaryText));
        OnPropertyChanged(nameof(ShowCombinations));
        OnPropertyChanged(nameof(ShowEnvelopes));
        OnPropertyChanged(nameof(IsEnvelopeShown));
        OnPropertyChanged(nameof(ShowLoadCases));
        OnPropertyChanged(nameof(CaseItems));
        OnPropertyChanged(nameof(SelectedCaseItem));
        OnPropertyChanged(nameof(SelectedCombinationId));
        OnPropertyChanged(nameof(CanCreateForceSets));
        CaseChanged?.Invoke();
    }

    /// <summary>Показанное изменилось: шаг, загружение, комбинация или огибающая (для 3D-вида — общий сигнал).</summary>
    public event Action? CaseChanged;

    void SetFieldsNote(string note)
    {
        if (note == FieldsNote) return;
        FieldsNote = note;
        OnPropertyChanged(nameof(FieldsNote));
        OnPropertyChanged(nameof(HasFieldsNote));
    }

    // ------------------------------------------------------------ огибающая группы комбинаций

    // Вектор величин огибающей (раскладка по опорному шагу — номера узлов и КЭ у всех загружений одни): узел — 6
    // перемещений и |u|; пластина — 8 усилий, ux/uy/uz среднее по узлам КЭ (м), отпор C1 (кПа, NaN без основания);
    // стержень — 12 концевых сил; опора — 6 реакций. Всё, кроме |u|, линейно по загружениям.
    const int NodeQ = RcSecantStepFields.NodeComponents + 1;
    const int ShellQ = RcSecantStepFields.ShellForceComponents + 4;

    /// <summary>Готовая огибающая группы: значения, опорный шаг раскладки, группа (имена комбинаций-источников).</summary>
    sealed record EnvelopeData(FemCsfeaEnvelope Values, RcSecantStepFields Reference, FemCsfeaEnvelopeGroup Group)
    {
        public int ShellOffset => Reference.NodeIds.Length * NodeQ;
        public int BeamOffset => ShellOffset + Reference.ShellIds.Length * ShellQ;
        public int ReactionOffset => BeamOffset + Reference.BeamIds.Length * RcSecantStepFields.BeamForceComponents;
    }

    readonly Dictionary<string, EnvelopeData> _envelopes = new();
    EnvelopeData? _envelope;
    CancellationTokenSource? _envelopeCts;
    string? _envelopeTaskType;

    /// <summary>Идущий расчёт огибающей (завершённая задача — расчёта нет).</summary>
    public Task EnvelopeTask { get; private set; } = Task.CompletedTask;

    static string EnvelopeProgressNote(FemCsfeaEnvelopeGroup env, int done) =>
        string.Format(Loc.S("FemCsfeaEnvelopeProgress"), env.Type, done, env.Combinations.Count);

    /// <summary>Запустить расчёт огибающей пункта в фоне; по готовности, если пункт ещё показан, — показать.</summary>
    void StartEnvelope(FemCsfeaCaseItem item)
    {
        var env = item.Envelope!;
        if (_envelopeTaskType == env.Type && _envelopeCts is { IsCancellationRequested: false } && !EnvelopeTask.IsCompleted) return;
        _envelopeCts?.Cancel();
        var cts = _envelopeCts = new CancellationTokenSource();
        _envelopeTaskType = env.Type;
        var progress = new Progress<int>(done =>
        {
            if (ReferenceEquals(_shownEnvelope, item) && _envelope == null) SetFieldsNote(EnvelopeProgressNote(env, done));
        });
        EnvelopeTask = RunEnvelopeAsync(item, progress, cts);
    }

    async Task RunEnvelopeAsync(FemCsfeaCaseItem item, IProgress<int> progress, CancellationTokenSource cts)
    {
        var env = item.Envelope!;
        var ct = cts.Token;
        try
        {
            _envelopes[env.Type] = await Task.Run(() => BuildEnvelope(env, progress, ct), ct);
        }
        catch (OperationCanceledException) { return; }
        catch (InvalidOperationException ex)
        {
            if (ReferenceEquals(_shownEnvelope, item)) SetFieldsNote(ex.Message);
            return;
        }
        finally
        {
            // Признак сбрасывает только последний запущенный расчёт (прерванный мог смениться новым того же типа).
            if (ReferenceEquals(_envelopeCts, cts)) _envelopeTaskType = null;
        }
        if (ReferenceEquals(_shownEnvelope, item)) ApplyStep();
    }

    /// <summary>Огибающая группы по полям загружений (фон): величины загружений один раз, комбинации — суммой.</summary>
    EnvelopeData BuildEnvelope(FemCsfeaEnvelopeGroup env, IProgress<int> progress, CancellationToken ct)
    {
        var caseIndex = new Dictionary<int, int>();
        var vectors = new List<double[]>();
        RcSecantStepFields? reference = null;
        Dictionary<int, int>? nodeIndex = null;
        foreach (var term in env.Combinations.SelectMany(i => i.Combination!.Terms).DistinctBy(t => t.StepN).OrderBy(t => t.StepN))
        {
            ct.ThrowIfCancellationRequested();
            var f = LoadFields(term.StepN) ?? throw new InvalidOperationException(
                string.Format(Loc.S("FemCsfeaCombinationNoFields"), term.LoadCase.Tag));
            if (reference == null)
            {
                reference = f;
                nodeIndex = new Dictionary<int, int>();
                for (int i = 0; i < f.NodeIds.Length; i++) nodeIndex.TryAdd(f.NodeIds[i], i);
            }
            else if (!FemCsfeaCombinationFields.SameMesh(f, reference))
                throw new InvalidOperationException(Loc.S("FemCsfeaEnvelopeMeshMismatch"));
            caseIndex[term.StepN] = vectors.Count;
            vectors.Add(EnvelopeVector(f, nodeIndex!));
        }
        if (reference == null) throw new InvalidOperationException(Loc.S("FemCsfeaCombinationEmpty"));
        var combinations = env.Combinations
            .Select(i => (IReadOnlyList<(int, double)>)i.Combination!.Terms.Select(t => (caseIndex[t.StepN], t.Factor)).ToList())
            .ToList();
        // |u| — по записям узлов (куски вектора не режут записи по NodeQ величин).
        int nodeEnd = reference.NodeIds.Length * NodeQ;
        var values = FemCsfeaCombinationEnvelope.Build(vectors, combinations, (v, offset) =>
        {
            for (int o = 0; o < v.Length && offset + o < nodeEnd; o += NodeQ)
                v[o + 6] = Math.Sqrt(v[o] * v[o] + v[o + 1] * v[o + 1] + v[o + 2] * v[o + 2]);
        }, NodeQ, progress, ct);
        return new EnvelopeData(values, reference, env);
    }

    /// <summary>Величины огибающей одного загружения (раскладка — см. <see cref="NodeQ"/>).</summary>
    double[] EnvelopeVector(RcSecantStepFields f, Dictionary<int, int> nodeIndex)
    {
        int nn = f.NodeIds.Length, ns = f.ShellIds.Length;
        const int sc = RcSecantStepFields.ShellForceComponents, nc = RcSecantStepFields.NodeComponents;
        var v = new double[nn * NodeQ + ns * ShellQ + f.BeamForces.Length + f.Reactions.Length];
        var d = f.Displacements;
        for (int i = 0; i < nn; i++)
        {
            for (int c = 0; c < nc; c++) v[i * NodeQ + c] = d[nc * i + c];
            v[i * NodeQ + nc] = double.NaN;
        }
        int o = nn * NodeQ;
        for (int i = 0; i < ns; i++, o += ShellQ)
        {
            Array.Copy(f.ShellForces, i * sc, v, o, sc);
            int elem = f.ShellIds[i];
            if (!_contours.TryGetValue(elem, out var contour))
            {
                for (int c = 0; c < 4; c++) v[o + sc + c] = double.NaN;
                continue;
            }
            // Как ShellValue/FoundationPressure: среднее по узлам контура (узел вне полей — ноль).
            for (int c = 0; c < 3; c++)
            {
                double sum = 0;
                foreach (int n in contour)
                    if (nodeIndex.TryGetValue(n, out int k)) sum += d[nc * k + c];
                v[o + sc + c] = sum / contour.Length;
            }
            if (_foundation.TryGetValue(elem, out var fd))
            {
                double un = 0;
                foreach (int n in contour)
                    if (nodeIndex.TryGetValue(n, out int k))
                        un += fd.Normal.X * d[nc * k] + fd.Normal.Y * d[nc * k + 1] + fd.Normal.Z * d[nc * k + 2];
                v[o + sc + 3] = -fd.C1 * un / contour.Length / 1e3;
            }
            else v[o + sc + 3] = double.NaN;
        }
        Array.Copy(f.BeamForces, 0, v, o, f.BeamForces.Length);
        Array.Copy(f.Reactions, 0, v, o + f.BeamForces.Length, f.Reactions.Length);
        return v;
    }

    /// <summary>Поля показа огибающей: каждая величина — по выбору |max| / max / min (состояния пластин пусты).</summary>
    static RcSecantStepFields PickEnvelope(EnvelopeData e, ForceRowAggregate a)
    {
        var r = e.Reference;
        var env = e.Values;
        double P(int i) => ForceSetEnvelope.Entry.Pick(env.Min[i], env.Max[i], a) ?? double.NaN;
        const int sc = RcSecantStepFields.ShellForceComponents, nc = RcSecantStepFields.NodeComponents;
        var disp = new double[r.Displacements.Length];
        for (int i = 0; i < r.NodeIds.Length; i++)
            for (int c = 0; c < nc; c++) disp[nc * i + c] = P(i * NodeQ + c);
        var shell = new double[r.ShellForces.Length];
        for (int i = 0; i < r.ShellIds.Length; i++)
            for (int c = 0; c < sc; c++) shell[sc * i + c] = P(e.ShellOffset + i * ShellQ + c);
        var beam = new double[r.BeamForces.Length];
        for (int i = 0; i < beam.Length; i++) beam[i] = P(e.BeamOffset + i);
        var reactions = new double[r.Reactions.Length];
        for (int i = 0; i < reactions.Length; i++) reactions[i] = P(e.ReactionOffset + i);
        var states = new double[r.ShellStates.Length];
        Array.Fill(states, double.NaN);
        return new RcSecantStepFields
        {
            Stage = -1, Step = 1, LoadFactor = 1,
            NodeIds = r.NodeIds, Displacements = disp,
            ShellIds = r.ShellIds, ShellForces = shell, ShellStates = states, ShellFlags = new byte[r.ShellFlags.Length],
            BeamIds = r.BeamIds, BeamForces = beam, BeamFlags = new byte[r.BeamFlags.Length],
            ReactionNodeIds = r.ReactionNodeIds, Reactions = reactions, ReactionKinds = r.ReactionKinds,
            ReactionSums = [], LoadSum = [],
        };
    }

    /// <summary>Величина огибающей по индексу вектора ×<paramref name="scale"/>; null — огибающая не показана или значений нет.</summary>
    FemCsfeaEnvelopeValue? EnvelopeAt(int index, double scale = 1)
    {
        if (_envelope is not { } e || e.Values.MaxSource[index] < 0) return null;
        var items = e.Group.Combinations;
        return new FemCsfeaEnvelopeValue(e.Values.Max[index], items[e.Values.MaxSource[index]].Name,
            e.Values.Min[index], items[e.Values.MinSource[index]].Name).Scale(scale);
    }

    /// <summary>Огибающая перемещения узла: компоненты 0…5 (м, рад), 6 — |u| (м); null — огибающая не показана.</summary>
    public FemCsfeaEnvelopeValue? EnvelopeNode(int node, int component) =>
        _envelope != null && _nodeIndex.TryGetValue(node, out int i) ? EnvelopeAt(i * NodeQ + component) : null;

    /// <summary>Огибающая поля пластины в единицах мозаики (усилия, ux/uy/uz, отпор C1); null — поля нет в огибающей.</summary>
    public FemCsfeaEnvelopeValue? EnvelopeShell(int elem, FemCsfeaShellField field)
    {
        if (_envelope is not { } e || !_shellIndex.TryGetValue(elem, out int i)) return null;
        int o = e.ShellOffset + i * ShellQ;
        const int sc = RcSecantStepFields.ShellForceComponents;
        return field switch
        {
            >= FemCsfeaShellField.Nx and <= FemCsfeaShellField.Qy => EnvelopeAt(o + (field - FemCsfeaShellField.Nx), 1e-3),
            >= FemCsfeaShellField.Ux and <= FemCsfeaShellField.Uz => EnvelopeAt(o + sc + (field - FemCsfeaShellField.Ux), 1e3),
            FemCsfeaShellField.FoundationPressure => EnvelopeAt(o + sc + 3),
            _ => null,
        };
    }

    /// <summary>Огибающая концевой силы стержня (0…11 — как в <see cref="FemElementEndForces"/>, Н и Н·м); null — нет.</summary>
    public FemCsfeaEnvelopeValue? EnvelopeBeam(int elem, int component) =>
        _envelope is { } e && _beamIndex.TryGetValue(elem, out int i)
            ? EnvelopeAt(e.BeamOffset + i * RcSecantStepFields.BeamForceComponents + component) : null;

    /// <summary>Огибающая реакции опоры узла (0…5, Н и Н·м, глобальные оси); null — нет.</summary>
    public FemCsfeaEnvelopeValue? EnvelopeReaction(int node, int component) =>
        _envelope is { } e && _reactionIndex.TryGetValue(node, out int i)
            ? EnvelopeAt(e.ReactionOffset + i * RcSecantStepFields.NodeComponents + component) : null;

    /// <summary>Концевые силы стержня ветвями max и min огибающей (сырые, местные оси); null — огибающая не показана.</summary>
    public (FemElementEndForces Max, FemElementEndForces Min)? BeamForcesEnvelope(int elem)
    {
        if (_envelope == null) return null;
        var max = new double[RcSecantStepFields.BeamForceComponents];
        var min = new double[max.Length];
        for (int c = 0; c < max.Length; c++)
        {
            if (EnvelopeBeam(elem, c) is not { } v) return null;
            max[c] = v.Max;
            min[c] = v.Min;
        }
        static FemElementEndForces Of(int tag, double[] f) => new(tag, f[0], f[1], f[2], f[3], f[4], f[5],
            f[6], f[7], f[8], f[9], f[10], f[11]);
        return (Of(elem, max), Of(elem, min));
    }

    /// <summary>|u| узла по огибающей при выбранном |max| / max / min; null — огибающая не показана.</summary>
    public double? EnvelopeDisplacementLength(int node) => EnvelopeNode(node, 6)?.Pick(EnvelopeAggregate);

    // ------------------------------------------------------------ загружения и комбинации линейного расчёта

    FemCsfeaCaseItem? _shownCombination, _rememberedCombination, _shownEnvelope, _rememberedEnvelope;
    bool _showCombinations, _showEnvelopes;

    /// <summary>Загружения линейного расчёта — по пункту на шаг сводки; пусто — расчёт не линейный.</summary>
    public IReadOnlyList<FemCsfeaCaseItem> LoadCaseItems { get; private set; } = [];

    /// <summary>Комбинации схемы в порядке C → CL → N → NL → A; недоступные — со слагаемым вне расчёта.</summary>
    public IReadOnlyList<FemCsfeaCaseItem> CombinationItems { get; private set; } = [];

    /// <summary>Есть хотя бы одна комбинация, которая собирается из загружений расчёта.</summary>
    public bool CanShowCombinations => CombinationItems.Any(i => i.IsAvailable);

    public string CasesModeLabel => string.Format(Loc.S("FemCsfeaCasesMode"), LoadCaseItems.Count);
    public string CombinationsModeLabel => string.Format(Loc.S("FemCsfeaCombinationsMode"), CombinationItems.Count);

    /// <summary>Подсказка переключателя «Комбинации»: что показывается или почему недоступно.</summary>
    public string CombinationsModeTip => CombinationItems.Count == 0 ? Loc.S("FemCsfeaCombinationsNone")
        : !CanShowCombinations ? string.Format(Loc.S("FemCsfeaCombinationsNoneAvailable"), _result.TaskTag)
        : Loc.S("FemCsfeaCombinationsModeTip");

    /// <summary>
    /// Список показывает комбинации (true) или загружения. Включение показывает запомненную комбинацию или первую
    /// доступную; выключение возвращает загружение, которое было показано.
    /// </summary>
    public bool ShowCombinations
    {
        get => _showCombinations;
        set
        {
            if (value == _showCombinations) return;
            if (value)
            {
                var item = _rememberedCombination ?? CombinationItems.FirstOrDefault(i => i.IsAvailable);
                if (item == null) { OnPropertyChanged(); return; }
                SelectedCaseItem = item;
            }
            else ShowLoadCaseList();
        }
    }

    /// <summary>Вернуть показ загружения, которое было показано до комбинации или огибающей.</summary>
    void ShowLoadCaseList()
    {
        _showCombinations = false;
        _showEnvelopes = false;
        _shownCombination = null;
        _shownEnvelope = null;
        ApplyStep();
    }

    /// <summary>Список показывает загружения (не комбинации и не огибающие).</summary>
    public bool ShowLoadCases => !_showCombinations && !_showEnvelopes;

    /// <summary>Пункты списка в текущем режиме.</summary>
    public IReadOnlyList<FemCsfeaCaseItem> CaseItems => _showEnvelopes ? EnvelopeItems : _showCombinations ? CombinationItems : LoadCaseItems;

    /// <summary>Показанный пункт: огибающая, комбинация или загружение шага; недоступный пункт не выбирается.</summary>
    public FemCsfeaCaseItem? SelectedCaseItem
    {
        get => _shownEnvelope ?? _shownCombination ?? (_selectedStepIndex < LoadCaseItems.Count ? LoadCaseItems[_selectedStepIndex] : null);
        set
        {
            if (value == null || ReferenceEquals(value, SelectedCaseItem)) return;
            if (!value.IsAvailable) { OnPropertyChanged(); return; }
            if (value.IsEnvelope)
            {
                _shownEnvelope = _rememberedEnvelope = value;
                _shownCombination = null;
                _showEnvelopes = true;
                _showCombinations = false;
                ApplyStep();
            }
            else if (value.IsCombination)
            {
                _shownCombination = _rememberedCombination = value;
                _shownEnvelope = null;
                _showCombinations = true;
                _showEnvelopes = false;
                ApplyStep();
            }
            else if (value.StepIndex is int step) SelectedStepIndex = step;
        }
    }

    /// <summary>Id комбинации схемы показанного пункта (для перехода окно ↔ 3D-вид); null — показано загружение.</summary>
    public int? SelectedCombinationId
    {
        get => _shownCombination?.Source.Definition?.Id;
        set
        {
            if (value is int id && CombinationItems.FirstOrDefault(i => i.Source.Definition?.Id == id && i.IsAvailable) is { } item)
                SelectedCaseItem = item;
        }
    }

    /// <summary>Огибающая группы показана (значения — по <see cref="EnvelopeAggregate"/>, деформаций и ΣR нет).</summary>
    public bool IsEnvelopeShown => _shownEnvelope != null;

    /// <summary>Огибающие групп комбинаций по типам C → CL → N → NL → A (тип — если в нём есть доступная комбинация).</summary>
    public IReadOnlyList<FemCsfeaCaseItem> EnvelopeItems { get; private set; } = [];

    public bool CanShowEnvelopes => EnvelopeItems.Count > 0;
    public string EnvelopesModeLabel => string.Format(Loc.S("FemCsfeaEnvelopesMode"), EnvelopeItems.Count);
    public string EnvelopesModeTip => CanShowEnvelopes ? Loc.S("FemCsfeaEnvelopesModeTip") : CombinationsModeTip;

    /// <summary>
    /// Список показывает огибающие групп. Включение показывает запомненную огибающую или первую; выключение
    /// возвращает загружение.
    /// </summary>
    public bool ShowEnvelopes
    {
        get => _showEnvelopes;
        set
        {
            if (value == _showEnvelopes) return;
            if (value)
            {
                var item = _rememberedEnvelope ?? EnvelopeItems.FirstOrDefault();
                if (item == null) { OnPropertyChanged(); return; }
                SelectedCaseItem = item;
            }
            else ShowLoadCaseList();
        }
    }

    /// <summary>Варианты значения огибающей: |max|, max, min.</summary>
    public IReadOnlyList<FemCsfeaOption<ForceRowAggregate>> AggregateOptions => AggregateOptionList;

    static readonly FemCsfeaOption<ForceRowAggregate>[] AggregateOptionList =
    [
        new(ForceRowAggregate.MaxAbs, "|max|"), new(ForceRowAggregate.Max, "max"), new(ForceRowAggregate.Min, "min"),
    ];

    /// <summary>Подпись выбора огибающей: |max|, max, min.</summary>
    public static string AggregateLabel(ForceRowAggregate a) => AggregateOptionList.First(o => o.Value == a).Label;

    ForceRowAggregate _envelopeAggregate = ForceRowAggregate.MaxAbs;
    /// <summary>Какое значение огибающей показывать: мозаика, итог-экстремум, карточка группы КЭ.</summary>
    public ForceRowAggregate EnvelopeAggregate
    {
        get => _envelopeAggregate;
        set
        {
            if (value == _envelopeAggregate) return;
            _envelopeAggregate = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(SelectedAggregateOption));
            if (_shownEnvelope != null) ApplyStep();
        }
    }

    /// <summary>Пункт списка |max| / max / min (привязка выпадающего списка).</summary>
    public FemCsfeaOption<ForceRowAggregate> SelectedAggregateOption
    {
        get => AggregateOptionList.First(o => o.Value == _envelopeAggregate);
        set { if (value != null) EnvelopeAggregate = value.Value; }
    }

    /// <summary>Показанное поверх шага (комбинация или огибающая) — для перехода окно ↔ 3D-вид; null — шаг.</summary>
    public FemCsfeaCaseKey? ShownKey
    {
        get => _shownEnvelope?.Envelope is { } env ? new FemCsfeaCaseKey(null, env.Type, EnvelopeAggregate)
            : SelectedCombinationId is int id ? new FemCsfeaCaseKey(id, null, EnvelopeAggregate) : null;
        set
        {
            if (value == null) return;
            EnvelopeAggregate = value.Aggregate;
            if (value.EnvelopeType is { } type)
            {
                if (EnvelopeItems.FirstOrDefault(i => i.Envelope!.Type == type) is { } item) SelectedCaseItem = item;
            }
            else SelectedCombinationId = value.CombinationId;
        }
    }

    /// <summary>Источники окна выбора «⋯»: комбинации (недоступные — серые) и загружения расчёта.</summary>
    public IReadOnlyList<FemLoadSource> PickerSources =>
        CombinationItems.Select(i => i.Source).Concat(LoadCaseItems.Select(i => i.Source).Where(s => s.Kind == FemLoadSourceKind.LoadCase)).ToList();

    /// <summary>Показать выбранный в окне «⋯» источник.</summary>
    public void SelectSource(FemLoadSource source)
    {
        if (CombinationItems.Concat(LoadCaseItems).FirstOrDefault(i => ReferenceEquals(i.Source, source)) is { } item)
            SelectedCaseItem = item;
    }

    public System.Windows.Input.ICommand PrevCaseCommand { get; }
    public System.Windows.Input.ICommand NextCaseCommand { get; }

    /// <summary>Соседний доступный пункт списка текущего режима; null — его нет.</summary>
    FemCsfeaCaseItem? NeighbourCase(int direction)
    {
        var items = CaseItems;
        int i = SelectedCaseItem is { } current ? items.ToList().IndexOf(current) : -1;
        if (i < 0) return null;
        for (i += direction; i >= 0 && i < items.Count; i += direction)
            if (items[i].IsAvailable) return items[i];
        return null;
    }

    void StepCase(int direction)
    {
        if (NeighbourCase(direction) is { } item) SelectedCaseItem = item;
    }

    /// <summary>
    /// Пункты списков линейного расчёта: загружение — по шагу сводки; комбинации схемы — слагаемые по шагам загружений
    /// (<see cref="FemCsfeaCombinationFields"/>), без шага у слагаемого с k ≠ 0 — недоступна.
    /// </summary>
    void BuildCaseItems()
    {
        if (!IsLinear || Analysis == null) return;
        var loadCases = Schema.LoadCases.ToList();
        var sources = FemLoadSources.Build(Schema.LoadDefinitions.ToList(), loadCases, CScore.Combinations.Sp20GammaDefaults.Standard);
        var caseSteps = FemCsfeaCombinationFields.CaseSteps(Analysis, Summary);
        var caseByStep = caseSteps.ToDictionary(kv => kv.Value, kv => kv.Key);
        var caseSources = sources.Where(s => s.Kind == FemLoadSourceKind.LoadCase && s.LoadCase != null)
            .GroupBy(s => s.LoadCase!.Id).ToDictionary(g => g.Key, g => g.First());

        LoadCaseItems = Summary.Steps.Select((s, i) =>
        {
            string tag = s.Stage >= 0 && s.Stage < Summary.Stages.Count ? Summary.Stages[s.Stage].Tag : s.N.ToString(CultureInfo.CurrentCulture);
            var source = caseByStep.TryGetValue(s.N, out int id) && caseSources.TryGetValue(id, out var src) ? src
                : new FemLoadSource { Kind = FemLoadSourceKind.Unresolved, Tag = tag };
            return new FemCsfeaCaseItem { Source = source, StepIndex = i };
        }).ToList();

        CombinationItems = sources.Where(s => s.Kind == FemLoadSourceKind.Combination && s.Definition != null).Select(s =>
        {
            var comb = FemCsfeaCombinationFields.Resolve(s.Definition!, loadCases, caseSteps);
            s.DisabledReason = comb.Error != null ? string.Format(Loc.S("FemCsfeaCombinationError"), comb.Error)
                : comb.Missing.Count > 0 ? string.Format(Loc.S("FemCsfeaCombinationMissing"),
                    string.Join(", ", comb.Missing.Select(lc => $"«{lc.Tag}»")), _result.TaskTag)
                : comb.Terms.Count == 0 ? Loc.S("FemCsfeaCombinationEmpty") : null;
            return new FemCsfeaCaseItem { Source = s, Combination = comb };
        }).ToList();

        EnvelopeItems = CombinationItems.GroupBy(i => i.Badge).Where(g => g.Any(i => i.IsAvailable)).Select(g =>
        {
            var available = g.Where(i => i.IsAvailable).ToList();
            int unavailable = g.Count() - available.Count;
            string tip = string.Format(Loc.S("FemCsfeaEnvelopeTip"), g.Key, available.Count);
            if (unavailable > 0) tip += Environment.NewLine + string.Format(Loc.S("FemCsfeaEnvelopeSkipped"), unavailable, _result.TaskTag);
            return new FemCsfeaCaseItem
            {
                Source = new FemLoadSource
                {
                    Kind = FemLoadSourceKind.Combination, TypeBadge = g.Key,
                    Tag = string.Format(Loc.S("FemCsfeaEnvelopeName"), g.Key),
                    Formula = string.Format(Loc.S("FemCsfeaEnvelopeCount"), available.Count),
                },
                Envelope = new FemCsfeaEnvelopeGroup(g.Key, available, unavailable),
                ToolTipText = tip,
            };
        }).ToList();
    }

    void GoToFieldsStep()
    {
        if (FieldsStepIndex is int i) SelectedStepIndex = i;
    }

    // ------------------------------------------------------------ таблицы полей шага

    RcSecantStepFields? _rowsFields;
    IReadOnlyList<FemCsfeaNodeRow> _nodeRows = [];
    IReadOnlyList<FemCsfeaShellRow> _shellRows = [];
    IReadOnlyList<FemCsfeaBeamRow> _beamRows = [];
    IReadOnlyList<FemCsfeaReactionRow> _reactionRows = [];

    /// <summary>Перемещения узлов показанного шага — строятся при первом обращении (их читает только окно таблиц).</summary>
    public IReadOnlyList<FemCsfeaNodeRow> NodeRows { get { BuildRows(); return _nodeRows; } }

    /// <summary>Усилия пластин показанного шага.</summary>
    public IReadOnlyList<FemCsfeaShellRow> ShellRows { get { BuildRows(); return _shellRows; } }

    /// <summary>Концевые силы стержней показанного шага (Н, Н·м — в таблице переводятся в кН, кН·м).</summary>
    public IReadOnlyList<FemCsfeaBeamRow> BeamRows { get { BuildRows(); return _beamRows; } }

    /// <summary>Реакции опор показанного шага по узлам.</summary>
    public IReadOnlyList<FemCsfeaReactionRow> ReactionRows { get { BuildRows(); return _reactionRows; } }

    IReadOnlyList<FemCsfeaLineReactionRow> _lineReactionRows = [];
    IReadOnlyList<FemLineSupportChain>? _lineChains;

    /// <summary>Реакции линейных связей показанного шага по граням.</summary>
    public IReadOnlyList<FemCsfeaLineReactionRow> LineReactionRows { get { BuildRows(); return _lineReactionRows; } }

    /// <summary>У схемы есть линейные связи, и в полях шага есть реакции.</summary>
    public bool HasLineReactions => LineReactionRows.Count > 0;

    /// <summary>Узлы граней линейных связей по текущей сетке схемы (читаются один раз и только при наличии связей).</summary>
    IReadOnlyList<FemLineSupportChain> LineChains()
    {
        if (_lineChains != null) return _lineChains;
        if (_db.GetFemLineSupports(Schema.Id).Count == 0) return _lineChains = [];
        return _lineChains = _db.ResolveFemLineSupports(Schema.Id, _db.GetFemMembers(Schema.Id),
            _db.GetFemMeshNodes(Schema.Id), _db.GetFemMeshElements(Schema.Id)).Chains;
    }

    /// <summary>В полях показанного шага есть реакции (записи до реакций — без них).</summary>
    public bool HasReactions => Fields is { ReactionNodeIds.Length: > 0 };

    /// <summary>Поля показаны, а реакций в них нет — запись шага старого формата.</summary>
    public bool ShowNoReactionsNote => Fields != null && _envelope == null && Fields.ReactionSums.Length == 0;

    void BuildRows()
    {
        if (ReferenceEquals(_rowsFields, Fields) && (Fields != null || _nodeRows.Count == 0)) return;
        _rowsFields = Fields;
        if (Fields is not { } f) { _nodeRows = []; _shellRows = []; _beamRows = []; _reactionRows = []; _lineReactionRows = []; return; }
        if (_envelope != null) { BuildEnvelopeRows(f); return; }

        var nodes = new FemCsfeaNodeRow[f.NodeIds.Length];
        var d = Denoise(f.Displacements, RcSecantStepFields.NodeComponents);
        for (int i = 0; i < nodes.Length; i++)
        {
            int o = i * RcSecantStepFields.NodeComponents;
            nodes[i] = new FemCsfeaNodeRow(f.NodeIds[i], d[o] * 1e3, d[o + 1] * 1e3, d[o + 2] * 1e3,
                d[o + 3] * 1e3, d[o + 4] * 1e3, d[o + 5] * 1e3);
        }
        _nodeRows = nodes;

        var shells = new FemCsfeaShellRow[f.ShellIds.Length];
        var sf = Denoise(f.ShellForces, RcSecantStepFields.ShellForceComponents);
        for (int i = 0; i < shells.Length; i++)
        {
            // Порядок полей: Nx, Ny, Nxy (Н/м), Mx, My, Mxy (Н·м/м), Qx, Qy (Н/м).
            int o = i * RcSecantStepFields.ShellForceComponents;
            byte flags = f.ShellFlags[i];
            string state = IsLinear ? ""
                : Loc.S(StateStyles[(flags & RcSecantStepFields.Failed) != 0 ? 3
                    : (flags & RcSecantStepFields.Yielded) != 0 ? 2
                    : (flags & RcSecantStepFields.Cracked) != 0 ? 1 : 0].Key);
            shells[i] = new FemCsfeaShellRow(f.ShellIds[i], sf[o] / 1e3, sf[o + 1] / 1e3, sf[o + 2] / 1e3,
                sf[o + 3] / 1e3, sf[o + 4] / 1e3, sf[o + 5] / 1e3, sf[o + 6] / 1e3, sf[o + 7] / 1e3, state);
        }
        _shellRows = shells;

        var beams = new FemCsfeaBeamRow[f.BeamIds.Length];
        var bf = Denoise(f.BeamForces, RcSecantStepFields.BeamForceComponents);
        for (int i = 0; i < beams.Length; i++)
        {
            int o = i * RcSecantStepFields.BeamForceComponents;
            beams[i] = new FemCsfeaBeamRow(f.BeamIds[i], bf[o], bf[o + 1], bf[o + 2], bf[o + 3], bf[o + 4], bf[o + 5],
                bf[o + 6], bf[o + 7], bf[o + 8], bf[o + 9], bf[o + 10], bf[o + 11]);
        }
        _beamRows = beams;

        var reactions = new FemCsfeaReactionRow[f.ReactionNodeIds.Length];
        var rf = Denoise(f.Reactions, RcSecantStepFields.NodeComponents);
        for (int i = 0; i < reactions.Length; i++)
        {
            int o = i * RcSecantStepFields.NodeComponents;
            reactions[i] = new FemCsfeaReactionRow(f.ReactionNodeIds[i], rf[o] / 1e3, rf[o + 1] / 1e3, rf[o + 2] / 1e3,
                rf[o + 3] / 1e3, rf[o + 4] / 1e3, rf[o + 5] / 1e3, ReactionKindText(f.ReactionKinds[i]));
        }
        _reactionRows = reactions;

        _lineReactionRows = [];
        if (f.ReactionNodeIds.Length > 0 && LineChains() is { Count: > 0 } chains)
        {
            var index = new Dictionary<string, int>(StringComparer.Ordinal);
            for (int i = 0; i < f.ReactionNodeIds.Length; i++)
                index.TryAdd(f.ReactionNodeIds[i].ToString(CultureInfo.InvariantCulture), i);
            _lineReactionRows = FemLineSupportReactions.Compute(chains, tag =>
                    FemMeshTopology.CanonicalNodeTag(tag) is { } t && index.TryGetValue(t, out int i)
                        ? f.Reactions.AsSpan(6 * i, 6).ToArray() : null)
                .Select(r => new FemCsfeaLineReactionRow(r.Key.ToString(), r.Length, r.Rx / 1e3, r.Ry / 1e3, r.Rz / 1e3,
                    r.QzMean / 1e3, r.QzMin / 1e3, r.QzMax / 1e3))
                .ToList();
        }
    }

    static readonly string[] NodeColumns = ["Ux", "Uy", "Uz", "Rx", "Ry", "Rz"];
    static readonly string[] ShellColumns = ["Nx", "Ny", "Nxy", "Mx", "My", "Mxy", "Qx", "Qy"];
    static readonly string[] BeamColumns = ["Ni", "Qyi", "Qzi", "Mxi", "Myi", "Mzi", "Nj", "Qyj", "Qzj", "Mxj", "Myj", "Mzj"];
    static readonly string[] ReactionColumns = ["Rx", "Ry", "Rz", "Mx", "My", "Mz"];

    /// <summary>
    /// Таблицы огибающей: по объекту строки «max» и «min»; значение и комбинация-источник каждой ячейки (по имени
    /// столбца). Реакций по граням нет — огибающая их не считает.
    /// </summary>
    void BuildEnvelopeRows(RcSecantStepFields f)
    {
        // Ветви объекта: значения (в единицах таблицы) и источники по столбцам; нет значения — 0 и пустой источник.
        (double[] Max, double[] Min, Dictionary<string, string> MaxSrc, Dictionary<string, string> MinSrc) Branches(
            string[] columns, Func<int, FemCsfeaEnvelopeValue?> value, double scale)
        {
            var max = new double[columns.Length];
            var min = new double[columns.Length];
            var maxSrc = new Dictionary<string, string>(StringComparer.Ordinal);
            var minSrc = new Dictionary<string, string>(StringComparer.Ordinal);
            for (int c = 0; c < columns.Length; c++)
            {
                if (value(c) is not { } v) { maxSrc[columns[c]] = minSrc[columns[c]] = ""; continue; }
                (max[c], min[c]) = (v.Max * scale, v.Min * scale);
                maxSrc[columns[c]] = v.MaxSource;
                minSrc[columns[c]] = v.MinSource;
            }
            return (max, min, maxSrc, minSrc);
        }

        var nodes = new List<FemCsfeaNodeRow>(2 * f.NodeIds.Length);
        foreach (int id in f.NodeIds)
        {
            var (mx, mn, xs, ns) = Branches(NodeColumns, c => EnvelopeNode(id, c), 1e3);
            nodes.Add(new FemCsfeaNodeRow(id, mx[0], mx[1], mx[2], mx[3], mx[4], mx[5], "max", xs));
            nodes.Add(new FemCsfeaNodeRow(id, mn[0], mn[1], mn[2], mn[3], mn[4], mn[5], "min", ns));
        }
        _nodeRows = nodes;

        var shells = new List<FemCsfeaShellRow>(2 * f.ShellIds.Length);
        foreach (int id in f.ShellIds)
        {
            var (mx, mn, xs, ns) = Branches(ShellColumns, c => EnvelopeShell(id, FemCsfeaShellField.Nx + c), 1);
            shells.Add(new FemCsfeaShellRow(id, mx[0], mx[1], mx[2], mx[3], mx[4], mx[5], mx[6], mx[7], "", "max", xs));
            shells.Add(new FemCsfeaShellRow(id, mn[0], mn[1], mn[2], mn[3], mn[4], mn[5], mn[6], mn[7], "", "min", ns));
        }
        _shellRows = shells;

        var beams = new List<FemCsfeaBeamRow>(2 * f.BeamIds.Length);
        foreach (int id in f.BeamIds)
        {
            var (mx, mn, xs, ns) = Branches(BeamColumns, c => EnvelopeBeam(id, c), 1);
            beams.Add(new FemCsfeaBeamRow(id, mx[0], mx[1], mx[2], mx[3], mx[4], mx[5], mx[6], mx[7], mx[8], mx[9], mx[10], mx[11], "max", xs));
            beams.Add(new FemCsfeaBeamRow(id, mn[0], mn[1], mn[2], mn[3], mn[4], mn[5], mn[6], mn[7], mn[8], mn[9], mn[10], mn[11], "min", ns));
        }
        _beamRows = beams;

        var reactions = new List<FemCsfeaReactionRow>(2 * f.ReactionNodeIds.Length);
        for (int i = 0; i < f.ReactionNodeIds.Length; i++)
        {
            int id = f.ReactionNodeIds[i];
            string kind = ReactionKindText(f.ReactionKinds[i]);
            var (mx, mn, xs, ns) = Branches(ReactionColumns, c => EnvelopeReaction(id, c), 1e-3);
            reactions.Add(new FemCsfeaReactionRow(id, mx[0], mx[1], mx[2], mx[3], mx[4], mx[5], kind, "max", xs));
            reactions.Add(new FemCsfeaReactionRow(id, mn[0], mn[1], mn[2], mn[3], mn[4], mn[5], kind, "min", ns));
        }
        _reactionRows = reactions;
        _lineReactionRows = [];
    }

    static readonly (byte Flag, string Key)[] ReactionKindKeys =
    [
        (RcReactionSet.Support, "FemCsfeaReactionSupport"), (RcReactionSet.Spring, "FemCsfeaReactionSpring"),
        (RcReactionSet.Foundation, "FemCsfeaReactionFoundation"), (RcReactionSet.Auto, "FemCsfeaReactionAuto"),
    ];

    /// <summary>Типы опор узла словами через запятую (<see cref="RcReactionSet.Support"/> …).</summary>
    public static string ReactionKindText(byte kinds) =>
        string.Join(", ", ReactionKindKeys.Where(k => (kinds & k.Flag) != 0).Select(k => Loc.S(k.Key)));

    /// <summary>Итог реакций показанного шага (из сводки шага); пусто — нет данных.</summary>
    public string ReactionSummaryText => SelectedStep is { } s ? DescribeReactions(s) : "";

    /// <summary>
    /// Итог реакций шага: ΣR (кН) с раскладкой по типам опор (ненулевые), ΣF нагрузки и небаланс |ΣR + ΣF|; пусто — в
    /// сводке шага реакций нет.
    /// </summary>
    public static string DescribeReactions(FemCsfeaStepSummary s)
    {
        if (s.ReactionTotal is not { } total || s.ReactionSums is not { } sums) return "";
        var c = CultureInfo.CurrentCulture;
        string Vec(double[] v, int o = 0) => string.Create(c, $"({v[o] / 1e3:0.###}; {v[o + 1] / 1e3:0.###}; {v[o + 2] / 1e3:0.###})");
        var parts = new List<string>();
        for (int k = 0; k < RcReactionSet.KindCount; k++)
            if (sums[3 * k] != 0 || sums[3 * k + 1] != 0 || sums[3 * k + 2] != 0)
                parts.Add(Loc.S(ReactionKindKeys[k].Key) + " " + Vec(sums, 3 * k));
        string kn = Loc.S("UnitKN");
        string text = $"ΣR = {Vec(total)} {kn}: {string.Join(", ", parts)}";
        if (s.LoadSum is { Length: 3 } f)
        {
            double un = Math.Sqrt(Enumerable.Range(0, 3).Sum(i => (total[i] + f[i]) * (total[i] + f[i])));
            text += string.Create(c, $"; ΣF = {Vec(f)} {kn}, |ΣR + ΣF| = {un / 1e3:0.###} {kn}");
        }
        return text;
    }

    /// <summary>
    /// Копия поля, в которой численный ноль (|v| ≤ 1e-9 наибольшего по модулю значения той же компоненты на шаге)
    /// заменён точным нулём: иначе в таблице вместо 0 видны остатки округления вроде 7,9E-34.
    /// </summary>
    static double[] Denoise(double[] a, int comps)
    {
        var floor = new double[comps];
        for (int i = 0; i < a.Length; i++) floor[i % comps] = Math.Max(floor[i % comps], Math.Abs(a[i]));
        for (int c = 0; c < comps; c++) floor[c] *= 1e-9;
        var r = new double[a.Length];
        for (int i = 0; i < a.Length; i++) r[i] = Math.Abs(a[i]) <= floor[i % comps] ? 0 : a[i];
        return r;
    }

    // Индексы полей по номерам узлов/КЭ.
    readonly Dictionary<int, int> _nodeIndex = new(), _shellIndex = new(), _beamIndex = new(), _reactionIndex = new();
    RcSecantStepFields? _indexed;

    void IndexFields()
    {
        if (Fields == null || ReferenceEquals(Fields, _indexed)) return;
        if (_indexed == null || !Fields.ReactionNodeIds.SequenceEqual(_indexed.ReactionNodeIds))
        {
            _reactionIndex.Clear();
            for (int i = 0; i < Fields.ReactionNodeIds.Length; i++) _reactionIndex.TryAdd(Fields.ReactionNodeIds[i], i);
        }
        if (_indexed == null || !Fields.NodeIds.SequenceEqual(_indexed.NodeIds) || !Fields.ShellIds.SequenceEqual(_indexed.ShellIds)
            || !Fields.BeamIds.SequenceEqual(_indexed.BeamIds))
        {
            _nodeIndex.Clear(); _shellIndex.Clear(); _beamIndex.Clear();
            for (int i = 0; i < Fields.NodeIds.Length; i++) _nodeIndex.TryAdd(Fields.NodeIds[i], i);
            for (int i = 0; i < Fields.ShellIds.Length; i++) _shellIndex.TryAdd(Fields.ShellIds[i], i);
            for (int i = 0; i < Fields.BeamIds.Length; i++) _beamIndex.TryAdd(Fields.BeamIds[i], i);
        }
        _indexed = Fields;
    }

    /// <summary>Перемещение узла сетки на показанном шаге (м); нет поля — ноль.</summary>
    public Vector3D Displacement(int node)
    {
        if (Fields == null || !_nodeIndex.TryGetValue(node, out int i)) return default;
        var d = Fields.Displacements;
        return new Vector3D(d[6 * i], d[6 * i + 1], d[6 * i + 2]);
    }

    /// <summary>Реакция опор узла на показанном шаге (6, Н и Н·м, глобальные оси) и типы опор; null — опор в узле нет.</summary>
    public (double[] Values, byte Kinds)? Reaction(int node)
    {
        if (Fields == null || !_reactionIndex.TryGetValue(node, out int i)) return null;
        return (Fields.Reactions.AsSpan(6 * i, 6).ToArray(), Fields.ReactionKinds[i]);
    }

    /// <summary>Силы реакций узлов показанного шага (Н, глобальные оси) — для стрелок вида.</summary>
    public IEnumerable<(int Node, Vector3D Force)> ReactionForces()
    {
        if (Fields is not { } f) yield break;
        for (int i = 0; i < f.ReactionNodeIds.Length; i++)
            yield return (f.ReactionNodeIds[i], new Vector3D(f.Reactions[6 * i], f.Reactions[6 * i + 1], f.Reactions[6 * i + 2]));
    }

    /// <summary>Усилия пластины (8, СИ, оси выдачи); null — нет в полях.</summary>
    public double[]? ShellForces(int elem)
    {
        if (Fields == null || !_shellIndex.TryGetValue(elem, out int i)) return null;
        return Fields.ShellForces.AsSpan(i * RcSecantStepFields.ShellForceComponents, RcSecantStepFields.ShellForceComponents).ToArray();
    }

    /// <summary>Концевые силы стержня (12, местные, как localForce OpenSees); null — нет в полях.</summary>
    public FemElementEndForces? BeamForces(int elem)
    {
        if (Fields == null || !_beamIndex.TryGetValue(elem, out int i)) return null;
        var f = Fields.BeamForces;
        int o = i * RcSecantStepFields.BeamForceComponents;
        return new FemElementEndForces(elem, f[o], f[o + 1], f[o + 2], f[o + 3], f[o + 4], f[o + 5],
            f[o + 6], f[o + 7], f[o + 8], f[o + 9], f[o + 10], f[o + 11]);
    }

    public System.Windows.Input.ICommand GoToFieldsStepCommand { get; }

    // ------------------------------------------------------------ шаг → набор усилий

    public System.Windows.Input.ICommand CreateForceSetsCommand { get; }

    /// <summary>Запрос «шаг → набор усилий» (обрабатывает AppViewModel: сохраняет наборы).</summary>
    public event Action<FemCsfeaResultVM>? CreateForceSetsRequested;

    /// <summary>Наборы можно создать: у шага под ползунком записаны поля и он сошёлся.</summary>
    public bool CanCreateForceSets => _shownEnvelope != null ? false
        : _shownCombination != null ? Fields != null
        : Fields != null && FieldsStepIndex == SelectedStepIndex && SelectedStep?.Converged == true;

    public System.Windows.Input.ICommand CreateLoadCaseForceSetsCommand { get; }

    /// <summary>Запрос «наборы усилий по загружениям» линейного расчёта (обрабатывает AppViewModel).</summary>
    public event Action<FemCsfeaResultVM>? CreateLoadCaseForceSetsRequested;

    /// <summary>Наборы по загружениям — только у линейного расчёта с известной постановкой.</summary>
    public bool CanCreateLoadCaseForceSets => IsLinear && Analysis != null && _result.Id > 0;

    /// <summary>Наборы усилий шага под ползунком (пластины и стержни); пусто — поля шага не записаны.</summary>
    public List<ForceSet> BuildForceSets(IReadOnlyCollection<ForceSet> existing)
    {
        if (!CanCreateForceSets) return [];
        if (_shownCombination is { } comb)
            return FemCsfeaForceSetBuilder.BuildNamed(Schema, $"{comb.Name} — {_result.TaskTag}",
                $"Комбинация «{comb.Name}» = {comb.Formula} по линейному расчёту CSfea «{_result.TaskTag}» схемы «{Schema.Tag}»: " +
                "суперпозиция результатов загружений, без перерасчёта.", Fields!, existing);
        var step = SelectedStep!;
        string stage = step.Stage < Summary.Stages.Count ? Summary.Stages[step.Stage].Tag : "";
        return FemCsfeaForceSetBuilder.Build(Schema, _result.TaskTag, step, stage, Fields!, existing);
    }

    // ------------------------------------------------------------ поля пластин

    public IReadOnlyList<FemCsfeaOption<FemCsfeaShellField>> ShellFields { get; }

    static readonly Color NoCrack = Color.FromRgb(225, 235, 245), Cracked = Color.FromRgb(254, 224, 144),
        Yielded = Color.FromRgb(244, 109, 67), Failed = Colors.Black;

    /// <summary>Цвета и ключи подписей состояний пластины по значению поля «Состояние» (0…3).</summary>
    public static IReadOnlyList<(Color Color, string Key)> StateStyles { get; } =
        [(NoCrack, "FemCsfeaStateNone"), (Cracked, "FemCsfeaStateCracked"), (Yielded, "FemCsfeaStateYielded"), (Failed, "FemCsfeaStateFailed")];

    static bool IsCrack(FemCsfeaShellField field) => field is >= FemCsfeaShellField.AcrcLongBottom and <= FemCsfeaShellField.AcrcShortTop;

    /// <summary>
    /// Значение поля пластины на показанном шаге; ширина раскрытия — из готового расчёта
    /// показанного шага при текущей доле (см. <see cref="PrepareShellField"/>). Null — нет данных.
    /// </summary>
    public double? ShellFieldValue(int elem, FemCsfeaShellField field) =>
        ShellValue(elem, field, IsCrack(field) ? CurrentCrackWidths() : null);

    /// <summary>
    /// Значения поля на показанном шаге готовы (true) или запущен фоновый расчёт ширины раскрытия (false): по готовности —
    /// <see cref="CrackWidthsReady"/>.
    /// </summary>
    public bool PrepareShellField(FemCsfeaShellField field) => !IsCrack(field) || Fields == null || EnsureCrackWidths() != null;

    /// <summary>Досчитана ширина раскрытия трещин (шаг или доля сменились).</summary>
    public event Action? CrackWidthsReady;

    Dictionary<int, FemCsfeaCrackWidth>? CurrentCrackWidths() =>
        Fields != null && FieldsStepIndex is int idx && _crackKey == (Summary.Steps[idx].N, _longShare) ? _crackWidths : null;

    /// <summary>Оси эпюр стержня (Ey, Ez местной системы расчёта с углом поворота сечения); null — стержня нет в сетке.</summary>
    public (Vector3D Ey, Vector3D Ez)? BeamAxes(int elem)
    {
        _beamAxes ??= _beams.GroupBy(b => b.Tag).ToDictionary(g => g.Key, g => (g.First().Ey, g.First().Ez));
        return _beamAxes.TryGetValue(elem, out var axes) ? axes : null;
    }

    Dictionary<int, (Vector3D Ey, Vector3D Ez)>? _beamAxes;

    double? ShellValue(int elem, FemCsfeaShellField field, Dictionary<int, FemCsfeaCrackWidth>? cracks)
    {
        if (Fields == null || !_shellIndex.TryGetValue(elem, out int i)) return null;
        // Огибающая: величины, которые не сводятся к компоненте полей (средние по узлам, отпор), — из её вектора.
        if (_envelope != null && field is >= FemCsfeaShellField.Ux and <= FemCsfeaShellField.Uz or FemCsfeaShellField.FoundationPressure)
            return EnvelopeShell(elem, field)?.Pick(EnvelopeAggregate);
        int fo = i * RcSecantStepFields.ShellForceComponents, so = i * RcSecantStepFields.ShellStateComponents;
        double State(int k) => Fields.ShellStates[so + k];
        double? Disp(int c)
        {
            var nodes = _contours[elem];
            return nodes.Average(n => _nodeIndex.TryGetValue(n, out int k) ? Fields.Displacements[6 * k + c] : 0) * 1e3;
        }
        double? v = field switch
        {
            FemCsfeaShellField.Ux => Disp(0),
            FemCsfeaShellField.Uy => Disp(1),
            FemCsfeaShellField.Uz => Disp(2),
            >= FemCsfeaShellField.Nx and <= FemCsfeaShellField.Qy => Fields.ShellForces[fo + (field - FemCsfeaShellField.Nx)] / 1e3,
            FemCsfeaShellField.CrackBottom => State(0),
            FemCsfeaShellField.CrackTop => State(1),
            FemCsfeaShellField.Psi => State(2),
            FemCsfeaShellField.SigmaRatio => State(3),
            FemCsfeaShellField.EpsRatio => State(4),
            >= FemCsfeaShellField.AcrcLongBottom and <= FemCsfeaShellField.AcrcShortTop =>
                cracks != null && cracks.TryGetValue(elem, out var w) ? CrackValue(w, field) : null,
            FemCsfeaShellField.FoundationPressure => FoundationPressure(elem),
            FemCsfeaShellField.State => (Fields.ShellFlags[i] & RcSecantStepFields.Failed) != 0 ? 3
                : (Fields.ShellFlags[i] & RcSecantStepFields.Yielded) != 0 ? 2
                : (Fields.ShellFlags[i] & RcSecantStepFields.Cracked) != 0 ? 1 : 0,
            _ => null,
        };
        return v is double d && double.IsFinite(d) ? d : null;
    }

    /// <summary>
    /// Отпор основания C1 под пластиной на показанном шаге, кПа: p = −C1·(n·ū), ū — среднее перемещение узлов КЭ, n —
    /// нормаль КЭ вверх; + — основание сжато (осадка), − — отрыв (связь двусторонняя, отрыв в расчёте не моделируется).
    /// Null — у КЭ нет основания или полей.
    /// </summary>
    public double? FoundationPressure(int elem)
    {
        if (_envelope != null) return EnvelopeShell(elem, FemCsfeaShellField.FoundationPressure)?.Pick(EnvelopeAggregate);
        if (Fields == null || !_foundation.TryGetValue(elem, out var f) || !_contours.TryGetValue(elem, out var nodes)) return null;
        double un = 0;
        foreach (int n in nodes)
            if (_nodeIndex.TryGetValue(n, out int k))
            {
                var d = Fields.Displacements;
                un += f.Normal.X * d[6 * k] + f.Normal.Y * d[6 * k + 1] + f.Normal.Z * d[6 * k + 2];
            }
        return -f.C1 * un / nodes.Length / 1e3;
    }

    /// <summary>Единичная нормаль многоугольника (по Ньюэллу), повёрнутая вверх (n_z ≥ 0; у вертикального — n_x, n_y ≥ 0).</summary>
    static Vector3D UpNormal(Point3D[] p)
    {
        var n = new Vector3D();
        for (int i = 0; i < p.Length; i++)
        {
            var a = p[i];
            var b = p[(i + 1) % p.Length];
            n += new Vector3D((a.Y - b.Y) * (a.Z + b.Z), (a.Z - b.Z) * (a.X + b.X), (a.X - b.X) * (a.Y + b.Y));
        }
        if (n.Length < 1e-300) return new Vector3D(0, 0, 1);
        n.Normalize();
        const double eps = 1e-9;
        bool flip = n.Z < -eps || Math.Abs(n.Z) <= eps && (n.X < -eps || Math.Abs(n.X) <= eps && n.Y < 0);
        return flip ? -n : n;
    }

    // ------------------------------------------------------------ ширина раскрытия трещин

    readonly FemAnalysis? _analysis;
    List<FemCsfeaCrackElement>? _crackElements;
    Dictionary<int, FemCsfeaCrackElement> _crackById = new();
    (int Step, double Share)? _crackKey, _crackPending;
    Dictionary<int, FemCsfeaCrackWidth>? _crackWidths;
    CancellationTokenSource? _crackCts;

    /// <summary>Мозаики ширины раскрытия доступны: вид расчёта постановки — N или NL.</summary>
    public bool HasCrackWidths { get; }

    /// <summary>Идущий расчёт ширины раскрытия (завершённая задача — расчёта нет).</summary>
    public Task CrackWidthsTask { get; private set; } = Task.CompletedTask;

    double _longShare;
    /// <summary>Доля длительной части усилий шага (0…1) для a_crc,1 и a_crc по п. 8.2.7.</summary>
    public double LongShare
    {
        get => _longShare;
        set
        {
            if (!(value >= 0 && value <= 1) || value == _longShare) return;
            _longShare = value;
            OnPropertyChanged();
        }
    }

    static double CrackValue(FemCsfeaCrackWidth w, FemCsfeaShellField field) => field switch
    {
        FemCsfeaShellField.AcrcLongBottom => w.LongBottom,
        FemCsfeaShellField.AcrcLongTop => w.LongTop,
        FemCsfeaShellField.AcrcShortBottom => w.ShortBottom,
        _ => w.ShortTop,
    };

    /// <summary>
    /// Ширина раскрытия показанного шага при текущей доле: из кеша или null — расчёт запущен в фоне, по завершении —
    /// <see cref="CrackWidthsReady"/>. Сечения пластин — по текущему состоянию проекта (как при запуске расчёта схемы).
    /// </summary>
    Dictionary<int, FemCsfeaCrackWidth>? EnsureCrackWidths()
    {
        if (Fields == null || FieldsStepIndex is not int idx || _analysis == null) return null;
        var key = (Summary.Steps[idx].N, _longShare);
        if (_crackKey == key && _crackWidths != null) return _crackWidths;
        if (_crackPending == key) return null;
        var elements = EnsureCrackElements();
        _crackCts?.Cancel();
        _crackCts = new CancellationTokenSource();
        _crackPending = key;
        CrackWidthsTask = RunCrackWidthsAsync(key, elements, Fields, _crackCts.Token);
        return null;
    }

    /// <summary>ЖБ-пластины постановки с сечениями; вход читается из БД — в вызывающем (UI) потоке, как при запуске расчёта.</summary>
    List<FemCsfeaCrackElement> EnsureCrackElements()
    {
        if (_crackElements != null) return _crackElements;
        try
        {
            var input = FemCsfeaInputBuilder.Build(_db, Schema.Id, FemCsfeaSetup.FromAnalysis(_analysis!));
            _crackElements = FemCsfeaCrackWidths.Elements(FemRcModelAdapter.Adapt(input), _db.Materials);
        }
        catch (InvalidOperationException) { _crackElements = []; }
        _crackById = _crackElements.ToDictionary(e => e.Id);
        return _crackElements;
    }

    // ------------------------------------------------------------ доля длительной части по наборам N и NL

    public System.Windows.Input.ICommand LongShareFromSetsCommand { get; }

    /// <summary>Идущая оценка доли по наборам (завершённая задача — оценки нет).</summary>
    public Task LongShareTask { get; private set; } = Task.CompletedTask;

    string _longShareNote = "";
    /// <summary>Итог оценки доли по наборам: число КЭ и разброс доли по КЭ либо причина, по которой оценки нет.</summary>
    public string LongShareNote
    {
        get => _longShareNote;
        private set { if (value == _longShareNote) return; _longShareNote = value; OnPropertyChanged(); }
    }

    /// <summary>
    /// Доля длительной части одним числом на схему (<see cref="FemLongTermShare"/>) по всем парам пластинчатых наборов
    /// схемы «… (N)» — «… (NL)»: ЖБ-пластины постановки, вес КЭ — площадь/толщина.
    /// </summary>
    async Task LongShareFromSetsAsync()
    {
        if (_analysis == null) return;
        var sets = _db.ForceSets.Where(s => s.Kind == "shell" && s.SourceSchemaId == Schema.Id).ToList();
        var pairs = sets.Where(s => (s.Tag ?? "").EndsWith("(N)", StringComparison.Ordinal))
            .Select(s => (Total: s, Long: FemEtaPsi.LongTermSet(s, sets))).Where(p => p.Long != null).ToList();
        if (pairs.Count == 0) { LongShareNote = Loc.S("FemCsfeaCrackLongShareNoSets"); return; }

        var thickness = EnsureCrackElements().ToDictionary(e => e.Id, e => e.Section.H);
        var weights = new Dictionary<int, double>(thickness.Count);
        foreach (var (id, h) in thickness)
            if (_contours.TryGetValue(id, out var c) && h > 0) weights[id] = Area(c) / h;

        var result = await Task.Run(() => FemLongTermShare.Shells(
            pairs.Select(p => ((IReadOnlyList<ShellLoadItem>)p.Total.ShellItems, (IReadOnlyList<ShellLoadItem>)p.Long!.ShellItems)),
            e => thickness.TryGetValue(e, out double h) ? h : null, e => weights.GetValueOrDefault(e, 1.0)));
        if (result == null) { LongShareNote = Loc.S("FemCsfeaCrackLongShareNoData"); return; }
        LongShare = Math.Round(result.Share, 2);
        LongShareNote = string.Format(Loc.S("FemCsfeaCrackLongShareNote"), pairs.Count, result.Groups, result.MinGroupShare, result.MaxGroupShare,
            result.GroupsWithoutLong);
    }

    /// <summary>Площадь КЭ по исходным координатам узлов контура (треугольник или четырёхугольник).</summary>
    double Area(int[] contour)
    {
        var s = new Vector3D();
        for (int k = 1; k + 1 < contour.Length; k++)
            s += Vector3D.CrossProduct(_nodes[contour[k]] - _nodes[contour[0]], _nodes[contour[k + 1]] - _nodes[contour[0]]);
        return 0.5 * s.Length;
    }

    async Task RunCrackWidthsAsync((int Step, double Share) key, List<FemCsfeaCrackElement> elements, RcSecantStepFields fields,
        CancellationToken ct)
    {
        Dictionary<int, FemCsfeaCrackWidth> widths;
        try { widths = await Task.Run(() => FemCsfeaCrackWidths.Compute(elements, fields, key.Share, ct), ct); }
        catch (OperationCanceledException) { return; }
        if (_crackPending != key) return;
        _crackPending = null;
        _crackKey = key;
        _crackWidths = widths;
        CrackWidthsReady?.Invoke();
    }

    /// <summary>
    /// Штрихи по линиям трещин в центрах КЭ (пары точек) для мозаики ширины раскрытия: у КЭ с раскрытием — отрезок
    /// 0,8 наименьшего ребра под углом трещины к оси x сечения (против часовой с конца оси z сечения). Пусто — ширина
    /// раскрытия показанного шага при текущей доле ещё не посчитана (см. <see cref="PrepareShellField"/>).
    /// </summary>
    /// <param name="field">Мозаика ширины раскрытия (грань — по ней).</param>
    /// <param name="elems">КЭ пластин, у которых рисовать штрихи.</param>
    /// <param name="position">Положение узла на виде.</param>
    public Point3DCollection CrackStrokes(FemCsfeaShellField field, IEnumerable<int> elems, Func<int, Point3D> position)
    {
        var pts = new Point3DCollection();
        if (IsCrack(field) && CurrentCrackWidths() is { } widths)
        {
            bool top = field is FemCsfeaShellField.AcrcLongTop or FemCsfeaShellField.AcrcShortTop;
            foreach (int tag in elems)
            {
                if (!_contours.TryGetValue(tag, out var c) || !widths.TryGetValue(tag, out var w) || !(CrackValue(w, field) > 0)
                    || !_crackById.TryGetValue(tag, out var el)) continue;
                double beta = (top ? w.AngleTop : w.AngleBottom) * Math.PI / 180;
                var x = new Vector3D(el.AxisX[0], el.AxisX[1], el.AxisX[2]);
                var y = Vector3D.CrossProduct(new Vector3D(el.AxisZ[0], el.AxisZ[1], el.AxisZ[2]), x);
                var dir = x * Math.Cos(beta) + y * Math.Sin(beta);
                var center = new Point3D();
                double edge = double.MaxValue;
                for (int k = 0; k < c.Length; k++)
                {
                    Point3D a = position(c[k]), b = position(c[(k + 1) % c.Length]);
                    center += (Vector3D)a / c.Length;
                    edge = Math.Min(edge, (b - a).Length);
                }
                pts.Add(center - dir * (0.4 * edge));
                pts.Add(center + dir * (0.4 * edge));
            }
        }
        pts.Freeze();
        return pts;
    }

    // ------------------------------------------------------------ утилиты

    static string DofLabel(int dof) => dof switch { 0 => "ux", 1 => "uy", 2 => "uz", 3 => "rx", 4 => "ry", _ => "rz" };

    static (Vector3D Ey, Vector3D Ez) LocalFrame(Point3D pi, Point3D pj, double rotationDeg)
    {
        var frame = FemLocalAxis.LocalFrame(
            new FemLinearNode(0, pi.X, pi.Y, pi.Z, new bool[6]), new FemLinearNode(0, pj.X, pj.Y, pj.Z, new bool[6]), rotationDeg);
        return (new Vector3D(frame.Y.X, frame.Y.Y, frame.Y.Z), new Vector3D(frame.Z.X, frame.Z.Y, frame.Z.Z));
    }
}
