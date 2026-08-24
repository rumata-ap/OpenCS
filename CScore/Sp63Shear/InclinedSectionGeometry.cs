namespace CScore.Sp63Shear;

/// <summary>
/// Расчётные характеристики сечения для одной плоскости сдвига: ширина, рабочая высота,
/// усилие в растянутой продольной арматуре и характеристики материалов.
/// </summary>
/// <param name="B">Расчётная ширина, м.</param>
/// <param name="H0">Рабочая высота, м.</param>
/// <param name="Ns">Усилие в растянутой продольной арматуре Σ(Rs,i·As,i), кН.</param>
/// <param name="As">Площадь растянутой продольной арматуры, м².</param>
/// <param name="Rb">Расчётное сопротивление бетона сжатию, кПа (положительное).</param>
/// <param name="Rbt">Расчётное сопротивление бетона растяжению, кПа.</param>
/// <param name="Ab">Площадь бетона сечения, м².</param>
/// <param name="AsTotal">Площадь всей продольной арматуры, м².</param>
/// <param name="Eb">Начальный модуль упругости бетона, кПа.</param>
/// <param name="Eb0">Деформация εb0 при непродолжительном действии нагрузки.</param>
/// <param name="Ebt0">Деформация εbt0 при непродолжительном действии нагрузки.</param>
/// <param name="Plane">Плоскость сдвига.</param>
/// <param name="TensionOnPositiveSide">Растянута грань с положительной координатой.</param>
/// <param name="Warnings">Оговорки, подлежащие выводу в отчёт.</param>
public sealed record InclinedSectionGeometry(
    double B, double H0, double Ns, double As, double Rb, double Rbt,
    double Ab, double AsTotal, double Eb, double Eb0, double Ebt0,
    ShearPlane Plane, bool TensionOnPositiveSide, IReadOnlyList<string> Warnings)
{
    /// <summary>Значение εb0 по умолчанию, если в характеристиках материала оно не задано.</summary>
    public const double DefaultEb0 = 0.002;

    /// <summary>Значение εbt0 по умолчанию, если в характеристиках материала оно не задано.</summary>
    public const double DefaultEbt0 = 0.0001;

    /// <summary>Извлекает расчётные характеристики сечения для заданной плоскости сдвига.</summary>
    /// <param name="section">Железобетонное сечение.</param>
    /// <param name="plane">Плоскость сдвига.</param>
    /// <param name="pairedMoment">Парный изгибающий момент (Mx для Vy, My для Vx), кН·м.</param>
    /// <param name="calc">Вид расчёта.</param>
    public static InclinedSectionGeometry Resolve(
        CrossSection section, ShearPlane plane, double pairedMoment, CalcType calc)
    {
        ArgumentNullException.ThrowIfNull(section);
        var pair = InclinedSectionGeometryPair.Resolve(section, plane, calc);
        var geometry = pair.For(pairedMoment);
        if (Math.Abs(pairedMoment) > InclinedSectionGeometryPair.MomentEpsilon) return geometry;

        var warnings = new List<string>(geometry.Warnings)
        {
            "Парный изгибающий момент нулевой — принята меньшая рабочая высота."
        };
        return geometry with { Warnings = warnings };
    }

    /// <summary>
    /// Извлекает характеристики сечения при заданной растянутой грани — без обращения
    /// к знаку момента. Используется для расчёта на каждой стоянке отдельно.
    /// </summary>
    /// <param name="section">Железобетонное сечение.</param>
    /// <param name="plane">Плоскость сдвига.</param>
    /// <param name="tensionOnPositiveSide">Растянута грань с положительной координатой.</param>
    /// <param name="calc">Вид расчёта.</param>
    public static InclinedSectionGeometry ResolveForTensionSide(
        CrossSection section, ShearPlane plane, bool tensionOnPositiveSide, CalcType calc)
    {
        ArgumentNullException.ThrowIfNull(section);
        var warnings = new List<string>();

        var concrete = section.Areas
            .Where(a => a.Category == AreaCategory.Region && a.Material?.Type == MatType.Concrete)
            .ToList();
        if (concrete.Count == 0)
            throw new InvalidOperationException(
                "В сечении нет бетонной области — расчёт наклонных сечений невозможен.");

        var bars = section.Areas
            .Where(a => a.Material?.Type is MatType.ReSteelF or MatType.ReSteelU)
            .SelectMany(a => a.Fibers
                .Where(f => f.TypeFiber == FiberType.point)
                .Select(f => (Fiber: f, Chars: a.Material!.GetChars(calc))))
            .Where(pair => pair.Chars is not null)
            .ToList();

        var (minLevel, maxLevel) = ConcreteRange(concrete, plane);
        bool tensionOnPositive = tensionOnPositiveSide;
        double compressedEdge = tensionOnPositive ? minLevel : maxLevel;

        var tensionBars = bars
            .Where(pair => IsTension(Along(pair.Fiber, plane), tensionOnPositive, minLevel, maxLevel))
            .ToList();

        double asTension = tensionBars.Sum(pair => pair.Fiber.Area);
        // Rs берётся из Ft характеристик арматуры: Ry в CScore заполняется только
        // для стальных сечений по СП 16, для арматуры ЖБ используется Ft.
        double ns = tensionBars.Sum(pair => Math.Abs(pair.Chars!.Ft) * pair.Fiber.Area);
        double h0;
        if (asTension > 0.0)
        {
            double centroid =
                tensionBars.Sum(pair => Along(pair.Fiber, plane) * pair.Fiber.Area) / asTension;
            h0 = Math.Abs(centroid - compressedEdge);
        }
        else
        {
            warnings.Add("Растянутая продольная арматура не найдена — проверка по 8.1.35 не выполняется.");
            h0 = 0.9 * Math.Abs(maxLevel - minLevel);
        }

        double rb = double.MaxValue, rbt = double.MaxValue, eb = 0.0, eb0 = 0.0, ebt0 = 0.0;
        var grades = new HashSet<int>();
        foreach (var area in concrete)
        {
            var chars = area.Material!.GetChars(calc)
                ?? throw new InvalidOperationException(
                    $"У бетона «{area.Material.Tag}» нет характеристик для типа расчёта {calc}.");
            grades.Add(area.MaterialId);

            // Fc бетона в CScore отрицательно (сжатие — «минус»), Rb нужен положительным.
            double fc = Math.Abs(chars.Fc);
            double ft = Math.Abs(chars.Ft);
            if (fc < rb) { rb = fc; eb = area.Material.E; eb0 = Strain(chars.Ec0, chars.Ec1Red, DefaultEb0); }
            if (ft < rbt) { rbt = ft; ebt0 = Strain(chars.Et0, chars.Et1Red, DefaultEbt0); }
        }
        if (grades.Count > 1)
            warnings.Add(
                "Бетон сечения неоднороден: принят минимальный класс — подтвердите или задайте Rb/Rbt вручную.");

        double b = ChordWidthScanner.MinWidth(concrete, plane,
            compressedEdge, tensionOnPositive ? compressedEdge + h0 : compressedEdge - h0);
        if (b <= 0.0)
            throw new InvalidOperationException(
                "Не удалось определить расчётную ширину сечения — проверьте геометрию бетонной области.");

        double ab = concrete.Sum(a => Math.Abs(a.Hull is null ? 0.0
            : WktHelper.PolygonArea(a.Hull.X, a.Hull.Y))
            - a.Holes.Sum(h => Math.Abs(WktHelper.PolygonArea(h.X, h.Y))));

        return new InclinedSectionGeometry(
            b, h0, ns, asTension, rb, rbt, ab, bars.Sum(pair => pair.Fiber.Area),
            eb, eb0, ebt0, plane, tensionOnPositive, warnings);
    }

    /// <summary>Предельная деформация: основное значение, запасное либо величина по умолчанию.</summary>
    static double Strain(double primary, double fallback, double defaultValue)
    {
        if (Math.Abs(primary) > 1e-12) return Math.Abs(primary);
        if (Math.Abs(fallback) > 1e-12) return Math.Abs(fallback);
        return defaultValue;
    }

    /// <summary>Координата волокна вдоль высоты сечения для заданной плоскости.</summary>
    static double Along(Fiber fiber, ShearPlane plane) =>
        plane == ShearPlane.Vy ? fiber.Y : fiber.X;

    /// <summary>Границы бетонного тела вдоль высоты сечения.</summary>
    static (double Min, double Max) ConcreteRange(
        IReadOnlyList<MaterialArea> concrete, ShearPlane plane)
    {
        double min = double.MaxValue, max = double.MinValue;
        foreach (var area in concrete)
        {
            if (area.Hull is null) continue;
            var along = plane == ShearPlane.Vy ? area.Hull.Y : area.Hull.X;
            foreach (double value in along)
            {
                if (value < min) min = value;
                if (value > max) max = value;
            }
        }
        return (min, max);
    }

    /// <summary>Стержень растянут, если лежит по растянутую сторону от середины сечения.</summary>
    static bool IsTension(double along, bool tensionOnPositive, double min, double max)
    {
        double middle = 0.5 * (min + max);
        return tensionOnPositive ? along > middle : along < middle;
    }
}

