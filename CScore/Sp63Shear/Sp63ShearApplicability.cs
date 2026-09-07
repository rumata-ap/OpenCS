namespace CScore.Sp63Shear;

/// <summary>Режим области применимости задачи наклонных сечений.</summary>
public static class Sp63ShearApplicabilityMode
{
    /// <summary>Автоматически проверяемое нормативное подмножество.</summary>
    public const string StandardAuto = "standard_auto";
    /// <summary>Нормативные формулы с эквивалентной геометрией пользователя.</summary>
    public const string StandardEquivalent = "standard_equivalent";
    /// <summary>Исследовательская оценка без нормативного вердикта.</summary>
    public const string Research = "research";
}

/// <summary>Входные данные квалификатора применимости одной плоскости сдвига.</summary>
public sealed record Sp63ShearApplicabilityInput(
    string Mode, bool IsRectangle, double? ManualB, bool EquivalentConfirmed,
    double OrthogonalShear, double Torsion, bool HasManualPhiN);

/// <summary>Результат квалификации области применимости.</summary>
public sealed record Sp63ShearApplicabilityResult(
    string RequestedMode, string EffectiveMode, string Status,
    bool HasNormativeVerdict, IReadOnlyList<string> Reasons);

/// <summary>Определяет, вправе ли задача дать нормативный вердикт наклонного сечения.</summary>
public static class Sp63ShearApplicability
{
    /// <summary>Допуск нуля усилий, кН и кН·м.</summary>
    public const double ForceZeroTolerance = 1e-6;

    /// <summary>Квалифицирует один расчётный случай.</summary>
    public static Sp63ShearApplicabilityResult Evaluate(Sp63ShearApplicabilityInput input)
    {
        ArgumentNullException.ThrowIfNull(input);
        string effectiveMode = NormalizeMode(input.Mode);
        var reasons = new List<string>();
        if (!IsKnownMode(input.Mode))
            reasons.Add("Неизвестный режим применимости — принят исследовательский режим.");

        if (effectiveMode == Sp63ShearApplicabilityMode.Research)
            return new Sp63ShearApplicabilityResult(
                input.Mode, effectiveMode, "research", false, reasons);

        if (Math.Abs(input.OrthogonalShear) > ForceZeroTolerance)
            reasons.Add("Одновременное действие поперечных сил в двух плоскостях не имеет нормативного правила взаимодействия.");
        if (Math.Abs(input.Torsion) > ForceZeroTolerance)
            reasons.Add("Кручение требует отдельной модели пространственных сечений и не учитывается этой проверкой.");
        if (input.HasManualPhiN)
            reasons.Add("Ручное значение φn допускается только в исследовательском режиме.");

        if (effectiveMode == Sp63ShearApplicabilityMode.StandardAuto)
        {
            if (!input.IsRectangle)
                reasons.Add("Автоматический нормативный режим доступен только для сплошного прямоугольного бетонного сечения.");
            if (input.ManualB is not null)
                reasons.Add("Ручная расчётная ширина b требует режима эквивалентного сечения или исследовательской оценки.");
        }
        if (effectiveMode == Sp63ShearApplicabilityMode.StandardEquivalent)
        {
            if (!input.EquivalentConfirmed)
                reasons.Add("Для эквивалентного сечения требуется подтверждение пользователя.");
            if (input.ManualB is not double b || !double.IsFinite(b) || b <= 0.0)
                reasons.Add("Для эквивалентного сечения задайте положительную расчётную ширину b.");
        }

        bool normative = reasons.Count == 0;
        return new Sp63ShearApplicabilityResult(
            input.Mode, effectiveMode, normative ? "ok" : "not_applicable", normative, reasons);
    }

    /// <summary>Нормализует неизвестные режимы к безопасной исследовательской оценке.</summary>
    public static string NormalizeMode(string? mode) => IsKnownMode(mode)
        ? mode!
        : Sp63ShearApplicabilityMode.Research;

    /// <summary>Проверяет известное строковое значение режима.</summary>
    public static bool IsKnownMode(string? mode) => mode is
        Sp63ShearApplicabilityMode.StandardAuto or
        Sp63ShearApplicabilityMode.StandardEquivalent or
        Sp63ShearApplicabilityMode.Research;
}
