using System.Text.Json;
using System.Text.Json.Nodes;
using CScore;
using CScore.CalculationTrace;
using CScore.Sp63Punching;
using CScore.Sp63Shear;
using OpenCS.Utilites;

namespace OpenCS.Tasks;

/// <summary>Расчётные сопротивления задачи продавливания и их источник.</summary>
/// <param name="Rbt">Rbt, кПа.</param>
/// <param name="Rsw">Rsw, кПа (0 — арматуры нет).</param>
/// <param name="ConcreteTag">Обозначение бетона или «вручную».</param>
/// <param name="RebarTag">Обозначение арматуры, «вручную» или пусто.</param>
/// <param name="Warnings">Оговорки по материалам.</param>
public sealed record PunchingStrengths(double Rbt, double Rsw, string ConcreteTag, string RebarTag, IReadOnlyList<string> Warnings);

/// <summary>Выполняет расчёт на продавливание по параметрам задачи и формирует JSON результата.</summary>
public static class PunchingRunner
{
    /// <summary>Вид задачи.</summary>
    public const string Kind = "punching";

    /// <summary>Выполняет задачу; ошибки — статусом error, не исключением.</summary>
    public static CalcResult Run(CalcTask task, CalcSettings settings, TaskRunContext? ctx)
    {
        string created = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
        try
        {
            var p = PunchingTaskParams.Parse(task.ParamsJson);
            var errors = p.Validate();
            if (errors.Count > 0) return Error(task, created, errors);
            var strengths = ResolveStrengths(p, ctx?.Database?.Materials ?? [], task.CalcType, out var materialErrors);
            if (materialErrors.Count > 0) return Error(task, created, materialErrors);

            var input = p.ToInput(strengths.Rbt, strengths.Rsw);
            var result = PunchingChecker.Check(input);
            return new CalcResult
            {
                TaskId = task.Id, TaskKind = task.Kind, TaskTag = task.Tag, Created = created,
                Status = result.Passed ? "ok" : "not_passed",
                DataJson = Serialize(task, p, strengths, input, result)
            };
        }
        catch (Exception ex)
        {
            return new CalcResult
            {
                TaskId = task.Id, TaskKind = task.Kind, TaskTag = task.Tag, Created = created, Status = "error",
                DataJson = JsonSerializer.Serialize(new { error = ex.Message, traceVersion = 1 })
            };
        }
    }

    /// <summary>
    /// Rbt и Rsw: ручные значения или характеристики материалов проекта для типа расчёта
    /// (γb1 и прочие коэффициенты — в характеристиках материала). Коды ошибок — как у <see cref="PunchingTaskParams.Validate"/>.
    /// </summary>
    public static PunchingStrengths ResolveStrengths(PunchingTaskParams p, IEnumerable<Material> materials, CalcType calc,
        out List<string> errors)
    {
        errors = [];
        var warnings = new List<string>();
        var list = materials as IReadOnlyCollection<Material> ?? materials.ToList();
        double rbt = 0;
        string concreteTag = "";   // пусто — Rbt задано вручную
        if (p.Slab.RbtOverride is double manualRbt) rbt = manualRbt;
        else
        {
            var concrete = list.FirstOrDefault(m => m.Id == p.Slab.ConcreteMaterialId && m.Type == MatType.Concrete);
            var chars = concrete?.GetChars(calc);
            if (concrete is null) errors.Add("concrete_missing");
            else if (chars is null || !(Math.Abs(chars.Ft) > 0)) errors.Add("concrete_no_chars");
            else { rbt = Math.Abs(chars.Ft); concreteTag = concrete.Tag; }
        }

        double rsw = 0;
        string rebarTag = "";
        if (p.Reinforcement.Enabled)
        {
            if (p.Reinforcement.RswOverride is double manualRsw)
            {
                rsw = StirrupResolver.LimitRsw(manualRsw);
                if (manualRsw > StirrupResolver.MaxRsw)
                    warnings.Add(string.Format(Loc.S("PunchingWarnRswLimited"), StirrupResolver.MaxRsw / 1000));
            }
            else
            {
                var rebar = list.FirstOrDefault(m => m.Id == p.Reinforcement.MaterialId);
                var chars = rebar?.GetChars(calc);
                if (rebar is null) errors.Add("rebar_missing");
                else if (chars is null) errors.Add("rebar_no_chars");
                else
                {
                    rsw = StirrupResolver.RswFromChars(chars, out string? warning);
                    rebarTag = rebar.Tag;
                    if (warning is not null) warnings.Add(warning);
                }
            }
        }
        return new PunchingStrengths(rbt, rsw, concreteTag, rebarTag, warnings);
    }

