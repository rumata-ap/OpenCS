using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using CScore;
using CScore.Fem;
using CScore.Import;
using CSfea.Core;
using CSfea.CScoreBridge.Structural;
using OpenCS.Gmsh;
using OpenCS.Tasks;
using OpenCS.Utilites;

namespace OpenCS.Services;

/// <summary>Шаг в сводке секущего расчёта: сквозной номер, стадия, доля стадии, сходимость, перемещение контрольного узла.</summary>
public sealed record FemCsfeaStepSummary(int N, int Stage, int Step, double LoadFactor, bool Refinement, bool Converged,
    int Iterations, double Residual, double? Control);

/// <summary>Стадия в сводке: тег, заданное число шагов, суммарная нагрузка (Н) в глобальных осях.</summary>
public sealed record FemCsfeaStageSummary(string Tag, int Steps, double Fx, double Fy, double Fz);

/// <summary>
/// Сводка секущего расчёта CSfea — DataJson <see cref="CalcResult"/> вида <see cref="FemCsfeaRunner.TaskKind"/>. Пишется
/// всегда для всех шагов; полные поля шагов — отдельными записями (срез 4е). При ошибке до расчёта заполнены только
/// <see cref="Errors"/> и <see cref="Report"/>.
/// </summary>
public sealed class FemCsfeaResultSummary
{
    public string Kind { get; set; } = FemCsfeaRunner.AnalysisKind;
    public bool Completed { get; set; }
    /// <summary>Стадия (с 0), на которой шаг не сошёлся и после дроблений; null — нет.</summary>
    public int? LimitStage { get; set; }
    public string? Message { get; set; }
    public double ElapsedSeconds { get; set; }
    public int Nodes { get; set; }
    public int Shells { get; set; }
    public int Beams { get; set; }
    public List<FemCsfeaStageSummary> Stages { get; set; } = [];
    public List<FemCsfeaStepSummary> Steps { get; set; } = [];
    /// <summary>Тег узла сетки контрольного узла (пусто — не задан) и его DOF.</summary>
    public string ControlNodeTag { get; set; } = "";
    public int ControlDof { get; set; }
    /// <summary>Отчёт подготовки и адаптера: ошибки, предупреждения, сведения.</summary>
    public List<string> Report { get; set; } = [];
    /// <summary>Ошибки, из-за которых расчёт не запускался (формат ошибок прочих FEM-результатов).</summary>
    public List<string> Errors { get; set; } = [];

    public string ToJson() => JsonSerializer.Serialize(this);

    public static FemCsfeaResultSummary? Parse(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try { return JsonSerializer.Deserialize<FemCsfeaResultSummary>(json); }
        catch (JsonException) { return null; }
    }

    /// <summary>Строки итога для журнала.</summary>
    public IEnumerable<string> Describe()
    {
        foreach (var e in Errors) yield return "Ошибка: " + e;
        if (Steps.Count == 0) yield break;
        yield return string.Create(CultureInfo.CurrentCulture,
            $"Узлов {Nodes}, оболочек {Shells}, стержней {Beams}; шагов {Steps.Count(s => s.Converged)}, итераций {Steps.Sum(s => s.Iterations)}, {ElapsedSeconds:0.#} с.");
        foreach (var (stage, i) in Stages.Select((s, i) => (s, i)))
        {
            var end = Steps.LastOrDefault(s => s.Stage == i && s.Converged);
            string control = end?.Control is { } c ? string.Create(CultureInfo.CurrentCulture, $", контрольный узел {ControlNodeTag}: {c * (ControlDof < 3 ? 1e3 : 1):0.###} {(ControlDof < 3 ? "мм" : "рад")}") : "";
            string state = end == null ? "не начата" : Math.Abs(end.LoadFactor - 1) < 1e-12 ? "пройдена" : string.Create(CultureInfo.CurrentCulture, $"остановлена на λ = {end.LoadFactor:0.###}");
            yield return string.Create(CultureInfo.CurrentCulture,
                $"Стадия «{stage.Tag}» (ΣFz = {stage.Fz / 1e3:0.###} кН): {state}{control}.");
        }
        if (Message != null) yield return Message;
    }
}

