namespace CScore.Import;

/// <summary>Параметры импорта усилий SCAD XLS.</summary>
public class ScadXlsImportOptions
{
    public double TonToKnFactor { get; set; } = 9.80665;
    public bool InvertBarBendingMoments { get; set; } = true;

    /// <summary>Инвертировать знаки изгибающих/крутящего моментов Mx/My/Mxy для пластин.</summary>
    public bool InvertShellBendingMoments { get; set; } = true;
    public IReadOnlySet<int> ElementIds { get; set; } = new HashSet<int>();

    /// <summary>Импортировать все элементы листа (игнорировать ElementIds).</summary>
    public bool ImportAllElements { get; set; }

    /// <summary>Толщина по умолчанию, м (поле диалога). Fallback, если нет h у КЭ.</summary>
    public double DefaultThicknessM { get; set; }

    /// <summary>Толщины по номеру КЭ SCAD (из FEM-топологии / XLS), м.</summary>
    public IReadOnlyDictionary<int, double> ElementThicknessM { get; set; }
        = new Dictionary<int, double>();

    public static ScadXlsImportOptions Default => new();

    /// <summary>Толщина для КЭ: карта → default. 0 если ничего не задано.</summary>
    public double ResolveThicknessM(int elementId)
    {
        if (ElementThicknessM.TryGetValue(elementId, out double h) && h > 0)
            return h;
        return DefaultThicknessM > 0 ? DefaultThicknessM : 0;
    }
}
