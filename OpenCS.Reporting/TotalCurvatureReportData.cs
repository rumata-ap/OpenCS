using System.Text.Json;
using System.Text.Json.Serialization;
using CScore;

namespace OpenCS.Reporting;

/// <summary>Типизированный контракт результата расчёта полной кривизны.</summary>
public sealed class TotalCurvatureReportData
{
    /// <summary>Продольная сила, кН.</summary>
    [JsonPropertyName("N")] public double N { get; set; }
    /// <summary>Длительный момент Mx, кН·м.</summary>
    [JsonPropertyName("Mx_long")] public double MxLong { get; set; }
    /// <summary>Длительный момент My, кН·м.</summary>
    [JsonPropertyName("My_long")] public double MyLong { get; set; }
    /// <summary>Полный момент Mx, кН·м.</summary>
    [JsonPropertyName("Mx_total")] public double MxTotal { get; set; }
    /// <summary>Полный момент My, кН·м.</summary>
    [JsonPropertyName("My_total")] public double MyTotal { get; set; }
    /// <summary>Признак наличия трещин при полной нагрузке.</summary>
    [JsonPropertyName("cracked")] public bool Cracked { get; set; }
    /// <summary>Момент образования трещин, кН·м.</summary>
    [JsonPropertyName("Mcrc")] public double Mcrc { get; set; }
    /// <summary>Компонента Mx момента образования трещин, кН·м.</summary>
    [JsonPropertyName("Mx_crc")] public double MxCrc { get; set; }
    /// <summary>Компонента My момента образования трещин, кН·м.</summary>
    [JsonPropertyName("My_crc")] public double MyCrc { get; set; }
    /// <summary>Сходимость определения момента образования трещин.</summary>
    [JsonPropertyName("crc_converged")] public bool CrcConverged { get; set; }
    /// <summary>Полная кривизна по оси y, 1/м.</summary>
    [JsonPropertyName("ky_full")] public double KyFull { get; set; }
    /// <summary>Полная кривизна по оси z, 1/м.</summary>
    [JsonPropertyName("kz_full")] public double KzFull { get; set; }
    /// <summary>Модуль полной кривизны, 1/м.</summary>
    [JsonPropertyName("k_full")] public double KFull { get; set; }
    /// <summary>Общий признак сходимости трёх стадий.</summary>
    [JsonPropertyName("all_converged")] public bool AllConverged { get; set; }
    /// <summary>Действия преднапряжения.</summary>
    [JsonPropertyName("prestress")] public PrestressActionsJsonModel? Prestress { get; set; }
    /// <summary>Первая стадия 1/r1.</summary>
    [JsonPropertyName("stage1")] public TotalCurvatureStageData? Stage1 { get; set; }
    /// <summary>Вторая стадия 1/r2.</summary>
    [JsonPropertyName("stage2")] public TotalCurvatureStageData? Stage2 { get; set; }
    /// <summary>Третья стадия 1/r3.</summary>
    [JsonPropertyName("stage3")] public TotalCurvatureStageData? Stage3 { get; set; }

    /// <summary>Разбирает фактический JSON результата total_curvature.</summary>
    /// <exception cref="JsonException">JSON пуст или имеет недопустимый формат.</exception>
    public static TotalCurvatureReportData Parse(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
            throw new JsonException("Пустой результат total_curvature.");
        return JsonSerializer.Deserialize<TotalCurvatureReportData>(json)
            ?? throw new JsonException("Пустой результат total_curvature.");
    }
}

/// <summary>Данные одной стадии расчёта кривизны.</summary>
public sealed class TotalCurvatureStageData
{
    /// <summary>Момент Mx стадии, кН·м.</summary>
    [JsonPropertyName("Mx")] public double? Mx { get; set; }
    /// <summary>Момент My стадии, кН·м.</summary>
    [JsonPropertyName("My")] public double? My { get; set; }
    /// <summary>Осевое перемещение плоскости стадии.</summary>
    [JsonPropertyName("e0")] public double? E0 { get; set; }
    /// <summary>Кривизна ky стадии, 1/м.</summary>
    [JsonPropertyName("ky")] public double? Ky { get; set; }
    /// <summary>Кривизна kz стадии, 1/м.</summary>
    [JsonPropertyName("kz")] public double? Kz { get; set; }
    /// <summary>Вид расчётных характеристик: N или NL.</summary>
    [JsonPropertyName("calc_type")] public string CalcType { get; set; } = "N";
    /// <summary>Учитывается ли растянутый бетон.</summary>
    [JsonPropertyName("concrete_tension")] public bool ConcreteTension { get; set; }
    /// <summary>Сходимость стадии.</summary>
    [JsonPropertyName("converged")] public bool Converged { get; set; }
    /// <summary>Коэффициенты ψs по точкам арматуры.</summary>
    [JsonPropertyName("psi_s_by_rebar")] public List<TotalCurvaturePsiRebarData> PsiSByRebar { get; set; } = [];
}

/// <summary>Коэффициент ψs в одной точке стадии; координаты в метрах.</summary>
public sealed class TotalCurvaturePsiRebarData
{
    /// <summary>Номер стержня; может отсутствовать в legacy JSON.</summary>
    [JsonPropertyName("num")] public int? Num { get; set; }
    /// <summary>Координата X, м.</summary>
    [JsonPropertyName("x")] public double X { get; set; }
    /// <summary>Координата Y, м.</summary>
    [JsonPropertyName("y")] public double Y { get; set; }
    /// <summary>Коэффициент ψs.</summary>
    [JsonPropertyName("psi_s")] public double PsiS { get; set; }
    /// <summary>Учитывается ли эта точка в расчёте.</summary>
    [JsonPropertyName("applicable")] public bool Applicable { get; set; }
}
