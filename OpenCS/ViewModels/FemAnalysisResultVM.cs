using System.Text.Json;
using System.IO;
using System.Windows.Media.Media3D;
using CScore;
using CScore.Fem;
using OpenCS.OpenSees.CScore;
using OpenCS.OpenSees.Results;
using OpenCS.OpenSees.Structural;
using OpenCS.Utilites;

namespace OpenCS.ViewModels;

/// <summary>ViewModel результатной вкладки линейного OpenSees-расчёта FEM-схемы.</summary>
public class FemAnalysisResultVM : ViewModelBase
{
    /// <summary>Статус расчёта (ok/not_converged/error/cancelled).</summary>
    public string Status { get; }
    /// <summary>Каталог артефактов запуска, если есть.</summary>
    public string? ArtifactDirectory { get; }
    /// <summary>Есть ли каталог артефактов.</summary>
    public bool HasArtifacts => !string.IsNullOrEmpty(ArtifactDirectory);
    /// <summary>Диагностические сообщения и ошибки валидации.</summary>
    public IReadOnlyList<string> Diagnostics { get; }
    public bool HasDiagnostics => Diagnostics.Count > 0 && Status != "ok";

    public IReadOnlyList<FemNodeDisplacement> Displacements { get; private set; } = [];
    public IReadOnlyList<FemNodeReaction> Reactions { get; private set; } = [];
    public IReadOnlyList<FemElementEndForces> ElementForces { get; private set; } = [];

    /// <summary>Точка графика «коэффициент нагрузки по шагам» для результатной вкладки.</summary>
    public sealed record FemLoadFactorPoint(int Step, double LoadFactor, bool Converged);

    /// <summary>True, если результат — нелинейный расчёт (несколько шагов, доступен слайдер шага).</summary>
    public bool IsNonlinear { get; }
    /// <summary>Полная история шагов; для линейного результата — один синтетический шаг.</summary>
    public IReadOnlyList<FemNonlinearStepResult> Steps { get; }
    /// <summary>Точки для графика load-factor по шагам.</summary>
    public IReadOnlyList<FemLoadFactorPoint> LoadFactorPoints { get; }
    /// <summary>Каталог точек интегрирования, доступных для просмотра fiber-состояния.</summary>
    public IReadOnlyList<FemSectionLocationRow> SectionLocations { get; private set; } = [];
    public bool HasSectionResults => SectionLocations.Count > 0;

    FemSectionLocationRow? _selectedSectionLocation;
    /// <summary>Выбранная точка интегрирования вдоль конструктивного стержня.</summary>
    public FemSectionLocationRow? SelectedSectionLocation
    {
        get => _selectedSectionLocation;
        set
        {
            if (Equals(value, _selectedSectionLocation)) return;
            _selectedSectionLocation = value;
            LoadSelectedFiberStates();
            RebuildSectionPlots();
            OnPropertyChanged();
            OnPropertyChanged(nameof(SelectedSectionPositionLabel));
        }
    }

    /// <summary>Положение выбранного сечения в метрах и долях длины стержня.</summary>
    public string SelectedSectionPositionLabel => _selectedSectionLocation == null
        ? ""
        : string.Format(Loc.S("FemResultSectionPositionValue"),
            _selectedSectionLocation.PositionFromMemberStartM,
            _selectedSectionLocation.MemberLengthM,
            _selectedSectionLocation.RelativePosition);
    public bool HasSelectedSectionState => SectionStressPlot != null && SectionStrainPlot != null;

    /// <summary>Карта напряжений выбранного сечения.</summary>
    public SectionPlotVM? SectionStressPlot { get; private set; }
    /// <summary>Карта деформаций выбранного сечения.</summary>
    public SectionPlotVM? SectionStrainPlot { get; private set; }
    /// <summary>Верхняя граница слайдера шага.</summary>
    public int MaxStepIndex => Math.Max(0, Steps.Count - 1);

