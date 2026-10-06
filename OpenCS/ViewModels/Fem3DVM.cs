using System.Text.Json;
using System.Windows.Media;
using System.Windows.Media.Media3D;
using CScore;
using CScore.Fem;
using CScore.Fem.Combinations;
using CScore.Planar;
using OpenCS.Utilites;

namespace OpenCS.ViewModels;

/// <summary>Группа стержней одного цвета для 3D-отображения.</summary>
public record BarGroup(string Label, Color Color, Point3DCollection Points, double Thickness = 1.5);

/// <summary>Источник, по которому в 3D показываются узловые нагрузки.</summary>
public sealed record FemDiagramLoadSource(string Label, FemLoadCase? LoadCase, FemLoadDefinition? Definition);

/// <summary>Один плоский конструктивный элемент (плита/стена) для 3D-отображения — заливка,
/// видимый контур (Hull+Holes) и тег для pick/выделения/контекстного меню.</summary>
/// <param name="IsMeshLocked">Элемент из кБ ЛИРЫ поверх её сетки: рисуется утолщённым контуром без заливки.</param>
public sealed record PlanarRegionVisual(string ElemTag, MeshGeometry3D Mesh, Point3DCollection EdgePoints, bool IsMeshLocked = false);

/// <summary>Пластины мозаики одного цвета.</summary>
public sealed record MosaicShellMesh(Color Color, MeshGeometry3D Mesh);

/// <summary>ViewModel 3D-вида расчётной схемы или конструктивного элемента МКЭ.</summary>
public class Fem3DVM : ViewModelBase
{
    static readonly Color[] _palette =
    [
        Colors.SteelBlue, Colors.OrangeRed, Colors.ForestGreen, Colors.DarkOrange,
        Colors.MediumPurple, Colors.Crimson, Colors.Teal, Colors.Goldenrod,
        Colors.SlateBlue, Colors.DarkCyan,
    ];

    readonly int             _schemaId;
    readonly DatabaseService _db;
    readonly FemMemberGroup? _memberOnly;      // показывать только КЭ группы
    readonly FemMemberGroup? _highlightMember; // показывать всю схему, группа подсвечена
    long _meshOverlayRequestVersion;
    IReadOnlyList<FemNode> _diagramNodes = [];
    IReadOnlyList<FemMember> _diagramMembers = [];
    IReadOnlyList<FemLoadCase> _diagramLoadCases = [];
    IReadOnlyList<FemNodeLoad> _diagramNodeLoads = [];
    IReadOnlyList<FemMemberLoad> _diagramMemberLoads = [];
    IReadOnlyList<FemKinematicLoad> _diagramKinematicLoads = [];

    bool   _isLoading = true;
    bool   _noData;
    string _status    = "";

    public bool   IsLoading { get => _isLoading; private set { _isLoading = value; OnPropertyChanged(); } }
    public bool   NoData    { get => _noData;    private set { _noData    = value; OnPropertyChanged(); } }
    public string Status    { get => _status;    private set { _status    = value; OnPropertyChanged(); } }

    public List<BarGroup>      BarGroups       { get; private set; } = [];
    public MeshGeometry3D?     ShellMesh       { get; private set; }
    public MeshGeometry3D?     HiShellMesh     { get; private set; }
    /// <summary>Мозаика пластин: пластины по цветам полос шкалы (пусто — мозаика пластин выключена).
    /// КЭ без данных остаются в <see cref="ShellMesh"/>. Мозаика стержней идёт через <see cref="BarGroups"/>.</summary>
    public IReadOnlyList<MosaicShellMesh> MosaicShellMeshes { get; private set; } = [];
    /// <summary>Стержни, раскрашенные мозаикой: тег КЭ и концы — для поиска стержня под курсором
    /// (пусто — мозаика стержней выключена).</summary>
    public IReadOnlyList<(string Tag, Point3D P1, Point3D P2)> MosaicBarSegments { get; private set; } = [];
    /// <summary>Настройки и легенда мозаики по КЭ: армирование, импортированные усилия, коэффициент использования.</summary>
    public PlateRebarMosaicVM Mosaic { get; } = new();

    public IReadOnlyList<PlanarRegionVisual> PlanarRegionVisuals { get; private set; } = [];
    public Point3DCollection?  ShellEdgePoints { get; private set; }

    bool _showShellEdges;
    Dictionary<string, Point3D>? _edgeNodeMap;
    List<FemMember>? _edgeElements;

    /// <summary>
    /// Рёбра пластин нужны (кнопка «Сетка»). Строятся по требованию: на сетке в сотню тысяч КЭ это ~0,4 с
    /// и ~460 тыс. точек, а линия такого размера пересчитывается при каждом движении камеры (~50 мс).
    /// </summary>
    public bool ShowShellEdges
    {
        get => _showShellEdges;
        set
        {
            if (_showShellEdges == value) return;
            _showShellEdges = value;
            if (value && ShellEdgePoints == null && _edgeNodeMap != null && _edgeElements != null)
            {
                ShellEdgePoints = BuildShellEdges(_edgeNodeMap, _edgeElements);
                OnPropertyChanged(nameof(ShellEdgePoints));
            }
        }
    }
    public Point3DCollection?  NodePoints      { get; private set; }
    public Point3DCollection?  MeshLinePoints  { get; private set; }
    public Point3DCollection?  MeshNodePoints  { get; private set; }
    /// <summary>Рёбра настоящей Gmsh-сетки (срез 2 дорожной карты Gmsh) регионов с актуальным
    /// расчётным PlanarMeshSnapshot — отдельно от MeshLinePoints (та — дискретизация стержней
    /// из fem_mesh_* таблиц, источник другой). Управляется тем же showGridCheck, что и остальные
    /// слои расчётной сетки.</summary>
    public Point3DCollection?  PlanarRegionMeshEdgePoints { get; private set; }
    public Point3DCollection?  PlanarRegionMeshNodePoints { get; private set; }
    public IReadOnlyList<FemDiagramGlyph> DiagramGlyphs { get; private set; } = [];
    public IReadOnlyList<FemMemberLoadGlyph> MemberLoadGlyphs { get; private set; } = [];
    public IReadOnlyList<FemSectionGlyph> SectionGlyphs { get; private set; } = [];
    public List<FemDiagramLoadSource> DiagramLoadSources { get; private set; } = [];
    public IReadOnlyDictionary<int, Point3D> DiagramNodePositions { get; private set; } = new Dictionary<int, Point3D>();

    bool _showSupportGlyphs = true;
    public bool ShowSupportGlyphs
    {
        get => _showSupportGlyphs;
        set { if (_showSupportGlyphs == value) return; _showSupportGlyphs = value; RefreshDiagramGlyphs(); OnPropertyChanged(); }
    }

    bool _showLoadGlyphs = true;
    public bool ShowLoadGlyphs
    {
        get => _showLoadGlyphs;
        set { if (_showLoadGlyphs == value) return; _showLoadGlyphs = value; RefreshDiagramGlyphs(); OnPropertyChanged(); }
    }

    bool _showSectionGlyphs = true;
    /// <summary>Показывать контуры поперечных сечений и локальные оси стержней.</summary>
    public bool ShowSectionGlyphs
    {
        get => _showSectionGlyphs;
        set { if (_showSectionGlyphs == value) return; _showSectionGlyphs = value; OnPropertyChanged(); }
    }

