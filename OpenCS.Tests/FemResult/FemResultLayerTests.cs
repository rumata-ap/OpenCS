using System.Text.Json;
using System.Windows.Media.Media3D;
using CScore;
using CScore.Fem;
using CSfea.CScoreBridge.Structural;
using OpenCS.OpenSees.Structural;
using OpenCS.Services;
using OpenCS.Utilites;
using OpenCS.ViewModels;
using OpenCS.ViewModels.FemResult;
using Xunit;

namespace OpenCS.Tests.FemResult;

/// <summary>Сетка, шаги и слой результата расчёта для показа в основном 3D-виде схемы.</summary>
public class FemResultLayerTests
{
    static FemMeshNode N(string tag, double x, double y) => new() { NodeTag = tag, X = x, Y = y };

    [Fact]
    public void Mesh_BarQ4T3_SkipsNonCanonicalTags()
    {
        var mesh = new FemResultMesh(
            [N("1", 0, 0), N("2", 1, 0), N("3", 0, 1), N("4", 1, 1), N("5", 2, 0), N("6", 2, 1), N("x", 9, 9)],
            [
                new FemElement { ElemTag = "10", NodeIdsJson = "[5,6]" },
                // Четырёхугольник хранится «1 2 4 3» — контур 1 → 2 → 4 → 3.
                new FemElement { ElemTag = "20", ElemType = "shell", NodeIdsJson = "[1,2,3,4]" },
                new FemElement { ElemTag = "21", ElemType = "shell", NodeIdsJson = "[2,5,4]" },
                new FemElement { ElemTag = "abc", NodeIdsJson = "[1,2]" },
                new FemElement { ElemTag = "30", NodeIdsJson = "[1,99]" },
            ]);

        Assert.Equal(["10"], mesh.Bars.Select(b => b.Tag));
        Assert.Equal(["20", "21"], mesh.Shells.Select(s => s.Tag));
        Assert.Equal([1, 2, 4, 3], mesh.Shells[0].Contour);
        Assert.Equal(3, mesh.Elements.Count);
        Assert.Equal([1, 2, 3, 4, 5, 6], mesh.UsedNodes);
        Assert.Equal(Math.Sqrt(5), mesh.Diagonal, 12);
        Assert.Equal(2 * (1 + 4 + 3), mesh.EdgeLines(n => mesh.Nodes[n]).Count);
        Assert.Null(mesh.NodesOf("abc"));
    }

    [Theory]
    [InlineData(10, 0.5, 1)]
    [InlineData(10, 0, 1)]
    [InlineData(10, 0.004, 125)]
    public void SuggestDeformScale_FivePercentOfDiagonal(double diagonal, double maxDisp, double expected) =>
        Assert.Equal(expected, FemResultMesh.SuggestDeformScale(diagonal, maxDisp), 9);

    sealed class TempDb : IDisposable
    {
        readonly string _path = Path.Combine(Path.GetTempPath(), "opencs_result_layer_" + Guid.NewGuid().ToString("N") + ".db");
        public DatabaseService Db { get; }
        public FemSchema Schema { get; } = new() { Tag = "Схема" };

        public TempDb()
        {
            Db = new DatabaseService(_path);
            Db.SaveFemSchema(Schema);
            Db.SaveFemMeshSnapshot(Schema.Id,
                [N("1", 0, 0), N("2", 2, 0), N("3", 0, 1)],
                [
                    new FemElement { ElemTag = "1", NodeIdsJson = "[1,2]" },
                    new FemElement { ElemTag = "2", ElemType = "shell", NodeIdsJson = "[1,2,3]" },
                ]);
        }

        public FemResultMesh Mesh() => new(Db.GetFemMeshNodes(Schema.Id), Db.GetFemMeshElements(Schema.Id));

        public void Dispose()
        {
            Db.Dispose();
            try { File.Delete(_path); } catch (IOException) { }
        }
    }

