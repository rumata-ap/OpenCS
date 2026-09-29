namespace CScore.Sp63.Normal;

/// <summary>
/// Полосовой профиль бетонного сечения для раздела 7 СП 63: полосы постоянной ширины,
/// координаты — от сжатой грани (0) к растянутой (<see cref="Height"/>). Прямоугольник —
/// одна полоса, тавр/двутавр — две-три. Длины в метрах.
/// </summary>
public sealed class Sp63ConcreteBandProfile
{
    /// <summary>Полоса постоянной ширины на участке [Start; End] от сжатой грани, м.</summary>
    public readonly record struct Band(double Start, double End, double Width);

    readonly Band[] _bands;

    Sp63ConcreteBandProfile(Band[] bands)
    {
        _bands = bands;
        Height = bands[^1].End;
        Area = bands.Sum(band => band.Width * (band.End - band.Start));
        double firstMoment = bands.Sum(band =>
            band.Width * (band.End * band.End - band.Start * band.Start) / 2.0);
        double secondMoment = bands.Sum(band =>
            band.Width * (Math.Pow(band.End, 3) - Math.Pow(band.Start, 3)) / 3.0);
        Centroid = firstMoment / Area;
        Inertia = secondMoment - Area * Centroid * Centroid;
    }

    /// <summary>Полосы, отсортированные от сжатой грани.</summary>
    public IReadOnlyList<Band> Bands => _bands;

    /// <summary>Полная высота в плоскости изгиба, м.</summary>
    public double Height { get; }

    /// <summary>Площадь сечения A, м².</summary>
    public double Area { get; }

    /// <summary>Расстояние от сжатой грани до центра тяжести сечения, м.</summary>
    public double Centroid { get; }

    /// <summary>Момент инерции I относительно центра тяжести, м⁴.</summary>
    public double Inertia { get; }

    /// <summary>yt — расстояние от центра тяжести до наиболее растянутого волокна, м.</summary>
    public double TensionFiberDistance => Height - Centroid;

    /// <summary>Радиус инерции √(I/A), м.</summary>
    public double RadiusOfGyration => Math.Sqrt(Inertia / Area);

    /// <summary>
    /// Уровень проверки главных напряжений по Пособию к СП 63, п. 3.1.13: у сжатой грани
    /// с полкой (тавр/двутавр) — примыкание полки к стенке, иначе — центр тяжести. Расстояние
    /// от сжатой грани, м.
    /// </summary>
    public double PrincipalStressLevel =>
        _bands.Length > 1 && _bands[0].Width > _bands[1].Width ? _bands[0].End : Centroid;

    /// <summary>
    /// Статический момент S относительно центра тяжести части сечения между сжатой гранью
    /// и уровнем <paramref name="depth"/>, м³ (для τ = Q·S/(I·b)).
    /// </summary>
    /// <param name="depth">Расстояние от сжатой грани, м.</param>
    public double StaticMoment(double depth)
    {
        double moment = 0.0;
        foreach (var band in _bands)
        {
            double end = Math.Min(band.End, depth);
            if (end <= band.Start)
                break;
            moment += band.Width * (end - band.Start) * (Centroid - (band.Start + end) / 2.0);
        }
        return moment;
    }

    /// <summary>
    /// Ширина сечения на уровне <paramref name="depth"/>, м. На границе полос — меньшая из
    /// смежных ширин (у примыкания полки — ширина стенки).
    /// </summary>
    /// <param name="depth">Расстояние от сжатой грани, м.</param>
    public double WidthAt(double depth)
    {
        double tolerance = Height * 1e-12;
        return _bands
            .Where(band => depth >= band.Start - tolerance && depth <= band.End + tolerance)
            .Min(band => band.Width);
    }

    /// <summary>Прямоугольник b×h (одна полоса).</summary>
    public static Sp63ConcreteBandProfile Rectangle(double b, double h) =>
        new([new Band(0.0, h, b)]);

    /// <summary>
    /// Тавр/двутавр, ориентированный от сжатой грани. При <paramref name="compressedAtMin"/>
    /// сжата грань с меньшей координатой высоты (нижняя полка — у сжатой грани).
    /// </summary>
    public static Sp63ConcreteBandProfile Tee(Sp63TeeGeometry geometry, bool compressedAtMin)
    {
        ArgumentNullException.ThrowIfNull(geometry);
        double h = geometry.H;
        double bottom = geometry.BottomFlangeThickness;
        double top = geometry.TopFlangeThickness;
        // Полосы в координате «от нижней грани», затем — разворот к сжатой грани.
        var fromBottom = new List<Band>();
        if (bottom > 0) fromBottom.Add(new Band(0.0, bottom, geometry.BottomFlangeWidth));
        fromBottom.Add(new Band(bottom, h - top, geometry.Bw));
        if (top > 0) fromBottom.Add(new Band(h - top, h, geometry.TopFlangeWidth));

        var bands = compressedAtMin
            ? fromBottom
            : fromBottom.Select(band => new Band(h - band.End, h - band.Start, band.Width))
                .OrderBy(band => band.Start).ToList();
        return new Sp63ConcreteBandProfile([.. bands]);
    }

    /// <summary>
    /// Площадь сжатой зоны Ab по п. 7.1.9: участок [0; x] от сжатой грани, центр тяжести
    /// которого находится на расстоянии <paramref name="depth"/> от сжатой грани (в точке
    /// приложения силы с учётом прогиба). Требует 0 &lt; depth ≤ <see cref="Centroid"/>.
    /// </summary>
    /// <param name="depth">Расстояние от сжатой грани до точки приложения силы, м.</param>
    /// <param name="height">Высота сжатой зоны x, м.</param>
    public double CompressedZoneArea(double depth, out double height)
    {
        if (!(depth > 0) || depth > Centroid * (1.0 + 1e-12))
            throw new ArgumentOutOfRangeException(nameof(depth),
                "Точка приложения силы должна лежать между сжатой гранью и центром тяжести.");

        // Центр тяжести участка [0; x] монотонно растёт с x; на полосе [s; e] ширины w
        // условие S(x) = depth·A(x) — квадратное уравнение
        // (w/2)·x² − w·depth·x + (S0 − w·s²/2 − depth·A0 + w·depth·s) = 0.
        double area0 = 0.0, moment0 = 0.0;
        foreach (var band in _bands)
        {
            double w = band.Width, s = band.Start, e = band.End;
            double areaEnd = area0 + w * (e - s);
            double momentEnd = moment0 + w * (e * e - s * s) / 2.0;
            bool last = band.Equals(_bands[^1]);
            if (momentEnd / areaEnd >= depth || last)
            {
                double c = moment0 - w * s * s / 2.0 - depth * area0 + w * depth * s;
                double discriminant = Math.Max(0.0, depth * depth - 2.0 * c / w);
                height = Math.Min(e, depth + Math.Sqrt(discriminant));
                return area0 + w * (height - s);
            }
            area0 = areaEnd;
            moment0 = momentEnd;
        }
        throw new InvalidOperationException("Профиль не содержит полос.");
    }
}
