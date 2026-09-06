using System.Text.Json;
using System.Text.Json.Serialization;
using CScore;

namespace OpenCS.Reporting;

/// <summary>Типизированный контракт результата расчёта предельных усилий limit_*.</summary>
public sealed class LimitForceReportData
{
    /// <summary>Метод решателя.</summary>
    [JsonPropertyName("solver_method")] public string SolverMethod { get; set; } = "";
    /// <summary>Признак сходимости.</summary>
    [JsonPropertyName("converged")] public bool Converged { get; set; }
    /// <summary>Общее число итераций.</summary>
    [JsonPropertyName("iterations")] public int Iterations { get; set; }
    /// <summary>Число итераций Ньютона.</summary>
    [JsonPropertyName("newton_iterations")] public int NewtonIterations { get; set; }
    /// <summary>Коэффициент запаса.</summary>
    [JsonPropertyName("factor")] public double Factor { get; set; }
    /// <summary>Коэффициент использования.</summary>
    [JsonPropertyName("utilization")] public double Utilization { get; set; }
    /// <summary>Определяющий критерий.</summary>
    [JsonPropertyName("governing")] public string Governing { get; set; } = "";

    /// <summary>Целевая продольная сила N, кН.</summary>
    [JsonPropertyName("N_target")] public double TargetN { get; set; }
    /// <summary>Целевой момент Mx, кН·м.</summary>
    [JsonPropertyName("Mx_target")] public double TargetMx { get; set; }
    /// <summary>Целевой момент My, кН·м.</summary>
    [JsonPropertyName("My_target")] public double TargetMy { get; set; }
    /// <summary>Предельная продольная сила N, кН.</summary>
    [JsonPropertyName("N_limit")] public double LimitN { get; set; }
    /// <summary>Предельный момент Mx, кН·м.</summary>
    [JsonPropertyName("Mx_limit")] public double LimitMx { get; set; }
    /// <summary>Предельный момент My, кН·м.</summary>
    [JsonPropertyName("My_limit")] public double LimitMy { get; set; }

    /// <summary>Осевое перемещение плоскости деформаций.</summary>
    [JsonPropertyName("e0")] public double E0 { get; set; }
    /// <summary>Кривизна по координате y, 1/м.</summary>
    [JsonPropertyName("ky")] public double Ky { get; set; }
    /// <summary>Кривизна по координате x, 1/м.</summary>
    [JsonPropertyName("kz")] public double Kz { get; set; }
    /// <summary>Минимальная деформация бетона в контуре.</summary>
    [JsonPropertyName("eps_contour_min")] public double EpsContourMin { get; set; }
    /// <summary>Предельная деформация сжатого бетона.</summary>
    [JsonPropertyName("eps_cu")] public double EpsCu { get; set; }
    /// <summary>Максимальная деформация арматуры.</summary>
    [JsonPropertyName("eps_rebar_max")] public double? EpsRebarMax { get; set; }
    /// <summary>Предельная деформация арматуры.</summary>
    [JsonPropertyName("eps_su")] public double? EpsSu { get; set; }
    /// <summary>Продольная сила в найденной плоскости, кН.</summary>
    [JsonPropertyName("N_result")] public double ResultN { get; set; }
    /// <summary>Момент Mx в найденной плоскости, кН·м.</summary>
    [JsonPropertyName("Mx_result")] public double ResultMx { get; set; }
    /// <summary>Момент My в найденной плоскости, кН·м.</summary>
    [JsonPropertyName("My_result")] public double ResultMy { get; set; }
    /// <summary>Данные поправки прогиба η.</summary>
    [JsonPropertyName("eta")] public EtaReportData? Eta { get; set; }
    /// <summary>Действия преднапряжения из результата.</summary>
    [JsonPropertyName("prestress")] public PrestressActionsJsonModel? Prestress { get; set; }

    /// <summary>Разбирает фактический JSON результата limit_*.</summary>
    /// <exception cref="JsonException">JSON пуст или имеет недопустимый формат.</exception>
    public static LimitForceReportData Parse(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
            throw new JsonException("Пустой результат limit_*.");
        return JsonSerializer.Deserialize<LimitForceReportData>(json)
            ?? throw new JsonException("Пустой результат limit_*.");
    }
}
