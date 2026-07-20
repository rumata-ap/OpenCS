using OpenCS.OpenSees.Results;

namespace OpenCS.OpenSees.Tests;

public class FemLinearResultParserTests
{
    static string WriteRun(Action<string> fill)
    {
        string dir = Path.Combine(Path.GetTempPath(), "opencs_fem_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        fill(dir);
        return dir;
    }

    [Fact]
    public void Parse_ValidFiles_ReturnsRows()
    {
        string dir = WriteRun(d =>
        {
            File.WriteAllText(Path.Combine(d, "completed.marker"), "0\n");
            File.WriteAllText(Path.Combine(d, "node_disp.out"),
                "1 0 0 0 0 0 0\n2 0.001 0 -0.005 0 0.002 0\n");
            File.WriteAllText(Path.Combine(d, "node_reactions.out"),
                "1 0 0 1000 0 3000 0\n");
            File.WriteAllText(Path.Combine(d, "element_forces.out"),
                "1 1000 0 0 0 0 0 -1000 0 0 0 0 3000\n");
        });

        var (disp, react, forces) = new FemLinearResultParser().Parse(dir);

        Assert.Equal(2, disp.Count);
        Assert.Equal(-0.005, disp.Single(d => d.NodeTag == 2).Uz, 9);
        var r = Assert.Single(react);
        Assert.Equal(1000, r.Rz, 6);
        var f = Assert.Single(forces);
        Assert.Equal(1000, f.Ni, 6);
        Assert.Equal(3000, f.Mzj, 6);

        Directory.Delete(dir, true);
    }

    [Fact]
    public void Parse_MissingMarker_Throws()
    {
        string dir = WriteRun(d =>
            File.WriteAllText(Path.Combine(d, "node_disp.out"), "1 0 0 0 0 0 0\n"));
        Assert.Throws<OpenSeesResultException>(() => new FemLinearResultParser().Parse(dir));
        Directory.Delete(dir, true);
    }
}
