using CScore.Sp63Shear;

namespace CScore.ShellSimpl;

/// <summary>
/// Проверка пластины на поперечную силу на 1 м ширины (спека, раздел 8). Все величины — на 1 м:
/// силы кН/м, сопротивления кПа, h0 м, qsw кН/м², Asw/sw — м²/м².
/// </summary>
/// <param name="Q0">Наибольшая поперечная сила Q₀ = √(Qx² + Qy²), кН/м.</param>
/// <param name="ThetaDeg">Направление θ_Q = atan2(Qy, Qx), градусы.</param>
/// <param name="N">Мембранное усилие фасетки θ_Q, кН/м («+» — растяжение).</param>
/// <param name="M">Момент фасетки θ_Q, кН·м/м («+» растягивает верхнюю грань).</param>
/// <param name="TensionFace">Растянутая моментом грань: "top", "bot"; "" — момента нет, принята меньшая h0.</param>
/// <param name="H0">Рабочая высота — меньшая среди направлений x/y у растянутой грани, м.</param>
/// <param name="PhiN">Коэффициент φn по п. 8.1.34.</param>
/// <param name="PhiNToStrip">φn входит в условие (8.55) (только при сжатии).</param>
/// <param name="QStrip">Правая часть (8.55): φn·φb1·Rb·b·h0 (φn — при сжатии), кН/м.</param>
/// <param name="Qb1">Правая часть (8.61): φn·0,5·Rbt·b·h0, кН/м.</param>
/// <param name="EtaStrip">Кисп по (8.55).</param>
/// <param name="EtaConcrete">Кисп по (8.60) без поперечной арматуры.</param>
/// <param name="QswRequired">Требуемое qsw по (8.60), (8.62), не менее 0,25·Rbt·b; 0 — не требуется.</param>
/// <param name="QswMin">Порог 0,25·Rbt·b п. 8.1.33, кН/м².</param>
/// <param name="Rsw">Rsw по табл. 6.15, кПа.</param>
/// <param name="AswPerSw">Требуемая Asw/(sw·1 м) = qsw/Rsw, м²/м².</param>
public sealed record PlateShearResult(
    double Qx, double Qy, double Q0, double ThetaDeg,
    double N, double M, string TensionFace, double H0,
    double Rb, double Rbt, double PhiN, bool PhiNToStrip, string PhiNNote,
    double QStrip, double Qb1, double EtaStrip, double EtaConcrete,
    double QswRequired, double QswMin, double Rsw, double AswPerSw,
    IReadOnlyList<string> Notes)
{
    /// <summary>Кисп,Q проверки заданного сечения (поперечной арматуры у сечения пластины нет):
    /// наибольший из (8.55) и (8.60).</summary>
    public double Eta => Math.Max(EtaStrip, EtaConcrete);

    /// <summary>Поперечная арматура требуется — условие (8.60) без неё не выполнено.</summary>
    public bool StirrupsRequired => EtaConcrete > 1.0;
}

/// <summary>
/// Поперечная сила в пластине (спека, раздел 8): Q(θ) = Qx·cosθ + Qy·sinθ, максимум Q₀ = √(Qx² + Qy²)
/// на фасетке θ_Q = atan2(Qy, Qx). Условия (8.55) и (8.60)–(8.61) п. 8.1.32–8.1.33 СП 63.13330 с φn по
/// п. 8.1.34 (<see cref="PhiNCalculator"/>, формулы <see cref="ShearFormulas"/>); при невыполнении (8.60)
/// без хомутов — требуемое qsw = (Q₀ − Qb1)/h0 по (8.62), не менее 0,25·Rbt·b (иначе хомуты не
/// учитываются, п. 8.1.33), и Asw/sw = qsw/Rsw.
/// </summary>
public static class PlateShearCheck
{
    /// <summary>Поперечная сила ниже порога считается нулевой — проверки нет, кН/м.</summary>
    public const double ZeroShear = 1e-9;

    /// <summary>Кисп при нулевой несущей способности (JSON не принимает ∞).</summary>
    public const double Infinite = 1e9;

