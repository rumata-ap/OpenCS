using System.Text.Json;

namespace CScore.Import;

/// <summary>
/// Единицы характеристик материалов документа ЛИРЫ (LiraMeasurementUnits API: MaterialProperties1 — сила,
/// MaterialProperties2 — длина). В них заданы E и Ro жёсткостей (сила/длина², сила/длина³) и численные жёсткости
/// стержней (EF — сила, EI и GIk — сила·длина²). Хранятся при схеме (вложение <c>lira_units</c>): без них свойства КЭ
/// ЛИРЫ не пересчитать в СИ. Размеры сечений — в <see cref="LiraStiffnessRecord.SectionUnitM"/>.
/// </summary>
/// <param name="ForceUnitN">Единица силы, Н.</param>
/// <param name="LengthUnitM">Единица длины, м.</param>
public sealed record LiraUnits(double ForceUnitN, double LengthUnitM)
{
    static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = false };

    /// <summary>Единицы по умолчанию — тонна-сила и метр (как на «Мирной» и в шаблонах ЛИРЫ).</summary>
    /// <param name="tonToKn">Сколько кН в тонне-силе (настройка импорта).</param>
    public static LiraUnits Default(double tonToKn) => new(tonToKn * 1000, 1.0);

    /// <summary>Единицы по кодам MaterialProperties1/2; неизвестный код — по умолчанию (т, м).</summary>
    public static LiraUnits FromCodes(int force, int length, double tonToKn) =>
        new(LiraApiUnits.ForceToKnOf(force, tonToKn, tonToKn) * 1000, LiraApiUnits.LengthToM(length, 1.0));

    /// <summary>Модуль упругости (сила/длина²) → Па.</summary>
    public double Stress(double v) => v * ForceUnitN / (LengthUnitM * LengthUnitM);

    /// <summary>Удельный вес (сила/длина³) → Н/м³.</summary>
    public double UnitWeight(double v) => v * ForceUnitN / (LengthUnitM * LengthUnitM * LengthUnitM);

    /// <summary>Погонный вес (сила/длина) → Н/м.</summary>
    public double PerLength(double v) => v * ForceUnitN / LengthUnitM;

    /// <summary>Сила → Н.</summary>
    public double Force(double v) => v * ForceUnitN;

    /// <summary>Жёсткость на изгиб или кручение (сила·длина²) → Н·м².</summary>
    public double Rigidity(double v) => v * ForceUnitN * LengthUnitM * LengthUnitM;

    /// <summary>JSON хранения.</summary>
    public string ToJson() => JsonSerializer.Serialize(this, JsonOptions);

    /// <summary>Единицы из JSON хранения.</summary>
    /// <exception cref="InvalidDataException">JSON повреждён или единицы не положительны.</exception>
    public static LiraUnits FromJson(string json)
    {
        LiraUnits? units;
        try { units = JsonSerializer.Deserialize<LiraUnits>(json, JsonOptions); }
        catch (JsonException ex) { throw new InvalidDataException("Единицы ЛИРЫ схемы повреждены: " + ex.Message, ex); }
        if (units is not { ForceUnitN: > 0, LengthUnitM: > 0 })
            throw new InvalidDataException("Единицы ЛИРЫ схемы повреждены: сила и длина должны быть положительны.");
        return units;
    }
}
