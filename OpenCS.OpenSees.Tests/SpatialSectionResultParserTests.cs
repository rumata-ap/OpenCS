using OpenCS.OpenSees.Results;

namespace OpenCS.OpenSees.Tests;

public sealed class SpatialSectionResultParserTests
{
    [Fact]
    public void Parser_reads_history_and_maps_OpenSees_components()
    {
        string root = CreateDirectory();
        try
        {
            string history = Path.Combine(root, "section_history.out");
            string marker = Path.Combine(root, "completed.marker");
            File.WriteAllText(history,
                "# step loadFactor axialForceN openSeesMzNm openSeesMyNm rotationY rotationZ curvatureMagnitude converged residual\n" +
                "\n" +
                "1 0.5 1000 2000 3000 0.004 0.003 0.005 1 1e-9\n" +
                "2 1.0 1000 4000 6000 0.008 0.006 0.010 true 2e-9\n");
            File.WriteAllText(marker, "done\n");

            var rows = new SpatialSectionResultParser().Parse(history, marker);

            Assert.Equal(2, rows.Count);
            Assert.Equal(4000, rows[1].MomentMxNm);
            Assert.Equal(6000, rows[1].MomentMyNm);
            Assert.Equal(0.006, rows[1].CurvatureMx);
            Assert.Equal(0.008, rows[1].CurvatureMy);
            Assert.Equal(0.010, rows[1].CurvatureMagnitude);
            Assert.True(rows[1].Converged);
        }
        finally
        {
            DeleteDirectory(root);
        }
    }

    [Fact]
    public void Parser_rejects_missing_history_marker_and_malformed_rows()
    {
        string root = CreateDirectory();
        try
        {
            string history = Path.Combine(root, "section_history.out");
            string marker = Path.Combine(root, "completed.marker");

            AssertCode("MissingHistory", () => new SpatialSectionResultParser().Parse(history, marker));

            File.WriteAllText(history, "1 1 0 0 0 0 0 0 1 0\n");
            AssertCode("MissingMarker", () => new SpatialSectionResultParser().Parse(history, marker));

            File.WriteAllText(marker, "done\n");
            File.WriteAllText(history, "1 1 0 0 0 0 0 1\n");
            AssertCode("WrongColumnCount", () => new SpatialSectionResultParser().Parse(history, marker));

            File.WriteAllText(history, "1 NaN 0 0 0 0 0 0 1 0\n");
            AssertCode("InvalidNumber", () => new SpatialSectionResultParser().Parse(history, marker));

            File.WriteAllText(history, "1 1 0 0 0 0 0 0 maybe 0\n");
            AssertCode("InvalidBoolean", () => new SpatialSectionResultParser().Parse(history, marker));
        }
        finally
        {
            DeleteDirectory(root);
        }
    }

    [Fact]
    public void Parser_rejects_empty_history()
    {
        string root = CreateDirectory();
        try
        {
            string history = Path.Combine(root, "section_history.out");
            string marker = Path.Combine(root, "completed.marker");
            File.WriteAllText(history, "# header only\n\n");
            File.WriteAllText(marker, "done\n");

            AssertCode("Empty", () => new SpatialSectionResultParser().Parse(history, marker));
        }
        finally
        {
            DeleteDirectory(root);
        }
    }

    private static string CreateDirectory()
    {
        string path = Path.Combine(Path.GetTempPath(), "opencs-spatial-parser", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    private static void DeleteDirectory(string path)
    {
        if (Directory.Exists(path))
            Directory.Delete(path, recursive: true);
    }

    private static void AssertCode(string code, Action action)
    {
        OpenSeesResultException exception = Assert.Throws<OpenSeesResultException>(action);
        Assert.Equal(code, exception.Code);
    }
}