    bool _showLoadValues;
    /// <summary>Показывать текстовые подписи значений нагрузок рядом со стрелками (узловых и на стержнях).</summary>
    public bool ShowLoadValues
    {
        get => _showLoadValues;
        set { if (_showLoadValues == value) return; _showLoadValues = value; OnPropertyChanged(); }
    }

    FemDiagramLoadSource? _selectedDiagramLoadSource;
    public FemDiagramLoadSource? SelectedDiagramLoadSource
    {
        get => _selectedDiagramLoadSource;
        set { _selectedDiagramLoadSource = value; RefreshDiagramGlyphs(); OnPropertyChanged(); }
    }

    public FemSchemaSelectionVM? Selection { get; set; }
    public bool EditMode { get; set; }

    /// <summary>Точка, отмеченная на виде (узел, выбранный в дереве схемы); null — без метки.</summary>
    public Point3D? MarkerPoint { get; init; }

    /// <summary>
    /// Сессия редактирования схемы (страница схемы): вид строится по ней, а не по БД. <see cref="LoadAsync"/>
    /// читает сетку-подложку в фоне и строит вид один раз (раньше страница строила его синхронно, а
    /// LoadAsync — ещё раз по БД: на сетке в 120 тыс. КЭ это 2,3 с замирания и ещё ~5 с). Схема без
    /// конструктивного слоя строится по сетке, как в режиме просмотра.
    /// </summary>
    public Func<CScore.Fem.Editing.FemSchemaEditSession>? SessionSource { get; init; }

    /// <summary>Тег узла и его позиция — для построения кликабельных прокси в режиме редактирования.</summary>
    public List<(string Tag, Point3D Position)> NodeProxies { get; private set; } = [];
    /// <summary>Тег элемента и его концы — для построения кликабельных прокси в режиме редактирования.</summary>
    public List<(string Tag, Point3D P1, Point3D P2)> BarProxies { get; private set; } = [];

    public static Color ShellColor   => Color.FromArgb(180, 160, 190, 210);
    public static Color ShellBgColor => Color.FromArgb(100, 180, 180, 190);
    public static Color ShellHiColor => Color.FromArgb(210, 255, 100,  50);
    public static Color PlanarRegionMeshColor => Color.FromArgb(150, 100, 149, 237);
    /// <summary>Цвет элементов из кБ ЛИРЫ, нарисованных поверх её сетки.</summary>
    public static Color LockedMemberColor => Colors.DarkOrange;

    /// <summary>Режим схемы — показывает все КЭ, раскрашенные по жёсткости.</summary>
    public Fem3DVM(FemSchema schema, DatabaseService db)
    {
        _schemaId = schema.Id;
        _db       = db;
        Status    = Loc.S("Fem3DLoading");
        Mosaic.Changed += (_, _) => RefreshMosaic();
        Mosaic.Reader = () => PlateRebarMosaicVM.ReadAll(_db, _schemaId);
    }

    /// <summary>Режим конструктивного элемента — показывает только КЭ этой группы.</summary>
    public Fem3DVM(FemMemberGroup member, DatabaseService db)
    {
        _schemaId   = member.SchemaId;
        _db         = db;
        _memberOnly = member;
        // Мозаика считается по КЭ сетки: у группы КонЭ — по КЭ её элементов.
        Mosaic.ScopeTags = member.IsMeshGroup
            ? FemCheckScope.GroupTags(member)
            : db.GetFemCheckScope(member).Elements.Select(e => e.Element.ElemTag).ToHashSet(StringComparer.Ordinal);
        Status      = Loc.S("Fem3DLoading");
        Mosaic.Changed += (_, _) => RefreshMosaic();
        Mosaic.Reader = () => PlateRebarMosaicVM.ReadAll(_db, _schemaId);
    }

    /// <summary>Режим схемы с подсветкой — все КЭ схемы, КЭ группы выделены красным
    /// (при включённой мозаике пластин вместо подсветки — мозаика по всей схеме).</summary>
    public Fem3DVM(FemMemberGroup member, DatabaseService db, bool highlightOnSchema)
    {
        _schemaId        = member.SchemaId;
        _db              = db;
        _highlightMember = highlightOnSchema ? member : null;
        Status           = Loc.S("Fem3DLoading");
        Mosaic.Changed += (_, _) => RefreshMosaic();
        Mosaic.Reader = () => PlateRebarMosaicVM.ReadAll(_db, _schemaId);
    }

    public async Task LoadAsync()
    {
        IsLoading = true;
        NoData    = false;
        Status    = Loc.S("Fem3DLoading");

        // Схема без конструктивного слоя (импорт ЛИРЫ/SCAD) — обычным путём: сетка и есть схема, её КЭ
        // выбираются кликом. По сессии сетка импорта была бы лишь подложкой.
        if (SessionSource?.Invoke() is { } session && (session.Nodes.Count > 0 || session.Members.Count > 0))
        {
            await LoadSessionAsync(SessionSource);
            return;
        }

        try
        {
            var allNodes    = await Task.Run(() => _db.GetFemNodes(_schemaId));
            var allElements = await Task.Run(() => _db.GetFemMembers(_schemaId));

            // Схемы, импортированные из ЛИРА/SCAD, пишут топологию сразу в mesh-слой и не создают
            // конструктивный слой (FemNode/FemMember) — тогда источником геометрии для 3D-вида
            // (сплошная заливка пластин, полные рёбра, группировка стержней по сечению) служит
            // именно mesh-слой, а не одна лишь тонкая оверлейная сетка.
            bool constructiveEmpty = allNodes.Count == 0 && allElements.Count == 0;
            _meshIsSchema = constructiveEmpty;
            bool importedBackground = false;
            // Глифы нагрузок и опор строятся только по конструктивному слою: у подложки из сетки ЛИРЫ
            // свои Id (другая таблица), они пересекаются с Id элементов и узлов слоя.
            var ownNodes = allNodes;
            var ownElements = allElements;
            List<FemElement>? meshElements = null;
            if (constructiveEmpty)
            {
                var meshNodes    = await Task.Run(() => _db.GetFemMeshNodes(_schemaId));
                meshElements = await Task.Run(() => _db.GetFemMeshElements(_schemaId));
                allNodes    = meshNodes.Select(ToFemNode).ToList();
                allElements = meshElements.Select(ToFemMember).ToList();
                _meshSchemaImported = new HashSet<FemMember>(
                    allElements.Where((_, i) => meshElements[i].Origin == FemMember.MeshSourceImported),
                    ReferenceEqualityComparer.Instance);
            }
            else if (await Task.Run(LoadImportedBackground))
            {
                // Схема ЛИРЫ с элементами из кБ: сетка ЛИРЫ — подложка, поверх неё элементы
                // утолщённым контуром (см. ApplyTopology/BuildPlanarRegionVisuals).
                importedBackground = true;
                allNodes    = MergeNodes(_bgNodes!, allNodes);
                allElements = [.. _bgElements!, .. allElements];
            }

            Mosaic.Apply((await Task.Run(() => PlateRebarMosaicVM.Read(_db, _schemaId, meshElements ?? Interlocked.Exchange(ref _meshElementsForMosaic, null)))).WithProject(_db, _schemaId));
            ApplyTopology(allNodes, allElements);
            _diagramNodes = importedBackground ? ownNodes : allNodes;
            _diagramMembers = importedBackground ? ownElements : allElements;
            _diagramLoadCases = _db.GetFemLoadCases(_schemaId);
            _diagramNodeLoads = _db.GetFemNodeLoads(_schemaId);
            _diagramMemberLoads = _db.GetFemMemberLoads(_schemaId);
            _diagramKinematicLoads = _db.GetFemKinematicLoads(_schemaId);
            RefreshDiagramSources(_diagramLoadCases, []);
            if (_memberOnly == null && _highlightMember == null && !constructiveEmpty && !importedBackground)
                await LoadMeshOverlayAsync();
        }
        finally
        {
            IsLoading = false;
            Status    = "";
        }
    }

