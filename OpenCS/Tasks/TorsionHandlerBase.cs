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
            // Геометрия сечения: берём первую область. Контур в OpenCS — в мм; под капотом СИ → перевод в м.
            var area = section.Areas[0];
            var boundaryMm = area.FromMaterialArea();
            var boundaryM = ScaleToMeters(boundaryMm);

            double elemSizeM = p.ElementSize > 0 ? p.ElementSize : 0.05;
            var props = TorsionSolver.Solve(boundaryM, Method, elemSizeM);

            // It в м⁴; τ_unit в м². Для τ_max нужны G (Па) и Θ = Mk/(G·It).
            double tauMax = double.NaN, twistRate = double.NaN;
            if (p.GMPa > 0 && props.It > 0)
            {
                double gPa = p.GMPa * 1e6;
                double mk = p.MkKNm * 1e3; // Н·м
                twistRate = mk / (gPa * props.It);
                tauMax = gPa * twistRate * props.TauUnitMax;
            }

            var data = new
            {
                method = Method.ToString().ToLowerInvariant(),
                It_m4 = props.It,
                It_mm4 = props.It * 1e12,
                shear_center_x_m = props.ShearCenterX,
                shear_center_y_m = props.ShearCenterY,
                tau_unit_max = props.TauUnitMax,
                tau_unit_max_mm2 = props.TauUnitMax * 1e6,
                n_elements = props.NElements,
                singular = props.Singular,
                element_size_m = elemSizeM,
                twist_rate = twistRate,
                tau_max_Pa = tauMax,
                node_x = props.NodeX,
                node_y = props.NodeY,
                tau_unit = props.TauUnitField
            };
            return new CalcResult
            {
                TaskId = task.Id, TaskKind = task.Kind, TaskTag = task.Tag,
                Created = created, Status = props.Singular ? "not_converged" : "ok",
                DataJson = JsonSerializer.Serialize(data)
            };
        }
        catch (Exception ex)
        {
            return new CalcResult
            {
                TaskId = task.Id, TaskKind = task.Kind, TaskTag = task.Tag,
                Created = created, Status = "error",
                DataJson = JsonSerializer.Serialize(new { error = ex.Message })
            };
        }
    }

    /// <summary>Масштабирует контуры из мм в м (деление на 1000).</summary>
    private static TorsionBoundary ScaleToMeters(TorsionBoundary b)
    {
        double[] ox = b.OuterX.Select(v => v / 1000.0).ToArray();
        double[] oy = b.OuterY.Select(v => v / 1000.0).ToArray();
        List<(double[] X, double[] Y)>? holes = null;
        if (b.Holes != null)
        {
            holes = new List<(double[] X, double[] Y)>(b.Holes.Count);
            foreach (var h in b.Holes)
                holes.Add((h.X.Select(v => v / 1000.0).ToArray(), h.Y.Select(v => v / 1000.0).ToArray()));
        }
        return new TorsionBoundary(ox, oy, holes);
    }
}
