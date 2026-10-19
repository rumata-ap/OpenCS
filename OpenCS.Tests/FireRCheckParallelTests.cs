using CScore;
using CScore.Fire;
using CSfea.Thermal;
using Xunit;

namespace OpenCS.Tests;

/// <summary>
/// Многопоточный пакет огнестойкости (<see cref="OpenCS.Tasks.FireRCheckBatchHandler"/>) считает каждую строку
/// на клоне сечения: R-проверка на клонах параллельно совпадает с проверкой строк по очереди на общем сечении.
/// Фикстура — как в CSfea.Tests/FireRCheckTests (сечение 1 × 1 м, стержень Ø16, нагрев до 400 °C).
/// </summary>
public sealed class FireRCheckParallelTests
{
    [Theory]
    [InlineData("fiber")]
    [InlineData("mvp")]
    public void ParallelOnClones_EqualsSequentialOnShared(string method)
    {
        var section = Section();
        var thermal = Thermal();
        section.ResolveAndBuildDiagramms(0.85);
        (double N, double Mx, double My)[] rows =
            [(-0.5, 0, 0), (-1.0, 0.1, 0), (-0.2, 0.05, -0.03), (0.1, 0.02, 0), (-2.0, 0, 0.2), (-0.8, -0.1, 0.1)];

        var sequential = rows.Select(r => FireRCheck.Run(thermal, section, r.N, r.Mx, r.My,
            method: method, snapshotIndex: 1)).ToArray();
        var parallel = new FireCheckResult[rows.Length];
        Parallel.For(0, rows.Length, i => parallel[i] = FireRCheck.Run(thermal, section.CloneForCalc(),
            rows[i].N, rows[i].Mx, rows[i].My, method: method, snapshotIndex: 1));

        for (int i = 0; i < rows.Length; i++)
        {
            Assert.Equal(sequential[i].Passed, parallel[i].Passed);
            Assert.Equal(sequential[i].Margin, parallel[i].Margin);
            Assert.Equal(sequential[i].Details.GetValueOrDefault("factor"), parallel[i].Details.GetValueOrDefault("factor"));
        }
        Assert.Contains(sequential, r => double.IsFinite(r.Margin));
    }

    static CrossSection Section()
    {
        var concrete = Linear(101, MatType.Concrete, eMpa: 30_000, fc: 30, ft: 2.0, ec2: -0.0035, et2: 0.00015);
        var rebar = Linear(202, MatType.ReSteelF, eMpa: 200_000, fc: 400, ft: 400, ec2: -0.02, et2: 0.025);
        var hull = new Contour([0.0, 1.0, 1.0, 0.0, 0.0], [0.0, 0.0, 1.0, 1.0, 0.0], "rect") { Type = ContourType.Hull };
        var concreteArea = new MaterialArea { Tag = "concrete-area", Category = AreaCategory.Region, Contours = [hull] };
        concreteArea.Hull = hull;
        concreteArea.SetMaterial(concrete, DiagrammType.L2);
        var rebarArea = new MaterialArea
        {
            Tag = "rebar-area", Category = AreaCategory.RebarGroup, Fibers = [Fiber.CreatePoint(diameter: 0.016, x: 0.2, y: 0.2)]
        };
        rebarArea.SetMaterial(rebar, DiagrammType.L2);
        return new CrossSection { Tag = "fire-fiber-test", Areas = [concreteArea, rebarArea] };
    }

    static Material Linear(int id, MatType type, double eMpa, double fc, double ft, double ec2, double et2)
    {
        MaterialChars Ch(CalcType calc) => new(calc)
        {
            Type = type, E = eMpa, Fc = fc, Ft = ft, Ry = ft, Ru = fc,
            Ec2 = ec2, Ec1Red = ec2 * 0.6, Et2 = et2, Et1Red = et2 * 0.6
        };
        return new Material
        {
            Id = id, Tag = $"m{id}", Type = type, E = eMpa,
            MaterialChars = [Ch(CalcType.C), Ch(CalcType.CL), Ch(CalcType.N), Ch(CalcType.NL)]
        };
    }

    static FireThermalResult Thermal() => new()
    {
        MeshInfo = new FireMeshBuildResult
        {
            Mesh = new HeatMesh(x: [0.0, 1.0, 0.0], y: [0.0, 0.0, 1.0], elements: [[0, 1, 2]]),
            BoundaryEdges = [],
            Rebars = [new FireRebarLocation { Id = 0, X = 0.2, Y = 0.2, ElementIndex = 0, Xi1 = 0.6, Xi2 = 0.2, Xi3 = 0.2 }]
        },
        TimesMin = [0.0, 60.0],
        Snapshots = [[20.0, 20.0, 20.0], [400.0, 400.0, 400.0]],
        RebarTemperatureHistory = new Dictionary<int, double[]> { [0] = [20.0, 400.0] },
        RebarMaxTemperatures = new Dictionary<int, double> { [0] = 400.0 },
        AggregateType = "silicate"
    };
}
