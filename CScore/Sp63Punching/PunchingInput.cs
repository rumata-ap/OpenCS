namespace CScore.Sp63Punching;

/// <summary>Грузовая площадь (площадка передачи нагрузки) в осях плиты, м.</summary>
public abstract record PunchingLoadArea
{
    /// <summary>Прямоугольник Bx × By с центром в начале координат.</summary>
    public sealed record Rectangle(double Bx, double By) : PunchingLoadArea;

    /// <summary>Круг диаметром D с центром в начале координат.</summary>
    public sealed record Circle(double D) : PunchingLoadArea;

    /// <summary>Кольцо: наружный диаметр D, толщина стенки T. Контур — по наружному D, как у круга.</summary>
    public sealed record Ring(double D, double T) : PunchingLoadArea;

    /// <summary>Многоугольник в осях плиты (обход любой). Вогнутый заменяется выпуклой оболочкой.</summary>
    public sealed record Polygon(IReadOnlyList<PlanPoint> Points) : PunchingLoadArea;
}

/// <summary>Сторона приложения нагрузки к плите.</summary>
public enum PunchingLoadSide
{
    /// <summary>Колонна под плитой: реакция опоры давит на плиту снизу вверх.</summary>
    FromBelow,

    /// <summary>Колонна над плитой (или штамп): сила давит на плиту сверху вниз.</summary>
    FromAbove
}

/// <summary>Учёт момента от эксцентриситета силы относительно центра тяжести контура (п. 8.1.46).</summary>
public enum EccentricityMode
{
    /// <summary>По знакам: M* = M ± F·e по статике, затем |M*|.</summary>
    Auto,

    /// <summary>Догружающий, в запас: |M| + F·|e|.</summary>
    Loading,

    /// <summary>Разгружающий: ||M| − F·|e||.</summary>
    Unloading,

    /// <summary>Не учитывать: |M|.</summary>
    Ignore
}

/// <summary>Проверка разорванного контура пилона.</summary>
public enum PylonMode
{
    /// <summary>Проверять, когда Lmax &gt; 2·Lmin + h0 и площадь — прямоугольник без краёв рядом.</summary>
    Auto,

    /// <summary>Не проверять.</summary>
    Off
}

/// <summary>Какой момент сопротивления контура брать в (8.94), (8.97).</summary>
public enum SectionModulusMode
{
    /// <summary>Наименьший по п. 8.1.51 (в запас).</summary>
    Minimum,

    /// <summary>Со стороны наибольших касательных напряжений; только при <see cref="EccentricityMode.Auto"/>.</summary>
    StressSide
}

/// <summary>Сосредоточенные усилия продавливания.</summary>
/// <param name="F">Модуль продавливающей силы, кН, ≥ 0.</param>
/// <param name="Mx">Момент относительно оси X (вектор вдоль X), кН·м, правило правой руки.</param>
/// <param name="My">Момент относительно оси Y, кН·м.</param>
/// <param name="MomentsAreMloc">true — заданы полные Mloc узла, в продавливание идёт половина (п. 8.1.46).</param>
/// <param name="LoadSide">Сторона приложения силы — нужна для знаков в режиме Auto.</param>
/// <param name="CounterPressure">Нагрузка против F в основании пирамиды продавливания, кПа.</param>
public sealed record PunchingForces(
    double F, double Mx, double My,
    bool MomentsAreMloc = false,
    PunchingLoadSide LoadSide = PunchingLoadSide.FromBelow,
    double CounterPressure = 0.0);

/// <summary>Равномерная поперечная арматура вдоль контура (пп. 8.1.48, 8.1.52).</summary>
/// <param name="Rsw">Расчётное сопротивление, кПа.</param>
/// <param name="Asw">Площадь в одном ряду вдоль контура на длине Sw, м².</param>
/// <param name="Sw">Шаг вдоль контура, м.</param>
/// <param name="ZoneExtent">От грани грузовой площади до внешнего ряда, м; null — контур за зоной не проверять.</param>
public sealed record PunchingShearReinforcement(double Rsw, double Asw, double Sw, double? ZoneExtent = null)
{
    /// <summary>qsw = Rsw·Asw/sw (8.92), кН/м.</summary>
    public double Qsw => Sw > 0 ? Rsw * Asw / Sw : 0;
}

