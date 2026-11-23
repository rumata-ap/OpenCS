using CScore;
using CScore.Combinations;
using Microsoft.Data.Sqlite;
using OpenCS.Services;
using OpenCS.Utilites;
using Xunit;

namespace OpenCS.Tests;

/// <summary>
/// Служба наборов усилий: проверка с путями документа, запись, копия без потерь, удаление используемого запрещено,
/// CSV с запятой и точкой, имя СП 20 не переписывается, сочетания СП 20 без частичного результата.
/// </summary>
[Collection("Loc language")]
public sealed class ForceSetAuthoringTests : IDisposable
{
    readonly string _dir = Path.Combine(Path.GetTempPath(), $"opencs_forces_{Guid.NewGuid():N}");
    public ForceSetAuthoringTests()
    {
        Directory.CreateDirectory(_dir);
        Loc.SetLanguage("ru-RU");
    }
    public void Dispose()
    {
        Loc.SetLanguage(null);
        SqliteConnection.ClearAllPools();
        try { Directory.Delete(_dir, true); } catch (IOException) { }
    }

    string PathOf(string name) => Path.Combine(_dir, name);

    internal static ForceSet Bar(string tag = "G: Вес", params double[][] rows)
    {
        var fs = new ForceSet { Tag = tag, Kind = "bar" };
        if (rows.Length == 0) rows = [[-100, 10, 0, 0, 5, 0], [-90, -12, 1, 0, 4, 0]];
        foreach (var r in rows)
            fs.Items.Add(new LoadItem { Label = (fs.Items.Count + 1).ToString(), N = r[0], Mx = r[1], My = r[2], Vx = r[3], Vy = r[4], T = r[5] });
        return fs;
    }

    internal static ForceSet Shell(string tag = "Q: Ветер [ветер]")
    {
        var fs = new ForceSet { Tag = tag, Kind = "shell" };
        fs.ShellItems.Add(new ShellLoadItem { Label = "1", Mx = 12.5, My = 4, Mxy = 0.5, Qx = 1 });
        fs.ShellItems.Add(new ShellLoadItem { Label = "2", SigmaX = 100, TauXY = -20, SourceElementNum = 15, SourceSectionNum = 1 });
        return fs;
    }

    [Fact]
    public void Validate_PathsByField()
    {
        Assert.Empty(ForceSetAuthoring.Validate(Bar()));
        Assert.Empty(ForceSetAuthoring.Validate(Shell()));

        var bad = Bar(" ");
        bad.Items[1].Mx = double.NaN;
        var errs = ForceSetAuthoring.Validate(bad);
        Assert.Contains(errs, e => e.Path == "tag");
        Assert.Contains(errs, e => e.Path == "rows[1].Mx");

        Assert.Contains(ForceSetAuthoring.Validate(new ForceSet { Tag = "x", Kind = "bar" }), e => e.Path == "rows");
        Assert.Contains(ForceSetAuthoring.Validate(new ForceSet { Tag = "x", Kind = "beam" }), e => e.Path == "kind");
        Assert.Contains(ForceSetAuthoring.Validate(Bar("Q(γf=0): Снег")), e => e.Path == "gammaF");

        var sh = Shell();
        sh.ShellItems[0].SigmaY = double.PositiveInfinity;
        Assert.Contains(ForceSetAuthoring.Validate(sh), e => e.Path == "rows[0].sigmaY");
    }

    [Fact]
    public void Save_NewSet_NumberedInPool_RowsAndIdsPersist()
    {
        using var db = new DatabaseService(PathOf("s.db"));
        var a = Bar();
        Assert.Empty(ForceSetAuthoring.Save(db, a));
        var b = Shell();
        Assert.Empty(ForceSetAuthoring.Save(db, b));
        Assert.Equal(1, a.Num);
        Assert.Equal(2, b.Num);   // один ряд номеров для стержней и пластин
        Assert.Contains(a, db.ForceSets);
        Assert.All(a.Items, i => Assert.True(i.Id > 0));

        var empty = new ForceSet { Tag = "Пусто", Kind = "bar" };
        Assert.Contains(ForceSetAuthoring.Save(db, empty), e => e.Path == "rows");
        Assert.Equal(0, empty.Id);
        Assert.DoesNotContain(empty, db.ForceSets);
    }

