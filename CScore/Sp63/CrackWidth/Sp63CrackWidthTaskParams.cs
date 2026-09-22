using System.Text.Json;
using CScore.Sp63.Normal;

namespace CScore.Sp63.CrackWidth;

/// <summary>JSON-контракт параметров задачи упрощённой проверки ширины раскрытия трещин.</summary>
public sealed class Sp63CrackWidthTaskParams
{
    /// <summary>Идентификатор формы: rectangular.</summary>
    public string ShapeKind { get; set; } = "rectangular";

    /// <summary>Идентификатор оси: Mx или My.</summary>
    public string Axis { get; set; } = "Mx";

    /// <summary>Коэффициент длительности действия нагрузки φ1 (п. 8.2.10).</summary>
    public double Phi1 { get; set; } = 1.0;

    /// <summary>Коэффициент профиля арматуры φ2 (п. 8.2.10).</summary>
    public double Phi2 { get; set; } = 0.5;

    /// <summary>
    /// Предельно допустимая ширина раскрытия трещин, мм; в режиме long_and_short — предел
    /// продолжительного раскрытия.
    /// </summary>
    public double AcrcLimMm { get; set; } = 0.3;

    /// <summary>
    /// Режим: "single_term" — одна составляющая с заданным φ1 (по умолчанию, в том числе для
    /// задач, сохранённых до появления поля); "long_and_short" — продолжительное и
    /// непродолжительное раскрытие по п. 8.2.7.
    /// </summary>
    public string Mode { get; set; } = "single_term";

    /// <summary>Доля длительных нагрузок ψ = Ml/M (0…1) для режима long_and_short.</summary>
    public double LongTermShare { get; set; } = 1.0;

    /// <summary>Предельная ширина непродолжительного раскрытия трещин, мм (режим long_and_short).</summary>
    public double AcrcLimShortMm { get; set; } = 0.4;

    /// <summary>
    /// Способ получения σs,crc в формуле ψs (п. 8.2.18): "stress" — по напряжениям,
    /// ф. (8.137) (по умолчанию); "moment" — через Mcrc, ф. (8.138).
    /// </summary>
    public string SigmaSCrcMethod { get; set; } = "stress";

    /// <summary>
    /// Источник коэффициента пластичности γ в Wpl = γ·Wred: "sp63" — 1,3 (по умолчанию);
    /// "snip" — 1,75 по СНиП 2.03.01-84*; "radaykin" — по степени армирования.
    /// </summary>
    public string WplGamma { get; set; } = "sp63";

    /// <summary>Использовать ручные значения N, Mx и My.</summary>
    public bool UseManualForces { get; set; }

    /// <summary>Ручная продольная сила, кН.</summary>
    public double? N { get; set; }

    /// <summary>Ручной момент Mx, кН·м.</summary>
    public double? Mx { get; set; }

    /// <summary>Ручной момент My, кН·м.</summary>
    public double? My { get; set; }

    /// <summary>Разбирает JSON или возвращает безопасные параметры по умолчанию.</summary>
    public static Sp63CrackWidthTaskParams Parse(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return new Sp63CrackWidthTaskParams();

        try
        {
            return JsonSerializer.Deserialize<Sp63CrackWidthTaskParams>(json,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
                ?? new Sp63CrackWidthTaskParams();
        }
        catch (JsonException)
        {
            return new Sp63CrackWidthTaskParams { ShapeKind = "" };
        }
    }

    /// <summary>Сериализует параметры с именами свойств в camelCase.</summary>
    public string ToJson() => JsonSerializer.Serialize(this, new JsonSerializerOptions
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    });

    /// <summary>Преобразует строковые значения JSON в типизированные параметры.</summary>
    public bool TryToOptions(out Sp63CrackWidthOptions options, out string errorCode)
    {
        options = null!;
        errorCode = "";

        if (!string.Equals(ShapeKind, "rectangular", StringComparison.OrdinalIgnoreCase))
            return Invalid("invalid_shape_kind", out errorCode);
        if (!Enum.TryParse<Sp63NormalAxis>(Axis, true, out var axis))
            return Invalid("invalid_axis", out errorCode);
        if (!double.IsFinite(Phi1) || Phi1 <= 0 || !double.IsFinite(Phi2) || Phi2 <= 0)
            return Invalid("invalid_phi", out errorCode);
        if (!double.IsFinite(AcrcLimMm) || AcrcLimMm <= 0 ||
            !double.IsFinite(AcrcLimShortMm) || AcrcLimShortMm <= 0)
            return Invalid("invalid_acrc_limit", out errorCode);

        if (!TryParseSigmaSCrcMethod(SigmaSCrcMethod, out var sigmaSCrc))
            return Invalid("invalid_sigma_s_crc_method", out errorCode);

        if (!TryParseWplGamma(WplGamma, out var wplGamma))
            return Invalid("invalid_wpl_gamma", out errorCode);

        if (!TryParseMode(Mode, out var mode))
            return Invalid("invalid_mode", out errorCode);
        if (!double.IsFinite(LongTermShare) || LongTermShare < 0 || LongTermShare > 1)
            return Invalid("invalid_long_term_share", out errorCode);

        options = new Sp63CrackWidthOptions(Sp63NormalShapeKind.Rectangular, axis, Phi1, Phi2,
            AcrcLimMm, sigmaSCrc, wplGamma, mode, LongTermShare, AcrcLimShortMm);
        return true;
    }

    /// <summary>Разбирает идентификатор режима проверки из JSON-контракта.</summary>
    public static bool TryParseMode(string? value, out Sp63CrackWidthMode mode)
    {
        mode = Sp63CrackWidthMode.SingleTerm;
        if (string.IsNullOrWhiteSpace(value)) return true;
        switch (value.Trim().ToLowerInvariant())
        {
            case "single_term": return true;
            case "long_and_short": mode = Sp63CrackWidthMode.LongAndShort; return true;
            default: return false;
        }
    }

    /// <summary>Разбирает идентификатор способа получения σs,crc из JSON-контракта.</summary>
    public static bool TryParseSigmaSCrcMethod(string? value, out CScore.SigmaSCrcMethod method)
    {
        method = CScore.SigmaSCrcMethod.ReleasedConcrete8137;
        if (string.IsNullOrWhiteSpace(value)) return true;
        switch (value.Trim().ToLowerInvariant())
        {
            case "stress": method = CScore.SigmaSCrcMethod.ReleasedConcrete8137; return true;
            case "moment": method = CScore.SigmaSCrcMethod.CrackingMoment8138; return true;
            default: return false;
        }
    }

    /// <summary>Разбирает идентификатор источника γ из JSON-контракта.</summary>
    public static bool TryParseWplGamma(string? value, out CScore.WplGammaMethod method)
    {
        method = CScore.WplGammaMethod.Sp63;
        if (string.IsNullOrWhiteSpace(value)) return true;
        switch (value.Trim().ToLowerInvariant())
        {
            case "sp63": method = CScore.WplGammaMethod.Sp63; return true;
            case "snip": method = CScore.WplGammaMethod.Snip2030184; return true;
            case "radaykin": method = CScore.WplGammaMethod.Radaykin2018; return true;
            default: return false;
        }
    }

    /// <summary>Создаёт строку нагрузки из ручных полей задачи.</summary>
    public LoadItem ToLoadItem() => new()
    {
        N = N ?? 0.0,
        Mx = Mx ?? 0.0,
        My = My ?? 0.0
    };

    static bool Invalid(string code, out string errorCode)
    {
        errorCode = code;
        return false;
    }
}
