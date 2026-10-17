using CScore.Fem;
using CSfea.CScoreBridge.Structural;
using OpenCS.Services;
using Xunit;

namespace OpenCS.Tests;

/// <summary>Наборы усилий CSfea по выбранным КЭ (спека 2026-10-17 «наборы по вариантам», срез 0).</summary>
public sealed class FemCsfeaForceSetScopeTests
{
    static readonly FemSchema Schema = new() { Id = 1, Tag = "Схема" };

    /// <summary>Пластины 101–103 (Mx = 1, 2, 3 кН·м/м), стержни 201–202.</summary>
    static RcSecantStepFields Fields()
    {
        var shell = new double[3 * RcSecantStepFields.ShellForceComponents];
        for (int e = 0; e < 3; e++) shell[e * RcSecantStepFields.ShellForceComponents + 3] = (e + 1) * 1e3;
        return new RcSecantStepFields
        {
            NodeIds = [], Displacements = [], ShellIds = [101, 102, 103], ShellForces = shell,
            ShellStates = new double[3 * RcSecantStepFields.ShellStateComponents], ShellFlags = new byte[3],
            BeamIds = [201, 202], BeamForces = new double[2 * RcSecantStepFields.BeamForceComponents], BeamFlags = new byte[2],
        };
    }

    [Fact]
    public void BuildNamed_WithScope_KeepsOnlyScopeRows()
    {
        var scope = new FemCsfeaForceSetScope([101, 103, 202], ["Пл-1", "Ст-2"]);
        var sets = FemCsfeaForceSetBuilder.BuildNamed(Schema, "Шаг", "Описание.", Fields(), [], scope);

        Assert.Equal(2, sets.Count);
        var shells = sets[0];
        Assert.Equal("Шаг — пластины [выбор: 3 КЭ]", shells.Tag);
        Assert.Equal([101, 103], shells.ShellItems.Select(r => r.SourceElementNum));
        Assert.Equal([1.0, 3.0], shells.ShellItems.Select(r => r.Mx));
        Assert.Equal([1, 2], shells.ShellItems.Select(r => r.Num));
        Assert.Equal("Описание. Охват — выбранные КЭ: 3; КонЭ: Пл-1, Ст-2.", shells.Description);

        var bars = sets[1];
        Assert.Equal("Шаг — стержни [выбор: 3 КЭ]", bars.Tag);
        Assert.Equal(["202-1", "202-2"], bars.Items.Select(r => r.Label));
    }

    [Fact]
    public void BuildNamed_ScopeWithoutBars_NoBarSet()
    {
        var sets = FemCsfeaForceSetBuilder.BuildNamed(Schema, "Шаг", "", Fields(), [], new FemCsfeaForceSetScope([102], []));

        var shells = Assert.Single(sets);
        Assert.Equal("shell", shells.Kind);
        Assert.Equal(102, Assert.Single(shells.ShellItems).SourceElementNum);
    }

    [Fact]
    public void BuildNamed_WithoutScope_WholeSchema()
    {
        var sets = FemCsfeaForceSetBuilder.BuildNamed(Schema, "Шаг", "", Fields(), []);

        Assert.Equal(["Шаг — пластины", "Шаг — стержни"], sets.Select(s => s.Tag));
        Assert.Equal(3, sets[0].ShellItems.Count);
        Assert.Equal(4, sets[1].Items.Count);
    }

    [Fact]
    public void FromSelection_ParsesNumericTags_DescribesFirstMembers()
    {
        Assert.Null(FemCsfeaForceSetScope.FromSelection(["узел"], []));

        var members = Enumerable.Range(1, 23).Select(i => $"К{i}").ToList();
        var scope = FemCsfeaForceSetScope.FromSelection(["5", "x", "7"], members)!;
        Assert.Equal(new HashSet<int> { 5, 7 }, scope.Elements);
        Assert.EndsWith("К20 и ещё 3.", scope.Description);
    }
}
