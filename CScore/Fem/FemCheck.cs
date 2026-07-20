using System.Collections.Generic;
using System.Text.Json;

namespace CScore.Fem;

/// <summary>Нормативная проверка конструктивного элемента по МКЭ-пайплайну.</summary>
public class FemCheck
{
    public int     Id               { get; set; }
    public int     SchemaId         { get; set; }
    public int     MemberId         { get; set; }
    /// <summary>FK → fem_members.id. Заполнено вместо MemberId, когда проверка нацелена на
    /// одиночный конструктивный элемент, а не на группу. Ровно одно из двух заполнено
    /// (MemberId=0 когда используется ElementId, т.к. member_id остаётся NOT NULL).</summary>
    public int?    ElementId        { get; set; }
    /// <summary>Код: "steel_check" | "rc_check" | "rc_plate_check"</summary>
    public string  NormCode         { get; set; } = "steel_check";
    /// <summary>JSON-параметры проверки. Null = брать из FemMember.DesignParamsJson.</summary>
    public string? ParamsJson        { get; set; }
    /// <summary>FK → calc_results.id.</summary>
    public int?    ResultId          { get; set; }
    /// <summary>Имя проверки.</summary>
    public string  Tag               { get; set; } = "";
    /// <summary>"[]" = все наборы элемента; "[1,5,12]" = конкретные.</summary>
    public string  ForceSetIdsJson   { get; set; } = "[]";
    /// <summary>null = авто из тега набора; "C"/"CL"/"N"/"NL" = принудительно.</summary>
    public string? CalcTypeOverride  { get; set; }

    public bool IsAllSets => ForceSetIdsJson == "[]" || ForceSetIdsJson == "null" || string.IsNullOrEmpty(ForceSetIdsJson);

    public IReadOnlyList<int> GetForceSetIds()
    {
        if (IsAllSets) return [];
        try { return JsonSerializer.Deserialize<int[]>(ForceSetIdsJson) ?? []; }
        catch { return []; }
    }

    public string DisplayTag => string.IsNullOrEmpty(Tag) ? $"{NormCode} #{Id}" : Tag;

    /// <summary>true, если проверка нацелена на одиночный конструктивный элемент (ElementId), а не на группу.</summary>
    public bool TargetsElement => ElementId is > 0;
}
