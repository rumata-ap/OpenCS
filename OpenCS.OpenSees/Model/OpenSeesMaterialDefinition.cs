namespace OpenCS.OpenSees.Model;

/// <summary>Материал OpenSees с монотонными огибающими в единицах SI.</summary>
public sealed class OpenSeesMaterialDefinition
{
    /// <summary>Положительный уникальный идентификатор материала.</summary>
    public int Tag { get; init; }

    /// <summary>Идентификатор исходного материала.</summary>
    public string SourceId { get; init; } = "";

    /// <summary>Тип исходного материала.</summary>
    public string SourceType { get; init; } = "";

    /// <summary>Огибающая при положительных деформациях.</summary>
    public IReadOnlyList<EnvelopePoint> PositiveEnvelope { get; init; } = [];

    /// <summary>Огибающая при отрицательных деформациях.</summary>
    public IReadOnlyList<EnvelopePoint> NegativeEnvelope { get; init; } = [];

    /// <summary>Предупреждения о потере особенностей исходной модели.</summary>
    public IReadOnlyList<string> Warnings { get; init; } = [];
}
