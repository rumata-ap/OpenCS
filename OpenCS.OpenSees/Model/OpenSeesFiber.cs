namespace OpenCS.OpenSees.Model;

/// <summary>Волокно секции в координатах OpenSees.</summary>
public readonly record struct OpenSeesFiber(
    double Y,
    double Z,
    double AreaM2,
    int MaterialTag);
