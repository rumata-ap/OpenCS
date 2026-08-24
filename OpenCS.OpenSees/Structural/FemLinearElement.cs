namespace OpenCS.OpenSees.Structural;

/// <summary>
/// Линейный стержень: эффективные A/E/G/J/Iy/Iz и вектор vecxz для geomTransf.
/// Если <see cref="Avy"/> и <see cref="Avz"/> заданы одновременно, экспортируется
/// <c>ElasticTimoshenkoBeam</c>; если обе равны null — прежний <c>elasticBeamColumn</c>.
/// </summary>
public sealed record FemLinearElement(
    int Tag, int NodeI, int NodeJ,
    double A, double E, double G, double J, double Iy, double Iz,
    (double X, double Y, double Z) Vecxz,
    double? Avy = null,
    double? Avz = null);
