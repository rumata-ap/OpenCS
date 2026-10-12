namespace CScore.Import;

/// <summary>
/// Единицы текстового файла ЛИРЫ (документ 0, строка 33: <c>M 1 CM 100 T 1 C 1</c>). Пары «имя коэффициент» по
/// порядку: длины, размеры сечений, силы, температура; коэффициент — число единиц в базовой (м, м, т, °C), как
/// <c>CM 100</c>. На образцах встречалось только <c>M 1 CM 100 T 1 C 1</c>.
/// </summary>
/// <param name="Length">Единица длин (координат, нагрузок на длину/площадь).</param>
/// <param name="LengthPerMeter">Единиц длины в метре.</param>
/// <param name="Section">Единица размеров сечений.</param>
/// <param name="SectionPerMeter">Единиц размеров сечений в метре.</param>
/// <param name="Force">Единица сил.</param>
/// <param name="ForcePerTonne">Единиц силы в тонне-силе.</param>
/// <param name="Temperature">Единица температуры.</param>
/// <param name="TemperatureFactor">Коэффициент температуры.</param>
public sealed record LiraTextUnits(
    string Length, double LengthPerMeter,
    string Section, double SectionPerMeter,
    string Force, double ForcePerTonne,
    string Temperature, double TemperatureFactor)
{
    /// <summary>Базовые единицы ЛИРЫ — м, см, т, °C (строки 33 нет).</summary>
    public static LiraTextUnits Default { get; } = new("M", 1, "CM", 100, "T", 1, "C", 1);

    /// <summary>Единица длины, м.</summary>
    public double LengthUnitM => 1.0 / LengthPerMeter;

    /// <summary>Единица размеров сечений, м.</summary>
    public double SectionUnitM => 1.0 / SectionPerMeter;

    /// <summary>Единица силы, т.</summary>
    public double ForceUnitT => 1.0 / ForcePerTonne;

    /// <summary>
    /// Единица силы, Н: KN, N, DaN, MN — точно; тонна и прочие — через коэффициент строки 33 и кН в тонне-силе.
    /// </summary>
    /// <param name="tonToKn">Сколько кН в тонне-силе (настройка импорта).</param>
    public double ForceUnitN(double tonToKn) => Force.ToUpperInvariant() switch
    {
        "N" => 1,
        "DAN" => 10,
        "KN" => 1e3,
        "MN" => 1e6,
        _ => ForceUnitT * tonToKn * 1e3,
    };
}

/// <summary>Конечный элемент документа 1.</summary>
/// <param name="Number">Номер КЭ — порядковый номер записи документа 1, с 1.</param>
/// <param name="Type">Тип КЭ ЛИРЫ (10, 44, 56, 57, 244…).</param>
/// <param name="Stiffness">Номер жёсткости документа 3.</param>
/// <param name="Nodes">Узлы в порядке файла (с продолжениями записи).</param>
public sealed record LiraTextElement(int Number, int Type, int Stiffness, int[] Nodes);

/// <summary>Жёсткость документа 3: строки записи как есть.</summary>
/// <param name="Number">Номер жёсткости.</param>
/// <param name="Lines">Первая строка без номера (<c>GEI 3.06e+06 0.2 0.25 RO 2.5</c>,
/// <c>459000 8262 1434.37 …</c>) и строки продолжения без ведущего 0 (<c>RO 0.375</c>).</param>
public sealed record LiraTextStiffness(int Number, IReadOnlyList<string> Lines)
{
    /// <summary>
    /// Числа первой строки, если она вся из чисел (численные жёсткости: стержни <c>EF EIy EIz GIk …</c>,
    /// КЭ 57 <c>N Rx Ry Rz Rux Ruy Ruz</c>); иначе null.
    /// </summary>
    public double[]? NumericValues => LiraTextTokens.TryNumbers(Lines.Count > 0 ? Lines[0] : "");
}

/// <summary>
/// Шарнир документа 2: освобождение связей конца стержня в его местных осях (1–3 — линейные, 4–6 — повороты).
/// </summary>
/// <param name="Element">Номер КЭ.</param>
/// <param name="End">Конец: 1 — начало, 2 — конец.</param>
/// <param name="Dofs">Освобождённые DOF (в образцах — один на запись).</param>
/// <param name="Value">Последнее поле записи (0; на ЗАГС 30 и 200 — вероятно, жёсткость упругого шарнира).</param>
public sealed record LiraTextHinge(int Element, int End, int[] Dofs, double Value);

/// <summary>Связь документа 5: запрещённые перемещения узла в общих осях.</summary>
/// <param name="Node">Узел.</param>
/// <param name="Dofs">DOF 1–6 (X, Y, Z, UX, UY, UZ).</param>
public sealed record LiraTextSupport(int Node, int[] Dofs);

/// <summary>
/// Объединение перемещений документа 5: строка <c>узел DOF…</c>, за которой идут строки только с номерами узлов.
/// Перемещения <see cref="Dofs"/> всех узлов группы равны (не жёсткое тело).
/// </summary>
/// <param name="Head">Первый узел группы.</param>
/// <param name="Dofs">Объединяемые DOF 1–6.</param>
/// <param name="Nodes">Остальные узлы группы.</param>
public sealed record LiraTextCoupling(int Head, int[] Dofs, int[] Nodes);

/// <summary>Абсолютно жёсткое тело документа 25: <c>ведущий ведомые… код</c>.</summary>
/// <param name="Master">Ведущий узел.</param>
/// <param name="Slaves">Ведомые узлы.</param>
/// <param name="Code">Завершающий отрицательный код (−1 — все DOF; −2 встречен на FRAME_rev04); null — записи без
/// кода (dorfman_lin).</param>
public sealed record LiraTextRigidBody(int Master, int[] Slaves, int? Code);

