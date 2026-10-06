using System.Text.Json.Serialization;

namespace CScore.Fem;

/// <summary>
/// Параметры учёта продольного изгиба (коэффициент η, п. 8.1.15 СП 63.13330) в проверке по КЭ.
/// Хранятся внутри параметров проверки (<see cref="BarCheckParams.Eta"/>, <see cref="PlateCheckParams.Eta"/>).
/// Расчётная длина l0 = μ·l (п. 8.1.17): l — длина элемента между раскреплениями, по умолчанию определяется
/// по сетке схемы для каждого КЭ (<see cref="FemBracedLength"/>).
/// </summary>
public sealed record FemEtaParams
{
    /// <summary>ψ берётся из парного длительного набора усилий.</summary>
    public const string PsiAuto = "auto";
    /// <summary>ψ задаётся вручную.</summary>
    public const string PsiManual = "manual";

    /// <summary>Учитывать влияние прогиба.</summary>
    public bool Enabled { get; init; }

    /// <summary>false — буквальный режим (формула нормы); true — уточнённый (по жёсткости из НДС).</summary>
    public bool Iterative { get; init; }

    /// <summary>Длина элемента l вручную, м; null — по сетке схемы для каждого КЭ.</summary>
    public double? LengthM { get; init; }

    /// <summary>Коэффициент расчётной длины в плоскости Mx сечения (у стен — вертикальной полосы).</summary>
    public double MuX { get; init; } = 1.0;

    /// <summary>Коэффициент расчётной длины в плоскости My сечения (у стен не используется).</summary>
    public double MuY { get; init; } = 1.0;

    /// <summary>Источник ψ = M1l/M1: <see cref="PsiAuto"/> или <see cref="PsiManual"/>.</summary>
    public string PsiMode { get; init; } = PsiAuto;

    /// <summary>ψ в плоскости Mx — вручную или при отсутствии длительного набора.</summary>
    public double PsiX { get; init; } = 1.0;

    /// <summary>ψ в плоскости My — вручную или при отсутствии длительного набора.</summary>
    public double PsiY { get; init; } = 1.0;

    /// <summary>Предельная гибкость l0/i, выше которой учитывается η; null — 14 (п. 8.1.2).</summary>
    public double? SlendernessThreshold { get; init; }

    /// <summary>ψ берётся из парного длительного набора.</summary>
    [JsonIgnore]
    public bool IsPsiAuto => PsiMode != PsiManual;

    /// <summary>Длина элемента задана вручную.</summary>
    [JsonIgnore]
    public bool HasManualLength => LengthM is > 0;
}
