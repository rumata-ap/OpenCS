namespace CScore.Import;

/// <summary>Узел расчётной схемы SCAD (текстовый формат, блок "(4/...)").</summary>
public record ScadNodeRecord(int Id, double X, double Y, double Z);

/// <summary>
/// Конечный элемент расчётной схемы SCAD (блок "(1/...)").
/// Id — канонический номер элемента SCAD: позиция записи в блоке (1) считая ВСЕ записи подряд,
/// включая пропущенные при импорте (пружины, жёсткие вставки и т.п.) — иначе нумерация
/// расходится с диапазонами элементов в именованных группах (блок "(47/...)").
/// </summary>
public record ScadElementRecord(int Id, int TypeCode, int StiffnessId, int[] NodeIds);

/// <summary>Категория жёсткости SCAD по ключевому слову записи блока "(3/...)".</summary>
public enum ScadStiffnessKind { Bar, Shell, Other }

/// <summary>Жёсткость/материал SCAD (блок "(3/...)" txt или ApiGetRigid). Name — из "Name &quot;...&quot;"
/// (txt) или ApiGetRigidName, может быть null.
/// ThicknessM — толщина оболочки из записи GE/GEI (м), null если не распознана.
/// Text — исходная строка жёсткости SCAD без номера (null — запись создана не разбором).
/// BarRect — брус S0 «E B H», м (B ‖ Y1, H ‖ Z1); null — не стержень или сечение не «брус».</summary>
public record ScadStiffnessRecord(int Id, string? Name, ScadStiffnessKind Kind, double? ThicknessM = null,
    string? Text = null, LiraBarRect? BarRect = null);

/// <summary>Именованная группа элементов SCAD (блок "(47/...)", код выборки "2" — элементы;
/// группа КЭ или блок SCADAPIX.dll).</summary>
public record ScadGroupRecord(string Name, int[] ElementIds);

/// <summary>ЖБ-группа SCAD (ApiConcreteElem, ApiGetNameConcrete).</summary>
/// <param name="Num">Номер группы.</param>
/// <param name="Name">Имя группы (может быть пустым).</param>
/// <param name="Module">Тип конструктивного элемента SCAD (поле Modul).</param>
/// <param name="RangeM">Расстояния до центров тяжести арматуры a1..a4, м.</param>
/// <param name="ConcreteClass">Класс бетона.</param>
/// <param name="LongitudinalRebarClass">Класс продольной арматуры.</param>
/// <param name="TransverseRebarClass">Класс поперечной арматуры.</param>
/// <param name="CrackResisting">Признак трещиностойкости.</param>
/// <param name="CrackWidthMm">Предельная ширина раскрытия трещин (непродолжительная, продолжительная), мм.</param>
/// <param name="ElementIds">Номера КЭ группы.</param>
public record ScadConcreteGroup(int Num, string Name, int Module, double[] RangeM, string ConcreteClass,
    string LongitudinalRebarClass, string TransverseRebarClass, bool CrackResisting, double[] CrackWidthMm,
    int[] ElementIds);

/// <summary>Контейнер сырых данных расчётной схемы SCAD (txt-экспорт или SCADAPIX.dll).</summary>
public class ScadSchemaData
{
    public List<ScadNodeRecord>      Nodes       { get; } = [];
    public List<ScadElementRecord>   Elements    { get; } = [];
    public List<ScadStiffnessRecord> Stiffnesses { get; } = [];
    public List<ScadGroupRecord>     Groups      { get; } = [];

    /// <summary>Блоки SCAD (пересекаются с группами). Пусто — не читались (txt).</summary>
    public List<ScadGroupRecord>     Blocks      { get; } = [];

    /// <summary>ЖБ-группы SCAD. Пусто — не читались (txt).</summary>
    public List<ScadConcreteGroup>   ConcreteGroups { get; } = [];

    /// <summary>Заданное армирование SCAD. Null — не читалось (txt).</summary>
    public ScadAssignedRebarFile?    AssignedRebar { get; set; }

    /// <summary>
    /// Углы согласования осей выдачи усилий пластин: номер КЭ → угол X1 от «узел 1 → узел 2»
    /// вокруг нормали, град. КЭ нет в словаре — угол неизвестен.
    /// </summary>
    public Dictionary<int, double>   PlateAxisAngles { get; } = [];

    /// <summary>Единица длины проекта в метрах (txt — 1).</summary>
    public double LengthUnitM  { get; set; } = 1;

    /// <summary>Единица размеров сечений стержней проекта в метрах (txt — 1).</summary>
    public double SectionUnitM { get; set; } = 1;
}
