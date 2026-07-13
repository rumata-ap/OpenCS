using System.Text.Json;
using CScore;
using CSfea.Torsion;
using OpenCS.Utilites;

namespace OpenCS.Tasks;

/// <summary>Базовый обработчик задачи кручения Сен-Венана.</summary>
public abstract class TorsionHandlerBase : ITaskHandler
{
    public abstract string Kind { get; }
    protected abstract TorsionMethod Method { get; }

    public CalcResult Run(CalcTask task, CrossSection section, LoadItem item, CalcSettings settings)
        => Run(task, section, item, settings, null);

    public CalcResult Run(CalcTask task, CrossSection section, LoadItem item, CalcSettings settings, TaskRunContext? ctx)
    {
        var created = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
        try
        {
            var p = TorsionParams.Parse(task.ParamsJson);
            var femOrder = string.Equals(p.FemOrder, "quadratic", StringComparison.OrdinalIgnoreCase)
                ? FemElementOrder.Quadratic
                : FemElementOrder.Linear;
            var area = section.Areas[0];
            var boundary = area.FromMaterialArea();

            TorsionProps props;
            TorsionAutoConvergeResult? autoConverge = null;
            double elemSizeM;
            if (p.AutoConverge)
            {
                double? h0 = p.AutoH0 > 0 ? p.AutoH0 : null;
                int nRuns = p.AutoRuns >= 2 ? p.AutoRuns : 3;
                autoConverge = TorsionRichardson.SolveAutoConverge(
                    boundary, Method, p.Triangulation, femOrder, h0, nRuns);
                props = autoConverge.ToTorsionProps();
                elemSizeM = autoConverge.Steps[^1].ElementSize;
            }
            else
            {
                elemSizeM = p.ElementSize > 0 ? p.ElementSize : 0.05;
                props = TorsionSolver.Solve(boundary, Method, elemSizeM, p.Triangulation, femOrder);
            }

            var baseMat = TorsionMaterialHelper.ResolveBaseMaterial(section);
            double gMpa = TorsionMaterialHelper.ShearModulusMpa(baseMat);
            double mkKNm = ResolveMk(p, item);

            double tauMax = double.NaN, twistRate = double.NaN;
            if (gMpa > 0 && props.It > 0 && mkKNm > 0)
            {
                double gPa = gMpa * 1e6;
                double mk = mkKNm * 1e3;
                twistRate = mk / (gPa * props.It);
                tauMax = gPa * twistRate * props.TauUnitMax;
            }

            var holesX = boundary.Holes?.Select(h => h.X.Select(v => v * 1000.0).ToArray()).ToList();
            var holesY = boundary.Holes?.Select(h => h.Y.Select(v => v * 1000.0).ToArray()).ToList();

            var data = new
            {
                method = Method.ToString().ToLowerInvariant(),
                fem_order = p.FemOrder,
                It_m4 = props.It,
                It_mm4 = props.It * 1e12,
                shear_center_x_m = TorsionJsonHelper.Finite(props.ShearCenterX),
                shear_center_y_m = TorsionJsonHelper.Finite(props.ShearCenterY),
                tau_unit_max = props.TauUnitMax,
                tau_unit_max_mm2 = props.TauUnitMax * 1e6,
                n_elements = props.NElements,
                singular = props.Singular,
                element_size_m = elemSizeM,
                twist_rate = TorsionJsonHelper.Finite(twistRate),
                tau_max_Pa = TorsionJsonHelper.Finite(tauMax),
                g_mpa = gMpa,
                e_mpa = baseMat?.E ?? 0,
                mk_knm = mkKNm,
                mk_from_force_set = Math.Abs(item.T) > 1e-12,
                node_x = props.NodeX,
                node_y = props.NodeY,
                tau_unit = TorsionJsonHelper.FiniteArray(props.TauUnitField),
                potential = TorsionJsonHelper.FiniteArray(props.PotentialField),
                triangles = props.Triangles,
                boundary_x = props.BoundaryX,
                boundary_y = props.BoundaryY,
                boundary_j1 = props.BoundaryJ1,
                outer_x_mm = boundary.OuterX.Select(v => v * 1000.0).ToArray(),
                outer_y_mm = boundary.OuterY.Select(v => v * 1000.0).ToArray(),
                holes_x_mm = holesX,
                holes_y_mm = holesY,
                auto_converge = p.AutoConverge,
                convergence_h_mm = autoConverge?.Steps.Select(s => s.ElementSize * 1000.0).ToArray(),
                convergence_it_mm4 = autoConverge?.Steps.Select(s => s.Props.It * 1e12).ToArray(),
                it_order = autoConverge?.ItOrder,
                it_extrapolated = autoConverge?.ItExtrapolated,
                shear_center_order_x = autoConverge?.ShearCenterXOrder,
                shear_center_order_y = autoConverge?.ShearCenterYOrder,
                shear_center_extrapolated = autoConverge?.ShearCenterExtrapolated
            };
            return new CalcResult
            {
                TaskId = task.Id, TaskKind = task.Kind, TaskTag = task.Tag,
                Created = created, Status = props.Singular ? "not_converged" : "ok",
                DataJson = TorsionJsonHelper.Serialize(data)
            };
        }
        catch (Exception ex)
        {
            return new CalcResult
            {
                TaskId = task.Id, TaskKind = task.Kind, TaskTag = task.Tag,
                Created = created, Status = "error",
                DataJson = TorsionJsonHelper.Serialize(new { error = ex.Message })
            };
        }
    }

    /// <summary>Mk: T из строки набора усилий, иначе ручное значение из ParamsJson.</summary>
    static double ResolveMk(TorsionParams p, LoadItem item)
    {
        if (Math.Abs(item.T) > 1e-12) return Math.Abs(item.T);
        return p.MkKNm > 0 ? p.MkKNm : 0;
    }
}
