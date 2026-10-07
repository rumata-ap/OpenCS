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

/// <summary>Связь конечной жёсткости SCAD (КЭ 51): пружина узел — земля в общих осях.</summary>
/// <param name="ElemId">Номер КЭ.</param>
/// <param name="StiffnessId">Номер жёсткости КЭ.</param>
/// <param name="Node">Узел.</param>
/// <param name="K">Жёсткости X Y Z (Н/м), UX UY UZ (Н·м/рад); 0 — связи нет.</param>
public sealed record ScadSpring(int ElemId, int StiffnessId, int Node, double[] K);

/// <summary>Шарниры стержня SCAD (ApiGetJoint): маски освобождённых связей концов в местных осях (биты 0–5 — X…UZ).</summary>
public sealed record ScadJoint(int ElemId, int MaskI, int MaskJ);

/// <summary>Виды ГУ SCAD, которые не переносятся (ключи <see cref="ScadAnalysisModel.NotTransferred"/>).</summary>
public static class ScadNotTransferredKinds
{
    /// <summary>Упругие связи двух узлов (КЭ 55), число КЭ.</summary>
    public const string Fe55 = "fe55";
    /// <summary>Объединения перемещений, число групп.</summary>
    public const string BoundUnite = "bound_unite";
    /// <summary>Упругие шарниры, число концов стержней.</summary>
    public const string ElasticJoint = "elastic_joint";
    /// <summary>Жёсткие вставки, число КЭ.</summary>
    public const string Insert = "insert";
    /// <summary>Упругое основание, число КЭ.</summary>
    public const string Bed = "bed";
    /// <summary>Жёсткости КЭ 51, которые не удалось разобрать, число КЭ.</summary>
    public const string SpringUnparsed = "spring_unparsed";
}

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

    /// <summary>Признак схемы SCAD (ApiGetTypeSystem): 5 — общего вида; 0 — не прочитан (старое вложение).</summary>
    public int SchemaType { get; init; }

    /// <summary>Связи конечной жёсткости (КЭ 51).</summary>
    public List<ScadSpring> Springs { get; init; } = [];

    /// <summary>Шарниры стержней (только концы с ненулевой маской, без упругих).</summary>
    public List<ScadJoint> Joints { get; init; } = [];

    /// <summary>Что в SCAD есть, но не переносится: вид (<see cref="ScadNotTransferredKinds"/>) → число объектов.</summary>
    public Dictionary<string, int> NotTransferred { get; init; } = [];

    /// <summary>
    /// Вложение прочитано с пружинами и шарнирами (срез 4б); false — старое, где их нет, и ГУ надо дочитать из .SPR.
    /// </summary>
    public bool HasBoundaryV2 { get; init; }

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

    /// <summary>
    /// Жёсткости КЭ 51 из строки «SPRING k1 … Type 51»: числа (в СИ, как отдаёт SCADAPIX) идут по степеням свободы
    /// признака схемы, хвостовые нули опускаются. Результат — 6 жёсткостей X Y Z UX UY UZ; null — не та строка или
    /// чисел больше, чем степеней свободы признака.
    /// </summary>
    public static double[]? SpringStiffness(string? stiffnessText, int schemaType)
    {
        if (string.IsNullOrWhiteSpace(stiffnessText)) return null;
        var parts = stiffnessText.Split([' ', '\t', '\r', '\n'], StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 2 || !parts[0].Equals("SPRING", StringComparison.OrdinalIgnoreCase)) return null;
        var dofs = SchemaDofs(schemaType);
        var k = new double[6];
        int count = 0;
        for (int i = 1; i < parts.Length; i++)
        {
            if (parts[i].Equals("Type", StringComparison.OrdinalIgnoreCase))
            {
                if (i + 1 >= parts.Length || parts[i + 1] != "51") return null;
                break;
            }
            if (!double.TryParse(parts[i].Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out double v))
                return null;
            if (count >= dofs.Length) return null;
            k[dofs[count++]] = v;
        }
        return count == 0 ? null : k;
    }

    /// <summary>
    /// Степени свободы узла по признаку схемы SCAD (справка «Признак схемы»): 1 — X Z, 2 — X Z UY, 3 — Z UX UY,
    /// 4 — X Y Z, 5 и многослойные 8, 9 — X Y Z UX UY UZ (дополнительные DOF многослойных схем здесь не учитываются).
    /// Неизвестный признак — как 5.
    /// </summary>
    public static int[] SchemaDofs(int schemaType) => schemaType switch
    {
        1 => [0, 2],
        2 => [0, 2, 4],
        3 => [2, 3, 4],
        4 => [0, 1, 2],
        _ => [0, 1, 2, 3, 4, 5],
    };
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
