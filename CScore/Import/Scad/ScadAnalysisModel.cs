using System.Globalization;
using System.Text.Json;

namespace CScore.Import;

/// <summary>
/// Абсолютно жёсткое тело SCAD (КЭ типа 100): главный узел — первый в списке узлов КЭ, остальные подчинены ему.
/// </summary>
/// <param name="ElemId">Номер КЭ.</param>
/// <param name="StiffnessId">Номер жёсткости КЭ.</param>
/// <param name="MasterNode">Главный узел.</param>
/// <param name="SlaveNodes">Подчинённые узлы.</param>
/// <param name="Mask">Связанные степени свободы: биты 0–5 — X, Y, Z, UX, UY, UZ.</param>
public sealed record ScadRigidBody(int ElemId, int StiffnessId, int MasterNode, int[] SlaveNodes, int Mask);

/// <summary>
/// Нагрузка загружения SCAD как её отдаёт SCADAPIX (ApiGetForceNode/Elem/Area): код вида Qw, направление Qn,
/// числа (в единицах проекта) и список узлов/КЭ.
/// </summary>
public sealed record ScadLoadRecord(int Qw, int Qn, double[] Data, int[] Ids);

/// <summary>Загружение SCAD: нагрузки на узлы, КЭ и площадные.</summary>
public sealed record ScadLoadCase(int Num, string Name, ScadLoadRecord[] NodeLoads, ScadLoadRecord[] ElementLoads,
    ScadLoadRecord[] AreaLoads);

/// <summary>
/// Расчётная модель SCAD сверх сетки: закрепления узлов, абсолютно жёсткие тела, нагрузки загружений.
/// Хранится вложением схемы (JSON).
/// </summary>
public sealed class ScadAnalysisModel
{
    static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = false };

    /// <summary>Код вида нагрузки «собственный вес» (Data[0] — коэффициент; пробник 03.10).</summary>
    public const int SelfWeightQw = 96;
    /// <summary>Код вида «равномерно распределённая нагрузка на пластину» (Data[0] — сила/длина²; пробник 03.10).</summary>
    public const int PlatePressureQw = 16;

    /// <summary>Закрепления: номер узла → маска (биты 0–5 — X, Y, Z, UX, UY, UZ); только ненулевые.</summary>
    public Dictionary<int, int> Bounds { get; init; } = [];

    /// <summary>Абсолютно жёсткие тела.</summary>
    public List<ScadRigidBody> RigidBodies { get; init; } = [];

    /// <summary>Загружения по возрастанию номера.</summary>
    public List<ScadLoadCase> LoadCases { get; init; } = [];

    /// <summary>Единица длины проекта, м.</summary>
    public double LengthUnitM { get; init; } = 1;

    /// <summary>Единица силы проекта, Н (1 т = 9,81 кН, как в SCAD).</summary>
    public double ForceUnitN { get; init; } = 1;

    /// <summary>Сериализация для хранения.</summary>
    public string ToJson() => JsonSerializer.Serialize(this, JsonOptions);

    /// <summary>Модель из JSON хранения.</summary>
    /// <exception cref="InvalidDataException">JSON повреждён.</exception>
    public static ScadAnalysisModel FromJson(string json)
    {
        try
        {
            return JsonSerializer.Deserialize<ScadAnalysisModel>(json, JsonOptions)
                ?? throw new InvalidDataException("Расчётная модель SCAD схемы пуста.");
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException($"Расчётная модель SCAD схемы повреждена: {ex.Message}", ex);
        }
    }

    /// <summary>
    /// Маска связей жёсткого тела из строки его жёсткости «SPRING f1 … f6 Type 100»: f = 1 — степень свободы
    /// связана. Не та строка — все шесть (абсолютно жёсткое тело по умолчанию).
    /// </summary>
    public static int RigidBodyMask(string? stiffnessText)
    {
        const int all = 0b111111;
        if (string.IsNullOrWhiteSpace(stiffnessText)) return all;
        var parts = stiffnessText.Split([' ', '\t', '\r', '\n'], StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 7 || !parts[0].Equals("SPRING", StringComparison.OrdinalIgnoreCase)) return all;
        int mask = 0;
        for (int i = 0; i < 6; i++)
        {
            if (!double.TryParse(parts[i + 1].Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out double f))
                return all;
            if (f != 0) mask |= 1 << i;
        }
        return mask;
    }
}

/// <summary>Строка результатов SCAD: загружение, номер строки, имя, шаг нелинейного процесса.</summary>
/// <param name="Nodes">Номера узлов (порядок совпадает с <paramref name="Values"/>).</param>
/// <param name="Values">По 6 чисел на узел: X, Y, Z (м), UX, UY, UZ (рад).</param>
public sealed record ScadDisplacementRow(int Load, int Row, string Name, int Step, int[] Nodes, double[] Values)
{
    /// <summary>Перемещения узла; null — узла нет в строке.</summary>
    public double[]? Of(int node)
    {
        int i = Array.IndexOf(Nodes, node);
        return i < 0 ? null : Values.AsSpan(6 * i, 6).ToArray();
    }
}

/// <summary>Перемещения узлов из результатов SCAD (вложение схемы, JSON).</summary>
public sealed class ScadDisplacementSet
{
    static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = false };

    /// <summary>Строки результатов по загружениям и шагам.</summary>
    public List<ScadDisplacementRow> Rows { get; init; } = [];

    /// <summary>Сериализация для хранения.</summary>
    public string ToJson() => JsonSerializer.Serialize(this, JsonOptions);

    /// <summary>Набор из JSON хранения.</summary>
    /// <exception cref="InvalidDataException">JSON повреждён.</exception>
    public static ScadDisplacementSet FromJson(string json)
    {
        try
        {
            return JsonSerializer.Deserialize<ScadDisplacementSet>(json, JsonOptions)
                ?? throw new InvalidDataException("Перемещения SCAD схемы пусты.");
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException($"Перемещения SCAD схемы повреждены: {ex.Message}", ex);
        }
    }
}
