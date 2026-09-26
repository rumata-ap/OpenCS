using System.Text.Json;
using CScore;
using CScore.Sp16;
using OpenCS.Utilites;

namespace OpenCS.Tasks;

/// <summary>Адаптер задач СП 16 к ядру с сохранением отверстий, осей и единиц кН/кПа.</summary>
public abstract class SteelTaskHandlerBase : ITaskHandler
{
    public abstract string Kind { get; }

    public CalcResult Run(CalcTask task, CrossSection section, LoadItem item,
        CalcSettings settings, TaskRunContext? ctx = null)
    {
        var created = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
        CalcResult Error(string error) => new()
        {
            TaskId = task.Id, TaskKind = task.Kind, TaskTag = task.Tag, Created = created,
            Status = "error", DataJson = JsonSerializer.Serialize(new { error })
        };
        try
        {
            var p = SteelDesignParams.Parse(task.ParamsJson);
            // Составные сечения вне объёма: нельзя молча считать только первую область.
            if (section.Areas.Count != 1 || section.Areas[0].Material?.Type != MatType.Steel)
                return Error(Loc.S("Sp16SingleSteelAreaRequired"));
            var area = section.Areas[0];
            if (area.Hull?.Points is not { Count: >= 3 }) return Error(Loc.S("Sp16ContourRequired"));
            var mat = SteelMaterialProps.FromMaterial(area.Material!);
            if (!double.IsFinite(mat.Ry) || mat.Ry <= 0 || !double.IsFinite(mat.E) || mat.E <= 0
                || !double.IsFinite(p.GammaC) || p.GammaC <= 0
                || !double.IsFinite(p.LefX) || p.LefX < 0 || !double.IsFinite(p.LefY) || p.LefY < 0)
                return Error(Loc.S("Sp16InvalidParameters"));
            var polygon = new PolygonSection(area.Hull.Points.Select(pt => (pt.X, pt.Y)),
                area.Holes.Select(h => h.Points.Select(pt => (pt.X, pt.Y))));
            var member = Sp16Member.Create(polygon, mat, p);
            var f = p.ManualForces?.ToForces() ?? (item == null ? null
                : new SteelForces(item.N, item.Mx, item.My, item.Vx, item.Vy));
            if (f == null) return Error(Loc.S("CalcTaskForceItemNotFound"));
            var report = Sp16Checker.Run(member, f, Sp16Checker.ParseKind(Kind)!.Value);
            if (report.Error != null) return Error(Loc.S(report.Error));
            double torsion = p.ManualForces?.Mz ?? item?.T ?? 0;
            if (!double.IsFinite(torsion)) return Error(Loc.S("Sp16InvalidForces"));
            if (torsion != 0) report.Notes.Add(Loc.S("Sp16TorsionIgnored"));
            bool any = report.Results.Any(r => r.Status != CheckStatus.NotApplicable);
            var accepted = report.Forces;
            var data = new
            {
                schemaVersion = 2, utilization = Finite(report.Utilization), passed = report.Passed,
                sectionTag = section.Tag, steelTag = area.Material!.Tag,
                requestedKind = Kind, effectiveKind = Sp16Checker.KindCode(report.Kind), notes = report.Notes,
                context = new { lefX = p.LefX, lefY = p.LefY, lefB = p.LefBOrY, gammaC = p.GammaC },
                forces = new { name = item?.Label ?? "", n = accepted.N, mx = accepted.Mx, my = accepted.My,
                    qx = accepted.Qx, qy = accepted.Qy, t = torsion },
                details = report.Results.Select(r => new
                {
                    clause = r.Clause, formula = r.Formula, description = r.Description,
                    normRef = $"СП 16.13330.2017, {r.Clause}", category = Category(r.Clause),
                    applied = Finite(r.Applied), allowable = Finite(r.Allowable), ratio = Finite(r.Utilization),
                    passed = r.Status == CheckStatus.Ok, status = r.Status.ToString(), notes = r.Notes,
                    variables = r.Variables.GroupBy(v => v.Key).ToDictionary(g => g.Key, g => Finite(g.Last().Value))
                }).ToArray()
            };
            return new CalcResult
            {
                TaskId = task.Id, TaskKind = task.Kind, TaskTag = task.Tag, Created = created,
                Status = !any ? "not_applicable" : report.Passed ? "ok" : "not_passed",
                DataJson = JsonSerializer.Serialize(data)
            };
        }
        catch (Exception ex) { return Error(ex.Message); }
    }

    static double? Finite(double v) => double.IsFinite(v) ? v : null;
    static string Category(string clause) => clause.StartsWith("10.") ? "constructive"
        : clause.StartsWith("7.1.1") || clause.StartsWith("8.2") || clause.StartsWith("9.1") ? "strength" : "stability";
}
