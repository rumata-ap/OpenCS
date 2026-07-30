using OpenCS.OpenSees.Model;

namespace OpenCS.OpenSees.CScore;

/// <summary>Результат преобразования нескольких PlateSection в native shell snapshot одним
/// разделяемым регистром материалов (см. PlateSectionOpenSeesMapper.MapMany).</summary>
public sealed record PlateSectionShellMappingResultBatch(
    IReadOnlyList<RCShellLayeredSection> Sections,
    IReadOnlyList<NativeShellMaterialDefinition> Materials,
    IReadOnlyList<string> Diagnostics);
