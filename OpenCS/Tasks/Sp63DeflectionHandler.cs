using System.Text.Json;
using System.Text.Json.Serialization;
using CScore;
using CScore.Sp63.Deflection;
using OpenCS.Utilites;

namespace OpenCS.Tasks;

/// <summary>Запускает отдельную формульную задачу прогиба по СП 63.</summary>
public sealed class Sp63DeflectionHandler : ITaskHandler
{
    static readonly JsonSerializerOptions JsonOptions = new()
    {
        NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals
    };

    /// <inheritdoc/>
    public string Kind => "sp63_deflection";

    /// <inheritdoc/>
    public CalcResult Run(CalcTask task, CrossSection section, LoadItem item,
        CalcSettings settings, TaskRunContext? ctx = null)
    {
        string created = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
        try
        {
            var parameters = Sp63DeflectionTaskParams.Parse(task.ParamsJson);
            if (!parameters.TryToOptions(out var options, out var errorCode))
            {
                var invalid = Invalid(errorCode);
                return MakeResult(task, created, invalid);
            }

            LoadItem full = parameters.UseManualForces
                ? new LoadItem { N = parameters.N ?? 0, Mx = parameters.Mx ?? 0, My = parameters.My ?? 0 }
                : item;
            LoadItem longitudinal = options.ForcesMode switch
            {
                Sp63DeflectionForcesMode.TotalOnly => Copy(full),
                Sp63DeflectionForcesMode.Share => Scale(full, options.LongTermShare),
                Sp63DeflectionForcesMode.Manual => new LoadItem
                {
                    N = options.ManualLongN!.Value,
                    Mx = options.ManualLongMx!.Value,
                    My = options.ManualLongMy!.Value
                },
                _ => new LoadItem()
            };

            var domain = Sp63DeflectionChecker.Check(section, full, longitudinal, task.CalcType, options);
            domain.Variables ??= [];
            domain.Variables["N"] = full.N;
            domain.Variables["M"] = options.Axis == CScore.Sp63.Normal.Sp63NormalAxis.Mx ? full.Mx : full.My;
            domain.Variables["Nl"] = longitudinal.N;
            domain.Variables["Ml"] = options.Axis == CScore.Sp63.Normal.Sp63NormalAxis.Mx
                ? longitudinal.Mx : longitudinal.My;
            domain.Variables["axis"] = options.Axis == CScore.Sp63.Normal.Sp63NormalAxis.Mx ? 0.0 : 1.0;
            return MakeResult(task, created, domain);
        }
        catch (Exception ex)
        {
            var invalid = Invalid("handler_exception");
            invalid.InformationalMessages = [new("handler_exception_detail",
                Sp63DeflectionMessageKind.Information, "8.2.21", ex.Message)];
            return MakeResult(task, created, invalid);
        }
    }

    static CalcResult MakeResult(CalcTask task, string created, Sp63DeflectionResult domain) => new()
    {
        TaskId = task.Id,
        TaskKind = task.Kind,
        TaskTag = task.Tag,
        Created = created,
        Status = domain.Status switch
        {
            Sp63DeflectionStatus.Calculated when domain.DeflectionPassed == true => "ok",
            Sp63DeflectionStatus.Calculated when domain.DeflectionPassed == false => "not_passed",
            Sp63DeflectionStatus.NotApplicable => "not_applicable",
            _ => "error"
        },
        DataJson = JsonSerializer.Serialize(domain, JsonOptions)
    };

    static Sp63DeflectionResult Invalid(string code) => new()
    {
        Status = Sp63DeflectionStatus.InvalidInput,
        Branch = "invalid_input",
        ApplicabilityMessages = [new(code, Sp63DeflectionMessageKind.Applicability,
            "8.2.21", "Sp63Deflection_InvalidInput")]
    };

    static LoadItem Copy(LoadItem item) => new() { N = item.N, Mx = item.Mx, My = item.My };
    static LoadItem Scale(LoadItem item, double share) =>
        new() { N = share * item.N, Mx = share * item.Mx, My = share * item.My };
}
