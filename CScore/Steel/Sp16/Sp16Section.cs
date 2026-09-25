namespace CScore.Sp16;

/// <summary>Тип сечения по табл. 7 СП 16 (коэффициенты α, β формулы (9)).</summary>
public enum SectionCurve { a = 0, b = 1, c = 2 }

/// <summary>
/// Расчётные характеристики стали, кПа: Ry, Ru — расчётные (характеристики C материала),
/// Ryn, Run — нормативные (характеристики N; если нет — null), E — модуль упругости.
/// </summary>
public sealed record SteelMaterialProps(double Ry, double Ru, double? Ryn, double? Run, double E)
{
    /// <summary>Модуль упругости проката по табл. Б.1: 2,06·10⁵ Н/мм² = 2,06·10⁸ кПа.</summary>
    public const double DefaultE = 2.06e8;

    /// <summary>Расчётное сопротивление сдвигу Rs = 0,58·Ryn/γm = 0,58·Ry (табл. 2).</summary>
    public double Rs => 0.58 * Ry;

    /// <summary>Нормативное сопротивление для условий «Ryn ≤ 440 Н/мм²»; при отсутствии — Ry.</summary>
    public double RynOrRy => Ryn ?? Ry;

    /// <summary>Из материала OpenCS (расчётные характеристики — C, нормативные — N).</summary>
    public static SteelMaterialProps FromMaterial(Material m)
    {
        var c = m.C ?? throw new ArgumentException("У материала нет расчётных характеристик (C)");
        var n = m.N;
        double e = m.E > 0 ? m.E : (c.E > 0 ? c.E : DefaultE);
        return new SteelMaterialProps(c.Ry, c.Ru, n?.Ry > 0 ? n.Ry : null, n?.Ru > 0 ? n.Ru : null, e);
    }
}

/// <summary>
/// Стальное сечение для проверок по СП 16: полигон в каноническом положении профиля,
/// дескриптор профиля и материал. Все величины — в осях канонического положения
/// (x — горизонтальная ось, для двутавра/швеллера/тавра — ось наибольшей жёсткости);
/// перестановку осей контура (<see cref="SteelProfile.Rotated90"/>) выполняет вызывающий код.
/// Единицы: м, кПа.
/// </summary>
public sealed class Sp16Section
{
    /// <summary>Полигон в каноническом положении.</summary>
    public PolygonSection Poly { get; }
    /// <summary>Профиль.</summary>
    public SteelProfile Profile { get; }
    /// <summary>Материал.</summary>
    public SteelMaterialProps Mat { get; }

    public Sp16Section(PolygonSection canonicalPoly, SteelProfile profile, SteelMaterialProps mat)
    {
        Poly = canonicalPoly;
        Profile = profile;
        Mat = mat;
    }

    /// <summary>Строит сечение по контуру (в осях контура) с распознаванием профиля, если он не задан.</summary>
    public static Sp16Section FromContour(PolygonSection contourPoly, SteelProfile? profile, SteelMaterialProps mat)
    {
        var p = profile ?? SteelProfileRecognizer.Recognize(contourPoly);
        var poly = p.Rotated90 ? contourPoly.SwapAxes() : contourPoly;
        return new Sp16Section(poly, p, mat);
    }

    public SteelProfileKind Kind => Profile.Kind;
    bool Rolled => Profile.Fabrication == SteelFabrication.Rolled;

    // ── Геометрия полигона ─────────────────────────────────────────────

    /// <summary>Площадь брутто, м².</summary>
    public double A => Poly.A;
    /// <summary>Момент инерции относительно оси x, м⁴.</summary>
    public double Ix => Poly.Ix;
    /// <summary>Момент инерции относительно оси y, м⁴.</summary>
    public double Iy => Poly.Iy;
    /// <summary>Радиус инерции относительно оси x, м.</summary>
    public double ix => Math.Sqrt(Ix / A);
    /// <summary>Радиус инерции относительно оси y, м.</summary>
    public double iy => Math.Sqrt(Iy / A);
    /// <summary>Минимальный радиус инерции (главные оси; для уголка — ось v), м.</summary>
    public double iMin => Math.Sqrt(Poly.PrincipalInertia().IMin / A);