    async Task LoadSessionAsync(Func<CScore.Fem.Editing.FemSchemaEditSession> source)
    {
        try
        {
            bool importedBackground = await Task.Run(LoadImportedBackground);
            var session = source();
            ApplySession(session);
            var meshElements = Interlocked.Exchange(ref _meshElementsForMosaic, null);
            Mosaic.Apply((await Task.Run(() => PlateRebarMosaicVM.Read(_db, _schemaId, meshElements))).WithProject(_db, _schemaId));
            if (!importedBackground)
                await LoadMeshOverlayAsync();
        }
        finally
        {
            IsLoading = false;
            Status    = "";
        }
    }

    readonly object _bgLock = new();
    // Все КЭ сетки, прочитанные для подложки, — чтобы мозаика не читала их второй раз (один раз).
    List<FemElement>? _meshElementsForMosaic;
    List<FemNode>? _bgNodes;
    List<FemMember>? _bgElements;
    HashSet<FemMember> _bgSet = new(ReferenceEqualityComparer.Instance);
    HashSet<string> _bgOnlyNodeTags = new(StringComparer.Ordinal);

    /// <summary>Загружает (один раз) импортированную сетку схемы как подложку вида. False — сетки ЛИРЫ нет.</summary>
    bool LoadImportedBackground()
    {
        // Вызывается и из фонового потока (LoadSessionAsync), и из UI-потока (LoadFromSession после правок).
        lock (_bgLock)
        {
            if (_bgElements == null)
            {
                if (_db.HasImportedMesh(_schemaId))
                {
                    _bgNodes = _db.GetFemMeshNodes(_schemaId).Where(n => n.Origin == FemMember.MeshSourceImported).Select(ToFemNode).ToList();
                    var meshElements = _db.GetFemMeshElements(_schemaId);
                    _meshElementsForMosaic = meshElements;
                    _bgElements = meshElements.Where(e => e.Origin == FemMember.MeshSourceImported).Select(ToFemMember).ToList();
                }
                else
                {
                    _bgNodes = [];
                    _bgElements = [];
                }
                _bgSet = new HashSet<FemMember>(_bgElements, ReferenceEqualityComparer.Instance);
            }
            return _bgElements.Count > 0;
        }
    }

    /// <summary>Узлы подложки и конструктивного слоя без повторов тегов (узел слоя важнее: он редактируется).</summary>
    List<FemNode> MergeNodes(List<FemNode> background, List<FemNode> own)
    {
        var ownTags = own.Select(n => n.NodeTag).ToHashSet(StringComparer.Ordinal);
        _bgOnlyNodeTags = background.Select(n => n.NodeTag).Where(t => !ownTags.Contains(t)).ToHashSet(StringComparer.Ordinal);
        return [.. background.Where(n => !ownTags.Contains(n.NodeTag)), .. own];
    }

    static FemNode ToFemNode(FemMeshNode n) => new()
    {
        NodeTag = n.NodeTag, X = n.X, Y = n.Y, Z = n.Z,
    };

    static FemMember ToFemMember(FemElement e) => new()
    {
        Id = e.Id,
        ElemTag = e.ElemTag, ElemType = e.ElemType, NodeIdsJson = e.NodeIdsJson,
        SectionTag = e.SectionTag, MaterialTag = e.MaterialTag, ThicknessM = e.ThicknessM,
    };

    public async Task LoadMeshOverlayAsync()
    {
        var requestVersion = Interlocked.Increment(ref _meshOverlayRequestVersion);
        var snapshot = await Task.Run(() =>
        {
            var nodes = _db.GetFemMeshNodes(_schemaId);
            var elements = _db.GetFemMeshElements(_schemaId);
            return (nodes, elements);
        });

        var nodeMap = new Dictionary<int, Point3D>();
        var meshNodePoints = new Point3DCollection();
        foreach (var node in snapshot.nodes)
        {
            meshNodePoints.Add(new Point3D(node.X, node.Y, node.Z));
            if (!int.TryParse(node.NodeTag, out var nodeTag)) continue;
            nodeMap.TryAdd(nodeTag, new Point3D(node.X, node.Y, node.Z));
        }

        var linePoints = new Point3DCollection();

        foreach (var element in snapshot.elements)
        {
            int[] nodeIds;
            try
            {
                nodeIds = NodeIds(element.NodeIdsJson);
            }
            catch (JsonException)
            {
                continue;
            }

            if (nodeIds.Length < 2 ||
                !nodeMap.TryGetValue(nodeIds[0], out var first) ||
                !nodeMap.TryGetValue(nodeIds[1], out var second))
                continue;

            linePoints.Add(first);
            linePoints.Add(second);
        }

        if (requestVersion != Volatile.Read(ref _meshOverlayRequestVersion)) return;
        if (linePoints.Count > 0)
            linePoints.Freeze();
        if (meshNodePoints.Count > 0)
            meshNodePoints.Freeze();
        MeshLinePoints = linePoints.Count > 0 ? linePoints : null;
        MeshNodePoints = meshNodePoints.Count > 0 ? meshNodePoints : null;
        OnPropertyChanged(nameof(MeshLinePoints));
        OnPropertyChanged(nameof(MeshNodePoints));
    }

    /// <summary>Синхронно перестраивает геометрию из живой сессии редактирования (без БД).
    /// Используется в режиме редактирования, чтобы созданные/изменённые узлы и стержни
    /// сразу отражались в 3D без ожидания сохранения.</summary>
    public void LoadFromSession(CScore.Fem.Editing.FemSchemaEditSession session)
    {
        LoadImportedBackground();
        ApplySession(session);
        IsLoading = false;
        Status    = "";
    }

    void ApplySession(CScore.Fem.Editing.FemSchemaEditSession session)
    {
        if (_bgElements!.Count > 0)
            ApplyTopology(MergeNodes(_bgNodes!, session.Nodes), [.. _bgElements!, .. session.Members]);
        else
            ApplyTopology(session.Nodes, session.Members);
        _diagramNodes = session.Nodes;
        _diagramMembers = session.Members;
        _diagramLoadCases = session.LoadCases;
        _diagramNodeLoads = session.NodeLoads;
        _diagramMemberLoads = session.MemberLoads;
        _diagramKinematicLoads = session.KinematicLoads;
        DiagramNodePositions = session.Nodes.ToDictionary(node => node.Id, node => new Point3D(node.X, node.Y, node.Z));
        RefreshDiagramSources(session.LoadCases, session.LoadDefinitions);
        RefreshDiagramGlyphs();
    }

