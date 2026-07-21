namespace OpenCS.OpenSees.Structural;

/// <summary>Стержневой forceBeamColumn: ссылка на fiber-сечение, число точек интегрирования и
/// вектор vecxz для geomTransf.</summary>
public sealed record FemNonlinearElement(
    int Tag, int NodeI, int NodeJ,
    int SectionTag, int NumIntegrationPoints,
    (double X, double Y, double Z) Vecxz);
