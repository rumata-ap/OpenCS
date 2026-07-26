using OpenCS.OpenSees.Model;

namespace OpenCS.OpenSees.CScore;

/// <summary>Разрешает исходные material ids в native shell-compatible materials.</summary>
public interface IPlateSectionShellMaterialResolver
{
    /// <summary>Разрешает material id бетона.</summary>
    NativeShellMaterialDefinition ResolveConcrete(int sourceMaterialId);

    /// <summary>Разрешает material id арматуры.</summary>
    NativeShellMaterialDefinition ResolveRebar(int sourceMaterialId);
}
