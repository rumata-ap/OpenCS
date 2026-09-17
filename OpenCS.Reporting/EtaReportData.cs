using System.Text.Json.Serialization;

namespace OpenCS.Reporting;

/// <summary>Общие JSON-данные поправки прогиба η по двум направлениям.</summary>
public sealed class EtaReportData
{
    /// <summary>Режим вычисления поправки.</summary>
    [JsonPropertyName("mode")] public string Mode { get; set; } = "";
    /// <summary>Порог гибкости, применённый расчётом.</summary>
    [JsonPropertyName("slendernessThreshold")] public double? SlendernessThreshold { get; set; }
    /// <summary>Коэффициент ψ по оси X.</summary>
    [JsonPropertyName("psiX")] public double? PsiX { get; set; }
    /// <summary>Коэффициент ψ по оси Y.</summary>
    [JsonPropertyName("psiY")] public double? PsiY { get; set; }
    /// <summary>Исходный момент Mx, кН·м.</summary>
    [JsonPropertyName("mxOriginal")] public double? MxOriginal { get; set; }
    /// <summary>Исходный момент My, кН·м.</summary>
    [JsonPropertyName("myOriginal")] public double? MyOriginal { get; set; }
    /// <summary>Длительный исходный момент Mx, кН·м.</summary>
    [JsonPropertyName("mxLongOriginal")] public double? MxLongOriginal { get; set; }
    /// <summary>Длительный исходный момент My, кН·м.</summary>
    [JsonPropertyName("myLongOriginal")] public double? MyLongOriginal { get; set; }

    /// <summary>Расчётная длина l0x, м.</summary>
    [JsonPropertyName("l0x")] public double? L0x { get; set; }
    /// <summary>Размер hx, м (используется только в δe = e0/hx).</summary>
    [JsonPropertyName("hx")] public double? Hx { get; set; }
    /// <summary>Радиус инерции бетонного сечения брутто по X, м.</summary>
    [JsonPropertyName("ix")] public double? Ix { get; set; }
    /// <summary>Радиус инерции по X принят по габариту (бетонные контуры не найдены).</summary>
    [JsonPropertyName("radiusFallbackX")] public bool RadiusFallbackX { get; set; }
    /// <summary>Гибкость l0x/ix (ix — радиус инерции бетонного сечения брутто).</summary>
    [JsonPropertyName("slendernessX")] public double? SlendernessX { get; set; }
    /// <summary>Изгибающий эффект Dx, кН·м².</summary>
    [JsonPropertyName("dX")] public double? DX { get; set; }
    /// <summary>Поправка ηx.</summary>
    [JsonPropertyName("etaX")] public double? EtaX { get; set; }
    /// <summary>Критическая сила Ncrx, кН.</summary>
    [JsonPropertyName("ncrX")] public double? NcrX { get; set; }
    /// <summary>Признак гибкости по X.</summary>
    [JsonPropertyName("slenderX")] public bool SlenderX { get; set; }
    /// <summary>Признак неудачной экстраполяции ηx.</summary>
    [JsonPropertyName("extrapolationFailedX")] public bool ExtrapolationFailedX { get; set; }
    /// <summary>Признак устойчивого решения по X.</summary>
    [JsonPropertyName("stableX")] public bool StableX { get; set; } = true;
    /// <summary>История итераций ηx.</summary>
    [JsonPropertyName("etaHistoryX")] public double[] EtaHistoryX { get; set; } = [];

    /// <summary>Расчётная длина l0y, м.</summary>
    [JsonPropertyName("l0y")] public double? L0y { get; set; }
    /// <summary>Размер hy, м (используется только в δe = e0/hy).</summary>
    [JsonPropertyName("hy")] public double? Hy { get; set; }
    /// <summary>Радиус инерции бетонного сечения брутто по Y, м.</summary>
    [JsonPropertyName("iy")] public double? Iy { get; set; }
    /// <summary>Радиус инерции по Y принят по габариту (бетонные контуры не найдены).</summary>
    [JsonPropertyName("radiusFallbackY")] public bool RadiusFallbackY { get; set; }
    /// <summary>Гибкость l0y/iy (iy — радиус инерции бетонного сечения брутто).</summary>
    [JsonPropertyName("slendernessY")] public double? SlendernessY { get; set; }
    /// <summary>Изгибающий эффект Dy, кН·м².</summary>
    [JsonPropertyName("dY")] public double? DY { get; set; }
    /// <summary>Поправка ηy.</summary>
    [JsonPropertyName("etaY")] public double? EtaY { get; set; }
    /// <summary>Критическая сила Ncry, кН.</summary>
    [JsonPropertyName("ncrY")] public double? NcrY { get; set; }
    /// <summary>Признак гибкости по Y.</summary>
    [JsonPropertyName("slenderY")] public bool SlenderY { get; set; }
    /// <summary>Признак неудачной экстраполяции ηy.</summary>
    [JsonPropertyName("extrapolationFailedY")] public bool ExtrapolationFailedY { get; set; }
    /// <summary>Признак устойчивого решения по Y.</summary>
    [JsonPropertyName("stableY")] public bool StableY { get; set; } = true;
    /// <summary>История итераций ηy.</summary>
    [JsonPropertyName("etaHistoryY")] public double[] EtaHistoryY { get; set; } = [];
}
