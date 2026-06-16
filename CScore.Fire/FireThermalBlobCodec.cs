using System.IO.Compression;
using System.Text.Json;
using CSfea.Thermal;
using CSfea.Thermal.Solvers;

namespace CScore.Fire;

/// <summary>
/// Кодек упаковки/распаковки результатов огневого теплового расчёта для хранения в SQLite BLOB.
/// </summary>
public static class FireThermalBlobCodec
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    /// <summary>
    /// Упаковать результат расчёта в единый бинарный blob (JSON + GZip).
    /// </summary>
    public static byte[] Pack(FireThermalResult result)
    {
        ArgumentNullException.ThrowIfNull(result);

        var payload = ToPayload(result);
        byte[] json = JsonSerializer.SerializeToUtf8Bytes(payload, JsonOptions);

        using var outStream = new MemoryStream();
        using (var gzip = new GZipStream(outStream, CompressionLevel.Optimal, leaveOpen: true))
            gzip.Write(json, 0, json.Length);

        return outStream.ToArray();
    }

    /// <summary>
    /// Распаковать результат расчёта из бинарного blob.
    /// </summary>
    public static FireThermalResult Unpack(byte[] blob)
    {
        ArgumentNullException.ThrowIfNull(blob);
        if (blob.Length == 0)
            throw new ArgumentException("Пустой blob теплового расчёта.", nameof(blob));

        using var inStream = new MemoryStream(blob);
        using var gzip = new GZipStream(inStream, CompressionMode.Decompress);
        using var jsonStream = new MemoryStream();
        gzip.CopyTo(jsonStream);
        byte[] json = jsonStream.ToArray();

        var payload = JsonSerializer.Deserialize<FireThermalPayload>(json, JsonOptions)
            ?? throw new InvalidOperationException("Не удалось десериализовать blob огневого расчёта.");

        return FromPayload(payload);
    }

    private static FireThermalPayload ToPayload(FireThermalResult result)
    {
        return new FireThermalPayload
        {
            Mesh = new FireMeshPayload
            {
                X = [.. result.MeshInfo.Mesh.X],
                Y = [.. result.MeshInfo.Mesh.Y],
                Elements = result.MeshInfo.Mesh.Elements.Select(e => new[] { e[0], e[1], e[2] }).ToArray(),
                BoundaryEdges = result.MeshInfo.BoundaryEdges.Select(e => new FireBoundaryEdgePayload
                {
                    NodeA = e.NodeA,
                    NodeB = e.NodeB,
                    LengthM = e.LengthM,
                    OriginalEdgeIndex = e.OriginalEdgeIndex,
                    ContourType = e.ContourType,
                    HoleIndex = e.HoleIndex
                }).ToList(),
                Rebars = result.MeshInfo.Rebars.Select(r => new FireRebarPayload
                {
                    Id = r.Id,
                    X = r.X,
                    Y = r.Y,
                    ElementIndex = r.ElementIndex,
                    Xi1 = r.Xi1,
                    Xi2 = r.Xi2,
                    Xi3 = r.Xi3
                }).ToList()
            },
            TimesMin = [.. result.TimesMin],
            Snapshots = result.Snapshots.Select(s => s.ToArray()).ToArray(),
            RebarMaxTemperatures = new Dictionary<int, double>(result.RebarMaxTemperatures),
            RebarTemperatureHistory = result.RebarTemperatureHistory.ToDictionary(kv => kv.Key, kv => kv.Value.ToArray()),
            ColdFaceNodeIds = [.. result.ColdFaceNodeIds],
            ColdFaceTemperatureHistory = result.ColdFaceTemperatureHistory?.Select(s => s.ToArray()).ToArray(),
            ColdFaceInitialTemperature = result.ColdFaceInitialTemperature,
            ConvergenceLog = result.ConvergenceLog.Select(r => new PicardRecord
            {
                Time_s = r.Time_s,
                NPicardIter = r.NPicardIter,
                MaxResidualCelsius = r.MaxResidualCelsius
            }).ToList(),
            AggregateType = result.AggregateType,
            MoistureFraction = result.MoistureFraction,
            FireCurve = result.FireCurve,
            FireDurationMin = result.FireDurationMin
        };
    }

    private static FireThermalResult FromPayload(FireThermalPayload payload)
    {
        var mesh = new HeatMesh(payload.Mesh.X, payload.Mesh.Y, payload.Mesh.Elements);
        var meshInfo = new FireMeshBuildResult
        {
            Mesh = mesh,
            BoundaryEdges = payload.Mesh.BoundaryEdges.Select(e => new FireBoundaryEdgeInfo
            {
                NodeA = e.NodeA,
                NodeB = e.NodeB,
                LengthM = e.LengthM,
                OriginalEdgeIndex = e.OriginalEdgeIndex,
                ContourType = e.ContourType ?? "outer",
                HoleIndex = e.HoleIndex
            }).ToList(),
            Rebars = payload.Mesh.Rebars.Select(r => new FireRebarLocation
            {
                Id = r.Id,
                X = r.X,
                Y = r.Y,
                ElementIndex = r.ElementIndex,
                Xi1 = r.Xi1,
                Xi2 = r.Xi2,
                Xi3 = r.Xi3
            }).ToList()
        };

        return new FireThermalResult
        {
            MeshInfo = meshInfo,
            TimesMin = payload.TimesMin ?? [],
            Snapshots = payload.Snapshots ?? [],
            RebarMaxTemperatures = payload.RebarMaxTemperatures ?? [],
            RebarTemperatureHistory = payload.RebarTemperatureHistory ?? [],
            ColdFaceNodeIds = payload.ColdFaceNodeIds ?? [],
            ColdFaceTemperatureHistory = payload.ColdFaceTemperatureHistory,
            ColdFaceInitialTemperature = payload.ColdFaceInitialTemperature,
            ConvergenceLog = payload.ConvergenceLog ?? [],
            AggregateType = payload.AggregateType ?? "silicate",
            MoistureFraction = payload.MoistureFraction,
            FireCurve = payload.FireCurve ?? "iso834",
            FireDurationMin = payload.FireDurationMin
        };
    }

    private sealed class FireThermalPayload
    {
        public FireMeshPayload Mesh { get; set; } = new();
        public double[]? TimesMin { get; set; }
        public double[][]? Snapshots { get; set; }
        public Dictionary<int, double>? RebarMaxTemperatures { get; set; }
        public Dictionary<int, double[]>? RebarTemperatureHistory { get; set; }
        public List<int>? ColdFaceNodeIds { get; set; }
        public double[][]? ColdFaceTemperatureHistory { get; set; }
        public double ColdFaceInitialTemperature { get; set; }
        public List<PicardRecord>? ConvergenceLog { get; set; }
        public string? AggregateType { get; set; }
        public double MoistureFraction { get; set; }
        public string? FireCurve { get; set; }
        public double FireDurationMin { get; set; }
    }

    private sealed class FireMeshPayload
    {
        public double[] X { get; set; } = [];
        public double[] Y { get; set; } = [];
        public int[][] Elements { get; set; } = [];
        public List<FireBoundaryEdgePayload> BoundaryEdges { get; set; } = [];
        public List<FireRebarPayload> Rebars { get; set; } = [];
    }

    private sealed class FireBoundaryEdgePayload
    {
        public int NodeA { get; set; }
        public int NodeB { get; set; }
        public double LengthM { get; set; }
        public int OriginalEdgeIndex { get; set; }
        public string? ContourType { get; set; }
        public int? HoleIndex { get; set; }
    }

    private sealed class FireRebarPayload
    {
        public int Id { get; set; }
        public double X { get; set; }
        public double Y { get; set; }
        public int ElementIndex { get; set; }
        public double Xi1 { get; set; }
        public double Xi2 { get; set; }
        public double Xi3 { get; set; }
    }
}