/// <summary>Окружение исполнителя: БД, Gmsh, журнал, параллельность пересчёта сечений.</summary>
public sealed record FemCsfeaRunContext(DatabaseService Db, GmshSettings Gmsh, ILogService Log, int MaxDegreeOfParallelism = -1);

/// <summary>Подготовленный вход: модель адаптера (null — подготовка прервана) и отчёт.</summary>
public sealed record FemCsfeaPrepared(FemRcModelInput? Input, FemRcModelResult? Adapted, List<FemValidationDiagnostic> Diagnostics)
{
    public bool HasErrors => Diagnostics.Any(d => d.IsError) || Adapted is not { HasErrors: false };

    /// <summary>Строки отчёта: ошибки, затем предупреждения и сведения.</summary>
    public IEnumerable<string> Report => Diagnostics.Concat(Adapted?.Diagnostics ?? []).OrderByDescending(d => d.IsError)
        .Select(d => (d.IsError ? "Ошибка: " : "") + d.Message);
}

/// <summary>
/// Секущий расчёт CSfea постановки схемы (Kind = <see cref="AnalysisKind"/>): сетка схемы (устаревшая — строится),
/// проверка переноса вложения SCAD, вход (<see cref="FemCsfeaInputBuilder"/>) — в вызывающем (UI) потоке; адаптер и
/// расчёт — в фоне. Отмена — <see cref="OperationCanceledException"/> (решатель откатывает незавершённый шаг).
/// </summary>
public static class FemCsfeaRunner
{
    public const string AnalysisKind = "csfea_secant";
    public const string TaskKind = "fem_csfea_secant";

    /// <summary>
    /// Подготовка без расчёта. <paramref name="buildMesh"/> = false — «Проверить вход»: сетка не перестраивается и не
    /// пишется (устаревшая — предупреждение, вход по сохранённой).
    /// </summary>
    public static async Task<FemCsfeaPrepared> PrepareAsync(FemCsfeaRunContext ctx, FemSchema schema, FemAnalysis analysis,
        bool buildMesh, CancellationToken ct)
    {
        var diag = new List<FemValidationDiagnostic>();
        var p = FemAnalysisParams.Parse(analysis.ParamsJson);
        var csfea = p.Csfea ?? new FemCsfeaParams();
        string? stepsError = null;
        if (csfea.ResultRecording == FemCsfeaRecording.Selected)
            FemCsfeaParams.ParseRecordSteps(csfea.RecordSteps, out stepsError);
        if (stepsError != null) diag.Add(new("record_steps", stepsError, true));

        await EnsureMeshAsync(ctx, schema, buildMesh, diag, ct);
        if (diag.Any(d => d.IsError)) return new(null, null, diag);

        var input = FemCsfeaInputBuilder.Build(ctx.Db, schema.Id, FemCsfeaSetup.FromAnalysis(analysis));
        CheckScadTransfer(ctx.Db, schema.Id, input, diag);
        if (!string.IsNullOrWhiteSpace(csfea.ControlNodeTag) && ControlMeshTag(input, csfea.ControlNodeTag) == null)
            diag.Add(new("control_node", $"Контрольный узел «{csfea.ControlNodeTag}» не найден ни среди узлов схемы, ни в сетке.", true));
        if (csfea.ControlDof is < 0 or > 5)
            diag.Add(new("control_dof", $"DOF контрольного узла {csfea.ControlDof} — вне 0…5.", true));
        if (diag.Any(d => d.IsError)) return new(input, null, diag);

        var adapted = await Task.Run(() => FemRcModelAdapter.Adapt(input), ct);
        return new(input, adapted, diag);
    }