    int _selectedStepIndex;
    /// <summary>Выбранный шаг (0-based) — управляет Displacements/Reactions/ElementForces и 3D-видом.</summary>
    public int SelectedStepIndex
    {
        get => _selectedStepIndex;
        set
        {
            int clamped = Steps.Count == 0 ? 0 : Math.Clamp(value, 0, Steps.Count - 1);
            if (clamped == _selectedStepIndex) return;
            _selectedStepIndex = clamped;
            ApplyStepData();
            RebuildDeformed();
            RebuildForceDiagram();
            RebuildSectionPlots();
            OnPropertyChanged();
            OnPropertyChanged(nameof(Displacements));
            OnPropertyChanged(nameof(Reactions));
            OnPropertyChanged(nameof(ElementForces));
            OnPropertyChanged(nameof(SelectedDisplacementRow));
            OnPropertyChanged(nameof(SelectedReactionRow));
            OnPropertyChanged(nameof(SelectedForceRow));
            OnPropertyChanged(nameof(CurrentStepLabel));
        }
    }

    /// <summary>Подпись текущего шага: номер, коэффициент нагрузки, статус сходимости.</summary>
    public string CurrentStepLabel
    {
        get
        {
            if (Steps.Count == 0) return "";
            var s = Steps[SelectedStepIndex];
            string convergedText = s.Converged ? Loc.S("FemResultConverged") : Loc.S("FemResultNotConverged");
            return string.Format(Loc.S("FemResultStepLabel"), SelectedStepIndex + 1, Steps.Count, s.LoadFactor, convergedText);
        }
    }

    void ApplyStepData()
    {
        var step = Steps[SelectedStepIndex];
        Displacements = step.Displacements;
        Reactions = step.Reactions;
        ElementForces = step.ElementForces;

        _dispByTag.Clear();
        foreach (var d in Displacements) _dispByTag[d.NodeTag] = new Vector3D(d.Ux, d.Uy, d.Uz);

        _forcesByElem.Clear();
        foreach (var f in ElementForces) _forcesByElem[f.ElemTag] = f;
    }

    readonly Dictionary<int, Point3D> _originalByTag = [];
    readonly Dictionary<int, Vector3D> _dispByTag = [];
    readonly List<(int I, int J)> _elementPairs = [];

    /// <summary>Геометрия mesh-элемента с локальным базисом для эпюр.</summary>
    readonly record struct ElementGeom(int Tag, string? SourceMemberTag, int Ni, int Nj, Point3D Pi, Point3D Pj, Vector3D Ey, Vector3D Ez);
    readonly List<ElementGeom> _elementGeoms = [];
    readonly Dictionary<int, FemElementEndForces> _forcesByElem = [];
    readonly DatabaseService _database;
    readonly Dictionary<int, int?> _sectionIdByElement = [];
    FemNonlinearResult? _nonlinearResult;
    IReadOnlyList<FemNonlinearFiberState> _fiberStates = [];
    string? _fiberStatePath;

    /// <summary>Точки линий исходной (недеформированной) схемы, парами.</summary>
    public Point3DCollection OriginalLines { get; } = [];
    /// <summary>Точки линий деформированной схемы, парами (масштаб DeformScale).</summary>
    public Point3DCollection DeformedLines { get; private set; } = [];
    /// <summary>Точки узлов деформированной схемы.</summary>
    public Point3DCollection DeformedNodes { get; private set; } = [];
    /// <summary>Деформированные координаты узлов по тегу mesh-узла — для pick targets в 3D-виде.</summary>
    public IReadOnlyDictionary<int, Point3D> DeformedNodesByTag { get; private set; } = new Dictionary<int, Point3D>();
    /// <summary>Деформированные концы каждого mesh-элемента (тег, конец i, конец j) — для pick targets в 3D-виде.</summary>
    public IReadOnlyList<(int Tag, Point3D P0, Point3D P1)> DeformedElementSegments { get; private set; } = [];

