using CScore;
using OpenCS.Utilites;

namespace OpenCS.Cli;

/// <summary>Списки объектов проекта, нужных для сборки: сечения, наборы усилий, материалы.</summary>
sealed partial class CliApp
{
    Task<int> ListSectionsAsync(CliCall call)
    {
        using var session = call.OpenProject();
        var ctx = session.Context;
        var rows = ctx.CrossSections.OrderBy(s => s.Num).ThenBy(s => s.Id).Select(s =>
        {
            var catalog = s.ParametricSteel?.Definition?.Catalog;
            return new
            {
                s.Id, s.Num, s.Tag,
                Profile = catalog == null ? null : $"{catalog.Name} ({catalog.Standard})",
                Materials = s.Areas.Select(a => a.MaterialId).Distinct()
                    .Select(id => ctx.Materials.FirstOrDefault(m => m.Id == id)?.Tag ?? $"#{id}").ToList()
            };
        }).ToList();

        if (call.Json) CliOutput.WriteJson(call.Out, rows);
        else
            CliOutput.WriteTable(call.Out,
                [Loc.S("CliColId"), Loc.S("CliColNum"), Loc.S("CliColTag"), Loc.S("CliColProfile"), Loc.S("CliColMaterials")],
                rows.Select(r => new[]
                {
                    r.Id.ToString(), r.Num.ToString(), r.Tag, r.Profile ?? Loc.S("CliNone"),
                    r.Materials.Count == 0 ? Loc.S("CliNone") : string.Join("; ", r.Materials)
                }));
        return Task.FromResult(CliExitCode.Ok);
    }

    Task<int> ListForceSetsAsync(CliCall call)
    {
        using var session = call.OpenProject();
        var rows = session.Context.ForceSets.OrderBy(f => f.Num).ThenBy(f => f.Id).Select(f => new
        {
            f.Id, f.Num, f.Tag, f.Kind,
            Rows = f.Kind == "shell" ? f.ShellItems.Count : f.Items.Count,
            Elements = ElementList.Format(f.Kind == "shell"
                ? f.ShellItems.Select(i => i.SourceElementNum).OfType<int>()
                : f.Items.Select(i => i.SourceElementNum).OfType<int>())
        }).ToList();

        if (call.Json) CliOutput.WriteJson(call.Out, rows);
        else
            CliOutput.WriteTable(call.Out,
                [Loc.S("CliColId"), Loc.S("CliColNum"), Loc.S("CliColTag"), Loc.S("CliColKind"), Loc.S("CliColRows"),
                 Loc.S("CliColElements")],
                rows.Select(r => new[]
                {
                    r.Id.ToString(), r.Num.ToString(), r.Tag, r.Kind, r.Rows.ToString(),
                    r.Elements.Length == 0 ? Loc.S("CliNone") : r.Elements
                }));
        return Task.FromResult(CliExitCode.Ok);
    }

    Task<int> ListMaterialsAsync(CliCall call)
    {
        using var session = call.OpenProject();
        var rows = session.Context.Materials.OrderBy(m => m.Id)
            .Select(m => new { m.Id, m.Tag, Type = m.Type.ToString() }).ToList();

        if (call.Json) CliOutput.WriteJson(call.Out, rows);
        else
            CliOutput.WriteTable(call.Out, [Loc.S("CliColId"), Loc.S("CliColTag"), Loc.S("CliColKind")],
                rows.Select(r => new[] { r.Id.ToString(), r.Tag, r.Type }));
        return Task.FromResult(CliExitCode.Ok);
    }
}
