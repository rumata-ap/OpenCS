using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using CScore;
using CScore.Fem;
using CScore.Fem.Fragment.Masks;
using CScore.Sp63Punching;
using CScore.Sp63Punching.Fem;
using OpenCS.Services;
using OpenCS.Utilites;

namespace OpenCS.Tasks;

/// <summary>Строка результата проверки продавливания по МКЭ: узел × ключ усилий.</summary>
public sealed record PunchingFemRow
{
    public string Key { get; init; } = "";
    public double F { get; init; }
    public double Mx { get; init; }
    public double My { get; init; }
    /// <summary>"from_below" | "from_above".</summary>
    public string Side { get; init; } = "from_below";
    /// <summary>"column" | "contour".</summary>
    public string Method { get; init; } = "column";
    public double? ColumnAction { get; init; }
    public double? BaseLoad { get; init; }
    public double? ContourForce { get; init; }
    public double Ratio { get; init; }
    public double RatioConcrete { get; init; }
    public double RatioMaxSw { get; init; }
    /// <summary>"concrete" | "reinforcement" | "not_ensured" | "error".</summary>
    public string Status { get; init; } = "concrete";
    public bool Passed { get; init; }
    /// <summary>Определяющий вариант контура (id ядра).</summary>
    public string Governing { get; init; } = "";
    public string? Error { get; init; }
    /// <summary>Пилон из пластин: сила по разрезу у торца −x, +x и в середине, кН.</summary>
    public double? PylonMinus { get; init; }
    public double? PylonPlus { get; init; }
    public double? PylonMiddle { get; init; }
    /// <summary>Пилон из пластин: наибольший Кисп торцов (П-контур с силой торца).</summary>
    public double? PylonEndRatio { get; init; }
}

/// <summary>Узел результата: геометрия, статус, огибающая по ключам и строки.</summary>
public sealed record PunchingFemJointResult
{
    public string Node { get; init; } = "";
    public int Slab { get; init; }
    /// <summary>Центр грузовой площади в общих осях, м.</summary>
    public double[] Origin { get; init; } = [];
    /// <summary>Ось x узла в общих осях.</summary>
    public double[] AxisX { get; init; } = [];
    public string Section { get; init; } = "";
    public string SlabSection { get; init; } = "";
    public double H { get; init; }
    public double H0 { get; init; }
    public int Edges { get; init; }
    public int Holes { get; init; }
    public List<string> Bars { get; init; } = [];
    /// <summary>"concrete" | "reinforcement" | "not_ensured" | "no_check".</summary>
    public string Status { get; init; } = "no_check";
    public bool Passed { get; init; }
    public double Ratio { get; init; }
    public string GoverningKey { get; init; } = "";
    public string? NoCheck { get; init; }
    public string? NoCheckText { get; init; }
    public List<PunchingWarningJson> Warnings { get; init; } = [];
    /// <summary>Контур грузовой площади в плане (общие X, Y), м.</summary>
    public List<double[]> Area { get; init; } = [];
    /// <summary>Параметры одиночной задачи по определяющему ключу (без него — с нулевыми усилиями).</summary>
    public string ParamsJson { get; init; } = "";
    public List<PunchingFemRow> Rows { get; init; } = [];
}

/// <summary>Предупреждение в JSON результата.</summary>
public sealed record PunchingWarningJson(string Code, string Text);

/// <summary>Плита для плана перекрытия: петли свободных рёбер в плане (общие X, Y), м; первая — наибольшая.</summary>
public sealed record PunchingFemSlabOutline
{
    public int Index { get; init; }
    public double Z { get; init; }
    public List<List<double[]>> Loops { get; init; } = [];
}

