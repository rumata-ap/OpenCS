using System.Globalization;
using CScore.Sp63.Normal;

namespace CScore.Sp63;

/// <summary>
/// Случайный эксцентриситет п. 8.1.7 СП 63.13330.2018 (сверено по HTML-копии нормы, <c>obsidian\norms\raw</c>):
/// e_a не менее 1/600 длины элемента или расстояния между его сечениями, закреплёнными от смещения, 1/30 высоты
/// сечения и 10 мм. Значение — <see cref="Sp63MemberContext.AccidentalEccentricity"/>; здесь — ход вычисления для
/// отчёта несовершенства-погиби (п. 8.1.7 — эксцентриситет проверки сечения; как погибь в расчёте второго порядка он
/// складывается с e₀ от расчёта, то есть для статически неопределимых конструкций — в запас против max(e₀, e_a)).
/// </summary>
public static class Sp63AccidentalEccentricity
{
    /// <summary>e_a, м: <paramref name="length"/> — длина (расстояние между закреплениями), м; <paramref name="h"/> — высота сечения, м.</summary>
    public static double Ea(double length, double h) => Sp63MemberContext.AccidentalEccentricity(length, h);

    /// <summary>Ход вычисления: «e_a = max(l/600 = …; h/30 = …; 10 мм) = … мм (п. 8.1.7 СП 63)»; возвращает e_a, м.</summary>
    public static double Ea(double length, double h, out string describe)
    {
        double e = Ea(length, h);
        describe = string.Create(CultureInfo.CurrentCulture,
            $"СП 63 п. 8.1.7: e_a = max(l/600 = {length:0.###}/600 = {length / 600 * 1000:0.##} мм; h/30 = {h * 1000:0.#}/30 = {h / 30 * 1000:0.##} мм; 10 мм) = {e * 1000:0.##} мм");
        return e;
    }
}
