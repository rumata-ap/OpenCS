namespace CScore.Fem.Fields;

/// <summary>Вид цели поля.</summary>
public enum FemFieldTargetKind { Shell, MeshNode }

/// <summary>Назначаемая величина: код, вид цели, компоненты, допустимы ли отрицательные значения.</summary>
public sealed record FemFieldQuantity(string Code, FemFieldTargetKind Target, IReadOnlyList<string> Components, bool AllowNegative);

/// <summary>Единица ввода: подпись и множитель к СИ.</summary>
public sealed record FemFieldUnit(string Label, double ToSi);

/// <summary>
/// Реестр величин полей. Сейчас C1 пластин (Н/м³) и пружины узлов сетки по DOF (Н/м, Н·м/рад). Будущие величины
/// (C1 по направлениям, C2, давление, узловые силы с загружением, температура, заданные смещения) добавляются записями
/// реестра без смены хранения.
/// </summary>
public static class FemFieldQuantities
{
    public const string FoundationC1 = "foundation.c1";
    public const string Spring = "spring";
    public static readonly string[] SpringComponents = ["kx", "ky", "kz", "kux", "kuy", "kuz"];

    public static IReadOnlyList<FemFieldQuantity> All { get; } =
    [
        new(FoundationC1, FemFieldTargetKind.Shell, ["c1"], AllowNegative: false),
        new(Spring, FemFieldTargetKind.MeshNode, SpringComponents, AllowNegative: false),
    ];

    public static FemFieldQuantity Get(string code) =>
        All.FirstOrDefault(q => q.Code == code) ?? throw new ArgumentException($"Неизвестная величина поля «{code}».", nameof(code));

    /// <summary>Единицы ввода компоненты; <paramref name="tonToKn"/> — кН в тонне-силе (настройка импорта).</summary>
    public static IReadOnlyList<FemFieldUnit> Units(string code, string component, double tonToKn)
    {
        double tf = tonToKn * 1000;   // Н в тонне-силе
        if (code == FoundationC1) return [new("тс/м³", tf), new("кН/м³", 1e3), new("МН/м³", 1e6), new("Н/м³", 1)];
        return component is "kux" or "kuy" or "kuz"
            ? [new("тс·м/рад", tf), new("кН·м/рад", 1e3), new("МН·м/рад", 1e6), new("Н·м/рад", 1)]
            : [new("тс/м", tf), new("тс/см", tf * 100), new("кН/м", 1e3), new("МН/м", 1e6), new("Н/м", 1)];
    }
}
