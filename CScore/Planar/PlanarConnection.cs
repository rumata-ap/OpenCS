namespace CScore.Planar;

/// <summary>Политика mesh-топологии для интерфейса между двумя PlanarRegion.</summary>
public enum PlanarConnectionMeshMode
{
    ConformingPartition,
    EmbeddedLocus,
    IndependentMpc
}

/// <summary>Ориентация mesh-chain относительно канонического направления connection.</summary>
public enum PlanarConnectionOrientation
{
    Forward,
    Reverse
}

/// <summary>Локальное представление одной стороны пространственного интерфейса.</summary>
public sealed record ConnectionLocus(
    int RegionId,
    IReadOnlyList<PlanarPoint2D> Points,
    string Tag = "");

/// <summary>Источник связи между двумя независимыми PlanarRegion.</summary>
public sealed class PlanarConnection
{
    public int Id { get; set; }
    public string Tag { get; set; } = "";
    public ConnectionLocus SideA { get; set; } = new(0, []);
    public ConnectionLocus SideB { get; set; } = new(0, []);
    public PlanarConnectionMeshMode MeshMode { get; set; } = PlanarConnectionMeshMode.EmbeddedLocus;
    public double MatchingToleranceM { get; set; } = 1e-8;
    /// <summary>Сохранённый fingerprint source contract, обновляемый при persistence.</summary>
    public string Fingerprint { get; set; } = "";
}

/// <summary>Набор connections с graph-level проверкой идентичности и ссылок на регионы.</summary>
public sealed class PlanarConnectionGraph
{
    public List<PlanarConnection> Connections { get; set; } = [];

    public IReadOnlyList<CScore.Fem.FemValidationDiagnostic> Validate(
        IReadOnlyDictionary<int, PlanarRegion> regions)
    {
        var diagnostics = new List<CScore.Fem.FemValidationDiagnostic>();
        var ids = new HashSet<int>();
        var spatialKeys = new HashSet<string>(StringComparer.Ordinal);

        foreach (var connection in Connections)
        {
            diagnostics.AddRange(PlanarConnectionValidator.Validate(connection, regions));
            if (!ids.Add(connection.Id))
                diagnostics.Add(new("planar_connection_id_duplicate", $"Connection ID {connection.Id} повторяется."));

            string spatialKey = SpatialKey(connection);
            if (!spatialKeys.Add(spatialKey))
                diagnostics.Add(new("planar_connection_spatial_duplicate", $"Spatial locus connection {connection.Id} повторяется."));
        }

        return diagnostics;
    }

    static string SpatialKey(PlanarConnection connection)
    {
        var first = LocusKey(connection.SideA);
        var second = LocusKey(connection.SideB);
        return connection.SideA.RegionId < connection.SideB.RegionId
            ? $"{first}|{second}"
            : $"{second}|{first}";
    }

    static string LocusKey(ConnectionLocus locus)
    {
        var forward = string.Join(';', locus.Points.Select(point => $"{point.U:G17},{point.V:G17}"));
        var reverse = string.Join(';', locus.Points.Reverse().Select(point => $"{point.U:G17},{point.V:G17}"));
        return $"{locus.RegionId}:{(string.CompareOrdinal(forward, reverse) <= 0 ? forward : reverse)}";
    }
}
