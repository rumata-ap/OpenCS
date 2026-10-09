using CScore;
using CScore.Fem;
using CScore.Fem.Import;
using CScore.Import;
using CSfea.CScoreBridge.Structural;
using Microsoft.Data.Sqlite;
using OpenCS.OpenSees.CScore;
using OpenCS.Services;
using OpenCS.Services.Scad;
using OpenCS.Tasks;
using OpenCS.Utilites;
using Xunit;
using Xunit.Abstractions;

namespace OpenCS.Tests;

/// <summary>
/// Тестовый проект для ручной проверки секущего расчёта CSfea схемы SCAD (4д): плита Дорфмана из .SPR, импорт — как
/// команда «Импорт SCAD (API)» (сетка, группы, ЖБ-группы, заданное армирование, перенос нагрузок и ГУ), материалы опыта
/// (<see cref="ScadShellMaterialMode.Experiment"/>), шаблоны сечений пластин на ЖБ-группах (армирование — «Заданное»
/// SCAD), постановка «L1, L2 × 5» с контрольным узлом 510. Запуск:
/// <c>OPENCS_DORFMAN_DEMO_DB=путь\к\файлу.db dotnet test OpenCS.Tests -p:AllowUnsafeBlocks=true
/// --filter FullyQualifiedName~FemCsfeaDorfmanDemoDbManualTests</c>; OPENCS_SCAD_NL_SPR — модель (по умолчанию
/// Downloads\Telegram Desktop\Перекрытие_Дорфмана_нелин.SPR), OPENCS_SCAD_DIR — SCADAPIX. Без переменной тест выходит.
/// </summary>
public sealed class FemCsfeaDorfmanDemoDbManualTests(ITestOutputHelper output)
{
    [Fact]
    public void CreateDorfmanDatabase()
    {
        var path = Environment.GetEnvironmentVariable("OPENCS_DORFMAN_DEMO_DB");
        if (string.IsNullOrWhiteSpace(path)) return;
        string spr = Environment.GetEnvironmentVariable("OPENCS_SCAD_NL_SPR") is { Length: > 0 } s ? s
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads", "Telegram Desktop",
                "Перекрытие_Дорфмана_нелин.SPR");
        string dir = Environment.GetEnvironmentVariable("OPENCS_SCAD_DIR") ?? ScadInstallLocator.FindDllDirectory()!;
        if (File.Exists(path)) File.Delete(path);

        ScadSchemaData data;
        using (var session = new ScadApiSession(ScadApiNative.Load(dir)))
        {
            session.Open(spr);
            data = ScadApiReader.Read(session, new ScadReadOptions(OutputAxes: true, ConcreteGroups: true), null,
                CancellationToken.None).Data;
        }

