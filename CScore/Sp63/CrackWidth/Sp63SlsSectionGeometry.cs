using CScore.Sp63.Normal;

namespace CScore.Sp63.CrackWidth;

/// <summary>
/// Полоса постоянной ширины ориентированного профиля: участок по высоте от
/// <see cref="Start"/> до <see cref="End"/> с шириной <see cref="Width"/>, м.
/// Ориентированные координаты идут от сжатой грани (0) к растянутой (Height).
/// </summary>
/// <param name="Start">Начало полосы от сжатой грани, м.</param>
/// <param name="End">Конец полосы от сжатой грани, м.</param>
/// <param name="Width">Ширина полосы поперёк плоскости изгиба, м.</param>
public readonly record struct Sp63SlsSectionBand(double Start, double End, double Width);

/// <summary>Эффективный уровень ненапрягаемой арматуры ориентированного профиля.</summary>
/// <param name="Coordinate">Ориентированная координата уровня от сжатой грани, м.</param>
/// <param name="Area">Суммарная площадь стержней уровня, м².</param>
/// <param name="Diameter">Эффективный диаметр стержней уровня, м.</param>
public sealed record Sp63SlsRebarLayer(double Coordinate, double Area, double Diameter);

/// <summary>
/// Ориентированный полосовой профиль нормального сечения для формульного расчёта второй группы
/// предельных состояний СП 63 (ширина трещин, кривизна, прогиб): бетон представлен полосами
/// постоянной ширины, арматура — двумя эффективными уровнями. Координаты и длины — в метрах.
/// Профиль строится от сжатой грани (координата 0) к растянутой (координата Height),
/// поэтому смена знака момента меняет только ориентацию полос.
/// </summary>
public sealed class Sp63SlsSectionGeometry
{
    /// <summary>Допуск сравнения координат, м.</summary>
    public const double Tolerance = 1e-9;

    readonly List<Sp63SlsSectionBand> _bands;

    /// <summary>Создаёт профиль из полос и двух уровней арматуры с проверкой размеров.</summary>
    /// <param name="shapeKind">Распознанная форма сечения.</param>
    /// <param name="height">Полная высота в плоскости изгиба, м.</param>
    /// <param name="bands">Полосы постоянной ширины (порядок не важен — будут отсортированы).</param>
    /// <param name="tensionLayer">Растянутый уровень арматуры.</param>
    /// <param name="compressionLayer">Сжатый уровень арматуры.</param>
    /// <param name="hasCompressionFlange">Полка у сжатой грани шире стенки.</param>
    /// <param name="hasTensionFlange">Полка у растянутой грани шире стенки.</param>
    /// <exception cref="ArgumentOutOfRangeException">Размеры некорректны.</exception>
    /// <exception cref="ArgumentException">Полосы пересекаются.</exception>
    public Sp63SlsSectionGeometry(
        Sp63NormalShapeKind shapeKind,
        double height,
        IReadOnlyList<Sp63SlsSectionBand> bands,
        Sp63SlsRebarLayer tensionLayer,
        Sp63SlsRebarLayer compressionLayer,
        bool hasCompressionFlange,
        bool hasTensionFlange)
    {
        if (!double.IsFinite(height) || height <= 0.0)
            throw new ArgumentOutOfRangeException(nameof(height), "Высота должна быть конечной и положительной.");
        ArgumentNullException.ThrowIfNull(bands);
        if (bands.Count == 0)
            throw new ArgumentException("Профиль должен содержать хотя бы одну полосу.", nameof(bands));

        _bands = bands.OrderBy(band => band.Start).ToList();
        double previousEnd = 0.0;
        foreach (var band in _bands)
        {
            if (!double.IsFinite(band.Start) || !double.IsFinite(band.End) || !double.IsFinite(band.Width))
                throw new ArgumentOutOfRangeException(nameof(bands), "Размеры полос должны быть конечными.");
            if (!(band.Start >= 0.0) || !(band.End <= height) || !(band.Start < band.End))
                throw new ArgumentOutOfRangeException(nameof(bands),
                    "Полосы должны удовлетворять 0 <= Start < End <= Height.");
            if (band.Width <= 0.0)
                throw new ArgumentOutOfRangeException(nameof(bands), "Ширина полосы должна быть положительной.");
            if (band.Start < previousEnd - Tolerance)
                throw new ArgumentException("Полосы не должны пересекаться.", nameof(bands));
            previousEnd = band.End;
        }

        ShapeKind = shapeKind;
        Height = height;
        TensionLayer = ValidateLayer(tensionLayer, height, nameof(tensionLayer));
        CompressionLayer = ValidateLayer(compressionLayer, height, nameof(compressionLayer));
        HasCompressionFlange = hasCompressionFlange;
        HasTensionFlange = hasTensionFlange;
    }

