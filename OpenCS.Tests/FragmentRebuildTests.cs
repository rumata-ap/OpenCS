using CScore.Fem;
using CScore.Fem.Fragment;
using CScore.Fem.Fragment.Masks;
using Microsoft.Data.Sqlite;
using OpenCS.Services;
using OpenCS.Utilites;
using Xunit;

namespace OpenCS.Tests;

/// <summary>
/// Пересборка фрагмента на месте (хвост вехи «Аудит GUI + CLI», п. 1): тот же Id и имя, новый состав из родителя;
/// постановки, проверки и ГУ разрезов переносятся по загружению родителя, тегу КонЭ и ключу разреза; маски сбрасываются.
/// </summary>
public sealed class FragmentRebuildTests : IDisposable
{
    readonly string _path = Path.Combine(Path.GetTempPath(), $"opencs-fragment-rebuild-{Guid.NewGuid():N}.db");

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        try { if (File.Exists(_path)) File.Delete(_path); } catch (IOException) { }
    }

    static FemSchema BuildFragment(DatabaseService db, FemSchema parent)
    {
        var r = FragmentAuthoring.Build(db, parent, new StoreyFragmentRequest(1.5, 4.5, "Этаж"));
        Assert.False(r.IsError, string.Join("; ", r.Rows.Select(x => x.Message)));
        return r.Schema!;
    }

    static FragmentRebuildResult Rebuild(DatabaseService db, FemSchema fragment) =>
        FragmentAuthoring.CommitRebuild(db, fragment, FragmentAuthoring.PrepareRebuild(db, fragment));

    [Fact]
    public void Rebuild_SameId_TransfersAnalysisCheckCutBc_PicksUpParentChange()
    {
        using var db = new DatabaseService(_path);
        var parent = StoreyFragmentDbFixture.CreateParent(db);
        db.LoadAll();
        var fragment = BuildFragment(db, parent);
        int id = fragment.Id;

        var live = fragment.LoadCases.Single(c => c.Tag == "Полезная");
        db.SaveFemAnalysis(new FemAnalysis
        {
            SchemaId = id, Tag = "Лин", Kind = FemCsfeaRunner.LinearAnalysisKind,
            LoadExpressionJson = new FemLoadExpression
            {
                Mode = FemLoadExpressionMode.Single, Terms = [new FemLoadTerm { LoadCaseId = live.Id, Coefficient = 1 }],
            }.ToJson(),
        });
        var slab = db.GetFemMembers(id).Single(m => m.ElemTag == "S1");
        db.SaveFemCheck(new FemCheck { SchemaId = id, ElementId = slab.Id, NormCode = "rc_plate_check", Tag = "Плита" });
        var cuts = db.GetFragmentCutsWithIds(id);
        var bottom = cuts.First(c => c.Cut.Side == FragmentCutSide.Bottom && c.Cut.Kind == FragmentCutKind.End);
        var modes = FragmentCutBc.Encode([FragmentDofMode.Free, null, null, null, null, FragmentDofMode.Fixed]);
        db.ReplaceFragmentCutBc(id, new Dictionary<int, byte[]> { [bottom.Id] = modes });
        db.LoadAll();
        fragment = db.FemSchemas.Single(s => s.Id == id);

        // Родитель меняется: загружение переименовано — после пересборки копия берёт новое имя.
        var session = FemSchemaAuthoring.OpenSession(db, db.FemSchemas.Single(s => s.Id == parent.Id));
        session.LoadCases.Single(c => c.Tag == "Полезная").Tag = "Полезная 2";
        Assert.True(FemSchemaAuthoring.Save(db, session, out var diag), string.Join("; ", diag.Select(x => x.Message)));
        Assert.Null(FragmentAuthoring.RebuildBlocker(db, fragment));

        var impact = FragmentAuthoring.RebuildImpact(db, fragment);
        Assert.Equal((1, 1, 1), (impact.Analyses, impact.Checks, impact.CutBcOverrides));
        Assert.Equal("Каркас", impact.ParentTag);
        Assert.False(string.IsNullOrEmpty(FragmentAuthoring.RebuildQuestion(fragment, impact)));

        var r = Rebuild(db, fragment);
        Assert.False(r.IsError, string.Join("; ", r.Rows.Select(x => x.Message)));
        Assert.Equal((1, 1, 1, 0), (r.Transfer!.Analyses, r.Transfer.Checks, r.Transfer.CutBc, r.Transfer.Lost.Count));
        Assert.Equal(Loc.S("FragmentRebuildReportSummary"), r.Rows[0].Level);

        db.LoadAll();
        var after = db.FemSchemas.Single(s => s.Id == id);
        Assert.Equal("Этаж", after.Tag);
        Assert.Equal(1, db.FemSchemas.Count(s => s.ParentSchemaId == parent.Id));
        var newLive = after.LoadCases.Single(c => c.Tag == "Полезная 2");
        Assert.NotEqual(live.Id, newLive.Id);
        Assert.DoesNotContain(after.LoadCases, c => c.Tag == "Полезная");

        var analysis = Assert.Single(db.GetFemAnalyses(id));
        Assert.Equal(newLive.Id, Assert.Single(FemLoadExpression.Parse(analysis.LoadExpressionJson).Terms).LoadCaseId);
        var check = Assert.Single(db.FemChecks, c => c.SchemaId == id);
        Assert.Equal(db.GetFemMembers(id).Single(m => m.ElemTag == "S1").Id, check.ElementId);

        var bc = Assert.Single(db.GetFragmentCutBc(id));
        var cut = db.GetFragmentCutsWithIds(id).Single(c => c.Id == bc.Key).Cut;
        Assert.Equal(FragmentRebuildTransfer.CutKey(bottom.Cut), FragmentRebuildTransfer.CutKey(cut));
        Assert.Equal(modes, bc.Value);

        Assert.Null(db.GetFragmentMasks(id, items: false));
        Assert.Equal(FragmentParentStatus.Unchanged, StoreyFragmentService.CheckParent(db, after));
    }

    [Fact]
    public void Rebuild_LostMemberCheckAndLoadCase_WarningRows()
    {
        using var db = new DatabaseService(_path);
        var parent = StoreyFragmentDbFixture.CreateParent(db);
        db.LoadAll();
        var fragment = BuildFragment(db, parent);
        int id = fragment.Id;
        // Проверка ссылается на КонЭ, которого после пересборки во фрагменте нет; постановка — на чужое загружение.
        db.SaveFemCheck(new FemCheck { SchemaId = id, ElementId = 987654, NormCode = "rc_check", Tag = "Пропавшая" });
        db.SaveFemAnalysis(new FemAnalysis
        {
            SchemaId = id, Tag = "Чужая", Kind = FemCsfeaRunner.LinearAnalysisKind,
            LoadExpressionJson = new FemLoadExpression
            {
                Mode = FemLoadExpressionMode.Single, Terms = [new FemLoadTerm { LoadCaseId = 987654, Coefficient = 1 }],
            }.ToJson(),
        });
        db.LoadAll();
        fragment = db.FemSchemas.Single(s => s.Id == id);

        var r = Rebuild(db, fragment);
        Assert.False(r.IsError, string.Join("; ", r.Rows.Select(x => x.Message)));
        Assert.Equal(2, r.Transfer!.Lost.Count);
        Assert.Equal(["fragment_rebuild_load_cases_dangling", "fragment_rebuild_check_lost"],
            r.Transfer.Lost.Select(x => x.Code).OrderByDescending(x => x).ToArray());
        Assert.Equal(3, r.Rows.Count);
        Assert.DoesNotContain(r.Rows, x => x.IsError);
        db.LoadAll();
        Assert.DoesNotContain(db.FemChecks, c => c.SchemaId == id);
        Assert.Single(db.GetFemAnalyses(id));
    }

    /// <summary>
    /// Ручная проверка 28.11: консоль готовит черновик фрагмента до записи документа, а запись документа пересохраняет
    /// родителя (Id загружений новые) — ссылки копий вели на старые Id, и пересборка после переименования загружения
    /// родителя теряла загружения постановки («загружений «Пост», «Врем» больше нет», затем «#5, #6»).
    /// </summary>
    [Fact]
    public void Rebuild_ParentSavedBetweenPrepareAndCommit_LinksFollowAndAnalysisKeepsLoadCases()
    {
        using var db = new DatabaseService(_path);
        var parent = StoreyFragmentDbFixture.CreateParent(db);
        db.LoadAll();
        var request = new StoreyFragmentRequest(1.5, 4.5, "Этаж");
        var prepared = StoreyFragmentService.Prepare(db, parent.Id, request);

        // Родитель пересохранён без изменений — Id его загружений меняются.
        var session = FemSchemaAuthoring.OpenSession(db, db.FemSchemas.Single(s => s.Id == parent.Id));
        var before = session.LoadCases.Select(c => c.Id).ToList();
        Assert.True(FemSchemaAuthoring.Save(db, session, out var diag), string.Join("; ", diag.Select(x => x.Message)));
        Assert.DoesNotContain(db.GetFemLoadCases(parent.Id), c => before.Contains(c.Id));

        var built = FragmentAuthoring.Commit(db, db.FemSchemas.Single(s => s.Id == parent.Id), request, prepared);
        Assert.False(built.IsError, string.Join("; ", built.Rows.Select(x => x.Message)));
        int id = built.Schema!.Id;
        var parentByTag = db.GetFemLoadCases(parent.Id).ToDictionary(c => c.Tag, c => (int?)c.Id);
        Assert.All(db.GetFemLoadCases(id), c => Assert.Equal(parentByTag[c.Tag], c.ParentLoadCaseId));
        Assert.All(built.Schema.LoadCases, c => Assert.Equal(parentByTag[c.Tag], c.ParentLoadCaseId));

        int live = db.GetFemLoadCases(id).Single(c => c.Tag == "Полезная").Id;
        db.SaveFemAnalysis(new FemAnalysis
        {
            SchemaId = id, Tag = "Лин", Kind = FemCsfeaRunner.LinearAnalysisKind,
            LoadExpressionJson = new FemLoadExpression
            {
                Mode = FemLoadExpressionMode.Single, Terms = [new FemLoadTerm { LoadCaseId = live, Coefficient = 1 }],
            }.ToJson(),
        });
        db.LoadAll();

        // Как на ручной проверке: загружение родителя переименовано, затем две пересборки подряд.
        session = FemSchemaAuthoring.OpenSession(db, db.FemSchemas.Single(s => s.Id == parent.Id));
        session.LoadCases.Single(c => c.Tag == "Полезная").Tag = "Полезная 2";
        Assert.True(FemSchemaAuthoring.Save(db, session, out diag), string.Join("; ", diag.Select(x => x.Message)));
        for (int pass = 0; pass < 2; pass++)
        {
            var r = Rebuild(db, db.FemSchemas.Single(s => s.Id == id));
            Assert.False(r.IsError, string.Join("; ", r.Rows.Select(x => x.Message)));
            Assert.Empty(r.Transfer!.Lost);
            var renamed = db.GetFemLoadCases(id).Single(c => c.Tag == "Полезная 2").Id;
            var analysis = Assert.Single(db.GetFemAnalyses(id));
            Assert.Equal(renamed, Assert.Single(FemLoadExpression.Parse(analysis.LoadExpressionJson).Terms).LoadCaseId);
        }
    }

    /// <summary>Ссылки копий испорчены прежней записью (база с ручной проверки) — пересборка сопоставляет по тегу.</summary>
    [Fact]
    public void Rebuild_BrokenParentLinks_MapsByTag()
    {
        using var db = new DatabaseService(_path);
        var parent = StoreyFragmentDbFixture.CreateParent(db);
        db.LoadAll();
        int id = BuildFragment(db, parent).Id;
        int live = db.GetFemLoadCases(id).Single(c => c.Tag == "Полезная").Id;
        db.SaveFemAnalysis(new FemAnalysis
        {
            SchemaId = id, Tag = "Лин", Kind = FemCsfeaRunner.LinearAnalysisKind,
            LoadExpressionJson = new FemLoadExpression
            {
                Mode = FemLoadExpressionMode.Single, Terms = [new FemLoadTerm { LoadCaseId = live, Coefficient = 1 }],
            }.ToJson(),
        });
        using (var c = new SqliteConnection($"Data Source={_path};Pooling=False"))
        {
            c.Open();
            using var cmd = c.CreateCommand();
            cmd.CommandText = $"UPDATE fem_load_cases SET parent_load_case_id = parent_load_case_id + 100000 WHERE schema_id={id}";
            Assert.True(cmd.ExecuteNonQuery() > 0);
        }
        db.LoadAll();

        var r = Rebuild(db, db.FemSchemas.Single(s => s.Id == id));
        Assert.False(r.IsError, string.Join("; ", r.Rows.Select(x => x.Message)));
        Assert.Empty(r.Transfer!.Lost);
        var analysis = Assert.Single(db.GetFemAnalyses(id));
        Assert.Equal(db.GetFemLoadCases(id).Single(c => c.Tag == "Полезная").Id,
            Assert.Single(FemLoadExpression.Parse(analysis.LoadExpressionJson).Terms).LoadCaseId);
    }

    [Fact]
    public void RebuildBlocker_NotFragment_ParentDeleted()
    {
        using var db = new DatabaseService(_path);
        var parent = StoreyFragmentDbFixture.CreateParent(db);
        db.LoadAll();
        Assert.Equal(Loc.S("FragmentRebuildNotFragment"), FragmentAuthoring.RebuildBlocker(db, parent));
        var fragment = BuildFragment(db, parent);
        db.DeleteFemSchema(db.FemSchemas.Single(s => s.Id == parent.Id));
        Assert.Equal(Loc.S("FragmentRebuildParentDeleted"), FragmentAuthoring.RebuildBlocker(db, fragment));
        var r = Rebuild(db, fragment);
        Assert.True(r.IsError);
        Assert.Contains(r.Rows, x => x.Message == Loc.S("FragmentRebuildParentDeleted"));
    }
}
