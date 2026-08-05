namespace CScore.PlateStrip;

/// <summary>Источник линейного отклика плитного сечения.</summary>
public enum EquivalentSectionSourceKind
{
    /// <summary>Постоянные блоки A/B/D/As, доступные для программных фикстур.</summary>
    ConstantLinear,

    /// <summary>Касательная PlateSection, замороженная в нулевом состоянии.</summary>
    PlateSectionTangentSnapshot
}
