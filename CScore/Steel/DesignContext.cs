namespace CScore;

/// <summary>
/// Кривые продольного изгиба по приложению Д, СП 16.13330.2017.
/// </summary>
public enum BucklingCurve
{
    a0 = 0, // α = 0.13 (сплошные сечения, прокатные, центр. сжатие)
    a  = 1, // α = 0.21
    b  = 2, // α = 0.34 (сварные двутавры с толстой стенкой)
    c  = 3, // α = 0.49 (сварные двутавры с тонкой стенкой)
    d  = 4  // α = 0.76 (составные сечения)
}

/// <summary>
/// Тип конструктивного элемента (для определения λmax, таблица 9, СП 16).
/// </summary>
public enum StructuralElementType
{
    CompressionMember,          // Сжатый элемент (стойка) — λmax = 180
    CompressionMemberInTruss,   // Сжатый элемент фермы — λmax = 120
    TensionMemberInTruss,       // Растянутый элемент фермы — λmax = 400
    BeamWeb,                    // Стенка балки — λmax = 250
    CraneBeam,                  // Подкрановая балка — λmax = 250
    ColumnInFrame,              // Колонна рамы — λmax = 150
    Tie,                        // Связь — λmax = 300
    Other                       // Прочее — λmax = 200
}

/// <summary>
/// Контекст расчёта стального элемента.
/// </summary>
public record DesignContext
{
    public double DesignLengthX { get; init; }  // l0x [м]
    public double DesignLengthY { get; init; }  // l0y [м]
    public double MuX { get; init; } = 1.0;     // μx
    public double MuY { get; init; } = 1.0;     // μy
    public double BetaM { get; init; } = 1.0;   // βm
    public double GammaM { get; init; } = 1.025; // γM
    /// <summary>Расстояние между точками боковой связи lbit [м]. 0 = не задано.</summary>
    public double DesignLengthBit { get; init; }
    /// <summary>Расчётная длина для проверки кручения lω [м]. 0 = не задано.</summary>
    public double DesignLengthTorsion { get; init; }
    /// <summary>Тип сечения для таблицы 22 (η коэффициент).</summary>
    public SteelStabilityCheck.SectionTypeForEta SectionType { get; init; }
        = SteelStabilityCheck.SectionTypeForEta.Solid;
    /// <summary>Кривая продольного изгиба по оси X (приложение Д). По умолчанию b.</summary>
    public BucklingCurve BucklingCurveX { get; init; } = BucklingCurve.b;
    /// <summary>Кривая продольного изгиба по оси Y (приложение Д). По умолчанию b.</summary>
    public BucklingCurve BucklingCurveY { get; init; } = BucklingCurve.b;
    /// <summary>Коэффициент αb для бокового выпучивания (приложение Ж). 0 = авто.</summary>
    public double AlphaB { get; init; }
    /// <summary>Коэффициент ψ для продольного изгиба (9.2.2). По умолчанию 0.85.</summary>
    public double Psi { get; init; } = 0.85;
    /// <summary>Тип элемента: колонна, балка, раскос и т.д. (для λmax).</summary>
    public StructuralElementType ElementType { get; init; }
        = StructuralElementType.CompressionMember;
}
