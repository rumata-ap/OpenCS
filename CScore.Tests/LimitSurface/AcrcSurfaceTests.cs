using CScore.LimitSurface;
using Xunit;
using Xunit.Abstractions;

namespace CScore.Tests.LimitSurface;

// Поверхность ширины раскрытия трещин (acrc): граница колонны (N, ψ) — корень g(m) = 1 при m ≥ Mcrc,
// Mcrc и плоскость трещинообразования — по телу ПС2.
public class AcrcSurfaceTests(ITestOutputHelper output)
{
    static readonly LimitSurfaceOptions Coarse = new() { InitialMeridians = 12, ThetaIntervals = 12 };

    static AcrcColumnMesher Mesher(CrossSection section, AcrcSurfaceCriterion? criterion = null) =>
        new(section, criterion ?? new AcrcSurfaceCriterion(), Coarse);

    static double G(CrackWidthResult r, AcrcSurfaceCriterion c) =>
        new[] { r.AcrcLong / c.AcrcUltLong, r.AcrcShort / c.AcrcUltShort, r.UtilSteel, r.UtilConcrete }.Max();

    [Fact]
    public void Column_BendingN0_MatchesCrackWidthCoreAtBoundary()
    {
        var section = LimitSurfaceTestSections.Symmetric();
        var criterion = new AcrcSurfaceCriterion();
        var mesher = Mesher(section, criterion);

        var col = mesher.Column(0.0, 0.0);
        Assert.True(col.Converged);
        Assert.NotEqual(ConstraintKind.CrackBody, col.Active.Kind);

        // Независимо: точка трещинообразования по телу ПС2, ядро без затравок в найденной точке.
        var u = mesher.CrackMesh.Utilization(0.0, 1.0, 0.0, UtilizationMode.Moment);
        var cap = u.Capacity!.Value;
        Assert.True(col.M > Math.Abs(cap.Mx));
        var check = criterion.CreateCore(LimitSurfaceTestSections.Symmetric());
        var onset = check.Onset(cap.N, cap.Mx, cap.My, u.StrainPlane!.Value, byAxialForce: false)!;
        double k = criterion.LongShare;
        var r = check.Evaluate(onset, 0.0, k * col.M, 0.0, col.M, 0.0, phi3: 1.0);

        Assert.True(r.Cracked);
        Assert.InRange(G(r, criterion), 1.0 - 1e-3, 1.0 + 1e-3);
        output.WriteLine($"Mcrc = {cap.Mx:G6}, m* = {col.M:G6}, вид = {col.Active.Kind}");
    }

    [Fact]
    public void Column_HighCompression_IsCrackBody()
    {
        var section = LimitSurfaceTestSections.Symmetric();
        var mesher = Mesher(section);
        double nMin = mesher.CrackMesh.Vertices.Min(v => v.N);

        var col = mesher.Column(0.95 * nMin, 0.3);
        var u = mesher.CrackMesh.Utilization(0.95 * nMin, Math.Cos(0.3), Math.Sin(0.3), UtilizationMode.Moment);
        var cap = u.Capacity!.Value;

        Assert.Equal(ConstraintKind.CrackBody, col.Active.Kind);
        Assert.Equal(Math.Sqrt(cap.Mx * cap.Mx + cap.My * cap.My), col.M, 6);
    }

    [Fact]
    public void Column_Monotone()
    {
        var section = LimitSurfaceTestSections.Asymmetric();
        var mesher = Mesher(section);
        foreach (var (n, psi) in new[] { (0.0, Math.PI), (-300.0, 0.4), (100.0, Math.PI + 0.3) })
        {
            var col = mesher.Column(n, psi);
            Assert.True(col.Converged, $"N = {n}, ψ = {psi}");
            if (col.Active.Kind == ConstraintKind.CrackBody) continue;
            var u = mesher.CrackMesh.Utilization(n, Math.Cos(psi), Math.Sin(psi), UtilizationMode.Moment);
            double mcrc = Math.Sqrt(u.Capacity!.Value.Mx * u.Capacity.Value.Mx + u.Capacity.Value.My * u.Capacity.Value.My);
            double prev = double.NegativeInfinity;
            for (int i = 0; i < 20; i++)
            {
                double m = mcrc * (1 + 1e-6) + (1.5 * col.M - mcrc) * i / 19.0;
                double g = mesher.ConditionAt(n, psi, m);
                if (double.IsNaN(g)) { Assert.True(m > col.M, $"N = {n}: NaN до границы при m = {m:G6}"); break; }
                Assert.True(g >= prev - 1e-9, $"N = {n}, ψ = {psi}: g({m:G6}) = {g:G6} < {prev:G6}");
                prev = g;
            }
        }
    }

    [Fact]
    public void Column_SmallUlt_CrackBody()
    {
        var section = LimitSurfaceTestSections.Symmetric();
        var mesher = Mesher(section, new AcrcSurfaceCriterion(acrcUltLong: 0.001, acrcUltShort: 0.001));
        var col = mesher.Column(0.0, 0.0);
        var u = mesher.CrackMesh.Utilization(0.0, 1.0, 0.0, UtilizationMode.Moment);

        Assert.Equal(ConstraintKind.CrackBody, col.Active.Kind);
        Assert.Equal(Math.Abs(u.Capacity!.Value.Mx), col.M, 4);
    }

    [Fact]
    public void Column_TensionBeyondPole_UsesAxialOnset()
    {
        var section = LimitSurfaceTestSections.Symmetric();
        var mesher = Mesher(section);
        double nMax = mesher.CrackMesh.Vertices.Max(v => v.N);

        var col = mesher.Column(1.15 * nMax, 0.7);

        Assert.True(col.ByAxialForce);
        Assert.True(col.Converged);
        Assert.True(col.M > 0.0 && double.IsFinite(col.M), $"m* = {col.M:G6}");
        output.WriteLine($"N_pole = {nMax:G6}, m* = {col.M:G6}, вид = {col.Active.Kind}");
    }
}
