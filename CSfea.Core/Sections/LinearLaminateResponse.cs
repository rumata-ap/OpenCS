using CSfea.Sparse;

namespace CSfea.Core;

/// <summary>
/// Адаптер над <see cref="Laminate"/> с замороженными ABD/A_s — линейное
/// оболочечное сечение. На любые деформации возвращает одну и ту же
/// касательную. Порт <c>fea/section_response.py: LinearLaminateResponse</c>.
/// </summary>
public sealed class LinearLaminateResponse : IShellSectionResponse
{
    private readonly double[,] _a;
    private readonly double[,] _b;
    private readonly double[,] _d;
    private readonly double[,] _as;

    public LinearLaminateResponse(Laminate laminate)
    {
        var abd = laminate.ABDAs();
        _a = abd.A;
        _b = abd.B;
        _d = abd.D;
        _as = abd.As;
    }

    public ShellForces Forces(double[] epsM, double[] kappa, double[] gamma)
    {
        // N = A·eps_m + B·kappa
        var n = Dense.AddV(Dense.MatVec(_a, epsM), Dense.MatVec(_b, kappa));
        // M = B^T·eps_m + D·kappa
        var m = Dense.AddV(Dense.MatTVec(_b, epsM), Dense.MatVec(_d, kappa));
        // Q = As·gamma
        var q = Dense.MatVec(_as, gamma);
        return new ShellForces(n, m, q);
    }

    public ShellTangent Tangent(double[] epsM, double[] kappa, double[] gamma)
        => new(_a, _b, _d, _as);

    public void Commit() { }

    public void Reset() { }
}
