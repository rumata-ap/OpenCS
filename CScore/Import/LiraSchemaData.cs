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

/// <summary>
/// Жёсткость стержня ЛираСАПР.
/// Id — порядковый номер жёсткости (колонка «Тип» в CSV, совпадает с «Номер жёсткости» в таблице элементов).
/// </summary>
public record LiraBarStiffnessRecord(
    int    Id,
    string Name,
    /// <summary>EF — жёсткость на растяжение/сжатие, т.</summary>
    double EF,
    double EIy,
    double EIz,
    double GIk
);

/// <summary>
/// Жёсткость пластины ЛираСАПР.
/// Id — порядковый номер жёсткости (колонка «Тип» в CSV).
/// </summary>
public record LiraPlateStiffnessRecord(
    int    Id,
    string Name,
    double E,
    double V12,
    double H_mm
);

/// <summary>
/// Жёсткость ЛИРЫ из таблицы схемы «Жёсткости» (API): параметры — строкой «ключ:значение» в том виде,
/// как их отдаёт ЛИРА («Ro:2.5 E:917745 B:20 H:40 … BAR_END»). Форма сечения разбирается при
/// использовании (<see cref="LiraStiffnessParams"/>).
/// </summary>
/// <param name="Id">Номер жёсткости в схеме.</param>
/// <param name="KindCode">Код вида жёсткости ЛИРЫ (0 — брус, 36 — пластина …).</param>
/// <param name="Name">Имя жёсткости.</param>
/// <param name="Params">Параметры строкой ЛИРЫ.</param>
/// <param name="SectionUnitM">Единица размеров сечения документа ЛИРЫ в метрах (см → 0,01).</param>
public sealed record LiraStiffnessRecord(int Id, int KindCode, string Name, string Params, double SectionUnitM);

/// <summary>Конструктивный блок ЛираСАПР (таблица 31).</summary>
public record LiraConstructiveBlockRecord(
    int      Id,
    /// <summary>Вд КоБ — тип блока (СТЕНА, КОЛОННА, БАЛКА и т.д.).</summary>
    string   Type,
    /// <summary>Этаж.</summary>
    string   Floor,
    /// <summary>Марка.</summary>
    string   Mark,
    /// <summary>Комментарий.</summary>
    string   Comment,
    /// <summary>Номера КЭ, входящих в блок (развёрнутые из диапазонов).</summary>
    int[]    ElementIds
);

/// <summary>Контейнер сырых данных расчётной схемы ЛираСАПР после CSV-парсинга.</summary>
public class LiraSchemaData
{
    public List<LiraNodeRecord>               Nodes              { get; } = [];
    public List<LiraElementRecord>            Elements           { get; } = [];
    public List<LiraBarStiffnessRecord>       BarStiffnesses     { get; } = [];
    public List<LiraPlateStiffnessRecord>     PlateStiffnesses   { get; } = [];
    public List<LiraConstructiveBlockRecord>  ConstructiveBlocks { get; } = [];

    /// <summary>Жёсткости схемы из таблицы 9 «Жёсткости» (API). Пусто — таблица не прочитана.</summary>
    public List<LiraStiffnessRecord>          Stiffnesses        { get; } = [];

    /// <summary>
    /// ТЗА (типы заданного армирования) КЭ из таблицы 33 «Элементы - ТЗА» (только ЛИРА-САПФИР 2025+):
    /// номер КЭ → номера ТЗА (фон и усиления). КЭ без ТЗА не попадают.
    /// </summary>
    public Dictionary<int, int[]>             ElementReinforcementTypes { get; } = [];

    /// <summary>
    /// Углы согласования местных осей пластин из таблицы 18 «местные оси пластин»: номер КЭ → угол поворота
    /// оси X1 от направления «узел 1 → узел 2» вокруг Z1, град. Пусто — таблица не прочитана.
    /// </summary>
    public Dictionary<int, double>            PlateAxisAngles { get; } = [];

    /// <summary>
    /// Применить таблицы API «Жёсткости» и «Элементы - жёсткости»: элементы получают номер жёсткости
    /// (таблица элементов API его не содержит), а списки жёсткостей стержней и пластин — имена и толщины,
    /// если они не заполнены CSV-импортом.
    /// </summary>
    /// <param name="elementStiffness">Номер жёсткости по номеру КЭ.</param>
    public void ApplyStiffnesses(IReadOnlyDictionary<int, int> elementStiffness)
    {
        for (int i = 0; i < Elements.Count; i++)
            if (elementStiffness.TryGetValue(Elements[i].Id, out int num) && num > 0)
                Elements[i] = Elements[i] with { StiffnessId = num };

        if (BarStiffnesses.Count == 0)
            foreach (var s in Stiffnesses.Where(LiraStiffnessParams.IsBar))
                BarStiffnesses.Add(new LiraBarStiffnessRecord(s.Id, s.Name,
                    LiraStiffnessParams.Value(s.Params, "EF") ?? 0, LiraStiffnessParams.Value(s.Params, "EIy") ?? 0,
                    LiraStiffnessParams.Value(s.Params, "EIz") ?? 0, LiraStiffnessParams.Value(s.Params, "GIk") ?? 0));
        if (PlateStiffnesses.Count == 0)
            foreach (var s in Stiffnesses.Where(LiraStiffnessParams.IsPlate))
                PlateStiffnesses.Add(new LiraPlateStiffnessRecord(s.Id, s.Name,
                    LiraStiffnessParams.Value(s.Params, "E") ?? 0, LiraStiffnessParams.Value(s.Params, "V") ?? 0,
                    (LiraStiffnessParams.PlateThicknessM(s) ?? 0) * 1000));
    }
}
