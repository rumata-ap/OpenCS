using System.Diagnostics;
using System.Globalization;
using CScore.Fem;
using CScore.Import;
using CSfea.CScoreBridge.Structural;
using OpenCS.OpenSees.CScore;
using OpenCS.Services;
using OpenCS.Services.Scad;
using OpenCS.Tasks;
using OpenCS.Utilites;
using Xunit;
using Xunit.Abstractions;

namespace OpenCS.Tests;

/// <summary>
/// Ручная сверка адаптера схемы FEM → CSfea (срез 4в) с адаптером сырых данных SCAD. OPENCS_SCAD_NL_SPR — плита
/// Дорфмана (.SPR); OPENCS_SCAD_DIR — каталог SCADAPIX (по умолчанию — найденная установка). Без переменной тест
/// сразу выходит.
/// </summary>
public class FemRcAdapterManualTests(ITestOutputHelper output)
{
    /// <summary>Узел центра плиты Дорфмана; линейный SCAD: −3,868 мм от L1, −6,873 мм от L2.</summary>
    const int CenterNode = 510;

    [Fact]
    public void DorfmanFromSpr()
    {
        string? spr = Environment.GetEnvironmentVariable("OPENCS_SCAD_NL_SPR");
        if (string.IsNullOrWhiteSpace(spr)) return;
        string dir = Environment.GetEnvironmentVariable("OPENCS_SCAD_DIR") ?? ScadInstallLocator.FindDllDirectory()!;
        ScadSchemaData data;
        using (var session = new ScadApiSession(ScadApiNative.Load(dir)))
        {
            session.Open(spr);
            data = ScadApiReader.Read(session, new ScadReadOptions(), null, CancellationToken.None).Data;
        }

        // Эталон — адаптер сырых данных SCAD.
        var (e, nu) = ScadShellScenario.ElasticPlate(data);
        var scenario = ScadShellScenario.Build(data, new ScadShellScenarioOptions(true, ScadShellMaterialMode.Experiment,
            [new ScadShellStage("L1", [(1, 1.0)], 1.0), new ScadShellStage("L2", [(2, 1.0)], 1.0)]), []);
        var reference = ScadRcModelAdapter.Adapt(new ScadRcModelInput
        {
            Data = data, PlateSection = scenario.PlateSection, ShellSection = ScadRcModelAdapter.ElasticShells(e, nu),
            Stages = scenario.Stages,
        });

        // Схема FEM в памяти: сетка, перенос ГУ и нагрузок, свойства по жёсткостям.
        var sw = Stopwatch.StartNew();
        var input = ScadSchemaInMemory(data, [("L1", 1), ("L2", 2)]);
        var adapted = FemRcModelAdapter.Adapt(input);
        output.WriteLine($"адаптер схемы FEM: {sw.ElapsedMilliseconds} мс");
        foreach (var line in adapted.Report) output.WriteLine("  " + line);
        Assert.False(adapted.HasErrors);

        var (m, r) = (adapted.Model, reference.Model);
        output.WriteLine($"узлы {m.Nodes.Count}/{r.Nodes.Count}, пластины {m.Shells.Count}/{r.Shells.Count}, " +
            $"стержни {m.Beams.Count}/{r.Beams.Count}, опоры {m.Supports.Count}/{r.Supports.Count}, " +
            $"пружины {m.Springs.Count}/{r.Springs.Count}, тела {m.RigidBodies.Count}/{r.RigidBodies.Count} (FEM / SCAD)");
        Assert.Equal(r.Shells.Count, m.Shells.Count);
        Assert.Equal(r.Beams.Count, m.Beams.Count);
        Assert.Equal(r.RigidBodies.Count, m.RigidBodies.Count);
        Assert.Equal(r.Supports.OrderBy(s => s.NodeId), m.Supports.OrderBy(s => s.NodeId));

        var refShells = r.Shells.ToDictionary(s => s.Id);
        double axisDiff = m.Shells.Max(s => Enumerable.Range(0, 3).Max(c => Math.Abs(s.SectionAxisX![c] - refShells[s.Id].SectionAxisX![c])));
        int contourDiff = m.Shells.Count(s => !s.NodeIds.SequenceEqual(refShells[s.Id].NodeIds));
        output.WriteLine($"оси x пластин: max|Δ| = {axisDiff:e2}; контуров с отличием {contourDiff}");
        Assert.True(axisDiff < 1e-9);
        Assert.Equal(0, contourDiff);

        for (int k = 0; k < 2; k++)
        {
            double down = -adapted.StageTotals[k].Fz;
            output.WriteLine($"{adapted.StageTotals[k].Tag}: ΣFz вниз {down / 9810:0.###} т (SCAD-адаптер {reference.StageTotalDownN[k] / 9810:0.###} т)");
            Assert.InRange(down / reference.StageTotalDownN[k], 0.9999, 1.0001);
        }

        var build = RcStructuralMeshBuilder.Build(m, new LinearRcSectionFactory());
        double[] expected = [-3.868, -6.873];
        for (int k = 0; k < 2; k++)
        {
            var u = build.Mesh.SolveLinear(build.Combination(m.Stages[k].Loads), build.Bc);
            double w = u[build.Dof(CenterNode, 2)] * 1000;
            output.WriteLine($"{m.Stages[k].Name}: прогиб центра {w:0.###} мм (SCAD {expected[k]} мм, {(w / expected[k] - 1) * 100:+0.00;-0.00} %)");
            Assert.InRange(w / expected[k], 0.99, 1.01);
        }
    }

