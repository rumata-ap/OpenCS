using System.IO;
using System.Text.Json;
using CScore;
using CScore.Fem;
using OpenCS.Tasks;
using OpenCS.Utilites;
using Xunit;

namespace OpenCS.Tests;

/// <summary>
/// Одиночная задача по строке проверки по КЭ (<see cref="FemCheckRowTaskFactory"/>): проверка считается настоящим
/// исполнителем, по каждой её строке собирается задача, и задача даёт тот же результат, что строка.
/// </summary>
public sealed class FemCheckRowTaskFactoryTests : IDisposable
{
    readonly string _path = Path.Combine(Path.GetTempPath(), $"opencs_femrowtask_{Guid.NewGuid():N}.db");
    readonly TestProjectContext _ctx;

    public FemCheckRowTaskFactoryTests()
    {
        _ctx = new TestProjectContext(new DatabaseService(_path));
        _ctx.Notices.ConfirmAnswer = true;
    }

    public void Dispose()
    {
        _ctx.Db.Dispose();
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try { File.Delete(_path); } catch (IOException) { }
    }

    FemSchema Schema()
    {
        var schema = new FemSchema { Tag = "S" };
        _ctx.Db.SaveFemSchema(schema);
        _ctx.FemSchemaList.Add(schema);
        return schema;
    }

    FemMemberGroup Group(FemSchema schema, params string[] tags)
    {
        var group = new FemMemberGroup { SchemaId = schema.Id, Tag = "G", Kind = FemMemberGroup.KindMesh };
        group.SetTags(tags);
        _ctx.Db.SaveFemMemberGroup(group);
        if (!schema.MemberGroups.Contains(group)) schema.MemberGroups.Add(group);
        return group;
    }

    ForceSet AddSet(ForceSet set)
    {
        _ctx.Db.SaveForceSet(set);
        _ctx.ForceSetList.Add(set);
        return set;
    }

    async Task<(FemCheck Check, List<FemCheckRow> Rows)> RunCheck(FemCheck check)
    {
        _ctx.Db.SaveFemCheck(check);
        var result = await FemCheckExecutor.RunAsync(_ctx, check);
        Assert.NotNull(result);
        var rows = _ctx.Db.GetFemCheckRows(result!.Id);
        Assert.NotEmpty(rows);
        Assert.All(rows, r => Assert.False(r.NotChecked, $"{r.Label}: {r.WorstDescription}"));
        return (check, rows);
    }

    async Task<CalcTask> TaskOf(FemCheck check, FemCheckRow row)
    {
        var task = await FemCheckRowTaskFactory.BuildAsync(_ctx, check, FemCheckRowKey.Of(row),
            _ctx.CrossSectionList.Add, _ => { });
        Assert.NotNull(task);
        task!.Num = _ctx.Db.CalcTasks.Count + 1;
        _ctx.Db.SaveCalcTask(task);
        return task;
    }

    async Task<JsonElement> RunTask(CalcTask task)
    {
        var result = await CalcTaskExecutor.RunAsync(_ctx, task);
        Assert.NotNull(result);
        using var doc = JsonDocument.Parse(result!.DataJson);
        Assert.False(doc.RootElement.TryGetProperty("error", out _), result.DataJson);
        return doc.RootElement.Clone();
    }

