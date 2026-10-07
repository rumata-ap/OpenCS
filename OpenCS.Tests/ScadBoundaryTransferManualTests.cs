using System.Diagnostics;
using CScore.Fem;
using CScore.Import;
using Microsoft.Data.Sqlite;
using OpenCS.Services.Scad;
using OpenCS.Utilites;
using Xunit;
using Xunit.Abstractions;

namespace OpenCS.Tests;

/// <summary>
/// Ручной прогон переноса ГУ SCAD (срез 2 подсреза 4б CSfea, 07.10): чтение .SPR (OPENCS_SCAD_BC_SPR, пути через
/// «;»), перенос в сеточный уровень, сводка по видам и проверки резолвера. Если задана OPENCS_SCAD_LOADS_DB — на её
/// копии ГУ пишутся в SCAD-схему с тем же именем .SPR дважды (повтор не плодит дублей). Без переменных — выход.
/// </summary>
public class ScadBoundaryTransferManualTests(ITestOutputHelper output)
{
    [Fact]
    public void TransferFromSpr()
    {
        string? list = Environment.GetEnvironmentVariable("OPENCS_SCAD_BC_SPR");
        if (string.IsNullOrWhiteSpace(list)) return;
        string dir = Environment.GetEnvironmentVariable("OPENCS_SCAD_DIR") ?? ScadInstallLocator.FindDllDirectory()!;
        var native = ScadApiNative.Load(dir);
        string? sourceDb = Environment.GetEnvironmentVariable("OPENCS_SCAD_LOADS_DB");
        string? dbPath = null;
        DatabaseService? db = null;
        if (!string.IsNullOrWhiteSpace(sourceDb))
        {
            dbPath = Path.Combine(Path.GetTempPath(), $"opencs-scad-bc-{Guid.NewGuid():N}.db");
            File.Copy(sourceDb, dbPath);
            db = new DatabaseService(dbPath);
            db.LoadAll();
        }
        try
        {
            foreach (string spr in list.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                output.WriteLine($"=== {Path.GetFileName(spr)}");
                var sw = Stopwatch.StartNew();
                ScadSchemaData data;
                using (var session = new ScadApiSession(native))
                {
                    session.Open(spr);
                    data = ScadApiReader.Read(session, new ScadReadOptions(OutputAxes: false, ConcreteGroups: false,
                        AssignedRebar: false, SteelGroups: false), null, CancellationToken.None).Data;
                }
                var model = data.AnalysisModel!;
                output.WriteLine($"  чтение {sw.ElapsedMilliseconds} мс; признак схемы {model.SchemaType}");
                var nodes = ScadSchemaConverter.ToFemMeshNodes(data, 1);
                var elements = ScadSchemaConverter.ToFemMeshElements(data, 1);
                var types = elements.GroupBy(e => e.ElemTag).ToDictionary(g => g.Key, g => g.First().ElemType);

                var r = ScadBoundaryTransfer.Transfer(model, nodes.Select(n => n.NodeTag).ToHashSet(), types);
                foreach (var line in r.Report) output.WriteLine("  " + line);
                output.WriteLine("  закрепления: " + string.Join(", ", r.Supports.GroupBy(s => s.Mask)
                    .OrderByDescending(g => g.Count()).Select(g => $"{FemBoundaryDofs.Describe(g.Key)} ×{g.Count()}")));
                if (r.Springs.Count > 0)
                    output.WriteLine("  пружины: " + string.Join(", ", r.Springs.GroupBy(s => string.Join(" ",
                        s.Stiffnesses.Select(k => k.ToString("G4")))).OrderByDescending(g => g.Count()).Take(6)
                        .Select(g => $"[{g.Key}] ×{g.Count()}")));
                if (r.ElementProps.Count > 0)
                    output.WriteLine("  шарниры: " + string.Join(", ", r.ElementProps.Values
                        .SelectMany(p => new[] { p.ReleaseI, p.ReleaseJ }).Where(m => m != null)
                        .GroupBy(m => m!.Value).OrderByDescending(g => g.Count())
                        .Select(g => $"{FemBoundaryDofs.Describe(g.Key)} ×{g.Count()}")));

                var resolved = FemBoundaryResolver.Resolve([], nodes, elements, r.Supports, r.Springs, r.RigidBodies);
                foreach (var d in resolved.Diagnostics.Take(10))
                    output.WriteLine($"  {(d.IsError ? "ОШИБКА" : "!")} {d.Message}");

                if (db == null) continue;
                var schema = db.FemSchemas.FirstOrDefault(s => s.SourceType == "scad" && string.Equals(
                    Path.GetFileName(s.SourcePath), Path.GetFileName(spr), StringComparison.OrdinalIgnoreCase));
                if (schema == null) { output.WriteLine("  схемы в БД нет"); continue; }
                sw.Restart();
                for (int pass = 0; pass < 2; pass++)
                {
                    var again = ScadBoundaryTransfer.Transfer(model,
                        db.GetFemMeshNodes(schema.Id).Select(n => n.NodeTag).ToHashSet(),
                        db.GetFemMeshElements(schema.Id).GroupBy(e => e.ElemTag).ToDictionary(g => g.Key, g => g.First().ElemType));
                    db.SaveFemBoundary(schema.Id, ScadBoundaryTransfer.Origin, again.Supports, again.Springs,
                        again.RigidBodies, again.ElementProps);
                }
                Assert.Equal(r.Supports.Count, db.GetFemMeshNodeSupports(schema.Id).Count(s => s.Origin == ScadBoundaryTransfer.Origin));
                Assert.Equal(r.Springs.Count, db.GetFemSprings(schema.Id).Count(s => s.Origin == ScadBoundaryTransfer.Origin));
                Assert.Equal(r.RigidBodies.Count, db.GetFemRigidBodies(schema.Id).Count(s => s.Origin == ScadBoundaryTransfer.Origin));
                Assert.Equal(r.ElementProps.Count, db.GetFemMeshElements(schema.Id).Count(e => e.ReleaseI != null || e.ReleaseJ != null || e.FoundationC1 != null));
                output.WriteLine($"  БД: схема {schema.Id}, две записи {sw.ElapsedMilliseconds} мс, дублей нет");
            }
        }
        finally
        {
            db?.Dispose();
            SqliteConnection.ClearAllPools();
            if (dbPath != null) File.Delete(dbPath);
        }
    }
}
