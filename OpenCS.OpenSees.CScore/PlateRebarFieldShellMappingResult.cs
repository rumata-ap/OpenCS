using CScore.Fem;
using OpenCS.OpenSees.Model;

namespace OpenCS.OpenSees.CScore;

/// <summary>Результат разрешения PlateRebarField для сетки shell-элементов: уникальные
/// секции + индекс элемент→tag секции + диагностика резолвера (с ElementId) и маппера.</summary>
public sealed record PlateRebarFieldShellMappingResult(
    IReadOnlyList<RCShellLayeredSection> Sections,
    IReadOnlyList<NativeShellMaterialDefinition> Materials,
    IReadOnlyDictionary<int, int> ElementSectionTag,
    IReadOnlyList<(int ElementId, FemValidationDiagnostic Diagnostic)> RebarDiagnostics,
    IReadOnlyList<string> MappingDiagnostics);
