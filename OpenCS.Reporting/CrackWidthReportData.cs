using System.Text.Json;
using System.Text.Json.Serialization;
using CScore;

namespace OpenCS.Reporting;

/// <summary>Типизированный контракт результата расчёта ширины нормальных трещин.</summary>
public sealed class CrackWidthReportData
{
    /// <summary>Продольная сила, кН.</summary>
    [JsonPropertyName("N")] public double N { get; set; }
    /// <summary>Длительный момент Mx, кН·м.</summary>
    [JsonPropertyName("Mx_long")] public double MxLong { get; set; }
    /// <summary>Полный момент Mx, кН·м.</summary>
    [JsonPropertyName("Mx_total")] public double MxTotal { get; set; }
    /// <summary>Длительный момент My, кН·м.</summary>
    [JsonPropertyName("My_long")] public double MyLong { get; set; }
    /// <summary>Полный момент My, кН·м.</summary>
    [JsonPropertyName("My_total")] public double MyTotal { get; set; }
    /// <summary>Исходный заданный длительный момент Mx, кН·м.</summary>
    [JsonPropertyName("Mx_long_input")] public double MxLongInput { get; set; }
    /// <summary>Исходный заданный полный момент Mx, кН·м.</summary>
    [JsonPropertyName("Mx_total_input")] public double MxTotalInput { get; set; }
    /// <summary>Исходный заданный длительный момент My, кН·м.</summary>
    [JsonPropertyName("My_long_input")] public double MyLongInput { get; set; }
    /// <summary>Исходный заданный полный момент My, кН·м.</summary>
    [JsonPropertyName("My_total_input")] public double MyTotalInput { get; set; }
    /// <summary>Признак образования трещин.</summary>
    [JsonPropertyName("cracked")] public bool Cracked { get; set; }
    /// <summary>Итоговая длительная ширина, мм.</summary>
    [JsonPropertyName("acrc_long")] public double AcrcLong { get; set; }
    /// <summary>Итоговая кратковременная ширина, мм.</summary>
    [JsonPropertyName("acrc_short")] public double AcrcShort { get; set; }
    /// <summary>Предельная длительная ширина, мм.</summary>
    [JsonPropertyName("acrc_ult_long")] public double AcrcUltLong { get; set; }
    /// <summary>Предельная кратковременная ширина, мм.</summary>
    [JsonPropertyName("acrc_ult_short")] public double AcrcUltShort { get; set; }
    /// <summary>Длительная проверка ширины.</summary>
    [JsonPropertyName("passed_long")] public bool PassedLong { get; set; }
    /// <summary>Кратковременная проверка ширины.</summary>
    [JsonPropertyName("passed_short")] public bool PassedShort { get; set; }
    /// <summary>Момент образования трещин, кН·м.</summary>
    [JsonPropertyName("Mcrc")] public double Mcrc { get; set; }
    /// <summary>Компонента Mx момента образования трещин, кН·м.</summary>
    [JsonPropertyName("Mx_crc")] public double MxCrc { get; set; }
    /// <summary>Компонента My момента образования трещин, кН·м.</summary>
    [JsonPropertyName("My_crc")] public double MyCrc { get; set; }
    /// <summary>Сходимость определения момента трещинообразования.</summary>
    [JsonPropertyName("crc_converged")] public bool CrcConverged { get; set; }
    /// <summary>Максимальная растягивающая деформация бетона.</summary>
    [JsonPropertyName("eps_max_tension")] public double EpsMaxTension { get; set; }
    /// <summary>Предельная растягивающая деформация бетона.</summary>
    [JsonPropertyName("eps_tension_limit")] public double EpsTensionLimit { get; set; }
    /// <summary>Эффективная высота сечения h0, мм.</summary>
    [JsonPropertyName("h0")] public double H0 { get; set; }
    /// <summary>Напряжение арматуры σs, МПа.</summary>
    [JsonPropertyName("sigma_s")] public double SigmaS { get; set; }
    /// <summary>Напряжение σs,cr для длительной ширины, МПа.</summary>
    [JsonPropertyName("sigma_s_crc")] public double SigmaSCrc { get; set; }
    /// <summary>Напряжение σs,cr для кратковременной ширины, МПа.</summary>
    [JsonPropertyName("sigma_s_crc2")] public double SigmaSCrc2 { get; set; }
    /// <summary>Коэффициент ψs для длительной ширины.</summary>
    [JsonPropertyName("psi_s")] public double PsiS { get; set; }
    /// <summary>Коэффициент ψs для кратковременной ширины.</summary>
    [JsonPropertyName("psi_s2")] public double PsiS2 { get; set; }
    /// <summary>Первая составляющая ширины, мм.</summary>
    [JsonPropertyName("acrc1")] public double Acrc1 { get; set; }
    /// <summary>Вторая составляющая ширины, мм.</summary>
    [JsonPropertyName("acrc2")] public double Acrc2 { get; set; }
    /// <summary>Третья составляющая ширины, мм.</summary>
    [JsonPropertyName("acrc3")] public double Acrc3 { get; set; }
    /// <summary>Расстояние между трещинами ls, мм.</summary>
    [JsonPropertyName("ls")] public double Ls { get; set; }
    /// <summary>Эквивалентный диаметр арматуры ds, мм.</summary>
    [JsonPropertyName("ds_eq")] public double DsEq { get; set; }
    /// <summary>Площадь растянутой арматуры As,tens, см².</summary>
    [JsonPropertyName("As_tens")] public double AsTens { get; set; }
    /// <summary>Площадь растянутого бетона Abt, см².</summary>
    [JsonPropertyName("Abt")] public double Abt { get; set; }
    /// <summary>Осевое перемещение плоскости, если она найдена.</summary>
    [JsonPropertyName("e0")] public double? E0 { get; set; }
    /// <summary>Кривизна ky, 1/м.</summary>
    [JsonPropertyName("ky")] public double? Ky { get; set; }
    /// <summary>Кривизна kz, 1/м.</summary>
    [JsonPropertyName("kz")] public double? Kz { get; set; }
    /// <summary>Сходимость плоскости длительной нагрузки.</summary>
    [JsonPropertyName("plane_converged")] public bool PlaneConverged { get; set; }
    /// <summary>Расчёт ψs и ширины в точках стержней.</summary>
    [JsonPropertyName("acrc_by_rebar")] public List<CrackWidthRebarData> AcrcByRebar { get; set; } = [];
    /// <summary>Данные поправки прогиба η.</summary>
    [JsonPropertyName("eta")] public EtaReportData? Eta { get; set; }
    /// <summary>Действия преднапряжения.</summary>
    [JsonPropertyName("prestress")] public PrestressActionsJsonModel? Prestress { get; set; }

