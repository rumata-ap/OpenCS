using System;
using System.Text.Json;
using CScore;
using OpenCS.Utilites;

namespace OpenCS.Tasks;

/// <summary>
/// Обработчик нормативной проверки «rc_check» стержня схемы МКЭ: прочность по НДМ
/// (п. 8.1.24, 8.1.30 СП 63.13330) для одной строки усилий. Коэффициент использования —
/// наибольшее из отношений ε_b/ε_b,ult и ε_s/ε_s,ult. Несошедшийся НДС — проверка
/// не пройдена (сечение не воспринимает усилия), коэффициент не определён.
/// </summary>
public sealed class RcCheckHandler : ITaskHandler
{
    public string Kind => "rc_check";

    public CalcResult Run(CalcTask task, CrossSection section, LoadItem item,
                          CalcSettings settings, TaskRunContext? ctx = null)
    {
        var created = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
        try
        {
            section.ResolveAndBuildDiagramms(settings.Sp63DescEtaMin,
                pool: ctx?.Database?.Diagrams,
                rebarDifferentialDiagram: settings.RebarDifferentialDiagram, ekbEtaMin: settings.EkbDescEtaMin);
            bool ten = settings.ResolveConcreteTension(task.CalcType);

            var r = StrengthNDMBatchHandler.Evaluate(section, item, task.CalcType, ten, settings);

            string dataJson;
            string status;
            if (!r.Converged)
            {
                status = "not_converged";
                dataJson = JsonSerializer.Serialize(new
                {
                    passed = false,
                    reason = $"НДС не найден за {r.Iterations} ит. (невязка {r.Residual:G3}): "
                           + "сечение не воспринимает заданные усилия",
                    iterations = r.Iterations,
                    residual = Math.Round(r.Residual, 6)
                });
            }
            else
            {
                double utilization = Math.Max(r.ConcreteRatio, r.RebarRatio);
                status = r.StrengthOk ? "ok" : "not_passed";
                dataJson = JsonSerializer.Serialize(new
                {
                    utilization = Math.Round(utilization, 6),
                    passed = r.StrengthOk,
                    e0 = Math.Round(r.Strain.e0, 8),
                    ky = Math.Round(r.Strain.ky, 8),
                    kz = Math.Round(r.Strain.kz, 8),
                    eps_concrete_compression = Math.Round(r.EpsConcreteCompression, 8),
                    eps_concrete_ult = Math.Round(r.EpsConcreteUlt, 8),
                    eps_rebar_tension = Math.Round(r.EpsRebarTension, 8),
                    eps_rebar_ult = Math.Round(r.EpsRebarUlt, 8),
                    iterations = r.Iterations,
                    details = new[]
                    {
                        new
                        {
                            formula = "п. 8.1.30",
                            description = $"бетон: ε_b = {r.EpsConcreteCompression:F5} / ε_b,ult = {r.EpsConcreteUlt:F5}",
                            ratio = Math.Round(r.ConcreteRatio, 6),
                            passed = r.ConcreteOk
                        },
                        new
                        {
                            formula = "п. 8.1.30",
                            description = $"арматура: ε_s = {r.EpsRebarTension:F5} / ε_s,ult = {r.EpsRebarUlt:F5}",
                            ratio = Math.Round(r.RebarRatio, 6),
                            passed = r.RebarOk
                        }
                    }
                });
            }

            return new CalcResult
            {
                TaskId = task.Id, TaskKind = task.Kind, TaskTag = task.Tag,
                Created = created, Status = status, DataJson = dataJson
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
}
