using System.Text.Json;
using System.Text.Json.Nodes;
using CScore;
using CScore.Fem;
using OpenCS.Services;
using OpenCS.Utilites;

namespace OpenCS.Tasks;

/// <summary>
/// Одиночная задача по строке проверки по КЭ: сечение, параметры и усилия — те, с которыми проверка считала
/// строку (<see cref="FemCheckRunner.CaptureRow"/>). Параметры КЭ (длина между раскреплениями по сетке, lef
/// «Авто (МКЭ)», ψ строки, φ1 «авто») переносятся числами. Сечение, собранное проверкой по данным
/// программы-источника (армирование ЛИРЫ/SCAD, сечение по жёсткости), сохраняется в проект копией с пометкой
/// источника; сечение проекта используется как есть. Строки наборов не загружаются: усилия — в параметрах.
/// <list type="bullet">
/// <item>steel_check → steel_check (усилия — ручные в параметрах СП 16);</item>
/// <item>rc_check (прочность по НДМ) → НДС strain_state с той же η: одиночной проверки прочности по НДМ нет;</item>
/// <item>пластины: упрощённые проверки — тот же вид, слоистая — shell_layered_uls / shell_layered_sls (п. 8.2.7).</item>
/// </list>
/// </summary>
public static class FemCheckRowTaskFactory
{
    const string SourceProp = "femCheckSource";

    /// <summary>Вид одиночной задачи по строке проверки; null — строку такой проверки задачей не воспроизвести.</summary>
    public static string? SingleKind(FemCheck check, FemCheckRowCall call)
    {
        switch (check.NormCode)
        {
            case "steel_check": return "steel_check";
            case "rc_check": return "strain_state";
        }
        var p = PlateCheckParams.Parse(check.ParamsJson);
        if (p.Kind.StartsWith("shell_simpl_", StringComparison.Ordinal)) return p.Kind;
        if (p.Kind == "shell_layered")
            return call.CalcType is CalcType.N or CalcType.NL ? "shell_layered_sls" : "shell_layered_uls";
        return null;
    }

    /// <summary>
    /// Собрать задачу (без Num и Id) по захваченной строке с сечением проекта <paramref name="sectionId"/>.
    /// Бросает InvalidOperationException, если строку задачей не воспроизвести.
    /// </summary>
    public static CalcTask Create(FemCheck check, FemCheckRowKey key, FemCheckRowCall call, int sectionId, string sectionTag)
    {
        if (call.Reason != null)
            throw new InvalidOperationException(call.Reason);
        string kind = SingleKind(check, call)
            ?? throw new InvalidOperationException(Loc.S("FemCheckRowTaskUnsupported"));

        string paramsJson = check.NormCode switch
        {
            "steel_check" => SteelParams(call),
            "rc_check" => StrainParams(call),
            _ => PlateParams(check, call, kind),
        };
        // У строк РСУ SCAD/ЛИРЫ метка — номер КЭ: тогда она не повторяется.
        string elem = key.ElemNum is int n ? $"КЭ {n}" : Loc.S("FemCheckRowTaskNoElement");
        string row = key.ElemNum?.ToString() == key.Label ? "" : $", {key.Label}";
        return new CalcTask
        {
            Kind = kind,
            SectionId = sectionId,
            CalcType = call.CalcType,
            Tag = $"{check.DisplayTag} — {elem}{row}",
            ParamsJson = WithSource(paramsJson, check.Id, key),
        };
    }

    /// <summary>
    /// Задача (без Num и Id) по строке <paramref name="key"/> проверки: входы — как у самой проверки
    /// (<see cref="FemCheckExecutor.PrepareAsync"/>), строка — захватом, сечение — в проекте (копия при
    /// необходимости, новая передаётся в <paramref name="addBar"/>/<paramref name="addPlate"/>). Null — входы не
    /// собраны (причина в журнале). Бросает InvalidOperationException, если строку задачей не воспроизвести.
    /// Вызывать из UI-потока.
    /// </summary>
    public static async Task<CalcTask?> BuildAsync(IProjectContext app, FemCheck check, FemCheckRowKey key,
        Action<CrossSection> addBar, Action<PlateSection> addPlate)
    {
        if (await FemCheckExecutor.PrepareAsync(app, check) is not { } setup) return null;
        var call = FemCheckRunner.CaptureRow(check, setup.Target, setup.Scope, setup.Selected, setup.Inputs, key)
            ?? throw new InvalidOperationException(Loc.S("FemCheckRowTaskRowNotFound"));
        if (call.Reason != null)
            throw new InvalidOperationException(call.Reason);
        _ = SingleKind(check, call) ?? throw new InvalidOperationException(Loc.S("FemCheckRowTaskUnsupported"));

        string marker = SectionMarker(check, key, call);
        if (setup.IsPlate)
        {
            // Подпись сечения источника уже называет армирование («SCAD, заданное «…»»).
            string tag = $"{call.SectionLabel} — КЭ {key.ElemNum?.ToString() ?? "—"}";
            var plate = EnsurePlateSection(app.Db, app.PlateSections, call.Plate!, tag, addPlate);
            return Create(check, key, call, plate.Id, plate.Tag);
        }
        var bar = EnsureBarSection(app.Db, app.CrossSections, call.BarSection!, marker, addBar);
        return Create(check, key, call, bar.Id, bar.Tag);
    }

