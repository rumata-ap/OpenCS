namespace OpenCS.Models;

/// <summary>Сохранённый параметрический источник стального сечения и отпечаток сформированной геометрии.</summary>
public sealed record ParametricSteelSectionRecord(int SectionId, int DefinitionVersion,
    int GeneratorVersion, string DefinitionJson, string GeneratedFingerprint);