    [Fact]
    public void Draft_EditDoesNotTouchModel_CopyIntoKeepsRowIds()
    {
        using var db = new DatabaseService(PathOf("d.db"));
        var fs = Bar();
        ForceSetAuthoring.Save(db, fs);
        int firstId = fs.Items[0].Id;
        string before = ForceSetAuthoring.Fingerprint(fs);

        var draft = ForceSetAuthoring.Draft(fs);
        draft.Items[0].N = -500;
        draft.Tag = "G: Вес 2";
        Assert.Equal(before, ForceSetAuthoring.Fingerprint(fs));
        Assert.NotEqual(before, ForceSetAuthoring.Fingerprint(draft));

        ForceSetAuthoring.CopyInto(draft, fs);
        Assert.Empty(ForceSetAuthoring.Save(db, fs));
        Assert.Equal(firstId, fs.Items[0].Id);   // ссылка задачи на строку цела
        Assert.Equal(-500, fs.Items[0].N);
    }

    [Fact]
    public void Duplicate_KeepsSecondOrderImperfectionsSourceAndElements()
    {
        using var db = new DatabaseService(PathOf("c.db"));
        var src = Shell();
        src.SecondOrder = true;
        src.Imperfections = "Крен X";
        src.SourceType = "table";
        ForceSetAuthoring.Save(db, src);

        var copy = ForceSetAuthoring.Duplicate(db, src);
        Assert.Equal("Q: Ветер [ветер] (копия)", copy.Tag);
        Assert.True(copy.SecondOrder);
        Assert.Equal("Крен X", copy.Imperfections);
        Assert.Equal("table", copy.SourceType);
        Assert.Equal(15, copy.ShellItems[1].SourceElementNum);
        Assert.Equal(100, copy.ShellItems[1].SigmaX);
        Assert.NotEqual(src.ShellItems[0].Id, copy.ShellItems[0].Id);
        copy.Tag = src.Tag;
        Assert.Equal(ForceSetAuthoring.Fingerprint(src), ForceSetAuthoring.Fingerprint(copy));
        Assert.Contains(copy, db.ForceSets);
    }

