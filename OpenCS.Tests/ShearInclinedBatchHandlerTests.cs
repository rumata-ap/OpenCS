using System.Text.Json;
using CScore;
using OpenCS.Tasks;
using OpenCS.Utilites;
using Xunit;

namespace OpenCS.Tests;

/// <summary>Пакетный расчёт наклонных сечений по набору усилий.</summary>
public sealed class ShearInclinedBatchHandlerTests
{
    [Fact]
    public void Run_WithoutDatabase_ReturnsError()
    {
        var handler = new ShearInclinedBatchHandler();
        var task = new CalcTask { Id = 1, Kind = "shear_inclined_batch", CalcType = CalcType.C };

        var result = handler.Run(task, ShearInclinedFixtures.Beam(),
            new LoadItem(), CalcSettings.Default);

        Assert.Equal("error", result.Status);
    }

    [Fact]
    public void Run_ProducesRowPerLoadItemOrderedByUtilization()
    {
        string path = TempPath();
        try
        {
            using var database = new DatabaseService(path);
            var forceSet = new ForceSet
            {
                Id = 5,
                Kind = "bar",
                Tag = "РСУ-1",
                Items =
                [
                    new LoadItem { Num = 1, Label = "оп. A", Vy = 200.0, Mx = -120.0 },
                    new LoadItem { Num = 2, Label = "оп. B", Vy = 90.0, Mx = -60.0 }
                ]
            };
            database.ForceSets.Add(forceSet);

            var task = new CalcTask
            {
                Id = 1,
                Kind = "shear_inclined_batch",
                CalcType = CalcType.C,
                ForceSetId = 5,
                ParamsJson = new ShearInclinedParams
                {
                    ConstructiveRequirements103Confirmed = true
                }.ToJson()
            };

            var result = new ShearInclinedBatchHandler().Run(
                task, ShearInclinedFixtures.Beam(), new LoadItem(), CalcSettings.Default,
                new TaskRunContext { Database = database });

            Assert.Equal("ok", result.Status);
            using var doc = JsonDocument.Parse(result.DataJson);
            var rows = doc.RootElement.GetProperty("rows").EnumerateArray().ToList();

            Assert.Equal(2, rows.Count);
            Assert.Equal("оп. A", rows[0].GetProperty("label").GetString());
            Assert.True(rows[0].GetProperty("utilization").GetDouble()
                      > rows[1].GetProperty("utilization").GetDouble());
            // У этой балки вердикт определяет упрощённое условие (8.60): его нижняя оценка
            // несущей способности (134 кН) жёстче точного расчёта по (8.56) (225 кН).
            Assert.Equal("8.60", rows[0].GetProperty("worstFormula").GetString());
            Assert.Equal("ok", doc.RootElement.GetProperty("utilizationStatus").GetString());
        }
        finally { TryDelete(path); }
    }

    static string TempPath() =>
        Path.Combine(Path.GetTempPath(), $"opencs-shear-batch-{Guid.NewGuid():N}.db");

    static void TryDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); } catch { }
    }
}