    /// <summary>Общие шаги трассировки задачи: h0, Asw, отпор и половина Mloc.</summary>
    public static IReadOnlyList<CalculationTraceStep> CommonTrace(PunchingTaskParams p, PunchingStrengths s, PunchingResult r)
    {
        var steps = new List<CalculationTraceStep>
        {
            Step("sp63.punching.h0x", "п. 8.1.46", "Punching_Trace_H0x", @"h_{0x} = h - a_x", @"{h} - {ax}", "{h0x}",
                ("h", p.Slab.H, CalculationUnit.Meter), ("ax", p.Slab.Ax, CalculationUnit.Meter), ("h0x", p.H0x, CalculationUnit.Meter)),
            Step("sp63.punching.h0y", "п. 8.1.46", "Punching_Trace_H0y", @"h_{0y} = h - a_y", @"{h} - {ay}", "{h0y}",
                ("h", p.Slab.H, CalculationUnit.Meter), ("ay", p.Slab.Ay, CalculationUnit.Meter), ("h0y", p.H0y, CalculationUnit.Meter)),
            Step("sp63.punching.h0", "п. 8.1.46", "Punching_Trace_H0", @"h_0 = 0{,}5\,(h_{0x} + h_{0y})", @"0{,}5 \cdot ({h0x} + {h0y})", "{h0}",
                ("h0x", p.H0x, CalculationUnit.Meter), ("h0y", p.H0y, CalculationUnit.Meter), ("h0", p.H0, CalculationUnit.Meter)),
            Step("sp63.punching.rbt", "пп. 6.1.11, 6.1.12", "Punching_Trace_Rbt", @"R_{bt}", "", "{Rbt}",
                ("Rbt", s.Rbt, CalculationUnit.Kilopascal))
        };
        if (p.Reinforcement.Enabled)
        {
            if (p.Reinforcement.ByRows)
            {
                steps.Add(Step("sp63.punching.as1", "п. 8.1.48", "Punching_Trace_As1", @"A_{s1} = \frac{\pi d_{sw}^2}{4}",
                    @"\pi \cdot {d}^2 / 4", "{As1}",
                    ("d", p.Reinforcement.Diameter, CalculationUnit.Meter), ("As1", p.Reinforcement.BarArea, CalculationUnit.SquareMeter)));
                if (r.Rows is { } rc)
                    steps.Add(Step("sp63.punching.nsw", "п. 8.1.48, п. 10.3.17", "Punching_Trace_Nsw", @"n_{sw}", "", "{n}",
                        ("n", rc.Counted, CalculationUnit.Count)));
            }
            else
                steps.Add(Step("sp63.punching.asw", "п. 8.1.48", "Punching_Trace_Asw", @"A_{sw} = n\,\frac{\pi d_{sw}^2}{4}",
                    @"{n} \cdot \pi \cdot {d}^2 / 4", "{Asw}",
                    ("n", p.Reinforcement.Count, CalculationUnit.Count), ("d", p.Reinforcement.Diameter, CalculationUnit.Meter),
                    ("Asw", p.Reinforcement.Asw, CalculationUnit.SquareMeter)));
            steps.Add(Step("sp63.punching.rsw", "табл. 6.15", "Punching_Trace_Rsw", @"R_{sw}", "", "{Rsw}",
                ("Rsw", s.Rsw, CalculationUnit.Kilopascal)));
        }
        // У узла стены моменты задаются по п. 6.3.8 СП 430 (½ΔM у торца — шаг варианта), флажок Mloc не действует.
        if (p.Forces.MomentsAreMloc && !p.Wall.IsWall)
        {
            steps.Add(Step("sp63.punching.mloc_x", "п. 8.1.46", "Punching_Trace_MlocX", @"M_x = 0{,}5\,M_{loc,x}", @"0{,}5 \cdot {Mloc}", "{M}",
                ("Mloc", p.Forces.Mx, CalculationUnit.KilonewtonMeter), ("M", 0.5 * p.Forces.Mx, CalculationUnit.KilonewtonMeter)));
            steps.Add(Step("sp63.punching.mloc_y", "п. 8.1.46", "Punching_Trace_MlocY", @"M_y = 0{,}5\,M_{loc,y}", @"0{,}5 \cdot {Mloc}", "{M}",
                ("Mloc", p.Forces.My, CalculationUnit.KilonewtonMeter), ("M", 0.5 * p.Forces.My, CalculationUnit.KilonewtonMeter)));
        }
        if (p.Forces.CounterPressure != 0)
            steps.Add(Step("sp63.punching.fred", "п. 8.1.46", "Punching_Trace_Fred", @"F = F_0 - q\,A_b", @"{F0} - {q} \cdot {Ab}", "{F}",
                ("F0", p.Forces.F, CalculationUnit.Kilonewton), ("q", p.Forces.CounterPressure, CalculationUnit.Kilopascal),
                ("Ab", r.BaseArea, CalculationUnit.SquareMeter), ("F", r.Fred, CalculationUnit.Kilonewton)));
        return steps;
    }

