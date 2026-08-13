using CScore;
using CScore.Fem;
using OpenCS.Services;
using OpenCS.Utilites;

namespace OpenCS.OpenSees.Tests;

public sealed class FemMemberGjCreationTests
{
    [Fact]
    public void NewBeamWithoutSectionGetsDefaultManualGj()
    {
        var factory = new FemMemberFactory(new FemGjDefaultResolver(() => new CalcSettings()));

        var member = factory.CreateBeam(1, "1", "[1,2]", null, null);

        Assert.Equal("beam", member.ElemType);
        Assert.Equal("manual", member.GjStrategy);
        Assert.Equal(1e10, member.GjManualValue);
        Assert.Null(member.GjTorsionTaskId);
    }

    [Fact]
    public void NewBeamWithSectionGetsResolvedSectionGj()
    {
        var resolver = new FemGjDefaultResolver(
            () => new CalcSettings(),
            _ => new CSfea.Torsion.TorsionProps { It = 0.0002 });
        var factory = new FemMemberFactory(resolver);
        var material = new Material { E = 30000, Type = MatType.Concrete };
        var section = new CrossSection
        {
            Id = 5,
            Areas = [CreateArea(material)]
        };

        var member = factory.CreateBeam(1, "1", "[1,2]", section.Id, section);

        Assert.Equal(section.Id, member.CrossSectionId);
        Assert.Equal("manual", member.GjStrategy);
        Assert.NotNull(member.GjManualValue);
        Assert.Equal(2.5e6, member.GjManualValue.Value, precision: 6);
        Assert.Null(member.GjTorsionTaskId);
    }

    static MaterialArea CreateArea(Material material)
    {
        var area = new MaterialArea { Material = material, Category = AreaCategory.Region };
        var contour = new Contour(
            [
                new StressPoint(-0.15, -0.25),
                new StressPoint(0.15, -0.25),
                new StressPoint(0.15, 0.25),
                new StressPoint(-0.15, 0.25),
                new StressPoint(-0.15, -0.25),
            ],
            "hull")
        {
            Type = ContourType.Hull
        };
        area.Contours.Add(contour);
        return area;
    }
}
