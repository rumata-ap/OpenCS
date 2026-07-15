using System.Globalization;
using OpenCS.OpenSees.Analysis;
using OpenCS.OpenSees.Model;
using OpenCS.OpenSees.Tcl;

namespace OpenCS.OpenSees.Tests;

public sealed class TclGeneratorTests
{
    [Fact]
    public void Generator_emits_deterministic_two_fiber_moment_curvature_script()
    {
        OpenSeesSectionModel model = CreateModel();
        SectionAnalysisRequest request = new()
        {
            AxialForceN = 1_000,
            MaxCurvature = 0.01,
            Increments = 2,
            Axis = SectionBendingAxis.Mx
        };

        string script = new SectionMomentCurvatureTclGenerator().Generate(model, request);

        Assert.Contains("section Fiber 1 -GJ 12.5 {", script);
        Assert.Contains($"fiber {TclNumber.Format(0.3)} {TclNumber.Format(0.2)} {TclNumber.Format(0.01)} 1", script);
        Assert.Contains($"fiber {TclNumber.Format(-0.3)} {TclNumber.Format(-0.2)} {TclNumber.Format(0.02)} 2", script);
        Assert.Contains("node 1 0 0", script);
        Assert.Contains("node 2 0 0", script);
        Assert.Contains("element zeroLengthSection 1 1 2 1", script);
        Assert.Contains($"integrator DisplacementControl 2 3 {TclNumber.Format(0.005)}", script);
        Assert.Contains("section_history.out", script);
        Assert.Contains("fiber_history.out", script);
        Assert.Contains("completed.marker", script);
        Assert.Contains("wipe", script);
    }

    [Fact]
    public void Generator_uses_invariant_decimal_format_under_comma_culture()
    {
        CultureInfo original = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("ru-RU");

            string script = new SectionMomentCurvatureTclGenerator().Generate(
                CreateModel(),
                new SectionAnalysisRequest { MaxCurvature = 0.0015, Increments = 3 });

            Assert.DoesNotContain("0,0015", script);
            Assert.Contains(TclNumber.Format(0.0005), script);
        }
        finally
        {
            CultureInfo.CurrentCulture = original;
        }
    }

    [Fact]
    public void Generator_contains_only_relative_fixed_artifact_file_names()
    {
        string script = new SectionMomentCurvatureTclGenerator().Generate(
            CreateModel(),
            new SectionAnalysisRequest { MaxCurvature = 0.001, Increments = 1 });

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