/// <summary>Сводка результата проверки продавливания по МКЭ (<see cref="CalcResult.DataJson"/>).</summary>
public sealed record PunchingFemCheckData
{
    public string NormCode { get; init; } = PunchingFemCheckParams.NormCode;
    public string Schema { get; init; } = "";
    public string Source { get; init; } = "";
    public string SourceKind { get; init; } = "";
    public bool Passed { get; init; }
    public double Ratio { get; init; }
    /// <summary>Наибольший Кисп под общим именем (его читает консоль: <c>run checks</c>).</summary>
    public double Utilization => Ratio;
    public int TotalJoints { get; init; }
    public int PassedJoints { get; init; }
    public int FailedJoints { get; init; }
    public int NoCheckJoints { get; init; }
    public int Keys { get; init; }
    public string? Error { get; init; }
    public List<PunchingWarningJson> Warnings { get; init; } = [];
    public List<PunchingFemJointResult> Joints { get; init; } = [];
    /// <summary>Плиты с узлами — для плана перекрытия.</summary>
    public List<PunchingFemSlabOutline> Slabs { get; init; } = [];
    /// <summary>Определяющий узел (наибольший Кисп).</summary>
    public string? GoverningNode { get; init; }
    /// <summary>DataJson одиночной задачи по определяющей строке определяющего узла (с ходом вычислений) — для отчёта.</summary>
    public string? GoverningDetail { get; init; }

    static readonly JsonSerializerOptions Opts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals,
    };

    public string ToJson() => JsonSerializer.Serialize(this, Opts);

    public static PunchingFemCheckData? Parse(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try { return JsonSerializer.Deserialize<PunchingFemCheckData>(json, Opts); }
        catch (JsonException) { return null; }
    }
}

/// <summary>
/// Проверка продавливания по МКЭ-схеме (<c>punching_check</c>, спека 2026-11-15, срез 3): узлы схемы → усилия по
/// ключам источника → одиночная задача по каждому узлу и ключу → огибающая по узлам. Узел, который не проверяется,
/// входит в результат со статусом «нет проверки» и причиной. Читает БД — вызывать в потоке, где можно читать БД.
/// </summary>
public static class PunchingFemCheckRunner
{
    static readonly CultureInfo Ru = CultureInfo.GetCultureInfo("ru-RU");

    /// <summary>Допуск невязки способов: доля |F| и абсолютный, кН.</summary>
    const double MismatchShare = 0.01, MismatchAbs = 0.5;

    public static CalcResult Run(DatabaseService db, FemCheck check, IProgress<double>? progress = null, CancellationToken token = default)
    {
        ArgumentNullException.ThrowIfNull(db);
        ArgumentNullException.ThrowIfNull(check);
        string created = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
        var p = PunchingFemCheckParams.Parse(check.ParamsJson);
        var data = Compute(db, check.SchemaId, p, CalcTypeOf(check), progress, token);
        return new CalcResult
        {
            TaskId = 0, TaskKind = PunchingFemCheckParams.NormCode, TaskTag = check.DisplayTag, Created = created,
            Status = data.Error != null ? "error" : data.Passed ? "ok" : "not_passed",
            DataJson = data.ToJson(),
        };
    }

    /// <summary>Тип расчёта (ПС1 по умолчанию; переопределение проверки — если задано).</summary>
    static CalcType CalcTypeOf(FemCheck check) =>
        check.CalcTypeOverride is { } s && Enum.TryParse<CalcType>(s, out var t) ? t : CalcType.C;

