using System.Text.Json;

namespace CScore;

/// <summary>
/// Параметры задачи «Кривизна-момент (двухплоскостной изгиб)», хранящиеся в
/// CalcTask.ParamsJson. Направление луча (N0/Mx0/My0) — ручной ввод (как у total_curvature),
/// не выбор ForceSet+ForceItem.
/// </summary>
public sealed class MomentCurvatureBiaxialTaskParams
{
    /// <summary>Ручная нормальная сила N0, кН.</summary>
    public double? N { get; set; }

    /// <summary>Ручной момент Mx0, кН·м.</summary>
    public double? Mx { get; set; }

    /// <summary>Ручной момент My0, кН·м.</summary>
    public double? My { get; set; }

    /// <summary>Режим продольной силы по ходу скана: constant или proportional.</summary>
    public string NMode { get; set; } = "constant";

    /// <summary>Учитывать ли ψs по 8.2.32 на уч. 3-4 (см. спеку, решение 5).</summary>
    public bool UsePsi { get; set; } = true;

    /// <summary>Число вспомогательных точек на участок диаграммы (null → дефолт 10 в солвере).</summary>
    public int? AuxPointsPerSegment { get; set; }

    /// <summary>Режим расстановки вспомогательных точек: "curvature" или "moment".</summary>
    public string StepMode { get; set; } = "curvature";

    /// <summary>Резолвит <see cref="StepMode"/> в enum, неизвестное/пустое значение → ByCurvature.</summary>
    public CurveStepMode ResolveStepMode() => StepMode?.Trim().ToLowerInvariant() switch
    {
        "moment" => CurveStepMode.ByMoment,
        _ => CurveStepMode.ByCurvature
    };

    /// <summary>Резолвит <see cref="AuxPointsPerSegment"/>: null → 10, отрицательное → 0.</summary>
    public int ResolveAuxPointsPerSegment() => AuxPointsPerSegment switch
    {
        null => 10,
        < 0 => 0,
        var v => v.Value
    };

    /// <summary>Разобрать JSON-параметры задачи.</summary>
    public static MomentCurvatureBiaxialTaskParams Parse(string? json)
    {
        if (string.IsNullOrWhiteSpace(json) || json == "{}")
            return new MomentCurvatureBiaxialTaskParams();

        try
        {
            return JsonSerializer.Deserialize<MomentCurvatureBiaxialTaskParams>(json,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
                ?? new MomentCurvatureBiaxialTaskParams();
        }
        catch
        {
            return new MomentCurvatureBiaxialTaskParams();
        }
    }

    /// <summary>Сериализовать параметры задачи.</summary>
    public string ToJson() => JsonSerializer.Serialize(this);

    /// <summary>Создать строку нагрузки для общего механизма исполнителя задач.</summary>
    public LoadItem ToLoadItem() => new()
    {
        N = N ?? 0.0,
        Mx = Mx ?? 0.0,
        My = My ?? 0.0
    };
}
