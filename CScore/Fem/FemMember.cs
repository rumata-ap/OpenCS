using System.Collections.ObjectModel;

namespace CScore.Fem;

/// <summary>Конструктивный элемент — контейнер для одного или нескольких КЭ.</summary>
public class FemMember
{
    public int     Id               { get; set; }
    public int     SchemaId         { get; set; }
    public string  Tag              { get; set; } = "";
    /// <summary>Тип: "column" | "beam" | "brace" | "other"</summary>
    public string? MemberType       { get; set; }
    /// <summary>JSON-массив ID конечных элементов: [e1, e2, ...].</summary>
    public string  ElemIdsJson      { get; set; } = "[]";
    /// <summary>FK → cross_sections.id. Сечение для нормативных проверок.</summary>
    public int?    CrossSectionId   { get; set; }
    /// <summary>FK → force_sets.id. Набор усилий (source_type='fea').</summary>
    public int?    ForceSetId       { get; set; }
    /// <summary>JSON-сериализация FemDesignParams (l₀, μ, βm, γM).</summary>
    public string? DesignParamsJson { get; set; }
    /// <summary>Проверки, привязанные к этому элементу (eager-loaded).</summary>
    public ObservableCollection<FemCheck> Checks { get; } = [];
}
