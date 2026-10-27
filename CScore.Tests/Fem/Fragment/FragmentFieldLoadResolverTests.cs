using System.Globalization;
using CScore.Fem;
using CScore.Fem.Fragment;
using CScore.Fem.Loads;
using CScore.Planar;
using Xunit;

namespace CScore.Tests.Fem.Fragment;

public sealed class FragmentFieldLoadResolverTests
{
    static PlanarVector3 V(double x, double y, double z) => new(x, y, z);

    static FragmentRefElement Quad(string tag, double x0, double y0, double x1, double y1) =>
        new(tag, "shell", [V(x0, y0, 0), V(x1, y0, 0), V(x0, y1, 0), V(x1, y1, 0)]);

    /// <summary>Сетка nx×ny Q4 (хранение «1 2 4 3») на [x0, x1]×[y0, y1] в плоскости z.</summary>
    static FemLoadMeshContext Grid(double x0, double y0, double x1, double y1, int nx, int ny, double z = 0)
    {
        string T(int i, int j) => (1 + j * (nx + 1) + i).ToString(CultureInfo.InvariantCulture);
        var nodes = new List<FemMeshNode>();
        for (int j = 0; j <= ny; j++)
            for (int i = 0; i <= nx; i++)
                nodes.Add(new FemMeshNode { NodeTag = T(i, j), X = x0 + (x1 - x0) * i / nx, Y = y0 + (y1 - y0) * j / ny, Z = z });
        var elements = new List<FemElement>();
        for (int j = 0; j < ny; j++)
            for (int i = 0; i < nx; i++)
                elements.Add(new FemElement
                {
                    ElemTag = (1 + j * nx + i).ToString(CultureInfo.InvariantCulture), ElemType = "shell",
                    NodeIdsJson = $"[{T(i, j)},{T(i + 1, j)},{T(i, j + 1)},{T(i + 1, j + 1)}]",
                });
        return new FemLoadMeshContext(nodes, elements);
    }

    static FragmentFieldLoad Field(string kind, params (string Tag, double[] Values)[] entries) =>
        new(7, kind, "z", entries.Select(e => new FragmentFieldEntry(e.Tag, e.Values)).ToList());

    static List<FemElementLoad> Resolve(FragmentFieldLoad field, FragmentRefElement[] refs, FemLoadMeshContext mesh,
        List<FemValidationDiagnostic>? diagnostics = null) =>
        FragmentFieldLoadResolver.Resolve([field], new ParentMeshMapper(refs), refs, mesh, diagnostics ?? []);

    static double Fz(List<FemElementLoad> loads, FemLoadMeshContext mesh) =>
        FemLoadCaseNodalForces.Resolve(new FemLoadCase { Id = 7 }, loads, [], mesh).Total.Fz;

    [Fact]
    public void Uniform_FineMeshExact_CoarseMeshTakesCenterValue()
    {
        FragmentRefElement[] refs = [Quad("1", 0, 0, 1, 1), Quad("2", 1, 0, 2, 1)];
        var field = Field("uniform", ("1", [1000]), ("2", [3000]));

        var fine = Grid(0, 0, 2, 1, 4, 2);
        var loads = Resolve(field, refs, fine);
        Assert.Equal(2, loads.Count);
        Assert.All(loads, x => Assert.Equal((FemLoadTargetKinds.Elements, "global", "z", 7), (x.TargetKind, x.CoordinateSystem, x.Axis, x.LoadCaseId)));
        Assert.Equal(4000, Fz(loads, fine), 6);

        var coarse = Grid(0, 0, 2, 1, 1, 1);
        Assert.NotEqual(4000, Fz(Resolve(field, refs, coarse), coarse), 3);
    }

    [Fact]
    public void Nodal_InterpolatedByParentShapeFunctions()
    {
        // q = 1000·x на [0, 2]²: узлы хранения (0,0), (2,0), (0,2), (2,2).
        FragmentRefElement[] refs = [Quad("1", 0, 0, 2, 2)];
        var mesh = Grid(0, 0, 2, 2, 2, 2);

        var loads = Resolve(Field("nodal", ("1", [0, 2000, 0, 2000])), refs, mesh);

        Assert.Equal(4, loads.Count);
        Assert.Equal(4000, Fz(loads, mesh), 6);
    }

    [Fact]
    public void Edge_OnMeshEdgesOfSameLine()
    {
        FragmentRefElement[] refs = [Quad("1", 0, 0, 1, 1)];
        var mesh = Grid(0, 0, 1, 1, 2, 2);

        var loads = Resolve(Field("edge", ("1", [1, 2, 1000, 3000])), refs, mesh);

        Assert.Equal(2, loads.Count);
        Assert.Equal(2000, Fz(loads, mesh), 6);
    }

    [Fact]
    public void OtherPlane_Unmapped_Diagnosed()
    {
        var diagnostics = new List<FemValidationDiagnostic>();
        var loads = Resolve(Field("uniform", ("1", [1000])), [Quad("1", 0, 0, 1, 1)], Grid(0, 0, 1, 1, 1, 1, z: 1), diagnostics);

        Assert.Empty(loads);
        Assert.Contains(diagnostics, d => d.Code == "fragment_field_unmapped" && !d.IsError);
    }
}
