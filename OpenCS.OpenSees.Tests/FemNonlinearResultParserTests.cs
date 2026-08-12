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
                stepStatus: "# step stageIndex loadFactor converged isRefinement\n1 0 0.5 1 0\n2 0 1.0 1 0\n",
                disp: "0.5 0 0 0 0 0 0 0 0 -0.001 0 0.002 0\n" +
                      "1.0 0 0 0 0 0 0 0 0 -0.002 0 0.004 0\n",
                react: "0.5 0 0 500 0 0 0\n1.0 0 0 1000 0 0 0\n",
                forces: "0.5 -100 0 500 0 300 0 100 0 -500 0 0 0\n" +
                        "1.0 -200 0 1000 0 600 0 200 0 -1000 0 0 0\n");

            var steps = new FemNonlinearResultParser().Parse(dir);

            Assert.Equal(2, steps.Count);
            Assert.True(steps[0].Converged);
            Assert.Equal(0.5, steps[0].LoadFactor, 6);
            Assert.Equal(0, steps[0].StageIndex);
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
                stepStatus: "# step stageIndex loadFactor converged isRefinement\n1 0 0.5 1 0\n2 0 0.5 0 1\n",   // шаг 2 не сошёлся
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
    public void Parse_EmptyDirectory_Throws()
    {
        string dir = NewDir();
        try
        {
            Assert.Throws<OpenSeesResultException>(() => new FemNonlinearResultParser().Parse(dir));
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [Fact]
    public void Parse_MissingCompletedMarker_StillReturnsConsistentSteps()
    {
        // Регрессия: процесс OpenSees убит по таймауту/сбою ДО записи completed.marker (не успел
        // дойти до штатного завершения расчёта), но recorder-файлы (-closeOnWrite) уже содержат
        // полностью согласованные строки для всех фактически сошедшихся шагов. Раньше отсутствие
        // completed.marker выбрасывало ВСЕ эти уже полученные данные — см. реальный кейс: расчёт
        // убит по 120-секундному таймауту возле точки потери устойчивости (λ≈2.2), при этом
        // step_status.out/node_disp.out/element_forces.out содержали 44 полностью целых
        // сошедшихся шага.
        string dir = NewDir();
        try
        {
            WriteCommonFiles(dir,
                stepStatus: "# step stageIndex loadFactor converged isRefinement\n1 0 0.5 1 0\n2 0 1.0 1 0\n",
                disp: "0.5 0 0 0 0 0 0 0 0 -0.001 0 0.002 0\n" +
                      "1.0 0 0 0 0 0 0 0 0 -0.002 0 0.004 0\n",
                react: "0.5 0 0 500 0 0 0\n1.0 0 0 1000 0 0 0\n",
                forces: "0.5 -100 0 500 0 300 0 100 0 -500 0 0 0\n" +
                        "1.0 -200 0 1000 0 600 0 200 0 -1000 0 0 0\n");
            File.Delete(Path.Combine(dir, "completed.marker"));

            var steps = new FemNonlinearResultParser().Parse(dir);

            Assert.Equal(2, steps.Count);
            Assert.True(steps[0].Converged);
            Assert.True(steps[1].Converged);
            Assert.Equal(1.0, steps[1].LoadFactor, 6);
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [Fact]
    public void Parse_RowCountMismatch_DropsTrailingStepMissingRecorderData()
    {
        // Обрыв процесса OpenSees (таймаут/сбой) между recorder-записью и записью строки
        // step_status.out для последнего шага — step_status.out указывает шаг сошедшимся, но
        // recorder-файл не успел получить для него строку. Раньше это валило парсинг целиком
        // (см. историю completed.marker); теперь такой "хвостовой" шаг молча опускается, а уже
        // полученные предыдущие шаги остаются доступны.
        string dir = NewDir();
        try
        {
            WriteCommonFiles(dir,
                stepStatus: "# step stageIndex loadFactor converged isRefinement\n1 0 0.5 1 0\n2 0 1.0 1 0\n",
                disp: "0.5 0 0 0 0 0 0 0 0 -0.001 0 0.002 0\n",   // не хватает строки для шага 2
                react: "0.5 0 0 500 0 0 0\n1.0 0 0 1000 0 0 0\n",
                forces: "0.5 -100 0 500 0 300 0 100 0 -500 0 0 0\n" +
                        "1.0 -200 0 1000 0 600 0 200 0 -1000 0 0 0\n");

            var steps = new FemNonlinearResultParser().Parse(dir);

            var step = Assert.Single(steps);
            Assert.True(step.Converged);
            Assert.Equal(0.5, step.LoadFactor, 6);
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [Fact]
    public void Parse_NulBytes_SubstitutesNaNForCorruptedRowAndKeepsOtherSteps()
    {
        // Известная нестабильность OpenSees 3.8.0 на Windows: даже с отключённой буферизацией
        // Tcl-канала изредка встречается блок нулевых байт внутри иначе корректной строки —
        // судя по всему, баг внутри самого OpenSees.exe, а не в нашей генерации Tcl (проверено
        // на реальных артефактах, см. FemNonlinearTclGenerator). Раньше это валило парсинг ВСЕГО
        // файла (и всех остальных, честных шагов); теперь портится только содержимое одной
        // строки — подставляются NaN, а остальные шаги остаются полностью пригодными.
        string dir = NewDir();
        try
        {
            WriteCommonFiles(dir,
                stepStatus: "# step stageIndex loadFactor converged isRefinement\n1 0 0.5 1 0\n2 0 1.0 1 0\n",
                disp: "0.5 0 0 0 0 0 0 0 0 0\0\0\0\n" +
                      "1.0 0 0 0 0 0 0 0 0 -0.002 0 0.004 0\n",
                react: "0.5 0 0 500 0 0 0\n1.0 0 0 1000 0 0 0\n",
                forces: "0.5 -100 0 500 0 300 0 100 0 -500 0 0 0\n" +
                        "1.0 -200 0 1000 0 600 0 200 0 -1000 0 0 0\n");

            var steps = new FemNonlinearResultParser().Parse(dir);

            Assert.Equal(2, steps.Count);
            Assert.True(steps[0].Converged);
            Assert.Equal(2, steps[0].Displacements.Count);
            Assert.True(double.IsNaN(steps[0].Displacements[1].Uz));

            Assert.True(steps[1].Converged);
            Assert.Equal(-0.002, steps[1].Displacements[1].Uz, 6);
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
                stepStatus: "# step stageIndex loadFactor converged isRefinement\n1 0 0.2 1 0\n2 0 0.3 1 1\n3 0 0.4 0 1\n",
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

    [Fact]
    public void Parse_TwoStages_AssignsStageIndexPerStep()
    {
        string dir = NewDir();
        try
        {
            WriteCommonFiles(dir,
                stepStatus: "# step stageIndex loadFactor converged isRefinement\n1 0 1.0 1 0\n2 1 1.0 1 0\n",
                disp: "0.5 0 0 0 0 0 0 0 0 -0.001 0 0.002 0\n" +
                      "1.0 0 0 0 0 0 0 0 0 -0.002 0 0.004 0\n",
                react: "0.5 0 0 500 0 0 0\n1.0 0 0 1000 0 0 0\n",
                forces: "0.5 -100 0 500 0 300 0 100 0 -500 0 0 0\n" +
                        "1.0 -200 0 1000 0 600 0 200 0 -1000 0 0 0\n");

            var steps = new FemNonlinearResultParser().Parse(dir);

            Assert.Equal(2, steps.Count);
            Assert.Equal(0, steps[0].StageIndex);
            Assert.Equal(1, steps[1].StageIndex);
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [Fact]
    public void ParseStepStatus_SixColumnRow_ReadsStopReason()
    {
        string dir = NewDir();
        try
        {
            File.WriteAllText(Path.Combine(dir, "recorder_order.json"), "{\"nodeTags\":[],\"restrainedTags\":[],\"elemTags\":[]}");
            File.WriteAllText(Path.Combine(dir, "step_status.out"), "1 0 0.5 0 1 min_increment_reached\n");
            File.WriteAllText(Path.Combine(dir, "nonlinear_node_disp.out"), "");
            File.WriteAllText(Path.Combine(dir, "nonlinear_element_forces.out"), "");

            var steps = new FemNonlinearResultParser().Parse(dir);
            var step = Assert.Single(steps);
            Assert.False(step.Converged);
            Assert.Equal("min_increment_reached", step.StopReason);
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [Fact]
    public void ParseStepStatus_SixColumnRowWithConvergedTrue_Throws()
    {
        string dir = NewDir();
        try
        {
            File.WriteAllText(Path.Combine(dir, "recorder_order.json"), "{\"nodeTags\":[],\"restrainedTags\":[],\"elemTags\":[]}");
            File.WriteAllText(Path.Combine(dir, "step_status.out"), "1 0 0.5 1 0 unexpected_reason\n");
            File.WriteAllText(Path.Combine(dir, "nonlinear_node_disp.out"), "0.5\n");
            File.WriteAllText(Path.Combine(dir, "nonlinear_element_forces.out"), "0.5\n");

            Assert.Throws<OpenSeesResultException>(() => new FemNonlinearResultParser().Parse(dir));
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [Fact]
    public void ParseStepStatus_UnknownStopReason_DoesNotThrow()
    {
        string dir = NewDir();
        try
        {
            File.WriteAllText(Path.Combine(dir, "recorder_order.json"), "{\"nodeTags\":[],\"restrainedTags\":[],\"elemTags\":[]}");
            File.WriteAllText(Path.Combine(dir, "step_status.out"), "1 0 0.5 0 1 some_future_reason\n");
            File.WriteAllText(Path.Combine(dir, "nonlinear_node_disp.out"), "");
            File.WriteAllText(Path.Combine(dir, "nonlinear_element_forces.out"), "");

            var steps = new FemNonlinearResultParser().Parse(dir);
            Assert.Equal("some_future_reason", steps[0].StopReason);
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [Fact]
    public void ParseSwitches_MissingFile_ReturnsEmpty()
    {
        string dir = NewDir();
        try
        {
            Assert.Empty(new FemNonlinearResultParser().ParseSwitches(Path.Combine(dir, "path_control_switches.out")));
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [Fact]
    public void ParseSwitches_ValidFile_ReturnsRecords()
    {
        string dir = NewDir();
        try
        {
            var path = Path.Combine(dir, "path_control_switches.out");
            File.WriteAllText(path, "0 7\n1 15\n");
            var switches = new FemNonlinearResultParser().ParseSwitches(path);
            Assert.Equal(2, switches.Count);
            Assert.Equal(0, switches[0].StageIndex);
            Assert.Equal(7, switches[0].AtStepIndex);
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [Fact]
    public void ParseSwitches_AtStepIndexBelowOne_Throws()
    {
        string dir = NewDir();
        try
        {
            var path = Path.Combine(dir, "path_control_switches.out");
            File.WriteAllText(path, "0 0\n");
            Assert.Throws<OpenSeesResultException>(() => new FemNonlinearResultParser().ParseSwitches(path));
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [Fact]
    public void ParseSwitches_DuplicateStageIndex_Throws()
    {
        string dir = NewDir();
        try
        {
            var path = Path.Combine(dir, "path_control_switches.out");
            File.WriteAllText(path, "0 5\n0 9\n");
            Assert.Throws<OpenSeesResultException>(() => new FemNonlinearResultParser().ParseSwitches(path));
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [Fact]
    public void ParseSwitches_NonNumericValue_Throws()
    {
        string dir = NewDir();
        try
        {
            var path = Path.Combine(dir, "path_control_switches.out");
            File.WriteAllText(path, "0 abc\n");
            Assert.Throws<OpenSeesResultException>(() => new FemNonlinearResultParser().ParseSwitches(path));
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [Fact]
    public void ParseStageCompletions_MissingFile_ReturnsEmpty()
    {
        string dir = NewDir();
        try
        {
            Assert.Empty(new FemNonlinearResultParser().ParseStageCompletions(Path.Combine(dir, "path_control_stage_status.out")));
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [Fact]
    public void ParseStageCompletions_ValidFile_ReturnsRecords()
    {
        string dir = NewDir();
        try
        {
            var path = Path.Combine(dir, "path_control_stage_status.out");
            File.WriteAllText(path, "0 load_control_completed\n1 target_reached\n");
            var completions = new FemNonlinearResultParser().ParseStageCompletions(path);
            Assert.Equal(2, completions.Count);
            Assert.Equal("target_reached", completions[1].Reason);
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [Fact]
    public void ParseStageCompletions_DuplicateStageIndex_Throws()
    {
        string dir = NewDir();
        try
        {
            var path = Path.Combine(dir, "path_control_stage_status.out");
            File.WriteAllText(path, "0 ok\n0 failed\n");
            Assert.Throws<OpenSeesResultException>(() => new FemNonlinearResultParser().ParseStageCompletions(path));
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [Fact]
    public void ParseStageCompletions_NegativeStageIndex_Throws()
    {
        string dir = NewDir();
        try
        {
            var path = Path.Combine(dir, "path_control_stage_status.out");
            File.WriteAllText(path, "-1 failed\n");
            Assert.Throws<OpenSeesResultException>(() => new FemNonlinearResultParser().ParseStageCompletions(path));
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [Fact]
    public void ParseStageCompletions_NonNumericStageIndex_Throws()
    {
        string dir = NewDir();
        try
        {
            var path = Path.Combine(dir, "path_control_stage_status.out");
            File.WriteAllText(path, "abc load_control_completed\n");
            Assert.Throws<OpenSeesResultException>(() => new FemNonlinearResultParser().ParseStageCompletions(path));
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [Fact]
    public void ParseStageCompletions_StageIndexWithoutReason_Throws()
    {
        string dir = NewDir();
        try
        {
            var path = Path.Combine(dir, "path_control_stage_status.out");
            File.WriteAllText(path, "0\n");
            Assert.Throws<OpenSeesResultException>(() => new FemNonlinearResultParser().ParseStageCompletions(path));
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [Fact]
    public void ParseStageCompletions_FewerRowsThanStagesModel_IsNotParserResponsibility()
    {
        // Парсер не знает число стадий модели — неполнота проверяется вызывающей стороной
        // (FemNonlinearAnalysisService), не здесь. Парсер просто возвращает то, что есть.
        string dir = NewDir();
        try
        {
            var path = Path.Combine(dir, "path_control_stage_status.out");
            File.WriteAllText(path, "0 load_control_completed\n");
            var completions = new FemNonlinearResultParser().ParseStageCompletions(path);
            Assert.Single(completions);
        }
        finally { Directory.Delete(dir, recursive: true); }
    }
}
