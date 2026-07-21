using OpenCS.OpenSees.Results;
using Xunit;

namespace OpenCS.OpenSees.Tests;

public class FemNonlinearResultParserTests
{
    static string NewDir()
    {
        string dir = Path.Combine(Path.GetTempPath(), "opencs_nonlinear_parser_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return dir;
    }

    static void WriteCommonFiles(string dir, string stepStatus, string disp, string react, string forces)
    {
        File.WriteAllText(Path.Combine(dir, "recorder_order.json"),
            "{\"nodeTags\":[1,2],\"restrainedTags\":[1],\"elemTags\":[1]}");
        File.WriteAllText(Path.Combine(dir, "step_status.out"), stepStatus);
        File.WriteAllText(Path.Combine(dir, "nonlinear_node_disp.out"), disp);
        File.WriteAllText(Path.Combine(dir, "nonlinear_node_reactions.out"), react);
        File.WriteAllText(Path.Combine(dir, "nonlinear_element_forces.out"), forces);
        File.WriteAllText(Path.Combine(dir, "completed.marker"), "done");
    }

    [Fact]
    public void Parse_AllStepsConverged_ReturnsFullHistory()
    {
        string dir = NewDir();
        try
        {
            WriteCommonFiles(dir,
                stepStatus: "# step loadFactor converged\n1 0.5 1\n2 1.0 1\n",
                disp: "0.5 0 0 0 0 0 0 0 0 -0.001 0 0.002 0\n" +
                      "1.0 0 0 0 0 0 0 0 0 -0.002 0 0.004 0\n",
                react: "0.5 0 0 500 0 0 0\n1.0 0 0 1000 0 0 0\n",
                forces: "0.5 -100 0 500 0 300 0 100 0 -500 0 0 0\n" +
                        "1.0 -200 0 1000 0 600 0 200 0 -1000 0 0 0\n");

            var steps = new FemNonlinearResultParser().Parse(dir);

            Assert.Equal(2, steps.Count);
            Assert.True(steps[0].Converged);
            Assert.Equal(0.5, steps[0].LoadFactor, 6);
            Assert.Equal(2, steps[0].Displacements.Count);
            Assert.Equal(2, steps[0].Displacements[1].NodeTag);
            Assert.Equal(-0.001, steps[0].Displacements[1].Uz, 6);
            Assert.Equal(1, steps[0].Reactions.Single().NodeTag);
            Assert.Equal(500, steps[0].Reactions.Single().Rz, 6);
            Assert.Equal(1, steps[0].ElementForces.Single().ElemTag);
            Assert.Equal(-100, steps[0].ElementForces.Single().Ni, 6);

            Assert.True(steps[1].Converged);
            Assert.Equal(1.0, steps[1].LoadFactor, 6);
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [Fact]
    public void Parse_LastStepDiverges_TrailingStepIsEmptyAndNotConverged()
    {
        string dir = NewDir();
        try
        {
            WriteCommonFiles(dir,
                stepStatus: "# step loadFactor converged\n1 0.5 1\n2 0.5 0\n",   // шаг 2 не сошёлся
                disp: "0.5 0 0 0 0 0 0 0 0 -0.001 0 0.002 0\n",                  // только 1 строка (сошедшийся шаг)
                react: "0.5 0 0 500 0 0 0\n",
                forces: "0.5 -100 0 500 0 300 0 100 0 -500 0 0 0\n");

            var steps = new FemNonlinearResultParser().Parse(dir);

            Assert.Equal(2, steps.Count);
            Assert.True(steps[0].Converged);
            Assert.False(steps[1].Converged);
            Assert.Empty(steps[1].Displacements);
            Assert.Empty(steps[1].Reactions);
            Assert.Empty(steps[1].ElementForces);
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [Fact]
    public void Parse_MissingMarker_Throws()
    {
        string dir = NewDir();
        try
        {
            Assert.Throws<OpenSeesResultException>(() => new FemNonlinearResultParser().Parse(dir));
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [Fact]
    public void Parse_RowCountMismatch_Throws()
    {
        string dir = NewDir();
        try
        {
            WriteCommonFiles(dir,
                stepStatus: "# step loadFactor converged\n1 0.5 1\n2 1.0 1\n",
                disp: "0.5 0 0 0 0 0 0 0 0 -0.001 0 0.002 0\n",   // не хватает строки для шага 2
                react: "0.5 0 0 500 0 0 0\n1.0 0 0 1000 0 0 0\n",
                forces: "0.5 -100 0 500 0 300 0 100 0 -500 0 0 0\n" +
                        "1.0 -200 0 1000 0 600 0 200 0 -1000 0 0 0\n");

            Assert.Throws<OpenSeesResultException>(() => new FemNonlinearResultParser().Parse(dir));
        }
        finally { Directory.Delete(dir, recursive: true); }
    }
}
