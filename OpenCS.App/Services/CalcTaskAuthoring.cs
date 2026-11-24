using System.Collections.Concurrent;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using CScore;
using CScore.History;
using CScore.PrestressLoss;
using CScore.Sp16;
using CScore.Sp63.CrackWidth;
using CScore.Sp63.Deflection;
using CScore.Sp63.Normal;
using OpenCS.Tasks;
using OpenCS.Utilites;

namespace OpenCS.Services;

/// <summary>
/// Проверка, номер, имя по умолчанию, запись, копия и удаление расчётных задач — одна для диалога задачи, дерева и
/// страницы задач, <c>opencs add tasks</c> и вида <c>tasks</c> документа <c>opencs-model</c>. Требования видов — в
/// <see cref="CalcTaskKinds"/>. Пути ошибок — как в документе (<c>tasks[]</c>): <c>tag</c>, <c>kind</c>,
/// <c>calcType</c>, <c>section</c>, <c>forceSet</c>, <c>row</c>, <c>params</c>, <c>params.EtaL</c>.
/// </summary>
public static class CalcTaskAuthoring
{
    /// <summary>Служебные поля параметров: источник одиночной задачи (строка пакета, точка поверхности, строка проверки МКЭ).</summary>
    public static readonly string[] SourceProps = ["batchSource", "surfaceSource", "femCheckSource"];

    // ── Проверка без проекта: имя, вид, тип расчёта, параметры, обязательность ссылок ────────────

    /// <summary>
    /// Проверка задачи без обращения к проекту: имя, вид, тип расчёта, параметры (строгий разбор и диапазоны),
    /// наличие и отсутствие ссылок по требованиям вида (по ненулевым Id).
    /// </summary>
    public static List<FieldError> ValidateFields(CalcTask t)
    {
        var errors = new List<FieldError>();
        if (string.IsNullOrWhiteSpace(t.Tag)) errors.Add(new("tag", Loc.S("CalcTaskErrTagEmpty")));
        var spec = CalcTaskKinds.Get(t.Kind);
        if (spec == null)
        {
            errors.Add(new("kind", string.Format(Loc.S("CalcTaskErrKind"), t.Kind)));
            return errors;
        }
        if (!spec.CalcTypes.Contains(t.CalcType))
            errors.Add(new("calcType", string.Format(Loc.S("CalcTaskErrCalcType"), t.CalcType,
                string.Join(", ", spec.CalcTypes))));

        var paramErrors = ValidateParams(t.Kind, t.ParamsJson);
        errors.AddRange(paramErrors);

        if (spec.Section == CalcTaskSectionKind.None && t.SectionId != 0)
            errors.Add(new("section", Loc.S("CalcTaskErrSectionNotUsed")));
        else if (spec.Section != CalcTaskSectionKind.None && t.SectionId == 0)
            errors.Add(new("section", Loc.S("CalcTaskErrSectionRequired")));

        if (spec.Set == CalcTaskRefNeed.None && t.ForceSetId != 0)
            errors.Add(new("forceSet", Loc.S("CalcTaskErrSetNotUsed")));
        else if (spec.Set == CalcTaskRefNeed.Required && t.ForceSetId == 0)
            errors.Add(new("forceSet", Loc.S("CalcTaskErrSetRequired")));

        if (t.ForceItemId != 0 && (spec.Row == CalcTaskRefNeed.None || t.ForceSetId == 0))
            errors.Add(new("row", Loc.S("CalcTaskErrRowNotUsed")));
        else if (t.ForceItemId == 0 && paramErrors.Count == 0 && RowRequired(t))
            errors.Add(new("row", Loc.S("CalcTaskErrRowRequired")));
        return errors;
    }

