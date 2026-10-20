using System.IO;
using System.Text.Json;
using CScore;
using OpenCS.Tasks;
using OpenCS.Utilites;
using Xunit;

namespace OpenCS.Tests;

/// <summary>
/// Одиночная задача по строке пакета (<see cref="BatchRowTaskFactory"/>): пакет и задача по его строке
/// считаются настоящим исполнителем, и результат одиночной задачи совпадает с результатом строки пакета.
/// </summary>
public sealed class BatchRowTaskFactoryTests : IDisposable
{
    const int BarSectionId = 7, PlateSectionId = 8, BarSetId = 11, ShellSetId = 12, LongSetId = 13;

    readonly string _path = Path.Combine(Path.GetTempPath(), $"opencs_batchrow_{Guid.NewGuid():N}.db");
    readonly TestProjectContext _ctx;

    public BatchRowTaskFactoryTests()
    {
        _ctx = new TestProjectContext(new DatabaseService(_path));

        var beam = ReportFixtures.BuildBeam();
        beam.Id = BarSectionId;
        _ctx.CrossSectionList.Add(beam);
        AddSet(new ForceSet
        {
            Id = BarSetId, Kind = "bar", Tag = "РСУ",
            // Mx < 0 растягивает низ, где стержни балки: за пределом прочности (растянут неармированный верх)
            // равновесие неоднозначно, и разные решатели законно сходятся в разные точки.
            Items =
            [
                new LoadItem { Id = 101, Num = 1, Label = "С1", N = -100, Mx = -60, Vy = 60 },
                new LoadItem { Id = 102, Num = 2, Label = "С2", N = -300, Mx = -90, My = 4, Vy = 90 },
                new LoadItem { Id = 103, Num = 3, Label = "С3", N = -50, Mx = -45, My = -3, Vy = 40 },
            ],
        });
        // Длительные усилия для ширины трещин «два набора»: строки в другом порядке — пакет сопоставляет по метке.
        AddSet(new ForceSet
        {
            Id = LongSetId, Kind = "bar", Tag = "Длительные",
            Items =
            [
                new LoadItem { Id = 202, Num = 1, Label = "С2", N = -300, Mx = -55, My = 2 },
                new LoadItem { Id = 201, Num = 2, Label = "С1", N = -100, Mx = -35 },
                new LoadItem { Id = 203, Num = 3, Label = "С3", N = -50, Mx = -20, My = -1 },
            ],
        });

        var (concrete, rebar) = PlateMaterials();
        _ctx.Db.Materials.Add(concrete);
        _ctx.Db.Materials.Add(rebar);
        var plate = new PlateSection { Id = PlateSectionId, Tag = "Плита", H = 0.2, ConcreteMaterialId = 1, RebarMaterialId = 2, ConcreteDiagramType = DiagrammType.L3 };
        plate.RebarLayers.Add(new PlateRebarLayer { Name = "низ", Asx = 7.85e-4, Asy = 7.85e-4, Zsx = -0.07, Zsy = -0.06, InputMode = "direct" });
        plate.RebarLayers.Add(new PlateRebarLayer { Name = "верх", Asx = 7.85e-4, Asy = 7.85e-4, Zsx = 0.07, Zsy = 0.06, InputMode = "direct" });
        _ctx.Db.PlateSections.Add(plate);
        AddSet(new ForceSet
        {
            Id = ShellSetId, Kind = "shell", Tag = "РСУ пластин",
            ShellItems =
            [
                new ShellLoadItem { Num = 1, Label = "т.1", Mx = 30.0, My = 10.0, Mxy = 8.0, Qx = 40 },
                // Напряжение σx: при «напряжения → усилия» пакет берёт Nx = σx·h.
                new ShellLoadItem { Num = 2, Label = "т.2", Mx = -25.0, My = 15.0, Mxy = -12.0, SigmaX = 500.0 },
                new ShellLoadItem { Num = 3, Label = "т.3", Nx = 50, Mx = 5.0, My = 35.0 },
            ],
        });
    }

    public void Dispose()
    {
        _ctx.Db.Dispose();
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try { File.Delete(_path); } catch (IOException) { }
    }

