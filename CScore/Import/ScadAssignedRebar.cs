using System.Text.Json;

namespace CScore.Import;

/// <summary>
/// Группа заданного армирования пластин SCAD (<c>ApiArmPlate</c>). S1..S4 — как у подбора: низ X1, верх X1,
/// низ Y1, верх Y1. Нулевой диаметр или шаг — арматуры нет; флаги «нет верхней/нижней/поперечной» при чтении
/// уже учтены (соответствующие диаметры обнулены).
/// </summary>
/// <param name="Num">Номер группы.</param>
/// <param name="Name">Имя группы (может быть пустым).</param>
/// <param name="ElementIds">Номера КЭ группы.</param>
/// <param name="DiametersMm">Диаметры S1..S4, мм.</param>
/// <param name="StepsM">Шаги S1..S4, м.</param>
/// <param name="TransverseDiameterMm">Диаметр поперечной арматуры, мм.</param>
/// <param name="TransverseStepXM">Шаг поперечной по X1, м.</param>
/// <param name="TransverseStepYM">Шаг поперечной по Y1, м.</param>
public sealed record ScadAssignedPlate(int Num, string Name, int[] ElementIds, int[] DiametersMm, double[] StepsM,
    int TransverseDiameterMm, double TransverseStepXM, double TransverseStepYM)
{
    /// <summary>Площадь S1..S4 (индекс 0..3), см²/м; 0 — арматуры нет.</summary>
    public double Area(int i) => ScadAssignedRebarMath.PerMeter(DiametersMm[i], StepsM[i]);

    /// <summary>Поперечная арматура, см²/м²; 0 — нет.</summary>
    public double TransverseArea =>
        TransverseDiameterMm > 0 && TransverseStepXM > 0 && TransverseStepYM > 0
            ? ScadAssignedRebarMath.BarAreaCm2(TransverseDiameterMm) / (TransverseStepXM * TransverseStepYM)
            : 0;

    /// <summary>В группе нет ни продольной, ни поперечной арматуры.</summary>
    public bool IsEmpty => Enumerable.Range(0, 4).All(i => Area(i) == 0) && TransverseArea == 0;
}

/// <summary>Стержни одного диаметра: число и диаметр.</summary>
/// <param name="Count">Число стержней.</param>
/// <param name="DiameterMm">Диаметр, мм.</param>
public sealed record ScadBarSet(int Count, int DiameterMm)
{
    /// <summary>Площадь набора, см².</summary>
    public double AreaCm2 => Count > 0 && DiameterMm > 0 ? Count * ScadAssignedRebarMath.BarAreaCm2(DiameterMm) : 0;
}

/// <summary>
/// Грань S1 или S2 участка стержня: первый ряд (до двух диаметров) и второй ряд.
/// </summary>
/// <param name="First">Первый ряд, первый диаметр.</param>
/// <param name="Second">Первый ряд, второй диаметр; null — нет.</param>
/// <param name="Row2">Второй ряд; null — нет.</param>
/// <param name="Row2DeltaM">Расстояние между рядами, м.</param>
public sealed record ScadRodFace(ScadBarSet First, ScadBarSet? Second, ScadBarSet? Row2, double Row2DeltaM)
{
    /// <summary>Площадь грани, см².</summary>
    public double AreaCm2 => First.AreaCm2 + (Second?.AreaCm2 ?? 0) + (Row2?.AreaCm2 ?? 0);
}

/// <summary>
/// Поперечная арматура участка стержня в одной плоскости: диаметр, число срезов, шаг.
/// </summary>
/// <param name="DiameterMm">Диаметр, мм.</param>
/// <param name="Legs">Число срезов (ветвей).</param>
/// <param name="StepM">Шаг, м.</param>
public sealed record ScadRodStirrups(int DiameterMm, int Legs, double StepM)
{
    /// <summary>Площадь на метр длины, см²/м; 0 — нет.</summary>
    public double AreaPerMeter => Legs > 0 && DiameterMm > 0 && StepM > 0
        ? Legs * ScadAssignedRebarMath.BarAreaCm2(DiameterMm) / StepM : 0;
}

/// <summary>
/// Участок заданного армирования стержня SCAD (<c>ApiArmElemRod</c>). S1 — грань −Z1, S2 — +Z1, S3 — −Y1,
/// S4 — +Y1 (как у подбора, §9.3 спеки). Поперечная «в плоскости Z» — ветви ‖ Z1 (как IWz подбора).
/// </summary>
/// <param name="PartNo">Номер участка.</param>
/// <param name="LengthPercent">Длина участка, % длины КЭ.</param>
/// <param name="S1">Грань S1.</param>
/// <param name="S2">Грань S2.</param>
/// <param name="S3">Грань S3; null — нет.</param>
/// <param name="S4">Грань S4; null — нет.</param>
/// <param name="StirrupsZ">Поперечная в плоскости Z; null — нет.</param>
/// <param name="StirrupsY">Поперечная в плоскости Y; null — нет.</param>
public sealed record ScadAssignedRodPart(int PartNo, double LengthPercent, ScadRodFace S1, ScadRodFace S2,
    ScadBarSet? S3, ScadBarSet? S4, ScadRodStirrups? StirrupsZ, ScadRodStirrups? StirrupsY)
{
    /// <summary>Суммарная продольная, см².</summary>
    public double LongitudinalSum => S1.AreaCm2 + S2.AreaCm2 + (S3?.AreaCm2 ?? 0) + (S4?.AreaCm2 ?? 0);
}

