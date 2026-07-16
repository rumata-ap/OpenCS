namespace OpenCS.OpenSees.Analysis;

/// <summary>Результат проверки одной строки ForceSet относительно поверхности.</summary>
public sealed class SectionSpatialInteractionDemandCheck
{
    /// <summary>Номер исходной строки набора усилий.</summary>
    public int Num { get; init; }

    /// <summary>Метка исходной строки набора усилий.</summary>
    public string Label { get; init; } = "";

    /// <summary>Продольная сила проверяемой точки в Н.</summary>
    public double AxialForceN { get; init; }

    /// <summary>Момент Mx проверяемой точки в Н·м.</summary>
    public double MomentMxNm { get; init; }

    /// <summary>Момент My проверяемой точки в Н·м.</summary>
    public double MomentMyNm { get; init; }

    /// <summary>Признак попадания точки внутрь фигуры.</summary>
    public bool IsInside { get; init; }

    /// <summary>Коэффициент использования по радиальному направлению.</summary>
    public double? Utilization { get; init; }

    /// <summary>Статус проверки.</summary>
    public string Status { get; init; } = "indeterminate";

    /// <summary>Пояснение к результату проверки.</summary>
    public string Diagnostic { get; init; } = "";
}