    /// <summary>Есть ли стержневая геометрия для 3D-отображения.</summary>
    public bool HasGeometry => _elementPairs.Count > 0;

    int? _selectedNodeTag;
    /// <summary>Тег выбранного в 3D-виде (или в таблице) узла; взаимоисключающе с <see cref="SelectedElemTag"/>.</summary>
    public int? SelectedNodeTag => _selectedNodeTag;

    int? _selectedElemTag;
    /// <summary>Тег выбранного в 3D-виде (или в таблице) mesh-элемента; взаимоисключающе с <see cref="SelectedNodeTag"/>.</summary>
    public int? SelectedElemTag => _selectedElemTag;

    /// <summary>Выбирает узел (или снимает весь выбор при null); сбрасывает выбор элемента.</summary>
    public void SelectNode(int? tag)
    {
        if (_selectedNodeTag == tag && _selectedElemTag == null) return;
        _selectedNodeTag = tag;
        _selectedElemTag = null;
        NotifySelectionChanged();
    }

    /// <summary>Выбирает mesh-элемент (или снимает весь выбор при null); сбрасывает выбор узла.</summary>
    public void SelectElement(int? tag)
    {
        if (_selectedElemTag == tag && _selectedNodeTag == null) return;
        _selectedElemTag = tag;
        _selectedNodeTag = null;
        NotifySelectionChanged();
    }

    void NotifySelectionChanged()
    {
        OnPropertyChanged(nameof(SelectedNodeTag));
        OnPropertyChanged(nameof(SelectedElemTag));
        OnPropertyChanged(nameof(SelectedDisplacementRow));
        OnPropertyChanged(nameof(SelectedReactionRow));
        OnPropertyChanged(nameof(SelectedForceRow));
    }

    /// <summary>Строка таблицы «Перемещения», соответствующая выбранному узлу (для подсветки грида).</summary>
    public FemNodeDisplacement? SelectedDisplacementRow =>
        _selectedNodeTag is int tag ? Displacements.FirstOrDefault(d => d.NodeTag == tag) : null;
    /// <summary>Строка таблицы «Реакции», соответствующая выбранному узлу (для подсветки грида).</summary>
    public FemNodeReaction? SelectedReactionRow =>
        _selectedNodeTag is int tag ? Reactions.FirstOrDefault(r => r.NodeTag == tag) : null;
    /// <summary>Строка таблицы «Усилия», соответствующая выбранному элементу (для подсветки грида).</summary>
    public FemElementEndForces? SelectedForceRow =>
        _selectedElemTag is int tag ? ElementForces.FirstOrDefault(f => f.ElemTag == tag) : null;

    /// <summary>Резолвит тег mesh-элемента в тег конструктивного стержня (для действий контекстного меню).</summary>
    public string ResolveMemberTag(int meshElemTag)
    {
        var eg = _elementGeoms.FirstOrDefault(g => g.Tag == meshElemTag);
        return FemResultIdentity.ResolveMemberTag(eg.SourceMemberTag, meshElemTag);
    }

    double _deformScale = 1.0;
    /// <summary>Масштаб визуализации перемещений.</summary>
    public double DeformScale
    {
        get => _deformScale;
        set { if (!FemScaleInput.IsValid(value) || value == _deformScale) return; _deformScale = value; RebuildDeformed(); OnPropertyChanged(); }
    }

    /// <summary>Компоненты усилий для выбора эпюры.</summary>
    public IReadOnlyList<FemForceComponent> ForceComponents { get; } =
        [FemForceComponent.N, FemForceComponent.Qy, FemForceComponent.Qz,
         FemForceComponent.Mx, FemForceComponent.My, FemForceComponent.Mz];

    FemForceComponent _selectedForceComponent = FemForceComponent.Mz;
    /// <summary>Выбранная компонента усилия для 3D-эпюры.</summary>
    public FemForceComponent SelectedForceComponent
    {
        get => _selectedForceComponent;
        set { if (value == _selectedForceComponent) return; _selectedForceComponent = value; RebuildForceDiagram(); OnPropertyChanged(); }
    }

