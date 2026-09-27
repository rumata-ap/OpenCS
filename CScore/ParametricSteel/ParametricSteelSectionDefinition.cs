using CScore.Sp16;

namespace CScore.ParametricSteel;

/// <summary>
/// Ссылка на строку сортамента: для отображения и повторного выбора, контур от неё не зависит.
/// It — справочный момент инерции при свободном кручении по сортаменту, м⁴ (0 — нет); передаётся в
/// расчёт по СП 16 вместо приближённой формулы прил. Д, пока размеры совпадают со строкой сортамента.
/// </summary>
public sealed record ParametricSteelCatalogRef(string Group, string Standard, string Name, double It = 0);

/// <summary>
/// Параметрический источник стального сечения в единицах СИ (м). Каноническое положение — как у
/// <see cref="SteelProfile"/>: стенка вертикальна, швеллер стенкой слева, тавр полкой сверху,
/// уголок пяткой в левом нижнем углу.
/// </summary>
/// <remarks>
/// Использование полей по видам: двутавр — H, Bf1/Tf1 (верхний пояс), Bf2/Tf2 (нижний пояс сварного,
/// 0 — как верхний), Tw, R1 (у стенки), R2 (у пера), FlangeSlope; швеллер — H, Bf1, Tf1, Tw, R1, R2,
/// FlangeSlope (гнутый: Tw — толщина, R1 — внутренний радиус гиба); тавр — H, Bf1, Tf1, Tw;
/// уголок — H (вертикальная полка), Bf1 (горизонтальная), Tw (толщина), R1 (у пятки), R2 (у пера);
/// короб — H, Bf1, Tf1 (пояса), Tw (стенки), R1 (гнутый: внутренний радиус, толщина — Tw);
/// труба — H (наружный диаметр), Tw; лист — H, Bf1; круг — H (диаметр).
/// </remarks>
public sealed record ParametricSteelSectionDefinition
{
    /// <summary>Версия JSON-контракта исходного описания.</summary>
    public const int CurrentVersion = 1;

    /// <summary>Вид сечения.</summary>
    public SteelProfileKind Kind { get; init; }
    /// <summary>Способ изготовления.</summary>
    public SteelFabrication Fabrication { get; init; }
    /// <summary>Высота (диаметр трубы и круга, вертикальная полка уголка), м.</summary>
    public double H { get; init; }
    /// <summary>Ширина (верхнего) пояса, полки, листа, м.</summary>
    public double Bf1 { get; init; }
    /// <summary>Толщина (верхнего) пояса, м; для полки с уклоном — посередине свеса.</summary>
    public double Tf1 { get; init; }
    /// <summary>Ширина нижнего пояса сварного двутавра, м (0 — как верхний).</summary>
    public double Bf2 { get; init; }
    /// <summary>Толщина нижнего пояса сварного двутавра, м (0 — как верхний).</summary>
    public double Tf2 { get; init; }
    /// <summary>Толщина стенки (стенок короба, трубы, уголка, гнутого профиля), м.</summary>
    public double Tw { get; init; }
    /// <summary>Радиус сопряжения у стенки/пятки (прокат) или внутренний радиус гиба (гнутый), м.</summary>
    public double R1 { get; init; }
    /// <summary>Радиус закругления у пера полки (прокат), м.</summary>
    public double R2 { get; init; }
    /// <summary>
    /// Уклон внутренних граней полок (0,12 = 12 %), 0 — параллельные грани. Справочные A, Ix, Iy
    /// ГОСТ 8239 соответствуют уклону 12 %, ГОСТ 8240 (серия «У») — 10 %.
    /// </summary>
    public double FlangeSlope { get; init; }
    /// <summary>Контур повёрнут на 90° относительно канонического положения.</summary>
    public bool Rotated90 { get; init; }
    /// <summary>Зеркальное положение: швеллер стенкой справа, тавр полкой снизу, уголок пером вниз.</summary>
    public bool Flipped { get; init; }
    /// <summary>Строка сортамента, из которой взяты размеры; null — «по размерам».</summary>
    public ParametricSteelCatalogRef? Catalog { get; init; }
    /// <summary>Id стального материала в проекте.</summary>
    public int MaterialId { get; init; }
    /// <summary>Метка сечения.</summary>
    public string Tag { get; init; } = "Параметрическое сечение";

    /// <summary>Ширина нижнего пояса с учётом умолчания.</summary>
    public double BfBottom => Bf2 > 0 ? Bf2 : Bf1;
    /// <summary>Толщина нижнего пояса с учётом умолчания.</summary>
    public double TfBottom => Tf2 > 0 ? Tf2 : Tf1;

    /// <summary>Допустимые способы изготовления для вида.</summary>
    public static IReadOnlyList<SteelFabrication> AllowedFabrications(SteelProfileKind kind) => kind switch
    {
        SteelProfileKind.IBeam => [SteelFabrication.Rolled, SteelFabrication.Welded],
        SteelProfileKind.Channel => [SteelFabrication.Rolled, SteelFabrication.Bent, SteelFabrication.Welded],
        SteelProfileKind.Tee => [SteelFabrication.Welded],
        SteelProfileKind.Angle => [SteelFabrication.Rolled, SteelFabrication.Bent],
        SteelProfileKind.Box => [SteelFabrication.Bent, SteelFabrication.Welded],
        SteelProfileKind.Pipe => [SteelFabrication.Rolled, SteelFabrication.Welded],
        SteelProfileKind.Rect => [SteelFabrication.Welded, SteelFabrication.Rolled],
        SteelProfileKind.Round => [SteelFabrication.Rolled],
        _ => [],
    };

