using System.Globalization;
using CScore.Fem;
using CScore.Fem.Editing;
using OpenCS.Utilites;

namespace OpenCS.ViewModels;

/// <summary>Комбинация в списке вкладки «Комбинации»: номер, имя, тип и формула «1,35·ЗГ1 + 1,05·ЗГ2».</summary>
public sealed class FemCombinationRow(FemCombinationsTabVM owner, FemLoadDefinition definition, int number) : FemLoadsRow
{
    public FemLoadDefinition Definition { get; } = definition;
    public override object Source => Definition;
    public int Number { get; } = number;
    public string Tag => Definition.Tag;
    public string Type => Definition.CombinationType ?? "";
    public string Formula => owner.Formula(Definition);
}

/// <summary>Загружение в списках вкладки «Комбинации» (схемы — слева, в комбинации — справа с коэффициентом).</summary>
public sealed class FemCombinationCaseRow(FemLoadCase loadCase, int number) : FemLoadsRow
{
    public FemLoadCase LoadCase { get; } = loadCase;
    public override object Source => LoadCase;
    public int Number { get; } = number;
    /// <summary>Короткое обозначение «ЗГ1» — как в формуле комбинации.</summary>
    public string Code => FemCombinationsTabVM.CaseCode(Number);
    public string Tag => LoadCase.Tag;
    public string Sp20Type => LoadCase.Sp20Type;
}

/// <summary>Слагаемое комбинации: коэффициент правится в ячейке (одна отмена на правку).</summary>
public sealed class FemCombinationTermRow(FemCombinationsTabVM owner, int index, FemLoadTerm term, FemLoadCase? loadCase, int number)
    : FemLoadsRow
{
    /// <summary>Позиция слагаемого в выражении комбинации.</summary>
    public int Index { get; } = index;
    public FemLoadTerm Term { get; internal set; } = term;
    public FemLoadCase? LoadCase { get; } = loadCase;
    public override object Source => Term;
    public int Number { get; } = number;
    public string Code => number > 0 ? FemCombinationsTabVM.CaseCode(number) : "";
    public string Tag => LoadCase?.Tag ?? $"#{Term.LoadCaseId}";
    public string Sp20Type => LoadCase?.Sp20Type ?? "";

    public string Coefficient
    {
        get => FemLoadsFormat.Number(Term.Coefficient);
        set => owner.SetCoefficient(this, value);
    }
}

/// <summary>
/// Вкладка «Комбинации» редактора схемы по мотивам RFEM: слева — комбинации с формулами, справа — имя, тип, комментарий
/// выбранной и два списка: загружения схемы и загружения в комбинации с коэффициентами; перенос стрелками. Все правки —
/// командами редактора (отмена, сохранение схемы пользователем).
/// </summary>
public sealed class FemCombinationsTabVM : ViewModelBase
{
    public FemSchemaEditorVM Editor { get; }

    public FemCombinationsTabVM(FemSchemaEditorVM editor)
    {
        Editor = editor;
        editor.PropertyChanged += (_, e) => { if (e.PropertyName is nameof(FemSchemaEditorVM.Session)) Reload(); };
        Reload();
    }

    /// <summary>Типы комбинаций: расчётные полные и длительные, нормативные полные и длительные.</summary>
    public static IReadOnlyList<string> CombinationTypes { get; } = ["C", "CL", "N", "NL"];

    /// <summary>Частые коэффициенты для списка (можно ввести любой).</summary>
    public static IReadOnlyList<string> CommonCoefficients { get; } =
        new[] { 1.0, 1.05, 1.1, 1.2, 1.3, 1.35, 1.4, 1.5, 0.9, 0.95, 0.8, 0.7, 0.5 }
            .Select(FemLoadsFormat.Number).ToList();

    /// <summary>Фильтр загружений схемы: пусто — все, иначе вид нагрузки СП 20.</summary>
    public IReadOnlyList<FemLoadsChoice> CaseFilters { get; private set; } = [];

    public static string CaseCode(int number) => Loc.S("FemCombinationsCaseCode") + number.ToString(CultureInfo.InvariantCulture);

    IReadOnlyList<FemCombinationRow> _rows = [];
    public IReadOnlyList<FemCombinationRow> Rows { get => _rows; private set { _rows = value; OnPropertyChanged(); } }
    IReadOnlyList<FemCombinationCaseRow> _availableCases = [];
    public IReadOnlyList<FemCombinationCaseRow> AvailableCases { get => _availableCases; private set { _availableCases = value; OnPropertyChanged(); } }
    IReadOnlyList<FemCombinationTermRow> _terms = [];
    public IReadOnlyList<FemCombinationTermRow> Terms { get => _terms; private set { _terms = value; OnPropertyChanged(); } }