    [Fact]
    public void TryDelete_UsedByTaskStageFemMemberGroupAndCheck_Refused()
    {
        string path = PathOf("u.db");
        using var db = new DatabaseService(path);
        var byTask = Bar("Q: задача");
        var byStage = Bar("Q: этап");
        var byMember = Bar("Q: КонЭ");
        var byGroup = Bar("Q: группа");
        var byCheck = Bar("Q: проверка");
        var byNl = Shell("L: NL");
        var free = Bar("Q: свободный");
        foreach (var fs in new[] { byTask, byStage, byMember, byGroup, byCheck, byNl, free })
            Assert.Empty(ForceSetAuthoring.Save(db, fs));

        db.SaveCalcTask(new CalcTask { Num = 1, Tag = "НДС-1", Kind = "strain_state", ForceSetId = byTask.Id, ForceItemId = byTask.Items[1].Id });
        db.SaveCalcTask(new CalcTask
        {
            Num = 2, Tag = "История", Kind = "load_history",
            ParamsJson = "{\"Stages\":[{\"Kind\":\"Load\",\"Force\":{\"Mode\":\"item\",\"ForceSetId\":" + byStage.Id
                         + ",\"ForceItemId\":" + byStage.Items[0].Id + "}}]}"
        });
        string nlParams = "{\"NlForceSetId\":" + byNl.Id + "}";
        using (var c = new SqliteConnection($"Data Source={path}"))
        {
            c.Open();
            using var cmd = c.CreateCommand();
            cmd.CommandText = $$"""
                PRAGMA foreign_keys=OFF;
                INSERT INTO fem_members (schema_id, elem_tag, force_set_id) VALUES (1, 'K1', {{byMember.Id}});
                INSERT INTO fem_member_groups (schema_id, tag, force_set_id) VALUES (1, 'Плиты', {{byGroup.Id}});
                INSERT INTO fem_checks (schema_id, member_id, tag, force_set_ids_json, params_json) VALUES (1, 1, 'Сталь', '[{{byCheck.Id}}]', '{}');
                INSERT INTO fem_checks (schema_id, member_id, tag, force_set_ids_json, params_json) VALUES (1, 1, 'Трещины', '[]', '{{nlParams}}');
                """;
            cmd.ExecuteNonQuery();
        }

        var u = db.ForceSetReferences(byTask.Id);
        Assert.Equal(["НДС-1"], u.Tasks);
        Assert.Contains(byTask.Items[1].Id, u.ItemIds);
        Assert.Contains(byStage.Items[0].Id, db.ForceSetReferences(byStage.Id).ItemIds);

        foreach (var (fs, expect) in new[]
                 {
                     (byTask, "задача «НДС-1»"), (byStage, "задача «История»"), (byMember, "КонЭ схем МКЭ: 1"),
                     (byGroup, "группа КЭ «Плиты»"), (byCheck, "проверка МКЭ «Сталь»"), (byNl, "проверка МКЭ «Трещины»")
                 })
        {
            Assert.False(ForceSetAuthoring.TryDelete(db, fs, out var usedBy));
            Assert.Contains(expect, usedBy);
            Assert.Contains(fs, db.ForceSets);
        }
        Assert.True(ForceSetAuthoring.TryDelete(db, free, out var none));
        Assert.Empty(none);
        Assert.DoesNotContain(free, db.ForceSets);
    }

    [Fact]
    public void Csv_CommaAndDot_Elements_ErrorsWithLine()
    {
        var (bar, _, errs) = ForceSetAuthoring.ParseCsv(["﻿Label;N;Mx;My", "1;-12,5;3.25;", "2;7;;1e2"], "bar");
        Assert.Empty(errs);
        Assert.Equal(2, bar.Count);
        Assert.Equal(-12.5, bar[0].N);
        Assert.Equal(3.25, bar[0].Mx);
        Assert.Equal(0, bar[0].My);
        Assert.Equal(100, bar[1].My);

        (bar, _, errs) = ForceSetAuthoring.ParseCsv(["Label,N,Mx", "1,-12.5,3", "2,\"1,5\",0"], "bar");
        Assert.Single(errs);   // при разделителе «,» запятая — не десятичный знак
        Assert.Contains("Строка 3", errs[0]);

        (bar, _, errs) = ForceSetAuthoring.ParseCsv(["Label;N;Mz", "1;2;3"], "bar");
        Assert.Contains("«Mz»", Assert.Single(errs));

        (bar, _, errs) = ForceSetAuthoring.ParseCsv(["Label;N", "1;abc", "2;1,000.5"], "bar");
        Assert.Equal(2, errs.Count);
        Assert.Contains("Строка 2", errs[0]);

        Assert.Single(ForceSetAuthoring.ParseCsv(["Label;N"], "bar").Errors);
        Assert.Single(ForceSetAuthoring.ParseCsv([], "bar").Errors);

        var (_, shell, e2) = ForceSetAuthoring.ParseCsv(["Label;Mx;SigmaX;TauXY;Elem;Sect", "э.15 с1;1,5;;-20;15;1"], "shell");
        Assert.Empty(e2);
        Assert.Null(shell[0].SigmaX);
        Assert.Equal(-20, shell[0].TauXY);
        Assert.Equal(15, shell[0].SourceElementNum);
        Assert.Equal(1, shell[0].SourceSectionNum);
    }

