using CScore.Fem;
using CScore.Import;
using CScore.Planar;
using Xunit;

namespace CScore.Tests;

/// <summary>Поворот сечения импортных стержней (<see cref="FemElement.BeamRotationDeg"/>): оси SCAD и ЛИРЫ → конвенция OpenCS.</summary>
public sealed class BeamRotationImportTests
{
    static ScadSchemaData Data(params (int Id, (double X, double Y, double Z) A, (double X, double Y, double Z) B, ScadRodAxes? Axes)[] bars)
    {
        var data = new ScadSchemaData();
        int node = 0;
        foreach (var (id, a, b, axes) in bars)
        {
            data.Nodes.Add(new ScadNodeRecord(++node, a.X, a.Y, a.Z));
            data.Nodes.Add(new ScadNodeRecord(++node, b.X, b.Y, b.Z));
            data.Elements.Add(new ScadElementRecord(id, 5, 1, [node - 1, node]));
            if (axes != null) data.RodAxes[id] = axes;
        }
        return data;
    }

    public static TheoryData<string, double[], double[], int, double[]> ScadCases => new()
    {
        { "горизонтальный", [0, 0, 0], [4, 0, 0], 0, [] },
        { "горизонтальный наклонный в плане", [1, 2, 0], [4, 6, 0], 0, [] },
        { "наклонный", [0, 0, 0], [3, 1, 2], 0, [] },
        { "вертикальный", [0, 0, 0], [0, 0, 3], 0, [] },
        { "вертикальный вниз", [0, 0, 3], [0, 0, 0], 0, [] },
        { "повёрнутый на 30°", [0, 0, 0], [4, 0, 0], ScadRodAxes.AngleDeg, [30] },
        { "вертикальный, повёрнутый на 0,5 рад", [0, 0, 0], [0, 0, 3], ScadRodAxes.AngleRad, [0.5] },
        { "Y1 на точку", [0, 0, 0], [3, 1, 2], 3, [5, -2, 7] },
        { "Z1 по вектору", [0, 0, 0], [4, 0, 0], 6, [0, 1, 1] },
    };

    [Theory]
    [MemberData(nameof(ScadCases))]
    public void Scad_LocalYOfConventionMatchesScadY1(string name, double[] a, double[] b, int type, double[] values)
    {
        var axes = type == 0 ? null : new ScadRodAxes(type, values);
        var data = Data((7, (a[0], a[1], a[2]), (b[0], b[1], b[2]), axes));
        var bar = Assert.Single(ScadSchemaConverter.ToFemMeshElements(data, 1));
        Assert.True(bar.BeamRotationDeg.HasValue, name);

        var expected = ScadRodAxes.LocalY(axes, (a[0], a[1], a[2]), (b[0], b[1], b[2]))!;
        var (_, y, _) = BeamLocalAxisConvention.Frame(new(a[0], a[1], a[2]), new(b[0], b[1], b[2]), bar.BeamRotationDeg!.Value);
        Assert.Equal(expected[0], y.X, 9);
        Assert.Equal(expected[1], y.Y, 9);
        Assert.Equal(expected[2], y.Z, 9);
    }

    [Fact]
    public void Scad_HorizontalDefault_IsMinus90AndShellsHaveNoRotation()
    {
        // По умолчанию Y1 SCAD горизонтальна, а Y конвенции — вверх: Y1 = −Z конвенции → поворот −90°.
        var data = Data((1, (0, 0, 0), (4, 0, 0), null));
        data.Nodes.Add(new ScadNodeRecord(3, 0, 1, 0));
        data.Nodes.Add(new ScadNodeRecord(4, 4, 1, 0));
        data.Elements.Add(new ScadElementRecord(2, 44, 1, [1, 2, 3, 4]));
        var elements = ScadSchemaConverter.ToFemMeshElements(data, 1);
        Assert.Equal(-90, elements.Single(e => e.ElemType == "beam").BeamRotationDeg!.Value, 9);
        Assert.Null(elements.Single(e => e.ElemType == "shell").BeamRotationDeg);
    }

    [Fact]
    public void Scad_ZeroLengthBar_HasNoRotation()
    {
        var bar = Assert.Single(ScadSchemaConverter.ToFemMeshElements(Data((1, (1, 1, 1), (1, 1, 1), null)), 1));
        Assert.Null(bar.BeamRotationDeg);
    }

    [Theory]
    [InlineData(0, 0, 0, 4, 0, 0)]
    [InlineData(0, 0, 0, 3, 1, 2)]
    [InlineData(0, 0, 0, 0, 0, 3)]
    public void Lira_DefaultRule_SameAsScadDefault(double ax, double ay, double az, double bx, double by, double bz)
    {
        var data = new LiraSchemaData();
        data.Nodes.Add(new LiraNodeRecord(1, ax, ay, az, 0));
        data.Nodes.Add(new LiraNodeRecord(2, bx, by, bz, 0));
        data.Elements.Add(new LiraElementRecord(1, 10, 1, 1, [1, 2]));
        var bar = Assert.Single(LiraSchemaConverter.ToFemMeshBarElements(data, 1));

        var expected = ScadRodAxes.LocalY(null, (ax, ay, az), (bx, by, bz))!;
        var (_, y, _) = BeamLocalAxisConvention.Frame(new(ax, ay, az), new(bx, by, bz), bar.BeamRotationDeg!.Value);
        Assert.Equal(expected[0], y.X, 9);
        Assert.Equal(expected[1], y.Y, 9);
        Assert.Equal(expected[2], y.Z, 9);
    }

    [Fact]
    public void RotationDeg_RoundTripsFrame()
    {
        PlanarVector3 i = new(1, 2, 3), j = new(4, -1, 5);
        foreach (double deg in new[] { -179.0, -90, -12.5, 0, 45, 90, 180 })
        {
            var (_, y, _) = BeamLocalAxisConvention.Frame(i, j, deg);
            double back = BeamLocalAxisConvention.RotationDeg(i, j, y)!.Value;
            Assert.Equal(0, Math.Abs(Math.IEEERemainder(back - deg, 360)), 9);
        }
        Assert.Null(BeamLocalAxisConvention.RotationDeg(i, j, j - i));
    }
}
