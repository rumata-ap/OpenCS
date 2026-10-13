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

        public TempDb(double? shellC1 = null)
        {
            Db = new DatabaseService(_path);
            Db.SaveFemSchema(Schema);
            Db.SaveFemMeshSnapshot(Schema.Id,
                [N("1", 0, 0), N("2", 2, 0), N("3", 0, 1)],
                [
                    new FemElement { ElemTag = "1", NodeIdsJson = "[1,2]" },
                    new FemElement { ElemTag = "2", ElemType = "shell", NodeIdsJson = "[1,2,3]", FoundationC1 = shellC1 },
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

        // Контур исходной сетки под деформированной — по умолчанию показан, отключается отдельно.
        Assert.True(layer.ShowOriginal);
        layer.ShowOriginal = false;
        layer.ShowOriginal = true;
        changed -= 2;

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

    /// <summary>Поля шага с реакциями: узел 1 — связь (Rz = 30 кН, My = −12 кН·м), узел 3 — пружина (Rx = −2 кН).</summary>
    static RcSecantStepFields FieldsWithReactions() => new()
    {
        NodeIds = [1, 2, 3], Displacements = new double[18],
        ShellIds = [], ShellForces = [], ShellStates = [], ShellFlags = [], BeamIds = [], BeamForces = [], BeamFlags = [],
        ReactionNodeIds = [1, 3], Reactions = [0, 0, 30e3, 0, -12e3, 0, -2e3, 0, 0, 0, 0, 0],
        ReactionKinds = [RcReactionSet.Support, RcReactionSet.Spring],
        ReactionSums = [0, 0, 30e3, -2e3, 0, 0, 0, 0, 0, 0, 0, 0], LoadSum = [2e3, 0, -30e3],
    };

    [Fact]
    public void Csfea_FoundationPressureField()
    {
        using var t = new TempDb(shellC1: 1e7);
        var fields = FieldsWithReactions();
        var disp = new double[18];
        disp[2] = disp[8] = disp[14] = -0.01;   // осадка всех узлов пластины 2 на 10 мм
        var layer = ReactionLayer(t, new RcSecantStepFields
        {
            NodeIds = fields.NodeIds, Displacements = disp, ShellIds = [2], ShellForces = new double[8],
            ShellStates = new double[5], ShellFlags = [0], BeamIds = [], BeamForces = [], BeamFlags = [],
        });
        Assert.Contains(layer.ShellFields, f => f.Value == FemCsfeaShellField.FoundationPressure);
        // p = −C1·(n·u) = 1e7 Н/м³ · 0,01 м = 100 кПа (сжатие основания — плюс).
        Assert.Equal(100, layer.ShellValue("2", FemCsfeaShellField.FoundationPressure)!.Value, 9);

        using var plain = new TempDb();
        Assert.DoesNotContain(ReactionLayer(plain, FieldsWithReactions()).ShellFields, f => f.Value == FemCsfeaShellField.FoundationPressure);
    }

    [Fact]
    public void Csfea_ReactionLabelsFollowFlag()
    {
        using var t = new TempDb();
        var layer = ReactionLayer(t, FieldsWithReactions());
        layer.ShowReactions = true;
        var (arrows, labels) = layer.ReactionGlyphs(layer.NodeMap);
        Assert.NotNull(arrows);
        // По убыванию силы: узел 1 (30 кН), узел 3 (2 кН); подпись — у хвоста стрелки.
        Assert.Equal([30.0, 2.0], labels.Select(l => Math.Round(l.ValueKn, 9)));
        Assert.Equal(arrows![0], labels[0].Position);
        int changed = 0;
        layer.Changed += () => changed++;
        layer.ShowReactionValues = false;
        Assert.Equal(1, changed);
        Assert.Empty(layer.ReactionGlyphs(layer.NodeMap).Labels);
        Assert.NotNull(layer.ReactionGlyphs(layer.NodeMap).Arrows);
    }

    static FemResultLayer ReactionLayer(TempDb t, RcSecantStepFields fields)
    {
        var summary = new FemCsfeaResultSummary
        {
            Steps = [new FemCsfeaStepSummary(1, 0, 1, 1.0, false, true, 3, 1e-6, null, 1, true, fields.ReactionSums, fields.LoadSum)],
        };
        var result = new CalcResult { TaskKind = FemCsfeaRunner.TaskKind, TaskTag = "Р1", DataJson = summary.ToJson() };
        t.Db.SaveCalcResult(result);
        t.Db.SaveFemResultSteps(result.Id, [(1, FemCsfeaStepFieldsCodec.Pack(fields))]);
        return new FemResultLayer(new FemAnalysis { Id = 1, Tag = "Р1", ResultId = result.Id }, "Р1",
            new CsfeaResultSteps(new FemCsfeaResultVM(result, t.Db, t.Schema)), t.Mesh());
    }

    [Fact]
    public void Csfea_ReactionRowsCardAndSummary()
    {
        using var t = new TempDb();
        var layer = ReactionLayer(t, FieldsWithReactions());
        var vm = layer.Csfea!;
        Assert.True(vm.HasReactions);
        Assert.False(vm.ShowNoReactionsNote);
        Assert.Equal([1, 3], vm.ReactionRows.Select(r => r.Node));
        var r1 = vm.ReactionRows[0];
        Assert.Equal(30, r1.Rz, 12);
        Assert.Equal(-12, r1.My, 12);
        Assert.Equal(Loc.S("FemCsfeaReactionSupport"), r1.Kind);
        Assert.Equal(Loc.S("FemCsfeaReactionSpring"), vm.ReactionRows[1].Kind);
        Assert.Equal("30", vm.StepRows[0].ReactionZ);

        // Итог: ΣR по типам (только ненулевые), ΣF и небаланс 0.
        var c = System.Globalization.CultureInfo.CurrentCulture;
        string text = vm.ReactionSummaryText;
        string kn = Loc.S("UnitKN"), minus2 = (-2).ToString(c), minus30 = (-30).ToString(c);
        Assert.StartsWith($"ΣR = ({minus2}; 0; 30) {kn}: ", text);
        Assert.Contains(Loc.S("FemCsfeaReactionSupport") + " (0; 0; 30)", text);
        Assert.Contains(Loc.S("FemCsfeaReactionSpring") + $" ({minus2}; 0; 0)", text);
        Assert.DoesNotContain(Loc.S("FemCsfeaReactionAuto"), text);
        Assert.EndsWith($"; ΣF = (2; 0; {minus30}) {kn}, |ΣR + ΣF| = 0 {kn}", text);
        Assert.True(layer.HasReactions);
        Assert.Equal(text, layer.ReactionSummaryText);

        // Карточка узла: опоры и ненулевые компоненты реакции, кН и кН·м.
        layer.Selection.PickNode(1);
        var rows = layer.Card!.Rows;
        Assert.Contains(rows, r => r.Label == Loc.S("FemResultCardSupports") && r.Value == Loc.S("FemCsfeaReactionSupport"));
        Assert.Contains(rows, r => r.Label == $"Rz, {kn}" && r.Value == 30.ToString(c));
        Assert.Contains(rows, r => r.Label == $"My, {Loc.S("UnitKNm")}" && r.Value == (-12).ToString(c));
        Assert.DoesNotContain(rows, r => r.Label.StartsWith("Rx", StringComparison.Ordinal));
        layer.Selection.PickNode(2);
        Assert.DoesNotContain(layer.Card!.Rows, r => r.Label == Loc.S("FemResultCardSupports"));
    }

    [Fact]
    public void Csfea_ReactionArrowsFollowFlagAndScale()
    {
        using var t = new TempDb();
        var layer = ReactionLayer(t, FieldsWithReactions());
        Assert.Null(layer.ReactionArrows(layer.NodeMap));   // по умолчанию выключены
        int changed = 0;
        layer.Changed += () => changed++;
        layer.ShowReactions = true;
        Assert.Equal(1, changed);
        var pts = layer.ReactionArrows(layer.NodeMap)!;
        // Две стрелки по три отрезка; остриё — в узле, стрелка наибольшей силы (узел 1, +z) — 8 % диагонали.
        Assert.Equal(12, pts.Count);
        double size = layer.Mesh.Diagonal * FemResultLayer.ReactionArrowRatio;
        static void Near(Point3D a, Point3D b) => Assert.True((a - b).Length < 1e-12, $"{a} ≠ {b}");
        Near(layer.NodeMap["1"], pts[1]);
        Near(layer.NodeMap["1"] - new Vector3D(0, 0, size), pts[0]);
        // Пружина узла 3: сила −x, в 15 раз меньше.
        Near(layer.NodeMap["3"], pts[7]);
        Near(layer.NodeMap["3"] + new Vector3D(size * 2 / 30, 0, 0), pts[6]);
        layer.ReactionScale = 2;
        Assert.Equal(2, changed);
        Near(layer.NodeMap["1"] - new Vector3D(0, 0, 2 * size), layer.ReactionArrows(layer.NodeMap)![0]);
        // Только показанные узлы.
        Assert.Equal(6, layer.ReactionArrows(layer.NodeMap, n => n == 3)!.Count);
    }

    [Fact]
    public void Csfea_OldRecordWithoutReactions_ShowsNote()
    {
        using var t = new TempDb();
        var layer = ReactionLayer(t, FieldsWithForces());
        Assert.False(layer.HasReactions);
        Assert.True(layer.Csfea!.ShowNoReactionsNote);
        Assert.Empty(layer.Csfea.ReactionRows);
        Assert.Equal("", layer.ReactionSummaryText);
        layer.ShowReactions = true;
        Assert.Null(layer.ReactionArrows(layer.NodeMap));
    }

    [Fact]
    public void FollowChord_RotatesWithBar()
    {
        // Хорда X → поворот на 30° в плоскости XZ: ось Z стержня поворачивается так же, ось Y — без изменений.
        double a = Math.PI / 6;
        var to = new Vector3D(Math.Cos(a), 0, Math.Sin(a));
        var ez = FemResultLayer.FollowChord(new Vector3D(0, 0, 1), new Vector3D(2, 0, 0), to * 3);
        Assert.True((ez - new Vector3D(-Math.Sin(a), 0, Math.Cos(a))).Length < 1e-12, ez.ToString());
        Assert.Equal(new Vector3D(0, 1, 0), FemResultLayer.FollowChord(new Vector3D(0, 1, 0), new Vector3D(1, 0, 0), to));
        Assert.Equal(new Vector3D(0, 0, 1), FemResultLayer.FollowChord(new Vector3D(0, 0, 1), new Vector3D(1, 0, 0), new Vector3D(1, 0, 0)));
    }

    [Fact]
    public void Csfea_DiagramAxisFollowsDeformedBar()
    {
        using var t = new TempDb();
        var fields = FieldsWithForces();
        var disp = new double[18];
        disp[6 + 2] = -0.01;   // узел 2 (конец стержня 1) опустился
        var layer = ReactionLayer(t, new RcSecantStepFields
        {
            NodeIds = fields.NodeIds, Displacements = disp, ShellIds = fields.ShellIds, ShellForces = fields.ShellForces,
            ShellStates = fields.ShellStates, ShellFlags = fields.ShellFlags, BeamIds = fields.BeamIds,
            BeamForces = fields.BeamForces, BeamFlags = fields.BeamFlags,
        });
        FemForceComponent[] comps = [FemForceComponent.My, FemForceComponent.Mz];
        var undeformed = comps.Select(c => layer.BarDiagramAxis("1", c)!.Value).ToArray();
        layer.ShowDeformed = true;
        layer.DeformScale = 100;   // показанный конец опущен на 1 м при длине 2 м
        var chord = layer.NodeMap["2"] - layer.NodeMap["1"];
        var deformed = comps.Select(c => layer.BarDiagramAxis("1", c)!.Value).ToArray();
        foreach (var d in deformed)
        {
            Assert.True(Math.Abs(Vector3D.DotProduct(d, chord)) < 1e-12 * chord.Length, $"{d} не ⊥ {chord}");
            Assert.Equal(1, d.Length, 12);
        }
        // Ось в плоскости изгиба (вертикальная у исходного стержня) повернулась вместе с ним.
        Assert.Contains(undeformed, u => Math.Abs(Vector3D.DotProduct(u, chord)) > 0.1 * chord.Length);
        layer.ShowDeformed = false;
        Assert.Equal(undeformed, comps.Select(c => layer.BarDiagramAxis("1", c)!.Value));
    }
}