    FemCombinationRow? _selected;
    /// <summary>Выбранная комбинация — её свойства и состав справа.</summary>
    public FemCombinationRow? Selected
    {
        get => _selected;
        set
        {
            if (ReferenceEquals(_selected, value)) return;
            _selected = value;
            OnPropertyChanged();
            ReloadSelected(rebuild: true);
        }
    }

    public FemLoadDefinition? SelectedDefinition => Selected?.Definition;
    public bool HasSelection => Selected != null;

    string _filter = "";
    public string Filter
    {
        get => _filter;
        set { _filter = value ?? ""; OnPropertyChanged(); ReloadSelected(rebuild: false); }
    }

    string _coefficientText = FemLoadsFormat.Number(1);
    /// <summary>Коэффициент для добавляемых слагаемых и кнопки «Применить к выделенным».</summary>
    public string CoefficientText { get => _coefficientText; set { _coefficientText = value; OnPropertyChanged(); } }

    string? _status;
    public string? Status { get => _status; private set { _status = value; OnPropertyChanged(); } }

    public string NumberText => Selected?.Number.ToString(CultureInfo.CurrentCulture) ?? "";
    public string TermsHeader => Selected is { } row
        ? string.Format(Loc.S("FemCombinationsTermsHeader"), row.Tag)
        : Loc.S("FemCombinationsTermsHeaderEmpty");

    /// <summary>Имя выбранной комбинации; правка — по выходу из поля (уникальность проверяется).</summary>
    public string TagText
    {
        get => SelectedDefinition?.Tag ?? "";
        set
        {
            if (SelectedDefinition is not { } d) return;
            string tag = (value ?? "").Trim();
            if (tag == d.Tag) return;
            if (tag.Length == 0 || Editor.Session.LoadDefinitions.Any(x => !ReferenceEquals(x, d) && x.Tag == tag))
            {
                Status = Loc.S("FemCombinationTagTaken");
                OnPropertyChanged();
                return;
            }
            Edit(d, v => v.Tag = tag);
        }
    }

    /// <summary>Тип выбранной комбинации (C, CL, N, NL или свой); пусто — без типа.</summary>
    public string TypeText
    {
        get => SelectedDefinition?.CombinationType ?? "";
        set
        {
            if (SelectedDefinition is not { } d) return;
            string? type = string.IsNullOrWhiteSpace(value) ? null : value.Trim();
            if (type == d.CombinationType) return;
            Edit(d, v => v.CombinationType = type, expression => new FemLoadExpression
            {
                Mode = expression.Mode, LoadCaseIds = expression.LoadCaseIds, Terms = expression.Terms, CombinationType = type,
            });
        }
    }

    /// <summary>Комментарий (описание) выбранной комбинации.</summary>
    public string CommentText
    {
        get => SelectedDefinition?.Description ?? "";
        set
        {
            if (SelectedDefinition is not { } d) return;
            string? text = string.IsNullOrWhiteSpace(value) ? null : value.Trim();
            if (text == d.Description) return;
            Edit(d, v => v.Description = text);
        }
    }

    // ── Чтение ─────────────────────────────────────────────────────────────

    /// <summary>Перечитывает списки из сессии; при том же составе строки только обновляют значения.</summary>
    public void Reload()
    {
        var session = Editor.Session;
        var previous = Selected?.Definition;
        Rows = FemLoadsTabVM.Merge(Rows, session.LoadDefinitions, (d, i) => new FemCombinationRow(this, d, i + 1));
        CaseFilters = BuildFilters(session.LoadCases);
        OnPropertyChanged(nameof(CaseFilters));
        // Выбранная комбинация сохраняется; удалённая (или первый показ) — первая в списке.
        var keep = Rows.FirstOrDefault(r => ReferenceEquals(r.Definition, previous)) ?? Rows.FirstOrDefault();
        bool changed = !ReferenceEquals(keep, _selected);
        _selected = keep;
        if (changed) OnPropertyChanged(nameof(Selected));
        ReloadSelected(rebuild: changed);
    }

