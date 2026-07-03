using CScore;
using CSfea.Core;
using CSfea.Sparse;

namespace CSfea.CScoreBridge;

/// <summary>Диаграммы и параметры для <see cref="PlateSectionShellResponse"/>.</summary>
public sealed class PlateSectionMaterials
{
    public required Diagramm ConcreteDiagram { get; init; }
    public required Diagramm RebarDiagram { get; init; }
    public IReadOnlyList<Diagramm?>? LayerDiagrams { get; init; }

    /// <summary>E бетона, МПа (для линейного As, если не задан AsOverride).</summary>
    public double ConcreteE_MPa { get; init; } = 30000.0;

    public double Nu { get; init; } = 0.2;
    public double KShear { get; init; } = 5.0 / 6.0;
    public double[,]? AsOverride { get; init; }
}

/// <summary>
/// Адаптер <see cref="PlateSection"/> → <see cref="IShellSectionResponse"/>.
/// Порт Python <c>FiberShellResponse</c> (Python-путь без numba).
/// </summary>
public sealed class PlateSectionShellResponse : IShellSectionResponse
{
    private readonly PlateSection _section;
    private readonly PlateSectionMaterials _materials;

    private double[]? _cacheEpsM;
    private double[]? _cacheKappa;
    private double[]? _cacheGamma;
    private (double[] N, double[] M, double[] Q)? _cacheForces;

    public PlateSectionShellResponse(PlateSection section, PlateSectionMaterials materials)
    {
        _section = section;
        _materials = materials;
    }

    public ShellForces Forces(double[] epsM, double[] kappa, double[] gamma)
    {
        if (TryGetCache(epsM, kappa, gamma, out var cached))
            return cached;

        var state = new ShellStrainState(epsM[0], epsM[1], epsM[2], kappa[0], kappa[1], kappa[2]);
        var r = _section.Compute(state, _materials.ConcreteDiagram, _materials.RebarDiagram,
                                 _materials.LayerDiagrams, computeStiffness: false);
        var asMat = _materials.AsOverride
                    ?? _section.BuildAs(_materials.ConcreteE_MPa, _materials.Nu, _materials.KShear);
        var q = Dense.MatVec(asMat, gamma);
        for (int i = 0; i < 2; i++) q[i] = UnitScale.ToCsfeaShellForce(q[i]);

        var n = new[]
        {
            UnitScale.ToCsfeaShellForce(r.Nx),
            UnitScale.ToCsfeaShellForce(r.Ny),
            UnitScale.ToCsfeaShellForce(r.Nxy),
        };
        var m = new[]
        {
            UnitScale.ToCsfeaShellMoment(r.Mx),
            UnitScale.ToCsfeaShellMoment(r.My),
            UnitScale.ToCsfeaShellMoment(r.Mxy),
        };

        StoreCache(epsM, kappa, gamma, n, m, q);
        return new ShellForces(n, m, q);
    }

    public ShellTangent Tangent(double[] epsM, double[] kappa, double[] gamma)
    {
        var state = new ShellStrainState(epsM[0], epsM[1], epsM[2], kappa[0], kappa[1], kappa[2]);
        var r = _section.ComputeTangent(state, _materials.ConcreteDiagram, _materials.RebarDiagram,
            _materials.LayerDiagrams, _materials.ConcreteE_MPa, _materials.Nu, _materials.KShear,
            _materials.AsOverride);

        return new ShellTangent(
            ScaleBlock(r.A, UnitScale.ShellForce),
            ScaleBlock(r.B, UnitScale.ShellForce),
            ScaleBlock(r.D, UnitScale.ShellMoment),
            ScaleBlock(r.As, UnitScale.ShellForce));
    }

    public void Commit() { }

    public void Reset()
    {
        _cacheEpsM = _cacheKappa = _cacheGamma = null;
        _cacheForces = null;
    }

    private bool TryGetCache(double[] epsM, double[] kappa, double[] gamma, out ShellForces forces)
    {
        forces = default;
        if (_cacheForces == null || _cacheEpsM == null || _cacheKappa == null || _cacheGamma == null)
            return false;
        if (!VecEq(_cacheEpsM, epsM) || !VecEq(_cacheKappa, kappa) || !VecEq(_cacheGamma, gamma))
            return false;
        var c = _cacheForces.Value;
        forces = new ShellForces(c.N, c.M, c.Q);
        return true;
    }

    private void StoreCache(double[] epsM, double[] kappa, double[] gamma,
                            double[] n, double[] m, double[] q)
    {
        _cacheEpsM = (double[])epsM.Clone();
        _cacheKappa = (double[])kappa.Clone();
        _cacheGamma = (double[])gamma.Clone();
        _cacheForces = (n, m, q);
    }

    private static bool VecEq(double[] a, double[] b)
    {
        if (a.Length != b.Length) return false;
        for (int i = 0; i < a.Length; i++)
            if (Math.Abs(a[i] - b[i]) > 1e-15) return false;
        return true;
    }

    private static double[,] ScaleBlock(double[,] m, double scale)
    {
        int nr = m.GetLength(0), nc = m.GetLength(1);
        var r = new double[nr, nc];
        for (int i = 0; i < nr; i++)
            for (int j = 0; j < nc; j++)
                r[i, j] = m[i, j] * scale;
        return r;
    }
}
