using System;
using System.Collections.ObjectModel;

namespace CScore.Fem;

/// <summary>МКЭ-расчётная схема. Контейнер для узлов, КЭ, конструктивных элементов и загружений.</summary>
public class FemSchema
{
    public int    Id         { get; set; }
    public string Tag        { get; set; } = "";
    /// <summary>Источник схемы: "lira" | "robot" | "rfem" | "opensees" | "internal"</summary>
    public string SourceType { get; set; } = "internal";
    public string Created    { get; set; } = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");

    /// <summary>Конструктивные элементы схемы. Заполняются при загрузке из БД.</summary>
    public ObservableCollection<FemMember> Members { get; } = [];
}
