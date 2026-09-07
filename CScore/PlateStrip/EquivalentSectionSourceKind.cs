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

    /// <summary>Нелинейный (не замороженный) PlateSection: касательная и усилия берутся в
    /// текущем состоянии (Срез 7). Значение добавлено в конец: и персистентность
    /// (DatabaseService, Enum.TryParse), и отпечаток (EquivalentSectionFingerprint:50) работают
    /// по имени, поэтому добавление безопасно, а порядок сохраняется как общая конвенция.</summary>
    PlateSectionLive,
}
