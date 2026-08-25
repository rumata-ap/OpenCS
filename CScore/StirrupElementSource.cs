using System.Text.Json.Serialization;

namespace CScore;

/// <summary>Способ построения элемента поперечного армирования.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<StirrupElementKind>))]
public enum StirrupElementKind
{
    /// <summary>Замкнутый хомут по оффсету контура области-носителя.</summary>
    OffsetLoop,
    /// <summary>Открытый срез-стержень по линии, обрезанной оффсетной линией.</summary>
    Cut,
    /// <summary>Геометрия задана напрямую, параметры построения неизвестны.</summary>
    Manual
}

/// <summary>Направление линии среза-стержня.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<StirrupCutDirection>))]
public enum StirrupCutDirection { Vertical, Horizontal, TwoPoints }

/// <summary>
/// Параметры, по которым был построен элемент поперечного армирования.
/// Источник истины для расчёта — материализованные координаты центровой линии.
/// </summary>
public sealed class StirrupElementSource
{
    /// <summary>Версия контракта сериализации.</summary>
    public int Version { get; set; } = 1;
    /// <summary>Способ построения.</summary>
    public StirrupElementKind Kind { get; set; } = StirrupElementKind.Manual;
    /// <summary>Бетонная область, от контура которой строился оффсет.</summary>
    public int? AnchorAreaId { get; set; }
    /// <summary>Базовый отступ элемента, м.</summary>
    public double? OffsetM { get; set; }
    /// <summary>Пофасадные отступы замкнутого хомута, м.</summary>
    public double[]? EdgeOffsets { get; set; }
    /// <summary>Направление среза.</summary>
    public StirrupCutDirection? Direction { get; set; }
    /// <summary>Координата линии среза, м.</summary>
    public double? Position { get; set; }
    /// <summary>Координаты первой точки произвольного среза, м.</summary>
    public double? P1X { get; set; }
    public double? P1Y { get; set; }
    /// <summary>Координаты второй точки произвольного среза, м.</summary>
    public double? P2X { get; set; }
    public double? P2Y { get; set; }
    /// <summary>Смещение копии, м.</summary>
    public double? Dx { get; set; }
    public double? Dy { get; set; }
    /// <summary>Индекс исходного элемента для копии.</summary>
    public int? BaseIndex { get; set; }

    /// <summary>Создаёт глубокую копию параметров.</summary>
    public StirrupElementSource Clone() => new()
    {
        Version = Version,
        Kind = Kind,
        AnchorAreaId = AnchorAreaId,
        OffsetM = OffsetM,
        EdgeOffsets = EdgeOffsets?.ToArray(),
        Direction = Direction,
        Position = Position,
        P1X = P1X,
        P1Y = P1Y,
        P2X = P2X,
        P2Y = P2Y,
        Dx = Dx,
        Dy = Dy,
        BaseIndex = BaseIndex
    };
}
