using System.Text.Json;
using System.Text.Json.Serialization;
using CScore;
using CScore.Sp63.Normal;
using OpenCS.Utilites;

namespace OpenCS.Tasks;

/// <summary>Запускает упрощённую формульную проверку нормального сечения СП 63.</summary>
public sealed class Sp63NormalHandler : ITaskHandler
{
    static readonly JsonSerializerOptions JsonOptions = new()
    {
        NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals
    };

    /// <inheritdoc/>
    public string Kind => "sp63_normal";

    /// <inheritdoc/>
    public CalcResult Run(CalcTask task, CrossSection section, LoadItem item,
        CalcSettings settings, TaskRunContext? ctx = null)
    {
        string created = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
        try
        {
            var parameters = Sp63NormalTaskParams.Parse(task.ParamsJson);
            if (!parameters.TryToOptions(out var options, out var errorCode))
            {
                var invalid = new Sp63NormalResult
                {
                    Status = Sp63NormalStatus.InvalidInput,
                    Branch = "invalid_input",
                    ApplicabilityMessages =
                    [new Sp63NormalMessage(
                        errorCode,
                        Sp63NormalMessageKind.Applicability,
                        "8.1",
                        "Sp63Normal_InvalidInput")]
                };
                return MakeResult(task, created, invalid);
            }

            var domain = Sp63NormalChecker.Check(section, item, task.CalcType, options);
            return MakeResult(task, created, domain);
        }
        catch (Exception ex)
        {
            var error = new Sp63NormalResult
            {
                Status = Sp63NormalStatus.InvalidInput,
                Branch = "handler_exception",
                ApplicabilityMessages =
                [new Sp63NormalMessage(
                    "handler_exception",
                    Sp63NormalMessageKind.Applicability,
                    "8.1",
                    "Sp63Normal_HandlerError")],
                InformationalMessages =
                [new Sp63NormalMessage(
                    "handler_exception_detail",
                    Sp63NormalMessageKind.Information,
                    "справочно",
                    ex.Message)]
            };
            return MakeResult(task, created, error);
        }
    }

    static CalcResult MakeResult(CalcTask task, string created,
        Sp63NormalResult domain) => new()
        {
            TaskId = task.Id,
            TaskKind = task.Kind,
            TaskTag = task.Tag,
            Created = created,
            Status = Sp63NormalTaskStatusMapper.ToCalcResultStatus(domain),
            DataJson = JsonSerializer.Serialize(domain, JsonOptions)
        };
}
