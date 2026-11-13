using System.Text.Json;
using static CScore.Batch.BatchVerdictText;

namespace CScore.Batch;

/// <summary>
/// Причина строки пакета для любых результатов: объект <c>verdict</c>, если он есть; иначе
/// восстановление из полей, которые пакет писал до 13.11.2026 (пересчёт не нужен). Где данных
/// не хватает (предел acrc старых ПС2), Кисп остаётся пустым, статус и текст — из того, что есть.
/// </summary>
public static class BatchRowVerdictReader
{
    /// <summary>Причина строки row пакета вида batchKind; никогда не null.</summary>
    public static BatchRowVerdict Read(string batchKind, JsonElement row)
    {
        if (BatchRowVerdict.TryRead(row) is { } stored) return stored;
        if (row.ValueKind != JsonValueKind.Object) return BatchRowVerdict.Error("строка результата не прочитана");

        if (batchKind.StartsWith("shell_simpl_capri_design_", StringComparison.Ordinal))
            return Bool(row, "Feasible") == true
                ? BatchRowVerdict.Converged()
                : new BatchRowVerdict(BatchRowStatus.NotPassed, null, "design_infeasible", "",
                    Str(row, "Diagnostics") is { Length: > 0 } d ? d : "армирование не подобрано");

        string status = Str(row, "status");
        if (status == "error") return BatchRowVerdict.Error(Str(row, "error"));
        if (status == "not_applicable") return BatchRowVerdict.NotApplicable(Str(row, "reason_code"), Str(row, "reason"));
        if (status == "not_converged" || Bool(row, "converged") == false)
            return BatchRowVerdict.NotConverged("НДС не найден");

        return batchKind switch
        {
            "strength_ndm_batch" => Ndm(row),
            _ when batchKind.StartsWith("limit_", StringComparison.Ordinal) => Limit(row),
            "crack_width_batch" => CrackWidth(row),
            "shell_layered_sls_batch" => ShellSls(row, status),
            "shell_layered_uls_batch" => new BatchRowVerdict(
                status == "ok" ? BatchRowStatus.Ok : BatchRowStatus.NotPassed, null, "shell_uls",
                Str(row, "formula"), Str(row, "note")),
            _ when batchKind.StartsWith("shell_simpl_", StringComparison.Ordinal) => ShellSimpl(row, status),
            "shear_inclined_batch" => Shear(row),
            "fire_r_check_batch" => Fire(row),
            _ => BatchRowVerdict.Converged(),
        };
    }

    static BatchRowVerdict Ndm(JsonElement row)
    {
        double eb = Math.Abs(Dbl(row, "eps_concrete_compression") ?? 0), ebu = Math.Abs(Dbl(row, "eps_concrete_ult") ?? 0);
        double es = Dbl(row, "eps_rebar_tension") ?? 0, esu = Dbl(row, "eps_rebar_ult") ?? 0;
        return NdmVerdict(eb, ebu, es, esu);
    }

    /// <summary>Причина проверки прочности по НДМ: определяющая из εb/εb,ult и εs/εs,ult (п. 8.1.30).</summary>
    public static BatchRowVerdict NdmVerdict(double epsB, double epsBUlt, double epsS, double epsSUlt)
    {
        double eb = Math.Abs(epsB), ebu = Math.Abs(epsBUlt);
        double ub = Ratio(eb, ebu);
        double us = Ratio(Math.Max(epsS, 0), epsSUlt);
        return BatchRowVerdict.Worst(
            new BatchCriterion("eps_b", ub, "п. 8.1.30", $"εb = {N(eb)} {Cmp(ub)} εb,ult = {N(ebu)}"),
            new BatchCriterion("eps_s", us, "п. 8.1.30", $"εs = {N(epsS)} {Cmp(us)} εs,ult = {N(epsSUlt)}"));
    }

    static BatchRowVerdict Limit(JsonElement row)
    {
        double u = Dbl(row, "utilization") ?? double.PositiveInfinity;
        string gov = Str(row, "governing") is { Length: > 0 } g ? g : "both";
        return BatchRowVerdict.Worst(new BatchCriterion($"limit_{gov}", u, "", $"Кисп = {F3(u)}"));
    }

