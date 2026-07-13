namespace OpenCS.Tasks;

/// <summary>Прогресс выполнения расчётной задачи (0…1) для StatusBar.</summary>
public sealed class CalcTaskProgress
{
    public double Fraction { get; init; }
    public string? Message { get; init; }
}