    /// <summary>
    /// Схема FEM из БД (срез 4в-5, проверки 2–3): копия OPENCS_FEMRC_DB во временной папке → <see cref="FemCsfeaInputBuilder"/>
    /// → <see cref="FemRcModelAdapter"/>; стадия на каждое загружение с коэффициентом 1. OPENCS_FEMRC_SCHEMA — Id схемы
    /// (по умолчанию первая импортированная); OPENCS_FEMRC_SELFWEIGHT=1 — добавить в копию загружение «с. в.»
    /// (коэффициент 1, для схем без нагрузок); OPENCS_FEMRC_SPR — .SPR той же схемы для сверки с
    /// <see cref="ScadRcModelAdapter"/> (упругие пластины по толщине жёсткости); OPENCS_FEMRC_SOLVE=1 — линейный расчёт
    /// стадий и баланс ΣRz (закрепления + пружины + C1) = −ΣFz; OPENCS_FEMRC_PIN_PLAN=1 — закрепить в копии схему на одном
    /// C1 от смещения в плане (<see cref="PinInPlan"/>). Без OPENCS_FEMRC_DB тест сразу выходит.
    /// </summary>
    [Fact]
    public void FromDatabase()
    {
        string? source = Environment.GetEnvironmentVariable("OPENCS_FEMRC_DB");
        if (string.IsNullOrWhiteSpace(source)) return;
        var sw = Stopwatch.StartNew();
        void Log(string text) => output.WriteLine(string.Create(CultureInfo.InvariantCulture,
            $"[{sw.Elapsed.TotalSeconds,7:0.0} с, {GC.GetTotalMemory(false) / 1048576.0,6:0} МБ] {text}"));

        string path = Path.Combine(Path.GetTempPath(), "opencs_femrc_" + Guid.NewGuid().ToString("N") + ".db");
        File.Copy(source, path);
        try
        {
            FemRcModelInput input;
            using (var db = new DatabaseService(path))
            {
                db.LoadAll();
                var schema = Environment.GetEnvironmentVariable("OPENCS_FEMRC_SCHEMA") is { Length: > 0 } sid
                    ? db.FemSchemas.Single(x => x.Id == int.Parse(sid, CultureInfo.InvariantCulture))
                    : db.FemSchemas.First(x => x.SourceType is "scad" or "lira");
                if (Environment.GetEnvironmentVariable("OPENCS_FEMRC_SELFWEIGHT") == "1")
                    db.SaveFemLoadCase(new FemLoadCase { SchemaId = schema.Id, Tag = "с. в. (проверка)", SelfWeightFactor = 1 });
                if (Environment.GetEnvironmentVariable("OPENCS_FEMRC_PIN_PLAN") == "1") PinInPlan(db, schema.Id, Log);
                Log($"БД открыта: схема {schema.Id} «{schema.Tag}» ({schema.SourceType})");

                var stages = db.GetFemLoadCases(schema.Id).Select(c => new FemAnalysisStage
                {
                    Tag = c.Tag, LoadFactorStep = 1, MaxLoadFactor = 1,
                    LoadExpressionJson = new FemLoadExpression { Mode = FemLoadExpressionMode.Single, LoadCaseIds = [c.Id] }.ToJson(),
                }).ToList();
                input = FemCsfeaInputBuilder.Build(db, schema.Id, new FemCsfeaSetup { Stages = stages });
            }
            Log($"Вход собран: узлов {input.MeshNodes.Count}, КЭ {input.MeshElements.Count}, опор {input.Supports.Count}, " +
                $"пружин {input.Springs.Count}, тел {input.RigidBodies.Count}, загружений {input.LoadCases.Count}");

            var adapted = FemRcModelAdapter.Adapt(input);
            var m = adapted.Model;
            Log($"Модель: узлов {m.Nodes.Count}, пластин {m.Shells.Count}, стержней {m.Beams.Count}, опор {m.Supports.Count}, " +
                $"пружин {m.Springs.Count}, тел {m.RigidBodies.Count}, на C1 {m.Shells.Count(x => x.FoundationC1 is > 0)}");
            foreach (var line in adapted.Report.Take(40)) output.WriteLine("  " + line);
            foreach (var t in adapted.StageTotals)
                output.WriteLine(string.Create(CultureInfo.InvariantCulture,
                    $"  «{t.Tag}»: ΣF = ({t.Fx / 1e3:0.###}; {t.Fy / 1e3:0.###}; {t.Fz / 1e3:0.###}) кН"));

            if (Environment.GetEnvironmentVariable("OPENCS_FEMRC_SPR") is { Length: > 0 } spr)
                CompareWithScad(spr, adapted, input, Log);

            Assert.False(adapted.HasErrors);
            if (Environment.GetEnvironmentVariable("OPENCS_FEMRC_SOLVE") == "1") SolveAndBalance(adapted, Log);
            output.WriteLine(string.Create(CultureInfo.InvariantCulture,
                $"Пик рабочего набора процесса {Process.GetCurrentProcess().PeakWorkingSet64 / 1048576.0:0} МБ"));
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            try { File.Delete(path); } catch (IOException) { }
        }
    }

