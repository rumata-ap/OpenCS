using CScore;

namespace CScore.Fire;

/// <summary>Результат определения высоты сжатой зоны для температурной кривизны.</summary>
/// <param name="XtM">Высота сжатой зоны, м.</param>
/// <param name="XiR">Относительная высота сжатой зоны.</param>
/// <param name="XiCapped">Применено ограничение x_t ≤ ξ_R·h_0.</param>
/// <param name="Method">Идентификатор применённого метода.</param>
/// <param name="Fallback">Признак резервного метода по равновесию фибрового сечения.</param>
/// <param name="Supported">Признак применимости выбранного метода.</param>
/// <param name="UnsupportedReasonKey">Ключ ресурса с причиной неприменимости.</param>
public sealed record FireCompressionZoneResult(
    double XtM,
    double XiR,
    bool XiCapped,
    string Method,
    bool Fallback,
    bool Supported,
    string? UnsupportedReasonKey);

/// <summary>
/// Методы определения высоты сжатой зоны в расчёте температурной кривизны по СП 468.
/// </summary>
public static class FireCompressionZone
{
    /// <summary>Количество итераций бисекции равновесия бетонной зоны.</summary>
    public const int EquilibriumIterations = 60;

    /// <summary>
    /// Вычисляет относительную высоту сжатой зоны по формуле (8.10) СП 468.
    /// </summary>
    /// <param name="rsMPa">Расчётное сопротивление арматуры, МПа.</param>
    /// <param name="esMPa">Модуль упругости арматуры, МПа.</param>
    /// <param name="gammaSt">Температурный коэффициент арматуры γ_st.</param>
    /// <param name="gammaStE">Коэффициент снижения модуля арматуры γ_st,E.</param>
    /// <param name="epsB2">Предельная деформация сжатого бетона ε_b2.</param>
    public static double XiR(
        double rsMPa, double esMPa, double gammaSt, double gammaStE, double epsB2)
    {
        RequirePositive(esMPa, nameof(esMPa));
        RequirePositive(gammaSt, nameof(gammaSt));
        RequirePositive(gammaStE, nameof(gammaStE));
        RequirePositive(epsB2, nameof(epsB2));

        double epsSEl = gammaSt * rsMPa / (gammaStE * esMPa);
        return 0.8 / (1.0 + epsSEl / epsB2);
    }

    /// <summary>
    /// Определяет высоту сжатой зоны по формуле (8.11) СП 468 для прямоугольной,
    /// тавровой или двутавровой части сечения.
    /// </summary>
    /// <param name="rsntNPerM2">Расчётное сопротивление растянутой арматуры, Па.</param>
    /// <param name="asM2">Площадь растянутой арматуры, м².</param>
    /// <param name="rbntNPerM2">Расчётное сопротивление бетона сжатию, Па.</param>
    /// <param name="bM">Ширина бетонной зоны, м.</param>
    /// <param name="h0M">Рабочая высота сечения, м.</param>
    /// <param name="xiR">Предельная относительная высота сжатой зоны.</param>
    public static FireCompressionZoneResult ByFormula811(
        double rsntNPerM2,
        double asM2,
        double rbntNPerM2,
        double bM,
        double h0M,
        double xiR)
    {
        const string method = "sp468_8_11";
        if (!double.IsFinite(bM) || bM <= 0.0 ||
            !double.IsFinite(rbntNPerM2) || rbntNPerM2 <= 0.0 ||
            !double.IsFinite(h0M) || h0M <= 0.0)
        {
            return new FireCompressionZoneResult(
                0.0, xiR, false, method, false, false, "FireCurvature_WidthUndefined");
        }

        double xt = rsntNPerM2 * asM2 / (rbntNPerM2 * bM);
        double limit = xiR * h0M;
        bool capped = xt > limit;
        if (capped) xt = limit;

        return new FireCompressionZoneResult(xt, xiR, capped, method, false, true, null);
    }

