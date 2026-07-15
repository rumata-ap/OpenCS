namespace OpenCS.OpenSees.Analysis;

/// <summary>Результат одной точки одноосной диаграммы N-M.</summary>
public sealed class SectionInteractionPoint
{
    /// <summary>Продольная сила, заданная для точки, в Н.</summary>
    public double AxialForceN { get; init; }

    /// <summary>Последний найденный момент в Н·м или null, если сходимость не достигнута.</summary>
    public double? BendingMomentNm { get; init; }

    /// <summary>Кривизна последней сошедшейся строки в 1/м.</summary>
    public double? Curvature { get; init; }

    /// <summary>Последняя сошедшаяся строка внутренней истории.</summary>
    public SectionHistoryRow? TerminalRow { get; init; }

    /// <summary>Статус точки: ok, not_converged или error.</summary>
    public string Status { get; init; } = "error";

    /// <summary>Диагностика внутреннего анализа.</summary>
    public IReadOnlyList<string> Diagnostics { get; init; } = [];

    /// <summary>Каталог артефактов внутреннего запуска.</summary>
    public string ArtifactDirectory { get; init; } = "";
}