    double _forceScale = 1.0;
    /// <summary>Масштаб 3D-эпюры усилий.</summary>
    public double ForceScale
    {
        get => _forceScale;
        set { if (!FemScaleInput.IsValid(value) || value == _forceScale) return; _forceScale = value; RebuildForceDiagram(); OnPropertyChanged(); }
    }

    /// <summary>Геометрия ленты выбранной эпюры.</summary>
    public MeshGeometry3D? ForceDiagramMesh { get; private set; }

    /// <summary>Положение максимума выбранной компоненты усилия на ленте эпюры (null, если данных нет).</summary>
    public Point3D? ForceMaxLabelPosition { get; private set; }
    /// <summary>Подпись максимума (значение в кН/кН·м + единица).</summary>
    public string? ForceMaxLabelText { get; private set; }
    /// <summary>Положение минимума выбранной компоненты усилия на ленте эпюры (null, если данных нет).</summary>
    public Point3D? ForceMinLabelPosition { get; private set; }
    /// <summary>Подпись минимума (значение в кН/кН·м + единица).</summary>
    public string? ForceMinLabelText { get; private set; }

    public System.Windows.Input.ICommand ResetDeformScaleCommand { get; }
    public System.Windows.Input.ICommand ResetForceScaleCommand { get; }

    public event Action<string>? ShowMemberForceRequested;
    public event Action<string>? GoToSectionRequested;
    public event Action<string>? ShowNodeValuesRequested;

    public void RequestShowMemberForce(string tag) => ShowMemberForceRequested?.Invoke(tag);
    public void RequestGoToSection(string tag) => GoToSectionRequested?.Invoke(tag);
    public void RequestShowNodeValues(string tag) => ShowNodeValuesRequested?.Invoke(tag);

    /// <summary>Возвращает координаты, перемещения и реакции узла из результата.</summary>
    public bool TryGetNodeResult(string tag, out Point3D point,
        out FemNodeDisplacement? displacement, out FemNodeReaction? reaction)
    {
        displacement = null;
        reaction = null;
        point = default;
        if (!int.TryParse(tag, out var nodeTag) || !_originalByTag.TryGetValue(nodeTag, out point))
            return false;
        displacement = Displacements.FirstOrDefault(item => item.NodeTag == nodeTag);
        reaction = Reactions.FirstOrDefault(item => item.NodeTag == nodeTag);
        return true;
    }

