using System.Text.Json;
using CScore;
using CScore.Fem;
using OpenCS.Services;
using OpenCS.Utilites;

namespace OpenCS.Tasks;

/// <summary>Входы проверки по КЭ, собранные <see cref="FemCheckExecutor.PrepareAsync"/>.</summary>
/// <param name="Candidates">Наборы цели (привязанные к ней и наборы схемы со строками по её КЭ).</param>
/// <param name="Selected">Наборы, выбранные для проверки.</param>
/// <param name="Lookup">Наборы для поиска парных (NL, длительных) строк.</param>
/// <param name="PerElement">Проверка идёт по КЭ (в строках наборов есть номера КЭ цели).</param>
public sealed record FemCheckRunSetup(
    IFemCheckable Target, FemCheckScope Scope, List<ForceSet> Candidates, List<ForceSet> Selected,
    List<ForceSet> Lookup, FemPerElementInputs Inputs, bool IsPlate, bool PerElement);

/// <summary>Запуск нормативной проверки по КЭ: сбор входов, готовность, расчёт, сохранение, итог в журнал.</summary>
public static class FemCheckExecutor
{
    /// <summary>
    /// Выполняет нормативную проверку цели. Если в строках усилий есть номера КЭ — проверка идёт по КЭ
    /// (каждая строка с сечением и армированием своего КЭ) в фоне, с прогрессом и отменой; иначе —
    /// по-старому, с одним сечением цели. Неполную по готовности проверку подтверждает пользователь
    /// (<see cref="IUserNotifier.Confirm"/>). Возвращает сохранённый результат; null — занято, цель не найдена,
    /// отказ, отмена или ошибка. Вызывать из UI-потока: читает коллекции проекта.
    /// </summary>
    public static async Task<CalcResult?> RunAsync(IProjectContext app, FemCheck check)
    {
        var busy = app.Busy;
        if (busy.IsBusy) return null;
        var db = app.Db;
        var log = app.LogService;

        if (await PrepareAsync(app, check) is not { } setup) return null;
        var (target, scope, candidates, selected, lookup, inputs, isPlate, perElement) = setup;

        CalcResult result;
        bool saved = false;   // результат по КЭ уже в БД (строки записаны по ходу расчёта)
        if (!perElement)
        {
            result = FemCheckRunner.RunMulti(
                check, target, inputs.TargetBarSection, inputs.PlateTemplate, check.IsAllSets ? candidates : lookup,
                (task, sect, item) => TaskRunner.Run(task, sect, item),
                inputs.ConcreteMat, inputs.RebarMat);
        }
        else
        {
            var readiness = FemCheckRunner.EvaluateReadiness(check, scope, selected, inputs);
            if (readiness.CanRun && !readiness.IsComplete)
            {
                string message = Loc.S("FemCheckIncompleteTitle") + "\n\n"
                    + FemCheckContext.ReadinessDetails(readiness, isPlate) + "\n\n"
                    + Loc.S("FemCheckIncompleteQuestion");
                if (!app.Notifier.Confirm(message, check.DisplayTag))
                    return null;
            }

            var cts = busy.BeginBusyWithCancellation(string.Format(Loc.S("FemCheckRunning"), check.DisplayTag), indeterminate: false);
            var progress = new Progress<double>(f => { if (busy.IsBusy) busy.ReportBusyProgress(f); });
            var token = cts.Token;
            int checkId = check.Id;
            try
            {
                // Строки РСУ читаются и строки результата пишутся порциями: в памяти — только порция.
                result = await Task.Run(() =>
                {
                    using var stream = db.BeginFemCheckResult();
                    var r = FemCheckRunner.RunPerElement(
                        check, target, scope, selected, inputs,
                        (task, sect, item) => TaskRunner.Run(task, sect, item), progress, token, stream);
                    stream.Complete(r, checkId);
                    return r;
                });
                saved = true;
                busy.EndBusy();
            }
            catch (OperationCanceledException)
            {
                // Отменённая проверка результат не сохраняет.
                busy.EndBusy(Loc.S("CalcTaskCancelled"));
                return null;
            }
            catch (Exception ex)
            {
                busy.EndBusy();
                log.Error($"FemCheck «{check.DisplayTag}»: {ex.Message}");
                return null;
            }
        }

        if (!saved)
            db.SaveCalcResultRaw(result, check.Id);
        check.ResultId = result.Id;
        db.SaveFemCheck(check);
        // Строки наборов проверке больше не нужны (на РСУ SCAD — сотни мегабайт): при следующем
        // обращении они прочитаются из БД.
        foreach (var fs in lookup) fs.UnloadRows();

        string? checkError = null;
        try
        {
            using var resDoc = JsonDocument.Parse(result.DataJson);
            if (resDoc.RootElement.TryGetProperty("error", out var errEl)) checkError = errEl.GetString();
        }
        catch (JsonException) { }
        if (checkError != null)
            log.Warning($"FemCheck «{check.DisplayTag}»: {checkError}");
        else
            log.Info($"FemCheck «{check.DisplayTag}»: {result.Status}");
        return result;
    }