    void ReloadSelected(bool rebuild)
    {
        var session = Editor.Session;
        var numbers = CaseNumbers();
        var terms = SelectedDefinition?.GetExpression().Terms ?? [];
        var used = terms.Select(t => t.LoadCaseId).ToHashSet();
        var available = session.LoadCases
            .Where(lc => !used.Contains(lc.Id) && (_filter.Length == 0 || lc.Sp20Type == _filter)).ToList();
        AvailableCases = FemLoadsTabVM.Merge(rebuild ? [] : AvailableCases, available, (lc, _) => new FemCombinationCaseRow(lc, numbers[lc.Id]));

        // Тот же состав загружений — строки сохраняются (правка коэффициента в ячейке не пересобирает таблицу).
        var byId = session.LoadCases.GroupBy(lc => lc.Id).ToDictionary(g => g.Key, g => g.First());
        if (!rebuild && Terms.Count == terms.Count && Terms.Select(r => r.Term.LoadCaseId).SequenceEqual(terms.Select(t => t.LoadCaseId)))
        {
            for (int i = 0; i < terms.Count; i++) { Terms[i].Term = terms[i]; Terms[i].Refresh(); }
        }
        else
        {
            Terms = terms.Select((t, i) => new FemCombinationTermRow(this, i, t, byId.GetValueOrDefault(t.LoadCaseId),
                numbers.GetValueOrDefault(t.LoadCaseId))).ToList();
        }
        foreach (var name in new[] { nameof(HasSelection), nameof(NumberText), nameof(TagText), nameof(TypeText),
                     nameof(CommentText), nameof(TermsHeader) })
            OnPropertyChanged(name);
    }

    Dictionary<int, int> CaseNumbers()
    {
        var numbers = new Dictionary<int, int>();
        var cases = Editor.Session.LoadCases;
        for (int i = 0; i < cases.Count; i++) numbers.TryAdd(cases[i].Id, i + 1);
        return numbers;
    }

    /// <summary>Формула комбинации: «1,35·ЗГ1 + 1,05·ЗГ2»; отрицательный коэффициент — через минус.</summary>
    public string Formula(FemLoadDefinition definition)
    {
        var numbers = CaseNumbers();
        var parts = definition.GetExpression().Terms.Select((t, i) =>
        {
            string code = numbers.TryGetValue(t.LoadCaseId, out var n) ? CaseCode(n) : $"#{t.LoadCaseId}";
            string value = FemLoadsFormat.Number(Math.Abs(t.Coefficient)) + "·" + code;
            return i == 0 ? (t.Coefficient < 0 ? "−" : "") + value : (t.Coefficient < 0 ? " − " : " + ") + value;
        });
        return string.Concat(parts);
    }

    IReadOnlyList<FemLoadsChoice> BuildFilters(IReadOnlyList<FemLoadCase> cases)
    {
        var list = new List<FemLoadsChoice> { new("", string.Format(Loc.S("FemCombinationsFilterAll"), cases.Count)) };
        foreach (var kind in FemLoadsTabVM.Sp20Types)
        {
            int count = cases.Count(lc => lc.Sp20Type == kind.Key);
            if (count > 0) list.Add(new(kind.Key, $"{kind.Label} ({count})"));
        }
        return list;
    }

    // ── Правка ─────────────────────────────────────────────────────────────

    /// <summary>Новая комбинация (пустая, тип C) становится выбранной.</summary>
    public void AddCombination()
    {
        var definition = new FemLoadDefinition
        {
            SchemaId = Editor.Session.Schema.Id, Tag = UniqueTag(Loc.S("FemLoadDefinitionDefaultTag")), SourceKind = "manual",
            CombinationType = "C",
        };
        definition.SetExpression(new FemLoadExpression { Mode = FemLoadExpressionMode.Sum, CombinationType = "C" });
        Add(definition);
    }

    /// <summary>Копия выбранной комбинации с тем же составом.</summary>
    public void CopyCombination()
    {
        if (SelectedDefinition is not { } source) return;
        Add(new FemLoadDefinition
        {
            SchemaId = source.SchemaId, Tag = UniqueTag(source.Tag), Description = source.Description, SourceKind = "manual",
            CombinationType = source.CombinationType, ExpressionJson = source.ExpressionJson,
        });
    }

    void Add(FemLoadDefinition definition)
    {
        Execute(new AddLoadDefinitionCommand(definition));
        Selected = Rows.FirstOrDefault(r => ReferenceEquals(r.Definition, definition));
    }

    string UniqueTag(string prefix)
    {
        var tags = Editor.Session.LoadDefinitions.Select(d => d.Tag).ToHashSet(StringComparer.Ordinal);
        if (!tags.Contains(prefix)) return prefix;
        int index = 2;
        while (tags.Contains($"{prefix} {index}")) index++;
        return $"{prefix} {index}";
    }

    /// <summary>Удаляет комбинации одной отменой.</summary>
    public void DeleteCombinations(IEnumerable<FemCombinationRow> rows)
    {
        var commands = rows.Select(r => (IFemEditCommand)new DeleteLoadDefinitionCommand(r.Definition)).ToList();
        if (commands.Count == 0) return;
        Execute(Group(commands));
    }

    /// <summary>Основные сочетания по СП 20 (тот же генератор, что в дереве).</summary>
    public void GenerateSp20()
    {
        Status = null;
        Editor.GenerateSp20LoadDefinitions("fundamental");
    }