    /// <summary>Расчёт проверки: сводка с узлами и строками.</summary>
    public static PunchingFemCheckData Compute(DatabaseService db, int schemaId, PunchingFemCheckParams p, CalcType calcType,
        IProgress<double>? progress = null, CancellationToken token = default)
    {
        var schema = db.FemSchemas.FirstOrDefault(s => s.Id == schemaId);
        if (schema == null) return new PunchingFemCheckData { Error = "Схема проверки не найдена." };
        var warnings = new List<PunchingWarningJson>();
        var diag = new List<FemValidationDiagnostic>();

        PunchingFemSource? source = null;
        if (p.IsCsfea)
        {
            if (!int.TryParse(p.SourceRef, NumberStyles.Integer, CultureInfo.InvariantCulture, out int analysisId))
                return new PunchingFemCheckData { Schema = schema.Tag, Error = "Не выбрана постановка расчёта CSfea — источник усилий." };
            source = PunchingFemService.OpenCsfea(db, schema, analysisId, diag);
        }
        else if (p.SourceKind is FragmentMaskSet.SourceLira or FragmentMaskSet.SourceScad)
            source = PunchingFemService.OpenImported(db, schema, p.SourceKind, diag);
        else
            diag.Add(new("punching_fem_source_unsupported", $"Источник усилий «{p.SourceKind}» не поддерживается.", true));
        foreach (var d in diag.Where(d => !d.IsError)) warnings.Add(new(d.Code, d.Message));
        if (source == null)
            return new PunchingFemCheckData
            {
                Schema = schema.Tag, Warnings = warnings,
                Error = diag.FirstOrDefault(d => d.IsError)?.Message ?? "Источник усилий не открыт.",
            };
        if (source.Nodal == null && p.MainMethod != PunchingFemForceMethod.Column)
            warnings.Add(new("punching_fem_nodal_unavailable",
                $"Сила по разрезу основания пирамиды недоступна ({source.NodalUnavailable}) — продавливающая сила по стержням колонн."));

        var schemaData = PunchingFemService.Load(db, schema, p.FinderOptions);
        var materials = db.Materials.ToList();
        var excluded = p.Overrides.Where(o => o.Excluded).Select(o => o.Node).ToHashSet(StringComparer.Ordinal);
        var joints = schemaData.Joints.Where(j => !excluded.Contains(j.NodeTag)).ToList();
        var results = new List<PunchingFemJointResult>();
        for (int i = 0; i < joints.Count; i++)
        {
            token.ThrowIfCancellationRequested();
            results.Add(Joint(schemaData, source, joints[i], p, materials, calcType));
            progress?.Report((i + 1.0) / Math.Max(1, joints.Count));
        }
        if (joints.Count == 0)
            warnings.Add(new("punching_fem_no_joints",
                "В схеме не найдено узлов «колонна — плита»: стержень под углом к горизонтальной плите с концом в её узле или в теле АЖТ."));

        int failed = results.Count(r => r.Status != "no_check" && !r.Passed);
        int noCheck = results.Count(r => r.Status == "no_check");
        var govJoint = results.Where(r => r.Status != "no_check" && r.Rows.Count > 0).MaxBy(r => r.Ratio);
        string? detail = null;
        if (govJoint != null && RowParams(govJoint, govJoint.Rows.First(r => r.Key == govJoint.GoverningKey)) is { } gp)
        {
            var st = PunchingRunner.ResolveStrengths(gp, materials, calcType, out var me);
            if (me.Count == 0 && gp.Validate().Count == 0)
            {
                var input = gp.ToInput(st.Rbt, st.Rsw);
                var task = new CalcTask { Kind = PunchingRunner.Kind, CalcType = calcType, Tag = $"{schema.Tag}, узел {govJoint.Node}, {govJoint.GoverningKey}" };
                detail = PunchingRunner.Serialize(task, gp, st, input, PunchingChecker.Check(input));
            }
        }
        return new PunchingFemCheckData
        {
            GoverningNode = govJoint?.Node, GoverningDetail = detail,
            Schema = schema.Tag, Source = source.Label, SourceKind = source.Kind,
            Passed = results.Count > 0 && failed == 0 && noCheck == 0,
            Ratio = results.Where(r => r.Status != "no_check").Select(r => r.Ratio).DefaultIfEmpty(0).Max(),
            TotalJoints = results.Count, PassedJoints = results.Count(r => r.Passed), FailedJoints = failed, NoCheckJoints = noCheck,
            Keys = source.Keys.Count, Warnings = warnings, Joints = results,
            Slabs = Outlines(schemaData, results.Select(r => r.Slab).ToHashSet()),
        };
    }

