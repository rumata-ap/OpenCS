namespace CScore.Import;

/// <summary>Параметры импорта усилий SCAD XLS.</summary>
public class ScadXlsImportOptions
{
    public double TonToKnFactor { get; set; } = 9.80665;

    /// <summary>Единица длины листа в метрах (см «Единицы длины для силовых факторов»). По умолчанию 1.0 (метры).</summary>
    public double LengthM { get; set; } = 1.0;

    public bool InvertBarBendingMoments { get; set; } = true;

    /// <summary>Инвертировать знаки изгибающих/крутящего моментов Mx/My/Mxy для пластин.</summary>
    public bool InvertShellBendingMoments { get; set; } = true;
    public IReadOnlySet<int> ElementIds { get; set; } = new HashSet<int>();

    /// <summary>Импортировать все элементы листа (игнорировать ElementIds).</summary>
    public bool ImportAllElements { get; set; }

    public static ScadXlsImportOptions Default => new();

    /// <summary>Клон с другими единицами (силы/длины), определёнными по конкретному листу.</summary>
    public ScadXlsImportOptions WithUnits(double tonToKnFactor, double lengthM) => new()
    {
        TonToKnFactor = tonToKnFactor,
        LengthM = lengthM,
        InvertBarBendingMoments = InvertBarBendingMoments,
        InvertShellBendingMoments = InvertShellBendingMoments,
        ElementIds = ElementIds,
        ImportAllElements = ImportAllElements,
    };
}
