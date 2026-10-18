using System.Text.Json;
using CScore;
using CScore.Fem;
using OpenCS.Services;
using OpenCS.Utilites;

namespace OpenCS.Tasks;

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

        var busy = app.Busy;
        var cts = busy.BeginBusyWithCancellation(string.Format(Loc.S("FemSteelAutoLefRunning"), analysis.Tag), indeterminate: true);
        try
        {
            var token = cts.Token;
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
            busy.EndBusy(Loc.S("CalcTaskCancelled"));
            return null;
        }
        catch (Exception ex)
        {
            busy.EndBusy();
            log.Error($"{Loc.S("FemSteelAutoLefLog")} «{check.DisplayTag}»: {ex.Message}");
            return null;
        }
    }
}
