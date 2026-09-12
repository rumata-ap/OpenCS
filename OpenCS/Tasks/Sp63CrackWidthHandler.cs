using System.Text.Json;
using System.Text.Json.Serialization;
using CScore;
using CScore.Sp63.CrackWidth;
using OpenCS.Utilites;

namespace OpenCS.Tasks;

/// <summary>Запускает упрощённую формульную проверку ширины раскрытия трещин СП 63.</summary>
public sealed class Sp63CrackWidthHandler : ITaskHandler
{
    static readonly JsonSerializerOptions JsonOptions = new()
    {
        NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals
    };

    /// <inheritdoc/>
    public string Kind => "sp63_crack_width";

    /// <inheritdoc/>
    public CalcResult Run(CalcTask task, CrossSection section, LoadItem item,
        CalcSettings settings, TaskRunContext? ctx = null)
    {
        string created = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
        try
        {
            var parameters = Sp63CrackWidthTaskParams.Parse(task.ParamsJson);
            if (!parameters.TryToOptions(out var options, out var errorCode))
            {
                var invalid = new Sp63CrackWidthResult
                {
                    Status = Sp63CrackWidthStatus.InvalidInput,
                    Branch = "invalid_input",
                    ApplicabilityMessages =
                    [new Sp63CrackWidthMessage(
                        errorCode,
                        Sp63CrackWidthMessageKind.Applicability,
                        "8.2",
                        "Sp63CrackWidth_InvalidInput")]
                };
                return MakeResult(task, created, invalid);
            }

            var domain = Sp63CrackWidthChecker.Check(section, item, task.CalcType, options);
            return MakeResult(task, created, domain);
        }
        catch (Exception ex)
        {
            var error = new Sp63CrackWidthResult
            {
                Status = Sp63CrackWidthStatus.InvalidInput,
                Branch = "handler_exception",
                ApplicabilityMessages =
                [new Sp63CrackWidthMessage(
                    "handler_exception",
                    Sp63CrackWidthMessageKind.Applicability,
                    "8.2",
                    "Sp63CrackWidth_HandlerError")],
                InformationalMessages =
                [new Sp63CrackWidthMessage(
                    "handler_exception_detail",
                    Sp63CrackWidthMessageKind.Information,
                    "справочно",
                    ex.Message)]
            };
            return MakeResult(task, created, error);
        }
    }

    static CalcResult MakeResult(CalcTask task, string created,
        Sp63CrackWidthResult domain) => new()
        {
            TaskId = task.Id,
            TaskKind = task.Kind,
            TaskTag = task.Tag,
            Created = created,
            Status = Sp63CrackWidthTaskStatusMapper.ToCalcResultStatus(domain),
            DataJson = JsonSerializer.Serialize(domain, JsonOptions)
        };
}