    static OpenSeesResultSteps OpenSeesTwoSteps(TempDb t)
    {
        var result = new FemNonlinearResult
        {
            Status = "ok",
            Steps =
            [
                new FemNonlinearStepResult(1, 0.5, true, [new FemNodeDisplacement(2, 0, 0, -0.01, 0, 0, 0)], [], []),
                new FemNonlinearStepResult(2, 1.0, true, [new FemNodeDisplacement(2, 0, 0, -0.02, 0, 0, 0)], [], []),
            ],
            CalcTypeName = "C",
        };
        return new OpenSeesResultSteps(new FemAnalysisResultVM(
            new CalcResult { Status = "ok", DataJson = JsonSerializer.Serialize(result) }, t.Db, t.Schema));
    }

    [Fact]
    public void OpenSeesSteps_FollowVmStep()
    {
        using var t = new TempDb();
        var steps = OpenSeesTwoSteps(t);
        int changed = 0;
        steps.StepChanged += () => changed++;

        Assert.True(steps.HasSteps);
        Assert.Equal(2, steps.StepCount);
        Assert.Equal(1, steps.SelectedStepIndex);   // последний сошедшийся шаг
        Assert.Equal(new Vector3D(0, 0, -0.02), steps.Displacement(2));
        Assert.Null(steps.Displacement(1));
        Assert.Equal(0.02, steps.MaxDisplacement(), 12);

        steps.SelectedStepIndex = 0;
        Assert.Equal(1, changed);
        Assert.Equal(new Vector3D(0, 0, -0.01), steps.Displacement(2));
    }

    static RcSecantStepFields Fields(double uz) => new()
    {
        NodeIds = [1, 2],
        Displacements = [0, 0, 0, 0, 0, 0, 0, 0, uz, 0, 0, 0],
        ShellIds = [], ShellForces = [], ShellStates = [], ShellFlags = [],
        BeamIds = [], BeamForces = [], BeamFlags = [],
    };

    [Fact]
    public void CsfeaSteps_DisplacementsFromRecordedFields()
    {
        using var t = new TempDb();
        var summary = new FemCsfeaResultSummary
        {
            Stages = [new FemCsfeaStageSummary("Ст1", 2, 0, 0, 0)],
            Steps =
            [
                new FemCsfeaStepSummary(1, 0, 1, 0.5, false, true, 3, 1e-6, null, 1, true),
                new FemCsfeaStepSummary(2, 0, 2, 1.0, false, true, 3, 1e-6, null, 2, true),
            ],
        };
        var result = new CalcResult { TaskKind = FemCsfeaRunner.TaskKind, TaskTag = "Р1", DataJson = summary.ToJson() };
        t.Db.SaveCalcResult(result);
        t.Db.SaveFemResultSteps(result.Id, [(1, FemCsfeaStepFieldsCodec.Pack(Fields(-0.01))), (2, FemCsfeaStepFieldsCodec.Pack(Fields(-0.03)))]);
        var steps = new CsfeaResultSteps(new FemCsfeaResultVM(result, t.Db, t.Schema));
        int changed = 0;
        steps.StepChanged += () => changed++;

        Assert.True(steps.HasSteps);
        Assert.True(steps.HasDisplacements);
        Assert.Equal(new Vector3D(0, 0, -0.03), steps.Displacement(2));
        Assert.Equal(0.03, steps.MaxDisplacement(), 12);

        steps.SelectedStepIndex = 0;
        Assert.Equal(1, changed);
        Assert.Equal(new Vector3D(0, 0, -0.01), steps.Displacement(2));
    }

    [Fact]
    public void CsfeaLinear_CasesAsList()
    {
        using var t = new TempDb();
        var summary = new FemCsfeaResultSummary
        {
            Kind = FemCsfeaRunner.LinearAnalysisKind,
            Stages = [new FemCsfeaStageSummary("Пост.", 1, 0, 0, 0), new FemCsfeaStageSummary("РСН 1", 1, 0, 0, 0)],
            Steps =
            [
                new FemCsfeaStepSummary(1, 0, 1, 1.0, false, true, 0, 0, null),
                new FemCsfeaStepSummary(2, 1, 1, 1.0, false, true, 0, 0, null),
            ],
        };
        var steps = new CsfeaResultSteps(new FemCsfeaResultVM(
            new CalcResult { TaskKind = FemCsfeaRunner.TaskKind, DataJson = summary.ToJson() }, t.Db, t.Schema));
        var layer = new FemResultLayer(new FemAnalysis { Tag = "Л" }, "Л", steps, t.Mesh());

        Assert.True(steps.IsCaseList);
        Assert.Equal(["Пост.", "РСН 1"], layer.StepNames);
        Assert.True(layer.ShowCaseList);
        Assert.False(layer.ShowStepSlider);
    }