    /// <summary>Петли плит с узлами в плане; почти коллинеарные вершины (отклонение до 0,5°) выбрасываются.</summary>
    static List<PunchingFemSlabOutline> Outlines(PunchingFemSchemaData data, IReadOnlySet<int> slabs)
    {
        var list = new List<PunchingFemSlabOutline>();
        foreach (var s in data.Slabs.Where(s => slabs.Contains(s.Index)))
        {
            var loops = new List<List<double[]>>();
            foreach (var loop in s.Loops)
            {
                var pts = loop.Where(data.Model.Nodes.ContainsKey).Select(t => data.Model.Nodes[t]).Select(n => new[] { n.X, n.Y }).ToList();
                var kept = new List<double[]>();
                for (int i = 0; i < pts.Count; i++)
                {
                    var a = pts[(i - 1 + pts.Count) % pts.Count];
                    var b = pts[i];
                    var c = pts[(i + 1) % pts.Count];
                    double ux = b[0] - a[0], uy = b[1] - a[1], vx = c[0] - b[0], vy = c[1] - b[1];
                    double lu = Math.Sqrt(ux * ux + uy * uy), lv = Math.Sqrt(vx * vx + vy * vy);
                    if (lu < 1e-9 || lv < 1e-9) continue;
                    if (Math.Abs(ux * vy - uy * vx) / (lu * lv) > Math.Sin(0.5 * Math.PI / 180) || (ux * vx + uy * vy) < 0) kept.Add(b);
                }
                if (kept.Count >= 3) loops.Add(kept);
            }
            loops = loops.OrderByDescending(l => Math.Abs(Area(l))).ToList();
            list.Add(new PunchingFemSlabOutline { Index = s.Index, Z = s.Point.Z, Loops = loops });
        }
        return list;
    }

    static double Area(List<double[]> p)
    {
        double a = 0;
        for (int i = 0; i < p.Count; i++)
        {
            var q = p[(i + 1) % p.Count];
            a += p[i][0] * q[1] - q[0] * p[i][1];
        }
        return a / 2;
    }

