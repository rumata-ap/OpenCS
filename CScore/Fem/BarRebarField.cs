using CScore.PlateRebar;

namespace CScore.Fem;

/// <summary>Компонента армирования стержневого КЭ для мозаики и эпюры (обозначения ЛИРЫ).</summary>
public enum BarRebarComponent
{
    /// <summary>Вся продольная арматура сечения (в подборе ЛИРЫ — AU1..AU4 + AS1..AS4), см².</summary>
    LongitudinalSum,
    /// <summary>Ряд у нижней грани сечения (заданное армирование), см².</summary>
    Bottom,
    /// <summary>Ряд у верхней грани сечения (заданное армирование), см².</summary>
    Top,
    /// <summary>Угловая арматура AU1, см².</summary>
    Au1,
    /// <summary>Угловая арматура AU2, см².</summary>
    Au2,
    /// <summary>Угловая арматура AU3, см².</summary>
    Au3,
    /// <summary>Угловая арматура AU4, см².</summary>
    Au4,
    /// <summary>Арматура у грани AS1, см².</summary>
    As1,
    /// <summary>Арматура у грани AS2, см².</summary>
    As2,
    /// <summary>Арматура у грани AS3, см².</summary>
    As3,
    /// <summary>Арматура у грани AS4, см².</summary>
    As4,
    /// <summary>Поперечная арматура ASW1 (в единицах программы-источника).</summary>
    Asw1,
    /// <summary>Поперечная арматура ASW2 (в единицах программы-источника).</summary>
    Asw2,
    /// <summary>Процент армирования.</summary>
    Percent,
}

/// <summary>
/// Источник армирования стержневых КЭ по тегу КЭ (подобранное или заданное армирование, прочитанное
/// из файла программы-источника): значение на КЭ и значения по сечениям вдоль КЭ.
/// </summary>
public interface IBarRebarFieldSource
{
    /// <summary>Есть ли в источнике данная компонента.</summary>
    bool Supports(BarRebarComponent component);

    /// <summary>Значение компоненты на КЭ — огибающая по его сечениям.</summary>
    PlateRebarValue Get(string elemTag, BarRebarComponent component);

    /// <summary>Значения компоненты в сечениях КЭ, от начального узла к конечному; пусто — КЭ в источнике нет.</summary>
    IReadOnlyList<PlateRebarValue> GetSections(string elemTag, BarRebarComponent component);
}

/// <summary>Разность «заданное − требуемое» по стержням: минус — дефицит, плюс — запас.
/// Сечения берутся у требуемого; заданное — по тем же сечениям, если их у него столько же (участки SCAD),
/// иначе (постоянное по длине КЭ, ТЗА ЛИРЫ) — значение на КЭ.</summary>
public sealed class BarRebarDifferenceSource(IBarRebarFieldSource assigned, IBarRebarFieldSource required)
    : IBarRebarFieldSource
{
    /// <inheritdoc/>
    public bool Supports(BarRebarComponent component) => assigned.Supports(component) && required.Supports(component);

    /// <inheritdoc/>
    public PlateRebarValue Get(string elemTag, BarRebarComponent component) =>
        Subtract(assigned.Get(elemTag, component), required.Get(elemTag, component));

    /// <inheritdoc/>
    public IReadOnlyList<PlateRebarValue> GetSections(string elemTag, BarRebarComponent component)
    {
        var need = required.GetSections(elemTag, component);
        var given = assigned.GetSections(elemTag, component);
        if (given.Count == need.Count)
            return need.Select((r, k) => Subtract(given[k], r)).ToList();
        var a = assigned.Get(elemTag, component);
        return need.Select(r => Subtract(a, r)).ToList();
    }

    static PlateRebarValue Subtract(PlateRebarValue a, PlateRebarValue r)
    {
        if (r.FailureCode != null) return r;
        if (a.FailureCode != null) return a;
        return a.Value is { } av && r.Value is { } rv ? PlateRebarValue.Of(av - rv) : PlateRebarValue.Missing;
    }
}
