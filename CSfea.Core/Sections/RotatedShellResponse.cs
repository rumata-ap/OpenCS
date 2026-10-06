using CSfea.Sparse;

namespace CSfea.Core;

/// <summary>
/// Сечение оболочки, заданное в собственных осях (например, оси пластины SCAD/ЛИРЫ, в которых
/// задана арматура), а используемое в локальных осях КЭ CSfea (ось x — ребро узел 0 → узел 1).
/// <paramref name="angle"/> α — угол (рад) от оси x КЭ до оси x сечения против часовой стрелки
/// вокруг нормали КЭ. Деформации поворачиваются в оси сечения, усилия и жёсткости — обратно:
/// ε' = T·ε, N = Tᵀ·N', A = Tᵀ·A'·T (то же для κ, M, B, D); γ' = R·γ, Q = Rᵀ·Q', As = Rᵀ·As'·R.
/// γxy и κxy — инженерные (удвоенные тензорные). Состояние (Commit/Reset) — у внутреннего сечения.
/// </summary>
public sealed class RotatedShellResponse : IShellSectionResponse
{
    private readonly double[,] _t;   // 3×3, деформации КЭ → оси сечения
    private readonly double[,] _r;   // 2×2, поперечные сдвиги КЭ → оси сечения

    /// <summary>Сечение в собственных осях.</summary>
    public IShellSectionResponse Inner { get; }

    /// <summary>Угол α (рад) от оси x КЭ до оси x сечения.</summary>
    public double Angle { get; }

    public RotatedShellResponse(IShellSectionResponse inner, double angle)
    {
        Inner = inner;
        Angle = angle;
        double c = Math.Cos(angle), s = Math.Sin(angle);
        _t = new[,]
        {
            { c * c, s * s, c * s },
            { s * s, c * c, -c * s },
            { -2.0 * c * s, 2.0 * c * s, c * c - s * s },
        };
        _r = new[,] { { c, s }, { -s, c } };
    }

    /// <summary>Деформации КЭ (ε, κ, γ) → оси сечения.</summary>
    public (double[] EpsM, double[] Kappa, double[] Gamma) ToSection(double[] epsM, double[] kappa, double[] gamma)
        => (Dense.MatVec(_t, epsM), Dense.MatVec(_t, kappa), Dense.MatVec(_r, gamma));

    public ShellForces Forces(double[] epsM, double[] kappa, double[] gamma)
    {
        var (e, k, g) = ToSection(epsM, kappa, gamma);
        var f = Inner.Forces(e, k, g);
        return new ShellForces(Dense.MatTVec(_t, f.N), Dense.MatTVec(_t, f.M), Dense.MatTVec(_r, f.Q));
    }

    public ShellTangent Tangent(double[] epsM, double[] kappa, double[] gamma)
    {
        var (e, k, g) = ToSection(epsM, kappa, gamma);
        var t = Inner.Tangent(e, k, g);
        return new ShellTangent(Congruent(t.A, _t), Congruent(t.B, _t), Congruent(t.D, _t), Congruent(t.As, _r));
    }

    private static double[,] Congruent(double[,] m, double[,] t)
        => Dense.MatMul(Dense.MatTMul(t, m), t);

    public void Commit() => Inner.Commit();

    public void Reset() => Inner.Reset();
}