    /// <summary>Расстояние от центра тяжести до верхнего (y&gt;0) крайнего волокна, м.</summary>
    public double YTop => Poly.YMax - Poly.Yc;
    /// <summary>Расстояние до нижнего крайнего волокна, м.</summary>
    public double YBottom => Poly.Yc - Poly.YMin;
    /// <summary>Расстояние до правого крайнего волокна, м.</summary>
    public double XRight => Poly.XMax - Poly.Xc;
    /// <summary>Расстояние до левого крайнего волокна, м.</summary>
    public double XLeft => Poly.Xc - Poly.XMin;

    /// <summary>Момент сопротивления относительно x для верхнего (true) или нижнего волокна, м³.</summary>
    public double Wx(bool top) => Ix / (top ? YTop : YBottom);
    /// <summary>Момент сопротивления относительно y для правого (true) или левого волокна, м³.</summary>
    public double Wy(bool right) => Iy / (right ? XRight : XLeft);
    /// <summary>Wx,min, м³.</summary>
    public double WxMin => Math.Min(Wx(true), Wx(false));
    /// <summary>Wy,min, м³.</summary>
    public double WyMin => Math.Min(Wy(true), Wy(false));

    /// <summary>
    /// Для формул τ = QS/(It): статический момент части сечения по одну сторону от центральной
    /// оси и суммарная толщина на этой оси. aboutX = true — для Qy (изгиб Mx), иначе для Qx.
    /// </summary>
    public (double S, double T) ShearAtCentroid(bool aboutX)
    {
        double level = aboutX ? Poly.Yc : Poly.Xc;
        var (_, s) = Poly.PartAbove(level, aboutX);
        return (Math.Abs(s), Poly.ChordLength(level, aboutX));
    }

    /// <summary>Статический момент (относительно оси x) части сечения выше уровня y (м от ц.т.), м³.</summary>
    public double StaticMomentAboveX(double yFromCentroid) => Math.Abs(Poly.PartAbove(Poly.Yc + yFromCentroid, true).S);

    // ── Размеры по профилю (рис. 5, 7.3.1, 7.3.7) ─────────────────────

    /// <summary>Толщина верхнего пояса, м.</summary>
    public double TfTop => Kind is SteelProfileKind.Tee && Profile.Flipped ? 0 : Profile.Tf1;
    /// <summary>Толщина нижнего пояса, м.</summary>
    public double TfBottom => Kind switch
    {
        SteelProfileKind.IBeam => Profile.TfBottom,
        SteelProfileKind.Channel or SteelProfileKind.Box => Profile.Tf1,
        SteelProfileKind.Tee => Profile.Flipped ? Profile.Tf1 : 0,
        _ => 0,
    };
    /// <summary>Ширина верхнего пояса, м.</summary>
    public double BfTop => Kind is SteelProfileKind.Tee && Profile.Flipped ? 0 : Profile.Bf1;
    /// <summary>Ширина нижнего пояса, м.</summary>
    public double BfBottom => Kind switch
    {
        SteelProfileKind.IBeam => Profile.BfBottom,
        SteelProfileKind.Channel or SteelProfileKind.Box => Profile.Bf1,
        SteelProfileKind.Tee => Profile.Flipped ? Profile.Bf1 : 0,
        _ => 0,
    };

    /// <summary>Толщина одной стенки, м.</summary>
    public double Tw => Profile.Tw;
    /// <summary>Число стенок (2 — короб).</summary>
    public int WebCount => Kind == SteelProfileKind.Box ? 2 : 1;

    /// <summary>Полная высота стенки hw (между поясами), м.</summary>
    public double Hw => Kind switch
    {
        SteelProfileKind.IBeam or SteelProfileKind.Channel or SteelProfileKind.Box => Profile.H - TfTop - TfBottom,
        SteelProfileKind.Tee => Profile.H - Profile.Tf1,
        _ => 0,
    };

    /// <summary>Расчётная высота стенки hef по 7.3.1 (рис. 5), м.</summary>
    public double Hef => Kind switch
    {
        SteelProfileKind.IBeam or SteelProfileKind.Channel => Rolled || Profile.Fabrication == SteelFabrication.Bent ? Hw - 2 * Profile.R : Hw,
        SteelProfileKind.Tee => Rolled ? Hw - Profile.R : Hw,
        SteelProfileKind.Box => Profile.Fabrication == SteelFabrication.Bent ? Profile.H - 2 * Profile.R : Hw,
        _ => 0,
    };

    /// <summary>Расчётная ширина свеса пояса (полки) bef по 7.3.7, м; для короба — свес отсутствует (0).</summary>
    public double BefTop => OverhangOf(BfTop);
    /// <summary>Расчётная ширина свеса нижнего пояса, м.</summary>
    public double BefBottom => OverhangOf(BfBottom);

