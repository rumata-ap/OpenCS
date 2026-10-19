using System.Text.Json;
using System.Text.Json.Nodes;
using CScore;

namespace OpenCS.Tasks;

/// <summary>
/// Одиночная задача по выбранной строке результата пакетного расчёта: тот же вид без «_batch»,
/// параметры пакета целиком и усилия той строки набора, которую считал пакет (не текст ячейки окна).
/// Стержни ссылаются на строку набора (ForceSetId + ForceItemId), точные усилия строки дублируются
/// в параметрах — их показывает диалог задачи. Пластины получают усилия только в параметрах,
/// уже приведёнными к кН/м так же, как в пакете («напряжения → усилия»).
/// Источник (пакет + строка) записывается в параметры: повторный выбор строки находит ту же задачу.
/// </summary>
public static class BatchRowTaskFactory
{
    const string SourceProp = "batchSource";

    /// <summary>Пакеты, для строк которых строится одиночная задача.</summary>
    public static bool Supports(string batchKind) => batchKind is
        "limit_force_batch" or "limit_moment_batch" or "limit_axial_batch"
        or "strain_state_batch" or "strength_ndm_batch" or "two_stage_strain_batch" or "total_curvature_batch"
        or "shell_simpl_wa_sls_batch" or "shell_simpl_wa_uls_batch"
        or "shell_simpl_capri_sls_batch" or "shell_simpl_capri_uls_batch"
        or "shell_simpl_capri_design_uls_batch" or "shell_simpl_capri_design_sls_batch"
        or "shell_strain_state_batch" or "shell_layered_uls_batch";

    /// <summary>
    /// Вид одиночной задачи для пакета. Одиночной проверки прочности по НДМ нет — строку пакета разворачивает
    /// НДС (та же плоскость деформаций; ε бетона и арматуры — в его выводе).
    /// </summary>
    public static string SingleKind(string batchKind) => batchKind switch
    {
        "strength_ndm_batch" => "strain_state",
        _ when batchKind.EndsWith("_batch", StringComparison.Ordinal) => batchKind[..^"_batch".Length],
        _ => batchKind,
    };

    /// <summary>
    /// Собрать одиночную задачу (без Num и Id) по строке пакета с номером строки набора rowNum.
    /// Бросает InvalidOperationException, если набор, строка или плитное сечение не найдены.
    /// </summary>
    public static CalcTask Create(CalcTask batch, int rowNum, string sectionTag,
        IReadOnlyList<ForceSet> forceSets, IReadOnlyList<PlateSection> plateSections)
    {
        if (!Supports(batch.Kind))
            throw new InvalidOperationException($"Одиночная задача по строке пакета «{batch.Kind}» не поддерживается.");

        var task = new CalcTask
        {
            Kind = SingleKind(batch.Kind),
            SectionId = batch.SectionId,
            CalcType = batch.CalcType,
            ForceSetId = batch.ForceSetId,
        };

        string label;
        string paramsJson;
        if (batch.Kind == "two_stage_strain_batch")
            (paramsJson, label, task.ForceSetId) = TwoStage(batch, rowNum, forceSets);
        else if (batch.Kind.StartsWith("shell_", StringComparison.Ordinal))
        {
            var item = ShellItem(batch, rowNum, forceSets);
            double h = plateSections.FirstOrDefault(s => s.Id == batch.SectionId)?.H
                ?? throw new InvalidOperationException($"Плитное сечение id={batch.SectionId} не найдено.");
            paramsJson = PlateParams(batch, item, h);
            label = item.Label;
        }
        else
        {
            var item = BarItem(batch, rowNum, forceSets);
            task.ForceItemId = item.Id;
            paramsJson = BarParams(batch, item);
            label = item.Label;
        }

        task.Tag = $"{sectionTag} — {label}";
        task.ParamsJson = WithSource(paramsJson, batch.Id, rowNum);
        return task;
    }

    /// <summary>Ранее созданная одиночная задача по той же строке того же пакета или null.</summary>
    public static CalcTask? FindExisting(IEnumerable<CalcTask> tasks, CalcTask batch, int rowNum)
    {
        string kind = SingleKind(batch.Kind);
        return tasks.FirstOrDefault(t => t.Kind == kind && ReadSource(t.ParamsJson) == (batch.Id, rowNum));
    }

    /// <summary>
    /// Параметры одиночной задачи упрощённой проверки пластин по строке пакета: все параметры пакета
    /// (шаг фасеток, предел и коэффициенты трещин, модель полосы, способ проверки КМ) и усилия строки —
    /// те же, что брал пакет (Nx/Ny/Nxy через ResolveN при «напряжения → усилия»), уже в кН/м.
    /// </summary>
    public static ShellSimplParams ShellSimplSingleParams(string? batchParamsJson, ShellLoadItem item, double h)
    {
        var p = ShellSimplParams.Parse(batchParamsJson);
        (p.Nx, p.Ny, p.Nxy) = p.AutoStressToForce ? item.ResolveN(h) : (item.Nx, item.Ny, item.Nxy);
        p.Mx = item.Mx; p.My = item.My; p.Mxy = item.Mxy;
        p.Qx = item.Qx; p.Qy = item.Qy;
        p.AutoStressToForce = false;
        return p;
    }

