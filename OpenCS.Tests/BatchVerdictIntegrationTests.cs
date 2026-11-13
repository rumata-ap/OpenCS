using System.IO;
using System.Text.Json;
using CScore;
using CScore.Batch;
using OpenCS.Tasks;
using OpenCS.Utilites;
using Xunit;
using Xunit.Abstractions;

namespace OpenCS.Tests;

/// <summary>
/// Причина и сводка на реальном пакете НДМ (сечение IV.Г.6.1 Пособия Краковского, Mult ≈ 772 кН·м при
/// N = 1700 кН): verdict строки согласован со старыми полями, сводка — со строками, статус пакета — с причинами.
/// </summary>
public sealed class BatchVerdictIntegrationTests(ITestOutputHelper output)
{
    [Fact]
    public void StrengthNdmBatch_VerdictsAgreeWithRowsAndSummary()
    {
        string path = Path.Combine(Path.GetTempPath(), $"opencs-verdict-{Guid.NewGuid():N}.db");
        try
        {
            using var database = new DatabaseService(path);
            database.ForceSets.Add(new ForceSet
            {
                Id = 1, Kind = "bar", Tag = "IV.Г.6.1",
                Items =
                [
                    new LoadItem { Num = 1, Label = "382", N = -1700.0, Mx = 382.0 },
                    new LoadItem { Num = 2, Label = "760", N = -1700.0, Mx = 760.0 },
                    new LoadItem { Num = 3, Label = "900", N = -1700.0, Mx = 900.0 },
                ]
            });
            var task = new CalcTask { Id = 1, Kind = "strength_ndm_batch", CalcType = CalcType.C, ForceSetId = 1 };

            var result = new StrengthNDMBatchHandler().Run(task, StrengthNdmBatchPrestressKrakovskyTests.Section(),
                new LoadItem(), CalcSettings.Default, new TaskRunContext { Database = database });

            using var doc = JsonDocument.Parse(result.DataJson);
            var root = doc.RootElement;
            var rows = root.GetProperty("rows").EnumerateArray().ToList();
            var verdicts = new List<BatchRowVerdict>();
            foreach (var row in rows)
            {
                var v = BatchRowVerdict.TryRead(row);
                Assert.NotNull(v);
                output.WriteLine($"{row.GetProperty("label").GetString()}: {v!.Status} {v.Util} {v.Code} — {v.Text}");
                // Новая причина и старые поля говорят одно и то же.
                string old = row.GetProperty("status").GetString()!;
                if (old == "not_converged") Assert.Equal(BatchRowStatus.NotConverged, v.Status);
                else Assert.Equal(row.GetProperty("strength_ok").GetBoolean(), v.IsPassed);
                // Читатель отдаёт сохранённую причину без изменений.
                Assert.Equal(v, BatchRowVerdictReader.Read(task.Kind, row));
                verdicts.Add(v);
            }

            Assert.Equal(BatchRowStatus.Ok, verdicts[0].Status);
            Assert.Equal(BatchVerdictSummary.PackageStatus(verdicts), result.Status);

            var summary = root.GetProperty("verdict_summary");
            Assert.Equal(verdicts.Count(v => v.Status == BatchRowStatus.Ok), summary.GetProperty("ok").GetInt32());
            Assert.Equal(verdicts.Count(v => v.Status == BatchRowStatus.NotPassed), summary.GetProperty("not_passed").GetInt32());
            Assert.Equal(verdicts.Count(v => v.Status == BatchRowStatus.NotConverged), summary.GetProperty("not_converged").GetInt32());
            int? g = BatchVerdictSummary.GoverningIndex(verdicts);
            Assert.Equal(g is { } i ? i + 1 : (int?)null,
                summary.GetProperty("governing_num").ValueKind == JsonValueKind.Number ? summary.GetProperty("governing_num").GetInt32() : null);
            // 900 кН·м заведомо выше Mult — строка не проходит или не сходится, но не «ok».
            Assert.NotEqual(BatchRowStatus.Ok, verdicts[2].Status);
        }
        finally { try { File.Delete(path); } catch { } }
    }
}
