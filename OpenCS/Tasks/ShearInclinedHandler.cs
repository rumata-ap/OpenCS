using CScore;
using OpenCS.Utilites;

namespace OpenCS.Tasks;

/// <summary>Расчёт наклонных сечений по СП 63.13330 для одной строки усилий.</summary>
public class ShearInclinedHandler : ITaskHandler
{
    /// <summary>Идентификатор вида задачи.</summary>
    public string Kind => "shear_inclined";

    /// <summary>Выполняет расчёт; ошибки кодируются статусом результата.</summary>
    public CalcResult Run(
        CalcTask task, CrossSection section, LoadItem item,
        CalcSettings settings, TaskRunContext? ctx = null) =>
        ShearInclinedRunner.Run(task, section, item, settings, ctx);
}
