using OpenCS.OpenSees.Analysis;
using OpenCS.OpenSees.Results;

namespace OpenCS.OpenSees.Tests;

public sealed class SectionResultParserTests
{
    [Fact]
    public void Parser_reads_comments_blank_lines_and_multiple_rows()
    {
        string directory = CreateDirectory();
        try
        {
            string history = Path.Combine(directory, "section_history.out");
            string marker = Path.Combine(directory, "completed.marker");
            File.WriteAllText(history,
                "# step loadFactor axialForceN bendingMomentNm axialStrain curvature converged residual\n" +
                "\n" +
                "1 1 1000 20 0.0001 0.001 1 0\n" +
                "2 2 1100 25 0.0002 0.002 true 1e-9\n");
            File.WriteAllText(marker, "done\n");

            IReadOnlyList<SectionHistoryRow> rows = new SectionResultParser().Parse(history, marker);

            Assert.Equal(2, rows.Count);
            Assert.Equal(1, rows[0].Step);
            Assert.Equal(20, rows[0].BendingMomentNm);
            Assert.True(rows[1].Converged);
            Assert.Equal(1e-9, rows[1].Residual);
        }
        finally
        {
            DeleteDirectory(directory);
        }
    }

    [Theory]
    [InlineData("", "Empty")]
    [InlineData("1 2 3", "WrongColumnCount")]
    [InlineData("1 2 nope 3 4 5 1 0", "InvalidNumber")]
    [InlineData("1 2 3 4 5 6 1", "WrongColumnCount")]
    public void Parser_returns_typed_diagnostic_for_invalid_history(string content, string expectedCode)
    {
        string directory = CreateDirectory();
        try
        {
            string history = Path.Combine(directory, "section_history.out");
            string marker = Path.Combine(directory, "completed.marker");
            File.WriteAllText(history, content);
            File.WriteAllText(marker, "done");

            OpenSeesResultException exception = Assert.Throws<OpenSeesResultException>(() =>
                new SectionResultParser().Parse(history, marker));

            Assert.Equal(expectedCode, exception.Code);
        }
        finally
        {
            DeleteDirectory(directory);
        }
    }

    [Fact]
    public void Parser_rejects_missing_completion_marker()
    {
        string directory = CreateDirectory();
        try
        {
            string history = Path.Combine(directory, "section_history.out");
            File.WriteAllText(history, "1 1 0 0 0 0 1 0\n");

            OpenSeesResultException exception = Assert.Throws<OpenSeesResultException>(() =>
                new SectionResultParser().Parse(history, Path.Combine(directory, "missing.marker")));

            Assert.Equal("MissingMarker", exception.Code);
        }
        finally
        {
            DeleteDirectory(directory);
        }
    }

    private static string CreateDirectory()
    {
        string path = Path.Combine(Path.GetTempPath(), "opencs-opensees-parser", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    private static void DeleteDirectory(string path)
    {
        if (Directory.Exists(path))
            Directory.Delete(path, recursive: true);
    }
}
