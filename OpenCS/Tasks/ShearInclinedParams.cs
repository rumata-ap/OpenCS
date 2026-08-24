using System.Text.Json;
using System.Text.Json.Serialization;
using CScore.Sp63Shear;

namespace OpenCS.Tasks;

/// <summary>Ручные переопределения расчётных величин одной плоскости сдвига.</summary>
public record ShearInclinedOverrides
{
    /// <summary>Расчётная ширина, м.</summary>
    public double? B { get; init; }
    /// <summary>Рабочая высота, м.</summary>
    public double? H0 { get; init; }
    /// <summary>Погонное усилие в хомутах, кН/м.</summary>
    public double? Qsw { get; init; }
    /// <summary>Сопротивление поперечной арматуры, кПа.</summary>
    public double? Rsw { get; init; }
    /// <summary>Усилие в растянутой продольной арматуре Σ(Rs,i·As,i), кН.</summary>
    public double? Ns { get; init; }
    /// <summary>Коэффициент φn.</summary>
    public double? PhiN { get; init; }
    /// <summary>Сопротивление бетона сжатию, кПа.</summary>
    public double? Rb { get; init; }
    /// <summary>Сопротивление бетона растяжению, кПа.</summary>
    public double? Rbt { get; init; }
}

/// <summary>
/// Параметры задачи расчёта наклонных сечений по СП 63.13330, пп. 8.1.32–8.1.35.
/// Хранятся в <see cref="CScore.CalcTask.ParamsJson"/>.
/// </summary>
public record ShearInclinedParams
{
    /// <summary>Источник усилий: "constant" | "uniform_load" | "fem_profile".</summary>
    public string ForceSource { get; init; } = "constant";

    /// <summary>Тип элемента: "bending_unstressed" | "other".</summary>
    public string ElementKind { get; init; } = "bending_unstressed";

    /// <summary>Равномерно распределённая нагрузка q, кН/м.</summary>
    public double DistributedLoad { get; init; }

    /// <summary>Расстояние от расчётного сечения до опоры, м; 0 — не задано.</summary>
    public double DistanceToSupport { get; init; }

    /// <summary>Направление к опоре: "auto" | "forward" | "backward".</summary>
    public string SupportDirection { get; init; } = "auto";

    /// <summary>Начало области определения профиля является опорой.</summary>
    public bool SupportAtStart { get; init; } = true;

    /// <summary>Конец области определения профиля является опорой.</summary>
    public bool SupportAtEnd { get; init; } = true;

    /// <summary>Шаг стоянок вдоль элемента, м; 0 — авто.</summary>
    public double StationStep { get; init; }

    /// <summary>Шаг перебора проекции наклонного сечения, м; 0 — авто.</summary>
    public double ProjectionStep { get; init; }

    /// <summary>Индекс шага нелинейного расчёта FEM; null — последний сошедшийся.</summary>
    public int? FemStepIndex { get; init; }

    /// <summary>Рассчитываемые плоскости: "both" | "vy" | "vx".</summary>
    public string Planes { get; init; } = "both";

    /// <summary>Выполнять проверки по моменту (8.1.35).</summary>
    public bool CheckMoment { get; init; } = true;

    /// <summary>Длина приопорной зоны проверки момента, м; 0 — 2·h0.</summary>
    public double MomentZoneLength { get; init; }

    /// <summary>Координаты обрывов продольной арматуры вдоль элемента, м.</summary>
    public double[] BarCutoffs { get; init; } = [];

    /// <summary>Коэффициент включения продольной арматуры k.</summary>
    public double AnchorageFactor { get; init; } = 1.0;

    /// <summary>
    /// Пользователь подтвердил соблюдение конструктивных требований 10.3.
    /// Пока не подтверждено, поперечная арматура в расчёт не включается (qsw = 0).
    /// </summary>
    public bool ConstructiveRequirements103Confirmed { get; init; }

    /// <summary>Переопределения для плоскости Vy.</summary>
    public ShearInclinedOverrides? OverridesVy { get; init; }

    /// <summary>Переопределения для плоскости Vx.</summary>
    public ShearInclinedOverrides? OverridesVx { get; init; }

    static readonly JsonSerializerOptions JsonOpts = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    /// <summary>Сериализует параметры в JSON.</summary>
    public string ToJson() => JsonSerializer.Serialize(this, JsonOpts);

    /// <summary>Разбирает параметры из JSON; пустая строка даёт значения по умолчанию.</summary>
    public static ShearInclinedParams Parse(string json)
    {
        if (string.IsNullOrWhiteSpace(json) || json.Trim() == "{}")
            return new ShearInclinedParams();
        return JsonSerializer.Deserialize<ShearInclinedParams>(json, JsonOpts)
            ?? new ShearInclinedParams();
    }

    /// <summary>Знак направления к опоре: +1, −1 или 0 для автоматического перебора.</summary>
    public int DirectionSign() => SupportDirection switch
    {
        "forward" => 1,
        "backward" => -1,
        _ => 0
    };

    /// <summary>Тип элемента для выбора режима φn.</summary>
    public ElementKind ResolveElementKind() =>
        ElementKind == "other" ? CScore.Sp63Shear.ElementKind.Other
                               : CScore.Sp63Shear.ElementKind.BendingUnstressed;
}
