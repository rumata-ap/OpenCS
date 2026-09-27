namespace CScore.Sp16;

/// <summary>Вид проверки стального элемента по СП 16 (набор проверок — спека §7).</summary>
public enum Sp16TaskKind
{
    /// <summary>Автоматически по знаку N и наличию M, Q (steel_check).</summary>
    Auto = 0,
    /// <summary>Центральное растяжение: 7.1.1, 10.4 (табл. 33).</summary>
    CentralTension = 1,
    /// <summary>Центральное сжатие: 7.1.1, 7.1.3, 7.3, 10.4 (табл. 32).</summary>
    CentralCompression = 2,
    /// <summary>Изгиб: 8.2, 8.4, 8.5.</summary>
    Bending = 3,
    /// <summary>Сдвиг: (42), (44), (54)/(55).</summary>
    Shear = 4,
    /// <summary>Сжатие с изгибом: 9.1, 9.2, 9.4, 10.4.</summary>
    CompressionBending = 5,
    /// <summary>Растяжение с изгибом: 9.1, 9.4.10 (8.5), 10.4.</summary>
    TensionBending = 6,
    /// <summary>Предельная гибкость 10.4.</summary>
    Constructive = 7,
}

/// <summary>Результат проверки элемента по СП 16 для одного сочетания усилий.</summary>
public sealed class Sp16Report
{
    /// <summary>Запрошенный вид проверки.</summary>
    public Sp16TaskKind RequestedKind { get; init; }
    /// <summary>Выполненный вид проверки (для <see cref="Sp16TaskKind.Auto"/> — выбранный по усилиям).</summary>
    public Sp16TaskKind Kind { get; init; }
    /// <summary>Усилия в осях контура, принятые в расчёте (пренебрежимо малые компоненты обнулены).</summary>
    public SteelForces Forces { get; init; } = new(0, 0, 0, 0, 0);
    /// <summary>Проверки.</summary>
    public List<Sp16CheckResult> Results { get; init; } = [];
    /// <summary>Общие примечания (распознавание профиля, миграция параметров, отброшенные усилия).</summary>
    public List<string> Notes { get; init; } = [];
    /// <summary>
    /// Ошибка постановки — ключ ресурса OpenCS («Sp16InvalidForces», «Sp16KindMismatch»); проверки не выполнялись.
    /// </summary>
    public string? Error { get; init; }
    /// <summary>
    /// Для «Sp16KindMismatch» — код причины несоответствия (NeedTension, NeedCompression, MomentsInTension,
    /// MomentsInCompression, AxialInBending, NoBendingOrShear, NoShear, NoMomentsCompression, NoMomentsTension).
    /// </summary>
    public string? ErrorReason { get; init; }

    /// <summary>Наибольший коэффициент использования среди выполненных проверок (0 — нет ни одной).</summary>
    public double Utilization => Results.Where(r => r.Status != CheckStatus.NotApplicable)
        .Select(r => r.Utilization).DefaultIfEmpty(0).Max();

    /// <summary>Все выполненные проверки удовлетворены.</summary>
    public bool Passed => Error == null && Results.Any(r => r.Status == CheckStatus.Ok)
        && Results.All(r => r.Status != CheckStatus.Fail);
}

/// <summary>
/// Диспетчер проверок стальных элементов по СП 16.13330.2017 (изм. № 1–6): вид задачи → набор проверок
/// разделов 7, 8, 9, 10.4. Усилия — в осях контура сечения (N &gt; 0 — растяжение), кН, кН·м.
/// </summary>
public static class Sp16Checker
{
    /// <summary>
    /// Доля несущей способности, ниже которой компонента усилий считается пренебрежимо малой
    /// (|N| ≤ 0,001·A·Ry, |M| ≤ 0,001·Wmin·Ry, |Q| ≤ 0,001·A·Rs) и обнуляется с примечанием.
    /// </summary>
    public const double NegligibleRatio = 1e-3;

    /// <summary>Код вида задачи OpenCS → вид проверки; null — не стальная задача СП 16.</summary>
    public static Sp16TaskKind? ParseKind(string? taskKind) => taskKind switch
    {
        "steel_check" => Sp16TaskKind.Auto,
        "steel_central_tension" => Sp16TaskKind.CentralTension,
        "steel_central_compression" => Sp16TaskKind.CentralCompression,
        "steel_bending" => Sp16TaskKind.Bending,
        "steel_shear" => Sp16TaskKind.Shear,
        "steel_compression_bending" => Sp16TaskKind.CompressionBending,
        "steel_tension_bending" => Sp16TaskKind.TensionBending,
        "steel_constructive" => Sp16TaskKind.Constructive,
        _ => null,
    };

