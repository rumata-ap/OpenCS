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
            double mcrc = mesher.CrackRayParameter(n, psi);
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

    static void AssertClosedManifold(LimitSurfaceMesh m)
    {
        var edges = new Dictionary<(int, int), int>();
        foreach (var (a, b, c) in m.Triangles)
        {
            Assert.True(a != b && b != c && a != c);
            foreach (var (i, j) in new[] { (a, b), (b, c), (c, a) })
            {
                var k = i < j ? (i, j) : (j, i);
                edges[k] = edges.GetValueOrDefault(k) + 1;
            }
        }
        Assert.All(edges.Values, n => Assert.Equal(2, n));
        Assert.Equal(2, m.Vertices.Count - edges.Count + m.Triangles.Count);
        Assert.True(SurfaceMeshGeometry.SignedVolume(m.Vertices, m.Triangles) > 0);
    }

    static readonly LimitSurfaceOptions MeshCoarse = new() { InitialMeridians = 8, ThetaIntervals = 8 };

    static (AcrcColumnMesher Mesher, LimitSurfaceMesh Mesh) BuildMesh(CrossSection section, LimitSurfaceOptions? o = null)
    {
        var mesher = new AcrcColumnMesher(section, new AcrcSurfaceCriterion(), o ?? MeshCoarse);
        return (mesher, mesher.Build());
    }

    [Trait("Category", "Slow")]
    [Theory]
    [InlineData("symmetric")]
    [InlineData("asymmetric")]
    public void Mesh_Topology(string name)
    {
        var section = name == "symmetric" ? LimitSurfaceTestSections.Symmetric() : LimitSurfaceTestSections.Asymmetric();
        var (mesher, mesh) = BuildMesh(section);
        AssertClosedManifold(mesh);
        output.WriteLine($"{name}: вершин {mesh.Vertices.Count}, колонн {mesh.MeridianCount}, не сошлось {mesher.NotConvergedCount}");
    }

    [Trait("Category", "Slow")]
    [Fact]
    public void Mesh_ContainsCrackBody()
    {
        // Сравнение одинаковыми лучами колонн по обеим сеткам: вершины сетки ПС2 напрямую не годятся —
        // фасетная сетка ПС2 местами не звёздна относительно оси колонн, и луч к вершине упирается в соседнюю грань.
        var (mesher, mesh) = BuildMesh(LimitSurfaceTestSections.Asymmetric());
        var crack = mesher.CrackMesh;
        double mMax = mesh.Vertices.Max(v => Math.Sqrt(v.Mx * v.Mx + v.My * v.My));
        double nMin = crack.Vertices.Min(v => v.N), nMax = crack.Vertices.Max(v => v.N);
        Assert.True(mesh.Vertices.Min(v => v.N) <= nMin + 1e-9, "сжатый край тела acrc внутри тела ПС2");
        Assert.True(mesh.Vertices.Max(v => v.N) >= nMax - 1e-9, "растянутый край тела acrc внутри тела ПС2");
        var rnd = new Random(5);
        double worst = 0;
        for (int i = 0; i < 200; i++)
        {
            double n = nMin + (nMax - nMin) * (0.01 + 0.98 * rnd.NextDouble());
            double psi = 2 * Math.PI * rnd.NextDouble();
            var (ax, ay) = mesher.Axis(n);
            var o = new ForcePoint(n, ax, ay);
            var d = new ForcePoint(0, 10 * mMax * Math.Cos(psi), 10 * mMax * Math.Sin(psi));
            var ua = mesh.Utilization(o, d);
            var uc = crack.Utilization(o, d);
            Assert.True(ua.RayOriginInside && uc.RayOriginInside, $"ось вне тела: N = {n:G6}");
            double ma = Math.Sqrt(Math.Pow(ua.Capacity!.Value.Mx - ax, 2) + Math.Pow(ua.Capacity.Value.My - ay, 2));
            double mc = Math.Sqrt(Math.Pow(uc.Capacity!.Value.Mx - ax, 2) + Math.Pow(uc.Capacity.Value.My - ay, 2));
            worst = Math.Max(worst, (mc - ma) / mMax);
        }
        output.WriteLine($"тело ПС2 выступает за тело acrc не более чем на {worst:P3} от M_max");
        // Сравниваются две фасетные сетки, каждая — в своём допуске 0,6 % от точной поверхности: допуск разности — сумма.
        Assert.True(worst <= 2 * 0.006, $"тело ПС2 выступает за тело acrc на {worst:P3} от M_max");
    }

    [Trait("Category", "Slow")]
    [Fact]
    public void Mesh_StepAtZeroN()
    {
        var (_, mesh) = BuildMesh(LimitSurfaceTestSections.Symmetric());
        for (int k = 0; k < mesh.MeridianCount; k++)
        {
            var zero = Enumerable.Range(0, mesh.Vertices.Count)
                .Where(i => mesh.MeridianOf[i] == k && Math.Abs(mesh.Vertices[i].N) < 1e-9)
                .OrderBy(i => mesh.Angles[i].Theta).ToList();
            Assert.Equal(2, zero.Count);
            static double M(ForcePoint p) => Math.Sqrt(p.Mx * p.Mx + p.My * p.My);
            // Меньший t — сторона растяжения (0⁺, φ3 = 1,2): ширина раскрытия больше, граница не дальше.
            Assert.True(M(mesh.Vertices[zero[0]]) <= M(mesh.Vertices[zero[1]]) * (1 + 1e-9),
                $"колонна {k}: m*(0⁺) = {M(mesh.Vertices[zero[0]]):G6} > m*(0⁻) = {M(mesh.Vertices[zero[1]]):G6}");
        }
    }

    [Trait("Category", "Slow")]
    [Fact]
    public void Mesh_Poles()
    {
        var (mesher, mesh) = BuildMesh(LimitSurfaceTestSections.Symmetric());
        Assert.Equal(mesher.CrackMesh.Vertices.MinBy(v => v.N), mesh.Vertices[1]);
        Assert.Equal(ConstraintKind.CrackBody, mesh.Active[1].Kind);

        // Симметричное сечение: ось колонн — M = 0, растянутый полюс — центральное растяжение.
        var top = mesh.Vertices[0];
        Assert.Equal(0.0, top.Mx, 6);
        Assert.Equal(0.0, top.My, 6);
        Assert.True(top.N > mesher.CrackMesh.Vertices.Max(v => v.N));
        if (mesh.Active[0].Kind != ConstraintKind.CrackBody)
            Assert.InRange(mesher.ConditionAt(top.N, 0.0, 0.0), 1.0 - 1e-3, 1.0 + 1e-3);
    }

    [Trait("Category", "Slow")]
    [Fact]
    public void Measure_Defaults()
    {
        foreach (var (name, section) in new[] { ("symmetric", LimitSurfaceTestSections.Symmetric()), ("asymmetric", LimitSurfaceTestSections.Asymmetric()) })
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            var mesher = new AcrcColumnMesher(section, new AcrcSurfaceCriterion());
            double tCrack = sw.Elapsed.TotalSeconds;
            var mesh = mesher.Build();
            sw.Stop();
            AssertClosedManifold(mesh);
            string line = $"{name}: ПС2 {tCrack:F1} с, всего {sw.Elapsed.TotalSeconds:F1} с, колонн {mesh.MeridianCount}, " +
                $"вершин {mesh.Vertices.Count}, не сошлось {mesher.NotConvergedCount}, без растянутой арматуры {mesher.NoTensileRebarCount}, журнал φ: " +
                string.Join("; ", mesh.RefineLog.Select(e => $"{e.Count}/{e.MaxDeviation:G3}"));
            output.WriteLine(line);
            Console.WriteLine(line);
        }
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