/// <summary>
/// Пара геометрий одного сечения — для растяжения положительной и отрицательной грани.
/// Позволяет выбирать расчётные величины по знаку момента в каждой стоянке, а не один раз
/// по моменту исходной строки усилий.
/// </summary>
/// <param name="TensionPositive">Геометрия при растянутой грани с положительной координатой.</param>
/// <param name="TensionNegative">Геометрия при растянутой грани с отрицательной координатой.</param>
public sealed record InclinedSectionGeometryPair(
    InclinedSectionGeometry TensionPositive, InclinedSectionGeometry TensionNegative)
{
    /// <summary>Порог, ниже которого момент считается нулевым, кН·м.</summary>
    public const double MomentEpsilon = 1e-9;

    /// <summary>Расчётные величины сторон различаются — смена знака момента меняет расчёт.</summary>
    public bool SidesDiffer =>
        Math.Abs(TensionPositive.H0 - TensionNegative.H0) > 1e-9 ||
        Math.Abs(TensionPositive.Ns - TensionNegative.Ns) > 1e-6 ||
        Math.Abs(TensionPositive.B - TensionNegative.B) > 1e-9;

    /// <summary>
    /// Геометрия, отвечающая знаку момента: положительный растягивает грань «плюс».
    /// При нулевом моменте берётся сторона с меньшей рабочей высотой — в запас.
    /// </summary>
    public InclinedSectionGeometry For(double moment)
    {
        if (moment > MomentEpsilon) return TensionPositive;
        if (moment < -MomentEpsilon) return TensionNegative;
        return TensionPositive.H0 <= TensionNegative.H0 ? TensionPositive : TensionNegative;
    }

    /// <summary>Вычисляет обе геометрии сечения для заданной плоскости сдвига.</summary>
    public static InclinedSectionGeometryPair Resolve(
        CrossSection section, ShearPlane plane, CalcType calc) => new(
            InclinedSectionGeometry.ResolveForTensionSide(section, plane, true, calc),
            InclinedSectionGeometry.ResolveForTensionSide(section, plane, false, calc));
}
