using CScore.Combinations;
using CScore.Fem;
using CScore.Fem.Combinations;
using OpenCS.Tasks;
using OpenCS.Utilites;

namespace OpenCS.ViewModels;

/// <summary>
/// Строка списка загружений линейного CSfea: загружение схемы или стадия-комбинация прежней постановки
/// (<see cref="IsLegacy"/>, до выбора загружений галочками).
/// </summary>
public sealed class FemLinearCaseRow(FemLinearCasesVM owner, string label, string expressionJson) : ViewModelBase
{
    public string Label { get; } = label;

    /// <summary>Выражение нагрузки стадии (JSON <see cref="FemLoadExpression"/>).</summary>
    public string ExpressionJson { get; } = expressionJson;

    /// <summary>Загружение схемы; null — стадия-комбинация прежней постановки.</summary>
    public FemLoadCase? LoadCase { get; init; }

    /// <summary>Вид загружения по СП 20: G/L/Q/A; у комбинации — пусто.</summary>
    public string TypeLetter { get; init; } = "";

    /// <summary>γf загружения (своё или по умолчанию из настроек).</summary>
    public string GammaText { get; init; } = "";

    /// <summary>Доля длительной части (только у кратковременных); иначе «—».</summary>
    public string ShareText { get; init; } = "";

    /// <summary>Загружение добавлено в схему после создания постановки — по умолчанию не отмечено.</summary>
    public bool IsNew { get; init; }

    public bool IsLegacy => LoadCase == null;

    /// <summary>Тип по СП 20 для иконки (как на вкладке «Комбинации»); у комбинации — пусто.</summary>
    public string Sp20Type => LoadCase?.Sp20Type ?? "";

    /// <summary>Пометка строки: «новое» или «комбинация (прежняя версия)».</summary>
    public string Note => IsLegacy ? Loc.S("FemCsfeaLinearCaseLegacy") : IsNew ? Loc.S("FemCsfeaLinearCaseNew") : "";

    bool _isChecked;
    public bool IsChecked
    {
        get => _isChecked;
        set
        {
            if (_isChecked == value) return;
            _isChecked = value;
            OnPropertyChanged();
            owner.OnCheckedChanged();
        }
    }
}

/// <summary>
/// Выбор загружений линейного расчёта CSfea галочками (вместо строк-стадий): каждое отмеченное — отдельная стадия
/// «загружение целиком» (λ = 1), усилия затем комбинируются. Новая постановка — все отмечены; правка — отмечены
/// загружения её стадий, добавленные позже — не отмечены с пометкой «новое», стадии-комбинации прежних версий — в
/// конце списка отмеченными (новые такие не добавляются).
/// </summary>
public sealed class FemLinearCasesVM : ViewModelBase
{
    /// <param name="existingStages">Стадии правимой линейной постановки CSfea; null — новая постановка.</param>
    public FemLinearCasesVM(IReadOnlyList<FemLoadCase> loadCases, Sp20GammaDefaults defaults,
        IReadOnlyList<FemAnalysisStage>? existingStages)
    {
        var checkedIds = new HashSet<int>();
        var legacy = new List<FemAnalysisStage>();
        foreach (var stage in existingStages ?? [])
        {
            if (SingleCaseId(stage.LoadExpressionJson) is { } id && loadCases.Any(lc => lc.Id == id)) checkedIds.Add(id);
            else legacy.Add(stage);
        }

        var rows = new List<FemLinearCaseRow>();
        for (int i = 0; i < loadCases.Count; i++)
        {
            var lc = loadCases[i];
            var type = FemCombinationCoefficients.LoadType(lc);
            bool isNew = existingStages != null && !checkedIds.Contains(lc.Id);
            var row = new FemLinearCaseRow(this, $"{i + 1} {lc.Tag}", CaseExpression(lc.Id).ToJson())
            {
                LoadCase = lc,
                TypeLetter = TypeLetter(type),
                GammaText = FemLoadsFormat.Number(FemCombinationCoefficients.GammaF(lc, defaults)),
                ShareText = type == NormLoadType.ShortTerm && lc.LongTermShare is double share ? FemLoadsFormat.Number(share) : "—",
                IsNew = isNew,
            };
            row.IsChecked = !isNew;
            rows.Add(row);
        }
        foreach (var stage in legacy)
        {
            var row = new FemLinearCaseRow(this, stage.Tag, stage.LoadExpressionJson);
            row.IsChecked = true;
            rows.Add(row);
        }
        Rows = rows;
        _initialized = true;
        ApplyFilter();
    }

