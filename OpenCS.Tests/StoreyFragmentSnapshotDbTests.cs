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

    /// <summary>Призма [2; 2,5]² в углу плиты S1: габарит отбора с запасом 1 м — x, y ≥ 0,999.</summary>
    static readonly StoreyFragmentRequest Corner = Band with { Prism = FragmentPrism.Create(2.25, 2.25, 1, 0, 0.25, 0.25) };

    [Fact]
    public void Prism_SnapshotOnlyNearPrism()
    {
        using var db = new DatabaseService(_path);
        var parent = StoreyFragmentDbFixture.CreateParent(db);
        var s = db.ReadStoreyFragmentSnapshot(parent.Id, Corner);

        // Колонна C1 в (0, 0) вне габарита — ни КонЭ, ни её КЭ; плита пересекает — остаётся.
        Assert.Equal(["S1"], s.Members.Select(m => m.ElemTag));
        // КЭ S1 с x, y в [0; 3] (касание габарита 1,5 — внутри): 4 КЭ из 16, колонны 502/503 нет.
        Assert.Equal(["11", "12", "15", "16"], s.MeshElements.Select(e => e.ElemTag).Order(StringComparer.Ordinal));
        Assert.All(s.MeshNodes, n => Assert.True(n.X >= 0 && n.Y >= 0));
        Assert.Single(s.ElementLoads);
        Assert.Empty(s.MeshNodeLoads);
        Assert.Empty(s.MemberLoads);
        // Без призмы — прежний отбор.
        Assert.Equal(18, db.ReadStoreyFragmentSnapshot(parent.Id, Band).MeshElements.Count);
    }

    [Fact]
    public void Prism_BuildsSameDraftAsFullParent()
    {
        using var db = new DatabaseService(_path);
        var parent = StoreyFragmentDbFixture.CreateParent(db);
        var prism = StoreyFragmentBuilder.Build(db.ReadStoreyFragmentSnapshot(parent.Id, Corner), Corner);
        var full = StoreyFragmentBuilder.Build(StoreyFragmentDbFixture.ReadFull(db, parent.Id), Corner);

        Assert.False(prism.HasErrors, string.Join("; ", prism.Diagnostics.Select(d => d.Message)));
        Assert.Equal(full.Members.Select(m => m.Member.ElemTag), prism.Members.Select(m => m.Member.ElemTag));
        Assert.Equal(full.Cuts.Select(c => (c.MemberTag, c.Side, c.Segments.Count)), prism.Cuts.Select(c => (c.MemberTag, c.Side, c.Segments.Count)));
        Assert.Equal(full.Nodes.Count, prism.Nodes.Count);
        Assert.Equal(full.ElementLoads.Count, prism.ElementLoads.Count);
        Assert.Equal(full.FieldLoads.Count, prism.FieldLoads.Count);
        Assert.Equal(full.NodeLoads.Count, prism.NodeLoads.Count);
        Assert.Equal(full.RefElements.Select(e => e.Tag).Order(), prism.RefElements.Select(e => e.Tag).Order());
        Assert.Equal(full.Diagnostics.Select(d => d.Code).Order(), prism.Diagnostics.Select(d => d.Code).Order());
        Assert.Contains(prism.Cuts, c => c.Side == FragmentCutSide.Lateral);
    }

    [Fact]
    public void Snapshot_SectionOutline_FromCrossSections()
    {
        using var db = new DatabaseService(_path);
        var parent = StoreyFragmentDbFixture.CreateParent(db);
        var area = new CScore.MaterialArea { Tag = "Бетон" };
        area.Contours.Add(new CScore.Contour([-.2, .2, .2, -.2, -.2], [-.3, -.3, .3, .3, -.3], "outer") { Type = CScore.ContourType.Hull });
        db.CrossSections.Add(new CScore.CrossSection { Id = 77, Num = 1, Tag = "К1", Areas = [area] });

        var s = db.ReadStoreyFragmentSnapshot(parent.Id, Band);
        var outline = s.SectionOutline!(77);
        Assert.NotNull(outline);
        Assert.Equal(4, outline!.Count);
        // Y стержня — y сечения (±0,3), Z — x сечения (±0,2).
        Assert.Equal(0.3, outline.Max(p => p.Y), 9);
        Assert.Equal(0.2, outline.Max(p => p.Z), 9);
        Assert.Null(s.SectionOutline(999));
    }

    /// <summary>
    /// Голова сваи: узел сетки в полосе без КЭ, с закреплением и пружиной родителя — попадает в снимок и во фрагмент
    /// закреплением и пружиной узла фрагмента (спека 4а, дефект D1).
    /// </summary>
    [Fact]
    public void Snapshot_IsolatedPileHead_SupportAndSpringReachFragment()
    {
        using var db = new DatabaseService(_path);
        var parent = StoreyFragmentDbFixture.CreateParent(db);
        // Узел вне всех КЭ, в полосе (z = 4,4), с закреплением 3 и пружиной kz — записи родителя (как импорт SCAD).
        using (var raw = new SqliteConnection($"Data Source={_path}"))
        {
            raw.Open();
            using var cmd = raw.CreateCommand();
            cmd.CommandText = """
                INSERT INTO fem_mesh_nodes (schema_id, node_tag, x, y, z, origin) VALUES (@sid, '9001', 5, 5, 4.4, 'generated');
                INSERT INTO fem_mesh_node_supports (schema_id, node_tag, mask, origin) VALUES (@sid, '9001', 3, 'import:scad');
                INSERT INTO fem_springs (schema_id, target_kind, node_tag, kz, origin) VALUES (@sid, 'mesh_node', '9001', 24525000, 'import:scad');
                """;
            cmd.Parameters.AddWithValue("@sid", parent.Id);
            cmd.ExecuteNonQuery();
        }

        var s = db.ReadStoreyFragmentSnapshot(parent.Id, Band);
        Assert.Contains(s.MeshNodes, n => n.NodeTag == "9001");
        Assert.Contains(s.MeshNodeSupports, m => m.NodeTag == "9001" && m.Mask == 3);
        Assert.Contains(s.Springs, p => p.NodeTag == "9001" && p.Kz == 24525000);

        var draft = StoreyFragmentBuilder.Build(s, Band);
        Assert.False(draft.HasErrors, string.Join("; ", draft.Diagnostics.Select(d => d.Message)));
        var head = Assert.Single(draft.Nodes, n => n.X == 5 && n.Y == 5 && Math.Abs(n.Z - 4.4) < 1e-9);
        Assert.Equal(3, head.DofMask);
        var spring = Assert.Single(draft.Springs);
        Assert.Equal(CScore.Fem.FemSpringTargetKinds.Node, spring.TargetKind);
        Assert.Equal(head.NodeTag, spring.NodeTag);
    }
}