    /// <summary>
    /// Колонна из трёх соосных КЭ по 1 м без промежуточных раскреплений: η считается по длине всей колонны (3 м),
    /// а не по длине КЭ — и одиночная задача (НДС с той же η) получает именно её. Плоскость деформаций задачи —
    /// та же, что у rc_check, которым проверка считала строку.
    /// </summary>
    [Fact]
    public async Task RcCheck_StrainStateWithMemberLength_EqualsRow()
    {
        var beam = ReportFixtures.BuildBeam();
        beam.Id = 7;
        beam.Num = 1;
        _ctx.CrossSectionList.Add(beam);
        var schema = Schema();
        _ctx.Db.SaveFemMeshSnapshot(schema.Id,
        [
            new FemMeshNode { NodeTag = "1", Z = 0 }, new FemMeshNode { NodeTag = "2", Z = 1 },
            new FemMeshNode { NodeTag = "3", Z = 2 }, new FemMeshNode { NodeTag = "4", Z = 3 },
        ],
        [
            new FemElement { ElemTag = "1", NodeIdsJson = "[1,2]", CrossSectionId = beam.Id },
            new FemElement { ElemTag = "2", NodeIdsJson = "[2,3]", CrossSectionId = beam.Id },
            new FemElement { ElemTag = "3", NodeIdsJson = "[3,4]", CrossSectionId = beam.Id },
        ]);
        var group = Group(schema, "1", "2", "3");
        var fs = AddSet(new ForceSet { Tag = "РСУ (C)", Kind = "bar", SourceSchemaId = schema.Id, SourceMemberId = group.Id });
        fs.Items.AddRange(
        [
            new LoadItem { Num = 1, Label = "1-1", N = -300, Mx = -60, SourceElementNum = 1, SourceSectionNum = 1 },
            new LoadItem { Num = 2, Label = "2-1", N = -200, Mx = -80, My = 3, SourceElementNum = 2, SourceSectionNum = 1 },
            new LoadItem { Num = 3, Label = "3-1", N = -100, Mx = -40, SourceElementNum = 3, SourceSectionNum = 1 },
        ]);
        _ctx.Db.SaveForceSet(fs);

        var eta = new FemEtaParams { Enabled = true, MuX = 1.0, MuY = 0.7, PsiMode = FemEtaParams.PsiManual, PsiX = 1.0, PsiY = 1.0 };
        var (check, rows) = await RunCheck(new FemCheck
        {
            SchemaId = schema.Id, MemberId = group.Id, NormCode = "rc_check", Tag = "ЖБ",
            ParamsJson = new BarCheckParams { Eta = eta }.ToJson(),
        });

        foreach (var row in rows)
        {
            var task = await TaskOf(check, row);
            Assert.Equal("strain_state", task.Kind);
            Assert.Equal(beam.Id, task.SectionId);   // сечение проекта — без копии
            var p = LimitForceParams.Parse(task.ParamsJson);
            Assert.True(p.EtaEnabled);
            Assert.Equal(3.0, p.EtaL!.Value, 9);      // длина колонны между раскреплениями, не КЭ
            Assert.Equal(0.7, p.EtaMuY);

            // Чем проверка считала строку — тем же исполнителем rc_check.
            var call = Capture(check, row);
            var rc = TaskRunner.Run(call.BarTask!, call.BarSection!, call.Bar!);
            using var rcDoc = JsonDocument.Parse(rc.DataJson);
            Assert.Equal(row.Utilization, rcDoc.RootElement.GetProperty("utilization").GetDouble(), 6);

            var single = await RunTask(task);
            foreach (var k in new[] { "e0", "ky", "kz" })
                Assert.Equal(rcDoc.RootElement.GetProperty(k).GetDouble(), single.GetProperty(k).GetDouble(), 7);
        }

        // Повторный выбор строки находит ту же задачу.
        Assert.NotNull(FemCheckRowTaskFactory.FindExisting(_ctx.Db.CalcTasks, check, FemCheckRowKey.Of(rows[0])));
    }

