using CScore.PlateStrip;
using CSfea.Core;

namespace CSfea.CScoreBridge;

/// <summary>
/// Адаптер сохранённого линейного эквивалентного сечения в контракт собственного стержневого МКЭ.
/// На границе CScore (кН) → CSfea (Н) силы и строки касательной умножаются на 1000.
/// </summary>
public sealed class EquivalentBeamResponse : IBeamSectionResponse
{
    readonly EquivalentSection _section;

    public EquivalentBeamResponse(EquivalentSection section)
    {
        _section = section ?? throw new ArgumentNullException(nameof(section));
        if (!section.IsCalculable)
            throw new ArgumentException("Эквивалентное сечение не рассчитано.", nameof(section));
    }

    public BeamForces Forces(double eps0, double kappaY, double kappaZ)
    {
        var f = _section.Forces(new BeamStrainState(eps0, kappaY, kappaZ));
        return new BeamForces(
            UnitScale.ToCsfeaForce(f[0]),
            UnitScale.ToCsfeaMoment(f[1]),
            UnitScale.ToCsfeaMoment(f[2]));
    }

    public double[,] Tangent(double eps0, double kappaY, double kappaZ)
    {
        var source = _section.BeamTangent;
        var result = new double[3, 3];
        for (int i = 0; i < 3; i++)
        for (int j = 0; j < 3; j++)
        {
            double scale = i == 0 ? UnitScale.Force : UnitScale.Moment;
            result[i, j] = source[i, j] * scale;
        }
        return result;
    }

    public (double EA, double EIy, double EIz) Secant(double eps0, double kappaY, double kappaZ)
    {
        var f = Forces(eps0, kappaY, kappaZ);
        var tangent = Tangent(eps0, kappaY, kappaZ);
        double ea = Math.Abs(eps0) > 1e-14 ? Math.Abs(f.N / eps0) : tangent[0, 0];
        double eIy = Math.Abs(kappaY) > 1e-14 ? Math.Abs(f.My / kappaY) : tangent[1, 1];
        double eIz = Math.Abs(kappaZ) > 1e-14 ? Math.Abs(f.Mz / kappaZ) : tangent[2, 2];
        return (ea, eIy, eIz);
    }

    public double TorsionalStiffness(double twist = 0.0) => 0.0;

    public void Commit() { }
    public void Reset() { }
}
