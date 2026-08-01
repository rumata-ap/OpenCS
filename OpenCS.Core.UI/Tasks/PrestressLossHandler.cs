using System;
using System.Text.Json;
using CScore;
using CScore.PrestressLoss;
using OpenCS.Utilites;

namespace OpenCS.Tasks;

public class PrestressLossHandler : ITaskHandler
{
    public string Kind => "prestress_loss";

    public CalcResult Run(CalcTask task, CrossSection section, LoadItem item,
                          CalcSettings settings, TaskRunContext? ctx = null)
    {
        var created = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
        try
        {
            section.ResolveAndBuildDiagramms(settings.Sp63DescEtaMin,
                pool: ctx?.Database?.Diagrams,
                rebarDifferentialDiagram: settings.RebarDifferentialDiagram);

            var p = JsonSerializer.Deserialize<PrestressLossParams>(task.ParamsJson)
                    ?? new PrestressLossParams();

            var lossResult = PrestressLossCalc.Compute(p, section);

            return new CalcResult
            {
                TaskId   = task.Id,
                TaskKind = task.Kind,
                TaskTag  = task.Tag,
                Created  = created,
                Status   = lossResult.Errors.Count > 0 ? "error" : "ok",
                DataJson = JsonSerializer.Serialize(lossResult)
            };
        }
        catch (Exception ex)
        {
            return new CalcResult
            {
                TaskId   = task.Id,
                TaskKind = task.Kind,
                TaskTag  = task.Tag,
                Created  = created,
                Status   = "error",
                DataJson = JsonSerializer.Serialize(new { error = ex.Message })
            };
        }
    }
}
