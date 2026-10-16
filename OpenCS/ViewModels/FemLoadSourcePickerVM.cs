using CScore.Combinations;
using CScore.Fem;
using CScore.Fem.Combinations;
using OpenCS.Tasks;
using OpenCS.Utilites;

namespace OpenCS.ViewModels;

/// <summary>Вид источника нагрузки стадии.</summary>
public enum FemLoadSourceKind
{
    /// <summary>Комбинация схемы (<see cref="FemLoadDefinition"/>).</summary>
    Combination,
    /// <summary>Загружение схемы целиком.</summary>
    LoadCase,
    /// <summary>Выражение сохранённой постановки, которого нет среди комбинаций и загружений схемы.</summary>
    Unresolved,
}

/// <summary>Слагаемое в расшифровке состава: «1,35 · ЗГ1 Собственный вес».</summary>
public sealed record FemLoadSourceTerm(string Coefficient, string Code, string Name)
{
    public string Text => $"{Coefficient} · {Code} {Name}";
}

/// <summary>
/// Источник нагрузки стадии постановки: комбинация, загружение или выражение прежней постановки. Несёт всё, что
/// показывают ячейка стадии и окно выбора: тип, имя, формулу «1,35·ЗГ1 + 1,05·ЗГ2», расшифровку, γf и долю загружения.
/// </summary>
public sealed class FemLoadSource
{
    public FemLoadSourceKind Kind { get; init; }
    public FemLoadDefinition? Definition { get; init; }
    public FemLoadCase? LoadCase { get; init; }

    /// <summary>Номер в своём списке схемы (комбинации — порядок вкладки «Комбинации», загружения — ЗГn).</summary>
    public int Number { get; init; }

    /// <summary>Имя комбинации/загружения.</summary>
    public string Tag { get; init; } = "";

    /// <summary>Тип: C/CL/N/NL/A у комбинации, G/L/Q/A у загружения, пусто у выражения прежней постановки.</summary>
    public string TypeBadge { get; init; } = "";

    /// <summary>Вид загружения по СП 20 (для иконки); у комбинации — пусто.</summary>
    public string Sp20Type => LoadCase?.Sp20Type ?? "";

    /// <summary>Формула «1,35·ЗГ1 + 1,05·ЗГ2»; у загружения — его код «ЗГ3».</summary>
    public string Formula { get; init; } = "";

    /// <summary>Откуда комбинация: огибающая, СП 20, вручную; у загружения — пусто.</summary>
    public string Origin { get; init; } = "";

    /// <summary>Расшифровка слагаемых с именами загружений.</summary>
    public IReadOnlyList<FemLoadSourceTerm> Terms { get; init; } = [];

    /// <summary>γf загружения; у комбинации — пусто.</summary>
    public string GammaText { get; init; } = "";

    /// <summary>Доля длительной части у кратковременного загружения, иначе «—».</summary>
    public string ShareText { get; init; } = "";

    public string ExpressionJson { get; init; } = "{}";

    public bool IsUnresolved => Kind == FemLoadSourceKind.Unresolved;

    /// <summary>Подсказка ячейки стадии: имя, формула и расшифровка построчно.</summary>
    public string ToolTipText
    {
        get
        {
            var lines = new List<string> { string.IsNullOrEmpty(TypeBadge) ? Tag : $"[{TypeBadge}] {Tag}" };
            if (IsUnresolved) lines.Add(Loc.S("FemLoadSourceUnresolvedTip"));
            if (Kind != FemLoadSourceKind.LoadCase && Formula.Length > 0) lines.Add(Formula);
            if (Kind != FemLoadSourceKind.LoadCase) lines.AddRange(Terms.Select(t => "  " + t.Text));
            return string.Join(Environment.NewLine, lines);
        }
    }

    /// <summary>Строка поиска: имя, формула и имена загружений слагаемых (нижний регистр).</summary>
    internal string SearchText { get; init; } = "";

    /// <summary>Коды загружений слагаемых «ЗГ3» — поиск по коду точный (ЗГ3 не находит ЗГ31).</summary>
    internal HashSet<string> Codes { get; init; } = new(StringComparer.CurrentCultureIgnoreCase);
}

/// <summary>Список источников нагрузки схемы и общая формула комбинаций (для вкладки «Комбинации» и диалога постановки).</summary>
public static class FemLoadSources
{
    /// <summary>Номера загружений ЗГn — по порядку схемы, с 1.</summary>
    public static Dictionary<int, int> CaseNumbers(IReadOnlyList<FemLoadCase> loadCases)
    {
        var numbers = new Dictionary<int, int>();
        for (int i = 0; i < loadCases.Count; i++) numbers.TryAdd(loadCases[i].Id, i + 1);
        return numbers;
    }

