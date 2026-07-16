namespace OpenCS.OpenSees.Analysis;

/// <summary>Одна строка совместной 3D-истории OpenSees.</summary>
public sealed class SpatialSectionHistoryRow
{
    /// <summary>Номер радиального шага.</summary>
    public int Step { get; init; }

    /// <summary>Общий множитель радиального нагружения.</summary>
    public double LoadFactor { get; init; }

    /// <summary>Продольная сила в Н.</summary>
    public double AxialForceN { get; init; }

    /// <summary>Момент Mx в Н·м.</summary>
    public double MomentMxNm { get; init; }

    /// <summary>Момент My в Н·м.</summary>
    public double MomentMyNm { get; init; }

    /// <summary>Кривизна, сопряжённая с Mx, в 1/м.</summary>
    public double CurvatureMx { get; init; }

    /// <summary>Кривизна, сопряжённая с My, в 1/м.</summary>
    public double CurvatureMy { get; init; }

    /// <summary>Модуль вектора кривизны в 1/м.</summary>
    public double CurvatureMagnitude { get; init; }

    /// <summary>Признак сходимости шага.</summary>
    public bool Converged { get; init; }

    /// <summary>Невязка шага.</summary>
    public double Residual { get; init; }

    /// <summary>Исходный момент OpenSees Mz в Н·м.</summary>
    public double OpenSeesMzNm { get; init; }

    /// <summary>Исходный момент OpenSees My в Н·м.</summary>
    public double OpenSeesMyNm { get; init; }

    /// <summary>Исходное вращение OpenSees вокруг Y.</summary>
    public double OpenSeesRotationY { get; init; }

    /// <summary>Исходное вращение OpenSees вокруг Z.</summary>
    public double OpenSeesRotationZ { get; init; }
}
