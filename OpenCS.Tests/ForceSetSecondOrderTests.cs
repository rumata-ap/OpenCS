using CScore;
using CScore.Combinations;
using Microsoft.Data.Sqlite;
using OpenCS.Services;
using OpenCS.Utilites;
using Xunit;

namespace OpenCS.Tests;

/// <summary>
/// Признак «второй порядок» набора усилий (БД v89): сохраняется и читается, у базы v88 колонка добавляется миграцией,
/// наборы по загружениям линейного расчёта с P-Δ его получают.
/// </summary>
public sealed class ForceSetSecondOrderTests : IDisposable
{
    readonly string _path = Path.Combine(Path.GetTempPath(), $"opencs_second_order_{Guid.NewGuid():N}.db");

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        try { File.Delete(_path); } catch (IOException) { }
    }

    [Fact]
    public void SecondOrder_SavedAndLoaded()
    {
        using (var db = new DatabaseService(_path))
        {
            db.SaveForceSet(new ForceSet { Tag = "второй", Kind = "bar", SourceType = "fea", SecondOrder = true });
            db.SaveForceSet(new ForceSet { Tag = "первый", Kind = "bar" });
        }
        using var again = new DatabaseService(_path);
        again.LoadAll();
        Assert.True(again.ForceSets.Single(f => f.Tag == "второй").SecondOrder);
        Assert.False(again.ForceSets.Single(f => f.Tag == "первый").SecondOrder);
    }

    [Fact]
    public void Migration_v88_AddsColumn_ExistingSetsFirstOrder()
    {
        using (var db = new DatabaseService(_path))
            db.SaveForceSet(new ForceSet { Tag = "старый", Kind = "bar" });
        // База v88: без колонки second_order.
        using (var c = new SqliteConnection($"Data Source={_path}"))
        {
            c.Open();
            using var cmd = c.CreateCommand();
            cmd.CommandText = "ALTER TABLE force_sets DROP COLUMN second_order; "
                + "INSERT OR REPLACE INTO settings (key, value_json) VALUES ('schema_version', '88');";
            cmd.ExecuteNonQuery();
        }
        SqliteConnection.ClearAllPools();
        using var migrated = new DatabaseService(_path);
        migrated.LoadAll();
        Assert.False(migrated.ForceSets.Single(f => f.Tag == "старый").SecondOrder);
        var set = migrated.ForceSets.Single(f => f.Tag == "старый");
        set.SecondOrder = true;
        migrated.SaveForceSet(set);
        using var reread = new DatabaseService(_path);
        reread.LoadAll();
        Assert.True(reread.ForceSets.Single(f => f.Tag == "старый").SecondOrder);
    }
}
