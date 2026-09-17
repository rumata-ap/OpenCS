using CScore;
using CScore.Sp63.Normal;
using Xunit;
using Xunit.Abstractions;

namespace CScore.Tests.Sp63Normal;

/// <summary>
/// Сверка предельного момента по приложению Д с НДМ для круглого и кольцевого сечений.
/// Фактические расхождения (Д/НДМ − 1) на 17.09.2026: circle-bending = −7,58 %;
/// circle-compression = −6,59 %; ring-bending = +4,07 % (ветвь б, не в запас);
/// ring-compression = −2,94 %. Отклонение площади 64-угольника от круга — 0,161 %.
/// </summary>
public sealed class Sp63CircularNdmComparisonTests(ITestOutputHelper output)
{
    static Material Steel340()
    {
        MaterialChars Ch(CalcType ct) => new(ct)
        {
            E = 200_000_000, Fc = -340_000, Ft = 340_000,
            Ec2 = -0.025, Et2 = 0.025, Type = MatType.ReSteelF
        };
        var m = new Material { Id = 10_003, Tag = "test-steel-A400", Type = MatType.ReSteelF, E = 200_000_000 };
        m.MaterialChars = [Ch(CalcType.C), Ch(CalcType.CL), Ch(CalcType.N), Ch(CalcType.NL)];
        return m;
    }

    static Contour Closed((double X, double Y)[] v, string tag, ContourType type) =>
        new(v.Select(p => p.X).Append(v[0].X).ToList(),
            v.Select(p => p.Y).Append(v[0].Y).ToList(), tag) { Type = type };

    static CrossSection Build(double outerRadius, double innerRadius, double rs,
        double barDiameter)
    {
        const int segments = 64, mesh = 41;
        var concrete = SectionCutFixtures.BuildConcreteMaterial();
        var contours = new List<Contour>
        {
            Closed(Sp63NormalFixtures.CircleVertices(outerRadius, segments), "outer", ContourType.Hull)
        };
        if (innerRadius > 0)
            contours.Add(Closed(Sp63NormalFixtures.CircleVertices(innerRadius, segments),
                "inner", ContourType.Hole));

        var area = new MaterialArea
        {
            Id = 1, Tag = "round", Category = AreaCategory.Region,
            Material = concrete, MaterialId = concrete.Id,
            DiagrammType = DiagrammType.L2, Contours = contours, NX = mesh, NY = mesh
        };
        area.Hull = contours[0];
        area.ResolveAndBuildDiagramms();
        area.SliceXY(mesh, mesh);

        var bars = Sp63NormalFixtures.CircleVertices(rs, 8)
            .Select(p => Fiber.CreatePoint(barDiameter, p.X, p.Y))
            .ToArray();
        var rebar = MaterialArea.CreateRebarArea(bars, Steel340(), DiagrammType.L2, area);
        return new CrossSection { Tag = "round-ndm", Areas = [area, rebar] };
    }

    static Sp63NormalOptions Options(Sp63NormalShapeKind kind) =>
        new(kind, Sp63NormalAxis.Mx, new Sp63MemberContext(6.0,
            Sp63StructuralScheme.StaticallyIndeterminate, 4.2,
            Sp63NormalStabilityMode.SectionOnlyExplicit, 0.0));

    [Theory]
    [InlineData("circle-bending", 0.25, 0.0, 0.20, 0.020, 0.0)]
    [InlineData("circle-compression", 0.25, 0.0, 0.20, 0.020, -800.0)]
    [InlineData("ring-bending", 0.30, 0.20, 0.25, 0.016, 0.0)]
    [InlineData("ring-compression", 0.30, 0.20, 0.25, 0.016, -500.0)]
    public void FormulaAndNdm_AgreeWithinTenPercent(string name, double outerRadius,
        double innerRadius, double rs, double barDiameter, double n)
    {
        var section = Build(outerRadius, innerRadius, rs, barDiameter);
        var kind = innerRadius > 0 ? Sp63NormalShapeKind.Annular : Sp63NormalShapeKind.Circular;
        const double mx = 50.0;

        var formula = Sp63NormalChecker.Check(section, new LoadItem { N = n, Mx = mx },
            CalcType.C, Options(kind));
        var ndm = LimitForceSolver.ForCrossSection(section, CalcType.C, ten: false)
            .MomentFactor(n: n, mx: mx, my: 0.0);

        Assert.Equal(Sp63NormalStatus.Calculated, formula.Status);
        Assert.True(ndm.Converged, $"НДМ не сошёлся: factor={ndm.Factor}");
        double formulaCapacity = formula.StrengthDetails.Single().Allowable;
        double ndmCapacity = Math.Abs(ndm.MxLimit);
        double relative = formulaCapacity / ndmCapacity - 1.0;
        output.WriteLine(
            $"{name}: Д = {formulaCapacity:F3} кН·м; НДМ = {ndmCapacity:F3} кН·м; " +
            $"расхождение = {relative:P2}; outerAreaDeviation = {formula.Variables["outerAreaDeviation"]:P3}");
        Assert.InRange(formulaCapacity, ndmCapacity * 0.90, ndmCapacity * 1.10);
    }
}
