using System.Text.Json;
using System.Text.Json.Serialization;
using CScore.Fem;
using CScore.Planar;
using CScore.Submodel;
using Xunit;

namespace CScore.Tests.Submodel;

public sealed class BoundaryScenarioModelTests
{
    /// <summary>Те же настройки, что у DatabaseService для JSON-колонок.</summary>
    static readonly JsonSerializerOptions DbOptions = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = false
    };

    [Fact]
    public void Dof6_Arithmetic_Indexer_AndParts()
    {
        var a = new Dof6(1, 2, 3, 4, 5, 6);
        var b = Dof6.FromParts(new PlanarVector3(-1, 0, 1), new PlanarVector3(10, -20, 0.5));

        Assert.Equal(new Dof6(0, 2, 4, 14, -15, 6.5), a + b);
        Assert.Equal(new Dof6(2, 2, 2, -6, 25, 5.5), a - b);
        Assert.Equal(new Dof6(-1, -2, -3, -4, -5, -6), -a);
        Assert.Equal(new Dof6(2, 4, 6, 8, 10, 12), a.Scale(2));
        Assert.Equal(new[] { 1.0, 2, 3, 4, 5, 6 }, Enumerable.Range(0, 6).Select(i => a[i]));
        Assert.Equal(new PlanarVector3(4, 5, 6), a.Moment);
        Assert.Equal(1, b.MaxForceAbs);
        Assert.Equal(20, b.MaxMomentAbs);
        Assert.Throws<ArgumentOutOfRangeException>(() => a[6]);
    }

    [Fact]
    public void Scenario_JsonRoundTrip_PreservesContent()
    {
        var scenario = new BoundaryScenario(
            ExtractionId: 5, ReferenceScale: 1.0, Status: ScenarioStatus.Incomplete,
            LoadCompleteness: LoadCompleteness.Unknown,
            RetainedDistributedLoads: [new("12", 0, 0.5, new PlanarVector3(0, 0, -10), new PlanarVector3(0, 0, -12), 7, "3")],
            RetainedPointLoads: [new("13", 0.25, new PlanarVector3(0, 0, -100), 8, "3")],
            RetainedNodalLoads: [new("21", new Dof6(0, 0, -5, 0, 1, 0), new NodalLoadSource("node_load", 42, "N7"))],
            RetainedKinematicLoads: [new("22", 2, -0.001, 43)],
            LoadAccounting: new LoadAccounting(4, 1, 9),
            Ends:
            [
                new ScenarioEnd(true, "20", "101",
                    [.. Enumerable.Repeat(new DofAssignment(DofMode.Force, 1.5, DofSource.Auto), 5),
                     new DofAssignment(DofMode.Fixed, null, DofSource.Override)],
                    new Dof6(1, 2, 3, 4, 5, 6),
                    [new InterfaceActionContribution(new Dof6(1, 2, 3, 4, 5, 6), "beam_end", "55", "localForce", ConversionQuality.Exact)],
                    null, new Dof6(0.001, 0, 0, 0, 0, 0),
                    new ControlCheck(new Dof6(1, 2, 3, 4, 5, 6), 0, 0, true, true))
            ],
            Diagnostics: [new FemValidationDiagnostic(BoundaryScenarioDiagnostics.LoadsUnknown, "нет нагрузок", false, ["S1"])]);

        string json = JsonSerializer.Serialize(scenario, DbOptions);
        var restored = JsonSerializer.Deserialize<BoundaryScenario>(json, DbOptions)!;

        Assert.Equal(json, JsonSerializer.Serialize(restored, DbOptions));
        Assert.Contains("\"Incomplete\"", json);
        Assert.Contains("\"Override\"", json);
        Assert.Equal(new Dof6(1, 2, 3, 4, 5, 6), restored.Ends[0].BoundaryVector);
        Assert.Null(restored.Ends[0].Reaction);
        Assert.Equal(-12, restored.RetainedDistributedLoads[0].QAtB.Z);
    }
}