    public FemAnalysisResultVM(CalcResult result, DatabaseService db, FemSchema schema)
    {
        _database = db;
        ResetDeformScaleCommand = new RelayCommand(_ => DeformScale = SuggestScale());
        ResetForceScaleCommand = new RelayCommand(_ => ForceScale = SuggestForceScale());

        Status = result.Status;

        var nonlinear = ParseNonlinearResult(result.DataJson);
        if (nonlinear != null)
        {
            _nonlinearResult = nonlinear;
            IsNonlinear = true;
            Steps = nonlinear.Steps;
            ArtifactDirectory = nonlinear.ArtifactDirectory;
            Diagnostics = CollectDiagnostics(result.DataJson, nonlinear.Diagnostics);
        }
        else
        {
            var linear = ParseResult(result.DataJson);
            IsNonlinear = false;
            ArtifactDirectory = linear?.ArtifactDirectory;
            Diagnostics = CollectDiagnostics(result.DataJson, linear?.Diagnostics ?? []);
            Steps = linear != null
                ? [new FemNonlinearStepResult(1, 1.0, Status == "ok", linear.Displacements, linear.Reactions, linear.ElementForces)]
                : [];
        }
        LoadFactorPoints = Steps.Select(s => new FemLoadFactorPoint(s.StepIndex, s.LoadFactor, s.Converged)).ToList();

        // Геометрия из mesh-снимка схемы
        foreach (var n in db.GetFemMeshNodes(schema.Id))
            if (int.TryParse(n.NodeTag, out int tag))
                _originalByTag[tag] = new Point3D(n.X, n.Y, n.Z);

        var meshNodes = db.GetFemMeshNodes(schema.Id);
        var meshElements = db.GetFemMeshElements(schema.Id);
        var sourceMembers = db.GetFemMembers(schema.Id);
        foreach (var e in meshElements)
        {
            var ends = JsonSerializer.Deserialize<int[]>(e.NodeIdsJson) ?? [];
            if (ends.Length != 2 || !_originalByTag.ContainsKey(ends[0]) || !_originalByTag.ContainsKey(ends[1]))
                continue;
            _elementPairs.Add((ends[0], ends[1]));
            if (int.TryParse(e.ElemTag, out int etag))
            {
                _sectionIdByElement[etag] = e.CrossSectionId;
                var pi = _originalByTag[ends[0]];
                var pj = _originalByTag[ends[1]];
                double rotationDeg = sourceMembers.FirstOrDefault(m => m.ElemTag == e.SourceMemberTag)?.RotationDeg ?? 0;
                var (ey, ez) = LocalFrame(pi, pj, rotationDeg);
                _elementGeoms.Add(new ElementGeom(etag, e.SourceMemberTag, ends[0], ends[1], pi, pj, ey, ez));
            }
        }

        foreach (var (i, j) in _elementPairs)
        {
            OriginalLines.Add(_originalByTag[i]);
            OriginalLines.Add(_originalByTag[j]);
        }

        LoadSectionResultData(meshNodes, meshElements, sourceMembers);

        _selectedStepIndex = 0;
        for (int i = Steps.Count - 1; i >= 0; i--)
            if (Steps[i].Converged)
            {
                _selectedStepIndex = i;
                break;
            }
        if (Steps.Count > 0) ApplyStepData();

        _deformScale = SuggestScale();
        RebuildDeformed();

        _forceScale = SuggestForceScale();
        RebuildForceDiagram();
        RebuildSectionPlots();
    }

    void LoadSectionResultData(
        IReadOnlyList<FemMeshNode> meshNodes,
        IReadOnlyList<FemElement> meshElements,
        IReadOnlyList<FemMember> sourceMembers)
    {
        foreach (var element in meshElements)
            if (int.TryParse(element.ElemTag, out int elementTag))
            {
                int? fallback = sourceMembers.FirstOrDefault(m => m.ElemTag == element.SourceMemberTag)?.CrossSectionId;
                if (_sectionIdByElement.TryGetValue(elementTag, out var current) && current is null)
                    _sectionIdByElement[elementTag] = fallback;
            }
        if (!IsNonlinear || _nonlinearResult == null || string.IsNullOrWhiteSpace(ArtifactDirectory)) return;
        if (string.IsNullOrWhiteSpace(_nonlinearResult.FiberStateFileName) ||
            string.IsNullOrWhiteSpace(_nonlinearResult.SectionOrderFileName)) return;
        try
        {
            var parser = new FemNonlinearFiberStateParser();
            _fiberStatePath = Path.Combine(ArtifactDirectory, _nonlinearResult.FiberStateFileName);
            var recordedLocations = parser.ParseLocations(Path.Combine(ArtifactDirectory, _nonlinearResult.SectionOrderFileName));
            var available = File.Exists(_fiberStatePath) && new FileInfo(_fiberStatePath).Length > 0
                ? recordedLocations.Select(x => (x.ElementTag, x.IntegrationPoint)).ToHashSet()
                : [];
            SectionLocations = new FemSectionLocationResolver().Resolve(
                meshNodes, meshElements, sourceMembers, recordedLocations, available);
            OnPropertyChanged(nameof(SectionLocations));
            OnPropertyChanged(nameof(HasSectionResults));
            _selectedSectionLocation = SectionLocations.FirstOrDefault(x => x.IsStateAvailable);
            LoadSelectedFiberStates();
            OnPropertyChanged(nameof(SelectedSectionLocation));
            OnPropertyChanged(nameof(SelectedSectionPositionLabel));
        }
        catch (OpenSeesResultException)
        {
            // Дополнительные файлы не должны скрывать остальные результаты FEM.
            _fiberStates = [];
            _fiberStatePath = null;
            SectionLocations = [];
        }
    }

