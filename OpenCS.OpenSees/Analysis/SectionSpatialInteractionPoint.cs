namespace OpenCS.OpenSees.Analysis;

/// <summary>Одна точка пространственной диаграммы для пары N и угла.</summary>
public sealed class SectionSpatialInteractionPoint
{
    /// <summary>Продольная сила точки в Н.</summary>
    public double AxialForceN { get; init; }

    /// <summary>Направление луча в градусах.</summary>
    public double AngleDegrees { get; init; }

    /// <summary>Последний сошедшийся Mx в Н·м.</summary>
    public double? MomentMxNm { get; init; }

    /// <summary>Последний сошедшийся My в Н·м.</summary>
    public double? MomentMyNm { get; init; }

    /// <summary>Последняя сошедшаяся кривизна Mx в 1/м.</summary>
    public double? CurvatureMx { get; init; }

    /// <summary>Последняя сошедшаяся кривизна My в 1/м.</summary>
    public double? CurvatureMy { get; init; }

    /// <summary>Последняя сошедшаяся строка истории.</summary>
    public SpatialSectionHistoryRow? TerminalRow { get; init; }

    /// <summary>Полная радиальная история точки.</summary>
    public IReadOnlyList<SpatialSectionHistoryRow> HistoryRows { get; init; } = [];

    /// <summary>Статус точки: ok, not_converged или error.</summary>
    public string Status { get; init; } = "error";

    /// <summary>Диагностика точки.</summary>
    public IReadOnlyList<string> Diagnostics { get; init; } = [];

    /// <summary>Каталог артефактов точки.</summary>
    public string ArtifactDirectory { get; init; } = "";
}
