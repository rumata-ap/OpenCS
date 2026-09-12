using CScore.Sp63;

namespace CScore.Sp63Shear;

/// <summary>Результат проверки автоматически поддерживаемой геометрии сечения.</summary>
/// <param name="IsSupported">Соответствует ли бетонное тело сплошному осевому прямоугольнику.</param>
/// <param name="Reasons">Причины, по которым автоматическая нормативная геометрия недоступна.</param>
public sealed record RectangularShearSectionClassification(
    bool IsSupported, IReadOnlyList<string> Reasons);

/// <summary>
/// Выделяет узкое автоматически проверяемое подмножество: одну сплошную прямоугольную
/// бетонную область без отверстий. Это ограничение OpenCS, а не утверждение о границах СП.
/// </summary>
public static class RectangularShearSectionClassifier
{
    /// <summary>Геометрический допуск, м.</summary>
    public const double GeometryTolerance = Sp63RectangularGeometryPolicy.GeometryTolerance;

    /// <summary>Проверяет бетонную геометрию сечения.</summary>
    public static RectangularShearSectionClassification Classify(CrossSection section)
    {
        ArgumentNullException.ThrowIfNull(section);
        var result = Sp63RectangularGeometryPolicy.Classify(section);
        return new RectangularShearSectionClassification(result.IsApplicable, result.Reasons);
    }
}
