using System.ComponentModel;
using System.Globalization;
using CScore.Fem;
using CScore.Fem.Combinations;
using OpenCS.Utilites;

namespace OpenCS.ViewModels;

/// <summary>Строка состава комбинации: «1,1 × 1. Собственный вес».</summary>
public sealed record FemLoadCaseDetail(string Text);

/// <summary>
/// Загружение в дереве схемы (как в RFEM — без нагрузок: они рисуются на схеме): номер, имя, вид по СП 20 (иконка),
/// сводка параметров серым, активное — жирным. Щелчок делает загружение активным, правка параметров — «Изменить…».
/// </summary>
public sealed class FemLoadCaseTreeItem : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    public FemSchema Schema { get; }
    public FemLoadCase LoadCase { get; }
    /// <summary>Порядковый номер в схеме, с 1.</summary>
    public int Number { get; }
    /// <summary>«1. Собственный вес».</summary>
    public string Label => $"{Number}. {LoadCase.Tag}";
    /// <summary>Вид по СП 20: permanent, long_term, short_term, accidental (иконка).</summary>
    public string Sp20Type => LoadCase.Sp20Type;
    /// <summary>Происхождение серым: «ЛИРА», «SCAD»; у ручных — null.</summary>
    public string? OriginText { get; }
    /// <summary>Заданные параметры серым: «γf 1,1/0,9; ψ1 0,95; СВ ×1,1»; null — все по умолчанию.</summary>
    public string? ParametersText { get; }

    bool _isActive;
    public bool IsActive
    {
        get => _isActive;
        internal set
        {
            if (_isActive == value) return;
            _isActive = value;
            PropertyChanged?.Invoke(this, new(nameof(IsActive)));
        }
    }

    internal FemLoadCaseTreeItem(FemSchema schema, FemLoadCase loadCase, int number)
    {
        Schema = schema;
        LoadCase = loadCase;
        Number = number;
        OriginText = loadCase.Origin == FemLoadOrigin.Manual ? null : FemMeshNodeRow.DescribeOrigin(loadCase.Origin);
        ParametersText = FemLoadTree.Parameters(loadCase);
    }
}

/// <summary>Ветка «Загружения (N)»: у своих схем видна и пустой (команда «Создать»), у импортированных — при N > 0.</summary>
public sealed class FemLoadCasesSubNode : FemSubNode, INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    public FemSchema Schema { get; }
    public FemTreeItems<FemLoadCaseTreeItem> Items { get; } = [];
    public int Count => Items.Count;
    public override bool IsVisible => Items.Count > 0 || Schema.SourceType is "internal" or "submodel";

    internal FemLoadCasesSubNode(FemSchema schema) => Schema = schema;

    /// <summary>Перечитывает загружения и активное загружение из БД.</summary>
    internal void Reload(DatabaseService db) => Reset(db.GetFemLoadCases(Schema.Id), db.GetFemActiveLoadCaseTag(Schema.Id));

    /// <summary>Загружения по списку (из БД или несохранённой сессии редактора) и запомненному активному.</summary>
    internal void Reset(IReadOnlyList<FemLoadCase> cases, string? activeTag)
    {
        Items.Reset(cases.Select((lc, i) => new FemLoadCaseTreeItem(Schema, lc, i + 1)));
        SetActive(FemLoadTree.ActiveTag(cases, activeTag));
        PropertyChanged?.Invoke(this, new(nameof(Count)));
        PropertyChanged?.Invoke(this, new(nameof(IsVisible)));
    }

    /// <summary>Отмечает активное загружение (по имени) жирным.</summary>
    public void SetActive(string? tag)
    {
        foreach (var item in Items) item.IsActive = tag != null && item.LoadCase.Tag == tag;
    }
}

/// <summary>Комбинация (определение загружений) в дереве: имя, тип, состав «1,1 × 1. Собственный вес».</summary>
public sealed class FemCombinationTreeItem
{
    public FemSchema Schema { get; }
    public FemLoadDefinition Definition { get; }
    public string Label => Definition.Tag;
    /// <summary>Тип комбинации серым (C, CL, N, NL…); null — не задан.</summary>
    public string? TypeText { get; }
    public IReadOnlyList<FemLoadCaseDetail> Terms { get; }

    internal FemCombinationTreeItem(FemSchema schema, FemLoadDefinition definition, IReadOnlyList<FemLoadCase> loadCases)
    {
        Schema = schema;
        Definition = definition;
        var expression = definition.GetExpression();
        TypeText = expression.CombinationType ?? definition.CombinationType;
        Terms = FemLoadTree.Terms(expression, loadCases);
    }
}

/// <summary>Подузел типа комбинаций в ветке «Комбинации»: «C — ПС1, полные (12)».</summary>
public sealed class FemCombinationGroupNode : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>Тип C/CL/N/NL/A.</summary>
    public string Type { get; }
    public string Title => Loc.S("FemCombinationGroup" + Type);
    public FemTreeItems<FemCombinationTreeItem> Items { get; } = [];
    public int Count => Items.Count;

    internal FemCombinationGroupNode(string type) => Type = type;

    internal void Reset(IEnumerable<FemCombinationTreeItem> items)
    {
        Items.Reset(items);
        PropertyChanged?.Invoke(this, new(nameof(Count)));
    }
}

