namespace OpenCS.OpenSees.Analysis;

/// <summary>Одна строка истории одноосного анализа секции.</summary>
public sealed class SectionHistoryRow
{
    /// <summary>Номер шага.</summary>
    public int Step { get; init; }

    /// <summary>Коэффициент нагрузки OpenSees.</summary>
    public double LoadFactor { get; init; }

    /// <summary>Продольное усилие в Н.</summary>
    public double AxialForceN { get; init; }

    /// <summary>Момент выбранного изгибающего направления в Н·м.</summary>
    public double BendingMomentNm { get; init; }

    /// <summary>Осевое перемещение/деформация.</summary>
    public double AxialStrain { get; init; }

    /// <summary>Кривизна выбранного изгибающего направления.</summary>
    public double Curvature { get; init; }

    /// <summary>Признак сходимости шага.</summary>
    public bool Converged { get; init; }

    /// <summary>Невязка шага.</summary>
    public double Residual { get; init; }

    /// <summary>Выбранная компонента CScore.</summary>
    public SectionBendingAxis Axis { get; init; }

    /// <summary>Сырое значение момента OpenSees до отображения.</summary>
    public double OpenSeesBendingMomentNm { get; init; }

    /// <summary>Сырое значение кривизны OpenSees до отображения.</summary>
    public double OpenSeesCurvature { get; init; }
}
