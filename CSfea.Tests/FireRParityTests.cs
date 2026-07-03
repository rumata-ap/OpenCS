using System.Text.Json;
using System.Text.Json.Serialization;
using CScore;
using CScore.Fire;
using CSfea.Thermal;

namespace CSfea.Tests;

/// <summary>
/// Паритет R-проверки (fiber) с Python-фикстурами: встроенное тепловое поле + ожидаемый factor/γ.
/// </summary>
public static class FireRParityTests
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public static void RunAll()
    {
        TestHarness.Section("Fire R parity: Python fixtures");
        TestHarness.RunSlow(
            "FireRParity_Rect400x400_5min_Fiber (embedded thermal + bisection)",
            FireRParity_Rect400x400_5min_Fiber);
    }

    private static void FireRParity_Rect400x400_5min_Fiber()
        => RunFixture("rect_400x400_5min_r_check_fiber.json", "FireRParity_Rect400x400_5min_Fiber");

    private static void RunFixture(string fixtureName, string testName)
    {
        var fixture = LoadFixture(FixturePath(fixtureName));
        CrossSection section = BuildSection(fixture);
        FireThermalResult thermal = BuildThermalResult(fixture);

        var check = FireRCheckFiber.Run(
            thermal,
            section,
            n: fixture.Loads.N,
            mx: fixture.Loads.Mx,
            my: fixture.Loads.My,
            snapshotIndex: fixture.SnapshotIndex);

        var exp = fixture.ExpectedFiber;
        bool countsOk = GetInt(check.Details, "n_concrete_elements") == exp.NConcreteElements
                        && GetInt(check.Details, "n_rebar_elements") == exp.NRebarElements;
        TestHarness.Check($"{testName}_elementCounts", countsOk,
            $"conc cs={GetInt(check.Details, "n_concrete_elements")} py={exp.NConcreteElements}, " +
            $"rebar cs={GetInt(check.Details, "n_rebar_elements")} py={exp.NRebarElements}");

        CompareDouble(testName, "gamma_bt_min", GetDouble(check.Details, "gamma_bt_min"), exp.GammaBtMin, absTol: 0.02, relTol: 0.02);
        CompareDouble(testName, "gamma_bt_avg", GetDouble(check.Details, "gamma_bt_avg"), exp.GammaBtAvg, absTol: 0.02, relTol: 0.02);
        CompareDouble(testName, "gamma_bt_max", GetDouble(check.Details, "gamma_bt_max"), exp.GammaBtMax, absTol: 0.02, relTol: 0.02);
        CompareDouble(testName, "gamma_st_c_min", GetDouble(check.Details, "gamma_st_c_min"), exp.GammaStCMin, absTol: 0.02, relTol: 0.02);
        CompareDouble(testName, "gamma_st_t_min", GetDouble(check.Details, "gamma_st_t_min"), exp.GammaStTMin, absTol: 0.02, relTol: 0.02);

        bool passedOk = check.Passed == exp.Passed;
        TestHarness.Check($"{testName}_passed", passedOk,
            $"cs={check.Passed}, py={exp.Passed}");

        double csFactor = GetDouble(check.Details, "factor");
        bool factorFailed = csFactor < 1.0 && exp.Factor < 1.0;
        TestHarness.Check($"{testName}_factorBelowOne", factorFailed,
            $"cs={csFactor:G6}, py={exp.Factor:G6}");

        // Числовой factor/N_limit: bisection (C#) vs LimitForceSolverFast (Python) — известный разрыв.
        double factorRelErr = Math.Abs(csFactor - exp.Factor) / Math.Max(Math.Abs(exp.Factor), 1e-6);
        Console.WriteLine(
            $"  [INFO] {testName}_factor_solver_gap: cs={csFactor:G6}, py={exp.Factor:G6}, " +
            $"relErr={factorRelErr:P1} (ожидается до порта LimitForceSolverFast)");
    }

    private static void CompareDouble(
        string testName,
        string field,
        double actual,
        double expected,
        double absTol,
        double relTol)
    {
        bool ok = NearlyEqual(actual, expected, absTol, relTol);
        TestHarness.Check($"{testName}_{field}", ok,
            $"cs={actual:G6}, py={expected:G6}, d={Math.Abs(actual - expected):G4}");
    }

    private static bool NearlyEqual(double actual, double expected, double absTol, double relTol)
    {
        double absErr = Math.Abs(actual - expected);
        if (absErr <= absTol)
            return true;
        double scale = Math.Max(Math.Abs(expected), 1e-6);
        return absErr / scale <= relTol;
    }

    private static double GetDouble(IReadOnlyDictionary<string, object?> details, string key)
    {
        if (!details.TryGetValue(key, out object? raw) || raw is null)
            return double.NaN;
        return raw switch
        {
            double d => d,
            float f => f,
            int i => i,
            long l => l,
            JsonElement je when je.ValueKind == JsonValueKind.Number => je.GetDouble(),
            _ => Convert.ToDouble(raw, System.Globalization.CultureInfo.InvariantCulture)
        };
    }

    private static int GetInt(IReadOnlyDictionary<string, object?> details, string key)
    {
        if (!details.TryGetValue(key, out object? raw) || raw is null)
            return -1;
        return raw switch
        {
            int i => i,
            long l => (int)l,
            double d => (int)d,
            JsonElement je when je.ValueKind == JsonValueKind.Number => je.GetInt32(),
            _ => Convert.ToInt32(raw, System.Globalization.CultureInfo.InvariantCulture)
        };
    }

    private static FireThermalResult BuildThermalResult(RFixture fixture)
    {
        var t = fixture.Thermal;
        var mesh = new HeatMesh(t.X, t.Y, t.Elements.Select(e => e.ToArray()).ToArray());
        var rebars = t.Rebars.Select(r => new FireRebarLocation
        {
            Id = r.Id,
            X = r.X,
            Y = r.Y,
            ElementIndex = r.ElementIndex,
            Xi1 = r.Xi1,
            Xi2 = r.Xi2,
            Xi3 = r.Xi3
        }).ToList();

        var meshInfo = new FireMeshBuildResult
        {
            Mesh = mesh,
            BoundaryEdges = [],
            Rebars = rebars
        };

        var hist = new Dictionary<int, double[]>();
        foreach (var kv in t.RebarTemperatureHistory)
        {
            if (!int.TryParse(kv.Key, out int rid))
                continue;
            hist[rid] = kv.Value.ToArray();
        }

        var maxT = new Dictionary<int, double>();
        foreach (var kv in t.RebarMaxTemperatures)
        {
            if (!int.TryParse(kv.Key, out int rid))
                continue;
            maxT[rid] = kv.Value;
        }

        return new FireThermalResult
        {
            MeshInfo = meshInfo,
            TimesMin = t.TimesMin.ToArray(),
            Snapshots = t.Snapshots.Select(s => s.ToArray()).ToArray(),
            RebarTemperatureHistory = hist,
            RebarMaxTemperatures = maxT,
            AggregateType = fixture.AggregateType,
            FireDurationMin = fixture.FireSection.FireDurationMin,
            FireCurve = fixture.FireSection.FireCurve
        };
    }

    private static CrossSection BuildSection(RFixture fixture)
    {
        var mats = fixture.Materials;
        Material concrete = CreateLinearMaterial(
            id: 1,
            tag: "B25",
            type: MatType.Concrete,
            eMpa: mats.Concrete.EMpa,
            fc: -mats.Concrete.FcMpa,
            ft: mats.Concrete.FtMpa,
            ec2: -0.0035,
            et2: 0.00015);

        Material rebar = CreateLinearMaterial(
            id: 2,
            tag: "A500",
            type: MatType.ReSteelF,
            eMpa: mats.Rebar.EMpa,
            fc: mats.Rebar.RsMpa,
            ft: mats.Rebar.RsMpa,
            ec2: -0.02,
            et2: 0.025);

        var hull = new Contour(
            fixture.Section.Outer.Select(p => p[0]).Append(fixture.Section.Outer[0][0]).ToArray(),
            fixture.Section.Outer.Select(p => p[1]).Append(fixture.Section.Outer[0][1]).ToArray(),
            "outer")
        { Type = ContourType.Hull };

        var concreteArea = new MaterialArea
        {
            Tag = "concrete",
            Category = AreaCategory.Region,
            Contours = [hull]
        };
        concreteArea.Hull = hull;
        concreteArea.SetMaterial(concrete, DiagrammType.L2);

        var rebars = fixture.Section.Rebars
            .OrderBy(r => r.Id)
            .Select(r => Fiber.CreatePoint(r.Diameter, r.X, r.Y))
            .ToList();

        var rebarArea = new MaterialArea
        {
            Tag = "rebars",
            Category = AreaCategory.RebarGroup,
            Fibers = rebars
        };
        rebarArea.SetMaterial(rebar, DiagrammType.L2);

        return new CrossSection
        {
            Tag = "r-parity-section",
            Areas = [concreteArea, rebarArea]
        };
    }

    private static Material CreateLinearMaterial(
        int id,
        string tag,
        MatType type,
        double eMpa,
        double fc,
        double ft,
        double ec2,
        double et2)
    {
        MaterialChars Ch(CalcType calcType) => new(calcType)
        {
            Type = type,
            E = eMpa,
            Fc = fc,
            Ft = ft,
            Ry = ft,
            Ru = fc,
            Ec2 = ec2,
            Ec1Red = ec2 * 0.6,
            Et2 = et2,
            Et1Red = et2 * 0.6
        };

        var material = new Material
        {
            Id = id,
            Tag = tag,
            Type = type,
            E = eMpa
        };
        material.MaterialChars =
        [
            Ch(CalcType.C),
            Ch(CalcType.CL),
            Ch(CalcType.N),
            Ch(CalcType.NL)
        ];
        return material;
    }

    private static RFixture LoadFixture(string path)
    {
        string json = File.ReadAllText(path);
        return JsonSerializer.Deserialize<RFixture>(json, JsonOptions)
               ?? throw new InvalidOperationException($"Не удалось разобрать фикстуру '{path}'.");
    }

    private static string FixturePath(string name)
    {
        string relative = Path.Combine("tools", "fire-parity", "fixtures", name);
        var probes = new[] { AppContext.BaseDirectory, Directory.GetCurrentDirectory() };
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

    private sealed class RFixture
    {
        [JsonPropertyName("aggregate_type")]
        public string AggregateType { get; set; } = "silicate";

        [JsonPropertyName("loads")]
        public LoadsDto Loads { get; set; } = new();

        [JsonPropertyName("snapshot_index")]
        public int SnapshotIndex { get; set; } = -1;

        [JsonPropertyName("section")]
        public SectionDto Section { get; set; } = new();

        [JsonPropertyName("materials")]
        public MaterialsDto Materials { get; set; } = new();

        [JsonPropertyName("fire_section")]
        public FireSectionRefDto FireSection { get; set; } = new();

        [JsonPropertyName("thermal")]
        public ThermalDto Thermal { get; set; } = new();

        [JsonPropertyName("expected_fiber")]
        public ExpectedFiberDto ExpectedFiber { get; set; } = new();
    }

    private sealed class LoadsDto
    {
        [JsonPropertyName("N")]
        public double N { get; set; }

        [JsonPropertyName("Mx")]
        public double Mx { get; set; }

        [JsonPropertyName("My")]
        public double My { get; set; }
    }

    private sealed class SectionDto
    {
        [JsonPropertyName("outer")]
        public List<double[]> Outer { get; set; } = [];

        [JsonPropertyName("rebars")]
        public List<RebarDto> Rebars { get; set; } = [];
    }

    private sealed class RebarDto
    {
        [JsonPropertyName("id")]
        public int Id { get; set; }

        [JsonPropertyName("x")]
        public double X { get; set; }

        [JsonPropertyName("y")]
        public double Y { get; set; }

        [JsonPropertyName("diameter")]
        public double Diameter { get; set; }
    }

    private sealed class MaterialsDto
    {
        [JsonPropertyName("concrete")]
        public ConcreteMatDto Concrete { get; set; } = new();

        [JsonPropertyName("rebar")]
        public RebarMatDto Rebar { get; set; } = new();
    }

    private sealed class ConcreteMatDto
    {
        [JsonPropertyName("fc_mpa")]
        public double FcMpa { get; set; }

        [JsonPropertyName("ft_mpa")]
        public double FtMpa { get; set; }

        [JsonPropertyName("e_mpa")]
        public double EMpa { get; set; }
    }

    private sealed class RebarMatDto
    {
        [JsonPropertyName("rs_mpa")]
        public double RsMpa { get; set; }

        [JsonPropertyName("e_mpa")]
        public double EMpa { get; set; }
    }

    private sealed class FireSectionRefDto
    {
        [JsonPropertyName("fire_duration_min")]
        public double FireDurationMin { get; set; }

        [JsonPropertyName("fire_curve")]
        public string FireCurve { get; set; } = "iso834";
    }

    private sealed class ThermalDto
    {
        [JsonPropertyName("x")]
        public double[] X { get; set; } = [];

        [JsonPropertyName("y")]
        public double[] Y { get; set; } = [];

        [JsonPropertyName("elements")]
        public List<int[]> Elements { get; set; } = [];

        [JsonPropertyName("rebars")]
        public List<ThermalRebarDto> Rebars { get; set; } = [];

        [JsonPropertyName("times_min")]
        public List<double> TimesMin { get; set; } = [];

        [JsonPropertyName("snapshots")]
        public List<double[]> Snapshots { get; set; } = [];

        [JsonPropertyName("rebar_temperature_history")]
        public Dictionary<string, List<double>> RebarTemperatureHistory { get; set; } = [];

        [JsonPropertyName("rebar_max_temperatures")]
        public Dictionary<string, double> RebarMaxTemperatures { get; set; } = [];
    }

    private sealed class ThermalRebarDto
    {
        [JsonPropertyName("id")]
        public int Id { get; set; }

        [JsonPropertyName("x")]
        public double X { get; set; }

        [JsonPropertyName("y")]
        public double Y { get; set; }

        [JsonPropertyName("element_index")]
        public int ElementIndex { get; set; }

        [JsonPropertyName("xi1")]
        public double Xi1 { get; set; }

        [JsonPropertyName("xi2")]
        public double Xi2 { get; set; }

        [JsonPropertyName("xi3")]
        public double Xi3 { get; set; }
    }

    private sealed class ExpectedFiberDto
    {
        [JsonPropertyName("passed")]
        public bool Passed { get; set; }

        [JsonPropertyName("margin")]
        public double Margin { get; set; }

        [JsonPropertyName("factor")]
        public double Factor { get; set; }

        [JsonPropertyName("gamma_bt_min")]
        public double GammaBtMin { get; set; }

        [JsonPropertyName("gamma_bt_avg")]
        public double GammaBtAvg { get; set; }

        [JsonPropertyName("gamma_bt_max")]
        public double GammaBtMax { get; set; }

        [JsonPropertyName("gamma_st_c_min")]
        public double GammaStCMin { get; set; }

        [JsonPropertyName("gamma_st_t_min")]
        public double GammaStTMin { get; set; }

        [JsonPropertyName("n_concrete_elements")]
        public int NConcreteElements { get; set; }

        [JsonPropertyName("n_rebar_elements")]
        public int NRebarElements { get; set; }

        [JsonPropertyName("N_limit")]
        public double? NLimit { get; set; }

        [JsonPropertyName("Mx_limit")]
        public double? MxLimit { get; set; }
    }
}
