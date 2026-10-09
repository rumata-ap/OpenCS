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
}

/// <summary>Пункт выпадающего списка: значение и подпись.</summary>
public sealed record FemCsfeaOption<T>(T Value, string Label)
{
    public override string ToString() => Label;
}

/// <summary>Строка журнала шагов: сквозной номер, стадия, λ, номер по плану, сходимость, итерации, невязка, контрольный узел.</summary>
public sealed record FemCsfeaStepRow(int N, string Stage, double LoadFactor, string Planned, string Converged, int Iterations,
    string Residual, string Control, string Fields);

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
    readonly double _diag;

    public FemCsfeaResultVM(CalcResult result, DatabaseService db, FemSchema schema)
    {
        _db = db;
        _result = result;
        Schema = schema;
        Summary = FemCsfeaResultSummary.Parse(result.DataJson) ?? new FemCsfeaResultSummary();
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
            s.Fields ? "✓" : "")).ToList();
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

        ShellFields = new FemCsfeaOption<FemCsfeaShellField>[]
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
        _selectedShellField = ShellFields[0];
        ForceComponents = new FemCsfeaOption<FemForceComponent?>[]
        {
            new(null, Loc.S("FemCsfeaFieldNone")),
            new(FemForceComponent.N, "N"), new(FemForceComponent.Qy, "Qy"), new(FemForceComponent.Qz, "Qz"),
            new(FemForceComponent.Mx, "Mx"), new(FemForceComponent.My, "My"), new(FemForceComponent.Mz, "Mz"),
        };
        _selectedForceComponent = ForceComponents[0];

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
        if (_nodes.Count > 0)
        {
            var p = _nodes.Values;
            double dx = p.Max(v => v.X) - p.Min(v => v.X), dy = p.Max(v => v.Y) - p.Min(v => v.Y), dz = p.Max(v => v.Z) - p.Min(v => v.Z);
            _diag = Math.Sqrt(dx * dx + dy * dy + dz * dz);
        }
        foreach (var (_, i, j, _, _) in _beams) { OriginalLines.Add(_nodes[i]); OriginalLines.Add(_nodes[j]); }
        foreach (var (_, c) in _shells)
            for (int k = 0; k < c.Length; k++) { OriginalLines.Add(_nodes[c[k]]); OriginalLines.Add(_nodes[c[(k + 1) % c.Length]]); }

        ResetDeformScaleCommand = new RelayCommand(_ => DeformScale = SuggestDeformScale());
        GoToFieldsStepCommand = new RelayCommand(_ => GoToFieldsStep(), _ => FieldsStepIndex is int i && i != SelectedStepIndex);
        CreateForceSetsCommand = new RelayCommand(_ => CreateForceSetsRequested?.Invoke(this), _ => CanCreateForceSets);
        ResetForceScaleCommand = new RelayCommand(_ => { _forceScale = SuggestForceScale(); RebuildForceDiagram(); OnPropertyChanged(nameof(ForceScale)); });

        int last = Summary.Steps.FindLastIndex(s => s.Converged);
        _selectedStepIndex = Math.Max(0, last);
        ApplyStep();
        _deformScale = SuggestDeformScale();
        RebuildDeformed();
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
    public bool HasGeometry => _nodes.Count > 0;
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
            RebuildDeformed();
        }
    }

    /// <summary>Шаг сводки под ползунком; null — шагов нет (ошибка до расчёта).</summary>
    public FemCsfeaStepSummary? SelectedStep => Summary.Steps.Count > 0 ? Summary.Steps[_selectedStepIndex] : null;

    public string CurrentStepLabel => SelectedStep is not { } s ? Loc.S("FemCsfeaNoSteps")
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
        RebuildMosaic();
        RebuildForceDiagram();
    }

    void GoToFieldsStep()
    {
        if (FieldsStepIndex is int i) SelectedStepIndex = i;
    }

    // Индексы полей по номерам узлов/КЭ.
    readonly Dictionary<int, int> _nodeIndex = new(), _shellIndex = new(), _beamIndex = new();
    RcSecantStepFields? _indexed;

    void IndexFields()
    {
        if (Fields == null || ReferenceEquals(Fields, _indexed)) return;
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

    // ------------------------------------------------------------ деформированная схема

    double _deformScale = 1;
    public double DeformScale
    {
        get => _deformScale;
        set
        {
            if (!FemScaleInput.IsValid(value) || value == _deformScale) return;
            _deformScale = value;
            OnPropertyChanged();
            RebuildDeformed();
        }
    }

    bool _showDeformed = true;
    public bool ShowDeformed
    {
        get => _showDeformed;
        set { if (value == _showDeformed) return; _showDeformed = value; OnPropertyChanged(); RebuildDeformed(); }
    }

    public System.Windows.Input.ICommand ResetDeformScaleCommand { get; }
    public System.Windows.Input.ICommand GoToFieldsStepCommand { get; }
    public System.Windows.Input.ICommand ResetForceScaleCommand { get; }

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

    public Point3DCollection OriginalLines { get; } = [];
    public Point3DCollection DeformedLines { get; private set; } = [];

    /// <summary>Положение узла на виде: деформированное (если включено) или исходное.</summary>
    public Point3D Position(int node)
    {
        var p = _nodes[node];
        return _showDeformed ? p + Displacement(node) * _deformScale : p;
    }

    double SuggestDeformScale()
    {
        if (Fields == null || _diag <= 0) return 1;
        double max = 0;
        for (int i = 0; i < Fields.NodeIds.Length; i++)
        {
            var d = Fields.Displacements;
            max = Math.Max(max, Math.Sqrt(d[6 * i] * d[6 * i] + d[6 * i + 1] * d[6 * i + 1] + d[6 * i + 2] * d[6 * i + 2]));
        }
        if (max <= 1e-12) return 1;
        double v = 0.05 * _diag / max;
        double r = Math.Round(v, 2);
        return r > 0 ? r : v;
    }

    void RebuildDeformed()
    {
        var pts = new Point3DCollection();
        if (_showDeformed && Fields != null)
        {
            foreach (var (_, i, j, _, _) in _beams) { pts.Add(Position(i)); pts.Add(Position(j)); }
            foreach (var (_, c) in _shells)
                for (int k = 0; k < c.Length; k++) { pts.Add(Position(c[k])); pts.Add(Position(c[(k + 1) % c.Length])); }
        }
        pts.Freeze();
        DeformedLines = pts;
        OnPropertyChanged(nameof(DeformedLines));
        RebuildShellMeshes();
        RebuildForceDiagram();
    }

    // ------------------------------------------------------------ мозаика пластин

    public IReadOnlyList<FemCsfeaOption<FemCsfeaShellField>> ShellFields { get; }

    FemCsfeaOption<FemCsfeaShellField> _selectedShellField;
    public FemCsfeaOption<FemCsfeaShellField> SelectedShellField
    {
        get => _selectedShellField;
        set
        {
            if (value == null || value == _selectedShellField) return;
            _selectedShellField = value;
            OnPropertyChanged();
            RebuildMosaic();
            RebuildShellMeshes();
        }
    }

    /// <summary>Мозаика включена (для легенды <c>PlateRebarMosaicLegend</c>).</summary>
    public bool IsActive => _selectedShellField.Value != FemCsfeaShellField.None && HasShells;
    public string LegendTitle { get; private set; } = "";
    public IReadOnlyList<PlateRebarMosaicLegendItem> Legend { get; private set; } = [];

    string _hoverText = "";
    public string HoverText
    {
        get => _hoverText;
        private set { if (value == _hoverText) return; _hoverText = value; OnPropertyChanged(); }
    }

    string _thresholdsText = "";
    /// <summary>Ручные пороги шкалы («a; b; c»); пусто — автошкала.</summary>
    public string ThresholdsText
    {
        get => _thresholdsText;
        set
        {
            value ??= "";
            if (value == _thresholdsText) return;
            _thresholdsText = value;
            OnPropertyChanged();
            RebuildMosaic();
            RebuildShellMeshes();
        }
    }

    readonly Dictionary<int, double> _mosaicValues = new();
    readonly Dictionary<int, Color> _mosaicColors = new();

    /// <summary>Пластины одного цвета: цвет, КЭ (по порядку треугольников — по два на четырёхугольник).</summary>
    public sealed record ShellPatch(Color Color, MeshGeometry3D Mesh, IReadOnlyList<int> TriangleElements);

    public IReadOnlyList<ShellPatch> ShellPatches { get; private set; } = [];

    /// <summary>Цвет пластин без мозаики.</summary>
    public static Color ShellColor => Color.FromArgb(150, 200, 210, 225);

    static readonly Color NoCrack = Color.FromRgb(225, 235, 245), Cracked = Color.FromRgb(254, 224, 144),
        Yielded = Color.FromRgb(244, 109, 67), Failed = Colors.Black;

    double? ShellValue(int elem, FemCsfeaShellField field)
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
            FemCsfeaShellField.State => (Fields.ShellFlags[i] & RcSecantStepFields.Failed) != 0 ? 3
                : (Fields.ShellFlags[i] & RcSecantStepFields.Yielded) != 0 ? 2
                : (Fields.ShellFlags[i] & RcSecantStepFields.Cracked) != 0 ? 1 : 0,
            _ => null,
        };
        return v is double d && double.IsFinite(d) ? d : null;
    }

    void RebuildMosaic()
    {
        _mosaicValues.Clear();
        _mosaicColors.Clear();
        var field = _selectedShellField.Value;
        var legend = new List<PlateRebarMosaicLegendItem>();
        if (IsActive && Fields != null)
        {
            // Перемещения ux/uy/uz по КЭ — среднее узлов контура.
            foreach (var (tag, _) in _shells)
                if (ShellValue(tag, field) is double v) _mosaicValues[tag] = v;
            int missing = _shells.Count - _mosaicValues.Count;
            if (field == FemCsfeaShellField.State)
            {
                var states = new[] { (NoCrack, "FemCsfeaStateNone"), (Cracked, "FemCsfeaStateCracked"),
                    (Yielded, "FemCsfeaStateYielded"), (Failed, "FemCsfeaStateFailed") };
                var counts = new int[4];
                foreach (var (tag, v) in _mosaicValues) { int k = (int)v; counts[k]++; _mosaicColors[tag] = states[k].Item1; }
                for (int k = 3; k >= 0; k--) legend.Add(new(PlateRebarMosaicVM.Freeze(states[k].Item1), Loc.S(states[k].Item2), counts[k]));
            }
            else
            {
                bool diverging = field is >= FemCsfeaShellField.Ux and <= FemCsfeaShellField.Qy;
                var numbers = _mosaicValues.Values.ToList();
                double min = numbers.Count > 0 ? numbers.Min() : 0, max = numbers.Count > 0 ? numbers.Max() : 0;
                var manual = PlateRebarMosaicVM.ParseThresholds(_thresholdsText);
                var scale = manual.Count > 0 ? PlateRebarMosaicScale.Manual(manual, diverging) : PlateRebarMosaicScale.Auto(numbers, diverging);
                var colors = PlateRebarMosaicVM.BandColors(scale, diverging ? PlateRebarMosaicVM.Palette.Forces
                    : field is FemCsfeaShellField.SigmaRatio or FemCsfeaShellField.EpsRatio ? PlateRebarMosaicVM.Palette.Utilization
                    : PlateRebarMosaicVM.Palette.Rebar);
                var counts = new int[scale.Bands.Count];
                foreach (var (tag, v) in _mosaicValues) { int b = scale.BandOf(v); counts[b]++; _mosaicColors[tag] = colors[b]; }
                for (int b = scale.Bands.Count - 1; b >= 0; b--)
                    legend.Add(new(PlateRebarMosaicVM.Freeze(colors[b]), PlateRebarMosaicVM.BandLabel(scale.Bands[b], min, max), counts[b]));
            }
            if (missing > 0) legend.Add(new(PlateRebarMosaicVM.Freeze(Fem3DVM.ShellBgColor), Loc.S("PlateRebarMosaicNoData"), missing));
        }
        Legend = legend;
        LegendTitle = IsActive ? $"{_selectedShellField.Label}\n{CurrentStepLabel}" : "";
        HoverText = "";
        OnPropertyChanged(nameof(IsActive));
        OnPropertyChanged(nameof(Legend));
        OnPropertyChanged(nameof(LegendTitle));
    }

    void RebuildShellMeshes()
    {
        var groups = new Dictionary<Color, (MeshGeometry3D Mesh, List<int> Elements)>();
        foreach (var (tag, c) in _shells)
        {
            var color = _mosaicColors.TryGetValue(tag, out var mc) ? mc : IsActive ? Fem3DVM.ShellBgColor : ShellColor;
            if (!groups.TryGetValue(color, out var g)) groups[color] = g = (new MeshGeometry3D(), new List<int>());
            int n0 = g.Mesh.Positions.Count;
            foreach (int node in c) g.Mesh.Positions.Add(Position(node));
            for (int k = 1; k + 1 < c.Length; k++)
            {
                g.Mesh.TriangleIndices.Add(n0); g.Mesh.TriangleIndices.Add(n0 + k); g.Mesh.TriangleIndices.Add(n0 + k + 1);
                g.Elements.Add(tag);
            }
        }
        ShellPatches = groups.Select(kv =>
        {
            kv.Value.Mesh.Freeze();
            return new ShellPatch(kv.Key, kv.Value.Mesh, kv.Value.Elements);
        }).ToList();
        OnPropertyChanged(nameof(ShellPatches));
    }

    /// <summary>Подсказка легенды для КЭ под курсором; null — курсор не над КЭ.</summary>
    public void SetHover(int? elem)
    {
        if (elem is not int e || !IsActive) { HoverText = ""; return; }
        string text = !_mosaicValues.TryGetValue(e, out double v) ? Loc.S("PlateRebarMosaicNoData")
            : _selectedShellField.Value == FemCsfeaShellField.State
                ? Loc.S(((int)v) switch { 0 => "FemCsfeaStateNone", 1 => "FemCsfeaStateCracked", 2 => "FemCsfeaStateYielded", _ => "FemCsfeaStateFailed" })
                : v.ToString("0.###", CultureInfo.CurrentCulture);
        HoverText = $"{Loc.S("MosaicElement")} {e}: {text}";
    }

    // ------------------------------------------------------------ эпюры стержней

    public IReadOnlyList<FemCsfeaOption<FemForceComponent?>> ForceComponents { get; }

    FemCsfeaOption<FemForceComponent?> _selectedForceComponent;
    public FemCsfeaOption<FemForceComponent?> SelectedForceComponent
    {
        get => _selectedForceComponent;
        set
        {
            if (value == null || value == _selectedForceComponent) return;
            _selectedForceComponent = value;
            _forceScale = SuggestForceScale();
            OnPropertyChanged();
            OnPropertyChanged(nameof(ForceScale));
            RebuildForceDiagram();
        }
    }

    double _forceScale = 1;
    /// <summary>Масштаб эпюры, м на кН (кН·м).</summary>
    public double ForceScale
    {
        get => _forceScale;
        set
        {
            if (!FemScaleInput.IsValid(value) || value == _forceScale) return;
            _forceScale = value;
            OnPropertyChanged();
            RebuildForceDiagram();
        }
    }

    public MeshGeometry3D ForceDiagramMesh { get; private set; } = new();
    public Point3D? ForceMaxLabelPosition { get; private set; }
    public string? ForceMaxLabelText { get; private set; }
    public Point3D? ForceMinLabelPosition { get; private set; }
    public string? ForceMinLabelText { get; private set; }

    IEnumerable<(double Vi, double Vj)> ForceValues(FemForceComponent c)
    {
        foreach (var (tag, _, _, _, _) in _beams)
            if (BeamForces(tag) is { } f)
            {
                var pair = FemForceEndpointConverter.Convert(f, FemForceEndpointSignPolicy.OpenSeesDefault);
                yield return (FemForceEndpointConverter.ReadComponent(pair.Start, c), FemForceEndpointConverter.ReadComponent(pair.End, c));
            }
    }

    double SuggestForceScale() => _selectedForceComponent.Value is { } c
        ? FemForceScaleCalculator.Suggest(_diag, ForceValues(c).SelectMany(v => new[] { v.Vi, v.Vj }).ToList())
        : 1;

    void RebuildForceDiagram()
    {
        var segs = new List<(Point3D, Point3D, Vector3D, Vector3D)>();
        double maxV = double.NegativeInfinity, minV = double.PositiveInfinity;
        Point3D maxPos = default, minPos = default;
        if (_selectedForceComponent.Value is { } comp)
            foreach (var (tag, i, j, ey, ez) in _beams)
            {
                if (BeamForces(tag) is not { } f) continue;
                var pair = FemForceEndpointConverter.Convert(f, FemForceEndpointSignPolicy.OpenSeesDefault);
                double vi = FemForceEndpointConverter.ReadComponent(pair.Start, comp), vj = FemForceEndpointConverter.ReadComponent(pair.End, comp);
                var axis = comp is FemForceComponent.Qy or FemForceComponent.Mz ? ey : ez;
                Point3D pi = Position(i), pj = Position(j);
                var oi = axis * (vi / 1e3 * _forceScale);
                var oj = axis * (vj / 1e3 * _forceScale);
                segs.Add((pi, pj, oi, oj));
                if (vi > maxV) { maxV = vi; maxPos = pi + oi; }
                if (vj > maxV) { maxV = vj; maxPos = pj + oj; }
                if (vi < minV) { minV = vi; minPos = pi + oi; }
                if (vj < minV) { minV = vj; minPos = pj + oj; }
            }
        ForceDiagramMesh = FemForceDiagramFactory.BuildRibbons(segs);
        bool has = segs.Count > 0;
        string unit = _selectedForceComponent.Value is FemForceComponent.N or FemForceComponent.Qy or FemForceComponent.Qz
            ? Loc.S("UnitKN") : Loc.S("UnitKNm");
        ForceMaxLabelPosition = has ? maxPos : null;
        ForceMaxLabelText = has ? $"{maxV / 1e3:G4} {unit}" : null;
        ForceMinLabelPosition = has ? minPos : null;
        ForceMinLabelText = has ? $"{minV / 1e3:G4} {unit}" : null;
        OnPropertyChanged(nameof(ForceDiagramMesh));
        OnPropertyChanged(nameof(ForceMaxLabelText));
    }

    // ------------------------------------------------------------ утилиты

    static string DofLabel(int dof) => dof switch { 0 => "ux", 1 => "uy", 2 => "uz", 3 => "rx", 4 => "ry", _ => "rz" };

    static (Vector3D Ey, Vector3D Ez) LocalFrame(Point3D pi, Point3D pj, double rotationDeg)
    {
        var frame = FemLocalAxis.LocalFrame(
            new FemLinearNode(0, pi.X, pi.Y, pi.Z, new bool[6]), new FemLinearNode(0, pj.X, pj.Y, pj.Z, new bool[6]), rotationDeg);
        return (new Vector3D(frame.Y.X, frame.Y.Y, frame.Y.Z), new Vector3D(frame.Z.X, frame.Z.Y, frame.Z.Z));
    }

    /// <summary>Номер КЭ пластины по треугольнику патча (для наведения).</summary>
    public static int? ElementOf(ShellPatch patch, int triangle) =>
        triangle >= 0 && triangle < patch.TriangleElements.Count ? patch.TriangleElements[triangle] : null;
}