        try
        {
            using var db = new DatabaseService(path);
            var schema = new FemSchema { Tag = "Перекрытие Дорфмана (опыт)", SourceType = "scad", SourcePath = Path.GetFullPath(spr) };
            db.SaveFemSchema(schema);
            Import(db, schema, data);
            db.LoadAll();
            schema = db.FemSchemas.Single(x => x.Id == schema.Id);

            AssignPlateSections(db, schema, data);
            AddAnalysis(db, schema);
            output.WriteLine($"База: {path}");
        }
        finally { SqliteConnection.ClearAllPools(); }
    }

    /// <summary>
    /// Проверка входа и расчёт постановок копии базы (OPENCS_DORFMAN_DEMO_CHECK — путь к базе) — отчёт и сводка в вывод.
    /// </summary>
    [Fact]
    public async Task CheckAndRunDatabase()
    {
        var source = Environment.GetEnvironmentVariable("OPENCS_DORFMAN_DEMO_CHECK");
        if (string.IsNullOrWhiteSpace(source)) return;
        var root = Path.Combine(Path.GetTempPath(), $"opencs-dorfman-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        var path = Path.Combine(root, "dorfman.db");
        File.Copy(source, path);
        try
        {
            using var db = new DatabaseService(path);
            db.LoadAll();
            var log = new OutputLog(output);
            var gmsh = new GmshSettings { ExecutablePath = @"C:\Tools\gmsh-4.15.2-Windows64\gmsh.exe", ArtifactsPath = Path.Combine(root, "gmsh") };
            var ctx = new FemCsfeaRunContext(db, gmsh, log);
            foreach (var schema in db.FemSchemas.ToList())
                foreach (var analysis in schema.Analyses.Where(a => a.Kind == FemCsfeaRunner.AnalysisKind).ToList())
                {
                    var prepared = await FemCsfeaRunner.PrepareAsync(ctx, schema, analysis, buildMesh: false, CancellationToken.None);
                    output.WriteLine($"«{analysis.Tag}» — проверка входа: ошибки {prepared.HasErrors}");
                    foreach (var line in prepared.Describe().Concat(prepared.Report)) output.WriteLine("  " + line);
                    var clock = System.Diagnostics.Stopwatch.StartNew();
                    var result = await FemCsfeaRunner.RunAsync(ctx, schema, analysis, null, CancellationToken.None);
                    output.WriteLine($"«{analysis.Tag}» — расчёт: {result.Status}, {clock.Elapsed.TotalSeconds:0} с");
                    var summary = FemCsfeaResultSummary.Parse(result.DataJson)!;
                    foreach (var line in summary.Describe()) output.WriteLine("  " + line);
                    foreach (var st in summary.Steps)
                        output.WriteLine($"    шаг {st.N}: стадия {st.Stage}, λ = {st.LoadFactor:0.###}, сошёлся {st.Converged}, " +
                            $"итераций {st.Iterations}, uz = {st.Control * 1e3:0.###} мм");
                }
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            try { Directory.Delete(root, true); } catch (IOException) { }
        }
    }

    /// <summary>Импорт как <c>AppViewModel.ImportScadSchemaFromApi</c> с группами КЭ по ЖБ-группам.</summary>
    void Import(DatabaseService db, FemSchema schema, ScadSchemaData data)
    {
        var meshNodes = ScadSchemaConverter.ToFemMeshNodes(data, schema.Id);
        var meshElements = ScadSchemaConverter.ToFemMeshElements(data, schema.Id);
        var groups = ScadSchemaConverter.ToFemMemberGroups(data, schema.Id)
            .Concat(ScadSchemaConverter.ToFemMemberGroupsByBlocks(data, schema.Id))
            .Concat(ScadSchemaConverter.ToFemMemberGroupsByConcreteGroups(data, schema.Id)).ToArray();
        var import = FemImportResult.MeshOnly(meshNodes, meshElements, groups);
        import.PruneMissingGroupTags();
        foreach (var w in db.SaveFemImport(schema.Id, import)) output.WriteLine("импорт: " + w.Message);
        db.SaveFemSchemaStiffnesses(schema.Id, ScadSchemaConverter.ToSchemaStiffnesses(data));
        db.SaveFemSchemaSourceFile(schema.Id, FemSchemaSourceFileKind.ScadConcreteGroups, "",
            System.Text.Encoding.UTF8.GetBytes(ScadConcreteGroupIndex.ToJson(data.ConcreteGroups)));
        if (data.AssignedRebar != null)
            db.SaveFemSchemaSourceFile(schema.Id, FemSchemaSourceFileKind.ScadAssignedRebar, "",
                System.Text.Encoding.UTF8.GetBytes(data.AssignedRebar.ToJson()));
        var model = data.AnalysisModel ?? throw new InvalidOperationException("В .SPR нет расчётной модели.");
        db.SaveFemSchemaSourceFile(schema.Id, FemSchemaSourceFileKind.ScadAnalysisModel, "",
            System.Text.Encoding.UTF8.GetBytes(model.ToJson()));

        var types = meshElements.GroupBy(e => e.ElemTag, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.First().ElemType, StringComparer.Ordinal);
        int next = 0;
        var loads = ScadLoadTransfer.Transfer(model, types, [], [], [], () => --next);
        db.SaveFemLoadCasesAndMeshLoads(schema.Id, loads.LoadCases, loads.ElementLoads, loads.MeshNodeLoads);
        foreach (var line in loads.Report) output.WriteLine("нагрузки: " + line);
        var boundary = ScadBoundaryTransfer.Transfer(model, meshNodes.Select(n => n.NodeTag).ToHashSet(StringComparer.Ordinal), types);
        db.SaveFemBoundary(schema.Id, ScadBoundaryTransfer.Origin, boundary.Supports, boundary.Springs, boundary.RigidBodies,
            boundary.ElementProps);
        foreach (var line in boundary.Report) output.WriteLine("ГУ: " + line);
    }

    /// <summary>
    /// Материалы опыта и шаблон сечения пластины на каждую ЖБ-группу с пластинами — как <see cref="ScadShellScenario"/>:
    /// бетон опыта, арматура пластин A400 → A300 (A-II), толщина по жёсткости. Слои арматуры даёт «Заданное» SCAD.
    /// </summary>
    void AssignPlateSections(DatabaseService db, FemSchema schema, ScadSchemaData data)
    {
        var materials = ScadShellScenario.Materials(new ScadShellScenarioOptions(false, ScadShellMaterialMode.Experiment, [],
            CatalogDirectory()));
        var byScenarioId = new Dictionary<int, Material>();
        foreach (var m in materials)
        {
            byScenarioId[m.Id] = m;
            m.Id = 0;
            db.AddMaterial(m);
        }
        var concrete = byScenarioId[1];
        Material PlateRebar(string? cls) => MaterialCatalog.ClassKey(cls ?? "A400") switch
        {
            "A240" => byScenarioId[2],
            _ => byScenarioId[3],
        };

        var stiff = data.Stiffnesses.ToDictionary(x => x.Id);
        var shells = db.GetFemMeshElements(schema.Id).Where(e => e.ElemType == "shell")
            .ToDictionary(e => e.ElemTag, StringComparer.Ordinal);
        var covered = new HashSet<string>(StringComparer.Ordinal);
        foreach (var g in data.ConcreteGroups)
        {
            string tag = $"ЖБ: {(string.IsNullOrWhiteSpace(g.Name) ? g.Num.ToString() : g.Name.Trim())}";
            var group = schema.MemberGroups.FirstOrDefault(x => x.Tag == tag);
            var plateTags = group?.Tags.Where(shells.ContainsKey).ToList() ?? [];
            if (group == null || plateTags.Count == 0) continue;
            double h = shells[plateTags[0]].ThicknessM ?? stiff.GetValueOrDefault(shells[plateTags[0]].StiffnessNum ?? 0)?.ThicknessM ?? 0.2;
            var section = new PlateSection
            {
                Num = db.PlateSections.Count + 1, Tag = $"Опыт — {tag}", H = h, NLayers = 20,
                ConcreteMaterialId = concrete.Id, RebarMaterialId = PlateRebar(g.LongitudinalRebarClass).Id, TensionConcrete = true,
            };
            db.SavePlateSection(section);
            group.PlateSectionId = section.Id;
            db.SaveFemMemberGroup(group);
            covered.UnionWith(plateTags);
            output.WriteLine($"{tag}: пластин {plateTags.Count}, h = {h:0.###} м, сечение «{section.Tag}»");
        }
        int rest = shells.Keys.Count(t => !covered.Contains(t));
        output.WriteLine($"Пластин вне ЖБ-групп: {rest} — считаются упруго.");
    }

    /// <summary>Постановка «L1, L2 × 5»: L1 одним шагом, L2 за 5 шагов (шаг 0,2); контрольный узел 510 (центр), uz.</summary>
    static void AddAnalysis(DatabaseService db, FemSchema schema)
    {
        var cases = db.GetFemLoadCases(schema.Id);
        string Expr(int num) => new FemLoadExpression
        {
            Mode = FemLoadExpressionMode.Single, LoadCaseIds = [cases.Single(c => c.SourceLoadNum == num).Id],
        }.ToJson();
        var pars = new FemAnalysisParams
        {
            CalcType = CalcType.N,
            Stages =
            [
                new FemAnalysisStage { Tag = "L1", LoadExpressionJson = Expr(1), LoadFactorStep = 1, MaxLoadFactor = 1 },
                new FemAnalysisStage { Tag = "L2 × 5", LoadExpressionJson = Expr(2), LoadFactorStep = 0.2, MaxLoadFactor = 1 },
            ],
            Csfea = new FemCsfeaParams
            {
                PlateRebarSource = FemCheckRebarSource.Assigned, PlateCrackRule = PlateCrackRule.Layer, Psi = true,
                ControlNodeTag = "510", ControlDof = 2,
            },
        };
        db.SaveFemAnalysis(new FemAnalysis
        {
            SchemaId = schema.Id, Tag = "CSfea: L1, L2 × 5 (опыт)", Kind = FemCsfeaRunner.AnalysisKind,
            LoadExpressionJson = pars.Stages[0].LoadExpressionJson, ParamsJson = pars.ToJson(),
        });
    }

    /// <summary>Журнал исполнителя — в вывод теста.</summary>
    sealed class OutputLog(ITestOutputHelper output) : ILogService
    {
        public System.Collections.ObjectModel.ObservableCollection<LogEntry> LogEntries { get; } = [];
        public void Info(string message) => output.WriteLine("журнал: " + message);
        public void Warning(string message) => output.WriteLine("журнал (!): " + message);
        public void Error(string message) => output.WriteLine("журнал (ошибка): " + message);
    }

    static string CatalogDirectory()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "OpenCS.sln"))) dir = dir.Parent;
        return Path.Combine(dir!.FullName, "OpenCS", "DataSource");
    }
}