    static string Code(IReadOnlyDictionary<int, int> numbers, int loadCaseId) =>
        numbers.TryGetValue(loadCaseId, out var n) ? FemCombinationsTabVM.CaseCode(n) : $"#{loadCaseId}";

    /// <summary>
    /// Формула «1,35·ЗГ1 + 1,05·ЗГ2»; отрицательный коэффициент — через минус. Выражения без слагаемых (прежние
    /// постановки): сумма загружений — «ЗГ1 + ЗГ2», «все загружения» — словами.
    /// </summary>
    public static string Formula(FemLoadExpression expression, IReadOnlyDictionary<int, int> numbers)
    {
        if (expression.Terms.Count > 0)
            return string.Concat(expression.Terms.Select((t, i) =>
            {
                string value = FemLoadsFormat.Number(Math.Abs(t.Coefficient)) + "·" + Code(numbers, t.LoadCaseId);
                return i == 0 ? (t.Coefficient < 0 ? "−" : "") + value : (t.Coefficient < 0 ? " − " : " + ") + value;
            }));
        if (expression.Mode == FemLoadExpressionMode.All) return Loc.S("FemLoadSourceAllCases");
        return string.Join(" + ", expression.LoadCaseIds.Select(id => Code(numbers, id)));
    }

    /// <summary>Расшифровка слагаемых: коэффициент, код и имя загружения.</summary>
    static List<FemLoadSourceTerm> Terms(FemLoadExpression expression, IReadOnlyDictionary<int, int> numbers,
        IReadOnlyDictionary<int, FemLoadCase> byId)
    {
        string Name(int id) => byId.TryGetValue(id, out var lc) ? lc.Tag : Loc.S("FemLoadSourceMissingCase");
        if (expression.Terms.Count > 0)
            return expression.Terms.Select(t => new FemLoadSourceTerm(
                (t.Coefficient < 0 ? "−" : "") + FemLoadsFormat.Number(Math.Abs(t.Coefficient)),
                Code(numbers, t.LoadCaseId), Name(t.LoadCaseId))).ToList();
        var ids = expression.Mode == FemLoadExpressionMode.All ? byId.Keys.OrderBy(id => numbers.GetValueOrDefault(id)) : expression.LoadCaseIds.AsEnumerable();
        return ids.Select(id => new FemLoadSourceTerm("1", Code(numbers, id), Name(id))).ToList();
    }

    /// <summary>Подпись происхождения комбинации по <see cref="FemLoadDefinition.SourceKind"/>.</summary>
    public static string OriginLabel(string? sourceKind) => sourceKind switch
    {
        "envelope" => Loc.S("FemLoadSourceOriginEnvelope"),
        "sp20" => Loc.S("FemLoadSourceOriginSp20"),
        "manual" or null or "" => Loc.S("FemLoadSourceOriginManual"),
        _ => sourceKind,
    };

    /// <summary>
    /// Источники схемы: комбинации по типам C → CL → N → NL → A (внутри — порядок вкладки «Комбинации»), затем
    /// загружения по порядку схемы.
    /// </summary>
    public static List<FemLoadSource> Build(IReadOnlyList<FemLoadDefinition> definitions, IReadOnlyList<FemLoadCase> loadCases,
        Sp20GammaDefaults defaults)
    {
        var numbers = CaseNumbers(loadCases);
        var byId = loadCases.GroupBy(lc => lc.Id).ToDictionary(g => g.Key, g => g.First());
        var list = new List<FemLoadSource>();
        var typeOrder = FemCombinationTypes.All.Select((t, i) => (t, i)).ToDictionary(p => p.t, p => p.i);
        var combinations = definitions.Select((d, i) => (d, Number: i + 1))
            .OrderBy(p => typeOrder[FemCombinationTypes.Normalize(p.d.CombinationType)]).ThenBy(p => p.Number);
        foreach (var (d, number) in combinations)
        {
            var expression = d.GetExpression();
            list.Add(Composite(FemLoadSourceKind.Combination, d, number, d.Tag, FemCombinationTypes.Normalize(d.CombinationType),
                OriginLabel(d.SourceKind), expression, numbers, byId));
        }
        for (int i = 0; i < loadCases.Count; i++)
        {
            var lc = loadCases[i];
            var type = FemCombinationCoefficients.LoadType(lc);
            var expression = FemLinearCasesVM.CaseExpression(lc.Id);
            string code = FemCombinationsTabVM.CaseCode(i + 1);
            list.Add(new FemLoadSource
            {
                Kind = FemLoadSourceKind.LoadCase, LoadCase = lc, Number = i + 1, Tag = lc.Tag,
                TypeBadge = FemLinearCasesVM.TypeLetter(type), Formula = code,
                GammaText = FemLoadsFormat.Number(FemCombinationCoefficients.GammaF(lc, defaults)),
                ShareText = type == NormLoadType.ShortTerm && lc.LongTermShare is double share ? FemLoadsFormat.Number(share) : "—",
                Terms = [new FemLoadSourceTerm("1", code, lc.Tag)],
                ExpressionJson = expression.ToJson(),
                SearchText = $"{lc.Tag} {code}".ToLowerInvariant(),
                Codes = new(StringComparer.CurrentCultureIgnoreCase) { code },
            });
        }
        return list;
    }

