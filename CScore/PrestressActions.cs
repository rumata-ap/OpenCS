using System.Text.Json.Serialization;

namespace CScore;

/// <summary>Набор интегральных действий преднапряжения в единицах OpenCS.</summary>
public sealed class PrestressAction
{
    /// <summary>Продольная сила N, кН.</summary>
    public double N { get; init; }

    /// <summary>Момент Mx = ∫σ·y·dA, кН·м.</summary>
    public double Mx { get; init; }

    /// <summary>Момент My = ∫σ·x·dA, кН·м.</summary>
    public double My { get; init; }
}

/// <summary>Результат действий преднапряжения одной группы точечных фибр.</summary>
public sealed class PrestressGroupActions
{
    /// <summary>Идентификатор области группы.</summary>
    public int AreaId { get; init; }

    /// <summary>Метка группы.</summary>
    public string Tag { get; init; } = "";

    /// <summary>Суммарная площадь точечных фибр, м².</summary>
    public double AreaM2 { get; init; }

    /// <summary>Центр площади группы, м.</summary>
    public XY Centroid { get; init; } = new();

    /// <summary>Заданное напряжение преднапряжения SigSp, МПа.</summary>
    public double SigSp { get; init; }

    /// <summary>Коэффициент точности преднапряжения GammaSp.</summary>
    public double GammaSp { get; init; }

    /// <summary>Номинальные действия по SigSp.</summary>
    public PrestressAction Nominal { get; init; } = new();

    /// <summary>Эффективные действия по SigSp·GammaSp.</summary>
    public PrestressAction Effective { get; init; } = new();
}

/// <summary>Интегральный результат действий всех преднапряжённых групп.</summary>
public sealed class PrestressActionsResult
{
    /// <summary>Точка, относительно которой вычислены моменты, м.</summary>
    public XY ReferencePoint { get; init; } = new();

    /// <summary>Результаты по отдельным группам.</summary>
    public IReadOnlyList<PrestressGroupActions> Groups { get; init; } = [];

    /// <summary>Суммарные номинальные действия по SigSp.</summary>
    public PrestressAction Nominal { get; init; } = new();

    /// <summary>Суммарные эффективные действия по SigSp·GammaSp.</summary>
    public PrestressAction Effective { get; init; } = new();

    /// <summary>Есть ли в результате хотя бы одна преднапряжённая группа.</summary>
    public bool HasPrestressedGroups => Groups.Count > 0;
}

/// <summary>JSON-представление точки отсчёта с единицами в именах полей.</summary>
public sealed class PrestressActionsJsonPoint
{
    /// <summary>Координата X, м.</summary>
    [JsonPropertyName("x_m")] public double X { get; init; }

    /// <summary>Координата Y, м.</summary>
    [JsonPropertyName("y_m")] public double Y { get; init; }
}

/// <summary>JSON-представление трёх интегральных действий.</summary>
public sealed class PrestressActionsJsonVector
{
    /// <summary>Продольная сила, кН.</summary>
    [JsonPropertyName("N_kN")] public double N { get; init; }

    /// <summary>Момент Mx, кН·м.</summary>
    [JsonPropertyName("Mx_kNm")] public double Mx { get; init; }

    /// <summary>Момент My, кН·м.</summary>
    [JsonPropertyName("My_kNm")] public double My { get; init; }

    /// <summary>Строит JSON-вектор из доменного результата.</summary>
    public static PrestressActionsJsonVector From(PrestressAction source) => new()
    {
        N = source.N,
        Mx = source.Mx,
        My = source.My,
    };
}

/// <summary>JSON-представление действий одной группы.</summary>
public sealed class PrestressActionsJsonGroup
{
    /// <summary>Идентификатор области.</summary>
    [JsonPropertyName("areaId")] public int AreaId { get; init; }

    /// <summary>Метка группы.</summary>
    [JsonPropertyName("tag")] public string Tag { get; init; } = "";

    /// <summary>Площадь группы, м².</summary>
    [JsonPropertyName("area_m2")] public double AreaM2 { get; init; }

    /// <summary>Координата X центра группы, м.</summary>
    [JsonPropertyName("x_m")] public double X { get; init; }

    /// <summary>Координата Y центра группы, м.</summary>
    [JsonPropertyName("y_m")] public double Y { get; init; }

    /// <summary>Заданное напряжение, МПа.</summary>
    [JsonPropertyName("sigSp_MPa")] public double SigSp { get; init; }

    /// <summary>Коэффициент точности преднапряжения.</summary>
    [JsonPropertyName("gammaSp")] public double GammaSp { get; init; }

    /// <summary>Номинальные действия группы.</summary>
    [JsonPropertyName("nominal")] public PrestressActionsJsonVector Nominal { get; init; } = new();

    /// <summary>Эффективные действия группы.</summary>
    [JsonPropertyName("effective")] public PrestressActionsJsonVector Effective { get; init; } = new();
}