    /// <summary>
    /// Сечение, собранное проверкой (не из проекта), сохраняется копией: новые области в пуле, фибры и пометка
    /// источника переживают перезагрузку проекта; повторный вызов возвращает ту же копию, сечение проекта — само.
    /// </summary>
    [Fact]
    public void EnsureBarSection_SavesCopyOnce_ProjectSectionAsIs()
    {
        var synthesized = ReportFixtures.BuildBeam();
        var project = new List<CrossSection>();

        var copy = FemCheckRowTaskFactory.EnsureBarSection(_ctx.Db, project, synthesized, "из проверки: КЭ 5", project.Add);
        var again = FemCheckRowTaskFactory.EnsureBarSection(_ctx.Db, project, synthesized, "из проверки: КЭ 5", project.Add);
        var own = FemCheckRowTaskFactory.EnsureBarSection(_ctx.Db, project, copy, "другая пометка", project.Add);

        Assert.Same(copy, again);
        Assert.Same(copy, own);
        Assert.Single(project);
        Assert.NotEqual(0, copy.Id);
        Assert.All(copy.Areas, a => Assert.NotEqual(0, a.Id));
        Assert.All(synthesized.Areas, a => Assert.Equal(0, a.Id));   // исходное сечение проверки не тронуто

        using var reopened = new DatabaseService(_path);
        reopened.LoadAll();
        var loaded = Assert.Single(reopened.CrossSections, s => s.Description == "из проверки: КЭ 5");
        Assert.Equal(synthesized.Areas.Count, loaded.Areas.Count);
        double Area(CrossSection s) => s.Areas.Sum(a => a.Fibers.Sum(f => f.Area));
        Assert.Equal(Area(synthesized), Area(loaded), 9);
        Assert.Equal(synthesized.Areas.Sum(a => a.Fibers.Count(f => f.TypeFiber == FiberType.point)),
                     loaded.Areas.Sum(a => a.Fibers.Count(f => f.TypeFiber == FiberType.point)));
    }

    /// <summary>
    /// Сталь, длины «по сетке»: колонна из трёх КЭ по 1 м — lef каждого КЭ от длины колонны между раскреплениями
    /// (3 м), не от длины КЭ; одиночная steel_check получает эти lef числами и усилия строки — Кисп = строке.
    /// </summary>
    [Fact]
    public async Task Steel_MeshLef_EqualsRow()
    {
        var area = new MaterialArea { Tag = "Короб", Material = new Material
        {
            Type = MatType.Steel, E = 2.06e8,
            MaterialChars = [new MaterialChars(CalcType.C) { Ry = 240000, Ru = 360000 }]
        }};
        area.Contours.Add(new Contour([-.1, .1, .1, -.1, -.1], [-.2, -.2, .2, .2, -.2], "outer") { Type = ContourType.Hull });
        area.Contours.Add(new Contour([-.09, .09, .09, -.09, -.09], [-.19, -.19, .19, .19, -.19], "hole") { Type = ContourType.Hole });
        var box = new CrossSection { Id = 9, Num = 1, Tag = "Короб", Areas = [area] };
        _ctx.CrossSectionList.Add(box);

        var schema = Schema();
        _ctx.Db.SaveFemMeshSnapshot(schema.Id,
        [
            new FemMeshNode { NodeTag = "1", Z = 0 }, new FemMeshNode { NodeTag = "2", Z = 1 },
            new FemMeshNode { NodeTag = "3", Z = 2 }, new FemMeshNode { NodeTag = "4", Z = 3 },
        ],
        [
            new FemElement { ElemTag = "1", NodeIdsJson = "[1,2]", CrossSectionId = box.Id },
            new FemElement { ElemTag = "2", NodeIdsJson = "[2,3]", CrossSectionId = box.Id },
            new FemElement { ElemTag = "3", NodeIdsJson = "[3,4]", CrossSectionId = box.Id },
        ]);
        var group = Group(schema, "1", "2", "3");
        var fs = AddSet(new ForceSet { Tag = "РСУ (C)", Kind = "bar", SourceSchemaId = schema.Id, SourceMemberId = group.Id });
        fs.Items.AddRange(
        [
            new LoadItem { Num = 1, Label = "1-1", N = -400, Mx = 20, Vy = 10, SourceElementNum = 1, SourceSectionNum = 1 },
            new LoadItem { Num = 2, Label = "2-1", N = -300, Mx = 35, My = 5, SourceElementNum = 2, SourceSectionNum = 1 },
            new LoadItem { Num = 3, Label = "3-1", N = -200, Mx = 10, T = 1, SourceElementNum = 3, SourceSectionNum = 1 },
        ]);
        _ctx.Db.SaveForceSet(fs);

        var (check, rows) = await RunCheck(new FemCheck
        {
            SchemaId = schema.Id, MemberId = group.Id, NormCode = "steel_check", Tag = "Сталь",
            ParamsJson = new CScore.Sp16.SteelDesignParams { MeshLef = new CScore.Sp16.SteelMeshLef { MuX = 1.0, MuY = 0.7 } }.ToJson(),
        });

        foreach (var row in rows)
        {
            var task = await TaskOf(check, row);
            Assert.Equal("steel_check", task.Kind);
            Assert.Equal(box.Id, task.SectionId);
            var p = CScore.Sp16.SteelDesignParams.Parse(task.ParamsJson);
            Assert.Equal(3.0, p.LefX, 9);
            Assert.Equal(2.1, p.LefY, 9);
            Assert.NotNull(p.ManualForces);
            var single = await RunTask(task);
            Assert.Equal(row.Utilization, single.GetProperty("utilization").GetDouble(), 6);
        }
    }

