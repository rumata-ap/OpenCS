namespace OpenCS.OpenSees.Structural;

/// <summary>Нормализованный узел shell-модели OpenSees с шестью степенями свободы.</summary>
public sealed record NormalizedShellNode(
    int Tag,
    double X,
    double Y,
    double Z,
    bool[] Fixed,
    string? SourceId)
{
    /// <summary>Проверяет tag, координаты и маску закреплений.</summary>
    public void Validate()
    {
        if (Tag <= 0)
            throw new InvalidOperationException("Tag shell-узла должен быть положительным.");
        if (!double.IsFinite(X) || !double.IsFinite(Y) || !double.IsFinite(Z))
            throw new InvalidOperationException($"Узел {Tag}: координаты должны быть конечными.");
        if (Fixed is null || Fixed.Length != 6)
            throw new InvalidOperationException($"Узел {Tag}: маска закрепления должна иметь 6 флагов.");
    }
}
