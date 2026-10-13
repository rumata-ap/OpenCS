using System.IO;
using System.IO.Compression;
using CSfea.CScoreBridge.Structural;
using OpenCS.Services;
using Xunit;

namespace OpenCS.Tests.FemResult;

/// <summary>Запись полей шага CSfea: реакции опор (версия 2) и чтение записей версии 1 без них.</summary>
public class FemCsfeaStepFieldsCodecTests
{
    static RcSecantStepFields Sample() => new()
    {
        Stage = 1, Step = 3, LoadFactor = 0.75, IsRefinement = true,
        NodeIds = [1, 2], Displacements = [0, 0, 0, 0, 0, 0, 0, 0, -0.01, 0, 0, 0],
        ShellIds = [7], ShellForces = [1, 2, 3, 4, 5, 6, 7, 8], ShellStates = [0.5, 0, 1, 0.25, 0.125], ShellFlags = [1],
        BeamIds = [9], BeamForces = Enumerable.Range(0, 12).Select(i => (double)i).ToArray(), BeamFlags = [2],
        ReactionNodeIds = [1], Reactions = [10, 20, 30, 40, 50, 60],
        ReactionKinds = [RcReactionSet.Support | RcReactionSet.Spring],
        ReactionSums = [10, 20, 25, 0, 0, 5, 0, 0, 0, 0, 0, 0], LoadSum = [-10, -20, -30],
    };

    [Fact]
    public void RoundTrip_KeepsReactions()
    {
        var f = FemCsfeaStepFieldsCodec.Unpack(FemCsfeaStepFieldsCodec.Pack(Sample()));
        var s = Sample();
        Assert.Equal(s.ReactionNodeIds, f.ReactionNodeIds);
        Assert.Equal(s.Reactions, f.Reactions);
        Assert.Equal(s.ReactionKinds, f.ReactionKinds);
        Assert.Equal(s.ReactionSums, f.ReactionSums);
        Assert.Equal(s.LoadSum, f.LoadSum);
        Assert.Equal(s.BeamForces, f.BeamForces);
        Assert.Equal(s.Displacements, f.Displacements);
        Assert.True(f.IsRefinement);
        Assert.Equal(0.75, f.LoadFactor);
    }

    [Fact]
    public void Version1_ReadsWithoutReactions()
    {
        var s = Sample();
        using var ms = new MemoryStream();
        using (var gz = new GZipStream(ms, CompressionLevel.Fastest, leaveOpen: true))
        using (var w = new BinaryWriter(gz))
        {
            w.Write(0x31465343);
            w.Write(1);
            w.Write(s.Stage); w.Write(s.Step); w.Write(s.LoadFactor); w.Write(s.IsRefinement);
            void Ints(int[] a) { w.Write(a.Length); foreach (int v in a) w.Write(v); }
            void Doubles(double[] a) { w.Write(a.Length); foreach (double v in a) w.Write(v); }
            void Bytes(byte[] a) { w.Write(a.Length); w.Write(a); }
            Ints(s.NodeIds); Doubles(s.Displacements);
            Ints(s.ShellIds); Doubles(s.ShellForces);
            w.Write(s.ShellStates.Length);
            foreach (double v in s.ShellStates) w.Write((float)v);
            Bytes(s.ShellFlags);
            Ints(s.BeamIds); Doubles(s.BeamForces); Bytes(s.BeamFlags);
        }
        var f = FemCsfeaStepFieldsCodec.Unpack(ms.ToArray());
        Assert.Equal(s.BeamForces, f.BeamForces);
        Assert.Equal(s.ShellStates, f.ShellStates);
        Assert.Empty(f.ReactionNodeIds);
        Assert.Empty(f.Reactions);
        Assert.Empty(f.ReactionSums);
        Assert.Empty(f.LoadSum);
    }
}