    /// <summary>
    /// Сечение КЭ с армированием программы-источника — клон шаблона с его Id (так собирают источники ЛИРЫ/SCAD):
    /// задача получает копию с армированием КЭ, а не шаблон без него; клон стержня с Id сечения проекта — тоже.
    /// </summary>
    [Fact]
    public void EnsureSection_CloneWithTemplateId_SavesCopyWithRebar()
    {
        var template = new PlateSection { Tag = "Шаблон", Num = 1, H = 0.2, ConcreteMaterialId = 1, RebarMaterialId = 2 };
        _ctx.Db.SavePlateSection(template);
        var plates = new List<PlateSection> { template };
        var element = template.CloneForCalc();   // Id шаблона
        element.RebarLayers.Add(new PlateRebarLayer { Name = "верх", Asx = 1.13e-3, Asy = 1.13e-3, Zsx = 0.07, Zsy = 0.06, InputMode = "direct" });

        var plate = FemCheckRowTaskFactory.EnsurePlateSection(_ctx.Db, plates, element, "Шаблон — КЭ 167 (Заданное)", plates.Add);

        Assert.NotSame(template, plate);
        Assert.NotEqual(template.Id, plate.Id);
        Assert.Equal(1.13e-3, Assert.Single(plate.RebarLayers).Asx);
        Assert.Same(template, FemCheckRowTaskFactory.EnsurePlateSection(_ctx.Db, plates, template, "x", plates.Add));

        var beam = ReportFixtures.BuildBeam();
        beam.Id = 7;
        var bars = new List<CrossSection> { beam };
        var barClone = beam.CloneForCalc();
        Assert.NotSame(beam, FemCheckRowTaskFactory.EnsureBarSection(_ctx.Db, bars, barClone, "из проверки: КЭ 1", bars.Add));
    }

    FemCheckRowCall Capture(FemCheck check, FemCheckRow row)
    {
        var setup = FemCheckExecutor.PrepareAsync(_ctx, check).GetAwaiter().GetResult()!;
        return FemCheckRunner.CaptureRow(check, setup.Target, setup.Scope, setup.Selected, setup.Inputs, FemCheckRowKey.Of(row))!;
    }