    /// <summary>
    /// Нужна ли задаче строка набора: по таблице вида или по параметрам (усилия не введены вручную; у стали — кроме
    /// конструктивной гибкости; у наклонных сечений — кроме профиля МКЭ).
    /// </summary>
    public static bool RowRequired(CalcTask t)
    {
        var spec = CalcTaskKinds.Get(t.Kind);
        if (spec == null || spec.Row == CalcTaskRefNeed.None) return false;
        if (spec.Row == CalcTaskRefNeed.Required) return true;
        try
        {
            return t.Kind switch
            {
                "sp63_normal" => !Sp63NormalTaskParams.Parse(t.ParamsJson).UseManualForces,
                "sp63_crack_width" => !Sp63CrackWidthTaskParams.Parse(t.ParamsJson).UseManualForces,
                "sp63_deflection" => !Sp63DeflectionTaskParams.Parse(t.ParamsJson).UseManualForces,
                "steel_constructive" => false,
                _ when t.Kind.StartsWith("steel_", StringComparison.Ordinal) =>
                    SteelDesignParams.Parse(t.ParamsJson).ManualForces == null,
                "shear_inclined" => ShearInclinedParams.Parse(t.ParamsJson) is var s
                                    && s.ManualForces == null && s.ForceSource != "fem_profile",
                // Кручение: набор выбран — нужна и строка, если момент не задан вручную (как в диалоге).
                "torsion_bem" or "torsion_fem" => t.ForceSetId != 0 && !(TorsionParams.Parse(t.ParamsJson).MkKNm > 0),
                _ => false,
            };
        }
        catch (Exception e) when (e is JsonException or ArgumentException or InvalidOperationException)
        {
            return false;   // ошибку разбора сообщает ValidateParams
        }
    }

    /// <summary>
    /// Параметры вида: объект JSON, известные поля (по классам параметров вида), строгие типы значений, диапазоны.
    /// Пусто или «{}» допустимо: у всех видов есть умолчания (продавливание и потери задаются в своём окне).
    /// </summary>
    public static List<FieldError> ValidateParams(string kind, string? json)
    {
        var errors = ValidateParamsShape(kind, json, out var obj);
        if (errors.Count > 0 || obj == null || obj.Count == 0) return errors;
        try
        {
            CheckRanges(kind, obj.ToJsonString(), errors);
        }
        catch (Exception e) when (e is JsonException or ArgumentException or InvalidOperationException)
        {
            errors.Add(new("params", string.Format(Loc.S("CalcTaskErrParamsJson"), e.Message)));
        }
        return errors;
    }

    /// <summary>Форма параметров без диапазонов: объект JSON, известные поля, строгие типы значений.</summary>
    public static List<FieldError> ValidateParamsShape(string kind, string? json) => ValidateParamsShape(kind, json, out _);

    static List<FieldError> ValidateParamsShape(string kind, string? json, out JsonObject? parsed)
    {
        parsed = null;
        var errors = new List<FieldError>();
        var spec = CalcTaskKinds.Get(kind);
        if (spec == null) return errors;
        if (string.IsNullOrWhiteSpace(json)) return errors;
        JsonObject obj;
        try
        {
            if (JsonNode.Parse(json) is not JsonObject o)
            {
                errors.Add(new("params", Loc.S("CalcTaskErrParamsNotObject")));
                return errors;
            }
            obj = o;
        }
        catch (JsonException e)
        {
            errors.Add(new("params", string.Format(Loc.S("CalcTaskErrParamsJson"), e.Message)));
            return errors;
        }
        if (obj.Count == 0) { parsed = obj; return errors; }

        foreach (var p in SourceProps) obj.Remove(p);
        var types = spec.ParamTypes;
        foreach (var (key, _) in obj)
            if (!types.Any(pt => Names(pt).Contains(key)))
                errors.Add(new($"params.{key}", string.Format(Loc.S("CalcTaskErrParamsUnknown"), key)));
        if (errors.Count > 0) return errors;

        foreach (var pt in types)
        {
            var sub = new JsonObject();
            foreach (var (key, value) in obj)
                if (Names(pt).Contains(key)) sub[key] = value?.DeepClone();
            try
            {
                JsonSerializer.Deserialize(sub.ToJsonString(), pt.Type, StrictOptions(pt));
            }
            catch (JsonException e)
            {
                string at = string.IsNullOrEmpty(e.Path) || e.Path == "$" ? "params" : "params" + e.Path.TrimStart('$');
                errors.Add(new(at, string.Format(Loc.S("CalcTaskErrParamsValue"), at["params".Length..].TrimStart('.'))));
            }
        }
        if (errors.Count == 0) parsed = obj;
        return errors;
    }

