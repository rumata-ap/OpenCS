using CScore;
using Microsoft.Data.Sqlite;
using OpenCS.Utilites;

namespace OpenCS.OpenSees.Tests;

/// <summary>Строки наборов усилий читаются из БД по требованию, а не при открытии проекта.</summary>
public sealed class ForceSetLazyRowsTests
{
    [Fact]
    public void Rows_AreLoadedOnDemand_AndSurviveHeaderOnlySave()
    {
        string path = Path.Combine(Path.GetTempPath(), $"opencs-lazy-rows-{Guid.NewGuid():N}.db");
        try
        {
            using (var db = new DatabaseService(path))
            {
                var shell = new ForceSet
                {
                    Tag = "РСУ (C)", Kind = "shell", SourceType = "fea",
                    ShellItems =
                    [
                        new ShellLoadItem { Label = "э.1 с1", Mx = -10, SourceElementNum = 1, SourceSectionNum = 1 },
                        new ShellLoadItem { Label = "э.1 с1 к2", Mx = -20, SourceElementNum = 1, SourceSectionNum = 1 },
                        new ShellLoadItem { Label = "э.2 с1", My = 5, SigmaX = 100, SourceElementNum = 2 },
                        new ShellLoadItem { Label = "ручная", Nx = 1 },
                    ],
                };
                var bar = new ForceSet { Tag = "Стержни", Items = [new LoadItem { Label = "1", N = -100, SourceElementNum = 7 }] };
                db.SaveForceSet(shell);
                db.SaveForceSet(bar);
                Assert.True(shell.RowsLoaded);
                Assert.True(shell.UnloadRows());     // после записи набор связан с БД
                Assert.False(shell.RowsLoaded);
                Assert.Equal(4, shell.RowCount);
                Assert.Equal(-20, shell.ShellItems[1].Mx);   // обращение читает строки заново
            }

            using (var db = new DatabaseService(path))
            {
                db.LoadAll();
                var shell = db.ForceSets.Single(f => f.Tag == "РСУ (C)");
                var bar = db.ForceSets.Single(f => f.Tag == "Стержни");
                Assert.False(shell.RowsLoaded);
                Assert.Equal(4, shell.RowCount);
                Assert.Equal(1, bar.RowCount);

                // Статистика по КЭ — без загрузки строк.
                var stats = shell.ElementStats(shell: true);
                Assert.False(shell.RowsLoaded);
                Assert.Equal(2, stats.ByElement[1]);
                Assert.Equal(1, stats.ByElement[2]);
                Assert.Equal(1, stats.WithoutElement);
                Assert.Equal(4, stats.Total);
                Assert.False(shell.ElementStats(shell: false).HasElementRows);
                Assert.Equal(1, bar.ElementStats(shell: false).ByElement[7]);

                // Переименование без загрузки строк не трогает их в БД.
                shell.Tag = "РСУ (C) — плита";
                shell.IsModified = true;
                db.SaveForceSet(shell);
                Assert.False(shell.RowsLoaded);
                Assert.False(shell.IsModified);
                Assert.Equal(4, Count(path, "SELECT COUNT(*) FROM force_shell_items"));

                // Загрузка: строки целиком, в порядке номеров.
                Assert.Equal(["э.1 с1", "э.1 с1 к2", "э.2 с1", "ручная"], shell.ShellItems.Select(i => i.Label));
                Assert.Equal(100, shell.ShellItems[2].SigmaX);
                Assert.Null(shell.ShellItems[3].SourceElementNum);
                Assert.True(shell.RowsLoaded);

                // Изменённый или закреплённый редактором набор не выгружается.
                shell.IsModified = true;
                Assert.False(shell.UnloadRows());
                shell.IsModified = false;
                shell.PinRows();
                Assert.False(shell.UnloadRows());
                Assert.True(bar.UnloadRows() || !bar.RowsLoaded);
            }
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Fact]
    public void Rows_LoadFromBackgroundThreads()
    {
        string path = Path.Combine(Path.GetTempPath(), $"opencs-lazy-rows-mt-{Guid.NewGuid():N}.db");
        try
        {
            using (var db = new DatabaseService(path))
                db.SaveForceSet(new ForceSet
                {
                    Tag = "Набор", Kind = "shell",
                    ShellItems = [.. Enumerable.Range(1, 500).Select(i => new ShellLoadItem { Label = $"{i}", Mx = i, SourceElementNum = i })],
                });

            using (var db = new DatabaseService(path))
            {
                db.LoadAll();
                var fs = db.ForceSets.Single();
                // Несколько потоков сразу: строки читаются один раз, все видят один и тот же список.
                var lists = new List<ShellLoadItem>[8];
                Parallel.For(0, lists.Length, i => lists[i] = fs.ShellItems);
                Assert.All(lists, l => Assert.Same(lists[0], l));
                Assert.Equal(500, lists[0].Count);
            }
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            if (File.Exists(path)) File.Delete(path);
        }
    }

    static int Count(string path, string sql)
    {
        using var connection = new SqliteConnection($"Data Source={path};Pooling=False");
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        return (int)(long)command.ExecuteScalar()!;
    }
}