    /// <summary>Распознанная форма сечения.</summary>
    public Sp63NormalShapeKind ShapeKind { get; }

    /// <summary>Полная высота в плоскости изгиба от сжатой грани, м.</summary>
    public double Height { get; }

    /// <summary>Полосы постоянной ширины, отсортированные от сжатой грани.</summary>
    public IReadOnlyList<Sp63SlsSectionBand> Bands => _bands;

    /// <summary>Эффективный растянутый уровень арматуры (у растянутой грани).</summary>
    public Sp63SlsRebarLayer TensionLayer { get; }

    /// <summary>Эффективный сжатый уровень арматуры (у сжатой грани).</summary>
    public Sp63SlsRebarLayer CompressionLayer { get; }

    /// <summary>У сжатой грани расположена полка шире стенки (тавр с полкой в сжатой зоне).</summary>
    public bool HasCompressionFlange { get; }

    /// <summary>У растянутой грани расположена полка шире стенки (тавр с полкой в растянутой зоне).</summary>
    public bool HasTensionFlange { get; }

    /// <summary>Площадь бетона на участке [start; end], м².</summary>
    public double Area(double start, double end) =>
        Integrate(start, end, (upper, lower) => upper - lower);

    /// <summary>Первый момент площади бетона на участке [start; end] относительно сжатой грани, м³.</summary>
    public double FirstMoment(double start, double end) =>
        Integrate(start, end, (upper, lower) => (upper * upper - lower * lower) / 2.0);

    /// <summary>Второй момент площади бетона на участке [start; end] относительно сжатой грани, м⁴.</summary>
    public double SecondMoment(double start, double end) =>
        Integrate(start, end, (upper, lower) =>
            (upper * upper * upper - lower * lower * lower) / 3.0);

    /// <summary>
    /// Интегрирует вклад полос на пересечении [start; end] с каждой полосой:
    /// Area = Σ w·(upper − lower), FirstMoment = Σ w·(upper² − lower²)/2,
    /// SecondMoment = Σ w·(upper³ − lower³)/3.
    /// </summary>
    double Integrate(double start, double end, Func<double, double, double> weight)
    {
        if (!double.IsFinite(start) || !double.IsFinite(end) || !(start < end))
            throw new ArgumentOutOfRangeException(nameof(start),
                "Границы участка должны быть конечными и удовлетворять start < end.");
        if (start < -Tolerance || end > Height + Tolerance)
            throw new ArgumentOutOfRangeException(nameof(start),
                "Границы участка должны лежать в [0; Height].");

        double sum = 0.0;
        foreach (var band in _bands)
        {
            double lower = Math.Max(start, band.Start);
            double upper = Math.Min(end, band.End);
            if (upper <= lower) continue;
            sum += band.Width * weight(upper, lower);
        }
        return sum;
    }

    static Sp63SlsRebarLayer ValidateLayer(Sp63SlsRebarLayer layer, double height, string name)
    {
        ArgumentNullException.ThrowIfNull(layer, name);
        if (!double.IsFinite(layer.Coordinate) || layer.Coordinate < -Tolerance ||
            layer.Coordinate > height + Tolerance)
            throw new ArgumentOutOfRangeException(name,
                "Координата уровня арматуры должна лежать в [0; Height].");
        if (!double.IsFinite(layer.Area) || layer.Area < 0.0)
            throw new ArgumentOutOfRangeException(name,
                "Площадь уровня арматуры должна быть неотрицательной.");
        if (!double.IsFinite(layer.Diameter) || layer.Diameter < 0.0)
            throw new ArgumentOutOfRangeException(name,
                "Диаметр уровня арматуры должен быть неотрицательным.");
        return layer;
    }
}
