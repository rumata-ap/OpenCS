namespace CScore.Import;

/// <summary>Прогресс импорта SCAD XLS (Fraction 0…1).</summary>
public class ScadXlsProgress
{
    public string Phase { get; init; } = "";
    public int SheetIndex { get; init; }
    public int SheetCount { get; init; }
    public double Fraction { get; init; }
    public string Message { get; init; } = "";
}