    void RefreshDiagramSources(
        IReadOnlyList<FemLoadCase> loadCases,
        IReadOnlyList<FemLoadDefinition> definitions)
    {
        var selectedLoadCaseId = SelectedDiagramLoadSource?.LoadCase?.Id;
        var selectedDefinitionId = SelectedDiagramLoadSource?.Definition?.Id;
        DiagramLoadSources = loadCases
            .Select(loadCase => new FemDiagramLoadSource(loadCase.Tag, loadCase, null))
            .Concat(definitions.Select(definition => new FemDiagramLoadSource(definition.Tag, null, definition)))
            .ToList();
        SelectedDiagramLoadSource = DiagramLoadSources.FirstOrDefault(source =>
            source.LoadCase?.Id == selectedLoadCaseId || source.Definition?.Id == selectedDefinitionId);
        OnPropertyChanged(nameof(DiagramLoadSources));
    }

    /// <summary>Выбирает исходное загружение как источник глифов в 3D-виде.</summary>
    public void SelectDiagramLoadCase(FemLoadCase loadCase)
    {
        ArgumentNullException.ThrowIfNull(loadCase);
        SelectedDiagramLoadSource = DiagramLoadSources.FirstOrDefault(source => source.LoadCase?.Id == loadCase.Id);
    }

    void RefreshDiagramGlyphs()
    {
        var resolved = SelectedDiagramLoadSource switch
        {
            { LoadCase: { } loadCase } => FemLoadExpressionResolver.Resolve(
                new FemLoadExpression { Mode = FemLoadExpressionMode.Single, LoadCaseIds = [loadCase.Id] },
                [loadCase], _diagramNodeLoads, _diagramMemberLoads, _diagramKinematicLoads),
            { Definition: { } definition } => FemLoadExpressionResolver.Resolve(
                definition.GetExpression(), _diagramLoadCases, _diagramNodeLoads, _diagramMemberLoads, _diagramKinematicLoads),
            _ => new FemResolvedLoads([], [], [])
        };
        var displayNodeLoads = resolved.NodeLoads
            .Where(load => load.Fx != 0 || load.Fy != 0 || load.Fz != 0 ||
                           load.Mx != 0 || load.My != 0 || load.Mz != 0)
            .Select(load => new FemResolvedNodeLoad(
                load.NodeId, load.Fx, load.Fy, load.Fz, load.Mx, load.My, load.Mz))
            .ToArray();
        var displayKinematicLoads = resolved.KinematicLoads.Where(load => load.Value != 0).ToArray();
        DiagramGlyphs = FemDiagramGlyphFactory.Create(
            _diagramNodes, displayNodeLoads, displayKinematicLoads, ShowSupportGlyphs, ShowLoadGlyphs);
        MemberLoadGlyphs = ShowLoadGlyphs
            ? FemMemberLoadGlyphFactory.Create(_diagramMembers, _diagramNodes, resolved.MemberLoads)
            : [];
        OnPropertyChanged(nameof(DiagramGlyphs));
        OnPropertyChanged(nameof(MemberLoadGlyphs));
    }

    /// <summary>Отображаемый элемент входит в группу: у группы КЭ — КЭ сетки (сетка схемы без конструктивного
    /// слоя или подложка) с номером из состава; у группы КонЭ — конструктивный элемент с тегом из состава.
    /// Номер КЭ сетки может совпасть с тегом конструктивного элемента, поэтому слой проверяется явно.</summary>
    bool InGroup(FemMember element, FemMemberGroup group, HashSet<string> tags) =>
        tags.Contains(element.ElemTag.Trim()) && group.IsMeshGroup == (_meshIsSchema || _bgSet.Contains(element));

    /// <summary>Конструктивного слоя нет — отображаемые элементы и есть КЭ сетки (импорт ЛИРЫ/SCAD).</summary>
    bool _meshIsSchema;

    /// <summary>Импортированные КЭ среди отображаемых, когда сетка и есть схема.</summary>
    HashSet<FemMember> _meshSchemaImported = new(ReferenceEqualityComparer.Instance);

    /// <summary>КЭ импортированной сетки — выбираются в 3D в режиме «КЭ сетки» (у КЭ дискретизации номера
    /// меняются при пересетке, в группы КЭ они не входят).</summary>
    bool IsPickableMesh(FemMember element) => _bgSet.Contains(element) || _meshSchemaImported.Contains(element);

    Dictionary<string, FemMember> _meshPickByTag = new(StringComparer.Ordinal);

    /// <summary>В схеме есть КЭ импортированной сетки, которые можно выбрать в 3D.</summary>
    public bool HasPickableMesh => _meshPickByTag.Count > 0;

    /// <summary>Сетка и есть схема (нет конструктивного слоя, импорт ЛИРЫ/SCAD).</summary>
    public bool MeshIsSchema => _meshIsSchema;

    /// <summary>Стержни импортированной сетки для выбора щелчком (по расстоянию до проекции на экране).</summary>
    public IReadOnlyList<(string Tag, Point3D P1, Point3D P2)> MeshPickBars { get; private set; } = [];