    /// <summary>Вид проверки → код вида задачи OpenCS.</summary>
    public static string KindCode(Sp16TaskKind kind) => kind switch
    {
        Sp16TaskKind.Auto => "steel_check",
        Sp16TaskKind.CentralTension => "steel_central_tension",
        Sp16TaskKind.CentralCompression => "steel_central_compression",
        Sp16TaskKind.Bending => "steel_bending",
        Sp16TaskKind.Shear => "steel_shear",
        Sp16TaskKind.CompressionBending => "steel_compression_bending",
        Sp16TaskKind.TensionBending => "steel_tension_bending",
        Sp16TaskKind.Constructive => "steel_constructive",
        _ => throw new ArgumentOutOfRangeException(nameof(kind)),
    };

    static string KindName(Sp16TaskKind kind) => kind switch
    {
        Sp16TaskKind.CentralTension => "«Центральное растяжение»",
        Sp16TaskKind.CentralCompression => "«Центральное сжатие»",
        Sp16TaskKind.Bending => "«Изгиб»",
        Sp16TaskKind.Shear => "«Сдвиг»",
        Sp16TaskKind.CompressionBending => "«Сжатие с изгибом»",
        Sp16TaskKind.TensionBending => "«Растяжение с изгибом»",
        Sp16TaskKind.Constructive => "«Предельная гибкость»",
        _ => "«Проверка по СП 16»",
    };

    /// <summary>
    /// Обнуляет пренебрежимо малые компоненты усилий (в канонических осях элемента) с примечанием.
    /// </summary>
    public static SteelForces DropNegligible(Sp16Member m, SteelForces canonical, List<string> notes)
    {
        var s = m.S;
        double nLim = NegligibleRatio * s.A * s.Mat.Ry;
        double mxLim = NegligibleRatio * s.WxMin * s.Mat.Ry;
        double myLim = NegligibleRatio * s.WyMin * s.Mat.Ry;
        double qLim = NegligibleRatio * s.A * s.Mat.Rs;
        double Keep(double v, double lim, string name, string unit)
        {
            if (v == 0 || Math.Abs(v) > lim) return v;
            notes.Add($"{name} = {v:G4} {unit} пренебрежимо мало (менее 0,1 % несущей способности) — принято равным нулю");
            return 0;
        }
        return new SteelForces(
            Keep(canonical.N, nLim, "N", "кН"),
            Keep(canonical.Mx, mxLim, $"M{m.Axis(true)}", "кН·м"),
            Keep(canonical.My, myLim, $"M{m.Axis(false)}", "кН·м"),
            Keep(canonical.Qx, qLim, $"Q{m.Axis(true)}", "кН"),
            Keep(canonical.Qy, qLim, $"Q{m.Axis(false)}", "кН"));
    }

    /// <summary>Вид проверки по усилиям (канонические оси, пренебрежимо малые уже обнулены).</summary>
    public static Sp16TaskKind Classify(SteelForces f)
    {
        bool hasM = f.Mx != 0 || f.My != 0, hasQ = f.Qx != 0 || f.Qy != 0;
        if (f.N > 0) return hasM ? Sp16TaskKind.TensionBending : Sp16TaskKind.CentralTension;
        if (f.N < 0) return hasM ? Sp16TaskKind.CompressionBending : Sp16TaskKind.CentralCompression;
        if (hasM) return Sp16TaskKind.Bending;
        return hasQ ? Sp16TaskKind.Shear : Sp16TaskKind.Constructive;
    }

    /// <summary>Код причины несоответствия усилий виду задачи (см. <see cref="Sp16Report.ErrorReason"/>); null — соответствуют.</summary>
    static string? Mismatch(Sp16TaskKind kind, SteelForces f)
    {
        bool hasM = f.Mx != 0 || f.My != 0, hasQ = f.Qx != 0 || f.Qy != 0;
        return kind switch
        {
            Sp16TaskKind.CentralTension when f.N <= 0 => "NeedTension",
            Sp16TaskKind.CentralTension when hasM => "MomentsInTension",
            Sp16TaskKind.CentralCompression when f.N >= 0 => "NeedCompression",
            Sp16TaskKind.CentralCompression when hasM => "MomentsInCompression",
            Sp16TaskKind.Bending when f.N != 0 => "AxialInBending",
            Sp16TaskKind.Bending when !hasM && !hasQ => "NoBendingOrShear",
            Sp16TaskKind.Shear when !hasQ => "NoShear",
            Sp16TaskKind.CompressionBending when f.N >= 0 => "NeedCompression",
            Sp16TaskKind.CompressionBending when !hasM => "NoMomentsCompression",
            Sp16TaskKind.TensionBending when f.N <= 0 => "NeedTension",
            Sp16TaskKind.TensionBending when !hasM => "NoMomentsTension",
            _ => null,
        };
    }