    /// <summary>Загружения — в комбинацию с коэффициентом из поля (одна отмена).</summary>
    public void AddTerms(IEnumerable<FemCombinationCaseRow> rows)
    {
        if (SelectedDefinition is not { } d) return;
        var cases = rows.Select(r => r.LoadCase).ToList();
        if (cases.Count == 0) return;
        if (!TryCoefficient(out var k)) return;
        EditTerms(d, terms => terms.AddRange(cases.Where(lc => terms.All(t => t.LoadCaseId != lc.Id))
            .Select(lc => new FemLoadTerm { LoadCaseId = lc.Id, Coefficient = k })));
    }

    public void AddAllTerms() => AddTerms(AvailableCases);

    /// <summary>Убирает слагаемые из комбинации (одна отмена).</summary>
    public void RemoveTerms(IEnumerable<FemCombinationTermRow> rows)
    {
        if (SelectedDefinition is not { } d) return;
        var indexes = rows.Select(r => r.Index).ToHashSet();
        if (indexes.Count == 0) return;
        EditTerms(d, terms =>
        {
            for (int i = terms.Count - 1; i >= 0; i--)
                if (indexes.Contains(i)) terms.RemoveAt(i);
        });
    }

    public void RemoveAllTerms() => RemoveTerms(Terms);

    /// <summary>Коэффициент из поля — выделенным слагаемым (одна отмена).</summary>
    public void ApplyCoefficient(IEnumerable<FemCombinationTermRow> rows) => SetCoefficients(rows, null);

    /// <summary>Коэффициент 1,0 выделенным слагаемым.</summary>
    public void ResetCoefficient(IEnumerable<FemCombinationTermRow> rows) => SetCoefficients(rows, 1);

    void SetCoefficients(IEnumerable<FemCombinationTermRow> rows, double? fixedValue)
    {
        if (SelectedDefinition is not { } d) return;
        var indexes = rows.Select(r => r.Index).ToHashSet();
        if (indexes.Count == 0) return;
        double k = 1;
        if (fixedValue is { } v) k = v;
        else if (!TryCoefficient(out k)) return;
        EditTerms(d, terms =>
        {
            for (int i = 0; i < terms.Count; i++)
                if (indexes.Contains(i)) terms[i] = new FemLoadTerm { LoadCaseId = terms[i].LoadCaseId, Coefficient = k };
        });
    }

    internal void SetCoefficient(FemCombinationTermRow row, string? text)
    {
        if (SelectedDefinition is not { } d) return;
        if (!TryNumber(text, out var k)) { row.Refresh(); return; }
        if (k == row.Term.Coefficient) return;
        EditTerms(d, terms =>
        {
            if (row.Index < terms.Count) terms[row.Index] = new FemLoadTerm { LoadCaseId = terms[row.Index].LoadCaseId, Coefficient = k };
        });
    }

    bool TryCoefficient(out double k) => TryNumber(CoefficientText, out k);

    bool TryNumber(string? text, out double k)
    {
        k = 0;
        if (text != null && Pars.ParseAny(text.Trim(), out k) && double.IsFinite(k) && k != 0) return true;
        Status = string.Format(Loc.S("FemCombinationsBadCoefficient"), text);
        return false;
    }

    void EditTerms(FemLoadDefinition d, Action<List<FemLoadTerm>> change)
    {
        var expression = d.GetExpression();
        var terms = expression.Terms.ToList();
        change(terms);
        Edit(d, _ => { }, _ => new FemLoadExpression
        {
            // Sum со слагаемыми: Single берёт LoadCaseIds с 1,0, а пустой Sum — LoadCaseIds, поэтому их не переносим.
            Mode = FemLoadExpressionMode.Sum, Terms = terms,
            CombinationType = d.CombinationType ?? expression.CombinationType,
        });
    }

    void Edit(FemLoadDefinition d, Action<FemLoadDefinition> apply, Func<FemLoadExpression, FemLoadExpression>? expression = null)
    {
        var values = new FemLoadDefinition
        {
            Tag = d.Tag, Description = d.Description, SourceKind = d.SourceKind, CombinationType = d.CombinationType,
            ExpressionJson = expression == null ? d.ExpressionJson : expression(d.GetExpression()).ToJson(),
        };
        apply(values);
        if (values.Tag == d.Tag && values.Description == d.Description && values.CombinationType == d.CombinationType
            && values.ExpressionJson == d.ExpressionJson) return;
        Execute(new EditLoadDefinitionCommand(d, values));
    }

    void Execute(IFemEditCommand command)
    {
        Status = null;
        Editor.Session.Execute(command);
        Editor.RefreshCollections();
    }

    static IFemEditCommand Group(IReadOnlyList<IFemEditCommand> commands) =>
        commands.Count == 1 ? commands[0] : new CompositeFemCommand(commands.ToList());
}