/// <summary>
/// Ветка «Комбинации (N)»: у своих схем видна и пустой, у импортированных — при N > 0. Внутри — подузлы по типам
/// (C, CL, N, NL, A), только непустые; узлы типов сохраняются между перезагрузками (раскрытие не сбрасывается).
/// </summary>
public sealed class FemCombinationsSubNode : FemSubNode, INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    public FemSchema Schema { get; }
    /// <summary>Все комбинации подряд.</summary>
    public FemTreeItems<FemCombinationTreeItem> Items { get; } = [];
    /// <summary>Непустые подузлы типов в порядке C, CL, N, NL, A.</summary>
    public FemTreeItems<FemCombinationGroupNode> Groups { get; } = [];
    public int Count => Items.Count;
    public override bool IsVisible => Items.Count > 0 || Schema.SourceType is "internal" or "submodel";

    readonly Dictionary<string, FemCombinationGroupNode> _groups =
        FemCombinationTypes.All.ToDictionary(t => t, t => new FemCombinationGroupNode(t));

    internal FemCombinationsSubNode(FemSchema schema) => Schema = schema;

    internal void Reload(DatabaseService db) => Reset(db.GetFemLoadDefinitions(Schema.Id), db.GetFemLoadCases(Schema.Id));

    /// <summary>Комбинации по списку (из БД или несохранённой сессии редактора).</summary>
    internal void Reset(IReadOnlyList<FemLoadDefinition> definitions, IReadOnlyList<FemLoadCase> cases)
    {
        Items.Reset(definitions.Select(d => new FemCombinationTreeItem(Schema, d, cases)));
        var byType = Items.ToLookup(i => FemCombinationTypes.Normalize(i.TypeText));
        foreach (var (type, node) in _groups) node.Reset(byType[type]);
        var visible = FemCombinationTypes.All.Select(t => _groups[t]).Where(g => g.Count > 0).ToList();
        if (!visible.SequenceEqual(Groups)) Groups.Reset(visible);
        PropertyChanged?.Invoke(this, new(nameof(Count)));
        PropertyChanged?.Invoke(this, new(nameof(IsVisible)));
    }
}

/// <summary>Подписи загружений и комбинаций дерева.</summary>
public static class FemLoadTree
{
    /// <summary>Заданные параметры загружения: γf, группа, собственный вес; null — ничего не задано.</summary>
    public static string? Parameters(FemLoadCase loadCase)
    {
        var c = CultureInfo.CurrentCulture;
        var parts = new List<string>();
        if (loadCase.GammaFUnfav is { } gu)
            parts.Add(loadCase.GammaFFav is { } gf ? string.Format(c, "γf {0:0.###}/{1:0.###}", gu, gf) : string.Format(c, "γf {0:0.###}", gu));
        else if (loadCase.GammaFFav is { } gfOnly) parts.Add(string.Format(c, "γf −/{0:0.###}", gfOnly));
        if (!string.IsNullOrWhiteSpace(loadCase.Sp20Group)) parts.Add($"[{loadCase.Sp20Group}]");
        if (loadCase.SelfWeightFactor is { } k && k != 0)
            parts.Add(string.Format(c, Loc.S("FemLoadTreeSelfWeight"), k));
        return parts.Count == 0 ? null : string.Join("; ", parts);
    }

    /// <summary>Состав комбинации: «1,1 × 1. Собственный вес»; режим «все загружения» — по одному с коэффициентом 1.</summary>
    public static IReadOnlyList<FemLoadCaseDetail> Terms(FemLoadExpression expression, IReadOnlyList<FemLoadCase> loadCases)
    {
        var number = loadCases.Select((lc, i) => (lc.Id, i + 1)).ToDictionary(x => x.Id, x => x.Item2);
        IReadOnlyList<(FemLoadCase LoadCase, double Coefficient)> terms;
        try { terms = FemLoadExpressionResolver.Terms(expression, loadCases); }
        catch (Exception ex) when (ex is InvalidOperationException or ArgumentException or NotSupportedException) { return [new(ex.Message)]; }
        return terms.Select(t => new FemLoadCaseDetail(string.Format(CultureInfo.CurrentCulture, "{0:0.###} × {1}. {2}",
            t.Coefficient, number.GetValueOrDefault(t.LoadCase.Id), t.LoadCase.Tag))).ToList();
    }

    /// <summary>Активное загружение: запомненное, если оно есть в схеме, иначе первое; null — загружений нет.</summary>
    public static string? ActiveTag(IReadOnlyList<FemLoadCase> loadCases, string? remembered) =>
        remembered != null && loadCases.Any(lc => lc.Tag == remembered) ? remembered : loadCases.FirstOrDefault()?.Tag;
}