    /// <summary>
    /// Схема только на C1 (ГУ ЛИРЫ не импортированы) смещается в плане: два узла пластин на основании закрепляются
    /// в копии БД — крайний по −X от X и Y, крайний по +X от Y (сдвиг и поворот вокруг Z); вертикальный баланс не меняется.
    /// </summary>
    static void PinInPlan(DatabaseService db, int schemaId, Action<string> log)
    {
        var nodes = db.GetFemMeshNodes(schemaId).ToDictionary(n => n.NodeTag);
        var tags = db.GetFemMeshElements(schemaId).Where(e => e.FoundationC1 is > 0)
            .SelectMany(e => FemMeshTopology.ReadNodeTags(e) ?? []).Distinct().Select(t => nodes[t]).ToList();
        if (tags.Count == 0) { log("Закрепление в плане: нет пластин на C1"); return; }
        var a = tags.MinBy(n => n.X)!;
        var b = tags.MaxBy(n => n.X)!;
        db.SetFemMeshNodeBoundary(schemaId, a.NodeTag, 0b011, new double[6]);
        db.SetFemMeshNodeBoundary(schemaId, b.NodeTag, 0b010, new double[6]);
        log($"Закрепление в плане (только копия БД): узел {a.NodeTag} — X, Y; узел {b.NodeTag} — Y");
    }