    void LoadSelectedFiberStates()
    {
        _fiberStates = [];
        if (_selectedSectionLocation is not { IsStateAvailable: true } selected ||
            string.IsNullOrWhiteSpace(_fiberStatePath)) return;
        try
        {
            _fiberStates = new FemNonlinearFiberStateParser().ParseSection(
                _fiberStatePath, selected.MeshElementTag, selected.IntegrationPoint);
        }
        catch (OpenSeesResultException)
        {
            _fiberStates = [];
        }
    }

    void RebuildSectionPlots()
    {
        SectionStressPlot = null;
        SectionStrainPlot = null;
        var selected = _selectedSectionLocation;
        if (selected == null || !selected.IsStateAvailable || _fiberStates.Count == 0 || Steps.Count == 0)
        {
            OnPropertyChanged(nameof(SectionStressPlot));
            OnPropertyChanged(nameof(SectionStrainPlot));
            OnPropertyChanged(nameof(HasSelectedSectionState));
            return;
        }
        if (!_sectionIdByElement.TryGetValue(selected.MeshElementTag, out int? sectionId) || sectionId is not int id)
        {
            NotifySectionPlotsChanged();
            return;
        }
        var section = _database.CrossSections.FirstOrDefault(s => s.Id == id);
        if (section == null)
        {
            NotifySectionPlotsChanged();
            return;
        }

        int step = Steps[SelectedStepIndex].StepIndex;
        var recorded = _fiberStates
            .Where(s => s.StepIndex == step &&
                        s.ElementTag == selected.MeshElementTag &&
                        s.IntegrationPoint == selected.IntegrationPoint)
            .ToDictionary(s => s.FiberIndex, s => (s.StressPa, s.Strain));
        if (recorded.Count == 0)
        {
            NotifySectionPlotsChanged();
            return;
        }
        var calcType = Enum.TryParse<CalcType>(_nonlinearResult?.CalcTypeName, out var parsedCalcType)
            ? parsedCalcType : CalcType.C;
        var curvature = new Kurvature();
        SectionStressPlot = new SectionPlotVM(section, curvature, calcType, SectionPlotMode.Stress,
            recordedFibers: recorded);
        SectionStrainPlot = new SectionPlotVM(section, curvature, calcType, SectionPlotMode.Strain,
            recordedFibers: recorded);
        NotifySectionPlotsChanged();
    }

    void NotifySectionPlotsChanged()
    {
        OnPropertyChanged(nameof(SectionStressPlot));
        OnPropertyChanged(nameof(SectionStrainPlot));
        OnPropertyChanged(nameof(HasSelectedSectionState));
    }

    static (Vector3D Ey, Vector3D Ez) LocalFrame(Point3D pi, Point3D pj, double rotationDeg)
    {
        var frame = FemLocalAxis.LocalFrame(
            new FemLinearNode(0, pi.X, pi.Y, pi.Z, new bool[6]),
            new FemLinearNode(0, pj.X, pj.Y, pj.Z, new bool[6]),
            rotationDeg);
        return (new Vector3D(frame.Y.X, frame.Y.Y, frame.Y.Z),
                new Vector3D(frame.Z.X, frame.Z.Y, frame.Z.Z));
    }

