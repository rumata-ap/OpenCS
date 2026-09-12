using CScore.Sp63;

namespace CScore.Sp63.Normal;

/// <summary>Переводит общую геометрическую policy в контракт нормального расчёта.</summary>
public static class Sp63NormalSectionClassifier
{
    /// <summary>Проверяет базовую прямоугольную геометрию сечения.</summary>
    public static Sp63NormalSectionClassification Classify(CrossSection section)
    {
        ArgumentNullException.ThrowIfNull(section);
        var geometry = Sp63RectangularGeometryPolicy.Classify(section);
        if (geometry.IsApplicable)
            return new Sp63NormalSectionClassification(true, geometry.Geometry, []);

        var messages = geometry.Reasons
            .Select(reason => new Sp63NormalMessage(
                "unsupported_geometry",
                Sp63NormalMessageKind.Applicability,
                "геометрическая policy OpenCS",
                "Sp63Normal_GeometryNotSupported"))
            .ToList();
        return new Sp63NormalSectionClassification(false, null, messages);
    }
}
