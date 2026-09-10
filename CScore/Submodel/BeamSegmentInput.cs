using CScore.Planar;

namespace CScore.Submodel;

/// <summary>Источник β-угла отрезка. Absent означает, что источника нет —
/// β принят нулевым по конвенции локальных осей, а не прочитан из модели.</summary>
public enum BetaSource
{
    Member,
    Absent
}

/// <summary>Нормализованный стержневой отрезок — единица анализа.</summary>
public sealed record BeamSegmentInput(
    string SourceKey,
    PlanarVector3 Start,
    PlanarVector3 End,
    double BetaDeg,
    BetaSource BetaSource,
    string? SourceMemberTag)
{
    public PlanarVector3 Delta => End - Start;

    public double LengthM => Delta.Length;

    /// <summary>Проверяется до любой нормализации: Normalize при NaN возвращает NaN-вектор.</summary>
    public bool IsFinite => Start.IsFinite && End.IsFinite && double.IsFinite(BetaDeg);
}