    /// <summary>Значения выбранной компоненты на концах i и j (внутреннее усилие, непрерывное по узлу).</summary>
    (double Vi, double Vj) ComponentValues(FemElementEndForces f) => _selectedForceComponent switch
    {
        FemForceComponent.N  => (-f.Ni,  f.Nj),
        FemForceComponent.Qy => (-f.Qyi, f.Qyj),
        FemForceComponent.Qz => (-f.Qzi, f.Qzj),
        FemForceComponent.Mx => (-f.Mxi, f.Mxj),
        FemForceComponent.My => (f.Myi, -f.Myj),
        FemForceComponent.Mz => (f.Mzi, -f.Mzj),
        _ => (0, 0)
    };

    Vector3D OffsetAxis(ElementGeom g) => _selectedForceComponent switch
    {
        FemForceComponent.Qy or FemForceComponent.Mz => g.Ey,
        _ => g.Ez
    };

    void RebuildForceDiagram()
    {
        var segs = new List<(Point3D, Point3D, Vector3D, Vector3D)>();
        double maxV = double.NegativeInfinity, minV = double.PositiveInfinity;
        Point3D maxPos = default, minPos = default;
        foreach (var g in _elementGeoms)
        {
            if (!_forcesByElem.TryGetValue(g.Tag, out var f)) continue;
            var (vi, vj) = ComponentValues(f);
            var axis = OffsetAxis(g);
            var offI = axis * (vi * _forceScale);
            var offJ = axis * (vj * _forceScale);
            segs.Add((g.Pi, g.Pj, offI, offJ));

            if (vi > maxV) { maxV = vi; maxPos = g.Pi + offI; }
            if (vj > maxV) { maxV = vj; maxPos = g.Pj + offJ; }
            if (vi < minV) { minV = vi; minPos = g.Pi + offI; }
            if (vj < minV) { minV = vj; minPos = g.Pj + offJ; }
        }
        ForceDiagramMesh = FemForceDiagramFactory.BuildRibbons(segs);
        OnPropertyChanged(nameof(ForceDiagramMesh));

        bool hasData = segs.Count > 0 && double.IsFinite(maxV) && double.IsFinite(minV);
        bool isForce = _selectedForceComponent is FemForceComponent.N or FemForceComponent.Qy or FemForceComponent.Qz;
        string unit = isForce ? Loc.S("UnitKN") : Loc.S("UnitKNm");
        ForceMaxLabelPosition = hasData ? maxPos : null;
        ForceMaxLabelText = hasData ? $"{maxV / 1000:G4} {unit}" : null;
        ForceMinLabelPosition = hasData ? minPos : null;
        ForceMinLabelText = hasData ? $"{minV / 1000:G4} {unit}" : null;
        OnPropertyChanged(nameof(ForceMaxLabelPosition));
        OnPropertyChanged(nameof(ForceMaxLabelText));
        OnPropertyChanged(nameof(ForceMinLabelPosition));
        OnPropertyChanged(nameof(ForceMinLabelText));
    }

    /// <summary>Масштаб эпюры: макс. |усилие| по всем компонентам → ≈10% габарита.</summary>
    double SuggestForceScale()
    {
        if (_elementGeoms.Count == 0 || _forcesByElem.Count == 0) return 1.0;
        double maxVal = 0;
        foreach (var f in _forcesByElem.Values)
            foreach (var v in new[] { f.Ni, f.Qyi, f.Qzi, f.Mxi, f.Myi, f.Mzi, f.Nj, f.Qyj, f.Qzj, f.Mxj, f.Myj, f.Mzj })
                maxVal = System.Math.Max(maxVal, System.Math.Abs(v));
        if (maxVal <= 1e-12) return 1.0;

        var xs = _originalByTag.Values;
        double dx = xs.Max(p => p.X) - xs.Min(p => p.X);
        double dy = xs.Max(p => p.Y) - xs.Min(p => p.Y);
        double dz = xs.Max(p => p.Z) - xs.Min(p => p.Z);
        double diag = System.Math.Sqrt(dx * dx + dy * dy + dz * dz);
        if (diag <= 1e-9) return 1.0;
        double val = 0.1 * diag / maxVal;
        double rounded = System.Math.Round(val, 2);
        return rounded > 0 ? rounded : val;
    }

