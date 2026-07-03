using System;
using System.Collections.Generic;
using System.Linq;

namespace CScore;

/// <summary>
/// Поперечное сечение стальной конструкции.
/// Определяется контуром (полигоном) и материалом (сталь).
/// Вычисляет геометрические характеристики по СП 16.13330.2017.
/// </summary>
[Serializable]
public class SteelSection
{
    private GeoProps? _geoProps;
    private SteelClassifier.Result? _classification;
    private double _xMin = double.MaxValue, _xMax = double.MinValue;
    private double _yMin = double.MaxValue, _yMax = double.MinValue;

    /// <summary>
    /// Внешний контур сечения (против часовой стрелки). Координаты в метрах.
    /// </summary>
    public List<(double X, double Y)> OuterContour { get; set; } = [];

    /// <summary>
    /// Внутренние контуры (отверстия) — по часовой стрелке. Координаты в метрах.
    /// </summary>
    public List<List<(double X, double Y)>> InnerContours { get; set; } = [];

    /// <summary>
    /// Материал — сталь конструкций (MatType.Steel).
    /// </summary>
    public Material Steel { get; set; } = new() { Type = MatType.Steel };

    /// <summary>
    /// Геометрические характеристики (ленивое вычисление).
    /// </summary>
    public GeoProps Geo => _geoProps ??= ComputeGeoProps();

    /// <summary>
    /// Классификация элементов сечения по СП 16 (7.3).
    /// </summary>
    public SteelClassifier.Result Classification =>
        _classification ??= SteelClassifier.Classify(this);

    /// <summary>Площадь сечения [м²].</summary>
    public double Area => Geo.A;

    /// <summary>Момент инерции относительно оси X [м⁴].</summary>
    public double Ix => Geo.Ix;

    /// <summary>Момент инерции относительно оси Y [м⁴].</summary>
    public double Iy => Geo.Iy;

    /// <summary>Момент сопротивления X [м³] (наибольшее).</summary>
    public double Wx => Geo.Ix / Math.Max(Math.Abs(_yMax - Geo.Centroid.Y),
                                           Math.Abs(Geo.Centroid.Y - _yMin));

    /// <summary>Момент сопротивления Y [м³] (наибольшее).</summary>
    public double Wy => Geo.Iy / Math.Max(Math.Abs(_xMax - Geo.Centroid.X),
                                           Math.Abs(Geo.Centroid.X - _xMin));

    /// <summary>
    /// Момент сопротивления кручению [м³].
    /// Для открытого профиля: Wt = Σ(bt²/3).
    /// </summary>
    public double Wt => ComputeWt();

    /// <summary>Минимальный момент инерции [м⁴].</summary>
    public double IxMin => Math.Min(Ix, Iy);

    /// <summary>Радиус инерции X [м].</summary>
    public double ix => Math.Sqrt(Ix / Area);

    /// <summary>Радиус инерции Y [м].</summary>
    public double iy => Math.Sqrt(Iy / Area);

    /// <summary>Минимальный радиус инерции [м].</summary>
    public double ixMin => Math.Min(ix, iy);

    /// <summary>Центр тяжести сечения.</summary>
    public (double X, double Y) Centroid => (Geo.Centroid.X, Geo.Centroid.Y);

    /// <summary>
    /// Вычисляет геометрические характеристики из контуров.
    /// Использует GeoProps (интегрирование по Грину).
    /// </summary>
    private GeoProps ComputeGeoProps()
    {
        // Внешний контур (замыкаем: добавляем первую точку в конец)
        var outerX = OuterContour.Select(p => p.X).ToList();
        var outerY = OuterContour.Select(p => p.Y).ToList();
        if (outerX.Count > 0)
        {
            outerX.Add(outerX[0]);
            outerY.Add(outerY[0]);
        }
        var outerContour = new Contour(outerX, outerY, "outer");
        var result = new GeoProps(outerContour);

        // Вычитаем отверстия
        foreach (var hole in InnerContours)
        {
            var holeX = hole.Select(p => p.X).ToList();
            var holeY = hole.Select(p => p.Y).ToList();
            if (holeX.Count > 0)
            {
                holeX.Add(holeX[0]);
                holeY.Add(holeY[0]);
            }
            var holeContour = new Contour(holeX, holeY, "hole");
            result = result - new GeoProps(holeContour);
        }

        // Запоминаем экстремумы для вычисления Wx, Wy
        ComputeExtremes();

        return result;
    }

    /// <summary>
    /// Вычисляет экстремальные координаты контура (для Wx, Wy).
    /// </summary>
    private void ComputeExtremes()
    {
        foreach (var p in OuterContour)
        {
            if (p.X < _xMin) _xMin = p.X;
            if (p.X > _xMax) _xMax = p.X;
            if (p.Y < _yMin) _yMin = p.Y;
            if (p.Y > _yMax) _yMax = p.Y;
        }
    }

    /// <summary>
    /// Вычисляет момент сопротивления кручению Wt.
    /// Для открытого профиля: Wt = Σ(bt²/3).
    /// </summary>
    private double ComputeWt()
    {
        // Приближённо: для двутавра Wt ≈ (2·b·tf³ + h·tw³) / 3
        // Обобщение: суммируем bt²/3 для всех элементов
        // TODO: точное вычисление по контуру
        double sum = 0;
        // Упрощённо: используем площадь и характерную толщину
        // Для точного расчёта нужен анализ контура
        return sum > 0 ? sum : Area * 0.01; // заглушка
    }

    /// <summary>
    /// Создаёт SteelSection из двутавра (шаблонные точки).
    /// </summary>
    public static SteelSection FromIBeam(double h, double b, double tw, double tf,
                                          Material? steel = null)
    {
        var pts = TemplatePoints.IBeamPoints(h, b, tw, tf);
        return new SteelSection
        {
            OuterContour = pts.Select(p => (p.X, p.Y)).ToList(),
            Steel = steel ?? new Material { Type = MatType.Steel, E = 210e9 }
        };
    }
}