    static string BarParams(CalcTask batch, LoadItem item)
    {
        if (batch.Kind == "total_curvature_batch")
        {
            // Режим длительной части (вся / доля / вручную) — как в пакете.
            var tc = TotalCurvatureTaskParams.Parse(batch.ParamsJson);
            tc.N = item.N; tc.Mx = item.Mx; tc.My = item.My;
            return tc.ToJson();
        }

        // limit_* и strain_state: решатель и учёт прогиба η пакета сохраняются.
        // У пакета прочности по НДМ своих параметров нет — только усилия строки.
        var p = batch.Kind == "strength_ndm_batch" ? new LimitForceParams() : LimitForceParams.Parse(batch.ParamsJson);
        p.N = item.N; p.Mx = item.Mx; p.My = item.My;
        return p.ToJson();
    }

    static string PlateParams(CalcTask batch, ShellLoadItem item, double h)
    {
        if (batch.Kind is "shell_strain_state_batch" or "shell_layered_uls_batch")
        {
            var s = ShellStrainParams.Parse(batch.ParamsJson);
            (s.Nx, s.Ny, s.Nxy) = s.AutoStressToForce ? item.ResolveN(h) : (item.Nx, item.Ny, item.Nxy);
            s.Mx = item.Mx; s.My = item.My; s.Mxy = item.Mxy;
            s.AutoStressToForce = false;
            return JsonSerializer.Serialize(s);
        }

        var p = ShellSimplSingleParams(batch.ParamsJson, item, h);
        // Подбор сеток Капри–Мори считается только по НДМ полосы (в диалоге модель скрыта).
        if (batch.Kind.StartsWith("shell_simpl_capri_design_", StringComparison.Ordinal))
            p.StripModel = "ndm";
        return JsonSerializer.Serialize(p);
    }

    static (string Json, string Label, int ForceSetId) TwoStage(CalcTask batch, int rowNum, IReadOnlyList<ForceSet> forceSets)
    {
        var p = TwoStageParams.Parse(batch.ParamsJson);
        var s1 = TwoStageForceResolver.Resolve(p.Stage1, forceSets);
        var s2 = TwoStageForceResolver.Resolve(p.Stage2, forceSets);
        int i = s2.FindIndex(fi => fi.Num == rowNum);
        if (i < 0 && s2.Count == 1) i = 0;
        if (i < 0)
            throw new InvalidOperationException($"Строка усилий № {rowNum} этапа 2 не найдена.");
        // Как в пакете: одна строка этапа 1 общая для всех строк этапа 2, иначе — построчно.
        var f1 = s1.Count == 1 ? s1[0] : s1[i];
        p.Stage1 = ItemStage(p.Stage1, f1);
        p.Stage2 = ItemStage(p.Stage2, s2[i]);
        return (p.ToJson(), s2[i].Label, p.Stage2.ForceSetId);
    }

    static StageForce ItemStage(StageForce stage, LoadItem item) => stage.Mode == "manual"
        ? stage
        : new StageForce { Mode = "item", ForceSetId = stage.ForceSetId, ForceItemId = item.Id };

    static LoadItem BarItem(CalcTask batch, int rowNum, IReadOnlyList<ForceSet> forceSets) =>
        Set(batch, forceSets).Items.FirstOrDefault(i => i.Num == rowNum)
        ?? throw new InvalidOperationException($"Строка усилий № {rowNum} не найдена.");

    static ShellLoadItem ShellItem(CalcTask batch, int rowNum, IReadOnlyList<ForceSet> forceSets) =>
        Set(batch, forceSets).ShellItems.FirstOrDefault(i => i.Num == rowNum)
        ?? throw new InvalidOperationException($"Строка усилий № {rowNum} не найдена.");

    static ForceSet Set(CalcTask batch, IReadOnlyList<ForceSet> forceSets) =>
        forceSets.FirstOrDefault(f => f.Id == batch.ForceSetId)
        ?? throw new InvalidOperationException($"Набор усилий id={batch.ForceSetId} не найден.");

    static string WithSource(string json, int batchId, int rowNum)
    {
        var obj = JsonNode.Parse(json) as JsonObject ?? new JsonObject();
        obj[SourceProp] = new JsonObject { ["taskId"] = batchId, ["num"] = rowNum };
        return obj.ToJsonString();
    }

    static (int TaskId, int Num)? ReadSource(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind == JsonValueKind.Object
                && doc.RootElement.TryGetProperty(SourceProp, out var src)
                && src.TryGetProperty("taskId", out var id) && src.TryGetProperty("num", out var num))
                return (id.GetInt32(), num.GetInt32());
        }
        catch (JsonException) { }
        return null;
    }
}
