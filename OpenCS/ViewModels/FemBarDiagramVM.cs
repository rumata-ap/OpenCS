using System.Globalization;
using System.IO;
using System.Text.Json;
using CScore;
using CScore.Fem;
using CScore.Import;
using OpenCS.Services;
using OpenCS.Utilites;

namespace OpenCS.ViewModels;

/// <summary>Что показывает эпюра стержня.</summary>
public enum FemBarDiagramKind { Forces, Rebar, Assigned, Utilization }

/// <summary>Проверка с результатом по КЭ — источник эпюры коэффициента использования.</summary>
/// <param name="Rows">Строки результата по стержням цели.</param>
public sealed record FemBarDiagramCheck(FemCheck Check, string Label, IReadOnlyList<FemCheckRowResult> Rows);

/// <summary>Пункт списка участков: цепочка стержневых КЭ цели.</summary>
public sealed record FemBarDiagramChainOption(BarChain Chain, string Label);

/// <summary>Пункт списка видов эпюры.</summary>
public sealed record FemBarDiagramKindOption(FemBarDiagramKind Kind, string Label);

/// <summary>Пункт списка компонент: <see cref="BarForceComponent"/> или <see cref="BarRebarComponent"/>.</summary>
public sealed record FemBarDiagramComponentOption(object Component, string Label);

/// <summary>Строка таблицы значений эпюры по сечениям.</summary>
/// <param name="Value">Значение (при огибающей — наибольшее, при сравнении — заданная арматура) либо текст
/// отказа подбора.</param>
/// <param name="Min">Вторая величина: наименьшее значение огибающей либо подобранная арматура; пусто — её нет.</param>
public sealed record FemBarDiagramRow(int ElemNum, string Section, string S, string Value, string Min);

/// <summary>
/// Эпюры вдоль стержней цели (группы или конструктивного элемента): импортированные усилия наборов
/// схемы, подобранная арматура (ASP ЛИРЫ) по сечениям КЭ и заданная арматура (ТЗА ЛИРЫ) — вместе
/// с подобранной, если та же величина есть в подборе.
/// </summary>
public sealed class FemBarDiagramVM : ViewModelBase
{
    readonly IBarRebarFieldSource? _rebar, _assigned;
    readonly string? _rebarFile, _assignedFile;

    /// <summary>Эпюра или её настройки изменились.</summary>
    public event EventHandler? Changed;

    /// <param name="targetTag">Имя цели — в заголовок окна.</param>
    /// <param name="chains">Цепочки стержневых КЭ цели.</param>
    /// <param name="forceSets">Наборы усилий со строками по стержням цели.</param>
    /// <param name="rebar">Подобранная арматура стержней; null — нет.</param>
    /// <param name="rebarFile">Имя файла подбора.</param>
    /// <param name="assigned">Заданная арматура стержней (ТЗА); null — нет.</param>
    /// <param name="assignedFile">Имя файла ТЗА.</param>
    /// <param name="checks">Проверки с результатом по КЭ стержней цели.</param>
    public FemBarDiagramVM(
        string targetTag, IReadOnlyList<BarChain> chains, IReadOnlyList<ForceSet> forceSets,
        IBarRebarFieldSource? rebar, string? rebarFile,
        IBarRebarFieldSource? assigned = null, string? assignedFile = null,
        IReadOnlyList<FemBarDiagramCheck>? checks = null)
    {
        Checks = checks ?? [];
        TargetTag = targetTag;
        _rebar = rebar;
        _rebarFile = rebarFile;
        _assigned = assigned;
        _assignedFile = assignedFile;
        Chains = chains.Select(c => new FemBarDiagramChainOption(c, ChainLabel(c))).ToList();
        ForceSets = forceSets;

        var kinds = new List<FemBarDiagramKindOption>();
        if (forceSets.Count > 0) kinds.Add(new(FemBarDiagramKind.Forces, Loc.S("FemBarDiagramKindForces")));
        if (rebar != null) kinds.Add(new(FemBarDiagramKind.Rebar, Loc.S("FemBarDiagramKindRebar")));
        if (assigned != null) kinds.Add(new(FemBarDiagramKind.Assigned, Loc.S("FemBarDiagramKindAssigned")));
        if (Checks.Count > 0) kinds.Add(new(FemBarDiagramKind.Utilization, Loc.S("MosaicSourceUtilization")));
        Kinds = kinds;

        _selectedCheck = Checks.FirstOrDefault();
        _selectedForceSet = forceSets.FirstOrDefault();
        // Наборы бывают уже цели (усилия импортированы на один элемент группы) — открыться на участке с усилиями.
        var loaded = _selectedForceSet?.Items.Select(i => i.SourceElementNum).OfType<int>().ToHashSet();
        _selectedChain = Chains.FirstOrDefault(c => loaded != null && c.Chain.Elements.Any(e => loaded.Contains(e.ElemNum)))
                         ?? Chains.FirstOrDefault();
        _selectedKind = kinds.FirstOrDefault();
        RefreshComponents();
        Rebuild();
    }

