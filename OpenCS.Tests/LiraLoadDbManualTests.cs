using System.Diagnostics;
using CScore.Fem;
using CScore.Import;
using OpenCS.Utilites;
using Xunit;
using Xunit.Abstractions;

namespace OpenCS.Tests;

/// <summary>
/// Ручной замер записи нагрузок ЛИРЫ в БД (срез 5 плана 4б ЛИРЫ + 4ж, 12.10): текстовый файл OPENCS_LIRA_DB_TXT
/// (ФР19 — 0,6 млн нагрузок на КЭ). Временная БД: сетка из документов 1/4, перенос нагрузок, запись, чтение и повторная
/// запись (замена своего) — время, память и размер файла. Без переменной — выход.
/// </summary>
public class LiraLoadDbManualTests(ITestOutputHelper output)
{
    [Fact]
    public void SaveAndLoadLiraLoads()
    {
        string? path = Environment.GetEnvironmentVariable("OPENCS_LIRA_DB_TXT");
        if (string.IsNullOrWhiteSpace(path)) return;

        var s = LiraTextReader.Read(path);
        double lu = s.Units.LengthUnitM;
        var meshNodes = s.Nodes.Select((p, i) => new FemMeshNode
        {
            NodeTag = (i + 1).ToString(), X = p.X * lu, Y = p.Y * lu, Z = p.Z * lu, Origin = FemMember.MeshSourceImported,
        }).ToList();
        var elements = s.Elements.Where(e => e.Nodes.Length is 2 or 3 or 4 && e.Type is not (51 or 55 or 56 or 57))
            .Select(e => new FemElement
            {
                ElemTag = e.Number.ToString(), ElemType = e.Nodes.Length == 2 ? "beam" : "shell",
                NodeIdsJson = System.Text.Json.JsonSerializer.Serialize(e.Nodes), Origin = FemMember.MeshSourceImported,
            }).ToList();

        string dbPath = Path.Combine(Path.GetTempPath(), $"opencs-lira-loads-{Guid.NewGuid():N}.db");
        try
        {
            var db = new DatabaseService(dbPath);
            var schema = new FemSchema { Tag = Path.GetFileNameWithoutExtension(path), SourceType = "lira" };
            db.SaveFemSchema(schema);
            var sw = Stopwatch.StartNew();
            db.SaveFemMeshSnapshot(schema.Id, meshNodes, elements);
            output.WriteLine($"сетка: узлов {meshNodes.Count}, КЭ {elements.Count}, запись {sw.Elapsed.TotalSeconds:0.00} с");

            var tags = meshNodes.Select(n => n.NodeTag).ToHashSet(StringComparer.Ordinal);
            var types = elements.ToDictionary(e => e.ElemTag, e => e.ElemType, StringComparer.Ordinal);
            for (int pass = 1; pass <= 2; pass++)
            {
                int next = 0;
                sw.Restart();
                var r = LiraLoadTransfer.Transfer(s, tags, types, 9.80665, schema.LoadCases.ToList(),
                    db.GetFemElementLoads(schema.Id), db.GetFemMeshNodeLoads(schema.Id), () => --next);
                double tTransfer = sw.Elapsed.TotalSeconds;
                sw.Restart();
                long jsonChars = r.ElementLoads.Sum(l => (long)l.TargetTagsJson.Length + l.ValuesJson.Length);
                output.WriteLine($"  JSON строк нагрузок: {sw.Elapsed.TotalSeconds:0.00} с, {jsonChars / 1e6:0.0} млн знаков");
                sw.Restart();
                db.SaveFemLoadCasesAndMeshLoads(schema.Id, r.LoadCases, r.ElementLoads, r.MeshNodeLoads);
                output.WriteLine($"проход {pass}: загружений {r.LoadCases.Count}, нагрузок на КЭ {r.ElementLoads.Count}, " +
                    $"узловых {r.MeshNodeLoads.Count}; перенос (с чтением прежних) {tTransfer:0.00} с, запись {sw.Elapsed.TotalSeconds:0.00} с");
            }

            GC.Collect();
            long mem0 = GC.GetTotalMemory(true);
            sw.Restart();
            var loads = db.GetFemElementLoads(schema.Id);
            var nodeLoads = db.GetFemMeshNodeLoads(schema.Id);
            double tRead = sw.Elapsed.TotalSeconds;
            long mem = GC.GetTotalMemory(true) - mem0;
            sw.Restart();
            int targets = loads.Sum(l => l.TargetTags.Count + l.Values.Count);
            output.WriteLine($"чтение: {loads.Count} + {nodeLoads.Count} строк за {tRead:0.00} с, {mem / 1e6:0} МБ; " +
                $"разбор JSON всех строк {sw.Elapsed.TotalSeconds:0.00} с ({targets})");
            GC.KeepAlive(nodeLoads);
            db.Dispose();
            output.WriteLine($"файл БД {new FileInfo(dbPath).Length / 1e6:0} МБ");
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            try { File.Delete(dbPath); } catch (IOException) { }
        }
    }
}