    /// <summary>Подготовка и расчёт; <paramref name="progress"/> — отчёты решателя (создавать в UI-потоке).</summary>
    public static async Task<CalcResult> RunAsync(FemCsfeaRunContext ctx, FemSchema schema, FemAnalysis analysis,
        IProgress<SecantProgress>? progress, CancellationToken ct)
    {
        string created = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
        var prepared = await PrepareAsync(ctx, schema, analysis, buildMesh: true, ct);
        var summary = new FemCsfeaResultSummary { Report = prepared.Report.ToList() };
        if (prepared.HasErrors)
        {
            summary.Errors = prepared.Diagnostics.Concat(prepared.Adapted?.Diagnostics ?? [])
                .Where(d => d.IsError).Select(d => d.Message).ToList();
            return Result(analysis, created, "error", summary);
        }

        var csfea = FemAnalysisParams.Parse(analysis.ParamsJson).Csfea ?? new FemCsfeaParams();
        var adapted = prepared.Adapted!;
        string tag = analysis.Tag;
        // В журнал — строки шагов; строки итераций (с отступом) остаются в сводке итераций решателя.
        void Log(string line)
        {
            if (!line.StartsWith(' ')) ctx.Log.Info($"[{tag}] {line}");
        }
        var options = FemCsfeaSetup.SecantOptions(csfea, ctx.MaxDegreeOfParallelism, Log);

        var clock = Stopwatch.StartNew();
        var run = await Task.Run(() => RcSecantAnalysis.Run(adapted.Model, options, progress, ct), ct);
        clock.Stop();

        string? controlTag = string.IsNullOrWhiteSpace(csfea.ControlNodeTag) ? null : ControlMeshTag(prepared.Input!, csfea.ControlNodeTag);
        int? controlDof = controlTag != null && FemMeshTopology.CanonicalNodeTag(controlTag) is { } canon
            && int.TryParse(canon, NumberStyles.Integer, CultureInfo.InvariantCulture, out int nodeId)
            && run.Build.NodeIndex.ContainsKey(nodeId) ? run.Build.Dof(nodeId, csfea.ControlDof) : null;
        if (controlTag != null && controlDof == null)
            summary.Report.Add($"Контрольный узел «{csfea.ControlNodeTag}» не входит в расчётную модель — история не записана.");

        var r = run.Result;
        summary.Completed = r.Completed;
        summary.LimitStage = r.LimitStage;
        summary.Message = r.Message;
        summary.ElapsedSeconds = clock.Elapsed.TotalSeconds;
        summary.Nodes = run.Build.Mesh.NNodes;
        summary.Shells = run.Build.Mesh.Shells.Count;
        summary.Beams = run.Build.Mesh.Beams.Count;
        summary.ControlNodeTag = controlDof != null ? controlTag! : "";
        summary.ControlDof = csfea.ControlDof;
        summary.Stages = adapted.Model.Stages.Select((s, i) =>
        {
            var total = i < adapted.StageTotals.Count ? adapted.StageTotals[i] : new FemRcStageTotal(s.Name, 0, 0, 0);
            return new FemCsfeaStageSummary(s.Name, s.Steps, total.Fx, total.Fy, total.Fz);
        }).ToList();
        summary.Steps = r.Steps.Select((s, i) => new FemCsfeaStepSummary(i + 1, s.Stage, s.Step, s.LoadFactor, s.IsRefinement,
            s.Converged, s.Iterations, s.TrueResidual, controlDof is int d ? s.U[d] : null)).ToList();
        return Result(analysis, created, r.Completed ? "ok" : "not_converged", summary);
    }

    static CalcResult Result(FemAnalysis analysis, string created, string status, FemCsfeaResultSummary summary) => new()
    {
        TaskId = 0, TaskKind = TaskKind, TaskTag = analysis.Tag, Created = created, Status = status, DataJson = summary.ToJson(),
    };