    /// <summary>Сверка с адаптером сырых данных SCAD: состав модели и ΣFz загружений (по номеру загружения SCAD).</summary>
    void CompareWithScad(string spr, FemRcModelResult adapted, FemRcModelInput input, Action<string> log)
    {
        string dir = Environment.GetEnvironmentVariable("OPENCS_SCAD_DIR") ?? ScadInstallLocator.FindDllDirectory()!;
        ScadSchemaData data;
        using (var session = new ScadApiSession(ScadApiNative.Load(dir)))
        {
            session.Open(spr);
            data = ScadApiReader.Read(session, new ScadReadOptions(), null, CancellationToken.None).Data;
        }
        var stiff = data.Stiffnesses.ToDictionary(x => x.Id);
        var elems = data.Elements.ToDictionary(e => e.Id);
        var cases = input.LoadCases.Where(c => c.SourceLoadNum != null).ToList();
        // Профили STZ — по сортаментам установленного SCAD, как ScadLinearCsfeaManualTests.
        var steel = ScadSteelProfiles.ResolveAll(
            data.Stiffnesses.Where(x => x.Text != null).Select(x => (x.Id, x.Text!)),
            b => File.Exists(Path.Combine(dir, b + ".PRF")) ? ScadPrfReader.Read(Path.Combine(dir, b + ".PRF")) : null);
        var reference = ScadRcModelAdapter.Adapt(new ScadRcModelInput
        {
            Data = data,
            PlateSection = id =>
            {
                double h = stiff.GetValueOrDefault(elems[id].StiffnessId)?.ThicknessM ?? 0.2;
                return new ScadShellElementSection(new CScore.PlateSection { Tag = $"h{h}", H = h }, $"elastic|{h}");
            },
            ShellSection = ScadRcModelAdapter.ElasticShells(3e10, 0.2),
            SteelShapes = steel.Where(p => p.Shape != null).ToDictionary(p => p.Num, p => p.Shape!),
            Stages = cases.Select(c => new ScadShellStage(c.Tag, [(c.SourceLoadNum!.Value, 1.0)], 1.0)).ToList(),
        });
        foreach (var line in reference.Report.Take(20)) output.WriteLine("  SCAD: " + line);
        var (m, r) = (adapted.Model, reference.Model);
        log($"SCAD-адаптер: узлов {r.Nodes.Count}, пластин {r.Shells.Count}, стержней {r.Beams.Count}, опор {r.Supports.Count}, " +
            $"пружин {r.Springs.Count}, тел {r.RigidBodies.Count}, на C1 {r.Shells.Count(x => x.FoundationC1 is > 0)}");
        int releasedM = m.Beams.Count(b => b.ReleaseI != 0 || b.ReleaseJ != 0);
        int releasedR = r.Beams.Count(b => b.ReleaseI != 0 || b.ReleaseJ != 0);
        output.WriteLine($"  стержней с шарнирами: {releasedM} / {releasedR} (FEM / SCAD)");
        var totals = adapted.StageTotals.ToDictionary(t => t.Tag);
        for (int k = 0; k < cases.Count; k++)
        {
            double down = -totals[cases[k].Tag].Fz, refDown = reference.StageTotalDownN[k];
            output.WriteLine(string.Create(CultureInfo.InvariantCulture,
                $"  L{cases[k].SourceLoadNum} «{cases[k].Tag}»: ΣFz вниз {down / 1e3:0.###} кН (SCAD-адаптер {refDown / 1e3:0.###} кН, " +
                $"{(refDown != 0 ? Math.Round((down / refDown - 1) * 100, 3) + 0.0 : 0):+0.000;-0.000;0.000} %)"));
        }
        Assert.Equal(r.Shells.Count, m.Shells.Count);
        Assert.Equal(r.Beams.Count, m.Beams.Count);
        Assert.Equal(r.RigidBodies.Count, m.RigidBodies.Count);
        Assert.Equal(r.Supports.Count, m.Supports.Count);
        Assert.Equal(r.Springs.Count, m.Springs.Count);
    }

