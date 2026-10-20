using System.Diagnostics;
using OpenCS.Services;
using OpenCS.Utilites;
using OpenCS.ViewModels;
using Xunit;
using Xunit.Abstractions;

namespace OpenCS.Tests;

/// <summary>
/// Ручной прогон мозаик ширины раскрытия трещин окна результата CSfea на базе с результатом секущего расчёта:
/// OPENCS_CSFEA_CRACK_DB — путь к базе (копируется во временный каталог; без переменной тест сразу выходит). Для
/// каждой постановки CSfea с результатом: время подготовки и счёта, число КЭ с трещинами, наибольшие a_crc,1 и a_crc
/// по граням, число штрихов линий трещин; затем то же при другой доле длительной части. У линейных постановок — ещё
/// и по каждой доступной комбинации (суперпозиция загружений).
/// </summary>
public sealed class FemCsfeaCrackWidthsManualTests(ITestOutputHelper output)
{
    [Fact]
    public async Task CrackWidthMosaics()
    {
        var source = Environment.GetEnvironmentVariable("OPENCS_CSFEA_CRACK_DB");
        if (string.IsNullOrWhiteSpace(source)) return;
        var root = Path.Combine(Path.GetTempPath(), $"opencs-csfea-crack-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        var path = Path.Combine(root, "crack.db");
        File.Copy(source, path);
        try
        {
            using var db = new DatabaseService(path);
            db.LoadAll();
            foreach (var schema in db.FemSchemas.ToList())
                foreach (var analysis in schema.Analyses.Where(a => a.Kind is FemCsfeaRunner.AnalysisKind or FemCsfeaRunner.LinearAnalysisKind && a.ResultId is > 0).ToList())
                {
                    if (db.GetCalcResultById(analysis.ResultId!.Value) is not { } result) continue;
                    var vm = new FemCsfeaResultVM(result, db, schema, analysis);
                    output.WriteLine($"«{schema.Tag}» / «{analysis.Tag}»: {vm.CurrentStepLabel}; мозаики a_crc: {vm.HasCrackWidths}, доля {vm.LongShare}");
                    if (!vm.HasCrackWidths || !vm.HasFields) continue;
                    double initial = vm.LongShare;
                    await Mosaics(vm, [initial, 0.5]);
                    if (!vm.CanShowCombinations) continue;
                    vm.ShowCombinations = true;
                    foreach (var item in vm.CombinationItems.Where(i => i.IsAvailable).ToList())
                    {
                        vm.SelectedCaseItem = item;
                        output.WriteLine($" комбинация «{item.Name}»");
                        await Mosaics(vm, [initial, 0.5]);
                    }
                }
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            try { Directory.Delete(root, true); } catch (IOException) { }
        }
    }

    async Task Mosaics(FemCsfeaResultVM vm, double[] shares)
    {
        int cracked = vm.Fields!.ShellIds.Count(id => vm.ShellFieldValue(id, FemCsfeaShellField.State) >= 1);
        output.WriteLine($"  КЭ с трещиной по состоянию расчёта: {cracked}");
        foreach (double share in shares)
        {
            vm.LongShare = share;
            foreach (var field in vm.ShellFields.Where(f => f.Value >= FemCsfeaShellField.AcrcLongBottom))
            {
                var clock = Stopwatch.StartNew();
                if (!vm.PrepareShellField(field.Value)) await vm.CrackWidthsTask;
                var values = vm.Fields!.ShellIds.Select(id => vm.ShellFieldValue(id, field.Value)).OfType<double>().ToList();
                double max = values.DefaultIfEmpty(0).Max();
                int strokes = vm.CrackStrokes(field.Value, vm.Fields.ShellIds, _ => default).Count / 2;
                output.WriteLine($"  доля {share}: {field.Label} — {clock.Elapsed.TotalSeconds:0.00} с, КЭ со значением {values.Count}, с трещиной {values.Count(v => v > 0)}, " +
                                 $"наибольшее {max:0.000} мм, штрихов {strokes}");
            }
        }
    }
}