/// <summary>Группа заданного армирования стержней SCAD (<c>ApiArmRod</c>): участки по длине КЭ.</summary>
/// <param name="Num">Номер группы.</param>
/// <param name="Name">Имя группы (может быть пустым).</param>
/// <param name="ElementIds">Номера КЭ группы.</param>
/// <param name="Parts">Участки от начального узла к конечному.</param>
public sealed record ScadAssignedRod(int Num, string Name, int[] ElementIds, ScadAssignedRodPart[] Parts)
{
    /// <summary>
    /// Участок в сечении <paramref name="sectionNum"/> (1..<paramref name="sectionCount"/>, сечения равномерно
    /// от начального узла к конечному). На границе участков — участок с меньшей продольной (в запас).
    /// </summary>
    public ScadAssignedRodPart? PartAt(int sectionNum, int sectionCount)
    {
        if (Parts.Length <= 1) return Parts.FirstOrDefault();
        if (sectionCount < 2) return Weakest;
        double t = 100.0 * Math.Clamp(sectionNum - 1, 0, sectionCount - 1) / (sectionCount - 1);
        double total = Parts.Sum(p => p.LengthPercent);
        if (total > 0) t *= total / 100;  // участки могут не давать в сумме ровно 100 %
        const double tol = 1e-6;
        double start = 0;
        ScadAssignedRodPart? found = null;
        foreach (var p in Parts)
        {
            double end = start + p.LengthPercent;
            if (t >= start - tol && t <= end + tol && (found == null || p.LongitudinalSum < found.LongitudinalSum))
                found = p;
            start = end;
        }
        return found ?? Parts[^1];
    }

    /// <summary>Участок с наименьшей продольной — для расчёта без номера сечения.</summary>
    public ScadAssignedRodPart? Weakest => Parts.Length == 0 ? null : Parts.MinBy(p => p.LongitudinalSum);
}

/// <summary>Площади стержней.</summary>
public static class ScadAssignedRebarMath
{
    /// <summary>Площадь одного стержня, см².</summary>
    public static double BarAreaCm2(int diameterMm) => Math.PI * diameterMm * diameterMm / 4 / 100;

    /// <summary>Площадь на метр при шаге, см²/м; 0 — нет диаметра или шага.</summary>
    public static double PerMeter(int diameterMm, double stepM) =>
        diameterMm > 0 && stepM > 0 ? BarAreaCm2(diameterMm) / stepM : 0;
}

/// <summary>
/// Заданное армирование SCAD схемы: группы пластин и стержней, хранение (JSON вложения схемы) и поиск группы КЭ.
/// КЭ, входящий в несколько групп, относится к группе с меньшим номером.
/// </summary>
public sealed class ScadAssignedRebarFile
{
    static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = false };

    readonly Dictionary<int, ScadAssignedPlate> _plateByElement = [];
    readonly Dictionary<int, ScadAssignedRod> _rodByElement = [];

    /// <summary>Индекс по группам.</summary>
    public ScadAssignedRebarFile(IEnumerable<ScadAssignedPlate> plates, IEnumerable<ScadAssignedRod> rods)
    {
        Plates = plates.OrderBy(g => g.Num).ToList();
        Rods = rods.OrderBy(g => g.Num).ToList();
        var multi = new HashSet<int>();
        foreach (var g in Plates)
            foreach (int id in g.ElementIds)
                if (!_plateByElement.TryAdd(id, g)) multi.Add(id);
        foreach (var g in Rods)
            foreach (int id in g.ElementIds)
                if (!_rodByElement.TryAdd(id, g)) multi.Add(id);
        MultiGroupElements = multi.Count;
    }

    /// <summary>Группы пластин по возрастанию номера.</summary>
    public IReadOnlyList<ScadAssignedPlate> Plates { get; }

    /// <summary>Группы стержней по возрастанию номера.</summary>
    public IReadOnlyList<ScadAssignedRod> Rods { get; }

    /// <summary>Число КЭ, входящих в несколько групп.</summary>
    public int MultiGroupElements { get; }

    /// <summary>Групп нет.</summary>
    public bool IsEmpty => Plates.Count == 0 && Rods.Count == 0;

    /// <summary>Группа пластины КЭ; null — КЭ ни в одной.</summary>
    public ScadAssignedPlate? Plate(int elementId) => _plateByElement.GetValueOrDefault(elementId);

    /// <summary>Группа стержня КЭ; null — КЭ ни в одной.</summary>
    public ScadAssignedRod? Rod(int elementId) => _rodByElement.GetValueOrDefault(elementId);

    /// <summary>Сериализация для хранения.</summary>
    public string ToJson() => JsonSerializer.Serialize(new Stored(Plates.ToList(), Rods.ToList()), JsonOptions);

    /// <summary>Заданное армирование из JSON хранения.</summary>
    /// <exception cref="InvalidDataException">JSON повреждён.</exception>
    public static ScadAssignedRebarFile FromJson(string json)
    {
        try
        {
            var s = JsonSerializer.Deserialize<Stored>(json, JsonOptions);
            return new ScadAssignedRebarFile(s?.Plates ?? [], s?.Rods ?? []);
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException($"Заданное армирование SCAD схемы повреждено: {ex.Message}", ex);
        }
    }

    sealed record Stored(List<ScadAssignedPlate> Plates, List<ScadAssignedRod> Rods);
}
