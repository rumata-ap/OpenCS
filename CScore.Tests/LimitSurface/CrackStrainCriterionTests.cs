using System.Diagnostics;
using CScore.LimitSurface;
using Xunit;
using Xunit.Abstractions;

namespace CScore.Tests.LimitSurface;

/// <summary>Критерий ПС2 (образование трещин, п. 8.2.14 СП 63) и сетка по нему.</summary>
public class CrackStrainCriterionTests(ITestOutputHelper output)
{
    static readonly LimitSurfaceOptions Coarse = new() { InitialMeridians = 12, ThetaIntervals = 12, RefineTol = 0 };

    [Theory]
    [InlineData(CalcType.N)]
    [InlineData(CalcType.NL)]
    public void Points_OnlyConcreteVertices_LimitsFromChars(CalcType calc)
    {
        var section = LimitSurfaceTestSections.Symmetric();
        var ch = section.Areas[0].Material!.GetChars(calc)!;
        var pts = new CrackStrainCriterion(calc).BuildDomain(section).Points;
        Assert.Equal(4, pts.Count);
        Assert.All(pts, p =>
        {
            Assert.Equal(StrainPointKind.Concrete, p.Kind);
            Assert.Equal(ch.Et2, p.EpsMax);
            Assert.Equal(ch.Et0, p.EpsBt0);
            Assert.Equal(ch.Ec0, p.EpsB0);
            Assert.Equal(ch.Ec2, p.EpsB2);
        });
    }

    [Fact]
    public void NL_DiffersFromN()
    {
        var section = LimitSurfaceTestSections.Symmetric();
        var n = new CrackStrainCriterion(CalcType.N).BuildDomain(section).Points[0];
        var nl = new CrackStrainCriterion(CalcType.NL).BuildDomain(section).Points[0];
        Assert.NotEqual(n.EpsMax, nl.EpsMax);
    }

    [Theory]
    [InlineData(CalcType.N)]
    [InlineData(CalcType.NL)]
    public void EpsBt2_MatchesDiagramTensionEnd(CalcType calc)
    {
        // Тот же предел, что берёт CrackingSolver.TensionLimit — конец ветви растяжения диаграммы.
        var section = LimitSurfaceTestSections.Symmetric();
        var p = new CrackStrainCriterion(calc).BuildDomain(section).Points[0];
        Assert.Equal(section.Areas[0].Diagramms[calc].It.X.Max(), p.EpsMax, 12);
    }

    [Theory]
    [InlineData(CalcType.C)]
    [InlineData(CalcType.CL)]
    public void FirstGroupChars_Throw(CalcType calc) =>
        Assert.Throws<ArgumentException>(() => new CrackStrainCriterion(calc));

    [Fact]
    public void NoTensionChars_Throws()
    {
        var section = LimitSurfaceTestSections.Symmetric();
        section.Areas[0].Material!.GetChars(CalcType.N)!.Et2 = 0;
        var ex = Assert.Throws<InvalidOperationException>(() => new CrackStrainCriterion().BuildDomain(section));
        Assert.Contains("concrete", ex.Message);
    }

    static LimitSurfaceMesh Build(CrossSection section, LimitSurfaceOptions? o = null) =>
        new StrainDomainMesher(section, new CrackStrainCriterion(), o).Build();

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

    [Fact]
    public void Mesh_Topology() => AssertClosedManifold(Build(LimitSurfaceTestSections.Asymmetric(), Coarse));

    [Fact]
    public void Mesh_NoRebar_Builds()
    {
        var section = LimitSurfaceTestSections.Rect([]);
        Assert.Throws<InvalidOperationException>(() =>
            new StrainDomainMesher(section, new UlsStrainCriterion(CalcType.C, ten: false), Coarse).Build());
        AssertClosedManifold(Build(section, Coarse));
    }

    [Fact]
    public void Poles_MatchIntegral()
    {
        var section = LimitSurfaceTestSections.Symmetric();
        var m = Build(section, Coarse);
        var ch = section.Areas[0].Material!.GetChars(CalcType.N)!;
        Assert.Equal(ch.Et0, m.StrainPlanes[0].e0, 12);                 // растяжение одного знака: (8.54) при r = 1
        Assert.Equal(ch.Ec0, m.StrainPlanes[1].e0, 12);                 // сжатие: (8.53) при r = 1
        foreach (int pole in new[] { 0, 1 })
        {
            Assert.Equal(0, m.StrainPlanes[pole].ky); Assert.Equal(0, m.StrainPlanes[pole].kz);
            var f = section.Compute(m.StrainPlanes[pole], CalcType.N, true, computeStiffness: false);
            Assert.Equal(f.N, m.Vertices[pole].N, 9);
        }
        Assert.True(m.Vertices[0].N > 0 && m.Vertices[1].N < 0);
    }

    [Fact]
    public void Active_CrackAndCompressionOnly()
    {
        var m = Build(LimitSurfaceTestSections.Asymmetric(), Coarse);
        Assert.Contains(m.Active, a => a.Side == 1);
        Assert.Contains(m.Active, a => a.Side == -1);
        Assert.Contains(m.Active, a => a.SingleSign);
    }

    [Fact]
    public void Measure_Defaults()
    {
        var sw = Stopwatch.StartNew();
        var m = Build(LimitSurfaceTestSections.Asymmetric());
        sw.Stop();
        output.WriteLine($"ПС2 по умолчанию: {m.MeridianCount} меридианов, {m.Vertices.Count} вершин, {sw.Elapsed.TotalSeconds:F1} с");
        foreach (var (count, dev) in m.RefineLog) output.WriteLine($"  круг: {count} меридианов, max отклонение {dev:G3}");
        int crack = m.Active.Count(a => a.Side == 1), comp = m.Active.Count(a => a.Side == -1);
        output.WriteLine($"  вершин с трещиной {crack}, со сжатием {comp}");
        AssertClosedManifold(m);
    }
}