    static PunchingFemJointResult Joint(PunchingFemSchemaData data, PunchingFemSource source, PunchingFemJoint joint,
        PunchingFemCheckParams p, IReadOnlyList<Material> materials, CalcType calcType)
    {
        var warnings = joint.Warnings.Select(w => new PunchingWarningJson(w.Code, w.Text)).ToList();
        var baseResult = new PunchingFemJointResult
        {
            Node = joint.NodeTag, Slab = joint.SlabIndex,
            Origin = [joint.Origin.X, joint.Origin.Y, joint.Origin.Z], AxisX = [joint.AxisX.X, joint.AxisX.Y, joint.AxisX.Z],
            Section = joint.SectionLabel, SlabSection = joint.SlabLabel, H = joint.H, H0 = joint.H0,
            Edges = joint.Edges.Count, Holes = joint.Holes.Count, Bars = joint.Bars.Select(b => b.ElemTag).ToList(),
            Warnings = warnings,
            Area = joint.Area == null ? [] : PunchingContourBuilder.AreaPolygon(joint.Area)
                .Where((_, i) => joint.Area is not (PunchingLoadArea.Circle or PunchingLoadArea.Ring) || i % 20 == 0)
                .Select(q => joint.ToGlobal(q)).Select(g => new[] { g.X, g.Y }).ToList(),
        };
        if (joint.NoCheck != null)
            return baseResult with
            {
                Status = "no_check", NoCheck = joint.NoCheck,
                NoCheckText = joint.NoCheck == PunchingFemNoCheck.AreaUnknown && !source.Kind.StartsWith("csfea", StringComparison.Ordinal)
                    ? joint.NoCheckText + " Назначьте стержням сечения (у импорта — «Создать сечения стержней» по жёсткостям)."
                    : joint.NoCheckText,
            };

        var forces = PunchingFemService.Evaluate(data, source, joint, p.MainMethod);
        if (forces.CutFailure != null && source.Nodal != null)
            warnings.Add(new("punching_fem_cut_failed", $"Разрез основания пирамиды не удался ({forces.CutFailure}) — сила по стержням колонн."));
        if (forces.ByKey.Count == 0)
            return baseResult with
            {
                Status = "no_check", NoCheck = PunchingFemNoCheck.NoForces,
                NoCheckText = $"Нет усилий колонн узла в результате «{source.Label}» ({forces.Missing ?? "нет ключей"}).",
            };

        var rows = new List<PunchingFemRow>();
        PunchingGeometryCache? geometry = null;
        PunchingStrengths? strengths = null;
        PunchingTaskParams? governingParams = null;
        double worst = double.NegativeInfinity;
        double mismatchWorst = 0;
        string mismatchKey = "";
        bool baseUnknown = false;
        foreach (var (key, label) in source.Keys)
        {
            if (!forces.ByKey.TryGetValue(key, out var f)) continue;
            if (f.BaseLoad == null) baseUnknown = true;
            if (!joint.Ribs && f.Mismatch is double mm && Math.Abs(mm) > MismatchShare * Math.Abs(f.F) + MismatchAbs && Math.Abs(mm) > mismatchWorst)
            {
                mismatchWorst = Math.Abs(mm);
                mismatchKey = label;
            }
            var tp = PunchingFemService.TaskParams(joint, f, p, data.Schema.Tag, label);
            var row = new PunchingFemRow
            {
                Key = label, F = tp.Forces.F, Mx = tp.Forces.Mx, My = tp.Forces.My, Side = tp.Forces.LoadSide,
                Method = f.Method == PunchingFemForceMethod.Contour ? "contour" : "column",
                ColumnAction = f.ColumnAction, BaseLoad = f.BaseLoad, ContourForce = f.ContourForce,
            };
            var errors = tp.Validate();
            if (errors.Count == 0 && strengths == null)
            {
                strengths = PunchingRunner.ResolveStrengths(tp, materials, calcType, out var materialErrors);
                if (materialErrors.Count > 0) { errors.AddRange(materialErrors); strengths = null; }
                else foreach (var w in strengths.Warnings) warnings.Add(new("material", w));
            }
            if (errors.Count > 0 || strengths == null)
            {
                string text = string.Join("; ", errors.Select(c => Loc.S("PunchingErr_" + c, c)));
                return baseResult with
                {
                    Status = "no_check", NoCheck = "input_invalid",
                    NoCheckText = $"Исходные данные узла не годятся для расчёта: {text}.", ParamsJson = tp.ToJson(),
                };
            }
            var input = tp.ToInput(strengths.Rbt, strengths.Rsw);
            geometry ??= PunchingGeometryCache.Prepare(input);
            var r = PunchingChecker.Check(input, includeTrace: false, geometry);
            row = row with
            {
                Ratio = r.Utilization, RatioConcrete = r.UtilizationConcreteOnly, RatioMaxSw = r.UtilizationMaxReinforcement,
                Status = StatusCode(r.Status), Passed = r.Passed, Governing = r.Governing?.Variant.Id ?? "",
            };
            if (f.PylonParts is var (pm, pp, pmid) && joint.Area is PunchingLoadArea.Rectangle pr)
            {
                // Торцы пилона из пластин: каждый — свой П-контур с силой торца по разрезу (М = 0), определяет худший.
                row = row with { PylonMinus = pm, PylonPlus = pp, PylonMiddle = pmid };
                double side = tp.Forces.LoadSide == "from_above" ? -1 : 1;
                foreach (var (signed, sign, id) in new[] { (pm, -1, "pylon_end_minus"), (pp, 1, "pylon_end_plus") })
                {
                    double fe = side * signed;
                    if (!(fe > 0)) continue;
                    var ep = tp with
                    {
                        Forces = tp.Forces with { F = fe, Mx = 0, My = 0 },
                        CustomContours = [EndContour(pr, input.H0, sign, id)],
                    };
                    var er = PunchingChecker.Check(ep.ToInput(strengths.Rbt, strengths.Rsw), includeTrace: false);
                    if (er.Variants.FirstOrDefault(v => v.Variant.Kind == PunchingVariantKind.Custom) is not { } ev) continue;
                    row = row with { PylonEndRatio = Math.Max(row.PylonEndRatio ?? 0, ev.Utilization) };
                    if (ev.Utilization > row.Ratio)
                        row = row with
                        {
                            Ratio = ev.Utilization, Governing = id, Passed = row.Passed && ev.Passed,
                            Status = StatusCode(ev.UtilizationConcreteOnly <= 1 ? PunchingStatus.ConcreteSufficient
                                : ev.UtilizationMaxReinforcement <= 1 ? PunchingStatus.ReinforcementRequired : PunchingStatus.NotEnsured),
                        };
                    else if (!ev.Passed) row = row with { Passed = false };
                }
            }
            rows.Add(row);
            if (r.Utilization > worst || governingParams == null)
            {
                worst = r.Utilization;
                governingParams = tp;
            }
        }
        if (baseUnknown)
            warnings.Add(new("punching_fem_base_load_unknown", "Нагрузки схемы в основании пирамиды неизвестны — сила по стержням без вычета (в запас)."));
        if (mismatchWorst > 0)
            warnings.Add(new("punching_fem_mismatch", string.Create(Ru,
                $"Сила по стержням колонн и по разрезу основания расходятся до {mismatchWorst:0.##} кН («{mismatchKey}»): проверьте связи и опоры у узла.")));

        var gov = rows.MaxBy(r => r.Ratio)!;
        var status = rows.Select(r => r.Status).OrderByDescending(Rank).First();
        return baseResult with
        {
            Status = status, Passed = rows.All(r => r.Passed), Ratio = gov.Ratio, GoverningKey = gov.Key,
            ParamsJson = governingParams!.ToJson(), Rows = rows, Warnings = warnings.DistinctBy(w => w.Code + w.Text).ToList(),
        };
    }

