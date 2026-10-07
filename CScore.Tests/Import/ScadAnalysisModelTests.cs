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

    [Theory]
    [InlineData("SPRING 0  0  9.81e+06 Type 51 Name File", 5, new[] { 0, 0, 9.81e6, 0, 0, 0 })]
    [InlineData("SPRING 1e6 2e6 3e6 4 5 6 Type 51", 5, new[] { 1e6, 2e6, 3e6, 4, 5, 6 })]
    [InlineData("SPRING 1e6 0 7 Type 51", 2, new[] { 1e6, 0, 0, 0, 7, 0 })]
    [InlineData("SPRING 0 5e5 Type 51", 2, new[] { 0, 0, 5e5, 0, 0, 0 })]
    [InlineData("SPRING 2e7 3 4 Type 51", 3, new[] { 0, 0, 2e7, 3, 4, 0 })]
    public void SpringStiffness_BySchemaType(string text, int schemaType, double[] expected) =>
        Assert.Equal(expected, ScadAnalysisModel.SpringStiffness(text, schemaType));

    [Theory]
    [InlineData("SPRING 1 1 1 1 1 1 Type 100", 5)]
    [InlineData("SPRING 9.81e+06 9.81e+06 9.81e+06 39.24 Type 55 DAMPER 0.01", 5)]
    [InlineData("SPRING 1 2 3 4 Type 51", 2)]
    [InlineData("GE 3e10 0.2 0.2", 5)]
    [InlineData(null, 5)]
    public void SpringStiffness_NotSpring51_Null(string? text, int schemaType) =>
        Assert.Null(ScadAnalysisModel.SpringStiffness(text, schemaType));

    [Fact]
    public void Json_BoundaryV2_RoundTrip_AndOldAttachmentReadsAsV1()
    {
        var model = new ScadAnalysisModel
        {
            SchemaType = 5, HasBoundaryV2 = true,
            Springs = { new ScadSpring(7, 26, 101, [0, 0, 9.81e6, 0, 0, 0]) },
            Joints = { new ScadJoint(12, 0b110000, 0) },
            NotTransferred = { [ScadNotTransferredKinds.Bed] = 815 },
        };

        var back = ScadAnalysisModel.FromJson(model.ToJson());

        Assert.True(back.HasBoundaryV2);
        Assert.Equal(5, back.SchemaType);
        Assert.Equal(9.81e6, Assert.Single(back.Springs).K[2]);
        Assert.Equal(new ScadJoint(12, 0b110000, 0), Assert.Single(back.Joints));
        Assert.Equal(815, back.NotTransferred[ScadNotTransferredKinds.Bed]);

        var old = ScadAnalysisModel.FromJson("""{"Bounds":{"5":63},"RigidBodies":[],"LoadCases":[],"LengthUnitM":1,"ForceUnitN":1}""");
        Assert.False(old.HasBoundaryV2);
        Assert.Empty(old.Springs);
        Assert.Empty(old.Joints);
        Assert.Equal(63, old.Bounds[5]);
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
