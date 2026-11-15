using CScore.Fem;
using CScore.Fem.Fragment.Masks;
using CScore.Planar;
using CScore.Sp63Punching.Fem;
using CScore.Submodel;
using Xunit;
using static CScore.Tests.Sp63Punching.Fem.PunchingFemTestMesh;

namespace CScore.Tests.Sp63Punching.Fem;

/// <summary>Действие стержней колонн на плиту по силам концов (способ S, срез 3, задача 3б).</summary>
public class PunchingFemColumnActionTests
{
    static readonly FragmentMaskKey Key = FragmentMaskKey.OfLoadCase(1);

    /// <summary>Результат с заданными силами концов стержней в общих осях (переводятся в местные оси КЭ).</summary>
    sealed class Ends(PunchingFemTestMesh mesh) : IFragmentParentResult
    {
        readonly Dictionary<string, BeamEndForces> _f = new(StringComparer.Ordinal);

        /// <summary>Сила и момент узла на КЭ у конца <paramref name="node"/> в общих осях.</summary>
        public void Set(FemElement bar, string node, PlanarVector3 force, PlanarVector3 moment)
        {
            var g = CScore.Fem.Loads.FemElementGeometry.Of(bar, mesh.Nodes)!;
            var (ex, ey, ez) = BeamLocalAxisConvention.Frame(g.Points[0], g.Points[1], bar.BeamRotationDeg ?? 0);
            var local = new Dof6(force.Dot(ex), force.Dot(ey), force.Dot(ez), moment.Dot(ex), moment.Dot(ey), moment.Dot(ez));
            _f[bar.ElemTag] = g.NodeTags[0] == node ? new BeamEndForces(local, Dof6.Zero) : new BeamEndForces(Dof6.Zero, local);
        }

        public IReadOnlyList<FragmentMaskKey> Keys => [Key];
        public bool TryNodeDisplacement(FragmentMaskKey key, string meshNodeTag, out Dof6 displacement) { displacement = Dof6.Zero; return false; }
        public bool TryBeamEndForces(FragmentMaskKey key, string elemTag, out BeamEndForces forces) => _f.TryGetValue(elemTag, out forces!);
        public bool TryShellForces(FragmentMaskKey key, string elemTag, out FragmentShellForces forces) { forces = null!; return false; }
        public IReadOnlyList<FragmentBeamSpanLoad> BeamLoads(FragmentMaskKey key, string elemTag) => [];
        public bool KnowsBeamLoads => false;
    }

    static (PunchingFemTestMesh M, PunchingFemJoint J, Dictionary<string, FemElement> E, FemElement Below, FemElement Above) Setup(double rotation = 0)
    {
        var m = new PunchingFemTestMesh();
        m.Slab(0, 0, 6, 6, 3, 0.2);
        var below = m.Bar(m.Node(3, 3, 0), m.Node(3, 3, 3), rotation);
        var above = m.Bar(m.Node(3, 3, 3), m.Node(3, 3, 6), rotation);
        var j = Assert.Single(PunchingFemJointFinder.Find(m.Model()));
        return (m, j, m.Elements.ToDictionary(e => e.ElemTag), below, above);
    }

    [Fact]
    public void ColumnBelowPushesUp_AboveDown_NetIsDifference()
    {
        var (m, j, e, below, above) = Setup();
        var r = new Ends(m);
        string n = m.Node(3, 3, 3);
        // Плита давит на нижнюю колонну вниз 500 кН (сила узла на КЭ), верхняя колонна давит на плиту вниз 200 кН.
        r.Set(below, n, P(0, 0, -500e3), PlanarVector3.Zero);
        r.Set(above, n, P(0, 0, 200e3), PlanarVector3.Zero);
        var a = PunchingFemColumnAction.Compute(j, m.Nodes, e, r, Key)!.Value;
        Assert.Equal(300e3, a.F.Dot(j.Normal), 6);
    }

    [Fact]
    public void Moment_InNodeAxes_ActionOnSlab()
    {
        var (m, j, e, below, above) = Setup(rotation: 90);
        var r = new Ends(m);
        string n = m.Node(3, 3, 3);
        // Момент узла на КЭ нижней колонны вокруг общей X = −40 кН·м → на плиту +40 кН·м вокруг X.
        r.Set(below, n, P(0, 0, -100e3), P(-40e3, 0, 0));
        r.Set(above, n, PlanarVector3.Zero, PlanarVector3.Zero);
        var a = PunchingFemColumnAction.Compute(j, m.Nodes, e, r, Key)!.Value;
        // Поворот 90°: ось x узла — общая Y, ось y узла = z × x = −X.
        Assert.Equal(1.0, j.AxisX.Y, 9);
        Assert.Equal(-1.0, j.AxisY.X, 9);
        Assert.Equal(0, a.M.Dot(j.AxisX), 6);
        Assert.Equal(-40e3, a.M.Dot(j.AxisY), 6);
    }

    [Fact]
    public void MissingBarForces_Null()
    {
        var (m, j, e, below, _) = Setup();
        var r = new Ends(m);
        r.Set(below, m.Node(3, 3, 3), P(0, 0, -1), PlanarVector3.Zero);
        Assert.Null(PunchingFemColumnAction.Compute(j, m.Nodes, e, r, Key));
    }

    [Fact]
    public void BaseBounds_ContainBaseOnly()
    {
        var (_, j, _, _, _) = Setup();
        var b = PunchingFemColumnAction.BaseBounds(j);
        double half = 0.2 + j.H0;
        Assert.True(b.InBox(P(3 + half - 1e-3, 3, 3)));
        Assert.False(b.InBox(P(3 + half + 1e-3, 3, 3)));
        Assert.Equal(4, PunchingFemColumnAction.BaseFaces(j).Count);
    }
}
