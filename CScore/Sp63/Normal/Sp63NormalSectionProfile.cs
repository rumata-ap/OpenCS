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
    IReadOnlyList<(double X, double Y, double Area, double Diameter)> Bars)
{
    /// <summary>Слой создан одним расчётным волокном, а не физическими стержнями.</summary>
    public bool IsIdealized { get; init; }
}

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
    double PrecomputedXWithoutCompressionRebar)
{
    /// <summary>
    /// Координаты всех уровней точечной арматуры по оси высоты (по возрастанию), м —
    /// включая промежуточные, не вошедшие в эффективные слои. Пусто — неизвестно.
    /// </summary>
    public IReadOnlyList<double> LayerCoordinates { get; init; } = [];
}

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

/// <summary>Профиль таврового/двутаврового сечения для одноосной проверки.</summary>
/// <param name="Bw">Ширина стенки, м.</param>
/// <param name="H">Полная высота сечения, м.</param>
/// <param name="H0">Рабочая высота от сжатой грани, м.</param>
/// <param name="APrime">Расстояние от сжатой грани до сжатой арматуры, м.</param>
/// <param name="TensionLayer">Эффективный растянутый слой.</param>
/// <param name="CompressionLayer">Эффективный сжатый слой.</param>
/// <param name="TotalRebarArea">Суммарная площадь точечной арматуры, м².</param>
/// <param name="CompressionFlangeActualWidth">Фактическая ширина сжатой полки, м (0 — полки нет).</param>
/// <param name="CompressionFlangeThickness">Толщина сжатой полки, м (0 — полки нет).</param>
/// <param name="IsSymmetric">Сходятся ли сопротивления двух крайних слоёв.</param>
/// <param name="SymmetryRelativeDifference">Относительное различие сопротивлений.</param>
public sealed record Sp63TeeSectionProfile(
    double Bw,
    double H,
    double H0,
    double APrime,
    Sp63NormalRebarLayer TensionLayer,
    Sp63NormalRebarLayer CompressionLayer,
    double TotalRebarArea,
    double CompressionFlangeActualWidth,
    double CompressionFlangeThickness,
    bool IsSymmetric,
    double SymmetryRelativeDifference);

/// <summary>Результат извлечения профиля арматуры таврового сечения.</summary>
/// <param name="Profile">Профиль или <see langword="null"/>.</param>
/// <param name="Messages">Причины, по которым профиль недоступен.</param>
public sealed record Sp63TeeProfileAnalysis(
    Sp63TeeSectionProfile? Profile,
    IReadOnlyList<Sp63NormalMessage> Messages);

/// <summary>Распознанная геометрия круглого или кольцевого сечения.</summary>
/// <param name="CenterX">X центра (центроид внешнего контура), м.</param>
/// <param name="CenterY">Y центра, м.</param>
/// <param name="OuterRadius">r₂ = √(A_hull/π) — эквивалентный по площади внешний радиус, м.</param>
/// <param name="InnerRadius">r₁ = √(A_hole/π); 0 для круга, м.</param>
/// <param name="Area">A = A_hull − A_hole, м².</param>
/// <param name="OuterMeanVertexRadius">ρ̄ внешнего контура (только метрика допуска), м.</param>
/// <param name="OuterRadialDeviation">max|ρᵢ − ρ̄|/ρ̄ внешнего контура.</param>
/// <param name="OuterAreaDeviation">|A_hull − πρ̄²|/(πρ̄²).</param>
/// <param name="InnerMeanVertexRadius">ρ̄ отверстия; 0 для круга, м.</param>
/// <param name="InnerRadialDeviation">Радиальное отклонение отверстия; 0 для круга.</param>
/// <param name="InnerAreaDeviation">Площадное отклонение отверстия; 0 для круга.</param>
/// <param name="CenterOffset">|c_hole − c_hull|/r₂; 0 для круга.</param>
public sealed record Sp63CircularGeometry(
    double CenterX,
    double CenterY,
    double OuterRadius,
    double InnerRadius,
    double Area,
    double OuterMeanVertexRadius,
    double OuterRadialDeviation,
    double OuterAreaDeviation,
    double InnerMeanVertexRadius,
    double InnerRadialDeviation,
    double InnerAreaDeviation,
    double CenterOffset);

/// <summary>Результат распознавания круглой/кольцевой геометрии.</summary>
/// <param name="Geometry">Геометрия или <see langword="null"/>.</param>
/// <param name="Messages">Причины неприменимости.</param>
public sealed record Sp63CircularGeometryClassification(
    Sp63CircularGeometry? Geometry,
    IReadOnlyList<Sp63NormalMessage> Messages);

/// <summary>Профиль равномерной полярной раскладки продольной арматуры.</summary>
/// <param name="BarCount">Число стержней.</param>
/// <param name="TotalArea">As,tot, м².</param>
/// <param name="RadiusRs">rs — среднее расстояние центров стержней до центра раскладки, м.</param>
/// <param name="Rs">Сопротивление растяжению, кПа.</param>
/// <param name="Rsc">Сопротивление сжатию, кПа.</param>
/// <param name="AreaDeviation">max|Aᵢ − Ā|/Ā.</param>
/// <param name="RadiusDeviation">max|ρᵢ − rs|/rs.</param>
/// <param name="AngularStepDeviation">max|Δθᵢ − 2π/n|/(2π/n).</param>
/// <param name="CenterOffset">|c_bars − c_concrete|/rs.</param>
/// <param name="Bars">Исходные стержни.</param>
public sealed record Sp63CircularRebarProfile(
    int BarCount,
    double TotalArea,
    double RadiusRs,
    double Rs,
    double Rsc,
    double AreaDeviation,
    double RadiusDeviation,
    double AngularStepDeviation,
    double CenterOffset,
    IReadOnlyList<(double X, double Y, double Area, double Diameter)> Bars);

/// <summary>Результат анализа полярной раскладки.</summary>
/// <param name="Profile">Профиль или <see langword="null"/>.</param>
/// <param name="Messages">Причины неприменимости.</param>
public sealed record Sp63CircularProfileAnalysis(
    Sp63CircularRebarProfile? Profile,
    IReadOnlyList<Sp63NormalMessage> Messages);
