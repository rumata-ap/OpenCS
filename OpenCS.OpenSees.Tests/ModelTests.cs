using System.Text.Json;
using OpenCS.OpenSees.Model;

namespace OpenCS.OpenSees.Tests;

public sealed class ModelTests
{
    [Fact]
    public void Valid_section_passes_validation_and_preserves_input_order()
    {
        OpenSeesFiber firstFiber = new(0.1, 0.2, 0.01, 1);
        OpenSeesFiber secondFiber = new(-0.1, -0.2, 0.02, 2);

        OpenSeesSectionModel model = new()
        {
            Materials =
            [
                CreateMaterial(1, "concrete"),
                CreateMaterial(2, "steel")
            ],
            Fibers = [firstFiber, secondFiber],
            GJ = 1.0
        };

        model.Validate();

        Assert.Equal(firstFiber, model.Fibers[0]);
        Assert.Equal(secondFiber, model.Fibers[1]);
        Assert.Same(OpenSeesCoordinateConvention.CScoreDefault, model.Convention);
    }

    [Theory]
    [MemberData(nameof(InvalidModels))]
    public void Invalid_section_values_are_rejected(OpenSeesSectionModel model)
    {
        ArgumentException exception = Assert.Throws<ArgumentException>(model.Validate);

        Assert.False(string.IsNullOrWhiteSpace(exception.Message));
    }

    [Fact]
    public void Identical_ordered_models_have_same_fingerprint_and_reordered_fibers_do_not()
    {
        OpenSeesSectionModel first = CreateModel(
            new OpenSeesFiber(0.1, 0.2, 0.01, 1),
            new OpenSeesFiber(-0.1, -0.2, 0.02, 1));
        OpenSeesSectionModel same = CreateModel(
            new OpenSeesFiber(0.1, 0.2, 0.01, 1),
            new OpenSeesFiber(-0.1, -0.2, 0.02, 1));
        OpenSeesSectionModel reordered = CreateModel(
            new OpenSeesFiber(-0.1, -0.2, 0.02, 1),
            new OpenSeesFiber(0.1, 0.2, 0.01, 1));

        string firstFingerprint = Fingerprint(first);

        Assert.Equal(firstFingerprint, Fingerprint(same));
        Assert.NotEqual(firstFingerprint, Fingerprint(reordered));
    }

    public static IEnumerable<object[]> InvalidModels()
    {
        yield return [CreateModel(new OpenSeesFiber(0, 0, 0, 1))];
        yield return
        [
            new OpenSeesSectionModel
            {
                Materials = [CreateMaterial(1, "one"), CreateMaterial(1, "duplicate")],
                Fibers = [new OpenSeesFiber(0, 0, 0.01, 1)]
            }
        ];
        yield return [CreateModel(new OpenSeesFiber(double.NaN, 0, 0.01, 1))];
        yield return
        [
            new OpenSeesSectionModel
            {
                Materials =
                [
                    new OpenSeesMaterialDefinition
                    {
                        Tag = 1,
                        SourceId = "empty"
                    }
                ],
                Fibers = [new OpenSeesFiber(0, 0, 0.01, 1)]
            }
        ];
    }

    private static OpenSeesSectionModel CreateModel(params OpenSeesFiber[] fibers) => new()
    {
        Materials = [CreateMaterial(1, "material")],
        Fibers = fibers
    };

    private static OpenSeesMaterialDefinition CreateMaterial(int tag, string sourceId) => new()
    {
        Tag = tag,
        SourceId = sourceId,
        SourceType = "test",
        PositiveEnvelope =
        [
            new EnvelopePoint(0, 0),
            new EnvelopePoint(0.001, 30_000_000)
        ],
        NegativeEnvelope =
        [
            new EnvelopePoint(-0.001, -30_000_000),
            new EnvelopePoint(0, 0)
        ]
    };

    private static string Fingerprint(OpenSeesSectionModel model) =>
        JsonSerializer.Serialize(new
        {
            model.Materials,
            model.Fibers,
            model.GJ,
            Convention = new
            {
                model.Convention.YFrom,
                model.Convention.ZFrom,
                model.Convention.AxialForce,
                model.Convention.BendingAboutY,
                model.Convention.BendingAboutZ,
                model.Convention.Torsion,
                model.Convention.AxialStrain,
                model.Convention.CurvatureAboutY,
                model.Convention.CurvatureAboutZ,
                model.Convention.Twist
            }
        });
}
