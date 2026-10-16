using System.Diagnostics;
using OpenCS.Services;
using OpenCS.Utilites;
using OpenCS.ViewModels;
using Xunit;
using Xunit.Abstractions;

namespace OpenCS.Tests;

/// <summary>
/// Ручной прогон комбинаций линейного результата CSfea на реальной базе: OPENCS_CSFEA_COMBINATIONS_DB — путь к базе
/// (копируется во временный каталог; без переменной тест сразу выходит). Для каждой линейной постановки с результатом:
/// число загружений и комбинаций (доступных), время первой и повторной сборки комбинации, наибольший |uz| и сверка
/// суперпозиции с ручной суммой по одному узлу.
/// </summary>
public sealed class FemCsfeaCombinationsManualTests(ITestOutputHelper output)
{
    [Fact]
    public void LinearCombinations()
    {
        var source = Environment.GetEnvironmentVariable("OPENCS_CSFEA_COMBINATIONS_DB");
        if (string.IsNullOrWhiteSpace(source)) return;
        var root = Path.Combine(Path.GetTempPath(), $"opencs-csfea-comb-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        var path = Path.Combine(root, "comb.db");
        File.Copy(source, path);
        try
        {
            using var db = new DatabaseService(path);
            db.LoadAll();
            foreach (var schema in db.FemSchemas.ToList())
                foreach (var analysis in schema.Analyses.Where(a => a.Kind == FemCsfeaRunner.LinearAnalysisKind && a.ResultId is > 0).ToList())
                {
                    if (db.GetCalcResultById(analysis.ResultId!.Value) is not { } result) continue;
                    var clock = Stopwatch.StartNew();
                    var vm = new FemCsfeaResultVM(result, db, schema, analysis);
                    output.WriteLine($"«{schema.Tag}» / «{analysis.Tag}»: загружений {vm.LoadCaseItems.Count}, комбинаций " +
                                     $"{vm.CombinationItems.Count} (доступно {vm.CombinationItems.Count(i => i.IsAvailable)}), VM {clock.Elapsed.TotalSeconds:0.00} с");
                    foreach (var item in vm.CombinationItems.Where(i => !i.IsAvailable).Take(3))
                        output.WriteLine($"  серая: {item.Name} — {item.Source.DisabledReason}");
                    if (!vm.CanShowCombinations) continue;

                    clock.Restart();
                    vm.ShowCombinations = true;
                    double first = clock.Elapsed.TotalSeconds;
                    var comb = vm.SelectedCaseItem!.Combination!;
                    clock.Restart();
                    vm.NextCaseCommand.Execute(null);
                    double next = clock.Elapsed.TotalSeconds;
                    vm.PrevCaseCommand.Execute(null);
                    var f = vm.Fields!;
                    int i = Enumerable.Range(0, f.NodeIds.Length).MaxBy(k => Math.Abs(f.Displacements[6 * k + 2]));
                    double manual = comb.Terms.Sum(t => t.Factor * vm.LoadFields(t.StepN)!.Displacements[6 * i + 2]);
                    output.WriteLine($"  «{vm.SelectedCaseItem.Name}» = {vm.SelectedCaseItem.Formula}: первая {first:0.00} с, следующая {next:0.000} с; " +
                                     $"узел {f.NodeIds[i]} uz = {f.Displacements[6 * i + 2] * 1e3:0.###} мм (сумма вручную {manual * 1e3:0.###}); " +
                                     $"{vm.ReactionSummaryText}");
                    Assert.Equal(manual, f.Displacements[6 * i + 2], 12);
                    clock.Restart();
                    var sum = FemCsfeaCombinationFields.Superpose(comb.Terms.Select(t => (vm.LoadFields(t.StepN)!, t.Factor)).ToList());
                    output.WriteLine($"  суперпозиция одна: {clock.Elapsed.TotalSeconds:0.000} с, узлов {sum.NodeIds.Length}, пластин {sum.ShellIds.Length}, стержней {sum.BeamIds.Length}");
                    Assert.Equal(2, vm.BuildForceSets([]).Count);
                    // Все загружения прочитаны — дальше переключение только суммирует.
                    var times = new List<double>();
                    for (int k = 0; k < 10; k++)
                    {
                        clock.Restart();
                        vm.NextCaseCommand.Execute(null);
                        times.Add(clock.Elapsed.TotalSeconds);
                    }
                    output.WriteLine($"  10 переключений: наибольшее {times.Max():0.000} с, среднее {times.Average():0.000} с");
                }
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            try { Directory.Delete(root, true); } catch (IOException) { }
        }
    }

    /// <summary>
    /// Огибающие групп (та же переменная OPENCS_CSFEA_COMBINATIONS_DB): время расчёта каждой, повторный показ из кеша,
    /// сверка uz наибольшего по модулю узла и Mx наибольшей пластины с перебором суперпозиций группы.
    /// </summary>
    [Fact]
    public async Task LinearEnvelopes()
    {
        var source = Environment.GetEnvironmentVariable("OPENCS_CSFEA_COMBINATIONS_DB");
        if (string.IsNullOrWhiteSpace(source)) return;
        var root = Path.Combine(Path.GetTempPath(), $"opencs-csfea-env-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        var path = Path.Combine(root, "env.db");
        File.Copy(source, path);
        try
        {
            using var db = new DatabaseService(path);
            db.LoadAll();
            foreach (var schema in db.FemSchemas.ToList())
                foreach (var analysis in schema.Analyses.Where(a => a.Kind == FemCsfeaRunner.LinearAnalysisKind && a.ResultId is > 0).ToList())
                {
                    if (db.GetCalcResultById(analysis.ResultId!.Value) is not { } result) continue;
                    var vm = new FemCsfeaResultVM(result, db, schema, analysis);
                    output.WriteLine($"«{schema.Tag}» / «{analysis.Tag}»: огибающих {vm.EnvelopeItems.Count}");
                    foreach (var item in vm.EnvelopeItems)
                    {
                        var clock = Stopwatch.StartNew();
                        vm.SelectedCaseItem = item;
                        await vm.EnvelopeTask;
                        double first = clock.Elapsed.TotalSeconds;
                        Assert.True(vm.HasFields, vm.FieldsNote);
                        vm.SelectedStepIndex = 0;
                        clock.Restart();
                        vm.SelectedCaseItem = item;
                        double cached = clock.Elapsed.TotalSeconds;
                        Assert.True(vm.HasFields);

                        var f = vm.Fields!;
                        int node = f.NodeIds[Enumerable.Range(0, f.NodeIds.Length).MaxBy(k => Math.Abs(f.Displacements[6 * k + 2]))];
                        var uz = vm.EnvelopeNode(node, 2)!.Value;
                        var sums = item.Envelope!.Combinations.Select(c => c.Combination!.Terms
                            .Sum(t => t.Factor * NodeUz(vm.LoadFields(t.StepN)!, node))).ToList();
                        Assert.Equal(sums.Max(), uz.Max, 9);
                        Assert.Equal(sums.Min(), uz.Min, 9);
                        string mx = "";
                        if (f.ShellIds.Length > 0)
                        {
                            int shell = f.ShellIds[Enumerable.Range(0, f.ShellIds.Length).MaxBy(k => Math.Abs(f.ShellForces[8 * k + 3]))];
                            var m = vm.EnvelopeShell(shell, FemCsfeaShellField.Mx)!.Value;
                            var ms = item.Envelope.Combinations.Select(c => c.Combination!.Terms
                                .Sum(t => t.Factor * ShellMx(vm.LoadFields(t.StepN)!, shell)) / 1e3).ToList();
                            Assert.Equal(ms.Max(), m.Max, 6);
                            Assert.Equal(ms.Min(), m.Min, 6);
                            mx = $"; КЭ {shell} Mx max {m.Max:0.###} ({m.MaxSource}), min {m.Min:0.###} ({m.MinSource}) кН·м/м";
                        }
                        output.WriteLine($"  {item.Name} ({item.Envelope.Combinations.Count} комб., пропущено {item.Envelope.Unavailable}): " +
                                         $"расчёт {first:0.00} с, из кеша {cached:0.000} с; узел {node} uz max {uz.Max * 1e3:0.###} ({uz.MaxSource}), " +
                                         $"min {uz.Min * 1e3:0.###} ({uz.MinSource}) мм{mx}");
                    }
                }
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            try { Directory.Delete(root, true); } catch (IOException) { }
        }

        static double NodeUz(CSfea.CScoreBridge.Structural.RcSecantStepFields f, int node) =>
            f.Displacements[6 * Array.IndexOf(f.NodeIds, node) + 2];

        static double ShellMx(CSfea.CScoreBridge.Structural.RcSecantStepFields f, int shell) =>
            f.ShellForces[8 * Array.IndexOf(f.ShellIds, shell) + 3];
    }
}
