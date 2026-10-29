using Microsoft.Data.Sqlite;
using OpenCS.Services;
using OpenCS.Utilites;
using Xunit;

namespace OpenCS.Tests;

/// <summary>Перекрытия схемы в БД и отметка перекрытия по выделению (спека 2026-10-29).</summary>
public sealed class StoreyFloorServiceTests : IDisposable
{
    readonly string _path = Path.Combine(Path.GetTempPath(), $"opencs-storey-floor-{Guid.NewGuid():N}.db");

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        try { if (File.Exists(_path)) File.Delete(_path); } catch (IOException) { }
    }

    [Fact]
    public void Read_LevelsAndMeshRange()
    {
        using var db = new DatabaseService(_path);
        var parent = StoreyFragmentDbFixture.CreateParent(db);
        var context = StoreyFloorService.Read(db, parent.Id);
        Assert.Equal([3.0, 9.0], context.Levels.Select(l => Math.Round(l.Z, 6)));
        Assert.Equal((0.0, 9.0), (context.ZMin, context.ZMax));

        var band = CScore.Fem.Fragment.StoreyFloorLevels.Band(context.Levels, 0, context.ZMin, context.ZMax);
        Assert.Equal((-1.0, 6.0), (band.Z1, band.Z2));
    }

    [Fact]
    public void SelectionZ_FromPlateMeshElementsAndNodes()
    {
        using var db = new DatabaseService(_path);
        var parent = StoreyFragmentDbFixture.CreateParent(db);

        Assert.Equal(3, StoreyFloorService.SelectionZ(db, parent.Id, [], ["S1"], [], [], out var e1)!.Value, 6);
        Assert.Null(e1);
        Assert.Equal(9, StoreyFloorService.SelectionZ(db, parent.Id, [], [], ["101"], [], out _)!.Value, 6);
        Assert.Equal(3, StoreyFloorService.SelectionZ(db, parent.Id, [], [], [], ["1001", "1002", "1007"], out _)!.Value, 6);

        Assert.Null(StoreyFloorService.SelectionZ(db, parent.Id, ["1", "2"], [], [], [], out var e2));
        Assert.Equal("StoreyFloorPickTilted", e2);
        Assert.Null(StoreyFloorService.SelectionZ(db, parent.Id, [], ["C1"], [], [], out var e3));   // колонна 0…6
        Assert.Equal("StoreyFloorPickTilted", e3);
        Assert.Null(StoreyFloorService.SelectionZ(db, parent.Id, [], [], [], [], out var e4));
        Assert.Equal("StoreyFloorPickEmpty", e4);
    }
}
