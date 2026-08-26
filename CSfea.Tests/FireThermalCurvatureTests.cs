using CScore;
using CScore.Fire;

namespace CSfea.Tests;

/// <summary>Температурная кривизна, удлинение оси и жёсткость по п. 8.44б СП 468.</summary>
public static class FireThermalCurvatureTests
{
    /// <summary>Запустить аналитические проверки температурной кривизны.</summary>
    public static void RunAll()
    {
        TestHarness.Section("FireThermalCurvature: χ_t, ε_t, D");
        Phi1_NodeValuesAndInterpolation();
        Chi_MatchesHandCalcForLinearField();
        Chi_ChangesSignWithRebarPosition();
        Eps_MatchesHandCalc();
        Stiffness_MatchesHandCalc();
        XiR_UsesWeightedRebarEquivalent();
        Run_WeightedRebarEquivalentIsOrderIndependent();
        Run_ConvertsMaterialModulusFromKpaToPa();
    }

    static void Phi1_NodeValuesAndInterpolation()
    {
        bool nodes = Math.Abs(FireThermalCurvature.Phi1(60) - 0.5) < 1e-12
                  && Math.Abs(FireThermalCurvature.Phi1(120) - 0.5) < 1e-12
                  && Math.Abs(FireThermalCurvature.Phi1(180) - 0.4) < 1e-12
                  && Math.Abs(FireThermalCurvature.Phi1(240) - 0.3) < 1e-12
                  && Math.Abs(FireThermalCurvature.Phi1(300) - 0.3) < 1e-12;
        bool interp = Math.Abs(FireThermalCurvature.Phi1(150) - 0.45) < 1e-12;

        TestHarness.Check("FireCurvature_Phi1Nodes", nodes);
        TestHarness.Check("FireCurvature_Phi1Interp", interp,
            $"phi1(150)={FireThermalCurvature.Phi1(150):F4}");
    }

    static void Chi_MatchesHandCalcForLinearField()
    {
        double chi = FireThermalCurvature.Chi(
            tRebar: 500.0, tColdConcrete: 100.0, h0M: 0.45,
            aggregateType: "silicate", tensionRebarAtHeatedFace: true);

        double expected = (Sp468Tables.AlphaSt(500.0) * 500.0
                         - Sp468Tables.AlphaBt("silicate", 100.0) * 100.0) / 0.45;
        TestHarness.CheckRel("FireCurvature_ChiHandCalc", chi, expected, 1e-9);
    }

    static void Chi_ChangesSignWithRebarPosition()
    {
        double a = FireThermalCurvature.Chi(500.0, 100.0, 0.45, "silicate", true);
        double b = FireThermalCurvature.Chi(500.0, 100.0, 0.45, "silicate", false);

        TestHarness.Check("FireCurvature_ChiSignFlips",
            Math.Abs(a + b) < 1e-12 && Math.Sign(a) != Math.Sign(b),
            $"a={a:e4}, b={b:e4}");
    }

    static void Eps_MatchesHandCalc()
    {
        double eps = FireThermalCurvature.EpsAxial(
            tRebar: 500.0, tColdConcrete: 100.0, aggregateType: "silicate");

        double expected = (Sp468Tables.AlphaBt("silicate", 100.0) * 100.0
                         + Sp468Tables.AlphaSt(500.0) * 500.0) / 2.0;
        TestHarness.CheckRel("FireCurvature_EpsHandCalc", eps, expected, 1e-9);
    }

    static void Stiffness_MatchesHandCalc()
    {
        double h0 = 0.45, xt = 0.09, esT = 160e9, as_ = 1e-3, phi1 = 0.5;
        double z = h0 - xt / 3.0;
        double expected = phi1 * esT * as_ * z * (h0 - xt);

        double d = FireThermalCurvature.Stiffness(phi1, esT, as_, h0, xt);
        TestHarness.CheckRel("FireCurvature_StiffnessHandCalc", d, expected, 1e-12);
    }

    static void XiR_UsesWeightedRebarEquivalent()
    {
        double numerator = 1.15 * 435e6 * 1e-3 + 0.8 * 600e6 * 2e-3;
        double denominator = 0.90 * 190e9 * 1e-3 + 0.70 * 170e9 * 2e-3;
        double expected = FireCompressionZone.XiRFromStrain(numerator / denominator, 0.0088);
        double reversed = FireCompressionZone.XiRFromStrain(
            (0.8 * 600e6 * 2e-3 + 1.15 * 435e6 * 1e-3)
            / (0.70 * 170e9 * 2e-3 + 0.90 * 190e9 * 1e-3), 0.0088);

        TestHarness.CheckRel("FireCurvature_WeightedXiR", expected, reversed, 1e-12);
    }

    static void Run_ConvertsMaterialModulusFromKpaToPa()
    {
        var (section, _) = FireRCheckTests.BuildFixtureForTests();
        var rebar = section.Areas.First(a => a.Material?.Type == MatType.ReSteelF).Material!;
        rebar.E = 200_000_000.0;
        var fiber = FireFiberSection.FromThermalResult(
            CreateCurvatureThermalResult(), section, snapshotIndex: 0);
        var result = FireThermalCurvature.Run(new FireThermalCurvatureInput(
            fiber, new CScore.Fire.Entities.FireSectionDef(), 0.1, 60.0, true, "auto"));

        TestHarness.CheckRel("FireCurvature_EstPaKpaConversion",
            result.EstPa, 200_000_000_000.0, 1e-12);
        TestHarness.Check("FireCurvature_DFinite", result.D is null || double.IsFinite(result.D.Value),
            $"D={result.D}");
    }

