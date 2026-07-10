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

/// <summary>Жёсткость/материал SCAD (блок "(3/...)"). Name — из "Name &quot;...&quot;", может быть null.</summary>
public record ScadStiffnessRecord(int Id, string? Name, ScadStiffnessKind Kind);

/// <summary>Именованная группа элементов SCAD (блок "(47/...)", код выборки "2" — элементы).</summary>
public record ScadGroupRecord(string Name, int[] ElementIds);

/// <summary>Контейнер сырых данных расчётной схемы SCAD после разбора текстового формата.</summary>
public class ScadSchemaData
{
    public List<ScadNodeRecord>      Nodes       { get; } = [];
    public List<ScadElementRecord>   Elements    { get; } = [];
    public List<ScadStiffnessRecord> Stiffnesses { get; } = [];
    public List<ScadGroupRecord>     Groups      { get; } = [];
}