    /// <summary>Разбирает фактический JSON результата crack_width.</summary>
    /// <exception cref="JsonException">JSON пуст или имеет недопустимый формат.</exception>
    public static CrackWidthReportData Parse(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
            throw new JsonException("Пустой результат crack_width.");
        return JsonSerializer.Deserialize<CrackWidthReportData>(json)
            ?? throw new JsonException("Пустой результат crack_width.");
    }
}

/// <summary>Данные ширины трещины в одной точке растянутого стержня; длины и координаты в мм.</summary>
public sealed class CrackWidthRebarData
{
    /// <summary>Координата X, мм.</summary>
    [JsonPropertyName("x")] public double X { get; set; }
    /// <summary>Координата Y, мм.</summary>
    [JsonPropertyName("y")] public double Y { get; set; }
    /// <summary>Коэффициент ψs, длительная ширина.</summary>
    [JsonPropertyName("psi_s")] public double PsiS { get; set; }
    /// <summary>Длительная ширина трещины, мм.</summary>
    [JsonPropertyName("acrc_long_mm")] public double AcrcLongMm { get; set; }
    /// <summary>Коэффициент ψs, кратковременная ширина.</summary>
    [JsonPropertyName("psi_s2")] public double PsiS2 { get; set; }
    /// <summary>Кратковременная ширина трещины, мм.</summary>
    [JsonPropertyName("acrc_short_mm")] public double AcrcShortMm { get; set; }
}
