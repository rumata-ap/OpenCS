using CScore;
using CScore.Fire.Entities;

namespace CScore.Fire;

/// <summary>Сборка <see cref="FireCheckResult"/> из результата предельного коэффициента.</summary>
internal static class FireRCheckResultBuilder
{
    public static FireCheckResult Build(
        LimitForceResult res,
        FireThermalResult thermal,
        int snapshotIndex,
        string method,
        double n,
        double mx,
        double my,
        FireSectionDef? fireDef,
        int? thermalResultId,
        IReadOnlyDictionary<string, object?>? extra = null)
    {
        int idx = snapshotIndex < 0 ? thermal.Snapshots.Length - 1 : snapshotIndex;
        double criticalTime = idx >= 0 && idx < thermal.TimesMin.Length
            ? thermal.TimesMin[idx]
            : thermal.TimesMin[^1];

        double nResult = 0, mxResult = 0, myResult = 0;
        if (res.StrainPlane is Kurvature sp)
        {
            // усилия пересчитываются вызывающим кодом при необходимости
            _ = sp;
        }

        var details = new Dictionary<string, object?>
        {
            ["method"] = method,
            ["thermal_result_id"] = thermalResultId,
            ["aggregate_type"] = thermal.AggregateType,
            ["snapshot_index"] = snapshotIndex,
            ["fire_duration_min"] = fireDef?.FireDurationMin ?? thermal.FireDurationMin,
            ["fire_curve"] = fireDef?.FireCurve ?? thermal.FireCurve,
            ["converged"] = res.Converged,
            ["iterations"] = res.Iterations,
            ["newton_iterations"] = res.NewtonIterations,
            ["factor"] = res.Factor,
            ["utilization"] = res.Utilization,
            ["governing"] = res.Governing,
            ["N_target"] = n,
            ["Mx_target"] = mx,
            ["My_target"] = my,
            ["N_limit"] = res.NLimit,
            ["Mx_limit"] = res.MxLimit,
            ["My_limit"] = res.MyLimit,
            ["N_result"] = nResult,
            ["Mx_result"] = mxResult,
            ["My_result"] = myResult,
            ["eps0"] = res.StrainPlane?.e0 ?? 0.0,
            ["ky"] = res.StrainPlane?.ky ?? 0.0,
            ["kz"] = res.StrainPlane?.kz ?? 0.0,
            ["eps_contour_min"] = res.EpsContourMin,
            ["eps_cu"] = res.EpsCu,
            ["eps_rebar_max"] = res.EpsRebarMax,
            ["eps_su"] = res.EpsSu,
            ["worst_governing"] = res.Governing,
            ["critical_time_min"] = criticalTime
        };

        if (extra != null)
        {
            foreach (var kv in extra)
                details[kv.Key] = kv.Value;
        }

        if (fireDef != null)
        {
            details["fire_section_id"] = fireDef.Id;
            details["fire_section_name"] = fireDef.Tag;
        }

        return new FireCheckResult
        {
            Criterion = "R",
            Passed = res.Factor >= 1.0,
            Margin = res.Factor - 1.0,
            CriticalTimeMin = criticalTime,
            Details = details
        };
    }

    public static void FillActualForces(FireCheckResult result, Load forces)
    {
        result.Details["N_result"] = forces.N;
        result.Details["Mx_result"] = forces.Mx;
        result.Details["My_result"] = forces.My;
    }
}
