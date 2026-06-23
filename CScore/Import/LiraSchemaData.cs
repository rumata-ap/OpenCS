namespace CScore.Import;

/// <summary>Узел расчётной схемы ЛираСАПР (из CSV-экспорта).</summary>
public record LiraNodeRecord(
    int    Id,
    double X,
    double Y,
    double Z,
    /// <summary>Маска закреплённых DOF (биты 0–5: Tx Ty Tz Rx Ry Rz, бит 6 — W-кручение).</summary>
    int    DofMask
);

/// <summary>Конечный элемент расчётной схемы ЛираСАПР (из CSV-экспорта).</summary>
public record LiraElementRecord(
    int   Id,
    /// <summary>Тип КЭ Лиры (10 = стержень, 4x = пластина и т.д.).</summary>
    int   FeType,
    int   SectionCount,
    int   StiffnessId,
    int[] NodeIds
);

/// <summary>Жёсткость стержня ЛираСАПР (из CSV-экспорта).</summary>
public record LiraBarStiffnessRecord(
    int    Id,
    int    TypeCode,
    string Name,
    /// <summary>EF — жёсткость на растяжение/сжатие, т.</summary>
    double EF,
    double EIy,
    double EIz,
    double GIk
);

/// <summary>Жёсткость пластины ЛираСАПР (из CSV-экспорта).</summary>
public record LiraPlateStiffnessRecord(
    int    Id,
    int    TypeCode,
    string Name,
    double E,
    double V12,
    double H_mm
);

/// <summary>Контейнер сырых данных расчётной схемы ЛираСАПР после CSV-парсинга.</summary>
public class LiraSchemaData
{
    public List<LiraNodeRecord>           Nodes            { get; } = [];
    public List<LiraElementRecord>        Elements         { get; } = [];
    public List<LiraBarStiffnessRecord>   BarStiffnesses   { get; } = [];
    public List<LiraPlateStiffnessRecord> PlateStiffnesses { get; } = [];
}