    void RebuildDeformed()
    {
        var pts = new Point3DCollection();
        foreach (var (i, j) in _elementPairs)
        {
            pts.Add(Deformed(i));
            pts.Add(Deformed(j));
        }
        DeformedLines = pts;
        OnPropertyChanged(nameof(DeformedLines));

        var nodes = new Point3DCollection();
        var nodesByTag = new Dictionary<int, Point3D>();
        foreach (var kvp in _originalByTag)
        {
            var d = Deformed(kvp.Key);
            nodes.Add(d);
            nodesByTag[kvp.Key] = d;
        }
        DeformedNodes = nodes;
        DeformedNodesByTag = nodesByTag;
        OnPropertyChanged(nameof(DeformedNodes));
        OnPropertyChanged(nameof(DeformedNodesByTag));

        DeformedElementSegments = _elementGeoms
            .Select(eg => (eg.Tag, Deformed(eg.Ni), Deformed(eg.Nj)))
            .ToList();
        OnPropertyChanged(nameof(DeformedElementSegments));
    }

    Point3D Deformed(int tag)
    {
        var p = _originalByTag[tag];
        if (_dispByTag.TryGetValue(tag, out var d))
            return p + d * _deformScale;
        return p;
    }

    /// <summary>Подбирает масштаб так, чтобы макс. перемещение было ≈5% габарита схемы.</summary>
    double SuggestScale()
    {
        if (_originalByTag.Count == 0 || _dispByTag.Count == 0) return 1.0;
        double maxDisp = _dispByTag.Values.Select(v => v.Length).DefaultIfEmpty(0).Max();
        if (maxDisp <= 1e-12) return 1.0;

        var xs = _originalByTag.Values;
        double dx = xs.Max(p => p.X) - xs.Min(p => p.X);
        double dy = xs.Max(p => p.Y) - xs.Min(p => p.Y);
        double dz = xs.Max(p => p.Z) - xs.Min(p => p.Z);
        double diag = System.Math.Sqrt(dx * dx + dy * dy + dz * dz);
        if (diag <= 1e-9) return 1.0;
        double val = 0.05 * diag / maxDisp;
        double rounded = System.Math.Round(val, 2);
        return rounded > 0 ? rounded : val;
    }

    static FemLinearResult? ParseResult(string dataJson)
    {
        try
        {
            using var doc = JsonDocument.Parse(dataJson);
            if (!doc.RootElement.TryGetProperty("Displacements", out _)) return null;
            return JsonSerializer.Deserialize<FemLinearResult>(dataJson);
        }
        catch (JsonException) { return null; }
    }

    static FemNonlinearResult? ParseNonlinearResult(string dataJson)
    {
        try
        {
            using var doc = JsonDocument.Parse(dataJson);
            if (!doc.RootElement.TryGetProperty("Steps", out _)) return null;
            return JsonSerializer.Deserialize<FemNonlinearResult>(dataJson);
        }
        catch (JsonException) { return null; }
    }

    static IReadOnlyList<string> CollectDiagnostics(string dataJson, IReadOnlyList<string> parsedDiagnostics)
    {
        var list = new List<string>();
        if (parsedDiagnostics.Count > 0) list.AddRange(parsedDiagnostics);
        try
        {
            using var doc = JsonDocument.Parse(dataJson);
            if (doc.RootElement.TryGetProperty("errors", out var errors) && errors.ValueKind == JsonValueKind.Array)
                foreach (var e in errors.EnumerateArray())
                    if (e.GetString() is { } s) list.Add(s);
            else if (doc.RootElement.TryGetProperty("error", out var err) && err.GetString() is { } m && list.Count == 0)
                list.Add(m);
        }
        catch (JsonException) { }
        return list;
    }
}

/// <summary>Компонента усилия стержня для отображения эпюры.</summary>
public enum FemForceComponent { N, Qy, Qz, Mx, My, Mz }
