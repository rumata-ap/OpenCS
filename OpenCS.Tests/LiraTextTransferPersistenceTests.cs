using CScore.Fem;
using CScore.Import;
using OpenCS.Utilites;
using Xunit;

namespace OpenCS.Tests;

/// <summary>
/// Перенос ГУ и нагрузок из текстового файла ЛИРЫ через БД (срез 5 плана 4б ЛИРЫ + 4ж): «Тест_стена», запись как у
/// импорта и команд «Дочитать…» — повторный перенос заменяет своё, ручные закрепления узла и ручные нагрузки остаются,
/// Id нагрузок в памяти = Id строк БД (вставка с переиспользуемыми параметрами).
/// </summary>
public sealed class LiraTextTransferPersistenceTests : IDisposable
{
    const double TonToKn = 9.80665;
    readonly string _path = Path.Combine(Path.GetTempPath(), $"opencs-lira-text-{Guid.NewGuid():N}.db");

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try { File.Delete(_path); } catch (IOException) { }
    }

    static LiraTextSchema Wall() =>
        LiraTextReader.Read(Path.Combine(AppContext.BaseDirectory, "Fixtures", "lira-text-wall.txt"));

    [Fact]
    public void RepeatedTransfer_ReplacesOwn_KeepsManual()
    {
        var txt = Wall();
        double lu = txt.Units.LengthUnitM;
        using var db = new DatabaseService(_path);
        var schema = new FemSchema { Tag = "Тест_стена", SourceType = "lira" };
        db.SaveFemSchema(schema);
        db.SaveFemMeshSnapshot(schema.Id,
            txt.Nodes.Select((p, i) => new FemMeshNode
            {
                NodeTag = (i + 1).ToString(), X = p.X * lu, Y = p.Y * lu, Z = p.Z * lu, Origin = FemMember.MeshSourceImported,
            }).ToList(),
            txt.Elements.Select(e => new FemElement
            {
                ElemTag = e.Number.ToString(), ElemType = e.Nodes.Length == 2 ? "beam" : "shell",
                NodeIdsJson = System.Text.Json.JsonSerializer.Serialize(e.Nodes), Origin = FemMember.MeshSourceImported,
            }).ToList());
        Assert.True(LiraTextMeshMatch.Compare(txt, db.GetFemMeshNodes(schema.Id), db.GetFemMeshElements(schema.Id)).Ok);

        // Ручное: закрепление одного из опорных узлов ЛИРЫ, своё загружение с узловой силой.
        string manualNode = txt.Supports[0].Node.ToString();
        db.SetFemMeshNodeBoundary(schema.Id, manualNode, 0b000111, [0, 0, 0, 0, 0, 0]);
        var manualCase = new FemLoadCase { Id = -100, Tag = "Ручное" };
        db.SaveFemLoadCasesAndMeshLoads(schema.Id, [manualCase],
            [], [new FemMeshNodeLoad { LoadCaseId = -100, MeshNodeTag = "1", Fz = -5e3 }]);

        int supports = 0, elementLoads = 0;
        for (int pass = 1; pass <= 2; pass++)
        {
            var nodes = db.GetFemMeshNodes(schema.Id);
            var elements = db.GetFemMeshElements(schema.Id);
            var tags = nodes.Select(n => n.NodeTag).ToHashSet(StringComparer.Ordinal);
            var types = elements.ToDictionary(e => e.ElemTag, e => e.ElemType, StringComparer.Ordinal);

            var bc = LiraBoundaryTransfer.Transfer(txt, tags, types, TonToKn);
            db.SaveFemBoundary(schema.Id, LiraBoundaryTransfer.Origin, bc.Supports, bc.Springs, bc.RigidBodies, bc.ElementProps);
            int next = 0;
            var loads = LiraLoadTransfer.Transfer(txt, tags, types, TonToKn, schema.LoadCases.ToList(),
                db.GetFemElementLoads(schema.Id), db.GetFemMeshNodeLoads(schema.Id), () => --next);
            db.SaveFemLoadCasesAndMeshLoads(schema.Id, loads.LoadCases, loads.ElementLoads, loads.MeshNodeLoads);

            var storedSupports = db.GetFemMeshNodeSupports(schema.Id);
            var lira = storedSupports.Where(s => s.Origin == LiraBoundaryTransfer.Origin).ToList();
            Assert.Equal(bc.Supports.Count - 1, lira.Count);             // на ручном узле импорт не пишет
            Assert.Single(storedSupports, s => s.NodeTag == manualNode && s.Origin == FemLoadOrigin.Manual);
            if (pass == 1) supports = lira.Count; else Assert.Equal(supports, lira.Count);

            var stored = db.GetFemElementLoads(schema.Id);
            Assert.Equal(loads.ElementLoads.Select(l => l.Id).Order(), stored.Select(l => l.Id).Order());
            Assert.All(stored, l => Assert.Equal(LiraLoadTransfer.Origin, l.Origin));
            if (pass == 1) elementLoads = stored.Count; else Assert.Equal(elementLoads, stored.Count);

            var nodeLoads = db.GetFemMeshNodeLoads(schema.Id);
            Assert.Equal(loads.MeshNodeLoads.Select(l => l.Id).Order(), nodeLoads.Select(l => l.Id).Order());
            Assert.Single(nodeLoads, l => l.Origin == FemLoadOrigin.Manual && l.Fz == -5e3);

            Assert.Equal(["Ручное", "ПОСТОЯННОЕ", "ВРЕМЕННЫЕ"], schema.LoadCases.Select(c => c.Tag.Trim()));
        }
        Assert.Equal(80 - 1, supports);
        Assert.True(elementLoads > 0);
    }
}
