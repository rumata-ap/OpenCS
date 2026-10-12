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
/// по граням, число штрихов линий трещин; затем то же при другой доле длительной части.
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
                foreach (var analysis in schema.Analyses.Where(a => a.Kind == FemCsfeaRunner.AnalysisKind && a.ResultId is > 0).ToList())
                {
                    if (db.GetCalcResultById(analysis.ResultId!.Value) is not { } result) continue;
                    var vm = new FemCsfeaResultVM(result, db, schema);
                    output.WriteLine($"«{schema.Tag}» / «{analysis.Tag}»: {vm.CurrentStepLabel}; мозаики a_crc: {vm.HasCrackWidths}, доля {vm.LongShare}");
                    if (!vm.HasCrackWidths || !vm.HasFields) continue;
                    foreach (double share in new[] { vm.LongShare, 0.5 })
                    {
                        vm.LongShare = share;
                        foreach (var field in vm.ShellFields.Where(f => f.Value >= FemCsfeaShellField.AcrcLongBottom))
                        {
                            var clock = Stopwatch.StartNew();
                            if (!vm.PrepareShellField(field.Value)) await vm.CrackWidthsTask;
                            var values = vm.Fields!.ShellIds.Select(id => vm.ShellFieldValue(id, field.Value)).OfType<double>().ToList();
                            double max = values.DefaultIfEmpty(0).Max();
                            int strokes = vm.CrackStrokes(field.Value, vm.Fields.ShellIds, _ => default).Count / 2;
                            output.WriteLine($"  доля {share}: {field.Label} — {clock.Elapsed.TotalSeconds:0.00} с, КЭ со значением {values.Count}, " +
                                             $"наибольшее {max:0.000} мм, штрихов {strokes}");
                        }
                    }
                }
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            try { Directory.Delete(root, true); } catch (IOException) { }
        }
    }
}
