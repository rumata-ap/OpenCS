using CScore;
using Xunit;

namespace CScore.Tests;

/// <summary>
/// <see cref="MaterialArea.PropagateEps_p"/> — перенос σ_sp·γ_sp в начальные деформации фибр.
/// Метод вызывается прямо из сеттеров редактора арматурной группы (RebarGroupEditorVM.SigSp /
/// GammaSpIndex), а <see cref="Fiber.Eps_p"/> сохраняется в БД (point_fibers.eps_p) — поэтому
/// он обязан не только записывать ε_p, но и СБРАСЫВАТЬ его при снятии преднапряжения.
/// </summary>
public class MaterialAreaPropagateEpsPTests
{
    const double E = 200_000_000.0;   // кПа, см. TestMaterials.PrestressedRebar

    static MaterialArea Strands(double sigSp, double gammaSp = 1.0)
    {
        var material = TestMaterials.PrestressedRebar("A1000");
        return new MaterialArea
        {
            Tag = "strands",
            Category = AreaCategory.RebarGroup,
            Material = material,
            MaterialId = material.Id,
            DiagrammType = DiagrammType.L3,
            SigSp = sigSp,
            GammaSp = gammaSp,
            Fibers = [Fiber.CreatePoint(0.02, -0.04, -0.22), Fiber.CreatePoint(0.02, 0.04, -0.22)],
        };
    }

    [Fact]
    public void PropagateEps_p_WritesStrainFromSigSp()
    {
        var area = Strands(sigSp: 900.0);

        area.PropagateEps_p();

        Assert.All(area.Fibers, f => Assert.Equal(900.0 * 1000.0 / E, f.Eps_p, 12));
    }

    [Fact]
    public void PropagateEps_p_AppliesGammaSp()
    {
        var area = Strands(sigSp: 900.0, gammaSp: 0.9);

        area.PropagateEps_p();

        Assert.All(area.Fibers, f => Assert.Equal(900.0 * 1000.0 * 0.9 / E, f.Eps_p, 12));
    }

    /// <summary>
    /// Снятие преднапряжения (σ_sp → 0) обязано обнулить ε_p. Иначе редактор показывает
    /// ненапрягаемую группу, а расчёт и БД продолжают жить со старым обжатием.
    /// </summary>
    [Fact]
    public void PropagateEps_p_ClearsStrainWhenSigSpReset()
    {
        var area = Strands(sigSp: 900.0);
        area.PropagateEps_p();
        Assert.All(area.Fibers, f => Assert.NotEqual(0.0, f.Eps_p));

        area.SigSp = 0.0;
        area.PropagateEps_p();

        Assert.All(area.Fibers, f => Assert.Equal(0.0, f.Eps_p));
    }

    /// <summary>
    /// Материал ещё не разрешён (порядок загрузки из БД: фибры с eps_p приходят раньше, чем
    /// проставляется Material) — вычислить ε_p нельзя, и загруженное значение должно уцелеть.
    /// </summary>
    [Fact]
    public void PropagateEps_p_KeepsLoadedStrainWhenMaterialUnresolved()
    {
        var area = Strands(sigSp: 900.0);
        double loaded = 0.0045;
        foreach (var f in area.Fibers) f.Eps_p = loaded;
        area.Material = null;

        area.PropagateEps_p();

        Assert.All(area.Fibers, f => Assert.Equal(loaded, f.Eps_p, 12));
    }
}