    /// <summary>Допустим ли поворот на 90° (для симметричных тел вращения и уголка — нет).</summary>
    public static bool CanRotate(SteelProfileKind kind) =>
        kind is SteelProfileKind.IBeam or SteelProfileKind.Channel or SteelProfileKind.Tee or SteelProfileKind.Box;

    /// <summary>Допустимо ли зеркальное положение.</summary>
    public static bool CanFlip(SteelProfileKind kind) =>
        kind is SteelProfileKind.Channel or SteelProfileKind.Tee or SteelProfileKind.Angle;

    /// <summary>Прокатный двутавр (R2, уклон — для ГОСТ 8239).</summary>
    public static ParametricSteelSectionDefinition RolledIBeam(double h, double b, double tw, double tf,
        double r1, double r2 = 0, double slope = 0) =>
        new() { Kind = SteelProfileKind.IBeam, Fabrication = SteelFabrication.Rolled, H = h, Bf1 = b, Tf1 = tf, Tw = tw, R1 = r1, R2 = r2, FlangeSlope = slope };

    /// <summary>Сварной двутавр, в том числе с разными поясами.</summary>
    public static ParametricSteelSectionDefinition WeldedIBeam(double h, double bf1, double tf1,
        double bf2, double tf2, double tw) =>
        new() { Kind = SteelProfileKind.IBeam, Fabrication = SteelFabrication.Welded, H = h, Bf1 = bf1, Tf1 = tf1, Bf2 = bf2, Tf2 = tf2, Tw = tw };

    /// <summary>Прокатный швеллер.</summary>
    public static ParametricSteelSectionDefinition RolledChannel(double h, double b, double tw, double tf,
        double r1, double r2 = 0, double slope = 0) =>
        new() { Kind = SteelProfileKind.Channel, Fabrication = SteelFabrication.Rolled, H = h, Bf1 = b, Tf1 = tf, Tw = tw, R1 = r1, R2 = r2, FlangeSlope = slope };

    /// <summary>Гнутый швеллер (r — внутренний радиус гиба).</summary>
    public static ParametricSteelSectionDefinition BentChannel(double h, double b, double t, double r) =>
        new() { Kind = SteelProfileKind.Channel, Fabrication = SteelFabrication.Bent, H = h, Bf1 = b, Tf1 = t, Tw = t, R1 = r };

    /// <summary>Сварной швеллер.</summary>
    public static ParametricSteelSectionDefinition WeldedChannel(double h, double b, double tw, double tf) =>
        new() { Kind = SteelProfileKind.Channel, Fabrication = SteelFabrication.Welded, H = h, Bf1 = b, Tf1 = tf, Tw = tw };

    /// <summary>Сварной тавр.</summary>
    public static ParametricSteelSectionDefinition WeldedTee(double h, double b, double tw, double tf) =>
        new() { Kind = SteelProfileKind.Tee, Fabrication = SteelFabrication.Welded, H = h, Bf1 = b, Tf1 = tf, Tw = tw };

    /// <summary>Прокатный уголок: h — вертикальная полка, b — горизонтальная.</summary>
    public static ParametricSteelSectionDefinition RolledAngle(double h, double b, double t, double r1, double r2 = 0) =>
        new() { Kind = SteelProfileKind.Angle, Fabrication = SteelFabrication.Rolled, H = h, Bf1 = b, Tf1 = t, Tw = t, R1 = r1, R2 = r2 };

    /// <summary>Гнутый уголок (r — внутренний радиус гиба).</summary>
    public static ParametricSteelSectionDefinition BentAngle(double h, double b, double t, double r) =>
        new() { Kind = SteelProfileKind.Angle, Fabrication = SteelFabrication.Bent, H = h, Bf1 = b, Tf1 = t, Tw = t, R1 = r };

    /// <summary>Сварной короб.</summary>
    public static ParametricSteelSectionDefinition WeldedBox(double h, double b, double tw, double tf) =>
        new() { Kind = SteelProfileKind.Box, Fabrication = SteelFabrication.Welded, H = h, Bf1 = b, Tf1 = tf, Tw = tw };

    /// <summary>Гнутый замкнутый профиль (r — внутренний радиус гиба).</summary>
    public static ParametricSteelSectionDefinition BentBox(double h, double b, double t, double r) =>
        new() { Kind = SteelProfileKind.Box, Fabrication = SteelFabrication.Bent, H = h, Bf1 = b, Tf1 = t, Tw = t, R1 = r };

    /// <summary>Круглая труба.</summary>
    public static ParametricSteelSectionDefinition Pipe(double d, double t) =>
        new() { Kind = SteelProfileKind.Pipe, Fabrication = SteelFabrication.Rolled, H = d, Tw = t };

    /// <summary>Лист (полоса) b×h.</summary>
    public static ParametricSteelSectionDefinition Plate(double b, double h) =>
        new() { Kind = SteelProfileKind.Rect, Fabrication = SteelFabrication.Welded, H = h, Bf1 = b };

    /// <summary>Сплошной круг.</summary>
    public static ParametricSteelSectionDefinition RoundBar(double d) =>
        new() { Kind = SteelProfileKind.Round, Fabrication = SteelFabrication.Rolled, H = d };
}
