using OpenCS.OpenSees.Results;
using OpenCS.OpenSees.Structural;
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
                stepStatus: "# step loadFactor converged isRefinement\n1 0.5 1 0\n2 1.0 1 0\n",
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
                stepStatus: "# step loadFactor converged isRefinement\n1 0.5 1 0\n2 0.5 0 1\n",   // шаг 2 не сошёлся
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
                stepStatus: "# step loadFactor converged isRefinement\n1 0.5 1 0\n2 1.0 1 0\n",
                disp: "0.5 0 0 0 0 0 0 0 0 -0.001 0 0.002 0\n",   // не хватает строки для шага 2
                react: "0.5 0 0 500 0 0 0\n1.0 0 0 1000 0 0 0\n",
                forces: "0.5 -100 0 500 0 300 0 100 0 -500 0 0 0\n" +
                        "1.0 -200 0 1000 0 600 0 200 0 -1000 0 0 0\n");

            Assert.Throws<OpenSeesResultException>(() => new FemNonlinearResultParser().Parse(dir));
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [Fact]
    public void Parse_NulBytes_ReportsCorruptedOutput()
    {
        string dir = NewDir();
        try
        {
            WriteCommonFiles(dir,
                stepStatus: "# step loadFactor converged isRefinement\n1 0.5 1 0\n",
                disp: "0.5 0 0 0 0 0 0 0 0 0\0\0\0\n",
                react: "0.5 0 0 500 0 0 0\n",
                forces: "0.5 -100 0 500 0 300 0 100 0 -500 0 0 0\n");

            var exception = Assert.Throws<OpenSeesResultException>(() => new FemNonlinearResultParser().Parse(dir));
            Assert.Equal("CorruptedOutput", exception.Code);
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [Fact]
    public void Parse_RefinedHistory_PreservesRefinementFlag()
    {
        string dir = NewDir();
        try
        {
            WriteCommonFiles(dir,
                stepStatus: "# step loadFactor converged isRefinement\n1 0.2 1 0\n2 0.3 1 1\n3 0.4 0 1\n",
                disp: "0.2 0 0 0 0 0 0 0 0 -0.001 0 0.002 0\n" +
                      "0.3 0 0 0 0 0 0 0 0 -0.002 0 0.004 0\n",
                react: "0.2 0 0 500 0 0 0\n0.3 0 0 600 0 0 0\n",
                forces: "0.2 -100 0 500 0 300 0 100 0 -500 0 0 0\n" +
                        "0.3 -120 0 600 0 360 0 120 0 -600 0 0 0\n");

            var steps = new FemNonlinearResultParser().Parse(dir);

            Assert.Equal(3, steps.Count);
            Assert.False(steps[0].IsRefinement);
            Assert.True(steps[1].IsRefinement);
            Assert.True(steps[1].Converged);
            Assert.True(steps[2].IsRefinement);
            Assert.False(steps[2].Converged);
            Assert.Empty(steps[2].Displacements);
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [Fact]
    public void FiberStateParser_ReadsStatesAndLocations()
    {
        string dir = NewDir();
        try
        {
            File.WriteAllText(Path.Combine(dir, "nonlinear_fiber_states.out"),
                "# step loadFactor elementTag integrationPoint fiberIndex stressPa strain\n" +
                "1 0.2 10 1 0 1200000 0.0005\n" +
                "2 0.3 10 1 0 1400000 0.0007\n");
            File.WriteAllText(Path.Combine(dir, "nonlinear_section_order.json"),
                "{\"locations\":[{\"elementTag\":10,\"integrationPoint\":1,\"sectionTag\":2,\"fiberCount\":1,\"distanceFromElementStartM\":0.5,\"elementLengthM\":2,\"relativePosition\":0.25}]}");

            var parser = new FemNonlinearFiberStateParser();
            var states = parser.Parse(Path.Combine(dir, "nonlinear_fiber_states.out"));
            var locations = parser.ParseLocations(Path.Combine(dir, "nonlinear_section_order.json"));

            Assert.Equal(2, states.Count);
            Assert.Equal(1_200_000, states[0].StressPa, 6);
            Assert.Equal(0.0007, states[1].Strain, 8);
            var location = Assert.Single(locations);
            Assert.Equal(10, location.ElementTag);
            Assert.Equal(0.25, location.RelativePosition, 8);
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [Fact]
    public void FiberStateParser_ReadsOnlyRequestedSection()
    {
        string dir = NewDir();
        try
        {
            string path = Path.Combine(dir, "nonlinear_fiber_states.out");
            File.WriteAllText(path,
                "# step loadFactor elementTag integrationPoint fiberIndex stressPa strain\n" +
                "1 0.2 10 1 0 1200000 0.0005\n" +
                "1 0.2 10 2 0 1300000 0.0006\n" +
                "2 0.3 10 1 0 1400000 0.0007\n" +
                "1 0.2 11 1 0 1500000 0.0008\n");

            var states = new FemNonlinearFiberStateParser().ParseSection(path, 10, 1);

            Assert.Equal(2, states.Count);
            Assert.All(states, state =>
            {
                Assert.Equal(10, state.ElementTag);
                Assert.Equal(1, state.IntegrationPoint);
            });
        }
        finally { Directory.Delete(dir, recursive: true); }
    }
}
