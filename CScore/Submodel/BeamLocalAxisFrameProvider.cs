using CScore.Fem;
using CScore.Planar;

namespace CScore.Submodel;

/// <summary>Поставщик кадров поверх единой конвенции <see cref="BeamLocalAxisConvention"/>
/// (та же, что у OpenSees geomTransf).</summary>
public sealed class BeamLocalAxisFrameProvider : IBeamLocalFrameProvider
{
    public BeamLocalFrame Frame(PlanarVector3 axisDirection, double betaDeg)
    {
        var (_, y, z) = BeamLocalAxisConvention.Frame(PlanarVector3.Zero, axisDirection.Normalize(), betaDeg);
        return new BeamLocalFrame(y, z);
    }
}
