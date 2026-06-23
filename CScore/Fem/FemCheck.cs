namespace CScore.Fem;

/// <summary>Нормативная проверка конструктивного элемента по МКЭ-пайплайну.</summary>
public class FemCheck
{
    public int     Id         { get; set; }
    public int     SchemaId   { get; set; }
    public int     MemberId   { get; set; }
    /// <summary>Код нормативной проверки: "steel_check" | будущие "rc_check" | ...</summary>
    public string  NormCode   { get; set; } = "steel_check";
    /// <summary>Переопределение параметров проверки. Null = брать из FemMember.DesignParamsJson.</summary>
    public string? ParamsJson { get; set; }
    /// <summary>FK → calc_results.id. Результат последнего запуска.</summary>
    public int?    ResultId   { get; set; }

    public string DisplayTag => $"{NormCode} #{Id}";
}