    /// <summary>
    /// Источник для выражения сохранённой стадии: совпадающий из списка схемы, иначе — выражение как есть
    /// (<see cref="FemLoadSourceKind.Unresolved"/>), чтобы правка постановки не подменила нагрузку молча.
    /// </summary>
    public static FemLoadSource Resolve(IReadOnlyList<FemLoadSource> sources, string? expressionJson, IReadOnlyList<FemLoadCase> loadCases)
    {
        FemLoadExpression expression;
        try { expression = FemLoadExpression.Parse(expressionJson); }
        catch (System.Text.Json.JsonException) { expression = new(); }
        string json = expression.ToJson();
        if (sources.FirstOrDefault(s => s.ExpressionJson == json) is { } match) return match;
        // Комбинация могла пересчитаться («авто»-коэффициенты) — сравнение по составу без ролей и режимов.
        string key = CompositionKey(expression);
        if (key.Length > 0 && sources.FirstOrDefault(s => CompositionKey(FemLoadExpression.Parse(s.ExpressionJson)) == key) is { } same)
            return same;
        var numbers = CaseNumbers(loadCases);
        var byId = loadCases.GroupBy(lc => lc.Id).ToDictionary(g => g.Key, g => g.First());
        return Composite(FemLoadSourceKind.Unresolved, null, 0, Loc.S("FemLoadSourceUnresolved"), "", "", expression, numbers, byId);
    }