/// <summary>Свободный край плиты — прямая; плита лежит со стороны внутренней нормали.</summary>
public sealed record PunchingSlabEdge(PlanPoint PointOnEdge, PlanPoint InwardNormal)
{
    /// <summary>Единичная внутренняя нормаль.</summary>
    public PlanPoint N => InwardNormal.Normalized();

    /// <summary>Расстояние от точки до края со знаком: &gt; 0 — внутри плиты.</summary>
    public double SignedDistance(PlanPoint p) => (p - PointOnEdge).Dot(N);
}

/// <summary>Отверстие в плите — многоугольник.</summary>
public sealed record PunchingHole(IReadOnlyList<PlanPoint> Polygon);

/// <summary>Контур пользователя: многоугольник и номера выключенных сторон (сторона i — от вершины i к i+1).</summary>
public sealed record PunchingCustomContour(IReadOnlyList<PlanPoint> Polygon, IReadOnlyList<int> InactiveEdges, string? Name = null);

/// <summary>Настройки расчёта.</summary>
/// <param name="EccentricityX">Режим эксцентриситета для Mx (смещение центра тяжести по Y).</param>
/// <param name="EccentricityY">Режим эксцентриситета для My (смещение по X).</param>
/// <param name="Pylon">Разорванный контур пилона.</param>
/// <param name="HoleInfluence">Отверстия ближе HoleInfluence·h0 вырезают часть контура (п. 8.1.46).</param>
/// <param name="Wb">Выбор момента сопротивления.</param>
/// <param name="PylonReducedPerimeter">Справочный вариант по Галяутдинову (2022), только F.</param>
/// <param name="PylonReducedGoverns">Справочный вариант участвует в выборе определяющего.</param>
/// <param name="LongitudinalRatio">Коэффициент продольного армирования плиты μ (доля, не %) для (5.34); null — множитель 1.</param>
public sealed record PunchingOptions(
    EccentricityMode EccentricityX = EccentricityMode.Loading,
    EccentricityMode EccentricityY = EccentricityMode.Loading,
    PylonMode Pylon = PylonMode.Auto,
    double HoleInfluence = 6.0,
    SectionModulusMode Wb = SectionModulusMode.Minimum,
    bool PylonReducedPerimeter = false,
    bool PylonReducedGoverns = false,
    double? LongitudinalRatio = null);

/// <summary>Исходные данные расчёта на продавливание по СП 63.13330.2018, пп. 8.1.46–8.1.52.</summary>
/// <param name="LoadArea">Грузовая площадь.</param>
/// <param name="H0">Приведённая рабочая высота 0,5(h0x + h0y), м.</param>
/// <param name="Rbt">Расчётное сопротивление бетона растяжению с коэффициентами условий работы, кПа.</param>
/// <param name="Forces">Усилия.</param>
/// <param name="Reinforcement">Поперечная арматура; null — нет.</param>
/// <param name="Edges">Края плиты рядом с площадью (0, 1 или 2).</param>
/// <param name="Holes">Отверстия.</param>
/// <param name="CustomContours">Контуры пользователя — дополнительные варианты.</param>
/// <param name="Options">Настройки.</param>
public sealed record PunchingInput(
    PunchingLoadArea LoadArea,
    double H0,
    double Rbt,
    PunchingForces Forces,
    PunchingShearReinforcement? Reinforcement = null,
    IReadOnlyList<PunchingSlabEdge>? Edges = null,
    IReadOnlyList<PunchingHole>? Holes = null,
    IReadOnlyList<PunchingCustomContour>? CustomContours = null,
    PunchingOptions? Options = null);