    [Fact]
    public void CsfeaLinear_CaseNamesFromSchemaLoads()
    {
        using var t = new TempDb();
        t.Schema.LoadCases.Add(new FemLoadCase { Id = 7, Tag = "Снег" });
        t.Schema.LoadDefinitions.Add(new FemLoadDefinition { Tag = "РСН 1" });
        var combo = new FemLoadExpression { Mode = FemLoadExpressionMode.Sum, Terms = [new FemLoadTerm { LoadCaseId = 7, Coefficient = 1.4 }] };
        t.Schema.LoadDefinitions[0].SetExpression(combo);
        var single = new FemLoadExpression { Mode = FemLoadExpressionMode.Single, LoadCaseIds = [7] };
        var pars = new OpenCS.Tasks.FemAnalysisParams
        {
            Stages =
            [
                new OpenCS.Tasks.FemAnalysisStage { Tag = "Стадия 1", LoadExpressionJson = single.ToJson() },
                new OpenCS.Tasks.FemAnalysisStage { Tag = "Стадия 2", LoadExpressionJson = combo.ToJson() },
                new OpenCS.Tasks.FemAnalysisStage { Tag = "Своя", LoadExpressionJson = "{}" },
            ],
        };
        var analysis = new FemAnalysis { Kind = FemCsfeaRunner.LinearAnalysisKind, Tag = "Л", ParamsJson = pars.ToJson() };
        Assert.Equal(["Снег", "РСН 1", "Своя"], OpenCS.Tasks.FemAnalysisCaseNames.Resolve(t.Schema, analysis));

        // Старый результат со «Стадия N» в сводке показывает имена загружений.
        var summary = new FemCsfeaResultSummary
        {
            Kind = FemCsfeaRunner.LinearAnalysisKind,
            Stages = [new("Стадия 1", 1, 0, 0, 0), new("Стадия 2", 1, 0, 0, 0), new("Своя", 1, 0, 0, 0)],
            Steps = [new(1, 0, 1, 1.0, false, true, 0, 0, null), new(2, 1, 1, 1.0, false, true, 0, 0, null), new(3, 2, 1, 1.0, false, true, 0, 0, null)],
        };
        var steps = new CsfeaResultSteps(new FemCsfeaResultVM(
            new CalcResult { TaskKind = FemCsfeaRunner.TaskKind, DataJson = summary.ToJson() }, t.Db, t.Schema, analysis));
        Assert.Equal(["Снег", "РСН 1", "Своя"], steps.StepNames);
    }

    [Fact]
    public void CsfeaSteps_WithoutFields_HaveNoDisplacements()
    {
        using var t = new TempDb();
        var summary = new FemCsfeaResultSummary { Steps = [new FemCsfeaStepSummary(1, 0, 1, 1.0, false, true, 3, 1e-6, null)] };
        var steps = new CsfeaResultSteps(new FemCsfeaResultVM(
            new CalcResult { TaskKind = FemCsfeaRunner.TaskKind, DataJson = summary.ToJson() }, t.Db, t.Schema));

        Assert.False(steps.HasSteps);
        Assert.False(steps.HasDisplacements);
        Assert.Null(steps.Displacement(2));
        Assert.Equal(0, steps.MaxDisplacement());
    }

