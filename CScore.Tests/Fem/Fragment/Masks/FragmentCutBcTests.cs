using CScore.Fem.Fragment;
using CScore.Fem.Fragment.Masks;
using Xunit;
using static CScore.Tests.Fem.Fragment.Masks.FragmentMaskFixtures;
using M = CScore.Fem.Fragment.Masks.FragmentDofMode;

namespace CScore.Tests.Fem.Fragment.Masks;

public sealed class FragmentCutBcTests
{
    const M F = M.Fixed, O = M.Free, D = M.MaskDisplacement, S = M.MaskForce;

    static FragmentCut End(FragmentCutSide side) => new("C1", FragmentCutKind.End, side, 1.5, "1", []);
    static FragmentCut Edge(FragmentCutSide side) => new("W1", FragmentCutKind.Edge, side, 1.5, null, [(V(0, 0, 1.5), V(4, 0, 1.5))]);

    public static TheoryData<FragmentBcPreset, FragmentCutSide, M[]> Table => new()
    {
        { FragmentBcPreset.Kinematic, FragmentCutSide.Bottom, [D, D, D, D, D, D] },
        { FragmentBcPreset.Kinematic, FragmentCutSide.Top, [D, D, D, D, D, D] },
        { FragmentBcPreset.KinematicTranslations, FragmentCutSide.Top, [D, D, D, O, O, O] },
        { FragmentBcPreset.Static, FragmentCutSide.Bottom, [S, S, S, S, S, S] },
        { FragmentBcPreset.Isolation, FragmentCutSide.Bottom, [F, F, F, O, O, O] },
        { FragmentBcPreset.Isolation, FragmentCutSide.Top, [F, F, O, O, O, O] },
    };

    [Theory]
    [MemberData(nameof(Table))]
    public void Resolve_BarEnd_IsPresetTable(FragmentBcPreset preset, FragmentCutSide side, M[] expected)
    {
        Assert.Equal(expected, FragmentCutBc.Resolve(preset, End(side), null, null, out bool oblique));
        Assert.False(oblique);
    }

    /// <summary>Стена вдоль X (нормаль ±Y): сверлильный поворот — Ry; стена вдоль Y (нормаль X) — Rx.</summary>
    [Fact]
    public void Resolve_WallEdge_FreesDrillingRotationInKinematicPresets()
    {
        Assert.Equal([D, D, D, D, O, D], FragmentCutBc.Resolve(FragmentBcPreset.Kinematic, Edge(FragmentCutSide.Bottom), null, V(0, -1, 0), out _));
        Assert.Equal([D, D, D, O, D, D], FragmentCutBc.Resolve(FragmentBcPreset.Kinematic, Edge(FragmentCutSide.Top), null, V(1, 0, 0), out _));
        // В статике и изоляции сверлильный DOF — по таблице.
        Assert.Equal([S, S, S, S, S, S], FragmentCutBc.Resolve(FragmentBcPreset.Static, Edge(FragmentCutSide.Top), null, V(0, 1, 0), out _));
        Assert.Equal([F, F, O, O, O, O], FragmentCutBc.Resolve(FragmentBcPreset.Isolation, Edge(FragmentCutSide.Top), null, V(0, 1, 0), out _));
    }

    [Fact]
    public void Resolve_ObliqueWall_KeepsRotationsAndReports()
    {
        var n = V(1, 1, 0);
        Assert.Equal([D, D, D, D, D, D], FragmentCutBc.Resolve(FragmentBcPreset.Kinematic, Edge(FragmentCutSide.Bottom), null, n, out bool oblique));
        Assert.True(oblique);
    }

    /// <summary>Правка поверх пресета: 255 — по пресету, остальное — режим; сверлильный DOF правкой включается обратно.</summary>
    [Fact]
    public void Resolve_OverridesOnTopOfPreset()
    {
        byte[] edit = [255, 255, 0, 255, 2, 3];
        Assert.Equal([D, D, F, D, D, S], FragmentCutBc.Resolve(FragmentBcPreset.Kinematic, Edge(FragmentCutSide.Bottom), edit, V(0, 1, 0), out _));
        Assert.Equal([F, F, F, O, D, S], FragmentCutBc.Resolve(FragmentBcPreset.Isolation, End(FragmentCutSide.Bottom), edit, null, out _));
    }

    [Fact]
    public void Codec_RoundTripAndPresetParsing()
    {
        var bytes = FragmentCutBc.Encode([M.Fixed, null, M.MaskDisplacement, M.MaskForce, M.Free, null]);
        Assert.Equal([0, 255, 2, 3, 1, 255], bytes);
        Assert.Throws<InvalidDataException>(() => FragmentCutBc.Decode(7));
        Assert.Equal(FragmentBcPreset.Kinematic, FragmentCutBc.ParsePreset(null));
        Assert.Equal(FragmentBcPreset.Kinematic, FragmentCutBc.ParsePreset("нечто"));
        Assert.Equal(FragmentBcPreset.Static, FragmentCutBc.ParsePreset("Static"));
        Assert.Equal(FragmentBcPreset.Isolation, FragmentCutBc.ParsePreset(nameof(FragmentBcPreset.Isolation)));
    }
}
