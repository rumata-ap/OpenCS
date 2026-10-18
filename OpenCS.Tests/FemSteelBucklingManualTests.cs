using System.Globalization;
using CSfea.CScoreBridge.Structural;
using OpenCS.Services;
using OpenCS.Utilites;
using Xunit;
using Xunit.Abstractions;

namespace OpenCS.Tests;

/// <summary>
/// Ручная проверка модели устойчивости схемы (срез 1 расчётных длин стали) на базе проекта: OPENCS_BUCKLING_DB — путь
/// к базе (копируется во временный файл), по всем схемам со стержнями и их постановкам — элементы, определяющие
/// сочетания, λ₁. Без переменной тест сразу выходит.
/// </summary>
public class FemSteelBucklingManualTests(ITestOutputHelper output)
{
    [Fact]
    public void AllSchemas()
    {
        string? source = Environment.GetEnvironmentVariable("OPENCS_BUCKLING_DB");
        if (string.IsNullOrWhiteSpace(source)) return;
        string path = Path.Combine(Path.GetTempPath(), "opencs_buckling_" + Guid.NewGuid().ToString("N") + ".db");
        File.Copy(source, path);
        try
        {
            using var db = new DatabaseService(path);
            db.LoadAll();
            foreach (var schema in db.FemSchemas.ToList())
                foreach (var analysis in db.GetFemAnalyses(schema.Id))
                {
                    output.WriteLine($"Схема «{schema.Tag}», постановка «{analysis.Tag}» ({analysis.Kind})");
                    var input = FemSteelBuckling.Prepare(db, schema.Id, analysis);
                    foreach (var line in input.Report) output.WriteLine("  " + line);
                    if (input.HasErrors) continue;
                    output.WriteLine($"  элементов {input.Units.Count}, сочетаний {input.Combinations.Count}");
                    var r = FemSteelBuckling.Run(input, new RcBucklingOptions(Modes: 6));
                    foreach (var line in r.Report) output.WriteLine("  " + line);
                    for (int i = 0; i < input.Units.Count; i++)
                    {
                        var g = r.Governing[i];
                        string comb = g.Combination < 0 ? "без сжатия" : $"«{input.Combinations[g.Combination].Tag}»";
                        output.WriteLine(string.Create(CultureInfo.CurrentCulture,
                            $"    {input.Units[i].Label} ({input.Units[i].ElemTags.Count} КЭ): {comb}, N = {g.N / 1e3:0.###} кН"));
                        if (r.Lengths[i] is { } x)
                            output.WriteLine(string.Create(CultureInfo.CurrentCulture,
                                $"      l = {x.Length:0.###} м; вокруг z: {x.AboutZ.Source} μ = {x.AboutZ.Mu:0.###} (форма {x.AboutZ.Mode + 1}, участие {x.AboutZ.Participation:0.##}, ρ/λ = {x.AboutZ.Rho / x.AboutZ.Lambda:0.##}); вокруг y: {x.AboutY.Source} μ = {x.AboutY.Mu:0.###} (форма {x.AboutY.Mode + 1})"));
                    }
                    foreach (var (tag, a) in FemSteelBuckling.ElementLengths(input, r).Take(3))
                        output.WriteLine($"    КЭ {tag}: {a.NoteX} | {a.NoteY}");
                    Assert.All(r.Cases, c => Assert.True(c.Result.Converged, $"сочетание «{c.Tag}» не сошлось"));
                }
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            try { File.Delete(path); } catch (IOException) { }
        }
    }
}
