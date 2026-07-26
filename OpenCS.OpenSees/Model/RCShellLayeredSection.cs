using OpenCS.OpenSees.Structural;

namespace OpenCS.OpenSees.Model;

/// <summary>Режим соответствия OpenCS-секции native LayeredShell.</summary>
public enum ShellMappingMode
{
    /// <summary>Отображение не содержит заявленной аппроксимации.</summary>
    Exact,

    /// <summary>Отображение разрешено с явно сохранённой аппроксимацией.</summary>
    NativeWithExplicitApproximation,

    /// <summary>Секция используется только как reference-модель.</summary>
    ReferenceOnly,

    /// <summary>Секция заблокирована и не может быть сгенерирована.</summary>
    Blocked
}

/// <summary>Backend-независимый snapshot слоистого железобетонного shell-сечения.</summary>
public sealed record RCShellLayeredSection(
    int Tag,
    string SourcePlateSectionFingerprint,
    double TotalThickness,
    ShellFrame Frame,
    IReadOnlyList<RCShellLayer> Layers,
    ShellMappingMode MappingMode,
    IReadOnlyList<string> Warnings,
    string Fingerprint)
{
    /// <summary>Проверяет содержательную целостность snapshot.</summary>
    public void Validate()
    {
        if (Tag <= 0)
            throw new InvalidOperationException("Tag shell-секции должен быть положительным.");
        if (string.IsNullOrWhiteSpace(SourcePlateSectionFingerprint))
            throw new InvalidOperationException($"Секция {Tag}: отсутствует fingerprint исходного PlateSection.");
        if (!double.IsFinite(TotalThickness) || TotalThickness <= 0)
            throw new InvalidOperationException($"Секция {Tag}: толщина должна быть положительной и конечной.");
        if (Layers is null || Layers.Count == 0)
            throw new InvalidOperationException($"Секция {Tag}: отсутствуют слои.");
        if (string.IsNullOrWhiteSpace(Fingerprint))
            throw new InvalidOperationException($"Секция {Tag}: отсутствует fingerprint.");

        Frame.Validate();

        var indexes = new HashSet<int>();
        foreach (RCShellLayer layer in Layers)
        {
            if (layer.Index < 0 || !indexes.Add(layer.Index))
                throw new InvalidOperationException($"Секция {Tag}: индексы слоёв должны быть уникальными и неотрицательными.");
            if (!double.IsFinite(layer.CenterZ) || !double.IsFinite(layer.Thickness) || layer.Thickness <= 0)
                throw new InvalidOperationException($"Секция {Tag}, слой {layer.Index}: толщина и координата должны быть конечными, толщина положительна.");
            if (layer.MaterialTag <= 0)
                throw new InvalidOperationException($"Секция {Tag}, слой {layer.Index}: tag материала должен быть положительным.");
            if (!double.IsFinite(layer.DirectionDegrees))
                throw new InvalidOperationException($"Секция {Tag}, слой {layer.Index}: направление должно быть конечным.");
        }
    }
}
