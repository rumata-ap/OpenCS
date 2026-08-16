using System;
using System.Linq;
using System.Text.Json;
using CScore;
using OpenCS.Utilites;

namespace OpenCS.Tasks;

/// <summary>
/// Обработчик задачи «Полная кривизна» на одну строку усилий по п. 8.2.23–8.2.32 СП 63.13330.
/// </summary>
public sealed class TotalCurvatureHandler : ITaskHandler
{
    /// <inheritdoc/>
    public string Kind => "total_curvature";

    /// <inheritdoc/>
    public CalcResult Run(CalcTask task, CrossSection section, LoadItem item,
        CalcSettings settings, TaskRunContext? ctx = null)
    {
        var created = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
        try
        {
            section.ResolveAndBuildDiagramms(settings.Sp63DescEtaMin,
                pool: ctx?.Database?.Diagrams,
                rebarDifferentialDiagram: settings.RebarDifferentialDiagram);

            var parameters = TotalCurvatureTaskParams.Parse(task.ParamsJson);
            double nTotal = item.N;
            double mxTotal = item.Mx;
            double myTotal = item.My;
            var (mxLong, myLong) = ResolveLongMoments(parameters, mxTotal, myTotal);

            var calcCrc = task.CalcType is CalcType.N or CalcType.NL
                ? task.CalcType
                : CalcType.N;
            var solver = new TotalCurvatureSolver(section, calcCrc: calcCrc,
                solverTol: settings.NewtonTolerance,
                solverMaxIter: settings.NewtonMaxIter,
                solverH: settings.NewtonDeltaH,
                centralJacobian: settings.NewtonJacobian == "central");
            var result = solver.Compute(nTotal, mxLong, myLong, mxTotal, myTotal);

            return new CalcResult
            {
                TaskId = task.Id,
                TaskKind = task.Kind,
                TaskTag = task.Tag,
                Created = created,
                Status = result.AllConverged ? "ok" : "error",
                DataJson = JsonSerializer.Serialize(
                    TotalCurvatureJson.Build(nTotal, mxLong, myLong, mxTotal, myTotal, result))
            };
        }
        catch (Exception ex)
        {
            return new CalcResult
            {
                TaskId = task.Id,
                TaskKind = task.Kind,
                TaskTag = task.Tag,
                Created = created,
                Status = "error",
                DataJson = JsonSerializer.Serialize(new { error = ex.Message })
            };
        }
    }

    internal static (double Mx, double My) ResolveLongMoments(
        TotalCurvatureTaskParams parameters, double mxTotal, double myTotal)
        => parameters.ForcesMode switch
        {
            "share" => (mxTotal * parameters.LongShare, myTotal * parameters.LongShare),
            "manual" => (parameters.MxLongManual ?? 0.0, parameters.MyLongManual ?? 0.0),
            _ => (mxTotal, myTotal)
        };
}

/// <summary>Общий JSON-контракт результата задачи полной кривизны.</summary>
internal static class TotalCurvatureJson
{
    public static object Build(double n, double mxLong, double myLong,
        double mxTotal, double myTotal, TotalCurvatureResult result) => new
    {
        N = Math.Round(n, 4),
        Mx_long = Math.Round(mxLong, 4),
        My_long = Math.Round(myLong, 4),
        Mx_total = Math.Round(mxTotal, 4),
        My_total = Math.Round(myTotal, 4),
        cracked = result.Cracked,
        Mcrc = Math.Round(result.Mcrc, 4),
        Mx_crc = Math.Round(result.MxCrc, 4),
        My_crc = Math.Round(result.MyCrc, 4),
        crc_converged = result.CrcConverged,
        stage1 = Stage(result.Stage1),
        stage2 = Stage(result.Stage2),
        stage3 = Stage(result.Stage3),
        ky_full = Math.Round(result.KyFull, 8),
        kz_full = Math.Round(result.KzFull, 8),
        k_full = Math.Round(result.KFull, 8),
        all_converged = result.AllConverged
    };

    static object? Stage(CurvatureStageResult? stage) => stage == null ? null : new
    {
        Mx = Math.Round(stage.Mx, 4),
        My = Math.Round(stage.My, 4),
        e0 = Math.Round(stage.Plane.e0, 10),
        ky = Math.Round(stage.Plane.ky, 8),
        kz = Math.Round(stage.Plane.kz, 8),
        calc_type = stage.CalcType.ToString(),
        concrete_tension = stage.ConcreteTension,
        psi_s_by_rebar = stage.PsiSByRebar.Select(rebar => new
        {
            num = rebar.Num,
            x = Math.Round(rebar.X, 8),
            y = Math.Round(rebar.Y, 8),
            psi_s = Math.Round(rebar.PsiS, 3),
            applicable = rebar.Applicable
        }),
        converged = stage.Converged
    };
}