    void ApplyTopology(List<FemNode> allNodes, List<FemMember> allElements)
    {
        // В режиме члена — фильтруем только нужные КЭ
        List<FemMember> elements;
        if (_memberOnly != null)
        {
            var tags = FemCheckScope.GroupTags(_memberOnly);
            elements = allElements.Where(e => InGroup(e, _memberOnly, tags)).ToList();
        }
        else
        {
            elements = allElements;
        }

        // В режиме просмотра пустая схема — это нормальный повод показать «нет данных».
        // В режиме редактирования оверлей перекрывал бы клики по пустому 3D-виду, где как раз
        // и создаётся самый первый узел, поэтому там он никогда не показывается.
        if (!EditMode && (allNodes.Count == 0 || elements.Count == 0))
        {
            BarGroups        = [];
            ShellMesh        = null;
            HiShellMesh      = null;
            PlanarRegionVisuals = [];
            ShellEdgePoints  = null;
            _edgeElements    = null;
            NodePoints       = null;
            PlanarRegionMeshEdgePoints = null;
            PlanarRegionMeshNodePoints = null;
            NodeProxies      = [];
            BarProxies       = [];
            SectionGlyphs    = [];
            NoData           = true;
            OnPropertyChanged(nameof(BarGroups));
            OnPropertyChanged(nameof(ShellMesh));
            OnPropertyChanged(nameof(HiShellMesh));
            OnPropertyChanged(nameof(PlanarRegionVisuals));
            OnPropertyChanged(nameof(ShellEdgePoints));
            OnPropertyChanged(nameof(NodePoints));
            OnPropertyChanged(nameof(PlanarRegionMeshEdgePoints));
            OnPropertyChanged(nameof(PlanarRegionMeshNodePoints));
            OnPropertyChanged(nameof(SectionGlyphs));
            return;
        }
        NoData = false;

        if (EditMode && elements.Count == 0)
        {
            BarGroups        = [];
            ShellMesh        = null;
            HiShellMesh      = null;
            PlanarRegionVisuals = [];
            ShellEdgePoints  = null;
            _edgeElements    = null;
            NodePoints       = new Point3DCollection(allNodes.Select(n => new Point3D(n.X, n.Y, n.Z)));
            PlanarRegionMeshEdgePoints = null;
            PlanarRegionMeshNodePoints = null;
            NodeProxies      = allNodes.Select(n => (n.NodeTag, new Point3D(n.X, n.Y, n.Z))).ToList();
            BarProxies       = [];
            SectionGlyphs    = [];
            OnPropertyChanged(nameof(BarGroups));
            OnPropertyChanged(nameof(ShellMesh));
            OnPropertyChanged(nameof(HiShellMesh));
            OnPropertyChanged(nameof(PlanarRegionVisuals));
            OnPropertyChanged(nameof(ShellEdgePoints));
            OnPropertyChanged(nameof(NodePoints));
            OnPropertyChanged(nameof(PlanarRegionMeshEdgePoints));
            OnPropertyChanged(nameof(PlanarRegionMeshNodePoints));
            OnPropertyChanged(nameof(SectionGlyphs));
            return;
        }

        var nodeMap = allNodes.ToDictionary(n => n.NodeTag, n => new Point3D(n.X, n.Y, n.Z));

        if (_highlightMember != null)
        {
            var hiTags  = FemCheckScope.GroupTags(_highlightMember);
            var hiElems = elements.Where(e => InGroup(e, _highlightMember, hiTags)).ToList();
            var bgElems = elements.Where(e => !InGroup(e, _highlightMember, hiTags)).ToList();

            var hiBars = hiElems.Where(e => e.ElemType == "beam").ToList();
            var bgBars = bgElems.Where(e => e.ElemType == "beam").ToList();

            var bars = new List<BarGroup>();
            if (bgBars.Count > 0)
                bars.Add(new BarGroup("", Colors.LightGray, BuildLinePoints(nodeMap, bgBars), 0.8));
            if (hiBars.Count > 0)
                bars.Add(new BarGroup(_highlightMember.Tag, Colors.OrangeRed, BuildLinePoints(nodeMap, hiBars), 3.0));
            // Группа подсвечена, пока мозаика выключена; включённая мозаика красит всю схему.
            _baseBarGroups = bars;
            _highlightShells = hiElems;
            _highlightBackground = bgElems;
            _mosaicNodeMap = nodeMap;
            _mosaicShells  = elements.Where(e => e.ElemType == "shell").ToList();
            _mosaicBars    = elements.Where(e => e.ElemType == "beam" && !e.IsMeshLocked).ToList();
            ApplyMosaic();
        }
        else
        {
            var allBars = elements.Where(e => e.ElemType == "beam" && !e.IsMeshLocked).ToList();
            _baseBarGroups = BuildSectionColoredBars(nodeMap, allBars);
            // Стержни из кБ ЛИРЫ — утолщённой линией поверх своих КЭ.
            var lockedBars = elements.Where(e => e.ElemType == "beam" && e.IsMeshLocked).ToList();
            if (lockedBars.Count > 0 && BuildLinePoints(nodeMap, lockedBars) is { Count: > 0 } lockedPoints)
                _baseBarGroups = [.. _baseBarGroups, new BarGroup(Loc.S("FemLockedMembersLegend"), LockedMemberColor, lockedPoints, 3.5)];
            _mosaicNodeMap = nodeMap;
            _mosaicShells  = elements.Where(e => e.ElemType == "shell").ToList();
            _mosaicBars    = allBars;
            ApplyMosaic();
        }
        _edgeNodeMap = nodeMap;
        _edgeElements = elements;
        ShellEdgePoints  = _showShellEdges ? BuildShellEdges(nodeMap, elements) : null;
        PlanarRegionVisuals = BuildPlanarRegionVisuals(elements);
        (PlanarRegionMeshEdgePoints, PlanarRegionMeshNodePoints) = BuildPlanarRegionMeshOverlay(elements);
        SectionGlyphs = FemSectionGlyphFactory.Create(elements, _db.CrossSections, nodeMap);

        // Узлы: в режиме просмотра — только реально используемые отображаемыми КЭ (меньше шума
        // для импортированных схем); в режиме редактирования — все, включая ещё не связанные стержнем.
        var usedNodeKeys = elements
            .SelectMany(e => NodeIds(e.NodeIdsJson))
            .Select(id => id.ToString())
            .ToHashSet();
        var visibleNodes = EditMode ? allNodes : allNodes.Where(n => usedNodeKeys.Contains(n.NodeTag)).ToList();
        NodePoints = new Point3DCollection(visibleNodes.Select(n => new Point3D(n.X, n.Y, n.Z)));

        if (EditMode)
        {
            // Подложка из сетки ЛИРЫ только рисуется: выбирать и править можно лишь конструктивный слой.
            NodeProxies = visibleNodes
                .Where(n => !_bgOnlyNodeTags.Contains(n.NodeTag))
                .Select(n => (n.NodeTag, new Point3D(n.X, n.Y, n.Z)))
                .ToList();
            BarProxies = elements
                .Where(e => e.ElemType == "beam" && !_bgSet.Contains(e))
                .Select(e => (Tag: e.ElemTag, Pair: GetBarPoints(nodeMap, e)))
                .Where(x => x.Pair.HasValue)
                .Select(x => (x.Tag, x.Pair!.Value.p1, x.Pair.Value.p2))
                .ToList();
            _meshPickByTag = new Dictionary<string, FemMember>(StringComparer.Ordinal);
            foreach (var e in elements.Where(IsPickableMesh)) _meshPickByTag.TryAdd(e.ElemTag.Trim(), e);
            MeshPickBars = _meshPickByTag.Values
                .Where(e => e.ElemType == "beam")
                .Select(e => (Tag: e.ElemTag.Trim(), Pair: GetBarPoints(nodeMap, e)))
                .Where(x => x.Pair.HasValue)
                .Select(x => (x.Tag, x.Pair!.Value.p1, x.Pair.Value.p2))
                .ToList();
        }

        OnPropertyChanged(nameof(BarGroups));
        OnPropertyChanged(nameof(ShellMesh));
        OnPropertyChanged(nameof(HiShellMesh));
        OnPropertyChanged(nameof(MosaicShellMeshes));
        OnPropertyChanged(nameof(PlanarRegionVisuals));
        OnPropertyChanged(nameof(ShellEdgePoints));
        OnPropertyChanged(nameof(NodePoints));
        OnPropertyChanged(nameof(PlanarRegionMeshEdgePoints));
        OnPropertyChanged(nameof(PlanarRegionMeshNodePoints));
        OnPropertyChanged(nameof(SectionGlyphs));
    }

    // -------------------------------------------------------------------------

    Dictionary<string, Point3D> _mosaicNodeMap = [];
    List<FemMember> _mosaicShells = [];
    List<FemMember> _mosaicBars = [];
    List<BarGroup> _baseBarGroups = [];
    List<FemMember> _highlightShells = [];
    List<FemMember> _highlightBackground = [];

    /// <summary>Пересчитать мозаику при смене настроек: меняются только заливки пластин
    /// (<see cref="ShellMesh"/> и <see cref="MosaicShellMeshes"/>) и цвета стержней (<see cref="BarGroups"/>),
    /// камера не сбрасывается.</summary>
    void RefreshMosaic()
    {
        if (_mosaicShells.Count == 0 && _mosaicBars.Count == 0) { Mosaic.Compute([]); return; }
        ApplyMosaic();
        OnPropertyChanged(nameof(MosaicShellMeshes));
    }