    /// <summary>
    /// Определяет высоту сжатой зоны из равновесия бетонного фибрового сечения.
    /// Проекция с координатой s = axisX·X + axisY·Y считается направлением от
    /// растянутой грани к сжатой, поэтому сжатая зона отсекается от максимального s.
    /// </summary>
    /// <param name="fiber">Огневое фибровое сечение.</param>
    /// <param name="axisX">X-компонента единичной оси высоты.</param>
    /// <param name="axisY">Y-компонента единичной оси высоты.</param>
    /// <param name="tensionForceN">Равнодействующая растянутой арматуры, Н.</param>
    /// <param name="h0M">Рабочая высота сечения, м.</param>
    /// <param name="xiR">Предельная относительная высота сжатой зоны.</param>
    public static FireCompressionZoneResult ByFiberEquilibrium(
        FireFiberSection fiber,
        double axisX,
        double axisY,
        double tensionForceN,
        double h0M,
        double xiR)
    {
        ArgumentNullException.ThrowIfNull(fiber);
        const string method = "fiber_equilibrium";
        if (!double.IsFinite(h0M) || h0M <= 0.0)
        {
            return new FireCompressionZoneResult(
                0.0, xiR, false, method, true, false, "FireCurvature_H0Undefined");
        }

        if (!double.IsFinite(tensionForceN) || tensionForceN < 0.0)
            throw new ArgumentOutOfRangeException(nameof(tensionForceN));

        double axisLength = Math.Sqrt(axisX * axisX + axisY * axisY);
        if (!double.IsFinite(axisLength) || axisLength <= 1e-12)
            throw new ArgumentException("Ось проекции должна иметь ненулевую длину.", nameof(axisX));
        axisX /= axisLength;
        axisY /= axisLength;

        if (fiber.ConcreteElements.Count == 0)
            return new FireCompressionZoneResult(
                0.0, xiR, false, method, true, false, "FireCurvature_ConcreteUndefined");

        double sMin = double.PositiveInfinity;
        double sMax = double.NegativeInfinity;
        foreach (var c in fiber.ConcreteElements)
        {
            double s = axisX * c.Cx + axisY * c.Cy;
            sMin = Math.Min(sMin, s);
            sMax = Math.Max(sMax, s);
        }

        double height = sMax - sMin;
        if (!double.IsFinite(height) || height <= 0.0)
            return new FireCompressionZoneResult(
                0.0, xiR, false, method, true, false, "FireCurvature_H0Undefined");

        double lo = 0.0;
        double hi = height;
        for (int i = 0; i < EquilibriumIterations; i++)
        {
            double depth = 0.5 * (lo + hi);
            double compression = CompressionResultant(fiber, axisX, axisY, sMax, depth);
            if (compression < tensionForceN) lo = depth;
            else hi = depth;
        }

        double xt = 0.5 * (lo + hi);
        double limit = xiR * h0M;
        bool capped = xt > limit;
        if (capped) xt = limit;

        return new FireCompressionZoneResult(xt, xiR, capped, method, true, true, null);
    }

    static double CompressionResultant(
        FireFiberSection fiber, double axisX, double axisY, double sMax, double depth)
    {
        double boundary = sMax - depth;
        double result = 0.0;
        foreach (var c in fiber.ConcreteElements)
        {
            double s = axisX * c.Cx + axisY * c.Cy;
            if (s + 1e-12 < boundary) continue;

            var chars = c.Material.GetChars(CalcType.C) ?? c.Material.MaterialChars.FirstOrDefault();
            if (chars is null) continue;

            double strength = ConcreteStrengthNPerM2(chars);
            if (strength > 0.0 && double.IsFinite(strength))
                result += c.Area * c.GammaBt * strength;
        }
        return result;
    }

    /// <summary>Прочность бетона в единицах Па; Fc в доменной модели хранится в кПа.</summary>
    public static double ConcreteStrengthNPerM2(MaterialChars chars)
    {
        ArgumentNullException.ThrowIfNull(chars);
        return Math.Abs(chars.Fc) * 1_000.0;
    }

    static void RequirePositive(double value, string name)
    {
        if (!double.IsFinite(value) || value <= 0.0)
            throw new ArgumentOutOfRangeException(name);
    }
}