/// <summary>Нагрузка документа 6: <c>объект вид направление №строки_док.7 загружение</c>.</summary>
/// <param name="Target">Узел (вид 0) или КЭ.</param>
/// <param name="Kind">Вид нагрузки (0 — узловая; 5/6/7 — местные оси; 15/16/17 — общие оси…).</param>
/// <param name="Direction">Направление: 1–3 — силы вдоль осей, 4–6 — моменты вокруг них.</param>
/// <param name="ValueRow">Номер строки значений документа 7.</param>
/// <param name="LoadCase">Загружение (у записи без него — как у предыдущей).</param>
public readonly record struct LiraTextLoad(int Target, int Kind, int Direction, int ValueRow, int LoadCase);

/// <summary>Координаты узла документа 4 в единицах длины файла.</summary>
public readonly record struct LiraTextPoint(double X, double Y, double Z);

/// <summary>Документ, который читатель не разбирает (РСУ, углы, вставки, описания сечений в фигурных скобках).</summary>
/// <param name="Number">Номер документа.</param>
/// <param name="Braced">Документ в фигурных скобках (<c>{13/ … }</c>).</param>
/// <param name="Records">Число непустых записей.</param>
public sealed record LiraTextSkippedDocument(int Number, bool Braced, int Records);

/// <summary>
/// Текстовый файл схемы ЛИРЫ (<c>Data\&lt;Title&gt;.txt</c>), разобранный <see cref="LiraTextReader"/>: всё в единицах
/// и знаках файла (нагрузки положительны против осей), без переноса в схему OpenCS. Формат — план
/// <c>2026-10-12-csfea-slice4zh-lira-bc-loads.md</c>, раздел 1.
/// </summary>
public sealed class LiraTextSchema
{
    /// <summary>Имя схемы (документ 0, строка 1).</summary>
    public string Title { get; internal set; } = "";

    /// <summary>Признак схемы (документ 0, строка 2; 5 — пространственная, 6 DOF в узле); null — не задан.</summary>
    public int? SchemaKind { get; internal set; }

    /// <summary>Единицы (документ 0, строка 33).</summary>
    public LiraTextUnits Units { get; internal set; } = LiraTextUnits.Default;

    /// <summary>Имена загружений по номерам (документ 0, строка 39).</summary>
    public Dictionary<int, string> LoadCaseNames { get; } = [];

    /// <summary>Строки документа 0 как есть (номер → текст без «N;» и концевой «/»).</summary>
    public Dictionary<int, string> HeaderRows { get; } = [];

    /// <summary>КЭ документа 1 по порядку (индекс = номер − 1).</summary>
    public List<LiraTextElement> Elements { get; } = [];

    /// <summary>Шарниры документа 2.</summary>
    public List<LiraTextHinge> Hinges { get; } = [];

    /// <summary>Жёсткости документа 3.</summary>
    public List<LiraTextStiffness> Stiffnesses { get; } = [];

    /// <summary>Координаты узлов документа 4 (индекс = номер узла − 1).</summary>
    public List<LiraTextPoint> Nodes { get; } = [];

    /// <summary>Связи документа 5.</summary>
    public List<LiraTextSupport> Supports { get; } = [];

    /// <summary>Объединения перемещений документа 5.</summary>
    public List<LiraTextCoupling> Couplings { get; } = [];

    /// <summary>Нагрузки документа 6.</summary>
    public List<LiraTextLoad> Loads { get; } = [];

    /// <summary>Строки значений нагрузок документа 7 (номер → значения с продолжениями).</summary>
    public Dictionary<int, double[]> LoadValues { get; } = [];

    /// <summary>Углы документа 17 по номерам КЭ (записи «КЭ угол»; у стержней — разворот местных осей, град).</summary>
    public Dictionary<int, double> ElementAngles { get; } = [];

    /// <summary>
    /// Углы документа 9 по номерам пластин — согласование местных осей (оси выдачи усилий): поворот X1 от направления
    /// «узел 1 → узел 2» вокруг Z1, град (= таблица 18 API).
    /// </summary>
    public Dictionary<int, double> PlateAxisAngles { get; } = [];

    /// <summary>
    /// Описания сечений документа <c>{13/ … }</c> по номеру жёсткости: строки «ключ = значение» блока <c>[N]</c> через два
    /// пробела, как строка стальной жёсткости API (<c>Section = Tubing  MatId = STL  File  = |…|  Shape = |…|</c>).
    /// </summary>
    public Dictionary<int, string> SectionDescriptions { get; } = [];

    /// <summary>
    /// Упругое основание документа 19 по номерам КЭ: величины записи после номера — у пластин <c>C1 C2 C2 …</c> (в единицах
    /// документа 0, сила/длина³ и сила/длина; «Мирная»: C1 = таблица 5 API), у стержней — свой состав (ширина, C1, C2 …).
    /// </summary>
    public Dictionary<int, double[]> Foundation { get; } = [];

    /// <summary>Абсолютно жёсткие тела документа 25.</summary>
    public List<LiraTextRigidBody> RigidBodies { get; } = [];

    /// <summary>Неразобранные документы в порядке файла.</summary>
    public List<LiraTextSkippedDocument> SkippedDocuments { get; } = [];

    /// <summary>Записи, которые не удалось разобрать, и прочие замечания (не более сотни, дальше — счётчик).</summary>
    public List<string> Warnings { get; } = [];

    /// <summary>Сколько замечаний не попало в <see cref="Warnings"/> сверх предела.</summary>
    public int SuppressedWarnings { get; internal set; }

    internal const int WarningLimit = 100;

    internal void Warn(string message)
    {
        if (Warnings.Count < WarningLimit) Warnings.Add(message);
        else SuppressedWarnings++;
    }
}
