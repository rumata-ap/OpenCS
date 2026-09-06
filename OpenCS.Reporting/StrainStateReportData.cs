using System.Text.Json;
using System.Text.Json.Serialization;
using CScore;

namespace OpenCS.Reporting;

/// <summary>Типизированные данные результата strain_state для построения отчёта.</summary>
public sealed class StrainStateReportData
{
    /// <summary>Признак сходимости расчёта.</summary>
    [JsonPropertyName("converged")] public bool Converged { get; set; }
    /// <summary>Количество итераций.</summary>
    [JsonPropertyName("iterations")] public int Iterations { get; set; }
    /// <summary>Норма невязки.</summary>
    [JsonPropertyName("residual")] public double Residual { get; set; }
    /// <summary>Осевое перемещение плоскости деформаций.</summary>
    [JsonPropertyName("e0")] public double E0 { get; set; }
    /// <summary>Кривизна по координате y.</summary>
    [JsonPropertyName("ky")] public double Ky { get; set; }
    /// <summary>Кривизна по координате x.</summary>
    [JsonPropertyName("kz")] public double Kz { get; set; }
    /// <summary>Целевая продольная сила.</summary>
    [JsonPropertyName("N_target")] public double TargetN { get; set; }
    /// <summary>Целевой момент Mx.</summary>
    [JsonPropertyName("Mx_target")] public double TargetMx { get; set; }
    /// <summary>Целевой момент My.</summary>
    [JsonPropertyName("My_target")] public double TargetMy { get; set; }
    /// <summary>Расчётная продольная сила.</summary>
    [JsonPropertyName("N_result")] public double ResultN { get; set; }
    /// <summary>Расчётный момент Mx.</summary>
    [JsonPropertyName("Mx_result")] public double ResultMx { get; set; }
    /// <summary>Расчётный момент My.</summary>
    [JsonPropertyName("My_result")] public double ResultMy { get; set; }

    /// <summary>Версия набора формул и нормативная ссылка.</summary>
    [JsonPropertyName("formula_version")]
    public string FormulaVersion { get; set; } = "";

    /// <summary>Секущая матрица жёсткости.</summary>
    [JsonPropertyName("stiffness")]
    public StrainStateStiffnessData Stiffness { get; set; } = new();

    /// <summary>Якобиан solver-а Ньютона.</summary>
    [JsonPropertyName("jacobian")]
    public StrainStateJacobianData Jacobian { get; set; } = new();

    /// <summary>Усилия, восстановленные в найденной плоскости деформаций.</summary>
    [JsonPropertyName("equilibrium")]
    public StrainStateEquilibriumData Equilibrium { get; set; } = new();

    /// <summary>Экстремальные деформации бетона и арматуры.</summary>
    [JsonPropertyName("extrema")]
    public StrainStateExtremaData Extrema { get; set; } = new();

    /// <summary>Данные о точечных фибрах арматуры, сохранённые в результате.</summary>
    [JsonPropertyName("rebar")]
    public List<StrainStateRebarData> Rebar { get; set; } = [];

    /// <summary>Идентификация поперечного сечения, сохранённая вместе с результатом.</summary>
    [JsonPropertyName("section")]
    public StrainStateSectionData? Section { get; set; }

    /// <summary>Данные поправки прогиба η из расчёта.</summary>
    [JsonPropertyName("eta")]
    public EtaReportData? Eta { get; set; }

    /// <summary>Действия преднапряжения из расчёта.</summary>
    [JsonPropertyName("prestress")]
    public PrestressActionsJsonModel? Prestress { get; set; }

    /// <summary>Разбирает JSON результата. Дополнительные поля старых результатов игнорируются.</summary>
    public static StrainStateReportData Parse(string json)
    {
        ArgumentException.ThrowIfNullOrEmpty(json);
        return JsonSerializer.Deserialize<StrainStateReportData>(json)
            ?? throw new JsonException("Пустой результат strain_state.");
    }
}