    static void Positive(List<FieldError> errors, string name, double? v, bool optional = false)
    {
        if (v is null) { if (!optional) errors.Add(new($"params.{name}", string.Format(Loc.S("CalcTaskErrPositive"), name))); return; }
        if (!(v > 0) || !double.IsFinite(v.Value)) errors.Add(new($"params.{name}", string.Format(Loc.S("CalcTaskErrPositive"), name)));
    }

    static void NonNegative(List<FieldError> errors, string name, double v)
    {
        if (!(v >= 0) || !double.IsFinite(v)) errors.Add(new($"params.{name}", string.Format(Loc.S("CalcTaskErrNonNegative"), name)));
    }

    static void Share(List<FieldError> errors, string name, double v, bool zeroAllowed = true)
    {
        if (!(zeroAllowed ? v >= 0 : v > 0) || !(v <= 1))
            errors.Add(new($"params.{name}", string.Format(Loc.S(zeroAllowed ? "CalcTaskErrShare" : "CalcTaskErrShareOpen"), name)));
    }

    static void OneOf(List<FieldError> errors, string name, string? v, params string[] allowed)
    {
        if (v == null || !allowed.Contains(v))
            errors.Add(new($"params.{name}", string.Format(Loc.S("CalcTaskErrOneOf"), name, string.Join(", ", allowed))));
    }