    /// <summary>
    /// Входы проверки цели: данные схемы, цель, наборы, сечения и армирование КЭ, расчётные длины «Авто (МКЭ)»
    /// (расчёт устойчивости — в фоне, с отменой). Null — цель не найдена, отмена или ошибка (причина в журнале).
    /// Вызывать из UI-потока: читает коллекции проекта.
    /// </summary>
    public static async Task<FemCheckRunSetup?> PrepareAsync(IProjectContext app, FemCheck check)
    {
        var db = app.Db;
        var log = app.LogService;
        var data = FemCheckSchemaData.Load(db, check.SchemaId);
        IFemCheckable? target = check.TargetsElement
            ? data.Members.FirstOrDefault(e => e.Id == check.ElementId)
            : app.FemSchemas.SelectMany(s => s.MemberGroups).FirstOrDefault(m => m.Id == check.MemberId);
        if (target == null) { log.Warning($"FemCheck #{check.Id}: конструктивный элемент не найден"); return null; }
        foreach (string error in data.Errors) log.Warning(error);

        bool isPlate = FemCheckContext.IsPlate(check);
        var scope = data.Scope(target);

        // Наборы цели: привязанные к ней и наборы схемы со строками по её КЭ; явный выбор — по id.
        var candidates = FemCheckContext.TargetForceSets(app.ForceSets, target, check.SchemaId, scope, isPlate)
            .Select(t => t.Set).ToList();
        List<ForceSet> selected = candidates;
        if (!check.IsAllSets)
        {
            var ids = check.GetForceSetIds().ToHashSet();
            selected = app.ForceSets.Where(f => ids.Contains(f.Id)).ToList();
        }
        var lookup = candidates.Union(selected).ToList();
        bool perElement = FemCheckRunner.HasElementNumbers(check, selected) && FemCheckRunner.ScopeElements(check, scope).Count > 0;

        // Режим «Авто (МКЭ)» стальной проверки: расчётные длины по расчёту устойчивости схемы — до сборки входов.
        IReadOnlyDictionary<string, FemSteelAutoLef>? steelAutoLef = null;
        if (perElement && check.NormCode == "steel_check" && SteelFemCheckParams.TryParse(check.ParamsJson)?.AutoLef is { } auto)
        {
            steelAutoLef = await SteelAutoLefAsync(app, check, auto);
            if (steelAutoLef == null) return null;
        }
        var inputs = FemCheckContext.BuildInputs(app, check, target, data, scope, lookup, steelAutoLef);
        if (isPlate)
        {
            var schemaGroups = app.FemSchemas.FirstOrDefault(s => s.Id == check.SchemaId)?.MemberGroups;
            FemCheckContext.TargetPlateSectionId(target, scope, schemaGroups ?? [], out string? inheritedFrom);
            if (inheritedFrom != null && inputs.PlateTemplate != null)
                log.Info(string.Format(Loc.S("FemCheckPlateSectionInherited"), check.DisplayTag,
                    inputs.PlateTemplate.Tag, inheritedFrom));
        }
        return new FemCheckRunSetup(target, scope, candidates, selected, lookup, inputs, isPlate, perElement);
    }

