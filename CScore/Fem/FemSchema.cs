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

    /// <summary>Группы конструктивных элементов схемы. Заполняются при загрузке из БД.</summary>
    public ObservableCollection<FemMemberGroup> MemberGroups { get; } = [];

    /// <summary>Загружения схемы.</summary>
    public ObservableCollection<FemLoadCase> LoadCases { get; } = [];

    /// <summary>Постановки расчёта схемы.</summary>
    public ObservableCollection<FemAnalysis> Analyses { get; } = [];
}