/// <summary>Стабильное JSON-представление результата действий преднапряжения.</summary>
public sealed class PrestressActionsJsonModel
{
    /// <summary>Точка отсчёта моментов.</summary>
    [JsonPropertyName("reference")] public PrestressActionsJsonPoint Reference { get; init; } = new();

    /// <summary>Суммарные номинальные действия.</summary>
    [JsonPropertyName("nominal")] public PrestressActionsJsonVector Nominal { get; init; } = new();

    /// <summary>Суммарные эффективные действия.</summary>
    [JsonPropertyName("effective")] public PrestressActionsJsonVector Effective { get; init; } = new();

    /// <summary>Результаты по группам.</summary>
    [JsonPropertyName("groups")] public IReadOnlyList<PrestressActionsJsonGroup> Groups { get; init; } = [];

    /// <summary>Преобразует доменный результат в стабильную JSON-модель.</summary>
    public static PrestressActionsJsonModel From(PrestressActionsResult source) => new()
    {
        Reference = new PrestressActionsJsonPoint
        {
            X = source.ReferencePoint.X,
            Y = source.ReferencePoint.Y,
        },
        Nominal = PrestressActionsJsonVector.From(source.Nominal),
        Effective = PrestressActionsJsonVector.From(source.Effective),
        Groups = source.Groups.Select(group => new PrestressActionsJsonGroup
        {
            AreaId = group.AreaId,
            Tag = group.Tag,
            AreaM2 = group.AreaM2,
            X = group.Centroid.X,
            Y = group.Centroid.Y,
            SigSp = group.SigSp,
            GammaSp = group.GammaSp,
            Nominal = PrestressActionsJsonVector.From(group.Nominal),
            Effective = PrestressActionsJsonVector.From(group.Effective),
        }).ToArray(),
    };
}

/// <summary>Внутренний расчётчик интегральных действий преднапряжения.</summary>
internal static class PrestressActionsCalculator
{
    /// <summary>Вычисляет действия по группам точечных фибр.</summary>
    public static PrestressActionsResult Calculate(CrossSection section, XY? referencePoint)
    {
        var groupData = section.Areas
            .Where(area => area.Category == AreaCategory.RebarGroup && area.SigSp != 0)
            .Select(CreateGroupData)
            .Where(data => data != null)
            .Select(data => data!)
            .ToArray();

        XY reference;
        if (referencePoint != null)
        {
            reference = referencePoint.Clone();
        }
        else
        {
            var props = new GeoProps(section);
            if (props.Centroid == null || props.EA <= 0)
            {
                if (groupData.Length > 0)
                    throw new InvalidOperationException(
                        "Невозможно определить приведённый центр сечения для действий преднапряжения.");

                reference = new XY();
            }
            else
            {
                reference = props.Centroid.Clone();
            }
        }

        var groups = groupData.Select(data => BuildGroupActions(data, reference)).ToArray();
        return new PrestressActionsResult
        {
            ReferencePoint = reference,
            Groups = groups,
            Nominal = Sum(groups.Select(group => group.Nominal)),
            Effective = Sum(groups.Select(group => group.Effective)),
        };
    }

    static GroupData? CreateGroupData(MaterialArea area)
    {
        var fibers = area.Fibers.Where(fiber => fiber.TypeFiber == FiberType.point).ToArray();
        double areaM2 = fibers.Sum(fiber => fiber.Area);
        if (areaM2 <= 0)
            return null;

        return new GroupData(
            area,
            areaM2,
            fibers.Sum(fiber => fiber.Area * fiber.X) / areaM2,
            fibers.Sum(fiber => fiber.Area * fiber.Y) / areaM2);
    }

    static PrestressGroupActions BuildGroupActions(GroupData data, XY reference)
    {
        double nominalN = data.Area.SigSp * 1000.0 * data.AreaM2;
        double effectiveN = nominalN * data.Area.GammaSp;
        var centroid = new XY(data.X, data.Y);

        return new PrestressGroupActions
        {
            AreaId = data.Area.Id,
            Tag = data.Area.Tag,
            AreaM2 = data.AreaM2,
            Centroid = centroid,
            SigSp = data.Area.SigSp,
            GammaSp = data.Area.GammaSp,
            Nominal = new PrestressAction
            {
                N = nominalN,
                Mx = nominalN * (data.Y - reference.Y),
                My = nominalN * (data.X - reference.X),
            },
            Effective = new PrestressAction
            {
                N = effectiveN,
                Mx = effectiveN * (data.Y - reference.Y),
                My = effectiveN * (data.X - reference.X),
            },
        };
    }

    static PrestressAction Sum(IEnumerable<PrestressAction> actions) => new()
    {
        N = actions.Sum(action => action.N),
        Mx = actions.Sum(action => action.Mx),
        My = actions.Sum(action => action.My),
    };

    sealed record GroupData(MaterialArea Area, double AreaM2, double X, double Y);
}