    /// <summary>
    /// Расчёт устойчивости схемы для режима «Авто (МКЭ)»: вход — в UI-потоке, расчёт — в фоне с отменой; отчёт — в
    /// журнал. Null — постановки нет, ошибки входа, отмена или сбой (причина в журнале).
    /// </summary>
    static async Task<IReadOnlyDictionary<string, FemSteelAutoLef>?> SteelAutoLefAsync(
        IProjectContext app, FemCheck check, SteelAutoLef auto)
    {
        var log = app.LogService;
        var analysis = app.Db.GetFemAnalyses(check.SchemaId).FirstOrDefault(a => a.Id == auto.AnalysisId);
        if (analysis == null)
        {
            log.Warning(string.Format(Loc.S("FemSteelAutoLefNoAnalysis"), check.DisplayTag));
            return null;
        }
        var input = FemSteelBuckling.Prepare(app.Db, check.SchemaId, analysis);
        foreach (string line in input.Report) log.Info($"{Loc.S("FemSteelAutoLefLog")}: {line}");
        if (input.HasErrors)
        {
            log.Warning(string.Format(Loc.S("FemSteelAutoLefInputErrors"), check.DisplayTag));
            return null;
        }

        // Постановка «Расчётные длины — CSfea»: её результат, если вход не изменился (отпечаток), иначе пересчёт с записью.
        bool stored = analysis.Kind == FemBucklingRunner.AnalysisKind;
        if (stored)
        {
            if (FemBucklingRunner.Current(app.Db, analysis, input, out var saved) is { } summary)
            {
                log.Info(string.Format(Loc.S("FemSteelAutoLefFromAnalysis"), check.DisplayTag, analysis.Tag, saved!.Created));
                return FemSteelBuckling.ElementLengths(summary.Units);
            }
            log.Info(string.Format(Loc.S(saved == null ? "FemSteelAutoLefNoResult" : "FemSteelAutoLefStale"), check.DisplayTag, analysis.Tag));
        }

        var busy = app.Busy;
        var cts = busy.BeginBusyWithCancellation(string.Format(Loc.S("FemSteelAutoLefRunning"), analysis.Tag), indeterminate: true);
        try
        {
            var token = cts.Token;
            if (stored)
            {
                analysis.Status = "running";
                app.Db.SaveFemAnalysis(analysis);
                string created = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", System.Globalization.CultureInfo.InvariantCulture);
                var saved = await FemBucklingRunner.SolveAsync(app.Db, analysis, input, created, token);
                analysis.ResultId = saved.Id;
                analysis.Status = saved.Status;
                app.Db.SaveFemAnalysis(analysis);
                busy.EndBusy();
                var summary = FemBucklingSummary.Parse(saved.DataJson) ?? new FemBucklingSummary();
                foreach (string line in summary.Describe()) log.Info($"{Loc.S("FemSteelAutoLefLog")}: {line}");
                return FemSteelBuckling.ElementLengths(summary.Units);
            }
            var (result, lengths) = await Task.Run(() =>
            {
                var r = FemSteelBuckling.Run(input, ct: token);
                return (r, FemSteelBuckling.ElementLengths(input, r));
            });
            busy.EndBusy();
            foreach (string line in result.Report) log.Info($"{Loc.S("FemSteelAutoLefLog")}: {line}");
            return lengths;
        }
        catch (OperationCanceledException)
        {
            if (stored) SetStatus(app, analysis, "cancelled");
            busy.EndBusy(Loc.S("CalcTaskCancelled"));
            return null;
        }
        catch (Exception ex)
        {
            if (stored) SetStatus(app, analysis, "error");
            busy.EndBusy();
            log.Error($"{Loc.S("FemSteelAutoLefLog")} «{check.DisplayTag}»: {ex.Message}");
            return null;
        }
    }

    static void SetStatus(IProjectContext app, FemAnalysis analysis, string status)
    {
        analysis.Status = status;
        app.Db.SaveFemAnalysis(analysis);
    }
}