    double OverhangOf(double bf)
    {
        if (bf <= 0) return 0;
        double r = Rolled || Profile.Fabrication == SteelFabrication.Bent ? Profile.R : 0;
        return Kind switch
        {
            SteelProfileKind.IBeam or SteelProfileKind.Tee => (bf - Profile.Tw) / 2 - r,
            SteelProfileKind.Channel => bf - Profile.Tw - r,
            SteelProfileKind.Angle => bf - Profile.Tw - r,
            _ => 0,
        };
    }

    /// <summary>Расчётная ширина поясного листа коробчатого сечения bef,1 (между стенками), м.</summary>
    public double BefBoxFlange => Kind != SteelProfileKind.Box ? 0
        : Profile.Fabrication == SteelFabrication.Bent ? Profile.Bf1 - 2 * Profile.R : Profile.Bf1 - 2 * Profile.Tw;

    /// <summary>Площадь верхнего пояса, м².</summary>
    public double AfTop => BfTop * TfTop;
    /// <summary>Площадь нижнего пояса, м².</summary>
    public double AfBottom => BfBottom * TfBottom;
    /// <summary>Площадь стенки (стенок короба) Aw = hw·tw·n, м².</summary>
    public double Aw => Hw * Tw * WebCount;

    /// <summary>
    /// Момент инерции при свободном кручении It = (k/3)·Σbi·ti³ (прил. Д, п. 1): k = 1,29 — двутавр с
    /// двумя осями симметрии; 1,25 — с одной осью; 1,20 — тавр; 1,12 — швеллер. Для прочих — 0.
    /// </summary>
    public double It => Kind switch
    {
        SteelProfileKind.IBeam => (Profile.IsDoublySymmetricIBeam ? 1.29 : 1.25) / 3
            * (BfTop * Math.Pow(TfTop, 3) + BfBottom * Math.Pow(TfBottom, 3) + Hw * Math.Pow(Tw, 3)),
        SteelProfileKind.Tee => 1.20 / 3 * (Profile.Bf1 * Math.Pow(Profile.Tf1, 3) + Hw * Math.Pow(Tw, 3)),
        SteelProfileKind.Channel => 1.12 / 3 * (2 * Profile.Bf1 * Math.Pow(Profile.Tf1, 3) + Hw * Math.Pow(Tw, 3)),
        _ => 0,
    };

    /// <summary>Сечение симметрично относительно вертикальной оси y (двутавр, тавр, короб, труба, прямоугольник, круг).</summary>
    public bool SymmetricAboutY => Kind is SteelProfileKind.IBeam or SteelProfileKind.Tee or SteelProfileKind.Box
        or SteelProfileKind.Pipe or SteelProfileKind.Rect or SteelProfileKind.Round;

    /// <summary>Сечение симметрично относительно горизонтальной оси x.</summary>
    public bool SymmetricAboutX => Kind switch
    {
        SteelProfileKind.IBeam => Profile.IsDoublySymmetricIBeam,
        SteelProfileKind.Channel or SteelProfileKind.Box or SteelProfileKind.Pipe or SteelProfileKind.Rect or SteelProfileKind.Round => true,
        _ => false,
    };

    // ── Табл. 7: тип сечения для φ ────────────────────────────────────

    /// <summary>
    /// Тип сечения по табл. 7 для потери устойчивости при изгибе относительно оси x (aboutX = true) или y.
    /// Возвращает null, если сечение в табл. 7 не приведено (Generic, сплошной круг).
    /// </summary>
    public SectionCurve? CurveFor(bool aboutX) => Kind switch
    {
        SteelProfileKind.Pipe => SectionCurve.a,
        SteelProfileKind.Box => Profile.Fabrication == SteelFabrication.Bent ? SectionCurve.a : SectionCurve.b,
        SteelProfileKind.Rect => SectionCurve.b,
        // Прим. 1: прокатные двутавры высотой свыше 500 мм в плоскости стенки — тип a.
        SteelProfileKind.IBeam => aboutX ? (Rolled && Profile.H > 0.5 ? SectionCurve.a : SectionCurve.b) : SectionCurve.c,
        SteelProfileKind.Channel or SteelProfileKind.Tee => SectionCurve.c,
        // Одиночный уголок относительно главных осей (рис. табл. 7, тип b).
        SteelProfileKind.Angle => SectionCurve.b,
        _ => null,
    };
}