    /// <summary>Линейный расчёт стадий: баланс вертикальных сил (закрепления, пружины, C1 пластин) и наибольший прогиб.</summary>
    static void SolveAndBalance(FemRcModelResult adapted, Action<string> log)
    {
        var m = adapted.Model;
        var build = RcStructuralMeshBuilder.Build(m, new LinearRcSectionFactory());
        log($"Сетка CSfea: NDof {build.Mesh.NDof}");
        var nodes = m.Nodes.ToDictionary(n => n.Id);
        for (int k = 0; k < m.Stages.Count; k++)
        {
            var f = build.Combination(m.Stages[k].Loads);
            var u = build.Mesh.SolveLinear(f, build.Bc);
            var r = build.Mesh.ComputeReactions(u, build.Bc, fExternal: f);
            double supports = build.NodeIds.Sum(id => r[build.Dof(id, 2)]);
            double springs = m.Springs.Where(s => s.Dof == 2).Sum(s => -s.Stiffness * u[build.Dof(s.NodeId, 2)]);
            double foundation = 0;
            foreach (var sh in m.Shells.Where(x => x.FoundationC1 is > 0))
            {
                var p = sh.NodeIds.Select(id => new[] { nodes[id].X, nodes[id].Y, nodes[id].Z }).ToArray();
                var w = RcStructuralMeshBuilder.AreaWeights(p);
                var n = Normal(p);
                for (int i = 0; i < p.Length; i++)
                {
                    int d = build.Dof(sh.NodeIds[i], 0);
                    double un = u[d] * n[0] + u[d + 1] * n[1] + u[d + 2] * n[2];
                    foundation -= sh.FoundationC1!.Value * w[i] * un * n[2];
                }
            }
            double fz = adapted.StageTotals[k].Fz, sum = supports + springs + foundation;
            double uzMin = build.NodeIds.Min(id => u[build.Dof(id, 2)]);
            log(string.Create(CultureInfo.InvariantCulture,
                $"«{m.Stages[k].Name}»: ΣFz {fz / 1e3:0.###} кН; ΣRz: опоры {supports / 1e3:0.###} + пружины {springs / 1e3:0.###} " +
                $"+ C1 {foundation / 1e3:0.###} = {sum / 1e3:0.###} кН (небаланс {(fz != 0 ? (sum + fz) / Math.Abs(fz) * 100 : 0):0.0000} %); " +
                $"min uz {uzMin * 1e3:0.###} мм"));
        }
    }

    /// <summary>Единичная нормаль пластины по диагоналям (Q4) или сторонам (T3).</summary>
    static double[] Normal(double[][] p)
    {
        double[] a = Sub(p[2], p[0]), b = p.Length == 4 ? Sub(p[3], p[1]) : Sub(p[1], p[0]);
        double[] n = p.Length == 4 ? Cross(a, b) : Cross(b, a);
        double len = Math.Sqrt(n[0] * n[0] + n[1] * n[1] + n[2] * n[2]);
        return [n[0] / len, n[1] / len, n[2] / len];

        static double[] Sub(double[] x, double[] y) => [x[0] - y[0], x[1] - y[1], x[2] - y[2]];
        static double[] Cross(double[] x, double[] y) =>
            [x[1] * y[2] - x[2] * y[1], x[2] * y[0] - x[0] * y[2], x[0] * y[1] - x[1] * y[0]];
    }

    /// <summary>
    /// Схема SCAD как схема FEM в памяти: сетка (<see cref="ScadSchemaConverter"/>), ГУ (<see cref="ScadBoundaryTransfer"/>),
    /// загружения и нагрузки (<see cref="ScadLoadTransfer"/>), свойства по жёсткостям; стадии — по номерам загружений SCAD.
    /// </summary>
    static FemRcModelInput ScadSchemaInMemory(ScadSchemaData data, IReadOnlyList<(string Tag, int ScadLoad)> stages)
    {
        var am = data.AnalysisModel!;
        var nodes = ScadSchemaConverter.ToFemMeshNodes(data, 1);
        var elements = ScadSchemaConverter.ToFemMeshElements(data, 1);
        var types = elements.ToDictionary(x => x.ElemTag, x => x.ElemType);
        var bc = ScadBoundaryTransfer.Transfer(am, nodes.Select(n => n.NodeTag).ToHashSet(), types);
        foreach (var el in elements)
            if (bc.ElementProps.TryGetValue(el.ElemTag, out var p))
                (el.ReleaseI, el.ReleaseJ, el.FoundationC1) = (p.ReleaseI, p.ReleaseJ, p.FoundationC1);
        int next = 0;
        var loads = ScadLoadTransfer.Transfer(am, types, [], [], [], () => --next);
        var stiffness = ScadSchemaConverter.ToSchemaStiffnesses(data).ToDictionary(s => s.Id);
        return new FemRcModelInput
        {
            MeshNodes = nodes, MeshElements = elements,
            Supports = bc.Supports, Springs = bc.Springs, RigidBodies = bc.RigidBodies,
            LoadCases = loads.LoadCases, ElementLoads = loads.ElementLoads, MeshNodeLoads = loads.MeshNodeLoads,
            Properties = new ScadElementStiffnessSource(stiffness, am.ForceUnitN, am.LengthUnitM),
            Stages = stages.Select(s => new FemRcStage(s.Tag,
                [(loads.LoadCases.Single(c => c.SourceLoadNum == s.ScadLoad).Id, 1.0)])).ToList(),
        };
    }
}
