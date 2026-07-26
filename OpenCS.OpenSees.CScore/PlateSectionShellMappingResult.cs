using OpenCS.OpenSees.Model;

namespace OpenCS.OpenSees.CScore;

/// <summary>Результат преобразования PlateSection в native shell snapshot.</summary>
public sealed record PlateSectionShellMappingResult(
    RCShellLayeredSection Section,
    IReadOnlyList<NativeShellMaterialDefinition> Materials,
    IReadOnlyList<string> Diagnostics);
