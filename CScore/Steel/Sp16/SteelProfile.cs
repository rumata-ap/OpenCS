namespace CScore.Sp16;

/// <summary>
/// Вид поперечного сечения стального стержня для выбора формул и таблиц СП 16.13330.2017
/// (типы сечений по табл. 7, 9, 10, 21, 22, 23, Д.2, Е.1, прил. Ж).
/// </summary>
public enum SteelProfileKind
{
    /// <summary>Произвольное сечение: только упругие проверки, типы сечений задаются вручную.</summary>
    Generic = 0,
    /// <summary>Двутавр (двоякосимметричный или с одной осью симметрии).</summary>
    IBeam = 1,
    /// <summary>Швеллер (стенка слева в каноническом положении).</summary>
    Channel = 2,
    /// <summary>Тавр (полка сверху в каноническом положении).</summary>
    Tee = 3,
    /// <summary>Коробчатое сечение (сварное или гнутый замкнутый профиль).</summary>
    Box = 4,
    /// <summary>Круглая труба.</summary>
    Pipe = 5,
    /// <summary>Одиночный уголок.</summary>
    Angle = 6,
    /// <summary>Сплошной прямоугольник (лист, полоса, пакет).</summary>
    Rect = 7,
    /// <summary>Сплошной круг.</summary>
    Round = 8,
}

/// <summary>Способ изготовления профиля (влияет на hef, bef — п. 7.3.1, 7.3.7, рис. 5; тип сечения по табл. 7).</summary>
public enum SteelFabrication
{
    /// <summary>Прокатный (размеры до начала внутренних закруглений).</summary>
    Rolled = 0,
    /// <summary>Сварной (из листов).</summary>
    Welded = 1,
    /// <summary>Гнутый (холодногнутый, гнутосварной).</summary>
    Bent = 2,
}

/// <summary>
/// Дескриптор профиля: вид и размеры в каноническом положении. Каноническое положение:
/// стенка двутавра/швеллера/тавра вертикальна, ось x — горизонтальная (ось наибольшей
/// жёсткости), швеллер — стенкой слева, тавр — полкой сверху. Все размеры — в метрах.
/// </summary>
/// <remarks>
/// Использование полей по видам:
/// двутавр — H, Bf1/Tf1 (верхний пояс), Bf2/Tf2 (нижний пояс), Tw, R;
/// швеллер — H, Bf1 (ширина полки), Tf1, Tw, R; тавр — H, Bf1 (полка), Tf1, Tw, R;
/// короб — H, Bf1 (ширина), Tf1 (толщина пояса), Tw (толщина каждой стенки), R (наружный радиус гиба);
/// труба — H (наружный диаметр), Tw (толщина стенки); уголок — H (вертикальная полка),
/// Bf1 (горизонтальная полка), Tw (толщина), R; прямоугольник — H, Bf1; круг — H (диаметр).
/// </remarks>
public sealed record SteelProfile
{
    /// <summary>Вид сечения.</summary>
    public SteelProfileKind Kind { get; init; }

    /// <summary>Способ изготовления.</summary>
    public SteelFabrication Fabrication { get; init; } = SteelFabrication.Welded;

    /// <summary>Полная высота (диаметр для трубы и круга), м.</summary>
    public double H { get; init; }

    /// <summary>Ширина верхнего (единственного) пояса / полки / сечения, м.</summary>
    public double Bf1 { get; init; }

    /// <summary>Толщина верхнего (единственного) пояса, м.</summary>
    public double Tf1 { get; init; }

    /// <summary>Ширина нижнего пояса двутавра, м (0 — как верхний).</summary>
    public double Bf2 { get; init; }

    /// <summary>Толщина нижнего пояса двутавра, м (0 — как верхний).</summary>
    public double Tf2 { get; init; }

    /// <summary>Толщина стенки (каждой стенки короба, стенки трубы, полки уголка), м.</summary>
    public double Tw { get; init; }

    /// <summary>Радиус внутреннего закругления (прокат) или наружный радиус гиба, м.</summary>
    public double R { get; init; }

    /// <summary>Контур повёрнут на 90° относительно канонического положения (оси x и y сечения переставлены).</summary>
    public bool Rotated90 { get; init; }

    /// <summary>Зеркальное положение: швеллер стенкой справа, тавр полкой снизу, уголок пером вниз.</summary>
    public bool Flipped { get; init; }

    /// <summary>Ширина нижнего пояса с учётом умолчания.</summary>
    public double BfBottom => Bf2 > 0 ? Bf2 : Bf1;

    /// <summary>Толщина нижнего пояса с учётом умолчания.</summary>
    public double TfBottom => Tf2 > 0 ? Tf2 : Tf1;

    /// <summary>Двутавр с одинаковыми поясами (две оси симметрии).</summary>
    public bool IsDoublySymmetricIBeam =>
        Kind == SteelProfileKind.IBeam
        && Math.Abs(BfBottom - Bf1) <= 1e-6 && Math.Abs(TfBottom - Tf1) <= 1e-6;

    /// <summary>Краткое описание для отчёта.</summary>
    public string Describe()
    {
        static string Mm(double v) => (v * 1000).ToString("0.#", System.Globalization.CultureInfo.InvariantCulture);
        return Kind switch
        {
            SteelProfileKind.IBeam => IsDoublySymmetricIBeam
                ? $"двутавр h={Mm(H)} bf={Mm(Bf1)} tf={Mm(Tf1)} tw={Mm(Tw)} r={Mm(R)} мм"
                : $"двутавр h={Mm(H)} bf1={Mm(Bf1)} tf1={Mm(Tf1)} bf2={Mm(BfBottom)} tf2={Mm(TfBottom)} tw={Mm(Tw)} мм",
            SteelProfileKind.Channel => $"швеллер h={Mm(H)} b={Mm(Bf1)} tf={Mm(Tf1)} tw={Mm(Tw)} r={Mm(R)} мм",
            SteelProfileKind.Tee => $"тавр h={Mm(H)} b={Mm(Bf1)} tf={Mm(Tf1)} tw={Mm(Tw)} мм",
            SteelProfileKind.Box => $"короб h={Mm(H)} b={Mm(Bf1)} tf={Mm(Tf1)} tw={Mm(Tw)} мм",
            SteelProfileKind.Pipe => $"труба D={Mm(H)} t={Mm(Tw)} мм",
            SteelProfileKind.Angle => $"уголок {Mm(H)}×{Mm(Bf1)}×{Mm(Tw)} мм",
            SteelProfileKind.Rect => $"прямоугольник {Mm(Bf1)}×{Mm(H)} мм",
            SteelProfileKind.Round => $"круг d={Mm(H)} мм",
            _ => "произвольное сечение",
        };
    }
}
