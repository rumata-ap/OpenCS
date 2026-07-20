namespace CScore.Fem;

/// <summary>Общий контракт цели нормативной проверки (FemCheck): группа конструктивных
/// элементов (FemMemberGroup) или одиночный конструктивный элемент (FemMember).</summary>
public interface IFemCheckable
{
    /// <summary>Отображаемое имя цели (используется в сообщениях об ошибках, CalcTask.Tag).</summary>
    string Tag { get; }

    /// <summary>JSON-параметры проекта (FemDesignParams) для этой цели, если заданы напрямую.</summary>
    string? DesignParamsJson { get; }
}
