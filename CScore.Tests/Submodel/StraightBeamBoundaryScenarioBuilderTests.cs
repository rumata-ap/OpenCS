using CScore.Fem;
using CScore.Planar;
using CScore.Submodel;
using Xunit;
using static CScore.Tests.Submodel.BoundaryTestModels;

namespace CScore.Tests.Submodel;

/// <summary>
/// Аналитический эталон: шарнирно опёртая балка вдоль X длиной L = 8 м под q = −10 кН/м (глобальная Z),
/// сетка 201..204 по 2 м, извлекается участок x = 2..6 (элементы 202, 203).
/// </summary>
public sealed class StraightBeamBoundaryScenarioBuilderTests
{
    const double Q = -10_000;          // Н/м, вниз
    const double Length = 8;
    const double Ra = -Q * Length / 2; // 40 000 Н — реакция каждой опоры, вверх

    /// <summary>Изгибающий момент, положительный — растянут низ: M(x) = Ra·x + q·x²/2.</summary>
    static double M(double x) => Ra * x + Q * x * x / 2;

    /// <summary>
    /// Концевые усилия элемента [xa, xb] в конвенции localForce (силы, с которыми узлы действуют на элемент),
    /// в локальных осях стержня вдоль X (x = X, y = Z, z = −Y).
    /// Сила, с которой левая часть действует на правую в сечении x: Fz = Ra + q·x, момент My = +M(x);
    /// правая на левую — с обратным знаком. На конце i узел передаёт действие левой части: Fz = Ra + q·xa,
    /// My = +M(xa); на конце j — действие правой: Fz = −(Ra + q·xb), My = −M(xb).
    /// Локально: Qy = Fz (y = Z), Mz = −My (глобальная Y = −z).
    /// </summary>
    static BeamEndForces EndForces(double xa, double xb) => new(
        new Dof6(0, Ra + Q * xa, 0, 0, 0, -M(xa)),
        new Dof6(0, -(Ra + Q * xb), 0, 0, 0, M(xb)));

    static DictionaryParentLinearResult Result(bool isLinear = true) => new(isLinear,
        Enumerable.Range(0, 5).ToDictionary(i => (101 + i).ToString(), i => new Dof6(0, 0, -0.001 * i * (4 - i), 0, 0.0005 * (2 - i), 0)),
        new Dictionary<string, Dof6> { ["101"] = new(0, 0, Ra, 0, 0, 0), ["105"] = new(0, 0, Ra, 0, 0, 0) },
        Enumerable.Range(0, 4).ToDictionary(i => (201 + i).ToString(), i => EndForces(2 * i, 2 * i + 2)));

    static BoundaryScenarioBuildResult Build((string, bool)[] chainSpec, string sourceType = "internal",
        IParentLinearResult? result = null, IReadOnlyList<DofOverride>? overrides = null)
    {
        var parent = Beam();
        parent.Nodes[0].DofMask = 0b000111;
        parent.Nodes[1].DofMask = 0b000110;
        return StraightBeamBoundaryScenarioBuilder.Build(new BoundaryScenarioInput(
            Extraction(parent, chainSpec), sourceType, parent.Nodes, parent.Members, parent.MeshNodes, parent.MeshElements,
            [new FemLoadCase { Id = 1, Tag = "G" }], [],
            [new FemMemberLoad { Id = 1, LoadCaseId = 1, MemberId = 10, DistributionType = "uniform", CoordinateSystem = "global", QzStart = Q }],
            [], result ?? Result(), overrides ?? []));
    }

    static ScenarioEnd EndAt(BoundaryScenario scenario, string parentTag) =>
        scenario.Ends.Single(e => e.ParentNodeTag == parentTag);

    [Fact]
    public void Middle_BoundaryVectors_MatchStaticsAndControl()
    {
        var build = Build(MiddleChain);
        var scenario = build.Scenario;

        Assert.Equal(ScenarioStatus.Complete, scenario.Status);
        var start = EndAt(scenario, "102");
        var end = EndAt(scenario, "104");
        // Левый конец (x = 2): действие отброшенной левой части Fz = Ra + q·2 = 20 кН, My = M(2) = 60 кН·м.
        AssertDof(new Dof6(0, 0, 20_000, 0, 60_000, 0), start.BoundaryVector!);
        // Правый конец (x = 6): действие правой части на левую — Fz = −(Ra + q·6) = 20 кН, My = −M(6) = −60 кН·м.
        AssertDof(new Dof6(0, 0, 20_000, 0, -60_000, 0), end.BoundaryVector!);
        Assert.True(start.Control.Passed);
        Assert.True(end.Control.Passed);
        Assert.All(scenario.Ends.SelectMany(e => e.Dofs), d => Assert.Equal(DofMode.Force, d.Mode));
        Assert.Contains(build.Diagnostics, d => d.Code == BoundaryScenarioDiagnostics.GaugeRequired && !d.IsError);
    }

