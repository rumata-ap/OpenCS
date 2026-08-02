namespace CScore.Fem;

/// <summary>Нормализованный read-only снимок топологии FEM-схемы для domain-сервисов.</summary>
public sealed record FemSchemaTopology
{
    public int SchemaId { get; }
    public IReadOnlyList<FemNode> Nodes { get; }
    public IReadOnlyList<FemMember> Members { get; }
    public IReadOnlyList<FemElement> Elements { get; }

    public FemSchemaTopology(
        int schemaId,
        IEnumerable<FemNode> nodes,
        IEnumerable<FemMember> members,
        IEnumerable<FemElement> elements)
    {
        SchemaId = schemaId;
        Nodes = (nodes ?? throw new ArgumentNullException(nameof(nodes))).ToArray();
        Members = (members ?? throw new ArgumentNullException(nameof(members))).ToArray();
        Elements = (elements ?? throw new ArgumentNullException(nameof(elements))).ToArray();
    }
}
