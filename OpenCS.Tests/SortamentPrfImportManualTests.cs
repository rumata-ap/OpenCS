using CScore.Import;
using CScore.Sp16;
using Microsoft.Data.Sqlite;
using OpenCS.Utilites;
using Xunit;
using Xunit.Abstractions;

namespace OpenCS.Tests;

/// <summary>
/// Пополнение Sortamenty.db3 сортаментом SCAD (<see cref="SortamentPrfImporter"/>). OPENCS_SCAD_PRF_DIR — каталог
/// PRF; без переменной тест сразу выходит. Работа идёт на копии базы; OPENCS_SORTAMENT_WRITE=1 — после проверок
/// результат записывается в <c>OpenCS/DataSource/Sortamenty.db3</c> репозитория.
/// </summary>
public class SortamentPrfImportManualTests(ITestOutputHelper output)
{
    static string RepoDb()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "OpenCS.sln"))) dir = dir.Parent;
        return Path.Combine(dir!.FullName, "OpenCS", "DataSource", "Sortamenty.db3");
    }

    [Fact]
    public void ImportScadTables()
    {
        string? prfDir = Environment.GetEnvironmentVariable("OPENCS_SCAD_PRF_DIR");
        if (string.IsNullOrWhiteSpace(prfDir)) return;
        string copy = Path.Combine(Path.GetTempPath(), $"opencs-sortament-{Guid.NewGuid():N}.db3");
        File.Copy(RepoDb(), copy);
        try
        {
            string log = Path.Combine(Path.GetTempPath(), "opencs-sortament-import.log");
            File.WriteAllText(log, "");
            var watch = System.Diagnostics.Stopwatch.StartNew();
            var report = SortamentPrfImporter.Import(copy, prfDir, line =>
            {
                output.WriteLine(line);
                File.AppendAllText(log, $"{watch.Elapsed:mm\\:ss} {line}\n");
            });
            foreach (string s in report.Subtypes) output.WriteLine("+ " + s);
            output.WriteLine($"строк {report.Rows}, пропущено {report.Skipped.Count}");
            foreach (string s in report.Skipped) output.WriteLine("  пропуск: " + s);

            // Повторный запуск не задваивает.
            var again = SortamentPrfImporter.Import(copy, prfDir);
            Assert.Equal(report.Rows, again.Rows);
            Assert.Equal(report.Subtypes.Count, again.Subtypes.Count);

            var db = new ProfileDB(copy);
            var tube = db.Find(new ImportedSteelShape(SteelProfileKind.Box, SteelFabrication.Bent,
                0.1, 0.1, 0.004, 0.004, 0.004, 0, 0, "ГОСТ 30245-2012", "100x4"));
            Assert.NotNull(tube);
            Assert.Contains("30245-2012", tube!.Standard);
            output.WriteLine($"100x4 ГОСТ 30245-2012: It = {tube.It * 1e8:0.##} см⁴");

            var boxes = db.GetSteelCatalogSubtypes(SteelProfileKind.Box, SteelFabrication.Bent);
            Assert.Contains(boxes, s => s.Name.Contains("30245-2012"));
            var beams = db.GetSteelCatalogSubtypes(SteelProfileKind.IBeam, SteelFabrication.Rolled);
            var tu = beams.First(s => s.Name.Contains("24107-016"));
            var item = db.GetSteelCatalogProfiles(tu.Id).First();
            var entry = db.GetSteelCatalogEntry(tu.Id, item.Id)!;
            Assert.True(entry.ItCm4 > 0);
            Assert.True(entry.ACm2 > 0);
            output.WriteLine($"{entry.Name} {entry.Standard}: A = {entry.ACm2} см², It = {entry.ItCm4:0.###} см⁴");
            // Строки старого формата GetProfile читаются и для новых подтипов.
            Assert.NotNull(db.GetProfile("Двутавры", item.Id));

            SqliteConnection.ClearAllPools();
            if (Environment.GetEnvironmentVariable("OPENCS_SORTAMENT_WRITE") == "1")
            {
                File.Copy(copy, RepoDb(), overwrite: true);
                output.WriteLine("записано в " + RepoDb());
            }
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            try { File.Delete(copy); } catch (IOException) { }
        }
    }
}
