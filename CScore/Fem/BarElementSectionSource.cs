using System.Text.Json;
using System.Text.Json.Serialization;

namespace CScore.Fem;

/// <summary>Сечение стержневого КЭ, выданное источником.</summary>
/// <param name="Section">Расчётное сечение; null — у КЭ нет сечения из этого источника.</param>
/// <param name="Label">Подпись сечения в результате («Б-1», «ASP», «ТЗА 16 20»).</param>
/// <param name="Reason">Почему сечения нет.</param>
public sealed record BarElementSection(CrossSection? Section, string Label, string? Reason)
{
    /// <summary>У КЭ нет сечения из этого источника.</summary>
    public static BarElementSection Missing(string reason, string label = "") => new(null, label, reason);
}

/// <summary>
/// Источник расчётного сечения стержневого КЭ для проверки по КЭ: сечение проекта либо сечение,
/// собранное по данным программы-источника схемы (размеры, подобранная или заданная арматура).
/// </summary>
public interface IBarElementSectionSource
{
    /// <summary>Ключ источника (<see cref="FemCheckRebarSource"/>); пусто — сечение цели без выбора источников.</summary>
    string Key { get; }

    /// <summary>Сечение зависит от номера сечения КЭ (армирование задано по сечениям вдоль КЭ).</summary>
    bool PerSection => false;

    /// <summary>Причина, по которой у КЭ нет сечения; null — сечение есть. Сечение при этом не строится.</summary>
    string? MissingReason(FemCheckScopeElement element);

    /// <summary>Сечение КЭ. Вызывается последовательно, до параллельной части проверки.</summary>
    /// <param name="sectionNum">Номер сечения КЭ строки усилий; null — строка без номера сечения
    /// или нужна оценка по КЭ в целом.</param>
    BarElementSection Resolve(FemCheckScopeElement element, int? sectionNum);
}

/// <summary>
/// Сечение проекта: своё сечение КЭ → сечение его конструктивного элемента → сечение цели.
/// </summary>
/// <param name="key">Ключ источника: <see cref="FemCheckRebarSource.Section"/> либо пусто.</param>
/// <param name="sectionById">Расчётное сечение по id.</param>
/// <param name="targetSection">Сечение цели.</param>
public sealed class ProjectBarSectionSource(string key, Func<int, CrossSection?> sectionById, CrossSection? targetSection)
    : IBarElementSectionSource
{
    const string NoSection = "нет расчётного сечения";

    /// <inheritdoc/>
    public string Key => key;

    /// <inheritdoc/>
    public string? MissingReason(FemCheckScopeElement element) => Find(element) == null ? NoSection : null;

    /// <inheritdoc/>
    public BarElementSection Resolve(FemCheckScopeElement element, int? sectionNum) =>
        Find(element) is { } s ? new BarElementSection(s, s.Tag, null) : BarElementSection.Missing(NoSection);

    /// <summary>Сечение КЭ; null — не назначено ни КЭ, ни элементу, ни цели.</summary>
    public CrossSection? Find(FemCheckScopeElement element)
    {
        if (element.Element.CrossSectionId is int own && sectionById(own) is { } ownSection) return ownSection;
        if (element.Member?.CrossSectionId is int mid && sectionById(mid) is { } memberSection) return memberSection;
        return targetSection;
    }
}

/// <summary>Источник, данных для которого у схемы нет (не приложен файл армирования): у всех КЭ — одна причина.</summary>
public sealed class UnavailableBarSectionSource(string key, string reason) : IBarElementSectionSource
{
    /// <inheritdoc/>
    public string Key => key;

    /// <inheritdoc/>
    public string? MissingReason(FemCheckScopeElement element) => reason;

    /// <inheritdoc/>
    public BarElementSection Resolve(FemCheckScopeElement element, int? sectionNum) => BarElementSection.Missing(reason);
}

/// <summary>Параметры проверки стержней по КЭ, хранимые в <see cref="FemCheck.ParamsJson"/>.</summary>
public sealed record BarCheckParams
{
    /// <summary>
    /// Источники сечения КЭ в порядке расчёта (<see cref="FemCheckRebarSource"/>). Пусто — сечение проекта
    /// без выбора источников (как до появления источников у стержней).
    /// </summary>
    public string[] RebarSources { get; init; } = [];

    /// <summary>Учёт продольного изгиба (η, п. 8.1.15); null — не учитывается.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public FemEtaParams? Eta { get; init; }

    /// <summary>Длина элемента l по сетке схемы для КЭ, м — проставляет проверка по КЭ; null — не определена.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public double? ElementLengthM { get; init; }

    /// <summary>ψ строки в плоскости Mx из длительного набора — проставляет проверка по КЭ; null — из <see cref="Eta"/>.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public double? RowPsiX { get; init; }

    /// <summary>ψ строки в плоскости My из длительного набора; null — из <see cref="Eta"/>.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public double? RowPsiY { get; init; }

    /// <summary>η учитывается.</summary>
    [JsonIgnore]
    public bool EtaEnabled => Eta is { Enabled: true };

    /// <summary>Длина элемента l: вручную, иначе по сетке; null — не определена.</summary>
    public double? ResolveLengthM() => Eta is { HasManualLength: true } e ? e.LengthM : ElementLengthM;

    public string ToJson() => JsonSerializer.Serialize(this);

    public static BarCheckParams Parse(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return new();
        try { return JsonSerializer.Deserialize<BarCheckParams>(json) ?? new(); }
        catch (JsonException) { return new(); }
    }
}
