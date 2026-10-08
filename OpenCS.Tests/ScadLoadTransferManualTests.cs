using System.Diagnostics;
using CScore.Fem.Loads;
using CScore.Import;
using Microsoft.Data.Sqlite;
using OpenCS.Services.Scad;
using OpenCS.Utilites;
using Xunit;
using Xunit.Abstractions;

namespace OpenCS.Tests;

/// <summary>
/// Ручной прогон переноса нагрузок SCAD на сохранённой БД (срез 4а CSfea, 07.10): копия БД во временной папке
/// (миграция до v77 не трогает исходник), перенос из вложения каждой SCAD-схемы, запись, ΣF загружений и журнал.
/// OPENCS_SCAD_LOADS_DB — путь к .db; без переменной тест сразу выходит.
/// </summary>
public class ScadLoadTransferManualTests(ITestOutputHelper output)
{
    [Fact]
    public void TransferFromStoredAttachments()
    {
        string? source = Environment.GetEnvironmentVariable("OPENCS_SCAD_LOADS_DB");
        if (string.IsNullOrWhiteSpace(source)) return;
        string path = Path.Combine(Path.GetTempPath(), $"opencs-scad-loads-{Guid.NewGuid():N}.db");
        File.Copy(source, path);
        try
        {
            using var db = new DatabaseService(path);
            db.LoadAll();
            foreach (var schema in db.FemSchemas.Where(s => s.SourceType == "scad").ToList())
            {
                if (db.GetFemSchemaSourceFile(schema.Id, FemSchemaSourceFileKind.ScadAnalysisModel) is not { } file) continue;
                var model = ScadAnalysisModel.FromJson(System.Text.Encoding.UTF8.GetString(file.Data));
                var sw = Stopwatch.StartNew();
                var nodes = db.GetFemMeshNodes(schema.Id);
                var elements = db.GetFemMeshElements(schema.Id);
                var types = elements.GroupBy(e => e.ElemTag).ToDictionary(g => g.Key, g => g.First().ElemType);
                int next = 0;
                var result = ScadLoadTransfer.Transfer(model, types, schema.LoadCases.ToList(),
                    db.GetFemElementLoads(schema.Id), db.GetFemMeshNodeLoads(schema.Id), () => --next);
                db.SaveFemLoadCasesAndMeshLoads(schema.Id, result.LoadCases, result.ElementLoads, result.MeshNodeLoads);
                output.WriteLine($"Схема {schema.Id} «{schema.Tag}»: КЭ {elements.Count}, перенос и запись {sw.ElapsedMilliseconds} мс");
                foreach (var line in result.Report) output.WriteLine("  " + line);

                // Повторный перенос не плодит дублей.
                var again = ScadLoadTransfer.Transfer(model, types, schema.LoadCases.ToList(),
                    db.GetFemElementLoads(schema.Id), db.GetFemMeshNodeLoads(schema.Id), () => --next);
                Assert.Equal(0, again.CasesCreated);
                Assert.Equal(result.ElementLoads.Count, again.ElementLoads.Count);

                var mesh = new FemLoadMeshContext(nodes, elements, schema.MemberGroups.ToList(),
                    new ScadElementStiffnessSource(db.GetFemSchemaStiffnesses(schema.Id), model.ForceUnitN, model.LengthUnitM));
                var stored = db.GetFemElementLoads(schema.Id);
                var storedNodes = db.GetFemMeshNodeLoads(schema.Id);
                foreach (var lc in db.GetFemLoadCases(schema.Id).Where(c => c.Origin == ScadLoadTransfer.Origin))
                {
                    sw.Restart();
                    var f = FemLoadCaseNodalForces.Resolve(lc, stored, storedNodes, mesh);
                    var (fx, fy, fz) = f.Total;
                    output.WriteLine($"  «{lc.Tag}» (SCAD {lc.SourceLoadNum}): ΣFx = {fx / 1e3:0.###} кН, ΣFy = {fy / 1e3:0.###} кН, " +
                        $"ΣFz = {fz / 1e3:0.###} кН ({fz / 9810:0.###} т), узлов {f.Forces.Count}, {sw.ElapsedMilliseconds} мс");
                    foreach (var d in f.Diagnostics.Select(d => d.Message).Distinct().Take(5)) output.WriteLine("    ! " + d);
                }
            }
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            File.Delete(path);
        }
    }

    /// <summary>
    /// Плита Дорфмана из .SPR (OPENCS_SCAD_NL_SPR): ΣZ L1 после переноса без нагрузок в защемлённых узлах =
    /// 43,32 т, как протокол SCAD «Суммарные внешние нагрузки».
    /// </summary>
    [Fact]
    public void DorfmanSelfWeightMatchesScadProtocol()
    {
        string? spr = Environment.GetEnvironmentVariable("OPENCS_SCAD_NL_SPR");
        if (string.IsNullOrWhiteSpace(spr)) return;
        string dir = Environment.GetEnvironmentVariable("OPENCS_SCAD_DIR") ?? ScadInstallLocator.FindDllDirectory()!;
        ScadSchemaData data;
        using (var session = new ScadApiSession(ScadApiNative.Load(dir)))
        {
            session.Open(spr);
            data = ScadApiReader.Read(session, new ScadReadOptions(), null, CancellationToken.None).Data;
        }
        var model = data.AnalysisModel!;
        var nodes = ScadSchemaConverter.ToFemMeshNodes(data, 1);
        var elements = ScadSchemaConverter.ToFemMeshElements(data, 1);
        int next = 0;
        var result = ScadLoadTransfer.Transfer(model, elements.ToDictionary(e => e.ElemTag, e => e.ElemType), [], [], [], () => --next);
        foreach (var line in result.Report) output.WriteLine(line);
        var mesh = new FemLoadMeshContext(nodes, elements, null, new ScadElementStiffnessSource(
            ScadSchemaConverter.ToSchemaStiffnesses(data).ToDictionary(x => x.Id), model.ForceUnitN, model.LengthUnitM));
        var fixedNodes = model.Bounds.Where(b => b.Value == 0x3F).Select(b => b.Key.ToString()).ToHashSet();
        foreach (var lc in result.LoadCases)
        {
            var f = FemLoadCaseNodalForces.Resolve(lc, result.ElementLoads, result.MeshNodeLoads, mesh);
            double free = f.Forces.Where(x => !fixedNodes.Contains(x.NodeTag)).Sum(x => x.Fz) / 9810;
            output.WriteLine($"«{lc.Tag}» (SCAD {lc.SourceLoadNum}): ΣZ = {f.Total.Fz / 9810:0.###} т, без защемлённых узлов {free:0.###} т");
            foreach (var d in f.Diagnostics.Select(d => d.Message).Distinct().Take(5)) output.WriteLine("  ! " + d);
            if (lc.SourceLoadNum == 1) Assert.Equal(-43.32, free, 2);
        }
    }
}
