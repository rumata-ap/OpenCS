namespace OpenCS.OpenSees.Structural;

/// <summary>Положение точки интегрирования и связь с проектным fiber-сечением.</summary>
public sealed record FemNonlinearSectionLocation(
    int ElementTag,
    int IntegrationPoint,
    int SectionTag,
    int? CrossSectionId,
    double DistanceFromElementStartM,
    double ElementLengthM,
    double RelativePosition);
