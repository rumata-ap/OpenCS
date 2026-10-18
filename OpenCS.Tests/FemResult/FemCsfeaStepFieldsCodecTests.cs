using System.IO;
using System.IO.Compression;
using CSfea.CScoreBridge.Structural;
using OpenCS.Services;
using Xunit;

namespace OpenCS.Tests.FemResult;

/// <summary>
/// Запись полей шага CSfea: реакции опор (версия 2), блоки жёсткостей, деформаций и полных матриц (версия 3), чтение
/// записей версий 1–2.
/// </summary>
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
    public void RoundTrip_KeepsStiffnessStrainsAndMatrices()
    {
        static double[] Seq(int n, double scale) => Enumerable.Range(1, n).Select(i => i * scale).ToArray();
        var s = Sample();
        var full = new RcSecantStepFields
        {
            NodeIds = s.NodeIds, Displacements = s.Displacements, ShellIds = s.ShellIds, ShellForces = s.ShellForces,
            ShellStates = s.ShellStates, ShellFlags = s.ShellFlags, BeamIds = s.BeamIds, BeamForces = s.BeamForces,
            BeamFlags = s.BeamFlags,
            HasStiffness = true, ShellStiffness = Seq(RcSecantStepFields.ShellStiffnessComponents, 1e8),
            BeamStiffness = Seq(RcSecantStepFields.BeamStiffnessComponents, 1e7).Select((v, i) => i == 3 ? double.PositiveInfinity : v).ToArray(),
            HasStrains = true, ShellStrains = Seq(RcSecantStepFields.ShellStrainComponents, 1e-4),
            BeamStrains = Seq(RcSecantStepFields.BeamStrainComponents, -1e-4),
            HasMatrices = true, ShellMatrices = Seq(RcSecantStepFields.ShellMatrixComponents, 1e6),
            BeamMatrices = Seq(RcSecantStepFields.BeamMatrixComponents, 1e5),
        };
        var f = FemCsfeaStepFieldsCodec.Unpack(FemCsfeaStepFieldsCodec.Pack(full));
        Assert.True(f.HasStiffness && f.HasStrains && f.HasMatrices);
        // Блоки — float: точность ~1e-7.
        void Same(double[] a, double[] b)
        {
            Assert.Equal(a.Length, b.Length);
            for (int i = 0; i < a.Length; i++)
                Assert.True(a[i] == b[i] || Math.Abs(a[i] - b[i]) <= 1e-6 * Math.Abs(a[i]), $"[{i}] {a[i]} ≠ {b[i]}");
        }
        Same(full.ShellStiffness, f.ShellStiffness);
        Same(full.BeamStiffness, f.BeamStiffness);
        Same(full.ShellStrains, f.ShellStrains);
        Same(full.BeamStrains, f.BeamStrains);
        Same(full.ShellMatrices, f.ShellMatrices);
        Same(full.BeamMatrices, f.BeamMatrices);
        Assert.True(double.IsPositiveInfinity(f.BeamStiffness[3]));

        // Только жёсткости (без галки деформаций, не конечное состояние).
        var stiffOnly = FemCsfeaStepFieldsCodec.Unpack(FemCsfeaStepFieldsCodec.Pack(new RcSecantStepFields
        {
            NodeIds = s.NodeIds, Displacements = s.Displacements, ShellIds = s.ShellIds, ShellForces = s.ShellForces,
            ShellStates = s.ShellStates, ShellFlags = s.ShellFlags, BeamIds = s.BeamIds, BeamForces = s.BeamForces,
            BeamFlags = s.BeamFlags, HasStiffness = true, ShellStiffness = full.ShellStiffness, BeamStiffness = full.BeamStiffness,
        }));
        Assert.True(stiffOnly.HasStiffness);
        Assert.False(stiffOnly.HasStrains || stiffOnly.HasMatrices);
        Assert.Empty(stiffOnly.ShellStrains);
        Assert.Empty(stiffOnly.BeamMatrices);
        Assert.False(stiffOnly.HasBeamStrains);

        // Деформации стержней без галки (пишутся всегда, с 18.10.2026) — своим блоком в конце записи.
        var beamOnly = FemCsfeaStepFieldsCodec.Unpack(FemCsfeaStepFieldsCodec.Pack(new RcSecantStepFields
        {
            NodeIds = s.NodeIds, Displacements = s.Displacements, ShellIds = s.ShellIds, ShellForces = s.ShellForces,
            ShellStates = s.ShellStates, ShellFlags = s.ShellFlags, BeamIds = s.BeamIds, BeamForces = s.BeamForces,
            BeamFlags = s.BeamFlags, HasStiffness = true, ShellStiffness = full.ShellStiffness, BeamStiffness = full.BeamStiffness,
            BeamStrains = full.BeamStrains,
        }));
        Assert.False(beamOnly.HasStrains);
        Assert.Empty(beamOnly.ShellStrains);
        Assert.True(beamOnly.HasBeamStrains);
        Same(full.BeamStrains, beamOnly.BeamStrains);
        Same(full.BeamStiffness, beamOnly.BeamStiffness);
    }

    /// <summary>
    /// Точные матрицы (линейный расчёт на жёсткостях секущего результата): double без потерь, с матрицами КЭ стержней;
    /// флаг прежних матриц не ставится — прежний разбор видит запись без матриц. Без признака — прежний float-блок.
    /// </summary>
    [Fact]
    public void RoundTrip_ExactMatricesInDouble()
    {
        static double[] Seq(int n, double scale) => Enumerable.Range(1, n).Select(i => i * scale + 1.0 / 3).ToArray();
        var s = Sample();
        RcSecantStepFields Make(bool exact) => new()
        {
            NodeIds = s.NodeIds, Displacements = s.Displacements, ShellIds = s.ShellIds, ShellForces = s.ShellForces,
            ShellStates = s.ShellStates, ShellFlags = s.ShellFlags, BeamIds = s.BeamIds, BeamForces = s.BeamForces,
            BeamFlags = s.BeamFlags, BeamStrains = Seq(RcSecantStepFields.BeamStrainComponents, -1e-4),
            HasMatrices = true, ShellMatrices = Seq(RcSecantStepFields.ShellMatrixComponents, 1e6),
            BeamMatrices = Seq(RcSecantStepFields.BeamMatrixComponents, 1e5),
            BeamElementMatrices = Seq(RcSecantStepFields.BeamElementMatrixComponents, 1e5).Select((v, i) => i == 9 ? double.PositiveInfinity : v).ToArray(),
            HasElementMatrices = exact,
        };
        var full = Make(exact: true);
        var data = FemCsfeaStepFieldsCodec.Pack(full);
        var f = FemCsfeaStepFieldsCodec.Unpack(data);

        Assert.True(f.HasMatrices && f.HasElementMatrices && f.HasBeamStrains);
        Assert.Equal(full.ShellMatrices, f.ShellMatrices);
        Assert.Equal(full.BeamMatrices, f.BeamMatrices);
        Assert.Equal(full.BeamElementMatrices, f.BeamElementMatrices);

        var old = FemCsfeaStepFieldsCodec.Unpack(FemCsfeaStepFieldsCodec.Pack(Make(exact: false)));
        Assert.True(old.HasMatrices);
        Assert.False(old.HasElementMatrices);
        Assert.Empty(old.BeamElementMatrices);
        Assert.NotEqual(full.ShellMatrices, old.ShellMatrices);   // float
    }

    [Fact]
    public void Version2_ReadsWithoutBlocks()
    {
        var s = Sample();
        using var ms = new MemoryStream();
        using (var gz = new GZipStream(ms, CompressionLevel.Fastest, leaveOpen: true))
        using (var w = new BinaryWriter(gz))
        {
            w.Write(0x31465343);
            w.Write(2);
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
            Ints(s.ReactionNodeIds); Doubles(s.Reactions); Bytes(s.ReactionKinds); Doubles(s.ReactionSums); Doubles(s.LoadSum);
        }
        var f = FemCsfeaStepFieldsCodec.Unpack(ms.ToArray());
        Assert.Equal(s.Reactions, f.Reactions);
        Assert.Equal(s.LoadSum, f.LoadSum);
        Assert.False(f.HasStiffness || f.HasStrains || f.HasMatrices);
        Assert.Empty(f.ShellStiffness);
        Assert.Empty(f.BeamStiffness);
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