    /// <summary>Состав выражения «id:коэффициент;…» (6 знаков) — без ролей и режимов слагаемых.</summary>
    static string CompositionKey(FemLoadExpression e) => e.Terms.Count > 0
        ? string.Join(";", e.Terms.Select(t => $"{t.LoadCaseId}:{t.Coefficient.ToString("0.######", System.Globalization.CultureInfo.InvariantCulture)}"))
        : "";

    /// <summary>Источник-формула: комбинация схемы или выражение прежней постановки.</summary>
    static FemLoadSource Composite(FemLoadSourceKind kind, FemLoadDefinition? definition, int number, string tag, string type,
        string origin, FemLoadExpression expression, IReadOnlyDictionary<int, int> numbers, IReadOnlyDictionary<int, FemLoadCase> byId)
    {
        var terms = Terms(expression, numbers, byId);
        string formula = Formula(expression, numbers);
        return new FemLoadSource
        {
            Kind = kind, Definition = definition, Number = number, Tag = tag, TypeBadge = type, Formula = formula,
            Origin = origin, Terms = terms, ExpressionJson = expression.ToJson(),
            SearchText = string.Join(" ", new[] { tag, formula }.Concat(terms.Select(t => t.Name))).ToLowerInvariant(),
            Codes = new(terms.Select(t => t.Code), StringComparer.CurrentCultureIgnoreCase),
        };
    }
}

/// <summary>Переключатель типа комбинаций C/CL/N/NL/A в окне выбора: «C 168», подсказка — название группы.</summary>
public sealed class FemLoadSourceTypeFilter(FemLoadSourcePickerVM owner, string type, int count) : ViewModelBase
{
    public string Type { get; } = type;
    public int Count { get; } = count;
    public string Label => $"{Type} {Count}";
    public string ToolTip => Loc.S("FemCombinationGroup" + Type);
    public bool IsAvailable => Count > 0;

    bool _isOn;
    public bool IsOn
    {
        get => _isOn;
        set
        {
            if (_isOn == value) return;
            _isOn = value;
            OnPropertyChanged();
            owner.OnTypeFilterChanged(this);
        }
    }

    /// <summary>Установка без пересчёта списка и без памяти сеанса (начальное состояние, «Все типы»).</summary>
    internal void Init(bool value)
    {
        if (_isOn == value) return;
        _isOn = value;
        OnPropertyChanged(nameof(IsOn));
    }
}

/// <summary>
/// Окно выбора нагрузки стадии нелинейного расчёта: вкладки «Комбинации» (переключатели типов, поиск по имени,
/// формуле, имени и коду загружения) и «Загружения». Выбирается одна строка активной вкладки. Отключённые типы и
/// последняя вкладка запоминаются на время сеанса программы.
/// </summary>
public sealed class FemLoadSourcePickerVM : ViewModelBase
{
    public const int CombinationsTab = 0;
    public const int CasesTab = 1;

    // Память сеанса: какие типы отключены пользователем и какая вкладка была последней.
    static readonly HashSet<string> s_typesOff = [];
    static int? s_lastTab;

    /// <summary>Сброс памяти сеанса (тесты).</summary>
    internal static void ResetSessionMemory()
    {
        s_typesOff.Clear();
        s_lastTab = null;
    }

    readonly List<FemLoadSource> _combinations;
    readonly List<FemLoadSource> _cases;

    /// <param name="sources">Источники схемы (<see cref="FemLoadSources.Build"/>).</param>
    /// <param name="current">Текущий источник стадии: окно открывается на его вкладке с выделенной строкой.</param>
    public FemLoadSourcePickerVM(IReadOnlyList<FemLoadSource> sources, FemLoadSource? current)
    {
        _combinations = sources.Where(s => s.Kind == FemLoadSourceKind.Combination).ToList();
        _cases = sources.Where(s => s.Kind == FemLoadSourceKind.LoadCase).ToList();
        TypeFilters = FemCombinationTypes.All
            .Select(t => new FemLoadSourceTypeFilter(this, t, _combinations.Count(c => c.TypeBadge == t))).ToList();
        foreach (var f in TypeFilters) f.Init(f.IsAvailable && !s_typesOff.Contains(f.Type));
        // Тип текущей комбинации виден всегда, даже если в прошлый раз был отключён.
        if (current?.Kind == FemLoadSourceKind.Combination && TypeFilters.FirstOrDefault(f => f.Type == current.TypeBadge) is { } own)
            own.Init(true);

        _selectedTab = current?.Kind switch
        {
            FemLoadSourceKind.Combination => CombinationsTab,
            FemLoadSourceKind.LoadCase => CasesTab,
            _ => _combinations.Count == 0 ? CasesTab : s_lastTab ?? CombinationsTab,
        };
        if (current?.Kind == FemLoadSourceKind.Combination) _selectedCombination = _combinations.FirstOrDefault(c => ReferenceEquals(c, current));
        if (current?.Kind == FemLoadSourceKind.LoadCase) _selectedCase = _cases.FirstOrDefault(c => ReferenceEquals(c, current));
        ApplyCombinationFilter();
        ApplyCaseFilter();
    }

    public IReadOnlyList<FemLoadSourceTypeFilter> TypeFilters { get; }

    public bool HasCombinations => _combinations.Count > 0;

    public string CombinationsHeader => string.Format(Loc.S("FemLoadSourcePickerCombinationsTab"), _combinations.Count);
    public string CasesHeader => string.Format(Loc.S("FemLoadSourcePickerCasesTab"), _cases.Count);

    int _selectedTab;
    public int SelectedTab
    {
        get => _selectedTab;
        set
        {
            if (_selectedTab == value || value < 0) return;
            _selectedTab = value;
            s_lastTab = value;
            OnPropertyChanged();
            OnSelectionChanged();
        }
    }

    // ── Комбинации ─────────────────────────────────────────────────────

    string _combinationSearch = "";
    public string CombinationSearch
    {
        get => _combinationSearch;
        set
        {
            if (_combinationSearch == value) return;
            _combinationSearch = value ?? "";
            OnPropertyChanged();
            ApplyCombinationFilter();
        }
    }

    public IReadOnlyList<FemLoadSource> VisibleCombinations { get; private set; } = [];

    /// <summary>«Показано 42 из 291».</summary>
    public string CombinationsShownText => string.Format(Loc.S("FemLoadSourcePickerShown"), VisibleCombinations.Count, _combinations.Count);

    FemLoadSource? _selectedCombination;
    public FemLoadSource? SelectedCombination
    {
        get => _selectedCombination;
        set
        {
            // Сетка сбрасывает выделение в null, когда строка уходит под фильтр, — выбор сохраняется до другой строки.
            if (value == null || ReferenceEquals(_selectedCombination, value)) return;
            _selectedCombination = value;
            OnPropertyChanged();
            OnSelectionChanged();
        }
    }

    /// <summary>Включить все типы, где есть комбинации.</summary>
    public void AllTypes()
    {
        foreach (var f in TypeFilters) f.Init(f.IsAvailable);
        s_typesOff.Clear();
        ApplyCombinationFilter();
    }

    internal void OnTypeFilterChanged(FemLoadSourceTypeFilter filter)
    {
        if (filter.IsOn) s_typesOff.Remove(filter.Type);
        else s_typesOff.Add(filter.Type);
        ApplyCombinationFilter();
    }

    void ApplyCombinationFilter()
    {
        var on = TypeFilters.Where(f => f.IsOn).Select(f => f.Type).ToHashSet();
        VisibleCombinations = _combinations.Where(c => on.Contains(c.TypeBadge) && Matches(c, _combinationSearch)).ToList();
        OnPropertyChanged(nameof(VisibleCombinations));
        OnPropertyChanged(nameof(CombinationsShownText));
        OnPropertyChanged(nameof(SelectedCombination));
        OnSelectionChanged();
    }

    // ── Загружения ─────────────────────────────────────────────────────

    string _caseSearch = "";
    public string CaseSearch
    {
        get => _caseSearch;
        set
        {
            if (_caseSearch == value) return;
            _caseSearch = value ?? "";
            OnPropertyChanged();
            ApplyCaseFilter();
        }
    }

    public IReadOnlyList<FemLoadSource> VisibleCases { get; private set; } = [];

    public string CasesShownText => string.Format(Loc.S("FemLoadSourcePickerShown"), VisibleCases.Count, _cases.Count);

    FemLoadSource? _selectedCase;
    public FemLoadSource? SelectedCase
    {
        get => _selectedCase;
        set
        {
            if (value == null || ReferenceEquals(_selectedCase, value)) return;
            _selectedCase = value;
            OnPropertyChanged();
            OnSelectionChanged();
        }
    }

    void ApplyCaseFilter()
    {
        VisibleCases = _cases.Where(c => Matches(c, _caseSearch)).ToList();
        OnPropertyChanged(nameof(VisibleCases));
        OnPropertyChanged(nameof(CasesShownText));
        OnPropertyChanged(nameof(SelectedCase));
        OnSelectionChanged();
    }

    // ── Выбор ──────────────────────────────────────────────────────────

    /// <summary>Выбранная строка активной вкладки, если она видна при текущем фильтре.</summary>
    public FemLoadSource? Selected => _selectedTab == CombinationsTab
        ? _selectedCombination is { } c && VisibleCombinations.Contains(c) ? c : null
        : _selectedCase is { } lc && VisibleCases.Contains(lc) ? lc : null;

    public bool CanAccept => Selected != null;

    /// <summary>«Состав «Огибающая C 018»:» над расшифровкой; без выбора — подсказка.</summary>
    public string SelectedCaption => Selected is { } s
        ? string.Format(Loc.S("FemLoadSourcePickerComposition"), s.Tag)
        : Loc.S("FemLoadSourcePickerNothingSelected");

    public IReadOnlyList<FemLoadSourceTerm> SelectedTerms => Selected?.Terms ?? [];

    /// <summary>Если под фильтром осталась ровно одна строка — выбрать её (Enter в поиске сразу принимает).</summary>
    public void SelectSingleVisible()
    {
        if (_selectedTab == CombinationsTab && VisibleCombinations.Count == 1) SelectedCombination = VisibleCombinations[0];
        if (_selectedTab == CasesTab && VisibleCases.Count == 1) SelectedCase = VisibleCases[0];
    }

    void OnSelectionChanged()
    {
        OnPropertyChanged(nameof(Selected));
        OnPropertyChanged(nameof(CanAccept));
        OnPropertyChanged(nameof(SelectedCaption));
        OnPropertyChanged(nameof(SelectedTerms));
    }

    /// <summary>
    /// Поиск: все слова запроса должны встретиться. Слово вида «ЗГ3» — точный код загружения слагаемого (не ЗГ31),
    /// иначе — подстрока имени, формулы или имени загружения слагаемого без учёта регистра.
    /// </summary>
    internal static bool Matches(FemLoadSource source, string query)
    {
        foreach (var word in query.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))
        {
            if (IsCaseCode(word) ? !source.Codes.Contains(word)
                : !source.SearchText.Contains(word.ToLowerInvariant(), StringComparison.Ordinal))
                return false;
        }
        return true;
    }

    static bool IsCaseCode(string word)
    {
        string prefix = Loc.S("FemCombinationsCaseCode");
        return word.Length > prefix.Length && word.StartsWith(prefix, StringComparison.CurrentCultureIgnoreCase)
            && word.AsSpan(prefix.Length).IndexOfAnyExceptInRange('0', '9') < 0;
    }
}
