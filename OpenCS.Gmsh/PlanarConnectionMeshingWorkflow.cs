using CScore.Fem;
using CScore.Planar;

namespace OpenCS.Gmsh;

/// <summary>Результат двух независимых Gmsh-run и их connection mapping.</summary>
public sealed record PlanarConnectionMeshingResult(
    PlanarMeshSnapshot? SideA,
    PlanarMeshSnapshot? SideB,
    PlanarConnectionMeshMapping? Mapping,
    IReadOnlyList<FemValidationDiagnostic> Diagnostics)
{
    public bool IsCalculable => Mapping?.IsCalculable == true && !Diagnostics.Any(diagnostic => diagnostic.IsError);
}

/// <summary>Оркестрирует построение двух mesh snapshots для одного spatial connection.</summary>
public sealed class PlanarConnectionMeshingWorkflow
{
    readonly IPlanarMesher _mesher;

    public PlanarConnectionMeshingWorkflow(IPlanarMesher mesher)
    {
        _mesher = mesher ?? throw new ArgumentNullException(nameof(mesher));
    }

    public async Task<PlanarConnectionMeshingResult> BuildAsync(
        PlanarConnection connection,
        PlanarRegion sideA,
        PlanarMeshSettings settingsA,
        PlanarRegion sideB,
        PlanarMeshSettings settingsB,
        IReadOnlyList<PlanarConstraintObject>? additionalConstraintsA = null,
        IReadOnlyList<PlanarConstraintObject>? additionalConstraintsB = null,
        string? sourceFingerprintA = null,
        string? sourceFingerprintB = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(connection);
        ArgumentNullException.ThrowIfNull(sideA);
        ArgumentNullException.ThrowIfNull(settingsA);
        ArgumentNullException.ThrowIfNull(sideB);
        ArgumentNullException.ThrowIfNull(settingsB);

        var diagnostics = PlanarConnectionValidator.Validate(
            connection,
            new Dictionary<int, PlanarRegion>
            {
                [sideA.Id] = sideA,
                [sideB.Id] = sideB
            }).ToList();
        if (diagnostics.Any(diagnostic => diagnostic.IsError))
            return new(null, null, null, diagnostics);

        var requestA = BuildRequest(
            connection,
            connection.SideA,
            sideA,
            settingsA,
            additionalConstraintsA,
            sourceFingerprintA);
        var requestB = BuildRequest(
            connection,
            connection.SideB,
            sideB,
            settingsB,
            additionalConstraintsB,
            sourceFingerprintB);

        var snapshotA = await _mesher.BuildAsync(requestA, cancellationToken);
        var snapshotB = await _mesher.BuildAsync(requestB, cancellationToken);
        var mappingResult = PlanarConnectionMapper.Map(connection, sideA, snapshotA, sideB, snapshotB);
        diagnostics.AddRange(mappingResult.Diagnostics);
        return new(snapshotA, snapshotB, mappingResult.Mapping, diagnostics);
    }

    static PlanarMeshingRequest BuildRequest(
        PlanarConnection connection,
        ConnectionLocus locus,
        PlanarRegion region,
        PlanarMeshSettings settings,
        IReadOnlyList<PlanarConstraintObject>? additionalConstraints,
        string? sourceFingerprint)
    {
        var connectionConstraint = PlanarConstraintObject.Curve(
            ConstraintId(connection.Id, region.Id),
            locus.Points,
            new PlanarStructuralFacet(PlanarStructuralKind.None),
            new PlanarMeshFacet(connection.MeshMode == PlanarConnectionMeshMode.ConformingPartition
                ? PlanarMeshKind.ConformingPartition
                : PlanarMeshKind.EmbeddedCurve),
            locus.Tag);
        var constraints = region.ConstraintObjects
            .Concat(additionalConstraints ?? [])
            .Append(connectionConstraint)
            .ToArray();
        return new(
            region,
            settings,
            constraints,
            CombineSourceFingerprint(sourceFingerprint, connection),
            []);
    }

    static string ConstraintId(int connectionId, int regionId) => $"connection:{connectionId}:region:{regionId}";

    static string CombineSourceFingerprint(string? sourceFingerprint, PlanarConnection connection)
    {
        var source = string.IsNullOrWhiteSpace(sourceFingerprint) ? "none" : sourceFingerprint;
        var connectionFingerprint = PlanarConnectionFingerprint.Compute(connection);
        return $"{source}|connection:{connectionFingerprint}";
    }
}
