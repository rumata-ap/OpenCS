namespace OpenCS.OpenSees.Model;

/// <summary>Источник координаты волокна в исходной системе CScore.</summary>
public enum OpenSeesCoordinateSource
{
    CScoreX,
    CScoreY
}

/// <summary>Компоненты сил, используемые при обмене с OpenSees.</summary>
public enum OpenSeesForceComponent
{
    Axial,
    BendingAboutY,
    BendingAboutZ,
    Torsion
}

/// <summary>Компоненты деформаций, используемые при обмене с OpenSees.</summary>
public enum OpenSeesDeformationComponent
{
    AxialStrain,
    CurvatureAboutY,
    CurvatureAboutZ,
    Twist
}

/// <summary>Явное соглашение преобразования координат и компонент секции.</summary>
public sealed class OpenSeesCoordinateConvention
{
    /// <summary>Координата OpenSees Y.</summary>
    public OpenSeesCoordinateSource YFrom { get; init; }

    /// <summary>Координата OpenSees Z.</summary>
    public OpenSeesCoordinateSource ZFrom { get; init; }

    /// <summary>Компонента продольной силы.</summary>
    public OpenSeesForceComponent AxialForce { get; init; }

    /// <summary>Компонента момента вокруг OpenSees Y.</summary>
    public OpenSeesForceComponent BendingAboutY { get; init; }

    /// <summary>Компонента момента вокруг OpenSees Z.</summary>
    public OpenSeesForceComponent BendingAboutZ { get; init; }

    /// <summary>Компонента крутящего момента.</summary>
    public OpenSeesForceComponent Torsion { get; init; }

    /// <summary>Компонента осевой деформации.</summary>
    public OpenSeesDeformationComponent AxialStrain { get; init; }

    /// <summary>Кривизна вокруг OpenSees Y.</summary>
    public OpenSeesDeformationComponent CurvatureAboutY { get; init; }

    /// <summary>Кривизна вокруг OpenSees Z.</summary>
    public OpenSeesDeformationComponent CurvatureAboutZ { get; init; }

    /// <summary>Угол закручивания.</summary>
    public OpenSeesDeformationComponent Twist { get; init; }

    /// <summary>Соглашение OpenCS: OpenSees Y = CScore Y, OpenSees Z = CScore X.</summary>
    public static OpenSeesCoordinateConvention CScoreDefault { get; } = new()
    {
        YFrom = OpenSeesCoordinateSource.CScoreY,
        ZFrom = OpenSeesCoordinateSource.CScoreX,
        AxialForce = OpenSeesForceComponent.Axial,
        BendingAboutY = OpenSeesForceComponent.BendingAboutY,
        BendingAboutZ = OpenSeesForceComponent.BendingAboutZ,
        Torsion = OpenSeesForceComponent.Torsion,
        AxialStrain = OpenSeesDeformationComponent.AxialStrain,
        CurvatureAboutY = OpenSeesDeformationComponent.CurvatureAboutY,
        CurvatureAboutZ = OpenSeesDeformationComponent.CurvatureAboutZ,
        Twist = OpenSeesDeformationComponent.Twist
    };
}
