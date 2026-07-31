using CScore;
using OpenCS.OpenSees.Model;

namespace OpenCS.OpenSees.CScore;

/// <summary>Production-реализация IPlateSectionShellMaterialResolver — разрешает material id в
/// нелинейные native shell-материалы через NativeShellMaterialMapper. Не завязан на WPF/SQLite:
/// источник материалов передаётся вызывающей стороной через lookupMaterial (например,
/// AppViewModel.Materials в будущем UI-слое, вне объёма этого среза).</summary>
public sealed class PlateSectionShellMaterialResolver(
    Func<int, Material?> lookupMaterial,
    CalcType calc,
    SteelModelKind steelModel,
    double? steelHardeningRatioOverride)
    : IPlateSectionShellMaterialResolver
{
    /// <inheritdoc />
    public IReadOnlyList<NativeShellMaterialDefinition> ResolveConcrete(int sourceMaterialId)
    {
        MaterialChars chars = ResolveChars(sourceMaterialId);
        return NativeShellMaterialMapper.MapConcrete(chars, $"concrete:{sourceMaterialId}");
    }

    /// <inheritdoc />
    public IReadOnlyList<NativeShellMaterialDefinition> ResolveRebar(int sourceMaterialId)
    {
        Material material = LookupOrThrow(sourceMaterialId);
        MaterialChars chars = ResolveChars(material);
        return NativeShellMaterialMapper.MapRebar(
            chars, material.Type, steelModel, steelHardeningRatioOverride, $"rebar:{sourceMaterialId}");
    }

    private MaterialChars ResolveChars(int sourceMaterialId) => ResolveChars(LookupOrThrow(sourceMaterialId));

    private MaterialChars ResolveChars(Material material) =>
        material.GetChars(calc) ?? throw new CScoreMappingException(
            $"Материал {material.Id}: нет характеристик для расчёта {calc}.");

    private Material LookupOrThrow(int sourceMaterialId) =>
        lookupMaterial(sourceMaterialId) ?? throw new CScoreMappingException(
            $"Материал {sourceMaterialId} не найден.");
}