    [Fact]
    public void Csv_WriteRead_RoundTrip()
    {
        foreach (var fs in new[] { Bar(), Shell() })
        {
            fs.Items.ForEach(i => i.Label = "a;b \"q\"");
            string file = PathOf($"{fs.Kind}.csv");
            ForceSetAuthoring.WriteCsv(file, fs);
            var (bar, shell, errs) = ForceSetAuthoring.ReadCsv(file, fs.Kind);
            Assert.Empty(errs);
            var back = new ForceSet { Tag = fs.Tag, Kind = fs.Kind, Items = bar, ShellItems = shell };
            Assert.Equal(ForceSetAuthoring.Fingerprint(fs), ForceSetAuthoring.Fingerprint(back));
        }
    }

    [Fact]
    public void Sp20Name_NotSp20NameNotRewritten_ComposeRoundTrip()
    {
        Assert.False(Sp20Name.TryParse("РСУ 1", out _));
        Assert.False(Sp20Name.TryParse("СП20 — G: Вес (основное (C))", out _));
        Assert.True(Sp20Name.TryParse("Q(γf=1,4): Ветер [ветер]", out var n));
        Assert.Equal('Q', n.Prefix);
        Assert.Equal(1.4, n.GammaF);
        Assert.Equal("Ветер", n.Title);
        Assert.Equal("ветер", n.Group);
        Assert.True(Sp20Name.TryParse(n.Compose(), out var back));
        Assert.Equal(n, back);
    }

    static ForceSetAuthoring.Sp20Request Request(IReadOnlyList<ForceSet> sets, ForceSetAuthoring.Sp20Mode mode = ForceSetAuthoring.Sp20Mode.Both,
                                                 string group = "C", string? name = "Комб") =>
        new(sets, mode, group, Envelope: true, Cases: true, name, Sp20LoadValueKind.Design, Sp20GammaDefaults.Standard);

    [Fact]
    public void CombineSp20_BothModes_FourSetsInOneGo_SecondOrderCarried()
    {
        using var db = new DatabaseService(PathOf("k.db"));
        var g = Bar("G: Вес");
        var q = Bar("Q: Снег", [-10, 2, 0, 0, 1, 0], [-8, -3, 0, 0, 1, 0]);
        g.SecondOrder = q.SecondOrder = true;
        ForceSetAuthoring.Save(db, g);
        ForceSetAuthoring.Save(db, q);

        var (created, _, errors) = ForceSetAuthoring.CombineSp20(db, Request([g, q]));
        Assert.Empty(errors);
        Assert.Equal(["Комб (основное (C))", "Комб (основное (C) — список)", "Комб (особое)", "Комб (особое — список)"],
                     created.Select(c => c.Tag).ToArray());
        Assert.All(created, c => { Assert.True(c.Id > 0); Assert.True(c.SecondOrder); Assert.Contains(c, db.ForceSets); });
        Assert.Equal([3, 4, 5, 6], created.Select(c => c.Num).ToArray());
    }

    [Fact]
    public void CombineSp20_Errors_NothingWritten()
    {
        using var db = new DatabaseService(PathOf("e.db"));
        var g = Bar("G: Вес");
        var q = Bar("Q: Снег", [-10, 2, 0, 0, 1, 0]);   // другое число строк
        ForceSetAuthoring.Save(db, g);
        ForceSetAuthoring.Save(db, q);
        int before = db.ForceSets.Count;

        Assert.NotEmpty(ForceSetAuthoring.CombineSp20(db, Request([g, q])).Errors);
        Assert.NotEmpty(ForceSetAuthoring.CombineSp20(db, Request([])).Errors);
        Assert.NotEmpty(ForceSetAuthoring.CombineSp20(db, Request([g], group: "X")).Errors);
        Assert.NotEmpty(ForceSetAuthoring.CombineSp20(db, Request([g]) with { Envelope = false, Cases = false }).Errors);
        Assert.Equal(before, db.ForceSets.Count);

        using var c = new SqliteConnection($"Data Source={PathOf("e.db")}");
        c.Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM force_sets";
        Assert.Equal(2L, (long)cmd.ExecuteScalar()!);
    }
}