    /// <summary>JSON результата: объект ядра + сцена, исходные данные, трассировка и подбор арматуры.</summary>
    /// <param name="design">Подбор поперечной арматуры (ширина зоны — перестроением геометрии); false — без подбора.</param>
    public static string Serialize(CalcTask task, PunchingTaskParams p, PunchingStrengths s, PunchingInput input, PunchingResult result,
        bool design = true)
    {
        var root = JsonNode.Parse(PunchingResultJson.Write(result))!.AsObject();
        if (design && result.Status != PunchingStatus.ConcreteSufficient)
        {
            // Без арматуры в задаче — подбор равномерной qsw; с арматурой — множитель к заданной раскладке.
            var d = PunchingDesign.Solve(input);
            root["design"] = JsonNode.Parse(PunchingResultJson.WriteDesign(d));
        }
        var details = root["details"]!.AsArray();
        for (int i = 0; i < result.Variants.Count; i++)
        {
            var d = details[i]!.AsObject();
            var v = result.Variants[i];
            d["trace"] = new JsonArray(v.Trace.Select(TraceNode).ToArray<JsonNode?>());
            d["kb"] = Finite(v.UtilizationConcreteOnly);
            d["kmax"] = Finite(v.UtilizationMaxReinforcement);
            d["Fb"] = Finite(v.Fb);
            d["Fsw"] = Finite(v.Fsw);
            d["Fred"] = Finite(v.Fred);
        }
        // Предупреждения материалов — в общий список (как у ядра: код + текст).
        var warnings = root["warnings"]!.AsArray();
        foreach (var w in s.Warnings) warnings.Add(new JsonObject { ["code"] = "material", ["text"] = w });

        root["traceVersion"] = 1;
        root["traceCommon"] = new JsonArray(CommonTrace(p, s, result).Select(TraceNode).ToArray<JsonNode?>());
        root["scene"] = JsonNode.Parse(PunchingSceneJson.ToJson(PunchingScene.From(input, result)));
        root["calcType"] = task.CalcType.ToString();
        root["inputs"] = JsonSerializer.SerializeToNode(new Dictionary<string, object?>
        {
            ["wall"] = p.Wall.IsWall ? p.Wall.Kind : null,
            ["a1"] = p.Wall.IsWall ? p.Wall.A1 : null, ["a2"] = p.Wall.IsCorner ? p.Wall.A2 : null,
            ["wallFigure"] = p.Wall.IsCorner ? (p.Options.WallFigure == "amd2" ? "amd2" : "amd1") : null,
            ["shape"] = p.Area.Shape, ["bx"] = p.Area.Bx, ["by"] = p.Area.By, ["d"] = p.Area.D, ["t"] = p.Area.T,
            ["h"] = p.Slab.H, ["ax"] = p.Slab.Ax, ["ay"] = p.Slab.Ay,
            ["capitalHc"] = input.Capital?.Hc,
            ["capitalD"] = input.Capital?.Area is PunchingLoadArea.Circle capD ? capD.D : null,
            ["capitalBx"] = input.Capital?.Area is PunchingLoadArea.Rectangle capR ? capR.Bx : null,
            ["capitalBy"] = input.Capital?.Area is PunchingLoadArea.Rectangle capR2 ? capR2.By : null,
            ["capitalDx"] = input.Capital is { Area: PunchingLoadArea.Rectangle } capO ? capO.Offset.X : null,
            ["capitalDy"] = input.Capital is { Area: PunchingLoadArea.Rectangle } capO2 ? capO2.Offset.Y : null,
            ["h0x"] = p.H0x, ["h0y"] = p.H0y, ["h0"] = p.H0,
            ["concrete"] = s.ConcreteTag, ["rbt"] = s.Rbt, ["rbtManual"] = p.Slab.RbtOverride is not null,
            ["sw"] = p.Reinforcement.Enabled, ["rebar"] = s.RebarTag, ["rsw"] = s.Rsw,
            ["swDiameter"] = p.Reinforcement.Diameter, ["swCount"] = p.Reinforcement.Count, ["swStep"] = p.Reinforcement.Step,
            ["asw"] = p.Reinforcement.Enabled ? p.Reinforcement.Asw : 0,
            ["qsw"] = input.Reinforcement is { Cross: null } ri ? PunchingSwLayout.QUniform(ri, input.H0) : 0,
            ["swZone"] = p.Reinforcement.ByRows ? input.Reinforcement?.EffectiveZoneExtent : p.Reinforcement.ZoneExtent,
            ["swLayout"] = p.Reinforcement.Layout,
            ["swFirstRow"] = p.Reinforcement.FirstRow, ["swRowStep"] = p.Reinforcement.ByRows ? p.Reinforcement.RowStep : null,
            ["swRowCount"] = p.Reinforcement.ByRows ? p.Reinforcement.RowCount : null,
            ["swCrossX"] = p.Reinforcement.IsCross ? CrossNode(p.Reinforcement.CrossX) : null,
            ["swCrossY"] = p.Reinforcement.IsCross ? CrossNode(p.Reinforcement.CrossY) : null,
            ["edges"] = p.Edges.Select(e => new { side = e.Side, c = e.C, angleDeg = e.AngleDeg }).ToList(),
            ["holes"] = p.Holes.Count,
            ["F"] = p.Forces.F, ["Mx"] = p.Forces.Mx, ["My"] = p.Forces.My,
            ["momentsAreMloc"] = p.Forces.MomentsAreMloc, ["loadSide"] = p.Forces.LoadSide,
            ["counterPressure"] = p.Forces.CounterPressure,
            ["eccentricityX"] = p.Options.EccentricityX, ["eccentricityY"] = p.Options.EccentricityY,
            ["wb"] = p.Options.Wb, ["pylon"] = p.Options.Pylon, ["holeInfluence"] = p.Options.HoleInfluence,
            ["pylonReduced"] = p.Options.PylonReduced, ["mu"] = p.Options.Mu,
            // Имена контуров пользователя (custom_i) — для подписи варианта.
            ["customNames"] = p.CustomContours.Count > 0 ? p.CustomContours.Select(c => c.Name).ToList() : null,
            ["source"] = p.Source is not { } src ? null : new Dictionary<string, object?>
            {
                ["schema"] = src.Schema, ["node"] = src.Node, ["key"] = src.Key, ["method"] = src.Method,
                ["columnAction"] = src.ColumnAction, ["baseLoad"] = src.BaseLoad, ["contourForce"] = src.ContourForce,
            }
        });
        return root.ToJsonString();
    }