    /// <summary>
    /// Проверка при Qx, Qy и мембранно-изгибных усилиях строки; null — Qx = Qy = 0.
    /// <paramref name="asNTotal"/> — площадь всей продольной арматуры, приведённая к фасетке θ_Q, м²/м,
    /// для A_red в φn; null — из сечения (в подборе — из подобранных сеток).
    /// </summary>
    public static PlateShearResult? Check(double qx, double qy,
        double nx, double ny, double nxy, double mx, double my, double mxy,
        PlateSection section, Material concreteMat, Material rebarMat, CalcType calcType,
        double? asNTotal = null)
    {
        double q0 = Math.Sqrt(qx * qx + qy * qy);
        if (q0 <= ZeroShear) return null;

        var notes = new List<string>();
        double theta = Math.Atan2(qy, qx);
        double c = Math.Cos(theta), s = Math.Sin(theta);
        double c2 = c * c, s2 = s * s, cs = c * s;
        double n = nx * c2 + ny * s2 + 2.0 * nxy * cs;
        double m = mx * c2 + my * s2 + 2.0 * mxy * cs;

        double h = section.H;
        string face = m > InclinedSectionGeometryPair.MomentEpsilon ? "top"
            : m < -InclinedSectionGeometryPair.MomentEpsilon ? "bot" : "";
        double? h0Top = FaceH0(section, true), h0Bot = FaceH0(section, false);
        double? h0 = face switch
        {
            "top" => h0Top,
            "bot" => h0Bot,
            _ => h0Top is double t && h0Bot is double b ? Math.Min(t, b) : h0Top ?? h0Bot,
        };
        if (face == "") notes.Add("Момент на фасетке θ_Q нулевой — принята меньшая рабочая высота граней.");
        if (h0 is not double h0v)
        {
            h0v = 0.9 * h;
            notes.Add("У растянутой грани нет продольной арматуры — принято h0 = 0,9·h.");
        }

        var cc = concreteMat.chars[calcType];
        var rc = rebarMat.chars[calcType];
        double rb = Math.Abs(cc.Fc), rbt = Math.Abs(cc.Ft);

        double asN = asNTotal ?? section.RebarLayers.Sum(l => l.Asx * c2 + l.Asy * s2);
        var geometry = new InclinedSectionGeometry(
            B: 1.0, H0: h0v, Ns: 0.0, As: 0.0, Rb: rb, Rbt: rbt, Ab: h, AsTotal: asN,
            Eb: concreteMat.E,
            Eb0: Strain(cc.Ec0, cc.Ec1Red, InclinedSectionGeometry.DefaultEb0),
            Ebt0: Strain(cc.Et0, cc.Et1Red, InclinedSectionGeometry.DefaultEbt0),
            Plane: ShearPlane.Vy, TensionOnPositiveSide: face != "bot", Warnings: [],
            Es: rc.E > 0.0 ? rc.E : InclinedSectionGeometry.DefaultEs);
        // Пластина с мембранным усилием — «остальные элементы» п. 8.1.34; при N = 0 φn = 1.
        var phi = PhiNCalculator.Compute(ElementKind.Other, n, geometry);

        var input = new ShearInclinedInput(
            B: 1.0, H0: h0v, Rb: rb, Rbt: rbt, Qsw: 0.0, Sw: 0.0, Ns: 0.0,
            Kind: ElementKind.Other, AnchorageFactor: 1.0,
            StationStep: 0.0, ProjectionStep: 0.0, MomentZoneLength: 0.0,
            BarCutoffs: [], CheckMoment: false, PhiNOverride: null);
        double qStrip = ShearFormulas.StripCapacity(input, phi.Value, phi.AppliesToStrip);
        double qb1 = ShearFormulas.MinConcreteShear(input, phi.Value, 0.0);
        double etaStrip = Ratio(q0, qStrip), etaConcrete = Ratio(q0, qb1);

        double qswMin = 0.25 * rbt * 1.0;
        double rsw = StirrupResolver.RswFromChars(rc, out string? rswNote);
        double qswReq = 0.0, asw = 0.0;
        if (etaConcrete > 1.0)
        {
            // (8.60): Q₀ ≤ Qb1 + qsw·h0 → qsw ≥ (Q₀ − Qb1)/h0; меньше 0,25·Rbt·b хомуты не учитываются.
            qswReq = Math.Max((q0 - qb1) / h0v, qswMin);
            asw = rsw > 0.0 ? qswReq / rsw : Infinite;
            if (rswNote is not null) notes.Add(rswNote);
        }
        if (etaStrip > 1.0)
            notes.Add("Условие (8.55) по бетонной полосе не выполняется — поперечная арматура не помогает, "
                + "нужна большая толщина или класс бетона.");

        return new PlateShearResult(
            qx, qy, q0, theta * 180.0 / Math.PI, n, m, face, h0v,
            rb, rbt, phi.Value, phi.AppliesToStrip, phi.Explanation,
            qStrip, qb1, etaStrip, etaConcrete, qswReq, qswMin, rsw, asw, notes);
    }

    /// <summary>
    /// Меньшая из рабочих высот направлений x и y у грани: h/2 + z̄ (верх) или h/2 − z̄ (низ), z̄ —
    /// центр тяжести рядов направления у грани (без площадей — как в подборе — среднее привязок рядов);
    /// null — у грани нет рядов.
    /// </summary>
    static double? FaceH0(PlateSection section, bool top)
    {
        double half = section.H / 2.0;
        double? Dir(Func<PlateRebarLayer, double> area, Func<PlateRebarLayer, double> z)
        {
            var rows = section.RebarLayers.Where(l => top ? z(l) > 0.0 : z(l) <= 0.0).ToList();
            if (rows.Count == 0) return null;
            double a = rows.Sum(area);
            double zc = a > 1e-14 ? rows.Sum(l => area(l) * z(l)) / a : rows.Average(z);
            return top ? half + zc : half - zc;
        }
        double? hx = Dir(l => l.Asx, l => l.Zsx), hy = Dir(l => l.Asy, l => l.Zsy);
        return hx is double x && hy is double y ? Math.Min(x, y) : hx ?? hy;
    }

    /// <summary>Предельная деформация: основное значение, запасное либо величина по умолчанию.</summary>
    static double Strain(double primary, double fallback, double defaultValue)
    {
        if (Math.Abs(primary) > 1e-12) return Math.Abs(primary);
        if (Math.Abs(fallback) > 1e-12) return Math.Abs(fallback);
        return defaultValue;
    }

    static double Ratio(double applied, double capacity) =>
        capacity > 1e-12 ? applied / capacity : Infinite;
}
