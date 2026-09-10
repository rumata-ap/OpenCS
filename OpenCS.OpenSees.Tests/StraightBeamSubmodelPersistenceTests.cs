using Microsoft.Data.Sqlite;
using OpenCS.Utilites;
using Xunit;

namespace OpenCS.OpenSees.Tests;

public sealed class StraightBeamSubmodelPersistenceTests
{
    [Fact]
    public void NewDatabase_CreatesSubmodelExtractionTables()
    {
        var path = TempDatabasePath();
        try
        {
            using var _ = new DatabaseService(path);

            Assert.True(TableExists(path, "submodel_extractions"));
            Assert.True(TableExists(path, "submodel_extraction_nodes"));
            Assert.True(TableExists(path, "submodel_extraction_segments"));
        }
        finally { DeleteDatabase(path); }
    }

    static bool TableExists(string path, string table)
    {
        using var connection = new SqliteConnection($"Data Source={path};Pooling=False");
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name=@name";
        command.Parameters.AddWithValue("@name", table);
        return (long)command.ExecuteScalar()! == 1;
    }

    static string TempDatabasePath() => Path.Combine(Path.GetTempPath(), $"opencs-submodel-{Guid.NewGuid():N}.db");

    static void DeleteDatabase(string path)
    {
        if (File.Exists(path)) File.Delete(path);
    }
}
