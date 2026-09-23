using CScore.Sp63.CrackWidth;
using CScore.Sp63.Normal;

namespace CScore.Sp63.Deflection;

/// <summary>Типизированные настройки формульного расчёта прогиба по СП 63.</summary>
public sealed record Sp63DeflectionOptions(
    Sp63NormalShapeKind ShapeKind,
    Sp63NormalAxis Axis,
    Sp63DeflectionStaticScheme Scheme,
    double SpanM,
    double DeflectionLimitMm,
    Sp63Humidity Humidity,
    Sp63DeflectionForcesMode ForcesMode,
    double LongTermShare,
    double? ManualN = null,
    double? ManualMx = null,
    double? ManualMy = null,
    double? ManualLongN = null,
    double? ManualLongMx = null,
    double? ManualLongMy = null);

/// <summary>Способ задания длительной части усилий.</summary>
public enum Sp63DeflectionForcesMode
{
    /// <summary>Длительная часть равна полной.</summary>
    TotalOnly,
    /// <summary>Длительная часть вычисляется по доле полной нагрузки.</summary>
    Share,
    /// <summary>Длительные усилия задаются отдельно.</summary>
    Manual
}