    /// <summary>Пластины: Кисп одиночной задачи = Кисп строки (Вуд–Армер ПС1, слоистая ПС1).</summary>
    [Theory]
    [InlineData("shell_simpl_wa_uls", "EtaMax")]
    [InlineData("shell_layered", "check.utilization")]
    public async Task Plate_SingleUtilization_EqualsRow(string kind, string utilizationPath)
    {
        var (schema, group, plate, fs) = PlateFixture("РСУ (C)");

        var (check, rows) = await RunCheck(new FemCheck
        {
            SchemaId = schema.Id, MemberId = group.Id, NormCode = "rc_plate_check", Tag = "Плита",
            ParamsJson = JsonSerializer.Serialize(new PlateCheckParams { Kind = kind, StripModel = StripModel.Ndm }),
        });

        foreach (var row in rows)
        {
            var task = await TaskOf(check, row);
            Assert.Equal(plate.Id, task.SectionId);
            var single = await RunTask(task);
            double actual = utilizationPath.Split('.').Aggregate(single, (e, part) => e.GetProperty(part)).GetDouble();
            Assert.Equal(row.Utilization, actual, 6);
        }
    }

    /// <summary>Плита из двух КЭ с сечением группы и набором строк по КЭ <paramref name="setTag"/>.</summary>
    (FemSchema Schema, FemMemberGroup Group, PlateSection Plate, ForceSet Set) PlateFixture(string setTag)
    {
        var (concrete, rebar) = PlateMaterials();
        _ctx.Db.Materials.Add(concrete);
        _ctx.Db.Materials.Add(rebar);
        var plate = new PlateSection { Tag = "Плита", Num = 1, H = 0.2, ConcreteMaterialId = 1, RebarMaterialId = 2, ConcreteDiagramType = DiagrammType.L3 };
        plate.RebarLayers.Add(new PlateRebarLayer { Name = "низ", Asx = 7.85e-4, Asy = 7.85e-4, Zsx = -0.07, Zsy = -0.06, InputMode = "direct" });
        plate.RebarLayers.Add(new PlateRebarLayer { Name = "верх", Asx = 7.85e-4, Asy = 7.85e-4, Zsx = 0.07, Zsy = 0.06, InputMode = "direct" });
        _ctx.Db.SavePlateSection(plate);

        var schema = Schema();
        _ctx.Db.SaveFemMeshSnapshot(schema.Id,
        [
            new FemMeshNode { NodeTag = "1" }, new FemMeshNode { NodeTag = "2", X = 1 },
            new FemMeshNode { NodeTag = "3", Y = 1 }, new FemMeshNode { NodeTag = "4", X = 1, Y = 1 },
            new FemMeshNode { NodeTag = "5", X = 2 }, new FemMeshNode { NodeTag = "6", X = 2, Y = 1 },
        ],
        [
            new FemElement { ElemTag = "1", ElemType = "shell", NodeIdsJson = "[1,2,3,4]" },
            new FemElement { ElemTag = "2", ElemType = "shell", NodeIdsJson = "[2,5,4,6]" },
        ]);
        var group = Group(schema, "1", "2");
        group.PlateSectionId = plate.Id;
        _ctx.Db.SaveFemMemberGroup(group);
        var fs = AddSet(new ForceSet { Tag = setTag, Kind = "shell", SourceSchemaId = schema.Id, SourceMemberId = group.Id });
        fs.ShellItems.AddRange(
        [
            new ShellLoadItem { Num = 1, Label = "а", Mx = 30, My = 10, Mxy = 8, SourceElementNum = 1 },
            new ShellLoadItem { Num = 2, Label = "б", Nx = 50, Mx = -25, My = 15, Mxy = -12, SourceElementNum = 1 },
            new ShellLoadItem { Num = 3, Label = "в", Mx = 5, My = 35, SourceElementNum = 2 },
        ]);
        _ctx.Db.SaveForceSet(fs);
        return (schema, group, plate, fs);
    }