    [Fact]
    public void Layer_ValuesDeformedNodesAndSummaryFollowStepAndScale()
    {
        using var t = new TempDb();
        var layer = new FemResultLayer(new FemAnalysis { Tag = "Р1" }, "Р1", OpenSeesTwoSteps(t), t.Mesh());
        Assert.False(layer.DeformedShown);   // по умолчанию деформированная схема выключена
        Assert.Equal(new Point3D(2, 0, 0), layer.NodeMap["2"]);
        Assert.True(layer.ShowStepSlider);
        Assert.False(layer.ShowCaseList);
        layer.ShowDeformed = true;
        int changed = 0;
        layer.Changed += () => changed++;

        // Диагональ √5 м, наибольшее перемещение 0,02 м → масштаб 0,05·√5/0,02 = 5,59.
        Assert.Equal(5.59, layer.DeformScale, 9);
        Assert.True(layer.DeformedShown);
        Assert.Equal(new Point3D(2, 0, -0.02 * 5.59), layer.NodeMap["2"]);
        Assert.Equal(new Point3D(0, 0, 0), layer.NodeMap["1"]);   // перемещения узла нет — исходное положение
        // На КЭ — среднее по узлам с данными, мм.
        Assert.Equal(-20, layer.Value("1", FemResultComponent.Uz)!.Value, 9);
        Assert.Equal(20, layer.Value("2", FemResultComponent.U)!.Value, 9);
        Assert.Null(layer.Value("99", FemResultComponent.Uz));
        layer.UpdateSummary(FemResultComponent.Uz);
        Assert.NotEqual(Loc.S("FemResultNoDisplacements"), layer.SummaryText);
        // Экстремум по фрагменту: только узлы показанных КЭ (у пустого фрагмента перемещений нет).
        layer.UpdateSummary(FemResultComponent.Uz, ["1"]);
        Assert.NotEqual(Loc.S("FemResultNoDisplacements"), layer.SummaryText);
        layer.UpdateSummary(FemResultComponent.Uz, []);
        Assert.Equal(Loc.S("FemResultNoDisplacements"), layer.SummaryText);

        layer.SelectedStepIndex = 0;
        Assert.Equal(1, changed);
        Assert.Equal(-10, layer.Value("1", FemResultComponent.Uz)!.Value, 9);

        layer.DeformScale = 100;
        Assert.Equal(2, changed);
        Assert.Equal(new Point3D(2, 0, -1), layer.NodeMap["2"]);

        layer.ShowDeformed = false;
        Assert.Equal(3, changed);
        Assert.False(layer.DeformedShown);
        Assert.Equal(new Point3D(2, 0, 0), layer.NodeMap["2"]);
    }

    [Fact]
    public void Layer_SuggestedScaleFollowsFragmentUntilSetByHand()
    {
        using var t = new TempDb();
        var layer = new FemResultLayer(new FemAnalysis { Tag = "Р1" }, "Р1", OpenSeesTwoSteps(t), t.Mesh());
        layer.ShowDeformed = true;
        Assert.Equal(5.59, layer.DeformScale, 9);

        // Фрагмент — стержень 1 (узлы 1, 2): диагональ 2 м, перемещение 0,02 м → 0,05·2/0,02 = 5.
        layer.SetScaleScope(["1"]);
        Assert.Equal(5, layer.DeformScale, 9);
        Assert.Equal(new Point3D(2, 0, -0.1), layer.NodeMap["2"]);
        // Пустой фрагмент масштаб не трогает; снятие фрагмента возвращает масштаб по всей сетке.
        layer.SetScaleScope([]);
        Assert.Equal(5, layer.DeformScale, 9);
        layer.SetScaleScope(null);
        Assert.Equal(5.59, layer.DeformScale, 9);

        // Заданный вручную масштаб фрагмент не меняет, сброс — считает по фрагменту.
        layer.DeformScale = 100;
        layer.SetScaleScope(["1"]);
        Assert.Equal(100, layer.DeformScale, 9);
        layer.ResetDeformScaleCommand.Execute(null);
        Assert.Equal(5, layer.DeformScale, 9);
        layer.SetScaleScope(null);
        Assert.Equal(5.59, layer.DeformScale, 9);
    }