    /// <summary>Ранее созданная задача по той же строке той же проверки или null.</summary>
    public static CalcTask? FindExisting(IEnumerable<CalcTask> tasks, FemCheck check, FemCheckRowKey key) =>
        tasks.FirstOrDefault(t => ReadSource(t.ParamsJson) is { } s && s.CheckId == check.Id && s.Key == key);

    /// <summary>Пометка копии сечения: проверка, КЭ, сечение КЭ и источник — по ней копия переиспользуется.</summary>
    public static string SectionMarker(FemCheck check, FemCheckRowKey key, FemCheckRowCall call) =>
        string.Format(Loc.S("FemCheckRowTaskSectionMarker"), check.DisplayTag,
            key.ElemNum?.ToString() ?? "—", call.SectionLabel, RebarSourceText(call.RebarSource));

    /// <summary>
    /// Сечение стержня строки в проекте: сечение проекта — оно само; собранное проверкой — копия с пометкой
    /// (прежняя с той же пометкой переиспользуется, иначе сохраняется новая и передаётся в <paramref name="add"/>).
    /// </summary>
    public static CrossSection EnsureBarSection(DatabaseService db, IReadOnlyList<CrossSection> project,
        CrossSection section, string marker, Action<CrossSection> add)
    {
        // Сечение проекта — только сам объект проекта: собранное проверкой — клон шаблона с армированием КЭ, и Id у
        // него шаблонный (по Id задача ссылалась бы на шаблон без армирования КЭ).
        if (project.FirstOrDefault(s => ReferenceEquals(s, section)) is { } own)
            return own;
        if (project.FirstOrDefault(s => s.Description == marker) is { } copy)
            return copy;

        // Области — новые в пуле проекта; хост хомутов внутри сечения переводится на копию хоста.
        var clones = section.Areas.ToDictionary(a => a, a => a.Clone());
        foreach (var (src, clone) in clones)
            if (src.HostArea is { } host && clones.TryGetValue(host, out var hostClone))
                clone.HostArea = hostClone;
        int areaNum = db.MaterialAreas.Count > 0 ? db.MaterialAreas.Max(a => a.Num) : 0;
        foreach (var src in section.Areas.OrderBy(a => a.HostArea != null && clones.ContainsKey(a.HostArea) ? 1 : 0))
        {
            var clone = clones[src];
            clone.Num = ++areaNum;
            db.SaveMaterialArea(clone);
            db.SaveMeshFibers(clone);
        }
        var result = new CrossSection
        {
            Num = project.Count > 0 ? project.Max(s => s.Num) + 1 : 1,
            Tag = section.Tag,
            Description = marker,
            Areas = [.. section.Areas.Select(a => clones[a])],
        };
        db.SaveCrossSection(result);
        add(result);
        return result;
    }

    /// <summary>Плитное сечение строки в проекте — как <see cref="EnsureBarSection"/>; копия узнаётся по имени.</summary>
    public static PlateSection EnsurePlateSection(DatabaseService db, IReadOnlyList<PlateSection> project,
        PlateSection section, string tag, Action<PlateSection> add)
    {
        if (project.FirstOrDefault(s => ReferenceEquals(s, section)) is { } own)
            return own;
        if (project.FirstOrDefault(s => s.Tag == tag) is { } copy)
            return copy;
        var result = section.CloneForCalc();
        result.Id = 0;
        result.Tag = tag;
        result.Num = project.Count > 0 ? project.Max(s => s.Num) + 1 : 1;
        db.SavePlateSection(result);
        add(result);
        return result;
    }

    static string SteelParams(FemCheckRowCall call)
    {
        var item = call.Bar!;
        var p = CScore.Sp16.SteelDesignParams.Parse(call.BarTask!.ParamsJson) with
        {
            // Как SteelTaskHandlerBase берёт строку набора: Qy ← Vx, Qz ← Vy, Mz ← T.
            ManualForces = new CScore.Sp16.SteelManualForces
            {
                N = item.N, Mx = item.Mx, My = item.My, Mz = item.T, Qy = item.Vx, Qz = item.Vy,
            },
        };
        return p.ToJson();
    }