    /// <summary>
    /// Выполняет проверки вида <paramref name="kind"/> для усилий в осях контура сечения.
    /// </summary>
    public static Sp16Report Run(Sp16Member m, SteelForces contourForces, Sp16TaskKind kind)
    {
        if (!Enum.IsDefined(kind) || new[] { contourForces.N, contourForces.Mx, contourForces.My,
                contourForces.Qx, contourForces.Qy }.Any(v => !double.IsFinite(v)))
            return new Sp16Report { RequestedKind = kind, Kind = kind, Error = "Sp16InvalidForces" };
        var notes = new List<string>(m.Notes);
        var f = DropNegligible(m, m.ToCanonical(contourForces), notes);
        var effective = kind == Sp16TaskKind.Auto ? Classify(f) : kind;
        var accepted = m.ToCanonical(f);   // перестановка осей обратима
        if (kind == Sp16TaskKind.Auto)
        {
            notes.Add($"Вид проверки выбран по усилиям: {KindName(effective)}");
            if (effective == Sp16TaskKind.Constructive)
                notes.Add("Усилия не заданы (нулевые) — выполнена только проверка предельной гибкости");
        }
        else if (Mismatch(kind, f) is { } reason)
            return new Sp16Report { RequestedKind = kind, Kind = kind, Forces = accepted, Notes = notes,
                Error = "Sp16KindMismatch", ErrorReason = reason };

        var res = new List<Sp16CheckResult>();
        switch (effective)
        {
            case Sp16TaskKind.CentralTension:
                res.Add(Sp16Section7.Strength(m, f));
                res.AddRange(Sp16Section8Strength.Shear(m, f));
                res.AddRange(Sp16Slenderness.Check(m, f));
                break;
            case Sp16TaskKind.CentralCompression:
                res.Add(Sp16Section7.Strength(m, f));
                res.AddRange(Sp16Section7.Stability(m, f));
                res.AddRange(Sp16Section7.LocalStability(m, f));
                res.AddRange(Sp16Section8Strength.Shear(m, f));
                res.AddRange(Sp16Slenderness.Check(m, f));
                break;
            case Sp16TaskKind.Bending:
                res.AddRange(Sp16Section8Strength.Check(m, f));
                res.AddRange(Sp16Section8Stability.Check(m, f));
                res.AddRange(Sp16Section8Local.Check(m, f));
                break;
            case Sp16TaskKind.Shear:
                res.AddRange(ShearOnly(m, f, notes));
                break;
            case Sp16TaskKind.CompressionBending:
            {
                res.AddRange(Sp16Section9.Strength(m, f));
                var inPlane = Sp16Section9.InPlaneStability(m, f);
                res.AddRange(inPlane);
                res.AddRange(Sp16Section9.OutOfPlaneStability(m, f));
                res.AddRange(Sp16Section9Local.Check(m, f));
                res.AddRange(Sp16Section8Strength.Shear(m, f));
                double? phiE = MinPhiE(inPlane);
                if (phiE != null)
                    notes.Add($"Предельная гибкость (табл. 32): в α вместо φ принят φe = {phiE:0.###} (прим. 1 табл. 32)");
                res.AddRange(Sp16Slenderness.Check(m, f, phiE));
                break;
            }
            case Sp16TaskKind.TensionBending:
                res.AddRange(Sp16Section9.Strength(m, f));
                res.AddRange(Sp16Section8Local.Check(m, f));
                notes.Add("9.4.10: местная устойчивость стенок и поясов растянуто-изгибаемого элемента проверена как для изгибаемого (8.5)");
                res.AddRange(Sp16Section8Strength.Shear(m, f));
                res.AddRange(Sp16Slenderness.Check(m, f));
                break;
            case Sp16TaskKind.Constructive:
                res.AddRange(Sp16Slenderness.Check(m, f));
                break;
        }
        return new Sp16Report { RequestedKind = kind, Kind = effective, Forces = accepted, Results = res, Notes = notes };
    }

    /// <summary>Сдвиг: (42), (44) при Mx; (54)/(55) — опорное сечение (M = 0) при учёте пластических деформаций.</summary>
    static List<Sp16CheckResult> ShearOnly(Sp16Member m, SteelForces f, List<string> notes)
    {
        var res = new List<Sp16CheckResult>();
        if (f.N != 0) notes.Add("Продольная сила в проверке на сдвиг не учитывается");
        if (m.P.AllowPlastic && f.Mx == 0 && f.My == 0 && Sp16Section8Strength.PlasticNotApplicableReason(m) == null)
            res.AddRange(Sp16Section8Strength.Check(m, f with { N = 0 }).Where(r => r.Formula is "(54)" or "(55)"));
        res.AddRange(Sp16Section8Strength.Shear(m, f));
        if (Sp16Section8Strength.WebCombined(m, f) is { } r44) res.Add(r44);
        return res;
    }

    /// <summary>Наименьший φe (φex, φey) среди проверок устойчивости в плоскости; null — нет.</summary>
    static double? MinPhiE(IEnumerable<Sp16CheckResult> results)
    {
        var values = results.Where(r => r.Status != CheckStatus.NotApplicable)
            .SelectMany(r => r.Variables)
            .Where(v => v.Key is "φe" or "φex" or "φey" && double.IsFinite(v.Value) && v.Value > 0)
            .Select(v => v.Value).ToList();
        return values.Count > 0 ? values.Min() : null;
    }
}