    [Fact]
    public void Middle_SaggingMomentSign_TensionAtBottomFace()
    {
        // Знак по конвенции, а не по совпадению с контролем. На левый торец субмодели действует момент
        // +My·ŷ (левая часть на правую). Правило правой руки вокруг +Y переводит +Z в +X: верхние волокна
        // левого торца вдавливаются внутрь балки (сжатие), нижние вытягиваются (растяжение) — пролётный
        // момент M(2) = +60 кН·м растягивает низ. Для правого торца действие правой части — −My·ŷ, тот же
        // изгиб. Итого: My(начало) > 0, My(конец) < 0 ⇔ растянута нижняя грань, как у шарнирной балки.
        var scenario = Build(MiddleChain).Scenario;

        Assert.True(EndAt(scenario, "102").BoundaryVector!.Ry > 0);
        Assert.True(EndAt(scenario, "104").BoundaryVector!.Ry < 0);
    }

    [Fact]
    public void Middle_SubmodelIsInEquilibrium()
    {
        var scenario = Build(MiddleChain).Scenario;
        var parent = Beam();
        var nodeX = parent.MeshNodes.ToDictionary(n => n.NodeTag, n => new PlanarVector3(n.X, n.Y, n.Z));
        var origin = nodeX["102"];

        var force = PlanarVector3.Zero;
        var moment = PlanarVector3.Zero;
        foreach (var end in scenario.Ends)
        {
            var v = end.BoundaryVector!;
            force += v.Force;
            moment += v.Moment + (nodeX[end.ParentNodeTag] - origin).Cross(v.Force);
        }
        foreach (var load in scenario.RetainedDistributedLoads)
        {
            var ends = FemMeshTopology.ReadNodeTags(parent.MeshElements.Single(e => e.ElemTag == load.ChildElementTag))!;
            var i = nodeX[ends[0]];
            var j = nodeX[ends[1]];
            double length = (j - i).Length;
            double a = load.AOverL * length, b = load.BOverL * length;
            // Трапеция qA→qB на [a, b]: равнодействующая и точка приложения вдоль оси элемента.
            var resultant = (load.QAtA + load.QAtB) * ((b - a) / 2);
            var axis = (j - i) * (1.0 / length);
            double qa = load.QAtA.Length, qb = load.QAtB.Length;
            double centroid = qa + qb < 1e-12 ? (a + b) / 2 : a + (b - a) * (qa + 2 * qb) / (3 * (qa + qb));
            force += resultant;
            moment += (i + axis * centroid - origin).Cross(resultant);
        }

        Assert.Equal(0, force.Length, 6);
        Assert.Equal(0, moment.Length, 6);
    }

    [Fact]
    public void ReversedChain_GivesSameGlobalBoundaryVectors()
    {
        var forward = Build(MiddleChain).Scenario;
        var reversed = Build(MiddleChainReversed).Scenario;

        Assert.Equal("104", reversed.Ends.Single(e => e.AtStart).ParentNodeTag);
        foreach (var tag in new[] { "102", "104" })
            AssertDof(EndAt(forward, tag).BoundaryVector!, EndAt(reversed, tag).BoundaryVector!);
    }

    [Fact]
    public void ChainAtSupport_FixedDofsAndControlWithReaction()
    {
        // Цепочка 201-202: начало в опоре 101 (Ux, Uy, Uz закреплены).
        var scenario = Build([("201", false), ("202", false)]).Scenario;
        var start = EndAt(scenario, "101");

        Assert.Equal(new[] { DofMode.Fixed, DofMode.Fixed, DofMode.Fixed, DofMode.Force, DofMode.Force, DofMode.Force },
            start.Dofs.Select(d => d.Mode));
        Assert.True(start.Control.Passed);
        Assert.Equal(ScenarioStatus.Complete, scenario.Status);
        Assert.DoesNotContain(scenario.Diagnostics, d => d.Code == BoundaryScenarioDiagnostics.GaugeRequired);
    }

    [Fact]
    public void NonlinearParent_IsBlocked()
    {
        var build = Build(MiddleChain, result: Result(isLinear: false));

        Assert.Equal(ScenarioStatus.Blocked, build.Scenario.Status);
        Assert.Contains(build.Diagnostics, d => d.Code == BoundaryScenarioDiagnostics.ParentResultNotLinear);
        Assert.Empty(build.Scenario.Ends);
    }

    [Fact]
    public void ImportedParent_IsIncomplete()
    {
        var scenario = Build(MiddleChain, sourceType: "lira").Scenario;

        Assert.Equal(ScenarioStatus.Incomplete, scenario.Status);
        Assert.Equal(LoadCompleteness.Unknown, scenario.LoadCompleteness);
    }

    static void AssertDof(Dof6 expected, Dof6 actual)
    {
        for (int k = 0; k < 6; k++) Assert.Equal(expected[k], actual[k], 6);
    }
}
