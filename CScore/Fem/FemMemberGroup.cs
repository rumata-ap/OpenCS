using System.Collections.ObjectModel;
using System.ComponentModel;

namespace CScore.Fem;

/// <summary>Группа конструктивных элементов (FemMember) — единица нормативной проверки и импорта усилий.
/// Сечение и GJ-стратегия у неё больше нет: они назначаются напрямую каждому FemMember.
/// Tag и MemberType уведомляют об изменении — их показывает дерево проекта при переименовании.</summary>
public class FemMemberGroup : IFemCheckable, INotifyPropertyChanged
{
    string  _tag = "";
    string? _memberType;

    public event PropertyChangedEventHandler? PropertyChanged;

    public int     Id               { get; set; }
    public int     SchemaId         { get; set; }
    public string  Tag
    {
        get => _tag;
        set { if (_tag == value) return; _tag = value; PropertyChanged?.Invoke(this, new(nameof(Tag))); }
    }
    /// <summary>Тип: "column" | "beam" | "brace" | "other"</summary>
    public string? MemberType
    {
        get => _memberType;
        set { if (_memberType == value) return; _memberType = value; PropertyChanged?.Invoke(this, new(nameof(MemberType))); }
    }
    /// <summary>JSON-массив тегов конструктивных элементов (FemMember.ElemTag), входящих в группу: [t1, t2, ...].</summary>
    public string  MemberTagsJson   { get; set; } = "[]";
    /// <summary>FK → plate_sections.id. Сечение для нормативных проверок (пластины/стены) — оболочки вне рамок этого среза.</summary>
    public int?    PlateSectionId   { get; set; }
    /// <summary>FK → force_sets.id. Набор усилий (source_type='fea').</summary>
    public int?    ForceSetId       { get; set; }
    /// <summary>JSON-сериализация FemDesignParams (l₀, μ, βm, γM).</summary>
    public string? DesignParamsJson { get; set; }
    /// <summary>Проверки, привязанные к этой группе (eager-loaded).</summary>
    public ObservableCollection<FemCheck> Checks { get; } = [];
}