    static string StatusCode(PunchingStatus s) => s switch
    {
        PunchingStatus.ConcreteSufficient => "concrete",
        PunchingStatus.ReinforcementRequired => "reinforcement",
        _ => "not_ensured",
    };

    /// <summary>Строгость статуса: «нет проверки» — худший.</summary>
    public static int Rank(string status) => status switch
    {
        "concrete" => 0,
        "reinforcement" => 1,
        "not_ensured" => 2,
        _ => 3,
    };

    /// <summary>
    /// П-контур торца пилона (СП 430, рис. 6.1а; как у <see cref="PunchingContourBuilder"/>): торцевой отрезок Lmin + h0 на
    /// h0/2 за торцом и ветви длиной Lmin + h0 вдоль пилона; сторона внутрь пилона выключена.
    /// </summary>
    static PunchingCustomContourParams EndContour(PunchingLoadArea.Rectangle r, double h0, int sign, string name)
    {
        bool alongX = r.Bx >= r.By;
        double cmin = Math.Min(r.Bx, r.By), cmax = Math.Max(r.Bx, r.By);
        double w = cmin + h0, leg = cmin + h0, end = cmax / 2 + h0 / 2;
        double[] P(double u, double v) => alongX ? [sign * u, v] : [v, sign * u];
        return new PunchingCustomContourParams
        {
            Name = name,
            Points = [P(end - leg, -w / 2), P(end, -w / 2), P(end, w / 2), P(end - leg, w / 2)],
            InactiveEdges = [3],
        };
    }

    /// <summary>Параметры одиночной задачи по строке узла: параметры узла с усилиями и источником строки.</summary>
    public static PunchingTaskParams? RowParams(PunchingFemJointResult joint, PunchingFemRow row)
    {
        if (string.IsNullOrEmpty(joint.ParamsJson)) return null;
        var p = PunchingTaskParams.Parse(joint.ParamsJson);
        return p with
        {
            Forces = p.Forces with { F = row.F, Mx = row.Mx, My = row.My, LoadSide = row.Side },
            Source = (p.Source ?? new PunchingSourceParams()) with
            {
                Key = row.Key, Method = row.Method, ColumnAction = row.ColumnAction, BaseLoad = row.BaseLoad, ContourForce = row.ContourForce,
            },
        };
    }
}
