namespace OpenCS.OpenSees.Structural;

/// <summary>Стержневой elasticBeamColumn: эффективные A/E/G/J/Iy/Iz и вектор vecxz для geomTransf.</summary>
public sealed record FemLinearElement(
    int Tag, int NodeI, int NodeJ,
    double A, double E, double G, double J, double Iy, double Iz,
    (double X, double Y, double Z) Vecxz);