    /// <summary>Раскрасить пластины или стержни по текущим настройкам мозаики.</summary>
    void ApplyMosaic()
    {
        var coloring = Mosaic.Compute(_mosaicShells.Select(e => e.ElemTag.Trim()), _mosaicBars.Select(e => e.ElemTag.Trim()));
        BarGroups = coloring is { Bars: true } ? BuildMosaicBars(coloring) : _baseBarGroups;
        MosaicBarSegments = coloring is { Bars: true }
            ? _mosaicBars
                .Select(e => (Tag: e.ElemTag.Trim(), Pair: GetBarPoints(_mosaicNodeMap, e)))
                .Where(x => x.Pair.HasValue)
                .Select(x => (x.Tag, x.Pair!.Value.p1, x.Pair.Value.p2))
                .ToList()
            : [];
        if (MosaicBarSegments.Count > 0)
            BarGroups = [.. BarGroups, .. BuildBarDiagrams()];
        var shellColoring = coloring is { Bars: false, ColorByTag.Count: > 0 } ? coloring : null;
        if (_highlightMember != null && shellColoring == null)
        {
            HiShellMesh = BuildShellMesh(_mosaicNodeMap, _highlightShells);
            ShellMesh   = BuildShellMesh(_mosaicNodeMap, _highlightBackground);
            MosaicShellMeshes = [];
        }
        else
        {
            HiShellMesh = null;
            (ShellMesh, MosaicShellMeshes) = BuildMosaicShells(shellColoring);
        }
    }

    /// <summary>Цвет положительной части эпюр на стержнях.</summary>
    public static Color BarDiagramPositiveColor => Color.FromRgb(190, 40, 40);
    /// <summary>Цвет отрицательной части эпюр на стержнях.</summary>
    public static Color BarDiagramNegativeColor => Color.FromRgb(40, 70, 170);

    /// <summary>Наибольшая ордината эпюры на стержнях — доля диагонали габарита стержней вида.</summary>
    const double BarDiagramSizeRatio = 0.05;

    /// <summary>Эпюры текущей мозаики на стержнях: ординаты по сечениям КЭ в местных осях, общий масштаб
    /// на вид. Пусто — эпюры выключены или у мозаики нет значений по сечениям.</summary>
    List<BarGroup> BuildBarDiagrams()
    {
        var profiles = Mosaic.BarProfiles(MosaicBarSegments.Select(s => s.Tag), out var plane);
        if (profiles == null || profiles.Count == 0) return [];
        double max = profiles.Values.SelectMany(p => p).Max(p => Math.Abs(p.V));
        if (!(max > 0)) return [];

        var bounds = new Rect3D(MosaicBarSegments[0].P1, new Size3D());
        foreach (var (_, p1, p2) in MosaicBarSegments) { bounds.Union(p1); bounds.Union(p2); }
        double diagonal = Math.Sqrt(bounds.SizeX * bounds.SizeX + bounds.SizeY * bounds.SizeY + bounds.SizeZ * bounds.SizeZ);
        double scale = diagonal * BarDiagramSizeRatio / max;

        var positive = new List<BarDiagramLine>();
        var negative = new List<BarDiagramLine>();

        // Коэффициент использования: ступени эпюры (сечения КЭ) окрашены шкалой мозаики — видно, какое сечение не прошло.
        if (Mosaic.UtilizationColor(0) != null)
        {
            var byColor = new Dictionary<Color, List<BarDiagramLine>>();
            foreach (var (tag, p1, p2) in MosaicBarSegments)
            {
                if (!profiles.TryGetValue(tag, out var profile)) continue;
                for (int i = 0; i + 1 < profile.Count; i += 2)
                {
                    var color = Mosaic.UtilizationColor(profile[i].V) ?? BarDiagramPositiveColor;
                    if (!byColor.TryGetValue(color, out var lines)) byColor[color] = lines = [];
                    BarDiagramGeometry.AddBar(lines, negative, (p1.X, p1.Y, p1.Z), (p2.X, p2.Y, p2.Z),
                        [profile[i], profile[i + 1]], plane, scale);
                }
            }
            return byColor.Select(kv => new BarGroup("", kv.Key, Points(kv.Value), 2.0)).ToList();
        }

        foreach (var (tag, p1, p2) in MosaicBarSegments)
            if (profiles.TryGetValue(tag, out var profile))
                BarDiagramGeometry.AddBar(positive, negative, (p1.X, p1.Y, p1.Z), (p2.X, p2.Y, p2.Z), profile, plane, scale);

        static Point3DCollection Points(List<BarDiagramLine> lines)
        {
            var points = new Point3DCollection(lines.Count * 2);
            foreach (var l in lines)
            {
                points.Add(new Point3D(l.A.X, l.A.Y, l.A.Z));
                points.Add(new Point3D(l.B.X, l.B.Y, l.B.Z));
            }
            return points;
        }
        var result = new List<BarGroup>();
        if (positive.Count > 0) result.Add(new BarGroup("", BarDiagramPositiveColor, Points(positive), 1.0));
        if (negative.Count > 0) result.Add(new BarGroup("", BarDiagramNegativeColor, Points(negative), 1.0));
        return result;
    }

    /// <summary>Стержни по цветам полос шкалы; стержни без данных — тонкой серой линией.</summary>
    List<BarGroup> BuildMosaicBars(PlateRebarMosaicColoring coloring)
    {
        var rest = new List<FemMember>();
        var byColor = new Dictionary<Color, List<FemMember>>();
        foreach (var e in _mosaicBars)
        {
            if (!coloring.ColorByTag.TryGetValue(e.ElemTag.Trim(), out var c)) { rest.Add(e); continue; }
            if (!byColor.TryGetValue(c, out var list)) byColor[c] = list = [];
            list.Add(e);
        }
        var result = new List<BarGroup>();
        if (rest.Count > 0 && BuildLinePoints(_mosaicNodeMap, rest) is { Count: > 0 } restPoints)
            result.Add(new BarGroup("", Colors.LightGray, restPoints, 0.8));
        foreach (var (color, bars) in byColor)
            if (BuildLinePoints(_mosaicNodeMap, bars) is { Count: > 0 } points)
                result.Add(new BarGroup("", color, points, 3.0));
        return result;
    }

    (MeshGeometry3D? Uncolored, IReadOnlyList<MosaicShellMesh> Colored) BuildMosaicShells(PlateRebarMosaicColoring? coloring)
    {
        if (coloring == null || coloring.ColorByTag.Count == 0)
            return (BuildShellMesh(_mosaicNodeMap, _mosaicShells), []);

        var rest = new List<FemMember>();
        var byColor = new Dictionary<Color, List<FemMember>>();
        foreach (var e in _mosaicShells)
        {
            if (!coloring.ColorByTag.TryGetValue(e.ElemTag.Trim(), out var c)) { rest.Add(e); continue; }
            if (!byColor.TryGetValue(c, out var list)) byColor[c] = list = [];
            list.Add(e);
        }
        var colored = byColor
            .Select(kv => (kv.Key, Mesh: BuildShellMesh(_mosaicNodeMap, kv.Value)))
            .Where(x => x.Mesh != null)
            .Select(x => new MosaicShellMesh(x.Key, x.Mesh!))
            .ToList();
        return (rest.Count > 0 ? BuildShellMesh(_mosaicNodeMap, rest) : null, colored);
    }

