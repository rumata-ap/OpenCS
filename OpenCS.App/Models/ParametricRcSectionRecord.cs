namespace OpenCS.Models;

/// <summary>Сохранённый параметрический источник и отпечаток сформированного сечения.</summary>
public sealed record ParametricRcSectionRecord(int SectionId, int DefinitionVersion,
    int GeneratorVersion, string DefinitionJson, string GeneratedFingerprint);