    static BatchRowVerdict CrackWidth(JsonElement row)
    {
        // До 13.11 пределы acrc в строку не писались — Кисп по ширине неизвестен, есть только флажки.
        double? us = Dbl(row, "util_steel");
        var crit = new List<BatchCriterion>();
        if (Bool(row, "passed_long") == false)
            crit.Add(new("acrc_long", null, "", $"acrc,1 = {F3(Dbl(row, "acrc_long") ?? 0)} мм > acrc,ult"));
        if (Bool(row, "passed_short") == false)
            crit.Add(new("acrc_short", null, "", $"acrc = {F3(Dbl(row, "acrc_short") ?? 0)} мм > acrc,ult"));
        var steel = new BatchCriterion("sigma_s", us, "", $"σs/Rs,ser = {F3(us ?? 0)}");
        if (Bool(row, "passed_steel") == false || crit.Count == 0) crit.Insert(0, steel);
        var first = crit[0];
        bool failed = Bool(row, "passed_long") == false || Bool(row, "passed_short") == false || Bool(row, "passed_steel") == false;
        return new BatchRowVerdict(failed ? BatchRowStatus.NotPassed : BatchRowStatus.Ok, first.Util, first.Code, first.Ref, first.Text);
    }

    static BatchRowVerdict ShellSls(JsonElement row, string status)
    {
        double acrc = Dbl(row, "acrc_mm") ?? 0;
        string dir = Str(row, "direction"), face = Str(row, "face");
        string where = dir.Length > 0 ? $", {dir} / {face}" : "";
        return new BatchRowVerdict(status == "ok" ? BatchRowStatus.Ok : BatchRowStatus.NotPassed, null, "acrc", "",
            $"acrc = {F3(acrc)} мм{where}");
    }

    static BatchRowVerdict ShellSimpl(JsonElement row, string status)
    {
        double? em = Dbl(row, "eta_max"), eq = Dbl(row, "eta_q");
        var crit = new List<BatchCriterion> { new("eta_m", em, "", em is { } m ? $"η = {F3(m)}" : "") };
        if (eq is { } q) crit.Add(new("eta_q", q, "", $"ηQ = {F3(q)}"));
        var v = BatchRowVerdict.Worst(crit.ToArray());
        return status != "ok" && v.IsPassed ? v with { Status = BatchRowStatus.NotPassed } : v;
    }

    static BatchRowVerdict Shear(JsonElement row)
    {
        // null у failed — нулевая несущая способность, то есть +∞.
        double u = Dbl(row, "utilization") ?? double.PositiveInfinity;
        return BatchRowVerdict.Worst(new BatchCriterion("shear", u, Str(row, "worstFormula"), $"Кисп = {F3(u)}"));
    }

    static BatchRowVerdict Fire(JsonElement row)
    {
        double f = Dbl(row, "factor") ?? 0;
        string gov = Str(row, "governing") is { Length: > 0 } g ? g : "r";
        return FireVerdict(f, gov);
    }

    /// <summary>Причина R-проверки: Кисп = 1/запас (запас ≤ 0 — ∞).</summary>
    public static BatchRowVerdict FireVerdict(double factor, string governing)
    {
        double u = factor > 0 ? 1.0 / factor : double.PositiveInfinity;
        return BatchRowVerdict.Worst(new BatchCriterion($"fire_{governing}", u, "", $"запас R = {F3(factor)}"));
    }

    static double Ratio(double a, double lim) => lim > 0 ? a / lim : a > 0 ? double.PositiveInfinity : 0;
    static string Cmp(double u) => u > BatchRowVerdict.Threshold ? ">" : "≤";

    static string Str(JsonElement e, string k)
        => e.TryGetProperty(k, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : "";
    static double? Dbl(JsonElement e, string k)
        => e.TryGetProperty(k, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetDouble() : null;
    static bool? Bool(JsonElement e, string k)
        => e.TryGetProperty(k, out var v) && v.ValueKind is JsonValueKind.True or JsonValueKind.False ? v.GetBoolean() : null;
}
