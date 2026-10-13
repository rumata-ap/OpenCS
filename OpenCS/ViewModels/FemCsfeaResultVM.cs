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
public sealed record FemCsfeaReactionRow(int Node, double Rx, double Ry, double Rz, double Mx, double My, double Mz, string Kind);

/// <summary>Строка таблицы перемещений узла сетки на показанном шаге: ux, uy, uz — мм; rx, ry, rz — мрад.</summary>
public sealed record FemCsfeaNodeRow(int Node, double Ux, double Uy, double Uz, double Rx, double Ry, double Rz);

/// <summary>
/// Строка таблицы усилий пластины на показанном шаге (оси выдачи, центр КЭ): Nx, Ny, Nxy, Qx, Qy — кН/м; Mx, My, Mxy —
/// кН·м/м; состояние сечения словом (у линейного расчёта — пусто).
/// </summary>
public sealed record FemCsfeaShellRow(int Elem, double Nx, double Ny, double Nxy, double Mx, double My, double Mxy,
    double Qx, double Qy, string State);

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

    /// <param name="analysis">Постановка результата: у линейного расчёта строки названы по загружениям схемы
    /// (<see cref="Tasks.FemAnalysisCaseNames"/>); null — имена из сводки.</param>
    public FemCsfeaResultVM(CalcResult result, DatabaseService db, FemSchema schema, FemAnalysis? analysis = null)
    {
        _db = db;
        _result = result;
        Schema = schema;
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
            }
        }
        GoToFieldsStepCommand = new RelayCommand(_ => GoToFieldsStep(), _ => FieldsStepIndex is int i && i != SelectedStepIndex);
        CreateForceSetsCommand = new RelayCommand(_ => CreateForceSetsRequested?.Invoke(this), _ => CanCreateForceSets);
        LongShareFromSetsCommand = new RelayCommand(_ => LongShareTask = LongShareFromSetsAsync(), _ => HasCrackWidths && LongShareTask.IsCompleted);

        int last = Summary.Steps.FindLastIndex(s => s.Converged);
        _selectedStepIndex = Math.Max(0, last);
        ApplyStep();
    }

    public FemSchema Schema { get; }
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
            if (v == _selectedStepIndex) return;
            _selectedStepIndex = v;
            OnPropertyChanged();
            ApplyStep();
        }
    }

    /// <summary>Шаг сводки под ползунком; null — шагов нет (ошибка до расчёта).</summary>
    public FemCsfeaStepSummary? SelectedStep => Summary.Steps.Count > 0 ? Summary.Steps[_selectedStepIndex] : null;

    /// <summary>Линейный расчёт: шаги сводки — независимые загружения (без λ и итераций).</summary>
    public bool IsLinear => Summary.Kind == FemCsfeaRunner.LinearAnalysisKind;

    public string CurrentStepLabel => SelectedStep is not { } s ? Loc.S("FemCsfeaNoSteps")
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

    /// <summary>Поля шага по сквозному номеру (кеш); null — не записаны или запись повреждена.</summary>
    public RcSecantStepFields? LoadFields(int stepN)
    {
        if (_cache.TryGetValue(stepN, out var f)) return f;
        if (_result.Id <= 0 || _db.GetFemResultStep(_result.Id, stepN) is not { } data) return null;
        try { f = FemCsfeaStepFieldsCodec.Unpack(data); }
        catch (Exception ex) when (ex is System.IO.InvalidDataException or System.IO.EndOfStreamException) { return null; }
        if (_cache.Count >= 4) _cache.Clear();
        return _cache[stepN] = f;
    }

    void ApplyStep()
    {
        FieldsStepIndex = null;
        Fields = null;
        FieldsNote = "";
        if (SelectedStep is { } s && _recorded.Count > 0)
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
        OnPropertyChanged(nameof(HasReactions));
        OnPropertyChanged(nameof(ShowNoReactionsNote));
        OnPropertyChanged(nameof(ReactionSummaryText));
    }

    void GoToFieldsStep()
    {
        if (FieldsStepIndex is int i) SelectedStepIndex = i;
    }

    // ------------------------------------------------------------ таблицы полей шага

    RcSecantStepFields? _rowsFields;
    IReadOnlyList<FemCsfeaNodeRow> _nodeRows = [];
    IReadOnlyList<FemCsfeaShellRow> _shellRows = [];
    IReadOnlyList<FemElementEndForces> _beamRows = [];
    IReadOnlyList<FemCsfeaReactionRow> _reactionRows = [];

    /// <summary>Перемещения узлов показанного шага — строятся при первом обращении (их читает только окно таблиц).</summary>
    public IReadOnlyList<FemCsfeaNodeRow> NodeRows { get { BuildRows(); return _nodeRows; } }

    /// <summary>Усилия пластин показанного шага.</summary>
    public IReadOnlyList<FemCsfeaShellRow> ShellRows { get { BuildRows(); return _shellRows; } }

    /// <summary>Концевые силы стержней показанного шага (Н, Н·м — в таблице переводятся в кН, кН·м).</summary>
    public IReadOnlyList<FemElementEndForces> BeamRows { get { BuildRows(); return _beamRows; } }

    /// <summary>Реакции опор показанного шага по узлам.</summary>
    public IReadOnlyList<FemCsfeaReactionRow> ReactionRows { get { BuildRows(); return _reactionRows; } }

    /// <summary>В полях показанного шага есть реакции (записи до реакций — без них).</summary>
    public bool HasReactions => Fields is { ReactionNodeIds.Length: > 0 };

    /// <summary>Поля показаны, а реакций в них нет — запись шага старого формата.</summary>
    public bool ShowNoReactionsNote => Fields != null && Fields.ReactionSums.Length == 0;

    void BuildRows()
    {
        if (ReferenceEquals(_rowsFields, Fields) && (Fields != null || _nodeRows.Count == 0)) return;
        _rowsFields = Fields;
        if (Fields is not { } f) { _nodeRows = []; _shellRows = []; _beamRows = []; _reactionRows = []; return; }

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

        var beams = new FemElementEndForces[f.BeamIds.Length];
        var bf = Denoise(f.BeamForces, RcSecantStepFields.BeamForceComponents);
        for (int i = 0; i < beams.Length; i++)
        {
            int o = i * RcSecantStepFields.BeamForceComponents;
            beams[i] = new FemElementEndForces(f.BeamIds[i], bf[o], bf[o + 1], bf[o + 2], bf[o + 3], bf[o + 4], bf[o + 5],
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
    public bool CanCreateForceSets => Fields != null && FieldsStepIndex == SelectedStepIndex && SelectedStep?.Converged == true;

    /// <summary>Наборы усилий шага под ползунком (пластины и стержни); пусто — поля шага не записаны.</summary>
    public List<ForceSet> BuildForceSets(IReadOnlyCollection<ForceSet> existing)
    {
        if (!CanCreateForceSets) return [];
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
            FemCsfeaShellField.State => (Fields.ShellFlags[i] & RcSecantStepFields.Failed) != 0 ? 3
                : (Fields.ShellFlags[i] & RcSecantStepFields.Yielded) != 0 ? 2
                : (Fields.ShellFlags[i] & RcSecantStepFields.Cracked) != 0 ? 1 : 0,
            _ => null,
        };
        return v is double d && double.IsFinite(d) ? d : null;
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