    static JsonNode? Finite(double v) => double.IsFinite(v) ? JsonValue.Create(v) : null;

    static Dictionary<string, object?> CrossNode(PunchingCrossArmParams a) => new()
    {
        ["lines"] = a.Lines, ["step"] = a.Step, ["width"] = a.Width, ["positive"] = a.Positive, ["negative"] = a.Negative
    };

    /// <summary>Шаг трассировки в формате отчётов (как у наклонных сечений).</summary>
    public static JsonNode TraceNode(CalculationTraceStep step)
    {
        var values = new JsonObject();
        foreach (var (key, value) in step.Values)
            values[key] = new JsonObject
            {
                ["value"] = Finite(value.Value),
                ["unit"] = value.Unit.ToString(),
                ["numberProfile"] = value.NumberProfile
            };
        return new JsonObject
        {
            ["stepId"] = step.StepId,
            ["codeReference"] = step.CodeReference,
            ["titleKey"] = step.TitleKey,
            ["formulaLatex"] = step.FormulaLatex,
            ["substitutionLatexTemplate"] = step.SubstitutionLatexTemplate,
            ["resultLatexTemplate"] = step.ResultLatexTemplate,
            ["status"] = step.Status.ToString().ToLowerInvariant(),
            ["formulaCode"] = step.FormulaCode,
            ["values"] = values
        };
    }

    internal static CalcResult Error(CalcTask task, string created, IReadOnlyList<string> codes) => new()
    {
        TaskId = task.Id, TaskKind = task.Kind, TaskTag = task.Tag, Created = created, Status = "error",
        DataJson = JsonSerializer.Serialize(new
        {
            error = string.Join("; ", codes.Select(c => Loc.S("PunchingErr_" + c, c))),
            errors = codes,
            traceVersion = 1
        })
    };

    static CalculationTraceStep Step(string id, string reference, string titleKey, string formula, string subst,
        string result, params (string Key, double Value, CalculationUnit Unit)[] values) => new()
    {
        StepId = id, CodeReference = reference, TitleKey = titleKey, FormulaLatex = formula,
        SubstitutionLatexTemplate = subst, ResultLatexTemplate = result,
        Values = values.ToDictionary(x => x.Key, x => new TraceValue(x.Value, x.Unit), StringComparer.Ordinal)
    };
}

/// <summary>Обработчик вида «punching».</summary>
public sealed class PunchingHandler : ITaskHandler
{
    /// <inheritdoc/>
    public string Kind => PunchingRunner.Kind;

    /// <inheritdoc/>
    public CalcResult Run(CalcTask task, CrossSection section, LoadItem item, CalcSettings settings, TaskRunContext? ctx = null)
        => PunchingRunner.Run(task, settings, ctx);
}