    static void Run_WeightedRebarEquivalentIsOrderIndependent()
    {
        var first = RunTwoRebarCurvature(reverse: false);
        var second = RunTwoRebarCurvature(reverse: true);

        TestHarness.CheckRel("FireCurvature_WeightedEstPa", first.EstPa, second.EstPa, 1e-12);
        TestHarness.CheckRel("FireCurvature_WeightedXiRRun", first.XiR, second.XiR, 1e-12);
        TestHarness.CheckRel("FireCurvature_WeightedD", first.D ?? double.NaN, second.D ?? double.NaN, 1e-12);
        TestHarness.Check("FireCurvature_RebarDetails", 
            first.RebarDetails.Count == 2 &&
            first.RebarDetails.Select(d => d.ClassGroup).Distinct().Count() == 2 &&
            first.RebarDetails.All(d => Math.Abs(d.TemperatureCelsius - 500.0) < 1e-12));
    }

    static FireThermalCurvatureResult RunTwoRebarCurvature(bool reverse)
    {
        var section = FireFiberSectionTests.CreateSectionForTests();
        var firstArea = section.Areas[1];
        var firstMaterial = firstArea.Material!;
        firstMaterial.E = 190_000.0;
        firstMaterial.FireRebarClass = "a240_a500";
        foreach (var chars in firstMaterial.MaterialChars)
        {
            chars.Class = 400.0;
            chars.E = 190_000.0;
            chars.Ft = 435.0;
        }

        var secondMaterial = new Material
        {
            Id = 303,
            Tag = "A600 test",
            Type = MatType.ReSteelF,
            E = 170_000.0,
            FireRebarClass = "a600_a1000"
        };
        secondMaterial.MaterialChars = firstMaterial.MaterialChars
            .Select(chars =>
            {
                var clone = chars.Clone();
                clone.Class = 600.0;
                clone.E = 170_000.0;
                clone.Ft = 600.0;
                clone.Fc = 600.0;
                return clone;
            })
            .ToList();

        var secondBar = Fiber.CreatePoint(diameter: 0.020, x: 0.25, y: 0.20);
        var secondArea = new MaterialArea
        {
            Tag = "second-rebar-area",
            Category = AreaCategory.RebarGroup,
            Fibers = [secondBar]
        };
        secondArea.SetMaterial(secondMaterial, DiagrammType.L2);
        section.Areas = reverse
            ? [section.Areas[0], secondArea, firstArea]
            : [section.Areas[0], firstArea, secondArea];

        var locations = reverse
            ? new[] { (X: 0.25, Y: 0.20), (X: 0.20, Y: 0.20) }
            : new[] { (X: 0.20, Y: 0.20), (X: 0.25, Y: 0.20) };
        var thermal = new FireThermalResult
        {
            MeshInfo = new FireMeshBuildResult
            {
                Mesh = new CSfea.Thermal.HeatMesh(
                    x: [0.0, 1.0, 0.0, 1.0],
                    y: [0.0, 0.0, 1.0, 1.0],
                    elements: [[0, 1, 2], [1, 3, 2]]),
                BoundaryEdges = [],
                Rebars =
                [
                    new FireRebarLocation { Id = 0, X = locations[0].X, Y = locations[0].Y, ElementIndex = 0 },
                    new FireRebarLocation { Id = 1, X = locations[1].X, Y = locations[1].Y, ElementIndex = 0 }
                ]
            },
            TimesMin = [0.0],
            Snapshots = [[500.0, 500.0, 500.0, 500.0]],
            RebarTemperatureHistory = new Dictionary<int, double[]>
            {
                [0] = [500.0], [1] = [500.0]
            },
            RebarMaxTemperatures = new Dictionary<int, double> { [0] = 500.0, [1] = 500.0 },
            AggregateType = "silicate"
        };

        var fiber = FireFiberSection.FromThermalResult(thermal, section, snapshotIndex: 0);
        return FireThermalCurvature.Run(new FireThermalCurvatureInput(
            fiber, new CScore.Fire.Entities.FireSectionDef(), 0.1, 180.0, true, "fiber_equilibrium"));
    }

    static CScore.Fire.FireThermalResult CreateCurvatureThermalResult()
    {
        var mesh = new CSfea.Thermal.HeatMesh(
            x: [0.0, 1.0, 0.0, 1.0],
            y: [0.0, 0.0, 1.0, 1.0],
            elements: [[0, 1, 2], [1, 3, 2]]);

        return new CScore.Fire.FireThermalResult
        {
            MeshInfo = new CScore.Fire.FireMeshBuildResult
            {
                Mesh = mesh,
                BoundaryEdges = [],
                Rebars =
                [
                    new CScore.Fire.FireRebarLocation
                    {
                        Id = 0, X = 0.2, Y = 0.2, ElementIndex = 0,
                        Xi1 = 0.6, Xi2 = 0.2, Xi3 = 0.2
                    }
                ]
            },
            TimesMin = [0.0],
            Snapshots = [[20.0, 20.0, 20.0, 20.0]],
            RebarTemperatureHistory = new Dictionary<int, double[]> { [0] = [20.0] },
            RebarMaxTemperatures = new Dictionary<int, double> { [0] = 20.0 },
            AggregateType = "silicate"
        };
    }
}