    /// <summary>Имя цели.</summary>
    public string TargetTag { get; }
    public IReadOnlyList<FemBarDiagramChainOption> Chains { get; }
    public IReadOnlyList<FemBarDiagramKindOption> Kinds { get; }
    public IReadOnlyList<ForceSet> ForceSets { get; }
    public IReadOnlyList<FemBarDiagramCheck> Checks { get; }
    public IReadOnlyList<FemBarDiagramComponentOption> Components { get; private set; } = [];

    /// <summary>У цели несколько участков — нужен их список.</summary>
    public bool HasSeveralChains => Chains.Count > 1;
    /// <summary>Показываются усилия — нужен выбор набора.</summary>
    public bool IsForces => SelectedKind?.Kind == FemBarDiagramKind.Forces;
    /// <summary>Показывается коэффициент использования — нужен выбор проверки.</summary>
    public bool IsUtilization => SelectedKind?.Kind == FemBarDiagramKind.Utilization;
    /// <summary>Нет ни наборов усилий, ни подобранной арматуры.</summary>
    public bool NoData => Kinds.Count == 0;

    FemBarDiagramChainOption? _selectedChain;
    public FemBarDiagramChainOption? SelectedChain
    {
        get => _selectedChain;
        set { if (Equals(_selectedChain, value)) return; _selectedChain = value; OnPropertyChanged(); Rebuild(); }
    }

