namespace CScore.Import;

/// <summary>Классы бетона и продольной арматуры стержневого КЭ по данным схемы-источника.</summary>
/// <param name="Concrete">Класс бетона («B25»).</param>
/// <param name="Rebar">Класс продольной арматуры («A500»).</param>
public readonly record struct ImportedBarRcClasses(string Concrete, string Rebar)
{
    /// <summary>
    /// Классы КЭ по номеру: у ЛИРЫ — из подбора (*.asp), у SCAD — из ЖБ-группы КЭ.
    /// Null — данных нет (файл не приложен, группы не прочитаны, КЭ в них нет).
    /// </summary>
    /// <param name="scad">Схема из SCAD (иначе — ЛИРА).</param>
    /// <param name="asp">Подбор ЛИРЫ; null — не приложен.</param>
    /// <param name="groups">ЖБ-группы SCAD; null — не прочитаны.</param>
    public static Func<int, ImportedBarRcClasses?> Lookup(bool scad, LiraAspFile? asp, ScadConcreteGroupIndex? groups)
    {
        if (scad)
            return num => groups?.Find(num) is { } g ? new(g.ConcreteClass, g.LongitudinalRebarClass) : null;
        return num => asp != null && asp.Bars.TryGetValue(num, out var bar) ? new(bar.ConcreteClass, bar.RebarClass) : null;
    }

    /// <summary>У схемы есть данные о классах: у ЛИРЫ приложен подбор, у SCAD прочитаны ЖБ-группы.</summary>
    public static bool Available(bool scad, LiraAspFile? asp, ScadConcreteGroupIndex? groups) =>
        scad ? groups != null : asp != null;
}
