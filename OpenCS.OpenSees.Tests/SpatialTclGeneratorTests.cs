using System.Globalization;
using OpenCS.OpenSees.Analysis;
using OpenCS.OpenSees.Model;
using OpenCS.OpenSees.Tcl;

namespace OpenCS.OpenSees.Tests;

public sealed class SpatialTclGeneratorTests
{
    [Fact]
    public void Generator_emits_deterministic_three_dimensional_script_and_component_mapping()
    {
        OpenSeesSectionModel model = CreateModel();
        SpatialSectionAnalysisRequest request = new()
        {
            AxialForceN = 1_000,
            AngleDegrees = 90,
            MaxCurvature = 0.01,
            Increments = 2
        };

        string script = new SpatialSectionTclGenerator().Generate(model, request);

        Assert.Contains("model basic -ndm 3 -ndf 6", script);
        Assert.Contains("fix 1 1 1 1 1 1 1", script);
        Assert.Contains("fix 2 0 1 1 1 0 0", script);
        Assert.Contains("element zeroLengthSection 1 1 2 1", script);
        Assert.Contains("sp 2 5", script);
        Assert.Contains("sp 2 6", script);
        Assert.Contains("section_history.out", script);
        Assert.Contains("completed.marker", script);
        Assert.Contains("set momentMx [lindex $forces 1]", script);
        Assert.Contains("set momentMy [lindex $forces 2]", script);
        Assert.Contains("set curvatureMx [lindex [nodeDisp 2 6] 0]", script);
        Assert.Contains("set curvatureMy [lindex [nodeDisp 2 5] 0]", script);
        Assert.Contains("load 2 1000 0 0 0 0 0", script);
        Assert.DoesNotContain("-ndm 2 -ndf 3", script);

        int firstFiber = script.IndexOf(
            $"fiber {TclNumber.Format(0.3)} {TclNumber.Format(0.2)} {TclNumber.Format(0.01)} 1",
            StringComparison.Ordinal);
        int secondFiber = script.IndexOf(
            $"fiber {TclNumber.Format(-0.3)} {TclNumber.Format(-0.2)} {TclNumber.Format(0.02)} 2",
            StringComparison.Ordinal);
        Assert.True(firstFiber >= 0 && secondFiber > firstFiber);
        Assert.Contains("uniaxialMaterial ElasticMultiLinear 1", script);
        Assert.Contains("uniaxialMaterial ElasticMultiLinear 2", script);
    }

    [Fact]
    public void Generator_uses_invariant_decimal_format_under_comma_culture()
    {
        CultureInfo original = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("ru-RU");

            string script = new SpatialSectionTclGenerator().Generate(
                CreateModel(),
                new SpatialSectionAnalysisRequest
                {
                    AxialForceN = 1_000.5,
                    AngleDegrees = 0,
                    MaxCurvature = 0.0015,
                    Increments = 3
                });

            Assert.DoesNotContain("1000,5", script);
            Assert.DoesNotContain("0,0015", script);
            Assert.Contains(TclNumber.Format(1_000.5), script);
            Assert.Contains(TclNumber.Format(0.0015), script);
        }
        finally
        {
            CultureInfo.CurrentCulture = original;
        }
    }

    [Fact]
    public void Generator_contains_only_relative_fixed_artifact_file_names()
    {
        string script = new SpatialSectionTclGenerator().Generate(
            CreateModel(),
            new SpatialSectionAnalysisRequest { AxialForceN = 1_000, Increments = 1 });

        Assert.DoesNotContain("..", script);
        Assert.DoesNotContain("C:\\", script, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("/tmp/", script, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("run_manifest.json", script);
    }

    private static OpenSeesSectionModel CreateModel() => new()
    {
        GJ = 12.5,
        Materials =
        [
            new OpenSeesMaterialDefinition
            {
                Tag = 1,
                SourceId = "concrete",
                PositiveEnvelope = [new EnvelopePoint(0, 0), new EnvelopePoint(0.001, 1_000_000)],
                NegativeEnvelope = [new EnvelopePoint(-0.001, -1_000_000), new EnvelopePoint(0, 0)]
            },
            new OpenSeesMaterialDefinition
            {
                Tag = 2,
                SourceId = "steel",
                PositiveEnvelope = [new EnvelopePoint(0, 0), new EnvelopePoint(0.002, 2_000_000)],
                NegativeEnvelope = [new EnvelopePoint(-0.002, -2_000_000), new EnvelopePoint(0, 0)]
            }
        ],
        Fibers =
        [
            new OpenSeesFiber(0.3, 0.2, 0.01, 1),
            new OpenSeesFiber(-0.3, -0.2, 0.02, 2)
        ]
    };
}
