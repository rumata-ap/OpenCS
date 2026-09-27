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
            // Custom — пользовательский материал с Ry/Ru (как и в диалоге задачи); Ry проверяется ниже.
            if (section.Areas.Count != 1 || section.Areas[0].Material?.Type is not (MatType.Steel or MatType.Custom))
                return Error(Loc.S("Sp16SingleSteelAreaRequired"));
            var area = section.Areas[0];
            if (area.Hull?.Points is not { Count: >= 3 }) return Error(Loc.S("Sp16ContourRequired"));
            var mat = SteelMaterialProps.FromMaterial(area.Material!);
            if (!double.IsFinite(mat.Ry) || mat.Ry <= 0 || !double.IsFinite(mat.E) || mat.E <= 0
                || !double.IsFinite(p.GammaC) || p.GammaC <= 0
                // lef = 0 даёт λ = 0, φ = 1 — устойчивость и гибкость «проходили» бы при любой нагрузке.
                || !double.IsFinite(p.LefX) || p.LefX <= 0 || !double.IsFinite(p.LefY) || p.LefY <= 0
                || !double.IsFinite(p.LefB) || p.LefB < 0)
                return Error(Loc.S("Sp16InvalidParameters"));
            var member = GetMember(task.ParamsJson, area, mat, p);
            var f = p.ManualForces?.ToForces() ?? new SteelForces(item.N, item.Mx, item.My, item.Vx, item.Vy);
            var report = Sp16Checker.Run(member, f, Sp16Checker.ParseKind(Kind)!.Value);
            if (report.Error == "Sp16KindMismatch")
                return Error(string.Format(Loc.S("Sp16KindMismatch"), Loc.S("CalcTaskKind_" + Sp16Checker.KindCode(report.Kind)),
                    Loc.S("Sp16Mismatch_" + report.ErrorReason), Loc.S("CalcTaskKind_steel_check")));
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
                    normRef = string.Format(Loc.S("Sp16NormRef"), r.Clause), category = Category(r.Clause),
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

    /// <summary>Последний построенный элемент потока: FEM вызывает хендлер на каждую строку усилий одной задачи.</summary>
    [ThreadStatic] static (string Key, Sp16Member Member)? _lastMember;

    /// <summary>
    /// Элемент СП 16 (распознавание профиля и геометрия) — с кэшем по параметрам, контуру и материалу:
    /// для одной задачи и сечения он не зависит от строки усилий.
    /// </summary>
    static Sp16Member GetMember(string? paramsJson, MaterialArea area, SteelMaterialProps mat, SteelDesignParams p)
    {
        var key = new System.Text.StringBuilder(paramsJson ?? "").Append('|').Append(mat);
        foreach (var pt in area.Hull!.Points) key.Append('|').Append(pt.X.ToString("R")).Append(',').Append(pt.Y.ToString("R"));
        foreach (var h in area.Holes)
        {
            key.Append("|h");
            foreach (var pt in h.Points) key.Append('|').Append(pt.X.ToString("R")).Append(',').Append(pt.Y.ToString("R"));
        }
        string k = key.ToString();
        if (_lastMember is { } last && last.Key == k) return last.Member;
        var polygon = new PolygonSection(area.Hull.Points.Select(pt => (pt.X, pt.Y)),
            area.Holes.Select(h => h.Points.Select(pt => (pt.X, pt.Y))));
        var member = Sp16Member.Create(polygon, mat, p);
        _lastMember = (k, member);
        return member;
    }

    static double? Finite(double v) => double.IsFinite(v) ? v : null;
    static string Category(string clause) => clause.StartsWith("10.") ? "constructive"
        : clause.StartsWith("7.1.1") || clause.StartsWith("8.2") || clause.StartsWith("9.1") ? "strength" : "stability";
}