    FemBarDiagramKindOption? _selectedKind;
    public FemBarDiagramKindOption? SelectedKind
    {
        get => _selectedKind;
        set
        {
            if (Equals(_selectedKind, value)) return;
            _selectedKind = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(IsForces));
            OnPropertyChanged(nameof(IsUtilization));
            RefreshComponents();
            Rebuild();
        }
    }

    ForceSet? _selectedForceSet;
    public ForceSet? SelectedForceSet
    {
        get => _selectedForceSet;
        set { if (ReferenceEquals(_selectedForceSet, value)) return; _selectedForceSet = value; OnPropertyChanged(); Rebuild(); }
    }

    FemBarDiagramCheck? _selectedCheck;
    public FemBarDiagramCheck? SelectedCheck
    {
        get => _selectedCheck;
        set
        {
            if (ReferenceEquals(_selectedCheck, value)) return;
            _selectedCheck = value;
            OnPropertyChanged();
            RefreshComponents();
            Rebuild();
        }
    }

    FemBarDiagramComponentOption? _selectedComponent;
    public FemBarDiagramComponentOption? SelectedComponent
    {
        get => _selectedComponent;
        set { if (Equals(_selectedComponent, value)) return; _selectedComponent = value; OnPropertyChanged(); Rebuild(); }
    }

    /// <summary>Текущая эпюра.</summary>
    public BarDiagramSeries Series { get; private set; } = BarDiagramSeries.Empty;
    /// <summary>Заголовок эпюры.</summary>
    public string Title { get; private set; } = "";
    /// <summary>Значения по сечениям.</summary>
    public IReadOnlyList<FemBarDiagramRow> Rows { get; private set; } = [];
    /// <summary>На эпюре две величины: огибающая усилий (наибольшие и наименьшие) либо заданная арматура
    /// вместе с подобранной — есть второй столбец таблицы.</summary>
    public bool HasEnvelope { get; private set; }
    /// <summary>Вторая величина эпюры — подобранная арматура рядом с заданной (иначе — наименьшие значения).</summary>
    public bool ComparesWithSelected { get; private set; }
    /// <summary>Опорная линия (Кисп = 1 на эпюре коэффициента использования); пусто — её нет.</summary>
    public IReadOnlyList<BarDiagramSegment> ReferenceLine { get; private set; } = [];

    void RefreshComponents()
    {
        List<FemBarDiagramComponentOption> Rebar(IBarRebarFieldSource? source) => source == null ? []
            : Enum.GetValues<BarRebarComponent>()
                .Where(source.Supports)
                .Select(c => new FemBarDiagramComponentOption(c,
                    RebarComponentLabels.Bar(c, RebarComponentLabels.IsScad(source)))).ToList();

        Components = SelectedKind?.Kind switch
        {
            FemBarDiagramKind.Forces => Enum.GetValues<BarForceComponent>()
                .Select(c => new FemBarDiagramComponentOption(c, Loc.S("MosaicBarForce" + c))).ToList(),
            FemBarDiagramKind.Rebar => Rebar(_rebar),
            FemBarDiagramKind.Assigned => Rebar(_assigned),
            FemBarDiagramKind.Utilization when SelectedCheck != null => SelectedCheck.Rows
                .Select(r => r.RebarSource).Distinct()
                .Select(key => new FemBarDiagramComponentOption(key,
                    key == "" ? Loc.S("MosaicUtilization") : FemCheckContext.SourceName(key)))
                .ToList(),
            _ => [],
        };
        OnPropertyChanged(nameof(Components));
        // Усилия открываются на изгибающем моменте Mx (плоскость X1Z1 стержня), арматура — на суммарной продольной.
        _selectedComponent = Components.FirstOrDefault(c => Equals(c.Component, BarForceComponent.Mx)) ?? Components.FirstOrDefault();
        OnPropertyChanged(nameof(SelectedComponent));
    }

    void Rebuild()
    {
        var chain = SelectedChain?.Chain;
        Series = (chain, SelectedKind?.Kind, SelectedComponent?.Component) switch
        {
            ({ } c, FemBarDiagramKind.Forces, BarForceComponent comp) when SelectedForceSet != null =>
                BarDiagram.Forces(c, SelectedForceSet, comp),
            ({ } c, FemBarDiagramKind.Rebar, BarRebarComponent comp) when _rebar != null =>
                BarDiagram.Rebar(c, _rebar, comp),
            ({ } c, FemBarDiagramKind.Assigned, BarRebarComponent comp) when _assigned != null =>
                AssignedSeries(c, comp),
            ({ } c, FemBarDiagramKind.Utilization, string source) when SelectedCheck != null =>
                BarDiagram.Utilization(c, SelectedCheck.Rows, source),
            _ => BarDiagramSeries.Empty,
        };
        bool utilization = SelectedKind?.Kind == FemBarDiagramKind.Utilization;
        ReferenceLine = utilization && chain != null && Series.Points.Count > 0
            ? [new BarDiagramSegment(0, chain.Length, 1, 1)] : [];
        ComparesWithSelected = SelectedKind?.Kind == FemBarDiagramKind.Assigned && Series.Lower.Count > 0;
        HasEnvelope = Series.Lower.Count > 0;
        Title = SelectedComponent == null ? "" : $"{SelectedComponent.Label} — " + SelectedKind?.Kind switch
        {
            FemBarDiagramKind.Forces => SelectedForceSet?.Tag,
            FemBarDiagramKind.Utilization => SelectedCheck?.Label,
            FemBarDiagramKind.Assigned when ComparesWithSelected => $"{_assignedFile} / {_rebarFile}",
            FemBarDiagramKind.Assigned => _assignedFile,
            _ => _rebarFile,
        };
        Rows = Series.Points.Select(p =>
        {
            string? failure = p.FailureCode is int code ? string.Format(Loc.S("FemBarDiagramFailure"), code) : null;
            return new FemBarDiagramRow(
                p.ElemNum,
                p.SectionNum?.ToString(CultureInfo.CurrentCulture) ?? "",
                p.S.ToString("0.###", CultureInfo.CurrentCulture),
                failure != null && !ComparesWithSelected ? failure
                    : utilization && p.Failed ? Loc.S("MosaicFailedNoUtilization")
                    : utilization && p.Max == null ? Loc.S("MosaicNotChecked") : Format(p.Max),
                failure != null && ComparesWithSelected ? failure : HasEnvelope ? Format(p.Min) : "");
        }).ToList();
        OnPropertyChanged(nameof(Series));
        OnPropertyChanged(nameof(Title));
        OnPropertyChanged(nameof(Rows));
        OnPropertyChanged(nameof(HasEnvelope));
        OnPropertyChanged(nameof(ComparesWithSelected));
        OnPropertyChanged(nameof(ReferenceLine));
        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Эпюра заданной арматуры; если та же величина есть в подборе — вместе с подобранной: вторая серия
    /// и строки таблицы по сечениям подбора (первая величина — заданная на КЭ, вторая — подобранная).
    /// </summary>
    BarDiagramSeries AssignedSeries(BarChain chain, BarRebarComponent component)
    {
        var assigned = BarDiagram.Rebar(chain, _assigned!, component);
        if (_rebar == null || !_rebar.Supports(component)) return assigned;
        var selected = BarDiagram.Rebar(chain, _rebar, component);
        if (selected.Points.Count == 0) return assigned;

        var assignedByElem = assigned.Points.ToDictionary(p => p.ElemNum, p => p.Max);
        var withSelected = selected.Points.Select(p => p.ElemNum).ToHashSet();
        var points = selected.Points
            .Select(p => p with { Max = assignedByElem.GetValueOrDefault(p.ElemNum), Min = p.Max })
            .Concat(assigned.Points.Where(p => !withSelected.Contains(p.ElemNum)).Select(p => p with { Min = null }))
            .OrderBy(p => p.S)
            .ToList();
        return new BarDiagramSeries(assigned.Upper, selected.Upper, points);
    }

    static string Format(double? v) => v?.ToString("0.###", CultureInfo.CurrentCulture) ?? "";

    static string ChainLabel(BarChain chain)
    {
        string length = chain.Length.ToString("0.##", CultureInfo.CurrentCulture);
        return chain.Elements.Count == 1
            ? string.Format(Loc.S("FemBarDiagramChainOne"), chain.Elements[0].ElemNum, length)
            : string.Format(Loc.S("FemBarDiagramChainMany"), chain.Elements[0].ElemNum, chain.Elements[^1].ElemNum,
                chain.Elements.Count, length);
    }

    /// <summary>
    /// Собрать данные эпюр цели из БД: цепочки её стержневых КЭ, наборы усилий схемы со строками по этим КЭ,
    /// подобранную арматуру стержней. Null — у цели нет стержневых КЭ с известными узлами.
    /// </summary>
    /// <param name="warn">Куда сообщить об ошибке чтения файла подбора.</param>
    public static FemBarDiagramVM? Load(DatabaseService db, IFemCheckable target, Action<string>? warn = null)
    {
        var (scope, schemaId) = target switch
        {
            FemMemberGroup group => (db.GetFemCheckScope(group), group.SchemaId),
            FemMember member => (db.GetFemCheckScope(member), member.SchemaId),
            _ => (new FemCheckScope([], [], RefersToMeshElements: false), 0),
        };

        var points = new Dictionary<int, (double, double, double)>();
        foreach (var n in db.GetFemMeshNodes(schemaId))
            if (int.TryParse(n.NodeTag, NumberStyles.Integer, CultureInfo.InvariantCulture, out int tag))
                points.TryAdd(tag, (n.X, n.Y, n.Z));

        var bars = new List<BarChainBar>();
        foreach (var e in scope.Elements)
        {
            if (e.Element.ElemType != "beam" || e.ElemNum is not int num) continue;
            int[] nodes;
            try { nodes = JsonSerializer.Deserialize<int[]>(e.Element.NodeIdsJson) ?? []; }
            catch (JsonException) { continue; }
            if (nodes.Length == 2 && points.TryGetValue(nodes[0], out var pi) && points.TryGetValue(nodes[1], out var pj))
                bars.Add(new BarChainBar(num, nodes[0], nodes[1], pi, pj));
        }
        var chains = BarChains.Build(bars);
        if (chains.Count == 0) return null;

        var numbers = bars.Select(b => b.ElemNum).ToHashSet();
        var sets = db.ForceSets
            .Where(fs => fs.SourceSchemaId == schemaId
                         && fs.ElementStats(shell: false).ByElement.Keys.Any(numbers.Contains))
            .OrderBy(fs => fs.Tag, StringComparer.CurrentCulture)
            .ToList();

        IBarRebarFieldSource? rebar = null;
        string? rebarFile = null;
        ScadSelectedRebarFile? scadSelected = null;
        // У схем SCAD подобранная арматура — из выгрузки плагина, а не из ASP.
        if (db.GetFemSchemaSourceType(schemaId) == "scad")
        {
            if (db.GetFemSchemaSourceFile(schemaId, FemSchemaSourceFileKind.ScadSelectedRebar) is { } scad)
                try
                {
                    var file = scadSelected = ScadRebarExportReader.Read(scad.Data);
                    if (numbers.Any(file.Bars.ContainsKey))
                    {
                        rebar = new ScadSelectedBarRebarSource(file);
                        rebarFile = scad.FileName;
                    }
                }
                catch (Exception ex) when (ex is InvalidDataException or IOException or ArgumentException)
                {
                    warn?.Invoke(string.Format(Loc.S("PlateRebarMosaicReadError"), scad.FileName, ex.Message));
                }
        }
        else if (db.GetFemSchemaSelectedReinforcementFile(schemaId) is { } asp)
            try
            {
                var file = LiraAspReader.Read(asp.Data);
                if (numbers.Any(file.Bars.ContainsKey))
                {
                    rebar = new LiraAspBarRebarSource(file);
                    rebarFile = asp.FileName;
                }
            }
            catch (Exception ex) when (ex is InvalidDataException or IOException or ArgumentException)
            {
                warn?.Invoke(string.Format(Loc.S("PlateRebarMosaicReadError"), asp.FileName, ex.Message));
            }

        IBarRebarFieldSource? assigned = null;
        string? assignedFile = null;
        // У схем SCAD заданное армирование — вложение схемы, прочитанное из .SPR.
        if (db.GetFemSchemaSourceType(schemaId) == "scad")
        {
            if (db.GetFemSchemaSourceFile(schemaId, FemSchemaSourceFileKind.ScadAssignedRebar) is { } scadAssigned)
                try
                {
                    var file = ScadAssignedRebarFile.FromJson(System.Text.Encoding.UTF8.GetString(scadAssigned.Data));
                    if (numbers.Any(n => file.Rod(n) != null))
                    {
                        assigned = new ScadAssignedBarRebarSource(file,
                            ScadAssignedBarRebarSource.SectionCounts(scadSelected, sets));
                        assignedFile = Loc.S("ScadAssignedRebarLabel");
                    }
                }
                catch (InvalidDataException ex)
                {
                    warn?.Invoke(string.Format(Loc.S("PlateRebarMosaicReadError"), Loc.S("ScadAssignedRebarLabel"), ex.Message));
                }
        }
        else if (db.GetFemSchemaReinforcementFile(schemaId) is { } rbt)
            try
            {
                var source = new LiraRbtBarRebarSource(LiraRbtReader.Read(rbt.Data), scope.Elements
                    .Where(e => e.Element.ElemType == "beam")
                    .Select(e => new KeyValuePair<string, string?>(e.Element.ElemTag, e.Element.ReinforcementTypeIds)));
                if (source.HasBars)
                {
                    assigned = source;
                    assignedFile = rbt.FileName;
                }
            }
            catch (Exception ex) when (ex is InvalidDataException or IOException or ArgumentException)
            {
                warn?.Invoke(string.Format(Loc.S("PlateRebarMosaicReadError"), rbt.FileName, ex.Message));
            }

        // Проверки стержней с результатом по КЭ: строки по КЭ цели.
        var checks = new List<FemBarDiagramCheck>();
        foreach (var c in db.FemChecks.Where(c => c.SchemaId == schemaId && c.ResultId != null && !FemCheckContext.IsPlate(c))
                                      .OrderBy(c => c.DisplayTag, StringComparer.CurrentCulture))
        {
            var rows = db.GetFemCheckRowResults(c.Id)
                .Where(r => numbers.Contains(r.ElemNum)).ToList();
            if (rows.Count > 0) checks.Add(new FemBarDiagramCheck(c, c.DisplayTag, rows));
        }

        return new FemBarDiagramVM(target.Tag, chains, sets, rebar, rebarFile, assigned, assignedFile, checks);
    }
}
