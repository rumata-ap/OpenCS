using CScore.Sp16;
using Xunit;

namespace CScore.Tests.Steel;

/// <summary>Проверка стали по КЭ: расчётные длины lef = μ·l по длине стержня между раскреплениями по сетке.</summary>
public class SteelMeshLefTests
{
    [Fact]
    public void WithMeshLength_MultipliesMuByLength()
    {
        var p = new SteelDesignParams
        {
            LefX = 9, LefY = 9, LefB = 2,
            MeshLef = new SteelMeshLef { MuX = 2, MuY = 0.7, MuB = 0.5 },
        };

        var e = p.WithMeshLength(4);

        Assert.Equal(8, e.LefX, 9);
        Assert.Equal(2.8, e.LefY, 9);
        Assert.Equal(2, e.LefB, 9);
        Assert.Null(e.MeshLef);                       // длины в результате уже конкретные
    }

    [Fact]
    public void WithMeshLength_MuBZero_KeepsLefB()
    {
        var p = new SteelDesignParams { LefB = 1.5, MeshLef = new SteelMeshLef() };
        var e = p.WithMeshLength(6);
        Assert.Equal(6, e.LefX, 9);
        Assert.Equal(6, e.LefY, 9);
        Assert.Equal(1.5, e.LefB, 9);
    }

    [Fact]
    public void WithMeshLength_NoModeOrNoLength_Unchanged()
    {
        var plain = new SteelDesignParams { LefX = 5 };
        Assert.Same(plain, plain.WithMeshLength(3));
        var mesh = plain with { MeshLef = new SteelMeshLef { MuX = 2 } };
        Assert.Same(mesh, mesh.WithMeshLength(0));
    }

    [Fact]
    public void MeshLef_RoundTripsThroughJson_WithoutLegacyMuClash()
    {
        var p = new SteelDesignParams { LefX = 4, MeshLef = new SteelMeshLef { MuX = 2, MuY = 0.5, MuB = 1 } };
        var back = SteelDesignParams.Parse(p.ToJson());
        Assert.Equal(p.MeshLef, back.MeshLef);
        Assert.Equal(4, back.LefX, 9);
        Assert.False(back.MigratedFromLegacy);
        Assert.Null(SteelDesignParams.Parse(new SteelDesignParams().ToJson()).MeshLef);
    }
}

/// <summary>Параметры стальной проверки по КЭ в самой проверке поверх параметров СП 16 цели.</summary>
public class SteelFemCheckParamsTests
{
    static CScore.Fem.FemMemberGroup Group(string? designJson) => new() { Tag = "Ферма", DesignParamsJson = designJson };

    [Fact]
    public void TryParse_DistinguishesCheckParamsFromFullSp16()
    {
        var p = new CScore.Fem.SteelFemCheckParams { MeshLef = new SteelMeshLef { MuX = 2 } };
        Assert.Equal(p.MeshLef, CScore.Fem.SteelFemCheckParams.TryParse(p.ToJson())!.MeshLef);
        Assert.Null(CScore.Fem.SteelFemCheckParams.TryParse(new SteelDesignParams().ToJson()));
        Assert.Null(CScore.Fem.SteelFemCheckParams.TryParse(null));
    }

    [Fact]
    public void BuildCalcTask_Steel_CheckMeshLefOverGroupParams()
    {
        var group = Group(new SteelDesignParams { GammaC = 0.9, LefX = 7 }.ToJson());
        var check = new CScore.Fem.FemCheck
        {
            NormCode = "steel_check",
            ParamsJson = new CScore.Fem.SteelFemCheckParams { MeshLef = new SteelMeshLef { MuX = 2, MuB = 1 } }.ToJson(),
        };

        var p = SteelDesignParams.Parse(CScore.Fem.FemCheckRunner.BuildCalcTask(check, group).ParamsJson);

        Assert.Equal(0.9, p.GammaC);                                // параметры группы
        Assert.Equal(7, p.LefX, 9);
        Assert.Equal(new SteelMeshLef { MuX = 2, MuB = 1 }, p.MeshLef); // режим проверки
    }

    [Fact]
    public void BuildCalcTask_Steel_NoCheckParams_DropsMeshLefOfGroup()
    {
        var group = Group(new SteelDesignParams { LefX = 7, MeshLef = new SteelMeshLef() }.ToJson());
        var check = new CScore.Fem.FemCheck { NormCode = "steel_check" };
        var p = SteelDesignParams.Parse(CScore.Fem.FemCheckRunner.BuildCalcTask(check, group).ParamsJson);
        Assert.Null(p.MeshLef);
        Assert.Equal(7, p.LefX, 9);
    }

    [Fact]
    public void MergeInto_KeepsLegacyFields()
    {
        string legacy = """{"DesignLengthX":2,"MuX":1.5}""";
        string merged = CScore.Fem.SteelFemCheckParams.MergeInto(legacy,
            new CScore.Fem.SteelFemCheckParams { MeshLef = new SteelMeshLef() });
        var p = SteelDesignParams.Parse(merged);
        Assert.True(p.MigratedFromLegacy);
        Assert.Equal(3, p.LefX, 9);
        Assert.NotNull(p.MeshLef);
    }
}
