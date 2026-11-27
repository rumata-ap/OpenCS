using CScore;
using CScore.Fem;
using OpenCS.Services;
using OpenCS.Utilites;

namespace OpenCS.Cli;

sealed partial class CliApp
{
    /// <summary>Происхождение схемы для вывода: internal, lira, scad, fragment, submodel…</summary>
    static string SchemaSource(FemSchema s) => s.ParentSchemaId != null ? "fragment" : s.SourceType;

    /// <summary>Состояние сетки схемы: none (не строилась), current, stale; ошибка проверки — error с текстом.</summary>
    static async Task<(string State, string? Reason)> MeshState(HeadlessProjectContext ctx, FemSchema s, CancellationToken ct)
    {
        var db = ctx.Db;
        bool hasMesh = db.GetFemMeshElements(s.Id).Count > 0;
        var nodes = db.GetFemNodes(s.Id);
        var members = db.GetFemMembers(s.Id);
        if (members.Count == 0) return (hasMesh ? "current" : "none", null);
        FemSchemaMeshBuildResult check;
        try
        {
            check = await new FemSchemaMeshService(db, ctx.GmshSettings)
                .CheckAsync(s.Id, nodes, members, db.GetFemSchemaMeshSteps(s.Id).Bar, ct);
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            // Нет Gmsh или сбой разбиения — список схем всё равно выводится, причина — в столбце сетки.
            return ("error", e.Message.Split('\n')[0].Trim());
        }
        if (check.HasErrors) return ("error", check.Diagnostics.First(d => d.IsError).Message);
        return check.IsCurrent == true ? ("current", null) : (hasMesh ? "stale" : "none", null);
    }

    /// <summary><c>opencs list schemas</c>: МКЭ-схемы, происхождение, состав и состояние сетки.</summary>
    async Task<int> ListSchemasAsync(CliCall call)
    {
        using var session = call.OpenProject();
        var ctx = session.Context;
        var rows = new List<(int Id, string Tag, string Source, int Nodes, int Members, string Mesh, string? Reason, (int Nodes, int Bars, int Shells) Grid)>();
        var fragments = new Dictionary<int, (string Parent, string ParentState, string Masks)?>();
        foreach (var s in ctx.FemSchemas.OrderBy(s => s.Id))
        {
            if (FragmentInfo(ctx.Db, s) is { } info) fragments[s.Id] = info;
            var (state, reason) = await MeshState(ctx, s, CancellationToken.None);
            rows.Add((s.Id, s.Tag, SchemaSource(s), ctx.Db.GetFemNodes(s.Id).Count, ctx.Db.GetFemMembers(s.Id).Count, state, reason,
                ctx.Db.GetFemMeshSnapshotCounts(s.Id)));
        }
        if (call.Json)
            CliOutput.WriteJson(call.Out, rows.Select(r => new
            {
                r.Id, r.Tag, r.Source, r.Nodes, r.Members, Mesh = r.Mesh, MeshReason = r.Reason,
                MeshNodes = r.Grid.Nodes, MeshBars = r.Grid.Bars, MeshShells = r.Grid.Shells,
                Parent = fragments.GetValueOrDefault(r.Id)?.Parent, ParentState = fragments.GetValueOrDefault(r.Id)?.ParentState,
                Masks = fragments.GetValueOrDefault(r.Id)?.Masks,
            }).ToList());
        else
            CliOutput.WriteTable(call.Out,
                [Loc.S("CliColId"), Loc.S("CliColTag"), Loc.S("CliColSource"), Loc.S("CliColNodes"), Loc.S("CliColMembers"),
                 Loc.S("CliColMesh"), Loc.S("CliColFragment")],
                rows.Select(r => new[]
                {
                    r.Id.ToString(), r.Tag, r.Source, r.Nodes.ToString(), r.Members.ToString(),
                    Loc.S("CliMesh_" + r.Mesh) + (r.Reason is { } why ? $" ({why})" : "")
                        + (r.Grid.Nodes > 0 ? string.Format(Loc.S("CliMeshCounts"), r.Grid.Nodes, r.Grid.Bars + r.Grid.Shells) : ""),
                    fragments.GetValueOrDefault(r.Id) is { } f
                        ? string.Format(Loc.S("CliFragmentInfo"), f.Parent, Loc.S("CliParentState_" + f.ParentState),
                            f.Masks.Length > 0 ? f.Masks : Loc.S("StoreyFragmentMasksNotTaken"))
                        : "",
                }));
        return CliExitCode.Ok;
    }

    /// <summary><c>opencs run mesh</c>: сетка схемы (та же сборка, что «Построить сетку схемы»); актуальная — не трогается.</summary>
    Task<int> RunMeshAsync(CliCall call) => RunItemsAsync(call, "CliKindSchema", ctx =>
        ctx.FemSchemas.OrderBy(s => s.Id)
            .Select(s => new RunItem("mesh", "CliKindSchema", s.Id, s.Tag, () => BuildMesh(ctx, s))));

    static async Task<CalcResult?> BuildMesh(HeadlessProjectContext ctx, FemSchema schema)
    {
        var db = ctx.Db;
        var nodes = db.GetFemNodes(schema.Id);
        var members = db.GetFemMembers(schema.Id);
        double? barStep = db.GetFemSchemaMeshSteps(schema.Id).Bar;
        var service = new FemSchemaMeshService(db, ctx.GmshSettings);
        try
        {
            var check = await service.CheckAsync(schema.Id, nodes, members, barStep, CancellationToken.None);
            if (check.IsCurrent == true && !check.HasErrors)
            {
                ctx.LogService.Info(string.Format(Loc.S("CliMeshCurrent"), schema.Tag));
                return new CalcResult { Id = schema.Id, Status = "ok", TaskTag = schema.Tag, TaskKind = "fem_mesh" };
            }
            var result = await service.BuildAsync(schema.Id, nodes, members, barStep, ctx.LogService.Info, CancellationToken.None);
            foreach (var d in result.Diagnostics)
                if (d.IsError) ctx.LogService.Error($"{schema.Tag}: {d.Message}"); else ctx.LogService.Warning($"{schema.Tag}: {d.Message}");
            if (result.Mesh is not { } mesh || result.HasErrors)
            {
                ctx.LogService.Error(string.Format(Loc.S("CliMeshFailed"), schema.Tag));
                return null;
            }
            bool changed = result.Unlock != null || service.DiffersFromStored(schema.Id, mesh);
            if (changed) service.Save(schema.Id, mesh, result.Unlock);
            if (changed) service.ReportMeshChanged(schema, ctx.LogService);
            ctx.LogService.Info(string.Format(Loc.S("FemSchemaMeshDone"), mesh.Nodes.Count,
                mesh.Elements.Count(e => e.ElemType == "beam"), mesh.Elements.Count(e => e.ElemType == "shell"),
                result.RegionCount, result.RebuiltRegionCount, mesh.SharedNodeCount, mesh.SplitBeamCount));
            return new CalcResult { Id = schema.Id, Status = "ok", TaskTag = schema.Tag, TaskKind = "fem_mesh" };
        }
        catch (Exception e) when (e is IOException or InvalidDataException or TimeoutException or InvalidOperationException)
        {
            ctx.LogService.Error($"{schema.Tag}: {e.Message}");
            return null;
        }
    }
}
