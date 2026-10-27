using CScore.Fem;
using CScore.Fem.Fragment;
using Microsoft.Data.Sqlite;
using OpenCS.Utilites;
using Xunit;

namespace OpenCS.Tests;

/// <summary>Снимок полосы родителя из БД: сетка, узлы и нагрузки отобраны фильтром SQL.</summary>
public sealed class StoreyFragmentSnapshotDbTests : IDisposable
{
    readonly string _path = Path.Combine(Path.GetTempPath(), $"opencs-fragment-snap-{Guid.NewGuid():N}.db");
    static readonly StoreyFragmentRequest Band = new(1.5, 4.5, "Этаж 2");

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        try { if (File.Exists(_path)) File.Delete(_path); } catch { }
    }

    [Fact]
    public void Snapshot_ContainsOnlyBand()
    {
        using var db = new DatabaseService(_path);
        var parent = StoreyFragmentDbFixture.CreateParent(db);
        var s = db.ReadStoreyFragmentSnapshot(parent.Id, Band);

        Assert.Equal(["C1", "S1"], s.Members.Select(m => m.ElemTag).Order());
        Assert.Single(s.Regions);
        var elems = s.MeshElements.Select(e => e.ElemTag).ToHashSet();
        Assert.Equal(16 + 2, elems.Count);
        Assert.Contains("502", elems);
        Assert.Contains("503", elems);
        Assert.DoesNotContain("501", elems);
        Assert.DoesNotContain("504", elems);
        Assert.DoesNotContain("101", elems);
        Assert.Equal(25 + 3, s.MeshNodes.Count);
        Assert.All(s.MeshNodes, n => Assert.InRange(n.Z, 1.5, 4.5));

        Assert.Single(s.ElementLoads);
        Assert.Equal(FemElementLoadKinds.Uniform, s.ElementLoads[0].LoadKind);
        Assert.Equal(["1007"], s.MeshNodeLoads.Select(l => l.MeshNodeTag));
        Assert.Single(s.MemberLoads);
        Assert.Equal(2, s.LoadCases.Count);
        Assert.Single(s.LoadDefinitions);
        // Временные таблицы снимка удалены: повторный снимок не падает и даёт то же.
        Assert.Equal(18, db.ReadStoreyFragmentSnapshot(parent.Id, Band).MeshElements.Count);
    }

    [Fact]
    public void Snapshot_BuildsSameDraftAsFullParent()
    {
        using var db = new DatabaseService(_path);
        var parent = StoreyFragmentDbFixture.CreateParent(db);
        var band = StoreyFragmentBuilder.Build(db.ReadStoreyFragmentSnapshot(parent.Id, Band), Band);
        var full = StoreyFragmentBuilder.Build(StoreyFragmentDbFixture.ReadFull(db, parent.Id), Band);

        Assert.False(band.HasErrors, string.Join("; ", band.Diagnostics.Select(d => d.Message)));
        Assert.Equal(full.Members.Select(m => m.Member.ElemTag), band.Members.Select(m => m.Member.ElemTag));
        Assert.Equal(full.Cuts.Count, band.Cuts.Count);
        Assert.Equal(full.Nodes.Count, band.Nodes.Count);
        Assert.Equal(full.ElementLoads.Count, band.ElementLoads.Count);
        Assert.Equal(full.MemberLoads.Count, band.MemberLoads.Count);
        Assert.Equal(full.FieldLoads.Count, band.FieldLoads.Count);
        Assert.Equal(full.NodeLoads.Count, band.NodeLoads.Count);
        Assert.Equal(full.RefElements.Select(e => e.Tag).Order(), band.RefElements.Select(e => e.Tag).Order());
        Assert.Equal(full.Diagnostics.Select(d => d.Code).Order(), band.Diagnostics.Select(d => d.Code).Order());
        Assert.Equal(2, band.Cuts.Count);
        Assert.NotEmpty(band.MemberLoads);
    }
}
