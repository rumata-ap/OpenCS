using System.Windows.Media.Media3D;

namespace OpenCS.ViewModels;

/// <summary>Описание контура поперечного сечения в локальной плоскости YZ.</summary>
public sealed record FemSectionGlyph(
    string MemberTag,
    Point3D Center,
    Vector3D LocalX,
    Vector3D LocalY,
    Vector3D LocalZ,
    double RotationDeg,
    IReadOnlyList<IReadOnlyList<(double Y, double Z)>> Contours,
    double FallbackHalfSize);
