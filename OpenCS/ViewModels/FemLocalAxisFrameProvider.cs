using CScore.Planar;
using CScore.Submodel;
using OpenCS.OpenSees.Structural;

namespace OpenCS.ViewModels;

/// <summary>Поставщик кадров анализатора поверх единой конвенции FemLocalAxis.</summary>
public sealed class FemLocalAxisFrameProvider : IBeamLocalFrameProvider
{
    public BeamLocalFrame Frame(PlanarVector3 axisDirection, double betaDeg)
    {
        var unit = axisDirection.Normalize();
        var (_, y, z) = FemLocalAxis.LocalFrame(new FemLinearNode(1,0,0,0,new bool[6]), new FemLinearNode(2,unit.X,unit.Y,unit.Z,new bool[6]), betaDeg);
        return new(new PlanarVector3(y.X,y.Y,y.Z),new PlanarVector3(z.X,z.Y,z.Z));
    }
}