    void AddSet(ForceSet set)
    {
        _ctx.Db.ForceSets.Add(set);
        _ctx.ForceSetList.Add(set);
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

    /// <summary>Вид пакета, тип расчёта, параметры пакета и пары ключей «строка пакета — одиночный результат».</summary>
    public static TheoryData<string, CalcType, string, string> Cases() => new()
    {
        { "limit_force_batch",  CalcType.C, """{"solver":"fast"}""", "utilization=utilization" },
        { "limit_moment_batch", CalcType.C, """{"solver":"bisection"}""", "utilization=utilization" },
        { "limit_axial_batch",  CalcType.C, """{"solver":"bisection"}""", "utilization=utilization" },
        { "strain_state_batch", CalcType.C, "{}", "e0=e0;ky=ky;kz=kz" },
        { "strength_ndm_batch", CalcType.C, "{}", "e0=e0;ky=ky;kz=kz" },
        { "total_curvature_batch", CalcType.N, """{"ForcesMode":"share","LongShare":0.6}""", "Mx_long=Mx_long;Mcrc=Mcrc;k_full=k_full;converged=all_converged" },
        { "shell_simpl_wa_uls_batch", CalcType.C, """{"AutoStressToForce":true}""", "eta_max=EtaMax" },
        { "shell_simpl_capri_uls_batch", CalcType.C, """{"AutoStressToForce":true,"StripModel":"ndm"}""", "eta_max=EtaMax" },
        { "shell_strain_state_batch", CalcType.C, "{}", "converged=converged" },
        { "shell_layered_uls_batch", CalcType.C, "{}", "status=@status" },
        { "shell_simpl_capri_design_uls_batch", CalcType.C, "{}", "TopAsx=design.Top.Asx" },
        { "cracking_batch", CalcType.N, "{}", "Mcrc=Mcrc;Mx_crc=Mx_crc;converged=converged" },
        { "crack_width_batch", CalcType.N, """{"ForcesMode":"share","LongShare":0.6,"AcrcUltLong":0.2}""",
            "acrc_long=acrc_long;acrc_short=acrc_short;Mx_long=Mx_long;status=@status" },
        { "crack_width_batch", CalcType.N, $$"""{"ForcesMode":"two_sets","ForceSetLongId":{{LongSetId}}}""",
            "acrc_long=acrc_long;acrc_short=acrc_short;Mx_long=Mx_long;status=@status" },
        { "crack_width_batch", CalcType.N, """{"etaEnabled":true,"etaL":6,"etaMuX":1,"etaMuY":1}""",
            "acrc_long=acrc_long;Mx_total=Mx_total;status=@status" },
        { "shear_inclined_batch", CalcType.C, """{"ConstructiveRequirements103Confirmed":true}""", "utilization=utilization" },
        { "shell_layered_sls_batch", CalcType.N, """{"auto_stress_to_force":true,"acrc_lim_mm":0.2}""", "acrc_mm=AcrcMaxMm;status=@status" },
    };

    [Theory]
    [MemberData(nameof(Cases))]
    public async Task SingleTaskResult_EqualsBatchRow(string batchKind, CalcType calcType, string paramsJson, string keys)
    {
        bool plate = batchKind.StartsWith("shell_", StringComparison.Ordinal);
        var batch = new CalcTask
        {
            Num = 1, Tag = "пакет", Kind = batchKind, CalcType = calcType, ParamsJson = paramsJson,
            SectionId = plate ? PlateSectionId : BarSectionId, ForceSetId = plate ? ShellSetId : BarSetId,
        };
        _ctx.Db.SaveCalcTask(batch);
        var batchResult = await CalcTaskExecutor.RunAsync(_ctx, batch);
        Assert.NotNull(batchResult);
        using var batchDoc = JsonDocument.Parse(batchResult!.DataJson);
        // Совпадение null = null или 0 = 0 ничего не доказывает: хотя бы одно сверяемое число должно быть ненулевым.
        bool anyNumeric = false, anyNonZero = false;

        foreach (int rowNum in new[] { 1, 2, 3 })
        {
            var single = BatchRowTaskFactory.Create(batch, rowNum, "сечение", _ctx.ForceSets, _ctx.PlateSections);
            Assert.Equal(BatchRowTaskFactory.SingleKind(batchKind), single.Kind);
            single.Num = 1 + rowNum;
            _ctx.Db.SaveCalcTask(single);
            var singleResult = await CalcTaskExecutor.RunAsync(_ctx, single);
            Assert.NotNull(singleResult);
            using var singleDoc = JsonDocument.Parse(singleResult!.DataJson);
            // Исключение обработчика — не результат; «не сошлось» сверяется со строкой пакета ниже.
            Assert.False(singleDoc.RootElement.TryGetProperty("error", out _), $"{batchKind} № {rowNum}: {singleResult.DataJson}");
            var row = BatchRow(batchDoc.RootElement, rowNum);

            foreach (var pair in keys.Split(';'))
            {
                var (rowKey, singleKey) = (pair.Split('=')[0], pair.Split('=')[1]);
                var expected = Find(row, rowKey) ?? throw new Xunit.Sdk.XunitException($"нет «{rowKey}» в строке пакета");
                if (singleKey == "@status")
                {
                    Assert.Equal(expected.GetString(), singleResult.Status);
                    continue;
                }
                var actual = Find(singleDoc.RootElement, singleKey) ?? throw new Xunit.Sdk.XunitException($"нет «{singleKey}» в одиночном результате");
                AssertSame(expected, actual, $"{batchKind} № {rowNum}, {rowKey}");
                if (expected.ValueKind is JsonValueKind.Number or JsonValueKind.Null)
                {
                    anyNumeric = true;
                    anyNonZero |= expected.ValueKind == JsonValueKind.Number && Math.Abs(expected.GetDouble()) > 1e-9;
                }
            }
        }
        Assert.True(!anyNumeric || anyNonZero, $"{batchKind}: все сверяемые числа нулевые или пустые — {batchResult.DataJson}");
    }

    [Theory]
    [InlineData("shell_simpl_capri_uls_batch", CalcType.C)]
    [InlineData("shell_simpl_wa_sls_batch", CalcType.N)]
    [InlineData("shell_simpl_capri_design_uls_batch", CalcType.C)]
    [InlineData("shell_simpl_capri_design_sls_batch", CalcType.N)]
    [InlineData("shell_layered_uls_batch", CalcType.C)]
    [InlineData("shell_layered_sls_batch", CalcType.N)]
    [InlineData("total_curvature_batch", CalcType.N)]
    [InlineData("crack_width_batch", CalcType.N)]
    public async Task Batch_ParallelEqualsSequential(string batchKind, CalcType calcType)
    {
        bool plate = batchKind.StartsWith("shell_", StringComparison.Ordinal);
        var batch = new CalcTask
        {
            Num = 1, Tag = "пакет", Kind = batchKind, CalcType = calcType,
            SectionId = plate ? PlateSectionId : BarSectionId, ForceSetId = plate ? ShellSetId : BarSetId,
            ParamsJson = plate ? """{"AutoStressToForce":true,"StripModel":"ndm"}""" : "{}",
        };
        _ctx.Db.SaveCalcTask(batch);

        _ctx.CalcSettings = new CalcSettings { BatchParallel = false };
        var sequential = await CalcTaskExecutor.RunAsync(_ctx, batch);
        _ctx.CalcSettings = new CalcSettings { BatchParallel = true };
        var parallel = await CalcTaskExecutor.RunAsync(_ctx, batch);

        Assert.NotNull(sequential);
        Assert.NotNull(parallel);
        Assert.DoesNotContain("\"error\"", sequential!.DataJson);
        Assert.Equal(sequential.Status, parallel!.Status);
        Assert.Equal(sequential.DataJson, parallel.DataJson);
    }

    /// <summary>
    /// Пакет ПС2: грань, растянутая моментом выше M_crc (у h = 0,2 м — 1050·1,3·0,04/6 ≈ 9,1 кН·м/м), без арматуры —
    /// строка не проходит (трещина без арматуры). Раньше такая полоса выбрасывалась, а пакет Вуда–Армера ПС2
    /// ещё и решал по фасеткам Капра–Мори, которых у него нет, — строки всегда «ok».
    /// </summary>
    [Theory]
    [InlineData("shell_simpl_wa_sls_batch")]
    [InlineData("shell_simpl_capri_sls_batch")]
    public async Task SlsBatch_TensionFaceWithoutRebar_Fails(string kind)
    {
        var bottomOnly = new PlateSection { Id = 9, Tag = "Только низ", H = 0.2, ConcreteMaterialId = 1, RebarMaterialId = 2, ConcreteDiagramType = DiagrammType.L3 };
        bottomOnly.RebarLayers.Add(new PlateRebarLayer { Name = "низ", Asx = 7.85e-4, Asy = 7.85e-4, Zsx = -0.07, Zsy = -0.06, InputMode = "direct" });
        _ctx.Db.PlateSections.Add(bottomOnly);
        var batch = new CalcTask
        {
            Num = 1, Tag = "пакет", Kind = kind, CalcType = CalcType.N, SectionId = 9, ForceSetId = ShellSetId,
            ParamsJson = """{"AutoStressToForce":true}""",
        };
        _ctx.Db.SaveCalcTask(batch);

        var result = await CalcTaskExecutor.RunAsync(_ctx, batch);

        using var doc = JsonDocument.Parse(result!.DataJson);
        var row1 = BatchRow(doc.RootElement, 1);   // Mx = 30 кН·м/м растягивает верх
        Assert.True(row1.GetProperty("status").GetString() == "fail", result.DataJson);
    }

    [Fact]
    public void BarTask_ReferencesSetRow_AndKeepsBatchParams()
    {
        var batch = new CalcTask
        {
            Id = 5, Kind = "strain_state_batch", SectionId = BarSectionId, ForceSetId = BarSetId, CalcType = CalcType.C,
            ParamsJson = """{"solver":"bisection","N":0,"Mx":0,"My":0,"etaEnabled":true,"etaIterative":false,"etaL":4.5,"etaMuX":0.7,"etaMuY":1,"etaPsiX":1,"etaPsiY":1}""",
        };

        var single = BatchRowTaskFactory.Create(batch, 2, "Балка", _ctx.ForceSets, _ctx.PlateSections);

        Assert.Equal("strain_state", single.Kind);
        Assert.Equal("Балка — С2", single.Tag);
        Assert.Equal((BarSetId, 102), (single.ForceSetId, single.ForceItemId));
        var p = LimitForceParams.Parse(single.ParamsJson);
        Assert.Equal((-300.0, -90.0, 4.0), (p.N, p.Mx, p.My));
        Assert.True(p.EtaEnabled);
        Assert.Equal(4.5, p.EtaL);
        Assert.Equal(0.7, p.EtaMuX);
    }

    [Fact]
    public void CrackWidth_TwoSets_TakesLongRowByLabel()
    {
        var batch = new CalcTask
        {
            Id = 7, Kind = "crack_width_batch", SectionId = BarSectionId, ForceSetId = BarSetId, CalcType = CalcType.N,
            ParamsJson = $$"""{"ForcesMode":"two_sets","ForceSetLongId":{{LongSetId}},"Phi2":0.8,"etaEnabled":true,"etaL":3}""",
        };

        var single = BatchRowTaskFactory.Create(batch, 1, "Балка", _ctx.ForceSets, _ctx.PlateSections);

        Assert.Equal(("crack_width", 101), (single.Kind, single.ForceItemId));
        var p = CrackWidthTaskParams.Parse(single.ParamsJson);
        Assert.Equal(("force_item_long", LongSetId, 201), (p.ForcesMode, p.ForceSetLongId, p.ForceItemLongId));
        Assert.Equal((-100.0, -60.0, 0.0, 0.8), (p.N, p.Mx, p.My, p.Phi2));
        Assert.Equal(3.0, LimitForceParams.Parse(single.ParamsJson).EtaL);
    }

    [Fact]
    public void FireCheck_KeepsBatchParams_ReferencesRow()
    {
        var batch = new CalcTask
        {
            Id = 8, Kind = "fire_r_check_batch", SectionId = BarSectionId, ForceSetId = BarSetId, CalcType = CalcType.N,
            ParamsJson = FireTaskParamsBuilder.Build("fire_r_check_batch", 5, 90, "mvp"),
        };

        var single = BatchRowTaskFactory.Create(batch, 3, "Балка", _ctx.ForceSets, _ctx.PlateSections);

        Assert.Equal(("fire_r_check", BarSetId, 103, CalcType.N), (single.Kind, single.ForceSetId, single.ForceItemId, single.CalcType));
        var p = FireTaskParamsBuilder.Parse("fire_r_check", single.ParamsJson);
        Assert.Equal((5, 90.0, "mvp"), (p.FireSectionId, p.SnapshotTimeMin, p.Method));
    }

    [Fact]
    public void TwoStage_FixedStage1_RowOfStage2()
    {
        var batch = new CalcTask
        {
            Id = 6, Kind = "two_stage_strain_batch", SectionId = BarSectionId, CalcType = CalcType.C,
            ParamsJson = new TwoStageParams
            {
                Stage1 = new StageForce { Mode = "item", ForceSetId = BarSetId, ForceItemId = 101 },
                Stage2 = new StageForce { Mode = "set", ForceSetId = BarSetId },
            }.ToJson(),
        };

        var single = BatchRowTaskFactory.Create(batch, 3, "Сборно-монолитная", _ctx.ForceSets, _ctx.PlateSections);

        Assert.Equal("two_stage_strain", single.Kind);
        Assert.Equal(BarSetId, single.ForceSetId);
        var p = TwoStageParams.Parse(single.ParamsJson);
        Assert.Equal(("item", 101), (p.Stage1.Mode, p.Stage1.ForceItemId));
        Assert.Equal(("item", BarSetId, 103), (p.Stage2.Mode, p.Stage2.ForceSetId, p.Stage2.ForceItemId));
    }

    [Fact]
    public void TwoStage_SetPerRow_TakesSameRowOfStage1()
    {
        var batch = new CalcTask
        {
            Id = 6, Kind = "two_stage_strain_batch", SectionId = BarSectionId, CalcType = CalcType.C,
            ParamsJson = new TwoStageParams
            {
                Stage1 = new StageForce { Mode = "set", ForceSetId = BarSetId },
                Stage2 = new StageForce { Mode = "set", ForceSetId = BarSetId },
            }.ToJson(),
        };

        var p = TwoStageParams.Parse(BatchRowTaskFactory.Create(batch, 2, "С", _ctx.ForceSets, _ctx.PlateSections).ParamsJson);

        Assert.Equal((102, 102), (p.Stage1.ForceItemId, p.Stage2.ForceItemId));
    }

    [Fact]
    public void FindExisting_ByBatchAndRow()
    {
        var batch = new CalcTask { Id = 9, Kind = "limit_force_batch", SectionId = BarSectionId, ForceSetId = BarSetId };
        var row1 = BatchRowTaskFactory.Create(batch, 1, "С", _ctx.ForceSets, _ctx.PlateSections);
        var row2 = BatchRowTaskFactory.Create(batch, 2, "С", _ctx.ForceSets, _ctx.PlateSections);
        var other = BatchRowTaskFactory.Create(new CalcTask { Id = 10, Kind = "limit_force_batch", ForceSetId = BarSetId }, 2,
            "С", _ctx.ForceSets, _ctx.PlateSections);
        var manual = new CalcTask { Kind = "limit_force", ParamsJson = """{"solver":"bisection","N":0,"Mx":1,"My":0}""" };
        var tasks = new[] { manual, other, row1, row2 };

        Assert.Same(row2, BatchRowTaskFactory.FindExisting(tasks, batch, 2));
        Assert.Same(row1, BatchRowTaskFactory.FindExisting(tasks, batch, 1));
        Assert.Null(BatchRowTaskFactory.FindExisting(tasks, batch, 3));
    }

    [Fact]
    public void MissingRow_Throws()
    {
        var batch = new CalcTask { Id = 9, Kind = "limit_force_batch", SectionId = BarSectionId, ForceSetId = BarSetId };
        Assert.Throws<InvalidOperationException>(() =>
            BatchRowTaskFactory.Create(batch, 42, "С", _ctx.ForceSets, _ctx.PlateSections));
    }

    static JsonElement BatchRow(JsonElement root, int rowNum)
    {
        foreach (var name in new[] { "rows", "Rows" })
            if (root.TryGetProperty(name, out var rows))
                foreach (var r in rows.EnumerateArray())
                    if ((Find(r, "num") ?? Find(r, "Num")) is { ValueKind: JsonValueKind.Number } n && n.GetInt32() == rowNum)
                        return r;
        throw new Xunit.Sdk.XunitException($"строка № {rowNum} не найдена в результате пакета: {root}");
    }

    /// <summary>Свойство по пути «a.b.c» либо первое свойство с таким именем в глубину.</summary>
    static JsonElement? Find(JsonElement el, string name)
    {
        if (name.Contains('.'))
        {
            foreach (var part in name.Split('.'))
            {
                if (el.ValueKind != JsonValueKind.Object || !el.TryGetProperty(part, out el)) return null;
            }
            return el;
        }
        if (el.ValueKind == JsonValueKind.Object)
        {
            if (el.TryGetProperty(name, out var v)) return v;
            foreach (var p in el.EnumerateObject())
                if (Find(p.Value, name) is { } found) return found;
        }
        else if (el.ValueKind == JsonValueKind.Array)
            foreach (var item in el.EnumerateArray())
                if (Find(item, name) is { } found) return found;
        return null;
    }

    static void AssertSame(JsonElement expected, JsonElement actual, string what)
    {
        if (expected.ValueKind == JsonValueKind.Number)
        {
            Assert.Equal(JsonValueKind.Number, actual.ValueKind);
            double a = expected.GetDouble(), b = actual.GetDouble();
            // Пакет может стартовать Ньютон с соседней строки — допуск на точность решателя.
            Assert.True(Math.Abs(a - b) <= 1e-6 + 1e-4 * Math.Abs(a), $"{what}: пакет {a}, одиночная {b}");
        }
        else
            Assert.Equal(expected.ToString(), actual.ToString());
    }
}
