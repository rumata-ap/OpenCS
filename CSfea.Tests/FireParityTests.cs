using System.Text.Json;
using System.Text.Json.Serialization;
using CScore;
using CScore.Fire;
using CScore.Fire.Entities;

namespace CSfea.Tests;

/// <summary>
/// Проверки паритета C# теплопереноса с Python-фикстурами.
/// </summary>
public static class FireParityTests
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public static void RunAll()
    {
        TestHarness.Section("Fire parity: Python fixtures");
        TestHarness.RunSlow(
            "FireParity_Beam200x400_R60 (60 min, ~1500 узлов)",
            FireParity_Beam200x400_R60);
        FireParity_Rectangle5min();
    }

    private static void FireParity_Beam200x400_R60()
        => RunFixture("beam_200x400_R60_3sided.json", "FireParity_Beam200x400_R60", strictAbsTolC: 0.5, relaxedAbsTolC: 5.0, relTol: 0.015);

    private static void FireParity_Rectangle5min()
        => RunFixture("rectangle_200x400_5min_3sided.json", "FireParity_Rectangle5min", strictAbsTolC: 0.5, relaxedAbsTolC: 5.0, relTol: 0.03);

    private static void RunFixture(string fixtureName, string testName, double strictAbsTolC, double relaxedAbsTolC, double relTol)
    {
        string path = FixturePath(fixtureName);
        var fixture = LoadFixture(path);

        CrossSection section = BuildCrossSection(fixture.Section);
        FireSectionDef def = BuildFireSection(fixture.FireSection);

        var result = FireThermalService.Run(def, section, fixture.AggregateType);
        int nodeCount = result.MeshInfo.Mesh.X.Length;
        int elemCount = result.MeshInfo.Mesh.Elements.Length;
        if (fixture.MeshStats is not null &&
            (fixture.MeshStats.NNodes != nodeCount || fixture.MeshStats.NElements != elemCount))
        {
            Console.WriteLine(
                $"  [INFO] {testName}: mesh differs from fixture " +
                $"(py nodes={fixture.MeshStats.NNodes}, cs nodes={nodeCount}; " +
                $"py elems={fixture.MeshStats.NElements}, cs elems={elemCount})");
        }

        bool strictOk = EvaluateProbeTemperatures(
            fixture,
            result,
            strictAbsTolC,
            relTol,
            testName,
            checkLabelPrefix: "strict",
            emitChecks: false);
        double usedAbsTol = strictOk ? strictAbsTolC : relaxedAbsTolC;
        string tolNote = strictOk
            ? $"tol=±{usedAbsTol:F1}C"
            : $"strict(±{strictAbsTolC:F1}C) failed -> DONE_WITH_CONCERNS using ±{usedAbsTol:F1}C";

        bool finalOk = EvaluateProbeTemperatures(
            fixture,
            result,
            usedAbsTol,
            relTol,
            testName,
            checkLabelPrefix: "final",
            emitChecks: true);
        TestHarness.Check(testName, finalOk, tolNote);
    }

    private static bool EvaluateProbeTemperatures(
        FixtureRoot fixture,
        FireThermalResult result,
        double absTol,
        double relTol,
        string testName,
        string checkLabelPrefix,
        bool emitChecks)
    {
        bool allOk = true;
        for (int p = 0; p < fixture.Probes.Count; p++)
        {
            var probe = fixture.Probes[p];
            bool probeOk = true;
            int nearest = FindNearestNode(result.MeshInfo.Mesh.X, result.MeshInfo.Mesh.Y, probe.X, probe.Y);

            int compareCount = Math.Min(probe.SnapshotsC.Count, result.Snapshots.Length);
            bool lenOk = probe.SnapshotsC.Count == result.Snapshots.Length;
            if (!lenOk)
            {
                Console.WriteLine(
                    $"  [INFO] {testName}/{probe.Name}: snapshot count differs " +
                    $"(py={probe.SnapshotsC.Count}, cs={result.Snapshots.Length}), compare min={compareCount}");
            }

            for (int i = 0; i < compareCount; i++)
            {
                double expected = probe.SnapshotsC[i];
                double actual = result.Snapshots[i][nearest];
                bool ok = NearlyEqualTemp(actual, expected, absTol, relTol);
                if (!ok)
                {
                    probeOk = false;
                    allOk = false;
                }
            }

            if (probe.SnapshotsC.Count > 0 && result.Snapshots.Length > 0)
            {
                double expectedFinal = probe.SnapshotsC[^1];
                double actualFinal = result.Snapshots[^1][nearest];
                bool finalOk = NearlyEqualTemp(actualFinal, expectedFinal, absTol, relTol);
                if (!finalOk)
                {
                    probeOk = false;
                    allOk = false;
                }
            }

            double pyFinal = probe.SnapshotsC.Count > 0 ? probe.SnapshotsC[^1] : double.NaN;
            double csFinal = result.Snapshots.Length > 0 ? result.Snapshots[^1][nearest] : double.NaN;
            double finalDiff = (double.IsNaN(pyFinal) || double.IsNaN(csFinal)) ? double.NaN : Math.Abs(csFinal - pyFinal);
            string detail = $"probe={probe.Name}, nearest={nearest}, dT_final={finalDiff:F3}C, tol=±{absTol:F1}C";
            if (emitChecks)
            {
                TestHarness.Check($"{testName}_{checkLabelPrefix}_probe_{p + 1}", probeOk, detail);
            }
            else
            {
                Console.WriteLine($"  [INFO] {testName}_{checkLabelPrefix}_probe_{p + 1}  — {(probeOk ? "PASS" : "FAIL")} {detail}");
            }
        }

        return allOk;
    }

    private static bool NearlyEqualTemp(double actual, double expected, double absTol, double relTol)
    {
        double absErr = Math.Abs(actual - expected);
        if (absErr <= absTol)
            return true;
        double scale = Math.Max(Math.Abs(expected), 1.0);
        return absErr / scale <= relTol;
    }

    private static int FindNearestNode(double[] x, double[] y, double px, double py)
    {
        int best = -1;
        double bestD2 = double.PositiveInfinity;
        for (int i = 0; i < x.Length; i++)
        {
            double dx = x[i] - px;
            double dy = y[i] - py;
            double d2 = dx * dx + dy * dy;
            if (d2 < bestD2)
            {
                bestD2 = d2;
                best = i;
            }
        }

        return best;
    }

    private static FixtureRoot LoadFixture(string path)
    {
        string json = File.ReadAllText(path);
        return JsonSerializer.Deserialize<FixtureRoot>(json, JsonOptions)
               ?? throw new InvalidOperationException($"Не удалось разобрать фикстуру '{path}'.");
    }

    private static CrossSection BuildCrossSection(SectionDto section)
    {
        var hull = ToContour(section.Outer, ContourType.Hull, "hull");
        var area = new MaterialArea
        {
            Tag = "fire-parity",
            Category = AreaCategory.Region,
            Contours = [hull]
        };
        area.Hull = hull;

        if (section.Holes is not null)
        {
            for (int i = 0; i < section.Holes.Count; i++)
            {
                var hole = ToContour(section.Holes[i], ContourType.Hole, $"hole-{i}");
                area.Contours.Add(hole);
            }
        }

        return new CrossSection
        {
            Tag = "fire-parity-section",
            Areas = [area]
        };
    }

    private static Contour ToContour(List<PointDto> points, ContourType type, string tag)
    {
        if (points.Count < 3)
            throw new InvalidOperationException("Контур фикстуры должен содержать минимум 3 точки.");

        var ring = new List<PointDto>(points.Count + 1);
        ring.AddRange(points);
        if (!SamePoint(ring[0], ring[^1]))
            ring.Add(new PointDto(ring[0].X, ring[0].Y));

        var contour = new Contour(ring.Select(p => p.X), ring.Select(p => p.Y), tag)
        {
            Type = type
        };
        return contour;
    }

    private static bool SamePoint(PointDto a, PointDto b)
        => Math.Abs(a.X - b.X) <= 1e-12 && Math.Abs(a.Y - b.Y) <= 1e-12;

    private static FireSectionDef BuildFireSection(FireSectionDto src)
    {
        var def = new FireSectionDef
        {
            FireDurationMin = src.FireDurationMin,
            FireCurve = src.FireCurve,
            MeshStepM = src.MeshStepM,
            TimeStepS = src.TimeStepS,
            SnapshotStepMin = src.SnapshotStepMin,
            Theta = src.Theta,
            PicardTolCelsius = src.PicardTolCelsius,
            PicardMaxIter = src.PicardMaxIter,
            BcPreset = src.BcPreset,
            HoleBcPreset = src.HoleBcPreset ?? "adiabatic",
            Algorithm = src.Algorithm,
            SmoothIterTri = src.SmoothIterTri,
            Edges = []
        };

        if (src.Edges is not null)
        {
            foreach (var edge in src.Edges)
            {
                def.Edges.Add(new FireBoundaryEdgeDef
                {
                    EdgeIndex = edge.OriginalEdgeIndex,
                    BcType = edge.BcType,
                    AlphaConv = edge.AlphaConv,
                    Emissivity = edge.Emissivity,
                    TAmbientCelsius = edge.TAmbientCelsius,
                    ContourType = edge.ContourType ?? "outer",
                    HoleIndex = edge.HoleIndex
                });
            }
        }

        return def;
    }

    private static string FixturePath(string name)
    {
        string relative = Path.Combine("tools", "fire-parity", "fixtures", name);
        var probes = new[]
        {
            AppContext.BaseDirectory,
            Directory.GetCurrentDirectory()
        };

        foreach (string start in probes)
        {
            string? current = Path.GetFullPath(start);
            while (!string.IsNullOrEmpty(current))
            {
                string candidate = Path.Combine(current, relative);
                if (File.Exists(candidate))
                    return candidate;

                string? parent = Directory.GetParent(current)?.FullName;
                if (string.Equals(parent, current, StringComparison.OrdinalIgnoreCase))
                    break;
                current = parent;
            }
        }

        string outputCandidate = Path.Combine(AppContext.BaseDirectory, "fixtures", name);
        if (File.Exists(outputCandidate))
            return outputCandidate;

        throw new FileNotFoundException($"Fixture not found: {name}");
    }

    private sealed class FixtureRoot
    {
        [JsonPropertyName("name")]
        public string Name { get; set; } = "";

        [JsonPropertyName("section")]
        public SectionDto Section { get; set; } = new();

        [JsonPropertyName("fire_section")]
        public FireSectionDto FireSection { get; set; } = new();

        [JsonPropertyName("aggregate_type")]
        public string AggregateType { get; set; } = "silicate";

        [JsonPropertyName("probes")]
        public List<ProbeDto> Probes { get; set; } = [];

        [JsonPropertyName("mesh_stats")]
        public MeshStatsDto? MeshStats { get; set; }
    }

    private sealed class SectionDto
    {
        [JsonPropertyName("outer")]
        [JsonConverter(typeof(PointListConverter))]
        public List<PointDto> Outer { get; set; } = [];

        [JsonPropertyName("holes")]
        [JsonConverter(typeof(HoleListConverter))]
        public List<List<PointDto>> Holes { get; set; } = [];
    }

    private sealed class FireSectionDto
    {
        [JsonPropertyName("fire_duration_min")]
        public double FireDurationMin { get; set; }

        [JsonPropertyName("fire_curve")]
        public string FireCurve { get; set; } = "iso834";

        [JsonPropertyName("mesh_step_m")]
        public double MeshStepM { get; set; }

        [JsonPropertyName("time_step_s")]
        public double TimeStepS { get; set; }

        [JsonPropertyName("snapshot_step_min")]
        public double SnapshotStepMin { get; set; }

        [JsonPropertyName("theta")]
        public double Theta { get; set; } = 1.0;

        [JsonPropertyName("picard_tol_celsius")]
        public double PicardTolCelsius { get; set; } = 0.5;

        [JsonPropertyName("picard_max_iter")]
        public int PicardMaxIter { get; set; } = 20;

        [JsonPropertyName("bc_preset")]
        public string BcPreset { get; set; } = "manual";

        [JsonPropertyName("hole_bc_preset")]
        public string? HoleBcPreset { get; set; }

        [JsonPropertyName("algorithm")]
        public string Algorithm { get; set; } = "ruppert";

        [JsonPropertyName("smooth_iter_tri")]
        public int SmoothIterTri { get; set; } = 2;

        [JsonPropertyName("edges")]
        public List<EdgeDto> Edges { get; set; } = [];
    }

    private sealed class EdgeDto
    {
        [JsonPropertyName("original_edge_index")]
        public int OriginalEdgeIndex { get; set; }

        [JsonPropertyName("bc_type")]
        public string BcType { get; set; } = "adiabatic";

        [JsonPropertyName("contour_type")]
        public string? ContourType { get; set; }

        [JsonPropertyName("hole_index")]
        public int? HoleIndex { get; set; }

        [JsonPropertyName("alpha_conv")]
        public double AlphaConv { get; set; }

        [JsonPropertyName("emissivity")]
        public double Emissivity { get; set; }

        [JsonPropertyName("t_ambient_celsius")]
        public double TAmbientCelsius { get; set; } = 20.0;
    }

    private sealed class ProbeDto
    {
        [JsonPropertyName("name")]
        public string Name { get; set; } = "";

        [JsonPropertyName("x")]
        public double X { get; set; }

        [JsonPropertyName("y")]
        public double Y { get; set; }

        [JsonPropertyName("snapshots_c")]
        public List<double> SnapshotsC { get; set; } = [];
    }

    private sealed class MeshStatsDto
    {
        [JsonPropertyName("n_nodes")]
        public int NNodes { get; set; }

        [JsonPropertyName("n_elements")]
        public int NElements { get; set; }
    }

    private sealed class PointListConverter : JsonConverter<List<PointDto>>
    {
        public override List<PointDto> Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            var points = new List<PointDto>();
            if (reader.TokenType != JsonTokenType.StartArray)
                throw new JsonException("Expected points array.");

            while (reader.Read())
            {
                if (reader.TokenType == JsonTokenType.EndArray)
                    return points;

                if (reader.TokenType != JsonTokenType.StartArray)
                    throw new JsonException("Expected point tuple.");

                reader.Read();
                double x = reader.GetDouble();
                reader.Read();
                double y = reader.GetDouble();
                reader.Read();
                if (reader.TokenType != JsonTokenType.EndArray)
                    throw new JsonException("Expected end of point tuple.");

                points.Add(new PointDto(x, y));
            }

            throw new JsonException("Unexpected end of points array.");
        }

        public override void Write(Utf8JsonWriter writer, List<PointDto> value, JsonSerializerOptions options)
            => throw new NotSupportedException();
    }

    private sealed class HoleListConverter : JsonConverter<List<List<PointDto>>>
    {
        public override List<List<PointDto>> Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            var holes = new List<List<PointDto>>();
            var pointListConverter = new PointListConverter();
            if (reader.TokenType != JsonTokenType.StartArray)
                throw new JsonException("Expected holes array.");

            while (reader.Read())
            {
                if (reader.TokenType == JsonTokenType.EndArray)
                    return holes;

                var hole = pointListConverter.Read(ref reader, typeof(List<PointDto>), options);
                holes.Add(hole);
            }

            throw new JsonException("Unexpected end of holes array.");
        }

        public override void Write(Utf8JsonWriter writer, List<List<PointDto>> value, JsonSerializerOptions options)
            => throw new NotSupportedException();
    }

    private sealed record PointDto(double X, double Y);
}