    /// <summary>
    /// Сетка схемы актуальна — ничего; устарела — строится и записывается (<paramref name="build"/>), иначе предупреждение.
    /// Изменившаяся сетка сбрасывает результаты постановок схемы, как команда «Построить сетку схемы».
    /// </summary>
    static async Task EnsureMeshAsync(FemCsfeaRunContext ctx, FemSchema schema, bool build, List<FemValidationDiagnostic> diag,
        CancellationToken ct)
    {
        var db = ctx.Db;
        var nodes = db.GetFemNodes(schema.Id);
        var members = db.GetFemMembers(schema.Id);
        double? barStep = db.GetFemSchemaMeshSteps(schema.Id).Bar;
        var service = new FemSchemaMeshService(db, ctx.Gmsh);
        var check = await service.CheckAsync(schema.Id, nodes, members, barStep, ct);
        if (check.HasErrors)
        {
            diag.AddRange(check.Diagnostics);
            return;
        }
        if (check.IsCurrent == true) return;
        if (!build)
        {
            diag.Add(new("mesh_stale", "Сетка схемы устарела — перед расчётом будет перестроена; вход проверен по сохранённой.", false));
            return;
        }

        ctx.Log.Info($"Сетка схемы «{schema.Tag}» устарела — строится заново.");
        var result = await service.BuildAsync(schema.Id, nodes, members, barStep, ctx.Log.Info, ct);
        if (result.Mesh is not { } mesh || result.HasErrors)
        {
            diag.AddRange(result.Diagnostics);
            if (!diag.Any(d => d.IsError)) diag.Add(new("mesh_build", "Сетка схемы не построена.", true));
            return;
        }
        diag.AddRange(result.Diagnostics);
        bool changed = !service.IsSameAsStored(schema.Id, mesh);
        service.Save(schema.Id, mesh);
        if (changed) service.ReportMeshChanged(schema, ctx.Log);
        ctx.Log.Info(string.Format(Loc.S("FemSchemaMeshDone"), mesh.Nodes.Count,
            mesh.Elements.Count(e => e.ElemType == "beam"), mesh.Elements.Count(e => e.ElemType == "shell"),
            result.RegionCount, result.RebuiltRegionCount, mesh.SharedNodeCount, mesh.SplitBeamCount));
    }

    /// <summary>
    /// Схема с вложением SCAD, из которого не перенесены нагрузки или ГУ (нет объектов с происхождением
    /// <see cref="ScadLoadTransfer.Origin"/>), — ошибка с подсказкой команды; расчёт данные схемы не меняет.
    /// </summary>
    static void CheckScadTransfer(DatabaseService db, int schemaId, FemRcModelInput input, List<FemValidationDiagnostic> diag)
    {
        if (!db.HasFemSchemaSourceFile(schemaId, FemSchemaSourceFileKind.ScadAnalysisModel)) return;
        string origin = ScadLoadTransfer.Origin;
        bool loads = input.LoadCases.Any(c => c.Origin == origin) || input.ElementLoads.Any(l => l.Origin == origin)
            || input.MeshNodeLoads.Any(l => l.Origin == origin);
        bool boundary = input.Supports.Any(s => s.Origin == origin) || input.Springs.Any(s => s.Origin == origin)
            || input.RigidBodies.Any(b => b.Origin == origin);
        if (!loads)
            diag.Add(new("scad_loads_not_transferred",
                $"Нагрузки из вложения SCAD не перенесены в схему — команда «{Loc.S("ScadLoadsTransfer")}».", true));
        if (!boundary)
            diag.Add(new("scad_boundary_not_transferred",
                $"Граничные условия SCAD не перенесены в схему — команда «{Loc.S("ScadBoundaryRefresh")}».", true));
    }

    /// <summary>Тег узла сетки контрольного узла: узел схемы (по <see cref="FemMeshNode.SourceNodeTag"/>), иначе узел сетки.</summary>
    static string? ControlMeshTag(FemRcModelInput input, string tag)
    {
        tag = tag.Trim();
        if (input.Nodes.Any(n => n.NodeTag == tag) && input.MeshNodes.FirstOrDefault(n => n.SourceNodeTag == tag) is { } bySource)
            return bySource.NodeTag;
        return input.MeshNodes.FirstOrDefault(n => n.NodeTag == tag)?.NodeTag;
    }
}
