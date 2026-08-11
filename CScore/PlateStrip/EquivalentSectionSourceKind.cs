namespace CScore.PlateStrip;

/// <summary>Источник линейного отклика плитного сечения.</summary>
public enum EquivalentSectionSourceKind
{
    /// <summary>Постоянные блоки A/B/D/As, доступные для программных фикстур.</summary>
    ConstantLinear,

    /// <summary>Касательная PlateSection, замороженная в нулевом состоянии.</summary>
    PlateSectionTangentSnapshot,

    /// <summary>RVE-гомогенизация через реальный OpenSees.exe (Срез 3b) — только для
    /// widthSources контрольной проверки, никогда не EquivalentSection.SourceKind.</summary>
    ShellMeshOpenSees,

    /// <summary>RVE-гомогенизация через реальный CSfea.Core.ShellMesh (Срез 3b) — только для
    /// widthSources контрольной проверки, никогда не EquivalentSection.SourceKind.</summary>
    ShellMeshCsfea,
}