    List<BarGroup> BuildSectionColoredBars(Dictionary<string, Point3D> nodeMap, List<FemMember> bars)
    {
        var grouped = bars.GroupBy(e => e.SectionTag ?? "").OrderBy(g => g.Key).ToList();
        var result  = new List<BarGroup>(grouped.Count);

        for (int i = 0; i < grouped.Count; i++)
        {
            var g      = grouped[i];
            var color  = _palette[i % _palette.Length];
            var points = BuildLinePoints(nodeMap, g);
            if (points.Count > 0)
                result.Add(new BarGroup(g.Key, color, points));
        }

        return result;
    }

    List<PlanarRegionVisual> BuildPlanarRegionVisuals(List<FemMember> elements)
    {
        var result = new List<PlanarRegionVisual>();
        var regionMembers = elements.Where(e => e.PlanarRegionId.HasValue).ToList();
        if (regionMembers.Count == 0) return result;

        var regionIds = regionMembers.Select(e => e.PlanarRegionId!.Value).ToHashSet();
        var regionsById = _db.GetPlanarRegions(_schemaId)
            .Where(r => regionIds.Contains(r.Id))
            .ToDictionary(r => r.Id);

        foreach (var member in regionMembers)
        {
            if (!regionsById.TryGetValue(member.PlanarRegionId!.Value, out var region)) continue;

            var o  = region.Frame.Origin;
            var lx = region.Frame.LocalX;
            var ly = region.Frame.LocalY;
            Point3D ToWorld(double x, double y) => new(
                o.X + lx.X * x + ly.X * y,
                o.Y + lx.Y * x + ly.Y * y,
                o.Z + lx.Z * x + ly.Z * y);

            var (vertices, triangles) = PlanarRegionTriangulation.Triangulate(region);
            var positions = new Point3DCollection();
            foreach (var (x, y) in vertices) positions.Add(ToWorld(x, y));
            var indices = new Int32Collection();
            foreach (var (a, b, c) in triangles) { indices.Add(a); indices.Add(b); indices.Add(c); }
            var mesh = new MeshGeometry3D { Positions = positions, TriangleIndices = indices };

            var edgePoints = new Point3DCollection();
            void AddLoop(Contour contour)
            {
                int n = contour.X.Count - 1; // открытая форма — без дублирующей замыкающей вершины
                for (int i = 0; i < n; i++)
                {
                    int j = (i + 1) % n;
                    edgePoints.Add(ToWorld(contour.X[i], contour.Y[i]));
                    edgePoints.Add(ToWorld(contour.X[j], contour.Y[j]));
                }
            }
            AddLoop(region.RequireHull());
            foreach (var hole in region.Holes) AddLoop(hole);

            result.Add(new PlanarRegionVisual(member.ElemTag, mesh, edgePoints, member.IsMeshLocked));
        }

        return result;
    }

    /// <summary>Строит оверлей настоящей Gmsh-сетки (рёбра+узлы) для всех регионов схемы, у
    /// которых есть последний расчётный (IsCalculable) PlanarMeshSnapshot. Координаты берутся
    /// напрямую из PlanarMeshNode.X/Y/Z — они уже в глобальной системе (см.
    /// GmshPlanarMesher.ParseMsh22), пересчёт через Frame не нужен.</summary>
    (Point3DCollection? Edges, Point3DCollection? Nodes) BuildPlanarRegionMeshOverlay(List<FemMember> elements)
    {
        var regionIds = elements
            .Where(e => e.PlanarRegionId.HasValue)
            .Select(e => e.PlanarRegionId!.Value)
            .Distinct()
            .ToList();
        if (regionIds.Count == 0) return (null, null);

        var edgePoints = new Point3DCollection();
        var nodePoints = new Point3DCollection();

        foreach (var regionId in regionIds)
        {
            var snapshot = _db.GetPlanarMeshSnapshots(regionId).LastOrDefault(s => s.IsCalculable);
            if (snapshot == null) continue;

            foreach (var (a, b) in CScore.Planar.PlanarMeshEdgeExtractor.ExtractEdges(snapshot))
            {
                var na = snapshot.Nodes[a];
                var nb = snapshot.Nodes[b];
                edgePoints.Add(new Point3D(na.X, na.Y, na.Z));
                edgePoints.Add(new Point3D(nb.X, nb.Y, nb.Z));
            }
            foreach (var node in snapshot.Nodes)
                nodePoints.Add(new Point3D(node.X, node.Y, node.Z));
        }

        if (edgePoints.Count == 0) return (null, null);
        return (edgePoints, nodePoints);
    }

    MeshGeometry3D? BuildShellMesh(Dictionary<string, Point3D> nodeMap, List<FemMember> elements)
    {
        var shells = elements.Where(e => e.ElemType == "shell").ToList();
        if (shells.Count == 0) return null;

        var positions = new Point3DCollection(shells.Count * 4);
        var indices   = new Int32Collection(shells.Count * 6);
        var tags      = new List<string>(shells.Count * 4);
        var pickable  = new List<bool>(shells.Count * 4);
        int idx       = 0;

        foreach (var e in shells)
        {
            var ids = NodeIds(e.NodeIdsJson);
            var pts = ids.Select(id =>
                          nodeMap.TryGetValue(id.ToString(), out var p) ? p : (Point3D?)null)
                         .Where(p => p.HasValue)
                         .Select(p => p!.Value)
                         .ToArray();

            bool isMesh = IsPickableMesh(e);
            if (pts.Length == 3)
            {
                foreach (var p in pts) { positions.Add(p); tags.Add(e.ElemTag.Trim()); pickable.Add(isMesh); }
                indices.Add(idx); indices.Add(idx + 1); indices.Add(idx + 2);
                idx += 3;
            }
            else if (pts.Length >= 4)
            {
                // ЛИРА хранит узлы как [n1,n2,n3,n4], геометрический обход: n1→n2→n4→n3
                for (int k = 0; k < 4; k++) { positions.Add(pts[k]); tags.Add(e.ElemTag.Trim()); pickable.Add(isMesh); }
                indices.Add(idx); indices.Add(idx + 1); indices.Add(idx + 3);
                indices.Add(idx); indices.Add(idx + 3); indices.Add(idx + 2);
                idx += 4;
            }
        }

        var mesh = new MeshGeometry3D { Positions = positions, TriangleIndices = indices };
        _shellMeshTags.AddOrUpdate(mesh, [.. tags]);
        if (pickable.Contains(true)) _shellMeshPickable.AddOrUpdate(mesh, [.. pickable]);
        return mesh;
    }

    readonly System.Runtime.CompilerServices.ConditionalWeakTable<MeshGeometry3D, bool[]> _shellMeshPickable = new();