/// <summary>Результат по одному точечному стержню арматуры в JSON отчёта.</summary>
public sealed class StrainStateRebarData
{
    /// <summary>Номер стержня.</summary>
    [JsonPropertyName("num")] public int Num { get; set; }
    /// <summary>Координата X, мм.</summary>
    [JsonPropertyName("x_mm")] public double Xmm { get; set; }
    /// <summary>Координата Y, мм.</summary>
    [JsonPropertyName("y_mm")] public double Ymm { get; set; }
    /// <summary>Полная деформация.</summary>
    [JsonPropertyName("eps")] public double Eps { get; set; }
    /// <summary>Напряжение, МПа.</summary>
    [JsonPropertyName("sigma_mpa")] public double SigmaMpa { get; set; }
    /// <summary>Секущий модуль стержня, МПа.</summary>
    [JsonPropertyName("e_sec_mpa")] public double SecantModulusMpa { get; set; }
    /// <summary>Площадь стержня, мм².</summary>
    [JsonPropertyName("area_mm2")] public double AreaMm2 { get; set; }
    /// <summary>Диаметр стержня, мм.</summary>
    [JsonPropertyName("diameter_mm")] public double DiameterMm { get; set; }
    /// <summary>Метка группы.</summary>
    [JsonPropertyName("group")] public string Group { get; set; } = "";
    /// <summary>Материал стержня.</summary>
    [JsonPropertyName("material")] public string Material { get; set; } = "";
}

/// <summary>Сохранённая в JSON идентификация сечения для автономного отчёта.</summary>
public sealed class StrainStateSectionData
{
    /// <summary>Идентификатор сечения в базе данных.</summary>
    [JsonPropertyName("id")] public int Id { get; set; }
    /// <summary>Порядковый номер сечения.</summary>
    [JsonPropertyName("num")] public int Num { get; set; }
    /// <summary>Метка сечения.</summary>
    [JsonPropertyName("tag")] public string Tag { get; set; } = "";
    /// <summary>Описание сечения.</summary>
    [JsonPropertyName("description")] public string Description { get; set; } = "";
}

/// <summary>Данные матрицы D по СП 63.</summary>
public sealed class StrainStateStiffnessData
{
    /// <summary>Источник интегрирования: fiber, contour или mixed.</summary>
    [JsonPropertyName("source")] public string Source { get; set; } = "unknown";
    /// <summary>Элемент D11.</summary>
    [JsonPropertyName("d11")] public double D11 { get; set; }
    /// <summary>Элемент D12.</summary>
    [JsonPropertyName("d12")] public double D12 { get; set; }
    /// <summary>Элемент D13.</summary>
    [JsonPropertyName("d13")] public double D13 { get; set; }
    /// <summary>Элемент D21.</summary>
    [JsonPropertyName("d21")] public double D21 { get; set; }
    /// <summary>Элемент D22.</summary>
    [JsonPropertyName("d22")] public double D22 { get; set; }
    /// <summary>Элемент D23.</summary>
    [JsonPropertyName("d23")] public double D23 { get; set; }
    /// <summary>Элемент D31.</summary>
    [JsonPropertyName("d31")] public double D31 { get; set; }
    /// <summary>Элемент D32.</summary>
    [JsonPropertyName("d32")] public double D32 { get; set; }
    /// <summary>Элемент D33.</summary>
    [JsonPropertyName("d33")] public double D33 { get; set; }
}

/// <summary>Данные численного якобиана Ньютона.</summary>
public sealed class StrainStateJacobianData
{
    /// <summary>Названия строк.</summary>
    [JsonPropertyName("rows")] public string[] Rows { get; set; } = [];
    /// <summary>Названия столбцов.</summary>
    [JsonPropertyName("columns")] public string[] Columns { get; set; } = [];
    /// <summary>Схема конечных разностей.</summary>
    [JsonPropertyName("scheme")] public string Scheme { get; set; } = "";
    /// <summary>Шаг конечных разностей.</summary>
    [JsonPropertyName("h")] public double Step { get; set; }
    /// <summary>Значения по строкам и столбцам.</summary>
    [JsonPropertyName("values")] public double[][] Values { get; set; } = [];
}

/// <summary>Результирующие усилия равновесия.</summary>
public sealed class StrainStateEquilibriumData
{
    /// <summary>Продольная сила.</summary>
    [JsonPropertyName("n")] public double N { get; set; }
    /// <summary>Момент Mx.</summary>
    [JsonPropertyName("mx")] public double Mx { get; set; }
    /// <summary>Момент My.</summary>
    [JsonPropertyName("my")] public double My { get; set; }
}

/// <summary>Экстремальные деформации материалов.</summary>
public sealed class StrainStateExtremaData
{
    /// <summary>Минимальная деформация бетона.</summary>
    [JsonPropertyName("eps_b_min")] public double ConcreteMin { get; set; }
    /// <summary>Максимальная деформация бетона.</summary>
    [JsonPropertyName("eps_b_max")] public double ConcreteMax { get; set; }
    /// <summary>Минимальная деформация арматуры.</summary>
    [JsonPropertyName("eps_s_min")] public double SteelMin { get; set; }
    /// <summary>Максимальная деформация арматуры.</summary>
    [JsonPropertyName("eps_s_max")] public double SteelMax { get; set; }
}
