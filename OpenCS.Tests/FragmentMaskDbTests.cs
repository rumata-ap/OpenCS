using CScore.Fem;
using CScore.Fem.Fragment;
using CScore.Fem.Fragment.Masks;
using CScore.Submodel;
using Microsoft.Data.Sqlite;
using OpenCS.Services;
using OpenCS.Utilites;
using Xunit;

namespace OpenCS.Tests;

/// <summary>Маски фрагмента в БД (v94–v95): миграция, круговая запись, копия схемы, каскадное удаление, карта загружений.</summary>
public sealed class FragmentMaskDbTests : IDisposable
{
    readonly string _path = Path.Combine(Path.GetTempPath(), $"opencs-fragment-masks-{Guid.NewGuid():N}.db");
    static readonly StoreyFragmentRequest Band = new(1.5, 4.5, "Этаж 2");
    static readonly string[] Tables = ["fem_fragment_masks", "fem_fragment_mask_items", "fem_fragment_cut_bc"];

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        try { if (File.Exists(_path)) File.Delete(_path); } catch { }
    }

    [Fact]
    public void NewDatabase_HasV95TablesAndColumns()
    {
        using (new DatabaseService(_path)) { }
        Assert.Equal(95, DatabaseService.SchemaVersion);
        Assert.Equal("95", Scalar("SELECT value_json FROM settings WHERE key='schema_version'"));
        Assert.Equal(1L, Scalar("SELECT COUNT(*) FROM pragma_table_info('fem_load_cases') WHERE name='parent_load_case_id'"));
        foreach (var t in Tables)
            Assert.Equal(1L, Scalar($"SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name='{t}'"));
        Assert.Equal(1L, Scalar("SELECT COUNT(*) FROM pragma_table_info('fem_schemas') WHERE name='fragment_bc_preset'"));
    }

    [Fact]
    public void V93Database_MigratesToV95()
    {
        using (var db = new DatabaseService(_path)) StoreyFragmentDbFixture.CreateParent(db);
        Exec(string.Join(";", Tables.Select(t => $"DROP TABLE {t}"))
             + ";ALTER TABLE fem_schemas DROP COLUMN fragment_bc_preset;ALTER TABLE fem_load_cases DROP COLUMN parent_load_case_id"
             + ";UPDATE settings SET value_json='93' WHERE key='schema_version'");
        using (var db = new DatabaseService(_path))
        {
            Assert.Equal("95", Scalar("SELECT value_json FROM settings WHERE key='schema_version'"));
            Assert.Equal(1L, Scalar("SELECT COUNT(*) FROM pragma_table_info('fem_load_cases') WHERE name='parent_load_case_id'"));
            foreach (var t in Tables)
                Assert.Equal(1L, Scalar($"SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name='{t}'"));
            db.LoadAll();
            Assert.Single(db.FemSchemas);
            Assert.Null(db.FemSchemas[0].FragmentBcPreset);
        }
    }

    static FragmentMaskSample S(double s, double u, double? f) =>
        new(s, new Dof6(u, 0, -u, 0, 0.5 * u, 0), f is { } v ? new Dof6(0, 0, v, v / 10, 0, 0) : null);

    static FragmentMaskSet Set(IReadOnlyList<int> cuts, FemSchema fragment) => new()
    {
        SourceKind = FragmentMaskSet.SourceCsfeaLinear, SourceRef = "17", SourceLabel = "Линейный", SourceFingerprint = "R5|2026|ABC",
        CreatedAt = "2026-10-28 12:00:00", ReportJson = "[{\"Code\":\"x\"}]",
        Items =
        [
            new(cuts[0], FragmentMaskKey.OfLoadCase(fragment.LoadCases[0].Id), [S(0, 1e-3, -2e5)]),
            new(cuts[1], FragmentMaskKey.OfStageTerms([(fragment.LoadCases[1].Id, 1.2), (fragment.LoadCases[0].Id, 1.1)]),
                [S(0, 2e-3, null), S(0.5, 3e-3, 4e4)]),
        ],
    };

    static void SameItems(IReadOnlyList<FragmentMaskItem> expected, IReadOnlyList<FragmentMaskItem> actual)
    {
        Assert.Equal(expected.Count, actual.Count);
        for (int i = 0; i < expected.Count; i++)
        {
            Assert.Equal((expected[i].CutId, expected[i].Key), (actual[i].CutId, actual[i].Key));
            Assert.Equal(expected[i].Samples, actual[i].Samples);
        }
    }

    [Fact]
    public void Masks_RoundTrip_ReplaceAndDelete()
    {
        using var db = new DatabaseService(_path);
        var parent = StoreyFragmentDbFixture.CreateParent(db);
        var fragment = StoreyFragmentService.Build(db, parent.Id, Band).Schema!;
        var cuts = db.GetFragmentCutsWithIds(fragment.Id);
        Assert.Equal(db.GetFragmentCuts(fragment.Id).Select(c => (c.MemberTag, c.Side)), cuts.Select(c => (c.Cut.MemberTag, c.Cut.Side)));
        Assert.Equal(2, cuts.Select(c => c.Id).Distinct().Count());
        Assert.Null(db.GetFragmentMasks(fragment.Id));

        var set = Set(cuts.Select(c => c.Id).ToList(), fragment);
        db.ReplaceFragmentMasks(fragment.Id, set);
        var read = db.GetFragmentMasks(fragment.Id)!;
        Assert.Equal((set.SourceKind, set.SourceRef, set.SourceLabel, set.SourceFingerprint, set.CreatedAt, set.ReportJson),
            (read.SourceKind, read.SourceRef, read.SourceLabel, read.SourceFingerprint, read.CreatedAt, read.ReportJson));
        SameItems(set.Items, read.Items);
        Assert.Equal((0L, 0L), (Scalar("SELECT no_displacements FROM fem_fragment_mask_items ORDER BY id LIMIT 1"),
            Scalar("SELECT no_forces FROM fem_fragment_mask_items ORDER BY id LIMIT 1")));
        Assert.Equal(1L, Scalar("SELECT no_forces FROM fem_fragment_mask_items ORDER BY id DESC LIMIT 1"));

        // Замена целиком; чужой разрез — отказ без изменений.
        var second = new FragmentMaskSet { SourceKind = FragmentMaskSet.SourceCsfeaSecant, Items = [set.Items[1]] };
        db.ReplaceFragmentMasks(fragment.Id, second);
        Assert.Equal(FragmentMaskSet.SourceCsfeaSecant, db.GetFragmentMasks(fragment.Id)!.SourceKind);
        SameItems(second.Items, db.GetFragmentMasks(fragment.Id)!.Items);
        Assert.Equal(1L, Scalar("SELECT COUNT(*) FROM fem_fragment_masks"));
        var foreign = new FragmentMaskSet { SourceKind = "csfea", Items = [set.Items[0] with { CutId = 999_999 }] };
        Assert.Throws<InvalidOperationException>(() => db.ReplaceFragmentMasks(fragment.Id, foreign));
        SameItems(second.Items, db.GetFragmentMasks(fragment.Id)!.Items);

        db.DeleteFragmentMasks(fragment.Id);
        Assert.Null(db.GetFragmentMasks(fragment.Id));
        Assert.Equal(0L, Scalar("SELECT COUNT(*) FROM fem_fragment_mask_items"));
    }

    [Fact]
    public void CutBcAndPreset_RoundTrip_ResetWithCuts()
    {
        using var db = new DatabaseService(_path);
        var parent = StoreyFragmentDbFixture.CreateParent(db);
        var fragment = StoreyFragmentService.Build(db, parent.Id, Band).Schema!;
        var cuts = db.GetFragmentCutsWithIds(fragment.Id);
        db.ReplaceFragmentCutBc(fragment.Id, new Dictionary<int, byte[]> { [cuts[1].Id] = [255, 255, 0, 1, 2, 3] });
        db.SetFragmentBcPreset(fragment, "Static");
        Assert.Equal("Static", fragment.FragmentBcPreset);
        Assert.Equal([255, 255, 0, 1, 2, 3], db.GetFragmentCutBc(fragment.Id)[cuts[1].Id]);
        db.ReplaceFragmentMasks(fragment.Id, Set(cuts.Select(c => c.Id).ToList(), fragment));
        using (var reopened = new DatabaseService(_path))
        {
            reopened.LoadAll();
            Assert.Equal("Static", reopened.FemSchemas.Single(s => s.Id == fragment.Id).FragmentBcPreset);
        }

        // Разрезы перезаписаны — их маски и ГУ сброшены.
        db.ReplaceStoreyFragmentData(fragment.Id, db.GetFragmentCuts(fragment.Id), db.GetFragmentRefElements(fragment.Id), []);
        Assert.Null(db.GetFragmentMasks(fragment.Id));
        Assert.Empty(db.GetFragmentCutBc(fragment.Id));
    }

    /// <summary>
    /// Карта загружений v95: копия во фрагменте помнит загружение родителя; полное сохранение родителя (новые Id
    /// загружений) переносит ссылки, удалённое загружение — сброс ссылки; переименование во фрагменте ссылку не трогает.
    /// </summary>
    [Fact]
    public void FragmentLoadCases_KeepParentLink_ThroughParentFullSave()
    {
        using var db = new DatabaseService(_path);
        var parent = StoreyFragmentDbFixture.CreateParent(db);
        var fragment = StoreyFragmentService.Build(db, parent.Id, Band).Schema!;
        var parentCases = db.GetFemLoadCases(parent.Id);

        Assert.Equal(parentCases.Select(c => (int?)c.Id), db.GetFemLoadCases(fragment.Id).Select(c => c.ParentLoadCaseId));
        Assert.Equal(parentCases.Select(c => (int?)c.Id), fragment.LoadCases.Select(c => c.ParentLoadCaseId));
        Assert.All(parentCases, c => Assert.Null(c.ParentLoadCaseId));

        // Родитель сохранён целиком без «Полезной»: «Пост.» получает новый Id.
        var kept = parentCases.Where(c => c.Tag == "Пост.").ToList();
        int oldPermanent = kept[0].Id;
        db.SaveFemSchemaEdit(parent.Id, db.GetFemNodes(parent.Id), db.GetFemMembers(parent.Id), [], kept, [], []);
        int newPermanent = Assert.Single(db.GetFemLoadCases(parent.Id)).Id;
        Assert.NotEqual(oldPermanent, newPermanent);

        var links = db.GetFemLoadCases(fragment.Id).ToDictionary(c => c.Tag, c => c.ParentLoadCaseId);
        Assert.Equal(newPermanent, links["Пост."]);
        Assert.Null(links["Полезная"]);
        var inMemory = db.FemSchemas.Single(s => s.Id == fragment.Id).LoadCases.ToDictionary(c => c.Tag, c => c.ParentLoadCaseId);
        Assert.Equal(newPermanent, inMemory["Пост."]);
        Assert.Null(inMemory["Полезная"]);

        // Переименование загружения во фрагменте (обновление по Id) ссылку сохраняет; копия фрагмента — тоже.
        var permanent = db.GetFemLoadCases(fragment.Id).Single(c => c.Tag == "Пост.");
        permanent.Tag = "G";
        db.SaveFemLoadCase(permanent);
        Assert.Equal(newPermanent, db.GetFemLoadCases(fragment.Id).Single(c => c.Tag == "G").ParentLoadCaseId);
        db.LoadAll();
        var copy = db.DuplicateFemSchema(fragment.Id, "Копия");
        Assert.Equal(newPermanent, db.GetFemLoadCases(copy.Id).Single(c => c.Tag == "G").ParentLoadCaseId);
    }

    [Fact]
    public void Duplicate_CopiesMasksWithRemappedCutsAndLoadCases_DeleteCascades()
    {
        using var db = new DatabaseService(_path);
        var parent = StoreyFragmentDbFixture.CreateParent(db);
        var fragment = StoreyFragmentService.Build(db, parent.Id, Band).Schema!;
        var cuts = db.GetFragmentCutsWithIds(fragment.Id).Select(c => c.Id).ToList();
        var set = Set(cuts, fragment);
        db.ReplaceFragmentMasks(fragment.Id, set);
        db.ReplaceFragmentCutBc(fragment.Id, new Dictionary<int, byte[]> { [cuts[0]] = [0, 0, 0, 255, 255, 255] });
        db.SetFragmentBcPreset(fragment, "Isolation");

        var copy = db.DuplicateFemSchema(fragment.Id, "Этаж 2 (копия)");

        Assert.Equal("Isolation", copy.FragmentBcPreset);
        var copyCuts = db.GetFragmentCutsWithIds(copy.Id).Select(c => c.Id).ToList();
        Assert.Empty(copyCuts.Intersect(cuts));
        int Lc(int id) => copy.LoadCases[fragment.LoadCases.ToList().FindIndex(c => c.Id == id)].Id;
        var masks = db.GetFragmentMasks(copy.Id)!;
        Assert.Equal((set.SourceKind, set.SourceRef, set.SourceFingerprint), (masks.SourceKind, masks.SourceRef, masks.SourceFingerprint));
        SameItems(set.Items.Select(i => i with { CutId = copyCuts[cuts.IndexOf(i.CutId)], Key = i.Key.MapLoadCases(Lc) }).ToList(),
            masks.Items);
        Assert.Equal([copy.LoadCases[0].Id, copy.LoadCases[1].Id], masks.Items[1].Key.Terms().Select(t => t.LoadCaseId));
        Assert.Equal([0, 0, 0, 255, 255, 255], db.GetFragmentCutBc(copy.Id)[copyCuts[0]]);

        db.DeleteFemSchema(copy);
        Assert.NotNull(db.GetFragmentMasks(fragment.Id));
        Assert.Single(db.GetFragmentCutBc(fragment.Id));
        db.DeleteFemSchema(fragment);
        foreach (var t in Tables) Assert.Equal(0L, Scalar($"SELECT COUNT(*) FROM {t}"));
    }

    void Exec(string sql)
    {
        using var c = new SqliteConnection($"Data Source={_path};Pooling=False");
        c.Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = sql;
        cmd.ExecuteNonQuery();
    }

    object? Scalar(string sql)
    {
        using var c = new SqliteConnection($"Data Source={_path};Pooling=False");
        c.Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = sql;
        return cmd.ExecuteScalar();
    }
}
