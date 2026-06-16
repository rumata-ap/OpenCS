using CScore;
using CSfea.Core;

namespace CSfea.CScoreBridge;

/// <summary>
/// Адаптер <see cref="CrossSection"/> → <see cref="IBeamSectionResponse"/>.
/// Порт Python <c>CrossSectionBeamResponse</c>.
/// </summary>
public sealed class CrossSectionBeamResponse : IBeamSectionResponse
{
    private readonly CrossSection _section;
    private readonly CalcType _calc;
    private readonly bool _ten;
    private readonly bool _ca;
    private readonly double _gj;

    public CrossSectionBeamResponse(CrossSection section, CalcType calc,
                                    double gjLinear = 0.0,
                                    bool ten = true, bool ca = true)
    {
        _section = section;
        _calc = calc;
        _gj = gjLinear;
        _ten = ten;
        _ca = ca;
    }

    private static Kurvature ToKurvature(double eps0, double kappaY, double kappaZ)
        => new() { e0 = eps0, ky = kappaY, kz = kappaZ };

    public BeamForces Forces(double eps0, double kappaY, double kappaZ)
    {
        var r = _section.Compute(ToKurvature(eps0, kappaY, kappaZ), _calc, _ten, _ca,
                                 computeStiffness: false);
        return new BeamForces(
            UnitScale.ToCsfeaForce(r.N),
            UnitScale.ToCsfeaMoment(r.Mx),
            UnitScale.ToCsfeaMoment(r.My));
    }

    public double[,] Tangent(double eps0, double kappaY, double kappaZ)
    {
        var r = _section.Compute(ToKurvature(eps0, kappaY, kappaZ), _calc, _ten, _ca,
                                 computeStiffness: true);
        var j = r.Tangent ?? throw new InvalidOperationException("Касательная не вычислена.");
        var t = new double[3, 3];
        for (int i = 0; i < 3; i++)
            for (int jcol = 0; jcol < 3; jcol++)
            {
                double scale = (i == 0) ? UnitScale.Force : UnitScale.Moment;
                t[i, jcol] = scale * j[i, jcol];
            }
        return t;
    }

    public (double EA, double EIy, double EIz) Secant(double eps0, double kappaY, double kappaZ)
    {
        var f = Forces(eps0, kappaY, kappaZ);
        double ea = Math.Abs(eps0) > 1e-14 ? Math.Abs(f.N / eps0) : Tangent(eps0, kappaY, kappaZ)[0, 0];
        double eIy = Math.Abs(kappaY) > 1e-14 ? Math.Abs(f.My / kappaY) : Tangent(eps0, kappaY, kappaZ)[1, 1];
        double eIz = Math.Abs(kappaZ) > 1e-14 ? Math.Abs(f.Mz / kappaZ) : Tangent(eps0, kappaY, kappaZ)[2, 2];
        return (ea, eIy, eIz);
    }

    public double TorsionalStiffness(double twist = 0.0) => _gj;

    public void Commit() { }

    public void Reset() { }
}
