using CScore.Fem;
using CScore.Fem.Fragment;
using CScore.Fem.Loads;
using CScore.Planar;
using Xunit;

namespace CScore.Tests.Fem;

/// <summary>Части нагрузок КЭ по виду совпадают с согласованными узловыми силами по ΣF и ΣM (спека 2026-11-05, разд. 3).</summary>
public sealed class FemElementLoadPiecesTests
{
    sealed class UnitWeights(double gamma, double area) : IFemSelfWeightSource
    {
        public double? UnitWeight(FemElement element) => gamma;
        public double? BarArea(FemElement element) => element.ElemType == "beam" ? area : null;
    }

    static readonly FragmentBounds All = new(-100, 100, null, 1e-6);

    static FemMeshNode N(int tag, double x, double y, double z) => new() { NodeTag = tag.ToString(), X = x, Y = y, Z = z };

    static FemElement E(string tag, string type, int[] nodes, double? h = null) => new()
    {
        ElemTag = tag, ElemType = type, NodeIdsJson = System.Text.Json.JsonSerializer.Serialize(nodes), ThicknessM = h,
    };

    /// <summary>Наклонная Q4 (хранение «1 2 4 3») и наклонный стержень 5 → 6.</summary>
    static FemLoadMeshContext Model() => new(
        [N(1, 0, 0, 0.3), N(2, 2, 0, 0.5), N(3, 0, 3, 0.3), N(4, 2, 3, 0.5), N(5, 1, 0, 0), N(6, 1, 2, 1)],
        [E("1", "shell", [1, 2, 3, 4], 0.2), E("2", "beam", [5, 6])], null, new UnitWeights(25_000, 0.04));

    static FemElementLoad Load(string kind, string target, string cs, string axis, double[] values)
    {
        var l = new FemElementLoad { LoadCaseId = 1, LoadKind = kind, CoordinateSystem = cs, Axis = axis };
        l.SetTargetTags([target]);
        l.SetValues(values);
        return l;
    }

    static (PlanarVector3 F, PlanarVector3 M) Nodal(Dictionary<string, double[]> forces, FemLoadMeshContext mesh)
    {
        PlanarVector3 f = PlanarVector3.Zero, m = PlanarVector3.Zero;
        foreach (var (tag, v) in forces)
        {
            var n = mesh.NodesByTag[tag];
            var force = new PlanarVector3(v[0], v[1], v[2]);
            f += force;
            m += new PlanarVector3(n.X, n.Y, n.Z).Cross(force) + new PlanarVector3(v[3], v[4], v[5]);
        }
        return (f, m);
    }

    static (PlanarVector3 F, PlanarVector3 M) Pieces(IReadOnlyList<FemLoadPiece> pieces)
    {
        PlanarVector3 f = PlanarVector3.Zero, m = PlanarVector3.Zero;
        foreach (var t in FragmentLoadInside.Terms(All, pieces))
        {
            f += t.F;
            m += t.At.Cross(t.F) + t.M;
        }
        return (f, m);
    }

    static void Same((PlanarVector3 F, PlanarVector3 M) expected, (PlanarVector3 F, PlanarVector3 M) actual)
    {
        double scale = Math.Max(1, expected.F.Length);
        Assert.True((expected.F - actual.F).Length <= 1e-9 * scale, $"F {expected.F} ≠ {actual.F}");
        Assert.True((expected.M - actual.M).Length <= 1e-9 * scale * 10, $"M {expected.M} ≠ {actual.M}");
    }

    [Theory]
    [InlineData("1", "uniform", "global", "z", new[] { -2000.0 })]
    [InlineData("1", "uniform", "local", "z", new[] { -2000.0 })]
    [InlineData("1", "nodal", "global", "z", new[] { -1000.0, -2000, -3000, -4000 })]
    [InlineData("1", "point", "global", "x", new[] { 5000.0, 0.4, 0.7 })]
    [InlineData("1", "edge", "global", "z", new[] { 1.0, 2, -1000, -3000 })]
    [InlineData("1", "edge", "global", "y", new[] { 2.0, 4, 500, 0 })]
    [InlineData("2", "uniform", "global", "x", new[] { 700.0 })]
    [InlineData("2", "uniform", "local", "y", new[] { 700.0 })]
    [InlineData("2", "point", "global", "z", new[] { 900.0, 1.3 })]
    [InlineData("2", "trapezoid", "global", "x", new[] { 1000.0, 0.5, 3000, 1.5 })]
    [InlineData("2", "trapezoid", "local", "z", new[] { 1000.0, -0.5, 3000, 1.5 })]
    public void Of_MatchesNodalizer(string target, string kind, string cs, string axis, double[] values)
    {
        var mesh = Model();
        var load = Load(kind, target, cs, axis, values);
        var forces = new Dictionary<string, double[]>(StringComparer.Ordinal);
        var diag = new List<FemValidationDiagnostic>();
        FemElementLoadNodalizer.Accumulate(load, mesh, 1.5, forces, diag);
        Assert.Empty(diag);
        var pieces = FemElementLoadPieces.Of(load, mesh.ElementsByTag[target], mesh, 1.5);
        Assert.NotEmpty(pieces);
        Same(Nodal(forces, mesh), Pieces(pieces));
    }

    [Fact]
    public void SelfWeight_MatchesNodalizer()
    {
        var mesh = Model();
        var forces = new Dictionary<string, double[]>(StringComparer.Ordinal);
        FemElementLoadNodalizer.AccumulateSelfWeight(1.1, mesh.Elements, mesh, forces, []);
        var pieces = mesh.Elements.SelectMany(e => FemElementLoadPieces.SelfWeight(1.1, e, mesh)).ToList();
        Assert.Equal(2, pieces.Count);
        Same(Nodal(forces, mesh), Pieces(pieces));
    }

    [Fact]
    public void Of_PointOutsideBar_Empty()
    {
        var mesh = Model();
        Assert.Empty(FemElementLoadPieces.Of(Load("point", "2", "global", "z", [900, 2.5]), mesh.ElementsByTag["2"], mesh, 1));
    }

    [Fact]
    public void Scale_MultipliesValues()
    {
        var mesh = Model();
        var pieces = FemElementLoadPieces.Of(Load("nodal", "1", "global", "z", [-1, -2, -3, -4]), mesh.ElementsByTag["1"], mesh, 1);
        var a = Pieces(pieces);
        var b = Pieces(pieces.Select(p => p.Scale(-2)).ToList());
        Same((a.F * -2, a.M * -2), b);
    }
}
