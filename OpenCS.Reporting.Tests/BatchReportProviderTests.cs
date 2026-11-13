using System.Text;
using CScore;
using OpenCS.Reporting;
using Xunit;

namespace OpenCS.Reporting.Tests;

/// <summary>Отчёт по пакету: шапка с итогом, сводная таблица строк, ход определяющей строки.</summary>
public sealed class BatchReportProviderTests
{
    const string Json = """
        {"verdict_summary":{"ok":1,"not_passed":1,"not_converged":1,"not_applicable":0,"error":0,"governing_num":2},
         "rows":[
          {"num":1,"label":"a","N":-100,"Mx":10,"My":0,"verdict":{"status":"ok","util":0.5,"util_inf":false,"code":"eps_b","ref":"п. 8.1.24, (8.37)","text":"εb ≤ εb,ult"}},
          {"num":2,"label":"b","N":-100,"Mx":50,"My":0,"verdict":{"status":"not_passed","util":1.3,"util_inf":false,"code":"eps_b","ref":"п. 8.1.24, (8.37)","text":"εb > εb,ult"}},
          {"num":3,"label":"c","N":-100,"Mx":90,"My":0,"status":"not_converged"}]}
        """;

    static ReportContext Ctx(string json = Json, string kind = "strength_ndm_batch")
        => new(new CalcTask { Id = 5, Num = 3, Tag = "пакет", Kind = kind },
               new CalcResult { TaskKind = kind, Status = "partial", DataJson = json });

    static string AllText(ReportDocument doc)
    {
        var sb = new StringBuilder(doc.Title).AppendLine();
        foreach (var b in doc.Blocks)
            switch (b)
            {
                case ReportHeading h: sb.AppendLine(h.Text); break;
                case ReportParagraph p: sb.AppendLine(p.Text); break;
                case ReportWarning w: sb.AppendLine(w.Text); break;
                case ReportKeyValueTable kv: foreach (var r in kv.Rows) sb.AppendLine($"{r.Key}: {r.Value}"); break;
                case ReportTable t: foreach (var r in t.Rows) sb.AppendLine(string.Join(" | ", r)); break;
            }
        return sb.ToString();
    }

    [Fact]
    public void Data_ParsesCountsAndGoverning()
    {
        var d = BatchReportData.Parse("strength_ndm_batch", Json);
        Assert.Equal(3, d.Rows.Count);
        Assert.Equal(2, d.GoverningNum);
        Assert.Equal(1, d.Count("not_passed"));
        Assert.Equal(1, d.Count("not_converged"));
        Assert.Equal(["N", "Mx", "My"], d.ForceKeys);
    }

    [Fact]
    public void Build_TableHasAllRowsAndGoverningMark()
    {
        var doc = new BatchReportProvider().Build(Ctx());
        var table = doc.Blocks.OfType<ReportTable>().Single();
        Assert.Equal(3, table.Rows.Count);
        Assert.Contains(table.Rows, r => r[0].Contains("★") && r[0].Contains('2'));
        string text = AllText(doc);
        Assert.Contains("εb > εb,ult", text);
        Assert.Contains("не сошлось", text);
        Assert.Contains("№ 2", text);   // без расчёта определяющей строки — указание создать задачу
    }

    [Fact]
    public void Build_WithDetail_AppendsGoverningDocument()
    {
        var detail = new ReportDocument("НДС строки 2").Add(new ReportHeading(1, "Ход вычислений")).Add(new ReportParagraph("подробно"));
        var doc = new BatchReportProvider().Build(Ctx(), detail);
        string text = AllText(doc);
        Assert.Contains("Ход вычислений", text);
        Assert.Contains("подробно", text);
        // Заголовки вложенного документа сдвинуты на уровень вниз.
        Assert.Contains(doc.Blocks.OfType<ReportHeading>(), h => h.Text == "Ход вычислений" && h.Level == 2);
    }

    [Fact]
    public void Build_LargeBatch_ShowsFailedAndTop()
    {
        var inv = System.Globalization.CultureInfo.InvariantCulture;
        var rows = Enumerable.Range(1, 600).Select(i =>
        {
            bool fail = i % 50 == 0;
            string util = (fail ? 1.5 : i / 1000.0).ToString(inv);
            return "{\"num\":" + i + ",\"label\":\"r" + i + "\",\"verdict\":{\"status\":\"" + (fail ? "not_passed" : "ok")
                + "\",\"util\":" + util + ",\"util_inf\":false,\"code\":\"x\",\"ref\":\"\",\"text\":\"\"}}";
        });
        string json = "{\"rows\":[" + string.Join(",", rows) + "]}";
        var doc = new BatchReportProvider().Build(Ctx(json));
        var table = doc.Blocks.OfType<ReportTable>().Single();
        // 12 не прошедших + 20 наибольших Кисп (пересекаются по не прошедшим) — не больше 32 и не меньше 20.
        Assert.InRange(table.Rows.Count, 20, 32);
        Assert.Contains("Показано", AllText(doc));
    }

    [Fact]
    public void Supports_AllBatchKinds()
    {
        var p = new BatchReportProvider();
        foreach (var kind in new[]
        {
            "limit_force_batch", "limit_moment_batch", "limit_axial_batch", "strain_state_batch", "strength_ndm_batch",
            "total_curvature_batch", "cracking_batch", "crack_width_batch", "fire_r_check_batch", "shear_inclined_batch",
            "shell_simpl_wa_sls_batch", "shell_simpl_wa_uls_batch", "shell_simpl_capri_sls_batch", "shell_simpl_capri_uls_batch",
            "shell_simpl_capri_design_uls_batch", "shell_simpl_capri_design_sls_batch",
            "shell_strain_state_batch", "shell_layered_uls_batch", "shell_layered_sls_batch",
        })
            Assert.True(p.CanHandle(new CalcTask { Kind = kind }), kind);
        Assert.True(ReportProviderRegistry.CreateDefault().TryResolve(new CalcTask { Kind = "crack_width_batch" }, out var r));
        Assert.IsType<BatchReportProvider>(r);
    }

    [Fact]
    public void NoUtilization_SaysKindHasNoUtilization()
    {
        // НДС-пакет: строки сошлись, Кисп у вида нет — не «Кисп не определён», а пояснение.
        const string json = """
            {"rows":[
              {"num":1,"label":"a","verdict":{"status":"ok","util":null,"util_inf":false,"code":"","ref":"","text":""}},
              {"num":2,"label":"b","verdict":{"status":"not_converged","util":null,"util_inf":false,"code":"","ref":"","text":""}}]}
            """;
        string text = AllText(new BatchReportProvider().Build(Ctx(json, "strain_state_batch")));
        Assert.Contains("Определяющая строка: не определяется — у строк этого вида расчёта нет Кисп", text);
        Assert.DoesNotContain("Определяющая строка №", text);
    }

    [Fact]
    public void NoUtilization_AllNotConverged_SaysNoneConverged()
    {
        const string json = """{"rows":[{"num":1,"label":"a","N":0,"Mx":900,"status":"not_converged"}]}""";
        string text = AllText(new BatchReportProvider().Build(Ctx(json)));
        Assert.Contains("Определяющая строка: нет — ни у одной строки Кисп не получен", text);
    }

    [Fact]
    public void ErrorResult_ReportsError()
        => Assert.Contains("нет набора", AllText(new BatchReportProvider().Build(Ctx("""{"error":"нет набора"}"""))));
}
