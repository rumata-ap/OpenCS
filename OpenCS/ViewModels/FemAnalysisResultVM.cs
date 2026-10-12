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

    /// <summary>Строки таблицы перемещений в выбранных единицах и режиме фильтра.</summary>
    public IReadOnlyList<FemNodeDisplacementRow> DisplayedDisplacements { get; private set; } = [];

    FemDisplacementDisplayMode _displacementDisplayMode = FemDisplacementDisplayMode.ExtremesOnly;
    /// <summary>Режим отображения всех узлов или только узловых экстремумов по стержням.</summary>
    public FemDisplacementDisplayMode DisplacementDisplayMode
    {
        get => _displacementDisplayMode;
        set
        {
            if (value == _displacementDisplayMode) return;
            _displacementDisplayMode = value;
            RebuildDisplayedDisplacements();
            OnPropertyChanged();
        }
    }

    FemLengthUnit _displacementLengthUnit = FemLengthUnit.Millimeters;
    /// <summary>Единица отображения линейных перемещений.</summary>
    public FemLengthUnit DisplacementLengthUnit
    {
        get => _displacementLengthUnit;
        set
        {
            if (value == _displacementLengthUnit) return;
            _displacementLengthUnit = value;
            RebuildDisplayedDisplacements();
            OnPropertyChanged();
        }
    }

    FemRotationScale _rotationDisplayScale = FemRotationScale.One;
    /// <summary>Коэффициент отображения углов поворота в радианах.</summary>
    public FemRotationScale RotationDisplayScale
    {
        get => _rotationDisplayScale;
        set
        {
            if (value == _rotationDisplayScale) return;
            _rotationDisplayScale = value;
            RebuildDisplayedDisplacements();
            OnPropertyChanged();
        }
    }

    /// <summary>Точка графика «коэффициент нагрузки по шагам» для результатной вкладки.</summary>
    public sealed record FemLoadFactorPoint(int Step, double LoadFactor, bool Converged);

    /// <summary>True, если результат — нелинейный расчёт (несколько шагов, доступен слайдер шага).</summary>
    public bool IsNonlinear { get; }
    /// <summary>Полная история шагов; для линейного результата — один синтетический шаг.</summary>
    public IReadOnlyList<FemNonlinearStepResult> Steps { get; }
    /// <summary>Точки для графика load-factor по шагам.</summary>
    public IReadOnlyList<FemLoadFactorPoint> LoadFactorPoints { get; }

    /// <summary>Точки графика λ–перемещение контрольного/мониторингового узла — той же
    /// длины и в том же порядке, что Steps (см. ControlDisplacementPointsBuilder).</summary>
    public IReadOnlyList<(double X, double Y, bool Converged, int SegmentId)> ControlDisplacementPoints { get; }
    public bool HasControlDisplacementChart => ControlDisplacementPoints.Any(p => double.IsFinite(p.X));

    /// <summary>Локализованная причина последнего несошедшегося шага; пусто, если расчёт
    /// сошёлся полностью или остановился штатно (тогда причины из step_status.out нет).</summary>
    public string StopReasonText { get; }
    public bool HasStopReason => !string.IsNullOrEmpty(StopReasonText);

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
            OnPropertyChanged();
            OnPropertyChanged(nameof(SelectedSectionPositionLabel));
            OnPropertyChanged(nameof(CanRequestSectionState));
        }
    }

    /// <summary>Можно ли запросить окно состояния для выбранной ТИ.</summary>
    public bool CanRequestSectionState => SelectedSectionLocation?.IsStateAvailable == true;

    /// <summary>Положение выбранного сечения в метрах и долях длины стержня.</summary>
    public string SelectedSectionPositionLabel => _selectedSectionLocation == null
        ? ""
        : string.Format(Loc.S("FemResultSectionPositionValue"),
            _selectedSectionLocation.PositionFromMemberStartM,
            _selectedSectionLocation.MemberLengthM,
            _selectedSectionLocation.RelativePosition);

    /// <summary>Событие запроса окна состояния сечения (обрабатывает AppViewModel).</summary>
    public event Action<FemSectionStateRequest>? SectionStateRequested;

    /// <summary>Событие уведомления о недоступном состоянии выбранной ТИ (ключ строки
    /// локализации) — рейзится, когда окно состояния открыть невозможно.</summary>
    public event Action<string>? SectionStateUnavailable;

    /// <summary>Событие запроса создания набора усилий выбранного конструктивного стержня.</summary>
    public event Action<string>? CreateMemberForceSetRequested;

    /// <summary>Проверяет готовность текущего шага для команды создания набора.</summary>
    public bool CanCreateMemberForceSet(string memberTag)
    {
        if (string.IsNullOrWhiteSpace(memberTag) || Steps.Count == 0)
            return false;
        return Steps[SelectedStepIndex].Converged;
    }

    /// <summary>Запрашивает создание набора усилий для конструктивного стержня.</summary>
    public void RequestCreateMemberForceSet(string memberTag)
    {
        if (CanCreateMemberForceSet(memberTag))
            CreateMemberForceSetRequested?.Invoke(memberTag);
    }

    /// <summary>Собирает входные данные builder-а для текущего выбранного шага.</summary>
    public FemMemberForceSetBuildInput BuildMemberForceSetInput(FemMember member)
    {
        ArgumentNullException.ThrowIfNull(member);
        var step = Steps[SelectedStepIndex];
        return new FemMemberForceSetBuildInput(
            _schema,
            member,
            _database.GetFemNodes(_schema.Id),
            _database.GetFemMeshNodes(_schema.Id),
            _database.GetFemMeshElements(_schema.Id),
            step.ElementForces,
            SelectedStepIndex,
            CurrentStepLabel,
            step.Converged);
    }

    /// <summary>Выбирает ТИ (например, кликом по 3D-маркеру) и сбрасывает выбор узла/элемента.</summary>
    public void SelectSectionLocation(FemSectionLocationRow? row)
    {
        _selectedNodeTag = null;
        _selectedElemTag = null;
        NotifySelectionChanged();
        SelectedSectionLocation = row;
    }

    /// <summary>Запрашивает окно состояния для ТИ; при недоступных данных рейзит
    /// <see cref="SectionStateUnavailable"/> вместо окна. Чтение волокон ленивое —
    /// выполняется фоновым потоком обработчиком события.</summary>
    public void RequestSectionState(FemSectionLocationRow? row)
    {
        if (row == null) return;
        if (!row.IsStateAvailable)
        {
            SectionStateUnavailable?.Invoke("FemSectionStateUnavailable");
            return;
        }
        if (_nonlinearResult == null || string.IsNullOrWhiteSpace(_fiberStatePath) || Steps.Count == 0)
        {
            SectionStateUnavailable?.Invoke("FemSectionStateUnavailable");
            return;
        }
        if (!_sectionIdByElement.TryGetValue(row.MeshElementTag, out int? sectionId) || sectionId is not int id)
        {
            SectionStateUnavailable?.Invoke("FemSectionStateUnavailable");
            return;
        }
        var step = Steps[SelectedStepIndex];
        SectionStateRequested?.Invoke(new FemSectionStateRequest(
            row,
            id,
            _nonlinearResult.CalcTypeName,
            () => LoadRecordedFibers(row, step.StepIndex),
            CurrentStepLabel,
            step.Converged,
            SelectedSectionPositionLabel));
    }

    IReadOnlyDictionary<int, (double StressPa, double Strain)> LoadRecordedFibers(
        FemSectionLocationRow row, int stepIndex)
    {
        try
        {
            var states = new FemNonlinearFiberStateParser().ParseSection(
                _fiberStatePath!, row.MeshElementTag, row.IntegrationPoint);
            return states
                .Where(s => s.StepIndex == stepIndex)
                .ToDictionary(s => s.FiberIndex, s => (s.StressPa, s.Strain));
        }
        catch (OpenSeesResultException)
        {
            return new Dictionary<int, (double StressPa, double Strain)>();
        }
    }
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
            var stageTags = _nonlinearResult?.StageTags ?? [];
            if (stageTags.Count > 1 && s.StageIndex >= 0 && s.StageIndex < stageTags.Count)
                return string.Format(Loc.S("FemResultStepLabelStaged"),
                    SelectedStepIndex + 1, Steps.Count, s.LoadFactor, convergedText, stageTags[s.StageIndex]);
            return string.Format(Loc.S("FemResultStepLabel"), SelectedStepIndex + 1, Steps.Count, s.LoadFactor, convergedText);
        }
    }

    void ApplyStepData()
    {
        var step = Steps[SelectedStepIndex];
        Displacements = step.Displacements;
        Reactions = step.Reactions;
        ElementForces = step.ElementForces;

        RebuildDisplayedDisplacements();

        _forcesByElem.Clear();
        foreach (var f in ElementForces) _forcesByElem[f.ElemTag] = f;
    }

    readonly Dictionary<int, Point3D> _originalByTag = [];

    /// <summary>Mesh-элемент стержня с локальным базисом для эпюр.</summary>
    readonly record struct ElementGeom(int Tag, string? SourceMemberTag, Vector3D Ey, Vector3D Ez);
    readonly List<ElementGeom> _elementGeoms = [];
    readonly Dictionary<int, FemElementEndForces> _forcesByElem = [];
    Dictionary<int, (Vector3D Ey, Vector3D Ez)>? _axesByElem;

    /// <summary>Концевые силы КЭ на выбранном шаге; null — нет в результате.</summary>
    public FemElementEndForces? ElementForce(int elemTag) => _forcesByElem.TryGetValue(elemTag, out var f) ? f : null;

    /// <summary>Оси эпюр КЭ (Ey, Ez местной системы OpenSees с углом поворота сечения); null — КЭ нет в сетке.</summary>
    public (Vector3D Ey, Vector3D Ez)? ElementAxes(int elemTag)
    {
        _axesByElem ??= _elementGeoms.GroupBy(g => g.Tag).ToDictionary(g => g.Key, g => (g.First().Ey, g.First().Ez));
        return _axesByElem.TryGetValue(elemTag, out var axes) ? axes : null;
    }

    readonly DatabaseService _database;
    readonly FemSchema _schema;
    readonly Dictionary<int, int?> _sectionIdByElement = [];
    /// <summary>Узлы mesh-элементов, сгруппированные по конструктивному стержню.</summary>
    public IReadOnlyDictionary<string, IReadOnlyList<int>> MemberNodes { get; private set; } =
        new Dictionary<string, IReadOnlyList<int>>(StringComparer.Ordinal);
    FemNonlinearResult? _nonlinearResult;
    string? _fiberStatePath;

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
        OnPropertyChanged(nameof(SelectedDisplayedDisplacementRow));
        OnPropertyChanged(nameof(SelectedReactionRow));
        OnPropertyChanged(nameof(SelectedForceRow));
    }

    /// <summary>Строка таблицы «Перемещения», соответствующая выбранному узлу (для подсветки грида).</summary>
    public FemNodeDisplacement? SelectedDisplacementRow =>
        _selectedNodeTag is int tag ? Displacements.FirstOrDefault(d => d.NodeTag == tag) : null;
    /// <summary>Строка отображаемой таблицы, соответствующая выбранному узлу.</summary>
    public FemNodeDisplacementRow? SelectedDisplayedDisplacementRow =>
        _selectedNodeTag is int tag
            ? DisplayedDisplacements.FirstOrDefault(row => row.NodeTag == tag)
            : null;
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

    public event Action<string>? ShowMemberForceRequested;
    /// <summary>Событие запроса read-only просмотра поперечного сечения стержня.</summary>
    public event Action<string>? ShowMemberSectionRequested;
    public event Action<string>? ShowNodeValuesRequested;

    public void RequestShowMemberForce(string tag) => ShowMemberForceRequested?.Invoke(tag);
    /// <summary>Запрашивает read-only просмотр поперечного сечения стержня.</summary>
    public void RequestShowMemberSection(string tag) => ShowMemberSectionRequested?.Invoke(tag);
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

    /// <summary>Схема результата.</summary>
    public FemSchema Schema => _schema;

    public FemAnalysisResultVM(CalcResult result, DatabaseService db, FemSchema schema)
    {
        _database = db;
        _schema = schema;
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
        ControlDisplacementPoints = ControlDisplacementPointsBuilder.Build(
            Steps, _nonlinearResult?.StagePathControls ?? [], _nonlinearResult?.PathControlSwitches ?? [])
            .Select(p => (p.X, p.Y, p.Converged, p.SegmentId)).ToList();
        StopReasonText = BuildStopReasonText(Steps);

        // Геометрия из mesh-снимка схемы
        foreach (var n in db.GetFemMeshNodes(schema.Id))
            if (int.TryParse(n.NodeTag, out int tag))
                _originalByTag[tag] = new Point3D(n.X, n.Y, n.Z);

        var meshNodes = db.GetFemMeshNodes(schema.Id);
        var meshElements = db.GetFemMeshElements(schema.Id);
        var sourceMembers = db.GetFemMembers(schema.Id);
        MemberNodes = BuildMemberNodes(meshElements);
        OnPropertyChanged(nameof(MemberNodes));
        foreach (var e in meshElements)
        {
            var ends = JsonSerializer.Deserialize<int[]>(e.NodeIdsJson) ?? [];
            if (ends.Length != 2 || !_originalByTag.ContainsKey(ends[0]) || !_originalByTag.ContainsKey(ends[1]))
                continue;
            if (int.TryParse(e.ElemTag, out int etag))
            {
                _sectionIdByElement[etag] = e.CrossSectionId;
                var pi = _originalByTag[ends[0]];
                var pj = _originalByTag[ends[1]];
                double rotationDeg = sourceMembers.FirstOrDefault(m => m.ElemTag == e.SourceMemberTag)?.RotationDeg ?? 0;
                var (ey, ez) = LocalFrame(pi, pj, rotationDeg);
                _elementGeoms.Add(new ElementGeom(etag, e.SourceMemberTag, ey, ez));
            }
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
    }

    static IReadOnlyDictionary<string, IReadOnlyList<int>> BuildMemberNodes(
        IReadOnlyList<FemElement> meshElements)
    {
        var byMember = new Dictionary<string, HashSet<int>>(StringComparer.Ordinal);
        foreach (FemElement element in meshElements)
        {
            if (string.IsNullOrWhiteSpace(element.SourceMemberTag)) continue;
            var ends = JsonSerializer.Deserialize<int[]>(element.NodeIdsJson) ?? [];
            if (ends.Length != 2) continue;
            if (!byMember.TryGetValue(element.SourceMemberTag, out HashSet<int>? nodeTags))
                byMember[element.SourceMemberTag] = nodeTags = [];
            nodeTags.Add(ends[0]);
            nodeTags.Add(ends[1]);
        }

        return byMember.ToDictionary(
            item => item.Key,
            item => (IReadOnlyList<int>)item.Value.OrderBy(tag => tag).ToArray(),
            StringComparer.Ordinal);
    }

    void RebuildDisplayedDisplacements()
    {
        DisplayedDisplacements = FemDisplacementTableBuilder.Build(
            Displacements,
            MemberNodes,
            DisplacementDisplayMode,
            DisplacementLengthUnit,
            RotationDisplayScale);
        OnPropertyChanged(nameof(DisplayedDisplacements));
        OnPropertyChanged(nameof(SelectedDisplayedDisplacementRow));
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
            OnPropertyChanged(nameof(SelectedSectionLocation));
            OnPropertyChanged(nameof(SelectedSectionPositionLabel));
        }
        catch (OpenSeesResultException)
        {
            // Дополнительные файлы не должны скрывать остальные результаты FEM.
            _fiberStatePath = null;
            SectionLocations = [];
        }
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

    static string BuildStopReasonText(IReadOnlyList<FemNonlinearStepResult> steps)
    {
        var lastFailed = steps.LastOrDefault(s => !s.Converged);
        if (lastFailed?.StopReason is not { } reason) return "";
        return reason switch
        {
            "no_convergence" => Loc.S("FemResultStopReasonNoConvergence"),
            "min_increment_reached" => Loc.S("FemResultStopReasonMinIncrementReached"),
            "min_arclength_reached" => Loc.S("FemResultStopReasonMinArcLengthReached"),
            _ => string.Format(Loc.S("FemResultStopReasonUnknown"), reason)
        };
    }
}

/// <summary>Компонента усилия стержня для отображения эпюры.</summary>
public enum FemForceComponent { N, Qy, Qz, Mx, My, Mz }
