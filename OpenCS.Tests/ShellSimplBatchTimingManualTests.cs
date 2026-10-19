using System.Diagnostics;
using System.IO;
using CScore;
using OpenCS.Tasks;
using OpenCS.Utilites;
using Xunit;
using Xunit.Abstractions;

namespace OpenCS.Tests;

/// <summary>
/// Ручной замер пакета упрощённой проверки пластин и одиночной задачи по его строке на копии проекта
/// пользователя: OPENCS_BATCH_TIMING_DB — путь к .db, OPENCS_BATCH_TIMING_TASK — Num пакетной задачи,
/// OPENCS_BATCH_TIMING_ROWS — сколько первых строк набора оставить (по умолчанию 8). Без переменной тест пуст.
/// </summary>
public sealed class ShellSimplBatchTimingManualTests(ITestOutputHelper output)
{
    [Fact]
    public async Task BatchSequentialParallel_AndSingleRow()
    {
        var source = Environment.GetEnvironmentVariable("OPENCS_BATCH_TIMING_DB");
        if (string.IsNullOrWhiteSpace(source)) return;
        int taskNum = int.TryParse(Environment.GetEnvironmentVariable("OPENCS_BATCH_TIMING_TASK"), out var t) ? t : 20;
        int keep = int.TryParse(Environment.GetEnvironmentVariable("OPENCS_BATCH_TIMING_ROWS"), out var k) ? k : 8;

        var root = Path.Combine(Path.GetTempPath(), $"opencs-batch-timing-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        var path = Path.Combine(root, "timing.db");
        File.Copy(source, path);
        try
        {
            using (var full = new DatabaseService(path))
            {
                full.LoadAll();
                var b = full.CalcTasks.Single(x => x.Num == taskNum);
                var fs = full.ForceSets.Single(f => f.Id == b.ForceSetId);
                var swLoad = Stopwatch.StartNew();
                int n = fs.ShellItems.Count + fs.Items.Count;
                output.WriteLine($"загрузка всех строк набора «{fs.Tag}»: {n} строк, {swLoad.Elapsed.TotalSeconds:F2} с");
            }
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();

            using (var conn = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={path}"))
            {
                conn.Open();
                var cmd = conn.CreateCommand();
                // Только первые строки набора пакета — полный РСУ считается слишком долго.
                cmd.CommandText = """
                    DELETE FROM force_shell_items
                    WHERE set_id = (SELECT force_set_id FROM calc_tasks WHERE num = $num)
                      AND id NOT IN (SELECT id FROM force_shell_items
                                     WHERE set_id = (SELECT force_set_id FROM calc_tasks WHERE num = $num)
                                     ORDER BY num LIMIT $keep);
                    """;
                cmd.Parameters.AddWithValue("$num", taskNum);
                cmd.Parameters.AddWithValue("$keep", keep);
                output.WriteLine($"удалено строк РСУ: {cmd.ExecuteNonQuery()}");
            }
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();

            using var db = new DatabaseService(path);
            db.LoadAll();
            var ctx = new TestProjectContext(db);
            ctx.ForceSetList.AddRange(db.ForceSets);
            var batch = db.CalcTasks.Single(x => x.Num == taskNum);
            int rows = db.ForceSets.Single(f => f.Id == batch.ForceSetId).ShellItems.Count;
            output.WriteLine($"задача № {taskNum} «{batch.Kind}», строк: {rows}");

            foreach (bool parallel in new[] { false, true })
            {
                ctx.CalcSettings = new CalcSettings { BatchParallel = parallel };
                var sw = Stopwatch.StartNew();
                var r = await CalcTaskExecutor.RunAsync(ctx, batch);
                output.WriteLine($"пакет, многопоточно = {parallel}: {sw.Elapsed.TotalSeconds:F2} с, статус {r?.Status}");
            }

            var first = db.ForceSets.Single(f => f.Id == batch.ForceSetId).ShellItems.OrderBy(i => i.Num).First();
            var plateTag = db.PlateSections.FirstOrDefault(s => s.Id == batch.SectionId)?.Tag ?? "?";
            var single = BatchRowTaskFactory.Create(batch, first.Num, plateTag, ctx.ForceSets, ctx.PlateSections);
            single.Num = db.CalcTasks.Max(x => x.Num) + 1;
            db.SaveCalcTask(single);
            var swSingle = Stopwatch.StartNew();
            var sr = await CalcTaskExecutor.RunAsync(ctx, single);
            output.WriteLine($"одиночная по строке № {first.Num}: {swSingle.Elapsed.TotalSeconds:F2} с, " +
                             $"статус {sr?.Status}, JSON {sr?.DataJson.Length / 1024.0:F0} КБ");
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            try { Directory.Delete(root, true); } catch (IOException) { }
        }
    }
}