    [Fact]
    public void Mosaic_AnalysisSource_ColorsShellsAndBars()
    {
        using var t = new TempDb();
        var analysis = new FemAnalysis { Id = 7, Tag = "Р1", ResultId = 3 };
        var layer = new FemResultLayer(analysis, "Р1", OpenSeesTwoSteps(t), t.Mesh());
        var data = new PlateRebarMosaicVM.Data(null, null, null, null, []) { Analyses = [new(analysis, "Р1 — OpenSees")] };

        var withoutLoader = new PlateRebarMosaicVM();
        withoutLoader.Apply(data);
        Assert.DoesNotContain(withoutLoader.SourceOptions, o => o.Kind == PlateRebarMosaicSourceKind.Analysis);

        int loads = 0;
        var vm = new PlateRebarMosaicVM { ResultLoader = _ => { loads++; return layer; } };
        vm.Apply(data);
        vm.SelectedSource = vm.SourceOptions.Single(o => o.Kind == PlateRebarMosaicSourceKind.Analysis);

        Assert.True(vm.HasSubject);
        Assert.Same(layer, vm.ActiveResult);
        Assert.True(vm.IsResultActive);
        // Перемещения (у OpenSees полей пластин нет); усилия стержней — своим списком «—», N…Mz.
        Assert.Equal(4, vm.ComponentOptions.Count);
        Assert.Equal(1 + 6, vm.BarComponentOptions.Count);
        vm.SelectedComponent = vm.ComponentOptions.Single(o => Equals(o.Component, FemResultComponent.Uz));

        int changed = 0;
        vm.Changed += (_, _) => changed++;
        var coloring = vm.Compute(["2"], ["1"])!;
        Assert.True(coloring.Both);
        Assert.Equal(["1", "2"], coloring.ColorByTag.Keys.Order());
        Assert.NotEmpty(vm.Legend);

        // Шаг слоя — пересчёт мозаики; слой загружается один раз на результат.
        layer.SelectedStepIndex = 0;
        Assert.Equal(1, changed);
        vm.SelectedSource = vm.SourceOptions[0];
        Assert.Null(vm.ActiveResult);
        vm.SelectedSource = vm.SourceOptions.Single(o => o.Kind == PlateRebarMosaicSourceKind.Analysis);
        Assert.Same(layer, vm.ActiveResult);
        Assert.Equal(1, loads);
    }

    /// <summary>Поля шага: пластина 2 (Nx = 5 кН/м, σ/R = 0,8, трещины и текучесть), стержень 1 (N = −10 кН, Mz 2 → −4 кН·м).</summary>
    static RcSecantStepFields FieldsWithForces() => new()
    {
        NodeIds = [1, 2, 3],
        Displacements = new double[18],
        ShellIds = [2],
        ShellForces = [5000, 0, 0, 0, 0, 0, 0, 0],
        ShellStates = [0, 0, 0, 0.8, 0],
        ShellFlags = [RcSecantStepFields.Cracked | RcSecantStepFields.Yielded],
        BeamIds = [1],
        BeamForces = [10000, 0, 0, 0, 0, 2000, -10000, 0, 0, 0, 0, 4000],
        BeamFlags = [0],
    };

    static PlateRebarMosaicVM MosaicOf(FemResultLayer layer)
    {
        var data = new PlateRebarMosaicVM.Data(null, null, null, null, []) { Analyses = [new(layer.Analysis, layer.Label)] };
        var vm = new PlateRebarMosaicVM { ResultLoader = _ => layer };
        vm.Apply(data);
        vm.SelectedSource = vm.SourceOptions.Single(o => o.Kind == PlateRebarMosaicSourceKind.Analysis);
        return vm;
    }

    static void Select(PlateRebarMosaicVM vm, object component) =>
        vm.SelectedComponent = vm.ComponentOptions.Single(o => Equals(o.Component, component));

    static void SelectBar(PlateRebarMosaicVM vm, object? component) =>
        vm.SelectedBarComponent = component == null ? vm.BarComponentOptions[0]
            : vm.BarComponentOptions.Single(o => Equals(o.Component, component));

    static string Extremum(PlateRebarMosaicVM vm, double value, string elem) => string.Format(
        System.Globalization.CultureInfo.CurrentCulture, Loc.S("FemResultElementExtremum"), vm.SelectedComponent!.Label, value, elem);