    /// <summary>Диапазоны параметров по видам (перенесены из веток диалога задачи и окон параметров).</summary>
    static void CheckRanges(string kind, string json, List<FieldError> errors)
    {
        var spec = CalcTaskKinds.Get(kind)!;
        foreach (var pt in spec.ParamTypes)
        {
            if (pt.Type == typeof(LimitForceParams))
            {
                var p = LimitForceParams.Parse(json);
                if (p.EtaEnabled)
                {
                    Positive(errors, "etaL", p.EtaL);
                    Positive(errors, "etaMuX", p.EtaMuX, optional: true);
                    Positive(errors, "etaMuY", p.EtaMuY, optional: true);
                    Positive(errors, "etaSlendernessThreshold", p.EtaSlendernessThreshold, optional: true);
                }
            }
            else if (pt.Type == typeof(CrackWidthTaskParams))
            {
                var p = CrackWidthTaskParams.Parse(json);
                Positive(errors, "AcrcUltLong", p.AcrcUltLong);
                Positive(errors, "AcrcUltShort", p.AcrcUltShort);
                Positive(errors, "Phi2", p.Phi2);
                if (kind == "acrc_surface") Share(errors, "LongShare", p.LongShare, zeroAllowed: false);
                else
                {
                    OneOf(errors, "ForcesMode", p.ForcesMode, "share", "two_sets", "force_item_long", "manual");
                    Share(errors, "LongShare", p.LongShare);
                }
            }
            else if (pt.Type == typeof(TotalCurvatureTaskParams))
            {
                var p = TotalCurvatureTaskParams.Parse(json);
                OneOf(errors, "ForcesMode", p.ForcesMode, "total_only", "share", "manual");
                Share(errors, "LongShare", p.LongShare);
            }
            else if (pt.Type == typeof(MomentCurvatureBiaxialTaskParams))
            {
                var p = MomentCurvatureBiaxialTaskParams.Parse(json);
                if (p.AuxPointsPerSegment is < 0) NonNegative(errors, "AuxPointsPerSegment", p.AuxPointsPerSegment.Value);
            }
            else if (pt.Type == typeof(ShellSimplParams))
            {
                var p = ShellSimplParams.Parse(json);
                Positive(errors, "step_deg", p.StepDeg);
                if (p.StepDeg > 90) errors.Add(new("params.step_deg", string.Format(Loc.S("CalcTaskErrMax"), "step_deg", 90)));
                Positive(errors, "acrc_lim_mm", p.AcrcLimMm);
                Positive(errors, "phi1", p.Phi1);
                Positive(errors, "phi2", p.Phi2);
            }
            else if (pt.Type == typeof(ShellLayeredSlsParams))
            {
                var p = ShellLayeredSlsParams.Parse(json);
                Positive(errors, "acrc_lim_mm", p.AcrcLimMm);
                Positive(errors, "phi1", p.Phi1);
                Positive(errors, "phi2", p.Phi2);
                Share(errors, "lt_fraction", p.LtFraction);
            }
            else if (pt.Type == typeof(ShellStrainParams))
            {
                var p = ShellStrainParams.Parse(json);
                Positive(errors, "tol_res", p.TolRes);
                NonNegative(errors, "max_iter", p.MaxIter);
            }
            else if (pt.Type == typeof(TorsionParams))
            {
                var p = TorsionParams.Parse(json);
                Positive(errors, "element_size", p.ElementSize);
                if (p.AutoConverge)
                {
                    Positive(errors, "auto_h0", p.AutoH0);
                    if (p.AutoRuns < 2) errors.Add(new("params.auto_runs", Loc.S("TorsionAutoRunsInvalid")));
                }
            }
            else if (pt.Type == typeof(SteelDesignParams))
                CheckSteel(SteelDesignParams.Parse(json), errors);
            else if (pt.Type == typeof(ShearInclinedParams))
            {
                var p = ShearInclinedParams.Parse(json);
                if (kind == "shear_inclined" && p.ForceSource != "fem_profile" && p.ManualForces is { HasShearForce: false })
                    errors.Add(new("params.ManualForces", Loc.S("ShearInclinedNeedShearForce")));
            }
            else if (pt.Type == typeof(PunchingTaskParams))
            {
                foreach (var code in PunchingTaskParams.Parse(json).Validate())
                    errors.Add(new("params", Loc.S("PunchingErr_" + code)));
            }
            else if (pt.Type == typeof(PrestressLossParams))
            {
                var p = JsonSerializer.Deserialize<PrestressLossParams>(json) ?? new();
                if (!p.ConcreteClassAuto && !(p.ConcreteClassOverride >= 10 && p.ConcreteClassOverride <= 100))
                    errors.Add(new("params.ConcreteClassOverride", Loc.S("PrestressErrConcreteClass")));
            }
            else if (pt.Type == typeof(Sp63NormalTaskParams))
            {
                var p = Sp63NormalTaskParams.Parse(json);
                if (!p.TryToOptions(out _, out string code))
                    errors.Add(new("params", string.Format(Loc.S("CalcTaskErrParamsCode"), code)));
                Positive(errors, "elementLengthOrRestraintDistance", p.ElementLengthOrRestraintDistance, optional: true);
                Positive(errors, "effectiveLengthL0", p.EffectiveLengthL0, optional: true);
                Positive(errors, "spanLength", p.SpanLength, optional: true);
            }
            else if (pt.Type == typeof(Sp63CrackWidthTaskParams))
            {
                var p = Sp63CrackWidthTaskParams.Parse(json);
                if (!p.TryToOptions(out _, out string code))
                    errors.Add(new("params", string.Format(Loc.S("CalcTaskErrParamsCode"), code)));
                Positive(errors, "phi1", p.Phi1);
                Positive(errors, "phi2", p.Phi2);
                Positive(errors, "acrcLimMm", p.AcrcLimMm);
                Positive(errors, "acrcLimShortMm", p.AcrcLimShortMm);
                Share(errors, "longTermShare", p.LongTermShare);
            }
            else if (pt.Type == typeof(Sp63DeflectionTaskParams))
            {
                var p = Sp63DeflectionTaskParams.Parse(json);
                if (!p.TryToOptions(out _, out string code))
                    errors.Add(new("params", string.Format(Loc.S("CalcTaskErrParamsCode"), code)));
                Positive(errors, "spanM", p.SpanM);
                Positive(errors, "deflectionLimitMm", p.DeflectionLimitMm);
                Share(errors, "longTermShare", p.LongTermShare);
            }
            else if (pt.Type == typeof(OpenSeesSectionParams)) Throws(errors, () => OpenSeesSectionParams.Parse(json));
            else if (pt.Type == typeof(OpenSeesSectionInteractionParams)) Throws(errors, () => OpenSeesSectionInteractionParams.Parse(json));
            else if (pt.Type == typeof(OpenSeesSpatialInteractionParams)) Throws(errors, () => OpenSeesSpatialInteractionParams.Parse(json));
        }
    }