    /// <summary>Набор пластин с теми же метками строк — длительная часть (NL) к набору <paramref name="of"/>.</summary>
    ForceSet LongSet(FemSchema schema, FemMemberGroup group, ForceSet of, double share)
    {
        var nl = AddSet(new ForceSet { Tag = "РСУ (NL)", Kind = "shell", SourceSchemaId = schema.Id, SourceMemberId = group.Id });
        nl.ShellItems.AddRange(of.ShellItems.Select(r => new ShellLoadItem
        {
            Num = r.Num, Label = r.Label, SourceElementNum = r.SourceElementNum,
            Nx = r.Nx * share, Ny = r.Ny * share, Nxy = r.Nxy * share, Mx = r.Mx * share, My = r.My * share, Mxy = r.Mxy * share,
        }));
        _ctx.Db.SaveForceSet(nl);
        return nl;
    }

    /// <summary>
    /// Слоистая ПС2 по п. 8.2.7: строки N — acrc1 + acrc2 − acrc3 с парной строкой явно заданного NL-набора
    /// (или долей N без него), строки NL — продолжительное раскрытие; одиночная shell_layered_sls = строке.
    /// </summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task LayeredSls_827_EqualsRow(bool explicitNlSet)
    {
        var (schema, group, _, fs) = PlateFixture("РСУ (N)");   // моменты выше Mcrc ≈ 7 кН·м/м — трещины есть
        var nl = LongSet(schema, group, fs, 0.6);

        var (check, rows) = await RunCheck(new FemCheck
        {
            SchemaId = schema.Id, MemberId = group.Id, NormCode = "rc_plate_check", Tag = "Трещины",
            ParamsJson = JsonSerializer.Serialize(new PlateCheckParams
            {
                Kind = "shell_layered", AcrcLimMm = 0.2,
                NlForceSetId = explicitNlSet ? nl.Id : 0, LtFraction = explicitNlSet ? 0 : 0.5,
            }),
        });
        Assert.Contains(rows, r => r.Utilization > 1e-6);
        Assert.Contains(rows, r => r.CalcType == "NL");

        foreach (var row in rows)
        {
            var task = await TaskOf(check, row);
            Assert.Equal("shell_layered_sls", task.Kind);
            var p = ShellLayeredSlsParams.Parse(task.ParamsJson);
            Assert.True(p.Combine827);
            if (row.CalcType == "N") Assert.Equal(explicitNlSet, p.Long != null);
            var single = await RunTask(task);
            // Строка без НДС («НДС», Кисп = 2 условно) — задача тоже без сходимости.
            if (row.WorstFormula == "НДС") Assert.False(single.GetProperty("Converged").GetBoolean());
            else Assert.Equal(row.Utilization, single.GetProperty("Utilization").GetDouble(), 6);
        }
    }

    static (Material concrete, Material rebar) PlateMaterials()
    {
        MaterialChars Concrete(CalcType calc) => new(calc)
        {
            Type = MatType.Concrete, E = 30_000_000.0, Fc = -14_500.0, Ft = 1_050.0,
            Ec0 = -0.002, Ec1 = -0.6 * 14_500.0 / 30_000_000.0, Ec2 = -0.0035,
            Ec1Red = -0.0015, Et0 = 0.0001, Et1 = 0.6 * 1_050.0 / 30_000_000.0, Et2 = 0.00015, Et1Red = 0.00008,
        };
        MaterialChars Rebar(CalcType calc) => new(calc)
        {
            Type = MatType.ReSteelF, E = 200_000_000.0, Fc = -435_000.0, Ft = 435_000.0, Ec2 = -0.025, Et2 = 0.025,
        };
        var concrete = new Material { Id = 1, Num = 1, Type = MatType.Concrete, E = 30_000_000.0 };
        concrete.MaterialChars = [Concrete(CalcType.C), Concrete(CalcType.CL), Concrete(CalcType.N), Concrete(CalcType.NL)];
        var rebar = new Material { Id = 2, Num = 2, Type = MatType.ReSteelF, E = 200_000_000.0 };
        rebar.MaterialChars = [Rebar(CalcType.C), Rebar(CalcType.CL), Rebar(CalcType.N), Rebar(CalcType.NL)];
        return (concrete, rebar);
    }
}
