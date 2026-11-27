using System.Collections.ObjectModel;
using System.IO;
using CScore.Fem;
using Microsoft.Data.Sqlite;
using OpenCS.Services;
using OpenCS.Utilites;
using Xunit;

namespace OpenCS.Tests;

/// <summary>
/// Раздел «Импорт», 9б: служба записи импорта схем <see cref="FemSchemaImport"/> — общая для меню и консоли.
/// Образец — текст ЛИРЫ стены (<c>CScore.Tests/Import/Fixtures/lira-text-wall.txt</c>, без ЛИРЫ и без COM).
/// </summary>
public sealed class FemSchemaImportTests : IDisposable
{
    readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"opencs-schema-import-{Guid.NewGuid():N}.db");
    readonly DatabaseService _db;
    readonly RecordingLog _log = new();

    public FemSchemaImportTests() => _db = new DatabaseService(_dbPath);

    public void Dispose()
    {
        _db.Dispose();
        SqliteConnection.ClearAllPools();
        try { File.Delete(_dbPath); } catch (IOException) { }
    }

    static string WallText => Path.Combine(UiInventory.RepoRoot, "CScore.Tests", "Import", "Fixtures", "lira-text-wall.txt");

    FemSchemaImport Service() => new(_db, _log, LiraImportSettings.Default, new CalcSettings());

    [Fact]
    public async Task LiraText_CreatesSchemaWithMeshBoundaryAndLoads()
    {
        var import = Service();
        var txt = await import.ReadLiraTextAsync(WallText, InlineImportWork.Instance);
        Assert.NotNull(txt);

        var (schema, done, boundary, loads) = await import.ImportLiraText(txt!, WallText, null, InlineImportWork.Instance);

        Assert.Equal("ТЕСТ_СТЕНА", schema.Tag);
        Assert.Equal("lira", schema.SourceType);
        Assert.Contains(schema, _db.FemSchemas);
        Assert.Equal(txt!.Nodes.Count, _db.GetFemMeshNodes(schema.Id).Count);
        Assert.Equal(txt.Elements.Count, _db.GetFemMeshElements(schema.Id).Count);
        Assert.NotEmpty(boundary.Rows);
        Assert.Contains(boundary.Rows, r => r.Transferred > 0);
        Assert.NotEmpty(loads.Totals);
        Assert.Contains(loads.Totals, t => Math.Abs(t.FzKn) > 0);
        Assert.Equal(Path.GetFullPath(WallText), schema.SourcePath);
        Assert.False(string.IsNullOrWhiteSpace(done));
    }

    [Fact]
    public async Task LiraText_RepeatedLoads_ReplaceTransferredNotDuplicate()
    {
        var import = Service();
        var txt = (await import.ReadLiraTextAsync(WallText, InlineImportWork.Instance))!;
        var (schema, _, _, first) = await import.ImportLiraText(txt, WallText, null, InlineImportWork.Instance);
        int cases = schema.LoadCases.Count;
        var nodes = _db.GetFemMeshNodes(schema.Id);
        var elements = _db.GetFemMeshElements(schema.Id);

        var again = await import.TransferLiraLoads(schema, txt, nodes, elements, InlineImportWork.Instance);

        Assert.Equal(cases, schema.LoadCases.Count);
        Assert.Equal(first.Totals.Select(t => Math.Round(t.FzKn, 6)), again.Totals.Select(t => Math.Round(t.FzKn, 6)));
    }

    [Fact]
    public void Create_ErrorOnAnyStep_RemovesTheSchema()
    {
        var import = Service();
        int before = _db.FemSchemas.Count;
        var ex = Assert.Throws<InvalidOperationException>(() =>
            import.Create("Полусхема", "lira", null, s =>
            {
                _db.SaveFemImport(s.Id, CScore.Fem.Import.FemImportResult.MeshOnly(
                    [new FemMeshNode { SchemaId = s.Id, NodeTag = "1" }], [], []));
                throw new InvalidOperationException("сбой записи профилей");
            }));

        Assert.Equal("сбой записи профилей", ex.Message);
        Assert.Equal(before, _db.FemSchemas.Count);
        Assert.DoesNotContain(_db.FemSchemas, s => s.Tag == "Полусхема");
        using var reopened = new DatabaseService(_dbPath);
        Assert.DoesNotContain(reopened.FemSchemas, s => s.Tag == "Полусхема");
        Assert.Contains(_log.Messages, m => m.Contains("Полусхема") || m.Contains("FemImportRolledBack"));
    }

    [Fact]
    public async Task LiraText_WithoutGeometry_IsRefusedAndNothingCreated()
    {
        string path = Path.Combine(Path.GetTempPath(), $"opencs-empty-{Guid.NewGuid():N}.txt");
        File.WriteAllText(path, "( 0/ 1; ПУСТО/ 2; 5/ )\n");
        try
        {
            var import = Service();
            var txt = await import.ReadLiraTextAsync(path, InlineImportWork.Instance);
            Assert.NotNull(txt);
            int before = _db.FemSchemas.Count;
            await Assert.ThrowsAsync<InvalidDataException>(() => import.ImportLiraText(txt!, path, null, InlineImportWork.Instance));
            Assert.Equal(before, _db.FemSchemas.Count);
        }
        finally { File.Delete(path); }
    }

    sealed class RecordingLog : ILogService
    {
        public List<string> Messages { get; } = [];
        public ObservableCollection<LogEntry> LogEntries { get; } = [];
        public void Info(string message) => Messages.Add(message);
        public void Warning(string message) => Messages.Add(message);
        public void Error(string message) => Messages.Add(message);
    }
}