    /// <summary>НДС по строке rc_check: усилия строки и η КЭ (l — длина КЭ между раскреплениями, ψ строки).</summary>
    static string StrainParams(FemCheckRowCall call)
    {
        var item = call.Bar!;
        var bp = BarCheckParams.Parse(call.BarTask!.ParamsJson);
        var p = new LimitForceParams { N = item.N, Mx = item.Mx, My = item.My };
        if (bp.EtaEnabled && bp.Eta is { } eta)
        {
            double l = bp.ResolveLengthM()
                ?? throw new InvalidOperationException(Loc.S("FemCheckRowTaskNoLength"));
            p.EtaEnabled = true;
            p.EtaIterative = eta.Iterative;
            p.EtaL = l;
            p.EtaMuX = eta.MuX;
            p.EtaMuY = eta.MuY;
            p.EtaPsiX = bp.RowPsiX ?? eta.PsiX;
            p.EtaPsiY = bp.RowPsiY ?? eta.PsiY;
            p.EtaSlendernessThreshold = eta.SlendernessThreshold;
        }
        return p.ToJson();
    }

    static string PlateParams(FemCheck check, FemCheckRowCall call, string kind)
    {
        var s = call.Shell!;
        if (kind == "shell_layered_uls")
            return JsonSerializer.Serialize(new ShellStrainParams
            {
                Nx = s.Nx, Ny = s.Ny, Nxy = s.Nxy, Mx = s.Mx, My = s.My, Mxy = s.Mxy, AutoStressToForce = false,
            });

        var c = PlateCheckParams.Parse(check.ParamsJson);
        string sigmaSCrc = c.SigmaSCrc == SigmaSCrcMethod.CrackingMoment8138 ? "moment" : "stress";
        string wplGamma = c.WplGamma switch
        {
            WplGammaMethod.Snip2030184 => "snip",
            WplGammaMethod.Radaykin2018 => "radaykin",
            _ => "sp63",
        };
        // Слоистая ПС2 — по п. 8.2.7, как проверка: длительная часть — парная строка NL или доля N.
        if (kind == "shell_layered_sls")
            return new ShellLayeredSlsParams
            {
                Nx = s.Nx, Ny = s.Ny, Nxy = s.Nxy, Mx = s.Mx, My = s.My, Mxy = s.Mxy, AutoStressToForce = false,
                AcrcLimMm = c.AcrcLimMm, Phi2 = c.Phi2, SigmaSCrcMethod = sigmaSCrc, WplGammaMethod = wplGamma,
                Combine827 = true,
                Long = call.Nl is { } nl ? ShellLayeredSlsForces.Of(nl) : null,
                LtFraction = call.Nl == null ? c.LtFraction : 0,
            }.ToJson();

        return JsonSerializer.Serialize(new ShellSimplParams
        {
            Nx = s.Nx, Ny = s.Ny, Nxy = s.Nxy, Mx = s.Mx, My = s.My, Mxy = s.Mxy, Qx = s.Qx, Qy = s.Qy,
            AutoStressToForce = false,
            StepDeg = c.StepDeg,
            AcrcLimMm = c.AcrcLimMm,
            Phi1 = call.Phi1,
            Phi2 = c.Phi2,
            SigmaSCrcMethod = sigmaSCrc,
            WplGammaMethod = wplGamma,
            StripModel = c.StripModel == StripModel.Ndm ? "ndm" : "formulas",
            CapriCheckMode = c.CapriCheckMode == CapriCheckMode.Constraints ? "constraints" : "direct",
        });
    }

    static string RebarSourceText(string source) => source switch
    {
        FemCheckRebarSource.Assigned => Loc.S("FemCheckSourceAssigned"),
        FemCheckRebarSource.Selected => Loc.S("FemCheckSourceSelected"),
        FemCheckRebarSource.Layout => Loc.S("FemCheckSourceLayout"),
        _ => Loc.S("FemCheckSourceSection"),
    };

    static string WithSource(string json, int checkId, FemCheckRowKey key)
    {
        var obj = (string.IsNullOrWhiteSpace(json) ? null : JsonNode.Parse(json)) as JsonObject ?? new JsonObject();
        obj[SourceProp] = new JsonObject
        {
            ["checkId"] = checkId,
            ["set"] = key.ForceSetTag,
            ["label"] = key.Label,
            ["elem"] = key.ElemNum,
            ["section"] = key.SectionNum,
            ["source"] = key.RebarSource,
        };
        return obj.ToJsonString();
    }

    static (int CheckId, FemCheckRowKey Key)? ReadSource(string? json)
    {
        if (string.IsNullOrWhiteSpace(json) || !json.Contains(SourceProp, StringComparison.Ordinal)) return null;
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind != JsonValueKind.Object
                || !doc.RootElement.TryGetProperty(SourceProp, out var s)) return null;
            int? Int(string name) => s.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetInt32() : null;
            string Str(string name) => s.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : "";
            return (Int("checkId") ?? 0, new FemCheckRowKey(Str("set"), Str("label"), Int("elem"), Int("section"), Str("source")));
        }
        catch (JsonException) { return null; }
    }
}
