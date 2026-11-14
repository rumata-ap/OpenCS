using System.IO;
using System.Text.Json;
using CScore;
using CScore.Batch;
using CScore.ParametricSteel;
using OpenCS.Tasks;
using OpenCS.Utilites;
using Xunit;

namespace OpenCS.Tests;

/// <summary>
/// Пакет проверок СП 16 по набору усилий: строка пакета = одиночная steel_check той же строки
/// (Кисп, вид проверки), группы Кисп (прочность, общая и местная устойчивость, гибкость), сводка и задача по строке.
/// </summary>
public sealed class SteelCheckBatchHandlerTests
{
    /// <summary>Колонна 30К1 по ГОСТ Р 57837-2017 (h = 298, b = 299, tw = 9, tf = 14, r = 18 мм), С255: Ry = 240 МПа.</summary>
    static CrossSection Column()
    {
        var definition = ParametricSteelSectionDefinition.RolledIBeam(0.298, 0.299, 0.009, 0.014, 0.018) with { MaterialId = 1 };
        var generated = ParametricSteelSectionGenerator.Generate(definition);
        Assert.Empty(generated.Diagnostics);
        var section = generated.Section;
        section.Tag = "30К1";
        section.Areas[0].Material = new Material
        {
            Id = 1, Tag = "С255", Type = MatType.Steel, E = 2.06e8,
            MaterialChars = [new MaterialChars(CalcType.C) { Ry = 240000, Ru = 360000 }]
        };
        section.ParametricSteel = new ParametricSteelBinding(generated.Profile!,
            ParametricSteelSectionFingerprint.Compute(section, ParametricSteelSectionGenerator.GeneratorVersion),
            ParametricSteelSectionGenerator.GeneratorVersion);
        return section;
    }

    const string Params = """{"LefX":6,"LefY":6,"LefB":6}""";

    static ForceSet Set() => new()
    {
        Id = 1, Kind = "bar", Tag = "Колонны",
        Items =
        [
            new LoadItem { Id = 11, Num = 1, Label = "Эл. 1, НС 5", N = -176, Mx = -96.901, Vy = 24.202 },
            new LoadItem { Id = 12, Num = 2, Label = "Эл. 1, НС 3", N = -78, Mx = -13.439, Vy = 13.389 },
            new LoadItem { Id = 13, Num = 3, Label = "перегруз", N = -4000, Mx = -300, Vy = 50 },
        ]
    };

    [Fact]
    public void RowsMatchSingleChecksAndGoverningIsOverloadedRow()
    {
        string path = Path.Combine(Path.GetTempPath(), $"opencs-steel-batch-{Guid.NewGuid():N}.db");
        try
        {
            using var database = new DatabaseService(path);
            var set = Set();
            database.ForceSets.Add(set);
            var section = Column();
            var batch = new CalcTask { Id = 7, Tag = "Колонны", Kind = "steel_check_batch", CalcType = CalcType.C,
                SectionId = 1, ForceSetId = 1, ParamsJson = Params };
            var ctx = new TaskRunContext { Database = database };

            var result = TaskRunner.Run(batch, section, new LoadItem(), CalcSettings.Default, ctx);

            Assert.Equal("not_passed", result.Status);
            using var doc = JsonDocument.Parse(result.DataJson);
            var root = doc.RootElement;
            var rows = root.GetProperty("rows").EnumerateArray().ToList();
            Assert.Equal(3, rows.Count);
            Assert.Equal(3, root.GetProperty("governing_num").GetInt32());
            Assert.Equal("30К1", root.GetProperty("sectionTag").GetString());
            Assert.Equal(6, root.GetProperty("context").GetProperty("lefX").GetDouble());

            for (int i = 0; i < rows.Count; i++)
            {
                var single = TaskRunner.Run(new CalcTask { Kind = "steel_check", ParamsJson = Params }, section,
                    set.Items[i], CalcSettings.Default, ctx);
                using var sdoc = JsonDocument.Parse(single.DataJson);
                double util = sdoc.RootElement.GetProperty("utilization").GetDouble();
                var verdict = BatchRowVerdict.TryRead(rows[i])!;
                Assert.Equal(Math.Round(util, 4), verdict.Util!.Value, 4);
                Assert.Equal(single.Status, rows[i].GetProperty("status").GetString());
                Assert.Equal(sdoc.RootElement.GetProperty("effectiveKind").GetString(),
                    rows[i].GetProperty("effective_kind").GetString());
                Assert.StartsWith("СП 16.13330.2017, п. ", verdict.Ref);
            }

            // Сжатый изгибаемый двутавр: местная устойчивость стенки и пояса по 9.4 — отдельной группой.
            var first = rows[0];
            Assert.Equal(JsonValueKind.Number, first.GetProperty("util_local").ValueKind);
            Assert.StartsWith("9.4", first.GetProperty("clause_local").GetString());
            Assert.Equal(JsonValueKind.Number, first.GetProperty("util_strength").ValueKind);
            Assert.Equal(JsonValueKind.Number, first.GetProperty("util_stability").ValueKind);
            Assert.False(SteelCheckBatchHandler.IsLocalStability(first.GetProperty("clause_stability").GetString()!));
            Assert.Equal(BatchRowStatus.Ok, BatchRowVerdict.TryRead(first)!.Status);
            Assert.Equal(BatchRowStatus.NotPassed, BatchRowVerdict.TryRead(rows[2])!.Status);
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            try { File.Delete(path); } catch (IOException) { }
        }
    }

    [Fact]
    public void RowTaskIsSingleSteelCheckWithBatchParams()
    {
        var batch = new CalcTask { Id = 7, Kind = "steel_check_batch", SectionId = 3, ForceSetId = 1, ParamsJson = Params };

        var task = BatchRowTaskFactory.Create(batch, 2, "30К1", [Set()], []);

        Assert.Equal("steel_check", task.Kind);
        Assert.Equal(12, task.ForceItemId);
        Assert.Equal(3, task.SectionId);
        Assert.Equal(6, CScore.Sp16.SteelDesignParams.Parse(task.ParamsJson).LefB);
    }

    [Fact]
    public void MissingForceSetIsError()
    {
        string path = Path.Combine(Path.GetTempPath(), $"opencs-steel-batch-{Guid.NewGuid():N}.db");
        try
        {
            using var database = new DatabaseService(path);
            var result = TaskRunner.Run(new CalcTask { Kind = "steel_check_batch", ForceSetId = 99, ParamsJson = Params },
                Column(), new LoadItem(), CalcSettings.Default, new TaskRunContext { Database = database });
            Assert.Equal("error", result.Status);
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            try { File.Delete(path); } catch (IOException) { }
        }
    }
}