    static void Throws(List<FieldError> errors, Action parse)
    {
        try { parse(); }
        catch (ArgumentException e)
        {
            errors.Add(new("params", $"{Loc.S("OpenSeesSpatialInvalidParams")} ({e.Message.Split(" (Parameter")[0]})"));
        }
    }

    /// <summary>Проверки стальных параметров — как у редактора параметров СП 16.</summary>
    static void CheckSteel(SteelDesignParams p, List<FieldError> errors)
    {
        Positive(errors, "GammaC", p.GammaC);
        Positive(errors, "LefX", p.LefX);
        Positive(errors, "LefY", p.LefY);
        if (!(p.NetAreaRatio > 0 && p.NetAreaRatio <= 1)) Share(errors, "NetAreaRatio", p.NetAreaRatio, zeroAllowed: false);
        NonNegative(errors, "LefB", p.LefB);
        Positive(errors, "GammaFEq", p.GammaFEq);
        NonNegative(errors, "LocalForce", p.LocalForce);
        NonNegative(errors, "BearingLength", p.BearingLength);
        NonNegative(errors, "FlangeWeldLeg", p.FlangeWeldLeg);
        NonNegative(errors, "RibSpacing", p.RibSpacing);
        NonNegative(errors, "WebHoleSpacing", p.WebHoleSpacing);
        NonNegative(errors, "WebHoleDiameter", p.WebHoleDiameter);
        if ((p.WebHoleSpacing > 0 || p.WebHoleDiameter > 0)
            && !(p.WebHoleSpacing > 0 && p.WebHoleDiameter > 0 && p.WebHoleDiameter < p.WebHoleSpacing))
            errors.Add(new("params.WebHoleDiameter", Loc.S("Sp16WebHolesInvalid")));
        if (!(p.EndMomentRatio >= -1 && p.EndMomentRatio <= 1))
            errors.Add(new("params.EndMomentRatio", string.Format(Loc.S("CalcTaskErrRange"), "EndMomentRatio", -1, 1)));
        if (!(p.MiddleThirdMomentRatio > 0 && p.MiddleThirdMomentRatio <= 1))
            Share(errors, "MiddleThirdMomentRatio", p.MiddleThirdMomentRatio, zeroAllowed: false);
        Positive(errors, "EtaOverride", p.EtaOverride, optional: true);
    }

    // ── Имена полей и строгий разбор ──────────────────────────────────────────────────────────

    static readonly ConcurrentDictionary<CalcTaskParamsType, HashSet<string>> NameCache = new();
    static readonly ConcurrentDictionary<CalcTaskParamsType, JsonSerializerOptions> OptionsCache = new();

    /// <summary>Параметры класса: его собственные (статическое поле JsonSerializerOptions) или по умолчанию.</summary>
    static JsonSerializerOptions? OwnOptions(Type t) =>
        t.GetFields(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)
            .Where(f => f.FieldType == typeof(JsonSerializerOptions)).Select(f => (JsonSerializerOptions?)f.GetValue(null))
            .Concat(t.GetProperties(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)
                .Where(p => p.PropertyType == typeof(JsonSerializerOptions)).Select(p => (JsonSerializerOptions?)p.GetValue(null)))
            .FirstOrDefault(o => o != null);

    static JsonSerializerOptions StrictOptions(CalcTaskParamsType pt) => OptionsCache.GetOrAdd(pt, key =>
    {
        var own = OwnOptions(key.Type);
        var o = own != null ? new JsonSerializerOptions(own) : new JsonSerializerOptions();
        o.UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow;
        if (key.CaseInsensitive) o.PropertyNameCaseInsensitive = true;
        o.Converters.Add(new JsonStringEnumConverter());
        return o;
    });

    /// <summary>Имена полей верхнего уровня класса параметров (с учётом атрибутов, политики имён и регистра).</summary>
    static HashSet<string> Names(CalcTaskParamsType pt) => NameCache.GetOrAdd(pt, key =>
    {
        var opts = StrictOptions(key);
        var set = new HashSet<string>(opts.PropertyNameCaseInsensitive ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
        foreach (var p in key.Type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            if (p.SetMethod is not { IsPublic: true }) continue;
            if (p.GetCustomAttribute<JsonIgnoreAttribute>() is { Condition: JsonIgnoreCondition.Always }) continue;
            string name = p.GetCustomAttribute<JsonPropertyNameAttribute>()?.Name
                          ?? opts.PropertyNamingPolicy?.ConvertName(p.Name) ?? p.Name;
            set.Add(name);
        }
        return set;
    });

    // ── Проверка по проекту ───────────────────────────────────────────────────────────────────

    /// <summary>Полная проверка: <see cref="ValidateFields"/> и ссылки на объекты проекта (сечение, набор, строка, Id в параметрах).</summary>
    public static List<FieldError> Validate(DatabaseService db, CalcTask t)
    {
        var errors = ValidateFields(t);
        var spec = CalcTaskKinds.Get(t.Kind);
        if (spec == null) return errors;

        if (t.SectionId != 0)
        {
            bool ok = spec.Section switch
            {
                CalcTaskSectionKind.Plate => db.PlateSections.Any(s => s.Id == t.SectionId),
                CalcTaskSectionKind.Staged => db.CrossSections.Any(s => s.Id == t.SectionId && s is StagedSection),
                _ => db.CrossSections.Any(s => s.Id == t.SectionId),
            };
            if (!ok)
                errors.Add(new("section", Loc.S(spec.Section switch
                {
                    CalcTaskSectionKind.Plate => "CalcTaskErrSectionNotPlate",
                    CalcTaskSectionKind.Staged => "CalcTaskErrSectionNotStaged",
                    _ => "CalcTaskErrSectionNotFound",
                })));
        }

        if (t.ForceSetId != 0)
        {
            var set = db.ForceSets.FirstOrDefault(f => f.Id == t.ForceSetId);
            if (set == null) errors.Add(new("forceSet", Loc.S("CalcTaskErrSetNotFound")));
            else if ((set.Kind == "shell") != spec.ShellSet)
                errors.Add(new("forceSet", string.Format(Loc.S("CalcTaskErrSetKind"), set.Tag, Loc.S(spec.ShellSet ? "ForceSetKindShell" : "ForceSetKindBar"))));
            else if (t.ForceItemId != 0 && db.ForceItemRef(t.ForceItemId, set.Kind == "shell")?.SetId != t.ForceSetId)
                errors.Add(new("row", string.Format(Loc.S("CalcTaskErrRowNotInSet"), set.Tag)));
        }

        if (errors.Count == 0) CheckParamRefs(db, t, spec, errors);
        return errors;
    }

    /// <summary>Ссылки по Id внутри параметров: огневое сечение, материалы продавливания, длительная строка, этапы, области потерь.</summary>
    static void CheckParamRefs(DatabaseService db, CalcTask t, CalcTaskKindSpec spec, List<FieldError> errors)
    {
        switch (spec.Section)
        {
            case CalcTaskSectionKind.Fire:
            {
                int fireId = t.Kind == "fire_thermal_curvature"
                    ? FireThermalCurvatureParams.Parse(t.ParamsJson).FireSectionId
                    : FireRCheckParams.Parse(t.ParamsJson).FireSectionId;
                var fire = db.FireSections.FirstOrDefault(f => f.Id == fireId);
                if (fire == null) errors.Add(new("params.fire_section_id", Loc.S("CalcTaskNeedFireSection")));
                else if (fire.SectionId != t.SectionId) errors.Add(new("section", Loc.S("CalcTaskErrFireSection")));
                break;
            }
        }
        if (t.Kind is "punching" or "punching_batch")
        {
            var p = PunchingTaskParams.Parse(t.ParamsJson);
            if (p.Slab.ConcreteMaterialId != 0 && !db.Materials.Any(m => m.Id == p.Slab.ConcreteMaterialId))
                errors.Add(new("params.slab.concreteMaterial", Loc.S("CalcTaskErrMaterialNotFound")));
            if (p.Reinforcement.MaterialId != 0 && !db.Materials.Any(m => m.Id == p.Reinforcement.MaterialId))
                errors.Add(new("params.reinforcement.material", Loc.S("CalcTaskErrMaterialNotFound")));
        }
        if (t.Kind is "crack_width" or "crack_width_batch")
        {
            var p = CrackWidthTaskParams.Parse(t.ParamsJson);
            bool needSet = p.ForcesMode is "two_sets" or "force_item_long";
            if (needSet && p.ForceSetLongId is not int sid)
                errors.Add(new("params.ForceSetLong", Loc.S("CalcTaskErrSetRequired")));
            else if (p.ForceSetLongId is int longSet && longSet != 0)
            {
                var set = db.ForceSets.FirstOrDefault(f => f.Id == longSet);
                if (set == null || set.Kind != "bar") errors.Add(new("params.ForceSetLong", Loc.S("CalcTaskErrSetNotFound")));
                else if (p.ForcesMode == "force_item_long"
                         && (p.ForceItemLongId is not int item || db.ForceItemRef(item)?.SetId != longSet))
                    errors.Add(new("params.ForceItemLong", string.Format(Loc.S("CalcTaskErrRowNotInSet"), set.Tag)));
            }
        }
        if (t.Kind == "load_history")
        {
            var p = LoadHistoryParams.Parse(t.ParamsJson);
            int parts = db.CrossSections.FirstOrDefault(s => s.Id == t.SectionId) is StagedSection st ? st.Parts.Count : 0;
            foreach (var code in p.Validate(parts))
                errors.Add(new("params", Loc.S("LoadHistoryErr_" + code)));
            for (int i = 0; i < p.Stages.Count; i++)
            {
                var f = p.Stages[i].Force;
                if (f.Mode != "item") continue;
                var set = db.ForceSets.FirstOrDefault(s => s.Id == f.ForceSetId);
                if (set == null || set.Kind != "bar")
                    errors.Add(new($"params.Stages[{i}].Force.ForceSet", Loc.S("CalcTaskErrSetNotFound")));
                else if (db.ForceItemRef(f.ForceItemId)?.SetId != f.ForceSetId)
                    errors.Add(new($"params.Stages[{i}].Force.ForceItem", string.Format(Loc.S("CalcTaskErrRowNotInSet"), set.Tag)));
            }
        }
        if (t.Kind == "prestress_loss")
        {
            var p = string.IsNullOrWhiteSpace(t.ParamsJson) ? new PrestressLossParams()
                : JsonSerializer.Deserialize<PrestressLossParams>(t.ParamsJson) ?? new();
            var section = db.CrossSections.FirstOrDefault(s => s.Id == t.SectionId);
            for (int i = 0; i < p.Groups.Count; i++)
                if (section?.Areas.Any(a => a.Id == p.Groups[i].AreaId && a.Category == AreaCategory.RebarGroup) != true)
                    errors.Add(new($"params.Groups[{i}].Area", Loc.S("CalcTaskErrAreaNotFound")));
        }
    }

    // ── Номер, имя, запись, копия, удаление ───────────────────────────────────────────────────

    /// <summary>Номер новой задачи: следующий за наибольшим в проекте.</summary>
    public static int NextNum(DatabaseService db) => db.CalcTasks.Count > 0 ? db.CalcTasks.Max(t => t.Num) + 1 : 1;

    /// <summary>Имя по умолчанию по номеру задачи («Задача 7»; для продавливания — «Продавливание 7»).</summary>
    public static string DefaultTag(string kind, int num) =>
        string.Format(Loc.S(kind is "punching" or "punching_batch" ? "CalcTaskDefaultTagPunching" : "CalcTaskDefaultTag"), num);

    /// <summary>Проверяет и записывает задачу; новой — номер (если не задан). Ошибки — без записи.</summary>
    public static List<FieldError> Save(DatabaseService db, CalcTask t)
    {
        var errors = Validate(db, t);
        if (errors.Count > 0) return errors;
        if (t.Id == 0 && t.Num == 0) t.Num = NextNum(db);
        db.SaveCalcTask(t);
        return errors;
    }

    /// <summary>Изменились ли входные данные задачи (всё, кроме имени и номера) — результаты считались по прежним.</summary>
    public static bool InputsChanged(CalcTask before, CalcTask after) =>
        before.Kind != after.Kind || before.SectionId != after.SectionId || before.ForceSetId != after.ForceSetId
        || before.ForceItemId != after.ForceItemId || before.CalcType != after.CalcType
        || !JsonEquals(before.ParamsJson, after.ParamsJson);

    static bool JsonEquals(string? a, string? b)
    {
        if (string.Equals(a, b, StringComparison.Ordinal)) return true;
        try
        {
            var na = JsonNode.Parse(string.IsNullOrWhiteSpace(a) ? "{}" : a);
            var nb = JsonNode.Parse(string.IsNullOrWhiteSpace(b) ? "{}" : b);
            return JsonNode.DeepEquals(na, nb);
        }
        catch (JsonException) { return false; }
    }

    /// <summary>Переносит входные данные и имя (без Id и номера).</summary>
    public static void CopyInto(CalcTask src, CalcTask dst)
    {
        dst.Tag = src.Tag;
        dst.Kind = src.Kind;
        dst.SectionId = src.SectionId;
        dst.ForceSetId = src.ForceSetId;
        dst.ForceItemId = src.ForceItemId;
        dst.CalcType = src.CalcType;
        dst.ParamsJson = src.ParamsJson;
    }

    /// <summary>Число результатов задачи в проекте.</summary>
    public static int ResultCount(DatabaseService db, CalcTask t) => t.Id == 0 ? 0 : db.CalcResults.Count(r => r.TaskId == t.Id);

    /// <summary>
    /// Записывает правку <paramref name="edited"/> в задачу <paramref name="target"/>. При изменённых входных данных и
    /// <paramref name="deleteResults"/> результаты задачи удаляются. Ошибки — без записи.
    /// </summary>
    public static List<FieldError> Update(DatabaseService db, CalcTask target, CalcTask edited, bool deleteResults)
    {
        var probe = new CalcTask { Id = target.Id, Num = target.Num };
        CopyInto(edited, probe);
        var errors = Validate(db, probe);
        if (errors.Count > 0) return errors;
        bool changed = InputsChanged(target, probe);
        CopyInto(probe, target);
        db.SaveCalcTask(target);
        if (changed && deleteResults) db.DeleteCalcResultsByTaskId(target.Id);
        return errors;
    }

    /// <summary>Копия задачи: все входные данные, имя «… (копия)», новый номер; результаты не копируются.</summary>
    public static CalcTask Duplicate(DatabaseService db, CalcTask src)
    {
        var copy = new CalcTask { Num = NextNum(db) };
        CopyInto(src, copy);
        copy.Tag = string.Format(Loc.S("CalcTaskCopyTag"), src.Tag);
        db.SaveCalcTask(copy);
        return copy;
    }

    /// <summary>Удаляет задачу и её результаты.</summary>
    public static void Delete(DatabaseService db, CalcTask t) => db.DeleteCalcTask(t);
}
