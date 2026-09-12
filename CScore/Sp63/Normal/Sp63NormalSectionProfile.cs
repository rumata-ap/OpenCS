namespace CScore.Sp63.Normal;

/// <summary>Эффективный слой точечных стержней на одной координате.</summary>
/// <param name="Coordinate">Исходная координата слоя по выбранной оси, м.</param>
/// <param name="Area">Суммарная площадь стержней слоя, м².</param>
/// <param name="Rs">Сопротивление арматуры растяжению, кПа.</param>
/// <param name="Rsc">Сопротивление арматуры сжатию, кПа.</param>
/// <param name="Bars">Исходные точечные стержни слоя.</param>
public sealed record Sp63NormalRebarLayer(
    double Coordinate,
    double Area,
    double Rs,
    double Rsc,
    IReadOnlyList<(double X, double Y, double Area, double Diameter)> Bars);

/// <summary>Профиль прямоугольного сечения для одноосной проверки.</summary>
/// <param name="B">Размер сечения поперёк плоскости изгиба, м.</param>
/// <param name="Height">Размер сечения в плоскости изгиба, м.</param>
/// <param name="H0">Рабочая высота от сжатой грани до растянутой арматуры, м.</param>
/// <param name="APrime">Расстояние от сжатой грани до сжатой арматуры, м.</param>
/// <param name="TensionLayer">Эффективный растянутый слой.</param>
/// <param name="CompressionLayer">Эффективный сжатый слой.</param>
/// <param name="TotalRebarArea">Суммарная площадь точечной арматуры, м².</param>
/// <param name="IsSymmetric">Сходятся ли сопротивления двух крайних слоёв.</param>
/// <param name="SymmetryRelativeDifference">Относительное различие сопротивлений.</param>
/// <param name="PrecomputedXWithoutCompressionRebar">x при исключённой сжатой арматуре, м.</param>
public sealed record Sp63NormalSectionProfile(
    double B,
    double Height,
    double H0,
    double APrime,
    Sp63NormalRebarLayer TensionLayer,
    Sp63NormalRebarLayer CompressionLayer,
    double TotalRebarArea,
    bool IsSymmetric,
    double SymmetryRelativeDifference,
    double PrecomputedXWithoutCompressionRebar);

/// <summary>Результат классификации профиля нормального сечения.</summary>
/// <param name="IsApplicable">Доступна ли базовая геометрия.</param>
/// <param name="Geometry">Габариты прямоугольника.</param>
/// <param name="Messages">Сообщения о применимости.</param>
public sealed record Sp63NormalSectionClassification(
    bool IsApplicable,
    Sp63RectangularGeometry? Geometry,
    IReadOnlyList<Sp63NormalMessage> Messages);

/// <summary>Результат извлечения профиля арматуры.</summary>
/// <param name="Profile">Профиль или <see langword="null"/>.</param>
/// <param name="Messages">Причины, по которым профиль недоступен.</param>
public sealed record Sp63NormalProfileAnalysis(
    Sp63NormalSectionProfile? Profile,
    IReadOnlyList<Sp63NormalMessage> Messages);
