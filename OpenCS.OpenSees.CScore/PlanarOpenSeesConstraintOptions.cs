namespace OpenCS.OpenSees.CScore;

/// <summary>Политика преобразования structural relation в OpenSees constraint.</summary>
public enum PlanarOpenSeesConstraintPolicy
{
    EqualDof,
    RigidLinkBar,
    RigidLinkBeam
}

/// <summary>Настройки solver-level mapping для PlanarConstraintObject.</summary>
public sealed record PlanarOpenSeesConstraintOptions
{
    /// <summary>Политика для derived EmbeddedMember и не переопределённых связей.</summary>
    public PlanarOpenSeesConstraintPolicy EmbeddedMemberPolicy { get; init; } =
        PlanarOpenSeesConstraintPolicy.EqualDof;

    /// <summary>Политика для structural kind RigidBody.</summary>
    public PlanarOpenSeesConstraintPolicy RigidBodyPolicy { get; init; } =
        PlanarOpenSeesConstraintPolicy.RigidLinkBeam;
}