    readonly bool _initialized;

    /// <summary>Все строки: загружения в порядке схемы, затем стадии-комбинации прежней версии.</summary>
    public IReadOnlyList<FemLinearCaseRow> Rows { get; }

    /// <summary>Строки, подходящие под поиск.</summary>
    public IReadOnlyList<FemLinearCaseRow> VisibleRows { get; private set; } = [];

    string _filterText = "";
    /// <summary>Поиск по имени загружения (без учёта регистра).</summary>
    public string FilterText
    {
        get => _filterText;
        set
        {
            if (_filterText == value) return;
            _filterText = value ?? "";
            OnPropertyChanged();
            ApplyFilter();
        }
    }

    void ApplyFilter()
    {
        string text = _filterText.Trim();
        VisibleRows = text.Length == 0 ? Rows
            : Rows.Where(r => r.Label.Contains(text, StringComparison.CurrentCultureIgnoreCase)).ToList();
        OnPropertyChanged(nameof(VisibleRows));
    }

    /// <summary>Отметить видимые строки (с учётом поиска).</summary>
    public void CheckAll() { foreach (var row in VisibleRows) row.IsChecked = true; }

    /// <summary>Снять отметку с видимых строк (с учётом поиска).</summary>
    public void CheckNone() { foreach (var row in VisibleRows) row.IsChecked = false; }

    public int CheckedCount => Rows.Count(r => r.IsChecked);

    /// <summary>«Отмечено 7 из 8».</summary>
    public string CountText => string.Format(Loc.S("FemCsfeaLinearCasesCount"), CheckedCount, Rows.Count);

    internal void OnCheckedChanged()
    {
        if (!_initialized) return;
        OnPropertyChanged(nameof(CheckedCount));
        OnPropertyChanged(nameof(CountText));
    }

    /// <summary>Стадии постановки: отмеченные строки по порядку, загружение целиком (шаг и предел λ = 1).</summary>
    public List<FemAnalysisStage> BuildStages() => Rows.Where(r => r.IsChecked).Select(r => new FemAnalysisStage
    {
        Tag = r.LoadCase?.Tag ?? r.Label,
        LoadExpressionJson = r.ExpressionJson,
        LoadFactorStep = 1.0,
        MaxLoadFactor = 1.0,
    }).ToList();

    /// <summary>Выражение «одно загружение» — как у источника-загружения прежнего выпадающего списка.</summary>
    public static FemLoadExpression CaseExpression(int loadCaseId) =>
        new() { Mode = FemLoadExpressionMode.Single, LoadCaseIds = [loadCaseId] };

    /// <summary>Id загружения стадии «одно загружение целиком»; иначе (комбинация, формула) — null.</summary>
    static int? SingleCaseId(string json)
    {
        try
        {
            var e = FemLoadExpression.Parse(json);
            return e.Mode == FemLoadExpressionMode.Single && e.LoadCaseIds.Count == 1 && e.Terms.Count == 0
                ? e.LoadCaseIds[0] : null;
        }
        catch (System.Text.Json.JsonException) { return null; }
    }

    /// <summary>Буква вида нагрузки СП 20: G/L/Q/A.</summary>
    public static string TypeLetter(NormLoadType type) => type switch
    {
        NormLoadType.Permanent => "G",
        NormLoadType.LongTerm => "L",
        NormLoadType.Accidental => "A",
        _ => "Q",
    };
}
