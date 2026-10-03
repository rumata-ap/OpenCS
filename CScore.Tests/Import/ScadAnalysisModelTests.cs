using CScore.Import;
using Xunit;

namespace CScore.Tests.Import;

public class ScadAnalysisModelTests
{
    [Theory]
    [InlineData("SPRING 1  1  1  1  1  1 Type 100", 0b111111)]
    [InlineData("SPRING 1 1 1 0 0 0 Type 100", 0b000111)]
    [InlineData("SPRING 0 0 1 1 1 0", 0b011100)]
    [InlineData("GE 3e10 0.2 0.2", 0b111111)]
    [InlineData(null, 0b111111)]
    public void RigidBodyMask_FromStiffnessText(string? text, int expected) =>
        Assert.Equal(expected, ScadAnalysisModel.RigidBodyMask(text));

    [Fact]
    public void Json_RoundTrip()
    {
        var model = new ScadAnalysisModel
        {
            Bounds = { [1125] = 0x3F, [7] = 0b100 },
            RigidBodies = { new ScadRigidBody(1046, 3, 5, [9, 13, 17], 0x3F) },
            LoadCases =
            {
                new ScadLoadCase(1, "собств. вес", [], [new ScadLoadRecord(96, 3, [1.0], [1, 2, 3])], []),
                new ScadLoadCase(2, "0,89 т/м2", [], [new ScadLoadRecord(16, 3, [8730.9], [1, 2])], []),
            },
            LengthUnitM = 1,
            ForceUnitN = 1,
        };

        var back = ScadAnalysisModel.FromJson(model.ToJson());

        Assert.Equal(model.Bounds, back.Bounds);
        var body = Assert.Single(back.RigidBodies);
        Assert.Equal(5, body.MasterNode);
        Assert.Equal([9, 13, 17], body.SlaveNodes);
        Assert.Equal(2, back.LoadCases.Count);
        var load = back.LoadCases[1].ElementLoads.Single();
        Assert.Equal((16, 3), (load.Qw, load.Qn));
        Assert.Equal(8730.9, load.Data[0]);
        Assert.Equal("0,89 т/м2", back.LoadCases[1].Name);
    }

    [Fact]
    public void Displacements_RoundTripAndLookup()
    {
        var set = new ScadDisplacementSet
        {
            Rows = { new ScadDisplacementRow(2, 1, "L2", 0, [510, 511], [0, 0, -0.006873, 1.25e-4, -1.25e-4, 0, 0, 0, -0.005, 0, 0, 0]) },
        };

        var back = ScadDisplacementSet.FromJson(set.ToJson());

        Assert.Equal(-0.006873, back.Rows[0].Of(510)![2]);
        Assert.Equal(-0.005, back.Rows[0].Of(511)![2]);
        Assert.Null(back.Rows[0].Of(1));
    }

    [Fact]
    public void FromJson_Corrupt_Throws() =>
        Assert.Throws<InvalidDataException>(() => ScadAnalysisModel.FromJson("{"));
}
