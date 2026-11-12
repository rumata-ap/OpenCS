using CScore.Fem.Fragment;
using CScore.Planar;
using Xunit;

namespace CScore.Tests.Fem.Fragment;

public sealed class ParentMeshMapperTests
{
    static PlanarVector3 V(double x, double y, double z) => new(x, y, z);

    /// <summary>Q4 в плоскости z: [x0, x1]×[y0, y1], хранение «1 2 4 3».</summary>
    static FragmentRefElement Quad(string tag, double x0, double y0, double x1, double y1, double z = 0) =>
        new(tag, "shell", [V(x0, y0, z), V(x1, y0, z), V(x0, y1, z), V(x1, y1, z)]);

    static readonly FragmentRefElement[] Shells = [Quad("1", 0, 0, 1, 1), Quad("2", 1, 0, 2, 1), Quad("3", 0, 0, 1, 1, 3)];
    static readonly PlanarVector3 Up = V(0, 0, 1);

    [Fact]
    public void Shell_ContainingElementOfSamePlane()
    {
        var m = new ParentMeshMapper(Shells);
        Assert.Equal("2", m.MapShell(V(1.5, 0.5, 0), Up)!.Tag);
        Assert.Equal("1", m.MapShell(V(0.5, 0.5, 0), V(0, 0, -1))!.Tag);
        Assert.Equal("3", m.MapShell(V(0.5, 0.5, 3), Up)!.Tag);
    }

    [Fact]
    public void Shell_OtherPlaneOrFar_Null_NearWithinDistance()
    {
        var m = new ParentMeshMapper(Shells);
        Assert.Null(m.MapShell(V(0.5, 0.5, 0), V(1, 0, 0)));
        Assert.Null(m.MapShell(V(0.5, 0.5, 1), Up));
        Assert.Null(m.MapShell(V(2.3, 0.5, 0), Up));
        Assert.Equal("2", m.MapShell(V(2.3, 0.5, 0), Up, maxDistance: 0.5)!.Tag);
    }

    [Fact]
    public void Shell_ManyElements_FindsContainerBeyondNearestCentroids()
    {
        // Мелкие КЭ рядом с крупным: ближайшие центры — мелкие, содержащий — крупный.
        var list = new List<FragmentRefElement> { Quad("big", 0, 0, 10, 10) };
        for (int i = 0; i < 40; i++) list.Add(Quad($"s{i}", 10 + 0.1 * i, 0, 10.1 + 0.1 * i, 0.1));
        var m = new ParentMeshMapper(list);
        Assert.Equal("big", m.MapShell(V(9.9, 0.05, 0), Up)!.Tag);
    }

    [Fact]
    public void Bar_SameAxisContainingPoint()
    {
        var m = new ParentMeshMapper([new("1", "beam", [V(0, 0, 0), V(0, 0, 2)]), new("2", "beam", [V(0, 0, 2), V(0, 0, 4)])]);
        Assert.Equal("2", m.MapBar(V(0, 0, 3), V(0, 0, -1))!.Tag);
        Assert.Null(m.MapBar(V(0.1, 0, 3), V(0, 0, 1)));
        Assert.Null(m.MapBar(V(0, 0, 3), V(1, 0, 0)));
    }

    /// <summary>
    /// Ось стержня фрагмента, привязанная к узлам источника с расхождением в миллиметры (колонны 71/80 «Музея»: 2,6 мм),
    /// — ближайший параллельный КЭ родителя в пределах <see cref="StoreyFragmentBuilder.SourceSnapM"/>; точная пара важнее.
    /// </summary>
    [Fact]
    public void Bar_OffsetAxisWithinSourceSnap_NearestParallel()
    {
        var m = new ParentMeshMapper([
            new("a", "beam", [V(0, 0, 0), V(0, 0, 2)]),
            new("b", "beam", [V(0.004, 0, 0), V(0.004, 0, 2)]),
            new("h", "beam", [V(0, 0, 1), V(2, 0, 1)])]);
        Assert.Equal("a", m.MapBar(V(0, 0.0026, 1.5), V(0, 0, 1))!.Tag);
        Assert.Equal("b", m.MapBar(V(0.0035, 0, 1.5), V(0, 0, 1))!.Tag);
        Assert.Equal("a", m.MapBar(V(0.0005, 0, 1.5), V(0, 0, 1))!.Tag);
        Assert.Null(m.MapBar(V(0, 0.011, 1.5), V(0, 0, 1)));
        Assert.Null(m.MapBar(V(0, 0.003, 2.1), V(0, 0, 1)));
    }
}