    [Fact]
    public void Mosaic_CsfeaShellFieldsAndBarForces()
    {
        using var t = new TempDb();
        var summary = new FemCsfeaResultSummary { Steps = [new FemCsfeaStepSummary(1, 0, 1, 1.0, false, true, 3, 1e-6, null, 1, true)] };
        var result = new CalcResult { TaskKind = FemCsfeaRunner.TaskKind, TaskTag = "Р1", DataJson = summary.ToJson() };
        t.Db.SaveCalcResult(result);
        t.Db.SaveFemResultSteps(result.Id, [(1, FemCsfeaStepFieldsCodec.Pack(FieldsWithForces()))]);
        var layer = new FemResultLayer(new FemAnalysis { Id = 1, Tag = "Р1", ResultId = result.Id }, "Р1",
            new CsfeaResultSteps(new FemCsfeaResultVM(result, t.Db, t.Schema)), t.Mesh());
        var vm = MosaicOf(layer);

        // Перемещения — отдельными величинами, не среди полей пластин; ширины раскрытия без постановки N/NL нет.
        var components = vm.ComponentOptions.Select(o => o.Component).ToList();
        Assert.DoesNotContain(FemCsfeaShellField.Ux, components.OfType<FemCsfeaShellField>());
        Assert.DoesNotContain(FemCsfeaShellField.AcrcLongBottom, components.OfType<FemCsfeaShellField>());
        Assert.Contains(FemCsfeaShellField.Nx, components.OfType<FemCsfeaShellField>());
        // Усилия стержней — своим списком: «—» и N…Mz, по умолчанию «—».
        Assert.Empty(components.OfType<FemForceComponent>());
        Assert.True(vm.HasBarComponents);
        Assert.Equal(7, vm.BarComponentOptions.Count);
        Assert.Null(vm.ResultBarForce);
        Assert.True(layer.IsCsfea);

        Select(vm, FemCsfeaShellField.Nx);
        var coloring = vm.Compute(["2"], ["1"])!;
        Assert.False(coloring.Bars);
        Assert.Equal(["2"], coloring.ColorByTag.Keys);
        Assert.Equal(Extremum(vm, 5, "2"), layer.SummaryText);
        Assert.False(vm.HasBarDiagrams);

        Select(vm, FemCsfeaShellField.SigmaRatio);
        vm.Compute(["2"], ["1"]);
        Assert.Equal(Extremum(vm, 0.8, "2"), layer.SummaryText);

        // Состояние — категориями: цвет и подпись текучести, без экстремума.
        Select(vm, FemCsfeaShellField.State);
        coloring = vm.Compute(["2"], ["1"])!;
        Assert.Equal(FemCsfeaResultVM.StateStyles[2].Color, coloring.ColorByTag["2"]);
        Assert.Equal(4, vm.Legend.Count);
        Assert.Equal(1, vm.Legend.Single(l => l.Label == Loc.S("FemCsfeaStateYielded")).Count);
        vm.SetHover("2");
        Assert.EndsWith(Loc.S("FemCsfeaStateYielded"), vm.HoverText);
        Assert.Equal("", layer.SummaryText);

        // Усилие стержня своим списком: пластины красит величина основного списка, на стержнях — эпюра по концам
        // вдоль местной оси расчёта, итог — отдельной строкой.
        Select(vm, FemCsfeaShellField.Nx);
        SelectBar(vm, FemForceComponent.Mz);
        coloring = vm.Compute(["2"], ["1"])!;
        Assert.False(coloring.Bars);
        Assert.Equal(["2"], coloring.ColorByTag.Keys);
        Assert.Equal(Extremum(vm, 5, "2"), layer.SummaryText);
        Assert.Equal(string.Format(System.Globalization.CultureInfo.CurrentCulture, Loc.S("FemResultBarExtremum"),
            vm.SelectedBarComponent!.Label, -4.0, "1"), layer.BarSummaryText);
        Assert.True(vm.HasBarDiagrams);
        Assert.Equal([(0.0, 2.0), (1.0, -4.0)], vm.BarProfiles(["1"], out _)!["1"]);
        vm.SetHover("1");
        Assert.Contains(vm.SelectedBarComponent.Label, vm.HoverText);
        Assert.False(vm.HasBarLegend);
        // Эпюры выключены — стержни окрашены усилием по своей шкале со своей легендой, пластины — величиной основного.
        vm.ShowBarDiagrams = false;
        coloring = vm.Compute(["2"], ["1"])!;
        Assert.True(coloring.Both);
        Assert.Equal(["1", "2"], coloring.ColorByTag.Keys.Order());
        Assert.True(vm.HasBarLegend);
        Assert.Null(vm.BarProfiles(["1"], out _));
        vm.ShowBarDiagrams = true;
        Assert.False(vm.Compute(["2"], ["1"])!.Both);
        Assert.False(vm.HasBarLegend);
        // Стержень вдоль X: Ey — проекция глобальной Z, Ez = X × Ey = −Y.
        Assert.Equal(new Vector3D(0, 0, 1), vm.BarDiagramAxis("1"));
        SelectBar(vm, FemForceComponent.N);
        Assert.Equal(new Vector3D(0, -1, 0), vm.BarDiagramAxis("1"));
        Assert.Equal([(0.0, -10.0), (1.0, -10.0)], vm.BarProfiles(["1"], out _)!["1"]);

        // Перемещения при эпюрах усилий красят только пластины; без эпюр — и стержни.
        Select(vm, FemResultComponent.Uz);
        Assert.False(vm.Compute(["2"], ["1"])!.Both);
        SelectBar(vm, null);
        Assert.True(vm.Compute(["2"], ["1"])!.Both);
        Assert.False(vm.HasBarDiagrams);
        Assert.Equal("", layer.BarSummaryText);
    }

