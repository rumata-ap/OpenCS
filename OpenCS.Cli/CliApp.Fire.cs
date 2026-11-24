using CScore;
using OpenCS.Services;
using OpenCS.Tasks;
using OpenCS.Utilites;

namespace OpenCS.Cli;

sealed partial class CliApp
{
    /// <summary>Состояние теплового расчёта огневого сечения для вывода: none, current, stale.</summary>
    static (string State, string? Reason) ThermalState(HeadlessProjectContext ctx, CScore.Fire.Entities.FireSectionDef f)
    {
        var section = ctx.CrossSections.FirstOrDefault(s => s.Id == f.SectionId);
        var (has, stale) = FireThermalRunner.State(ctx.Db, f, section);
        return !has ? ("none", null) : stale == null ? ("current", null) : ("stale", stale);
    }

    /// <summary><c>opencs list fire</c>: огневые сечения, их сечение и состояние теплового расчёта.</summary>
    Task<int> ListFireSectionsAsync(CliCall call)
    {
        using var session = call.OpenProject();
        var ctx = session.Context;
        var rows = ctx.FireSections.OrderBy(f => f.Num).ThenBy(f => f.Id).Select(f =>
        {
            var (state, reason) = ThermalState(ctx, f);
            return new
            {
                f.Id, f.Num, f.Tag,
                Section = ctx.CrossSections.FirstOrDefault(s => s.Id == f.SectionId)?.Tag,
                DurationMin = f.FireDurationMin,
                Thermal = state, StaleReason = reason,
            };
        }).ToList();

        if (call.Json) CliOutput.WriteJson(call.Out, rows);
        else
            CliOutput.WriteTable(call.Out,
                [Loc.S("CliColId"), Loc.S("CliColNum"), Loc.S("CliColTag"), Loc.S("CliColSection"), Loc.S("CliColDuration"),
                 Loc.S("CliColThermal")],
                rows.Select(r => new[]
                {
                    r.Id.ToString(), r.Num.ToString(), r.Tag, r.Section ?? Loc.S("CliNone"),
                    r.DurationMin.ToString("G", System.Globalization.CultureInfo.InvariantCulture),
                    Loc.S("CliThermal_" + r.Thermal) + (r.StaleReason is { } why ? $" ({why})" : ""),
                }));
        return Task.FromResult(CliExitCode.Ok);
    }

    /// <summary><c>opencs run fire</c>: тепловой расчёт огневых сечений (тот же, что кнопка на странице).</summary>
    Task<int> RunFireAsync(CliCall call) => RunItemsAsync(call, "CliKindFireSection", ctx =>
        ctx.FireSections.OrderBy(f => f.Num).ThenBy(f => f.Id)
            .Select(f => new RunItem("fire", "CliKindFireSection", f.Id, f.Tag, () => RunThermal(ctx, f))));

    static async Task<CalcResult?> RunThermal(HeadlessProjectContext ctx, CScore.Fire.Entities.FireSectionDef f)
    {
        var section = ctx.CrossSections.FirstOrDefault(s => s.Id == f.SectionId);
        if (section == null)
        {
            ctx.LogService.Error(string.Format(Loc.S("FireSection_SaveErrorLog"), f.Tag, Loc.S("FireSection_SectionNotFound")));
            return null;
        }
        var check = FireThermalRunner.Check(f, section);
        foreach (var w in check.Warnings) ctx.LogService.Warning($"{f.Tag}: {w}");
        if (check.Error != null)
        {
            ctx.LogService.Error($"{f.Tag}: {check.Error}");
            return null;
        }
        try
        {
            int id = await Task.Run(() => FireThermalRunner.Run(ctx.Db, f, section));
            ctx.LogService.Info(string.Format(Loc.S("FireSection_RunOkLog"), f.Tag));
            // Тепловой расчёт — не результат задачи: для вывода — Id строки расчёта и статус «ok».
            return new CalcResult { Id = id, Status = "ok", TaskTag = f.Tag, TaskKind = "fire_thermal" };
        }
        catch (Exception e) when (e is InvalidOperationException or ArgumentException)
        {
            ctx.LogService.Error(string.Format(Loc.S("FireSection_RunError"), e.Message));
            return null;
        }
    }
}
