using CScore;
using CScore.Fire;
using CSfea.Thermal;

namespace CSfea.Tests;

/// <summary>
/// Тесты огневого фибрового сечения для проверки редукции несущей способности.
/// </summary>
public static class FireFiberSectionTests
{
    public static void RunAll()
    {
        TestHarness.Section("FireFiberSection: редукция интеграла по температуре");
        Integral_DecreasesAtHighTemperature();
    }

    private static void Integral_DecreasesAtHighTemperature()
    {
        CrossSection section = CreateSection();
        FireThermalResult thermal = CreateThermalResult();
        var fire = FireFiberSection.FromThermalResult(thermal, section, snapshotIndex: 0);

        var k = new Kurvature
        {
            e0 = -0.001,
            ky = 0.0,
            kz = 0.0
        };

        Load cold = fire.Integral(k, CalcType.C);
        fire.SetSnapshot(1);
        Load hot = fire.Integral(k, CalcType.C);

        bool reduced = Math.Abs(hot.N) < Math.Abs(cold.N);
        TestHarness.Check(
            "FireFiberSection_ReducedAxialForce",
            reduced,
            $"|N20|={Math.Abs(cold.N):F6}, |N400|={Math.Abs(hot.N):F6}");
    }

    private static FireThermalResult CreateThermalResult()
    {
        var mesh = new HeatMesh(
            x: [0.0, 1.0, 0.0],
            y: [0.0, 0.0, 1.0],
            elements: [[0, 1, 2]]);

        var meshInfo = new FireMeshBuildResult
        {
            Mesh = mesh,
            BoundaryEdges = [],
            Rebars =
            [
                new FireRebarLocation
                {
                    Id = 0,
                    X = 0.2,
                    Y = 0.2,
                    ElementIndex = 0,
                    Xi1 = 0.6,
                    Xi2 = 0.2,
                    Xi3 = 0.2
                }
            ]
        };

        return new FireThermalResult
        {
            MeshInfo = meshInfo,
            TimesMin = [0.0, 60.0],
            Snapshots =
            [
                [20.0, 20.0, 20.0],
                [400.0, 400.0, 400.0]
            ],
            RebarTemperatureHistory = new Dictionary<int, double[]>
            {
                [0] = [20.0, 400.0]
            },
            RebarMaxTemperatures = new Dictionary<int, double>
            {
                [0] = 400.0
            },
            AggregateType = "silicate"
        };
    }

    internal static CrossSection CreateSectionForTests() => CreateSection();

    private static CrossSection CreateSection()
    {
        Material concrete = CreateLinearMaterial(
            id: 101,
            tag: "test-concrete",
            type: MatType.Concrete,
            eMpa: 30_000,
            fc: 30,
            ft: 2.0,
            ec2: -0.0035,
            et2: 0.00015);

        Material rebar = CreateLinearMaterial(
            id: 202,
            tag: "test-rebar",
            type: MatType.ReSteelF,
            eMpa: 200_000,
            fc: 400,
            ft: 400,
            ec2: -0.02,
            et2: 0.025);

        var hull = new Contour(
            new[] { 0.0, 1.0, 1.0, 0.0, 0.0 },
            new[] { 0.0, 0.0, 1.0, 1.0, 0.0 },
            "rect")
        { Type = ContourType.Hull };

        var concreteArea = new MaterialArea
        {
            Tag = "concrete-area",
            Category = AreaCategory.Region,
            Contours = [hull]
        };
        concreteArea.Hull = hull;
        concreteArea.SetMaterial(concrete, DiagrammType.L2);

        var bar = Fiber.CreatePoint(diameter: 0.016, x: 0.2, y: 0.2);
        var rebarArea = new MaterialArea
        {
            Tag = "rebar-area",
            Category = AreaCategory.RebarGroup,
            Fibers = [bar]
        };
        rebarArea.SetMaterial(rebar, DiagrammType.L2);

        return new CrossSection
        {
            Tag = "fire-fiber-test",
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
}
