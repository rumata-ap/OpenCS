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
        Assert.Equal(["fragment_rebuild_load_cases_lost", "fragment_rebuild_check_lost"],
            r.Transfer.Lost.Select(x => x.Code).OrderByDescending(x => x).ToArray());
        Assert.Equal(3, r.Rows.Count);
        Assert.DoesNotContain(r.Rows, x => x.IsError);
        db.LoadAll();
        Assert.DoesNotContain(db.FemChecks, c => c.SchemaId == id);
        Assert.Single(db.GetFemAnalyses(id));
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