    [Fact]
    public void Layer_OpenSeesBarForcesAndAxes()
    {
        using var t = new TempDb();
        var forces = new FemElementEndForces(1, 10000, 0, 0, 0, 0, 2000, -10000, 0, 0, 0, 0, 4000);
        var result = new FemNonlinearResult
        {
            Status = "ok",
            Steps = [new FemNonlinearStepResult(1, 1.0, true, [], [], [forces])],
            CalcTypeName = "C",
        };
        var steps = new OpenSeesResultSteps(new FemAnalysisResultVM(
            new CalcResult { Status = "ok", DataJson = JsonSerializer.Serialize(result) }, t.Db, t.Schema));
        var layer = new FemResultLayer(new FemAnalysis { Tag = "Р1" }, "Р1", steps, t.Mesh());

        Assert.True(layer.HasBarForces);
        Assert.Empty(layer.ShellFields);
        Assert.False(layer.IsCsfea);
        Assert.Equal((2.0, -4.0), layer.BarEndValues("1", FemForceComponent.Mz));
        Assert.Null(layer.BarEndValues("2", FemForceComponent.Mz));
        Assert.Equal(new Vector3D(0, 0, 1), layer.BarDiagramAxis("1", FemForceComponent.Qy));
        // My — против Ez: эпюра моментов на растянутых волокнах.
        Assert.Equal(new Vector3D(0, 1, 0), layer.BarDiagramAxis("1", FemForceComponent.My));
    }

    [Fact]
    public void BarDiagramGeometry_ExplicitDirection()
    {
        var positive = new List<BarDiagramLine>();
        var negative = new List<BarDiagramLine>();
        BarDiagramGeometry.AddBar(positive, negative, (0, 0, 0), (2, 0, 0), [(0, 1), (1, 1)], BarDiagramPlane.Z1, 0.5, (0, -1, 0));
        Assert.Empty(negative);
        Assert.Equal((0.0, -0.5, 0.0), positive[0].B);
    }

    [Fact]
    public void Mosaic_ShowAnalysis_SelectsResultBeforeAndAfterDataRead()
    {
        using var t = new TempDb();
        var analysis = new FemAnalysis { Id = 7, Tag = "Р1", ResultId = 3 };
        var layer = new FemResultLayer(analysis, "Р1", OpenSeesTwoSteps(t), t.Mesh());
        var data = new PlateRebarMosaicVM.Data(null, null, null, null, [], Key: "k") { Analyses = [new(analysis, "Р1")] };

        // Вход из дерева до чтения данных вида: выбор применяется при чтении.
        var early = new PlateRebarMosaicVM { ResultLoader = _ => layer };
        early.ShowAnalysis(7);
        Assert.Null(early.ActiveResult);
        early.Apply(data);
        Assert.Same(layer, early.ActiveResult);

        // Вид уже открыт на другом источнике: результат выбирается сразу.
        var open = new PlateRebarMosaicVM { ResultLoader = _ => layer, Reader = () => data };
        open.Apply(data);
        Assert.Null(open.ActiveResult);
        open.ShowAnalysis(7);
        Assert.Same(layer, open.ActiveResult);
        Assert.Equal("Р1", open.SelectedSubject!.Label);
    }
}