    /// <summary>Номер пластинчатого КЭ импортированной сетки по вершине заливки; null — не КЭ сетки
    /// (конструктивный элемент или сетка не из заливок пластин).</summary>
    public string? MeshShellTagAt(MeshGeometry3D mesh, int vertexIndex) =>
        _shellMeshPickable.TryGetValue(mesh, out var pickable) && vertexIndex >= 0 && vertexIndex < pickable.Length
            && pickable[vertexIndex] ? ShellTagAt(mesh, vertexIndex) : null;

    /// <summary>
    /// Подсветка выбранных КЭ сетки: линии стержней, заливка пластин (две копии, сдвинутые по нормали в обе
    /// стороны, — в плоскости подложки заливка мерцала бы) и контуры пластин.
    /// </summary>
    public (Point3DCollection Bars, MeshGeometry3D? Shells, Point3DCollection Outlines) MeshSelectionGeometry(
        IEnumerable<string> tags, double offset)
    {
        var bars = new Point3DCollection();
        var outlines = new Point3DCollection();
        var positions = new Point3DCollection();
        var indices = new Int32Collection();
        var nodeMap = _edgeNodeMap ?? [];
        foreach (var tag in tags)
        {
            if (!_meshPickByTag.TryGetValue(tag, out var e)) continue;
            if (e.ElemType == "beam")
            {
                if (GetBarPoints(nodeMap, e) is { } pair) { bars.Add(pair.p1); bars.Add(pair.p2); }
                continue;
            }
            var ids = NodeIds(e.NodeIdsJson);
            var pts = new List<Point3D>(4);
            foreach (int id in ids)
                if (nodeMap.TryGetValue(id.ToString(), out var p)) pts.Add(p);
            // Обход контура: у четырёхузлового КЭ узлы хранятся «1 2 4 3».
            Point3D[] loop = pts.Count switch
            {
                3 => [pts[0], pts[1], pts[2]],
                >= 4 => [pts[0], pts[1], pts[3], pts[2]],
                _ => [],
            };
            if (loop.Length == 0) continue;
            var normal = Vector3D.CrossProduct(loop[1] - loop[0], loop[^1] - loop[0]);
            if (normal.Length > 1e-12) normal.Normalize();
            for (int i = 0; i < loop.Length; i++) { outlines.Add(loop[i]); outlines.Add(loop[(i + 1) % loop.Length]); }
            foreach (double side in (double[])[offset, -offset])
            {
                int b = positions.Count;
                foreach (var p in loop) positions.Add(p + normal * side);
                for (int k = 1; k + 1 < loop.Length; k++) { indices.Add(b); indices.Add(b + k); indices.Add(b + k + 1); }
            }
        }
        var shells = positions.Count > 0 ? new MeshGeometry3D { Positions = positions, TriangleIndices = indices } : null;
        return (bars, shells, outlines);
    }

    // Вершины у каждого КЭ свои, поэтому вершина однозначно называет КЭ. Слабая таблица —
    // сетки пересоздаются при каждой смене мозаики.
    readonly System.Runtime.CompilerServices.ConditionalWeakTable<MeshGeometry3D, string[]> _shellMeshTags = new();

    /// <summary>Тег пластинчатого КЭ по вершине его заливки; null — сетка не из заливок пластин.</summary>
    public string? ShellTagAt(MeshGeometry3D mesh, int vertexIndex) =>
        _shellMeshTags.TryGetValue(mesh, out var tags) && vertexIndex >= 0 && vertexIndex < tags.Length
            ? tags[vertexIndex] : null;

    Point3DCollection? BuildShellEdges(Dictionary<string, Point3D> nodeMap, List<FemMember> elements)
    {
        var shells = elements.Where(e => e.ElemType == "shell").ToList();
        if (shells.Count == 0) return null;

        var seen = new HashSet<(int, int)>();
        var pts  = new Point3DCollection(shells.Count * 4);

        void AddEdge(int id1, int id2, Point3D p1, Point3D p2)
        {
            if (!seen.Add(id1 < id2 ? (id1, id2) : (id2, id1))) return;
            pts.Add(p1);
            pts.Add(p2);
        }

        foreach (var e in shells)
        {
            var ids = NodeIds(e.NodeIdsJson);
            var corners = ids
                .Select(id => nodeMap.TryGetValue(id.ToString(), out var p)
                    ? (nodeId: id, pos: p)
                    : (nodeId: -1, pos: default(Point3D)))
                .Where(x => x.nodeId >= 0)
                .ToArray();

            if (corners.Length == 3)
            {
                AddEdge(corners[0].nodeId, corners[1].nodeId, corners[0].pos, corners[1].pos);
                AddEdge(corners[1].nodeId, corners[2].nodeId, corners[1].pos, corners[2].pos);
                AddEdge(corners[2].nodeId, corners[0].nodeId, corners[2].pos, corners[0].pos);
            }
            else if (corners.Length >= 4)
            {
                // ЛИРА: геометрический обход n1→n2→n4→n3
                AddEdge(corners[0].nodeId, corners[1].nodeId, corners[0].pos, corners[1].pos);
                AddEdge(corners[1].nodeId, corners[3].nodeId, corners[1].pos, corners[3].pos);
                AddEdge(corners[3].nodeId, corners[2].nodeId, corners[3].pos, corners[2].pos);
                AddEdge(corners[2].nodeId, corners[0].nodeId, corners[2].pos, corners[0].pos);
            }
        }

        return pts.Count > 0 ? pts : null;
    }

    // -------------------------------------------------------------------------

    Point3DCollection BuildLinePoints(Dictionary<string, Point3D> nodeMap, IEnumerable<FemMember> elems)
    {
        var pts = new Point3DCollection();
        foreach (var e in elems)
        {
            var pair = GetBarPoints(nodeMap, e);
            if (pair.HasValue) { pts.Add(pair.Value.p1); pts.Add(pair.Value.p2); }
        }
        return pts;
    }

    /// <summary>
    /// Номера узлов КЭ из <c>NodeIdsJson</c> (<c>[1,2,3,4]</c>). Разбор вручную: JsonSerializer на сетках
    /// в сотни тысяч КЭ занимал заметную долю загрузки вида. Нестандартная запись — через JsonSerializer.
    /// </summary>
    public static int[] NodeIds(string? json)
    {
        if (string.IsNullOrEmpty(json)) return [];
        var s = json.AsSpan().Trim();
        if (s.Length < 2 || s[0] != '[' || s[^1] != ']') return JsonSerializer.Deserialize<int[]>(json) ?? [];
        s = s[1..^1];
        if (s.IsWhiteSpace()) return [];
        int count = s.Count(',') + 1;
        var ids = new int[count];
        int k = 0;
        foreach (var range in s.Split(','))
        {
            if (!int.TryParse(s[range].Trim(), System.Globalization.NumberStyles.AllowLeadingSign,
                    System.Globalization.CultureInfo.InvariantCulture, out ids[k++]))
                return JsonSerializer.Deserialize<int[]>(json) ?? [];
        }
        return ids;
    }

    static (Point3D p1, Point3D p2)? GetBarPoints(Dictionary<string, Point3D> nodeMap, FemMember e)
    {
        var ids = NodeIds(e.NodeIdsJson);
        if (ids.Length < 2) return null;
        if (!nodeMap.TryGetValue(ids[0].ToString(), out var p1)) return null;
        if (!nodeMap.TryGetValue(ids[1].ToString(), out var p2)) return null;
        return (p1, p2);
    }
}
