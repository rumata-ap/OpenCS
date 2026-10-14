using System.Windows.Media.Media3D;
using OpenCS.ViewModels;
using Xunit;

namespace OpenCS.Tests;

public class FemTransformCommandTests
{
    [Theory]
    [InlineData("1;2;3", "abs", 1, 2, 3)]
    [InlineData(" 1,5 ; -2 ", "abs", 1.5, -2, 0)]
    [InlineData("@0;0;3,2", "rel", 0, 0, 3.2)]
    [InlineData("@1;2", "rel", 1, 2, 0)]
    [InlineData("2.5", "dist", 2.5, 0, 0)]
    public void Parse_RecognizesPointForms(string text, string kind, double a, double b, double c)
    {
        var input = FemPointInput.Parse(text);
        switch (kind)
        {
            case "abs": Assert.Equal(new Point3D(a, b, c), Assert.IsType<FemPointInput.Absolute>(input).Point); break;
            case "rel": Assert.Equal(new Vector3D(a, b, c), Assert.IsType<FemPointInput.Relative>(input).Offset); break;
            default: Assert.Equal(a, Assert.IsType<FemPointInput.Distance>(input).Value); break;
        }
    }

    [Theory]
    [InlineData("", typeof(FemPointInput.Empty))]
    [InlineData("М", typeof(FemPointInput.ArrayOption))]
    [InlineData("m", typeof(FemPointInput.ArrayOption))]
    [InlineData("1;2;3;4", typeof(FemPointInput.Invalid))]
    [InlineData("abc", typeof(FemPointInput.Invalid))]
    [InlineData("@5", typeof(FemPointInput.Invalid))]
    public void Parse_RecognizesOtherForms(string text, Type expected) =>
        Assert.IsType(expected, FemPointInput.Parse(text));

    [Fact]
    public void Move_BaseThenPickedTarget_MovesByDifferenceAndFinishes()
    {
        var (command, moves, _) = Create(FemTransformKind.Move);
        command.PickPoint(new Point3D(1, 1, 0));
        Assert.Equal(FemTransformStage.TargetPoint, command.Stage);
        command.PickPoint(new Point3D(4, 5, 0));

        Assert.Equal([new Vector3D(3, 4, 0)], moves);
        Assert.True(command.IsFinished);
    }

    [Fact]
    public void Move_Ortho_KeepsDominantAxis()
    {
        var (command, moves, _) = Create(FemTransformKind.Move);
        command.Ortho = true;
        command.PickPoint(new Point3D(0, 0, 0));
        command.PickPoint(new Point3D(0.3, -2, 0.1));
        Assert.Equal([new Vector3D(0, -2, 0)], moves);
    }

    [Fact]
    public void Move_TypedAbsoluteAndRelative()
    {
        var (absolute, moves1, _) = Create(FemTransformKind.Move);
        absolute.Submit("1;1;1", null);
        absolute.Submit("2;3;1", null);
        Assert.Equal([new Vector3D(1, 2, 0)], moves1);

        var (relative, moves2, _) = Create(FemTransformKind.Move);
        relative.PickPoint(new Point3D(5, 5, 5));
        relative.Submit("@0;0;-3", null);
        Assert.Equal([new Vector3D(0, 0, -3)], moves2);
    }

    [Fact]
    public void Move_DisplacementAtBasePrompt_AppliesVectorDirectly()
    {
        var (command, moves, _) = Create(FemTransformKind.Move);
        command.Submit("@2;0;0", null);
        Assert.Equal([new Vector3D(2, 0, 0)], moves);
        Assert.True(command.IsFinished);
    }

    [Fact]
    public void DirectDistance_GoesAlongCursorDirection()
    {
        var (command, moves, _) = Create(FemTransformKind.Move);
        command.PickPoint(new Point3D(1, 0, 0));
        command.Submit("6", new Point3D(1, 0.3, 0.4));
        Assert.Equal(0, (moves.Single() - new Vector3D(0, 3.6, 4.8)).Length, 9);
    }

    [Fact]
    public void DirectDistance_WithoutCursor_AsksForDirection()
    {
        var (command, moves, _) = Create(FemTransformKind.Move);
        command.PickPoint(new Point3D(1, 0, 0));
        command.Submit("6", null);
        Assert.Empty(moves);
        Assert.Equal("FemTransformDistanceNeedsDirection", command.Message);
        Assert.False(command.IsFinished);
    }

    [Fact]
    public void Copy_RepeatsUntilEnter_AllFromSameBase()
    {
        var (command, _, copies) = Create(FemTransformKind.Copy);
        command.PickPoint(new Point3D(0, 0, 0));
        command.PickPoint(new Point3D(1, 0, 0));
        command.PickPoint(new Point3D(0, 2, 0));
        command.Submit("@0;0;3", null);
        Assert.False(command.IsFinished);
        command.Submit("", null);

        Assert.Equal([(new Vector3D(1, 0, 0), 1), (new Vector3D(0, 2, 0), 1), (new Vector3D(0, 0, 3), 1)], copies);
        Assert.True(command.IsFinished);
    }

    [Fact]
    public void Copy_ArrayOption_CopiesNTimesWithOneStep_ThenContinues()
    {
        var (command, _, copies) = Create(FemTransformKind.Copy);
        command.PickPoint(new Point3D(0, 0, 0));
        command.Submit("М", null);
        Assert.Equal(FemTransformStage.ArrayCount, command.Stage);
        command.Submit("0", null);
        Assert.Equal("FemTransformArrayCountInvalid", command.Message);
        command.Submit("4", null);
        Assert.Equal(FemTransformStage.ArrayTarget, command.Stage);
        command.PickPoint(new Point3D(0, 0, 3));

        Assert.Equal([(new Vector3D(0, 0, 3), 4)], copies);
        Assert.Equal(FemTransformStage.TargetPoint, command.Stage);
    }

    [Fact]
    public void Move_ArrayOption_IsRejected()
    {
        var (command, moves, _) = Create(FemTransformKind.Move);
        command.PickPoint(new Point3D(0, 0, 0));
        command.Submit("М", null);
        Assert.Equal(FemTransformStage.TargetPoint, command.Stage);
        Assert.StartsWith("FemTransformInvalidInput", command.Message);
        Assert.Empty(moves);
    }

    [Fact]
    public void ZeroOffset_IsIgnoredWithMessage()
    {
        var (command, moves, _) = Create(FemTransformKind.Move);
        command.PickPoint(new Point3D(1, 1, 1));
        command.PickPoint(new Point3D(1, 1, 1));
        Assert.Empty(moves);
        Assert.Equal("FemTransformZeroOffset", command.Message);
        Assert.False(command.IsFinished);
    }

    static (FemTransformCommand Command, List<Vector3D> Moves, List<(Vector3D, int)> Copies) Create(FemTransformKind kind)
    {
        var command = new FemTransformCommand(kind, ["1"], ["1"]);
        var moves = new List<Vector3D>();
        var copies = new List<(Vector3D, int)>();
        command.MoveRequested += moves.Add;
        command.CopyRequested += (v, n) => copies.Add((v, n));
        return (command, moves, copies);
    }
}
