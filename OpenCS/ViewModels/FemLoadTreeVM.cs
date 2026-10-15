using System.ComponentModel;
using System.Globalization;
using CScore.Fem;
using CScore.Fem.Combinations;
using OpenCS.Utilites;

namespace OpenCS.ViewModels;

/// <summary>Строка состава загружения в дереве: «Узловые — 3», «Собственный вес ×1,1».</summary>
public sealed record FemLoadCaseDetail(string Text);

/// <summary>
/// Загружение в дереве схемы: номер, имя, вид по СП 20 (иконка), активное — жирным; дочерние — нагрузки по видам
/// (только непустые) и собственный вес. Щелчок делает загружение активным (как в RFEM).
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
    public IReadOnlyList<FemLoadCaseDetail> Details { get; }
    /// <summary>Нагрузок всех видов.</summary>
    public int LoadCount { get; }

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

    internal FemLoadCaseTreeItem(FemSchema schema, FemLoadCase loadCase, int number, FemLoadCaseCounts counts)
    {
        Schema = schema;
        LoadCase = loadCase;
        Number = number;
        OriginText = loadCase.Origin == FemLoadOrigin.Manual ? null : FemMeshNodeRow.DescribeOrigin(loadCase.Origin);
        LoadCount = counts.Total;
        Details = FemLoadTree.Details(loadCase, counts);
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

    /// <summary>Перечитывает загружения, счётчики нагрузок и активное загружение из БД.</summary>
    internal void Reload(DatabaseService db)
    {
        var counts = db.GetFemLoadCaseCounts(Schema.Id);
        var cases = db.GetFemLoadCases(Schema.Id);
        Items.Reset(cases.Select((lc, i) => new FemLoadCaseTreeItem(Schema, lc, i + 1, counts.GetValueOrDefault(lc.Id))));
        SetActive(FemLoadTree.ActiveTag(cases, db.GetFemActiveLoadCaseTag(Schema.Id)));
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

/// <summary>Ветка «Комбинации (N)»: у своих схем видна и пустой, у импортированных — при N > 0.</summary>
public sealed class FemCombinationsSubNode : FemSubNode, INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    public FemSchema Schema { get; }
    public FemTreeItems<FemCombinationTreeItem> Items { get; } = [];
    public int Count => Items.Count;
    public override bool IsVisible => Items.Count > 0 || Schema.SourceType is "internal" or "submodel";

    internal FemCombinationsSubNode(FemSchema schema) => Schema = schema;

    internal void Reload(DatabaseService db)
    {
        var cases = db.GetFemLoadCases(Schema.Id);
        Items.Reset(db.GetFemLoadDefinitions(Schema.Id).Select(d => new FemCombinationTreeItem(Schema, d, cases)));
        PropertyChanged?.Invoke(this, new(nameof(Count)));
        PropertyChanged?.Invoke(this, new(nameof(IsVisible)));
    }
}

/// <summary>Подписи загружений и комбинаций дерева.</summary>
public static class FemLoadTree
{
    /// <summary>Нагрузки по видам (только непустые) и собственный вес.</summary>
    public static IReadOnlyList<FemLoadCaseDetail> Details(FemLoadCase loadCase, FemLoadCaseCounts counts)
    {
        var list = new List<FemLoadCaseDetail>();
        void Add(string key, int n) { if (n > 0) list.Add(new($"{Loc.S(key)} — {n}")); }
        Add("FemLoadTreeNodal", counts.Node);
        Add("FemLoadTreeMember", counts.Member);
        Add("FemLoadTreeArea", counts.Element);
        Add("FemLoadTreeKinematic", counts.Kinematic);
        Add("FemLoadTreeMeshNodal", counts.MeshNode);
        if (loadCase.SelfWeightFactor is { } k && k != 0)
            list.Add(new(string.Format(CultureInfo.CurrentCulture, Loc.S("FemLoadTreeSelfWeight"), k)));
        return list;
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
