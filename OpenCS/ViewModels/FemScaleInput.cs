namespace OpenCS.ViewModels;

/// <summary>Проверка числовых масштабов визуализации FEM-результата.</summary>
public static class FemScaleInput
{
    /// <summary>Масштаб должен быть конечным и строго положительным.</summary>
    public static bool IsValid(double value) => double.IsFinite(value) && value > 0;
}
