using CScore;
using CSfea.Torsion;
using OpenCS.Services;
using OpenCS.Utilites;

namespace OpenCS.OpenSees.Tests;

public sealed class FemGjDefaultResolverTests
{
    [Fact]
    public void MissingSectionUsesGlobalDefaultInInternalUnits()
    {
        var resolver = new FemGjDefaultResolver(() => new CalcSettings());

        var result = resolver.Resolve(null);

        Assert.Equal(FemGjValueSource.GlobalDefault, result.Source);
        Assert.Equal(1e10, result.GjNm2);
    }

    [Fact]
    public void InvalidGlobalDefaultUsesBuiltInFallback()
    {
        var settings = new CalcSettings { OpenSeesDefaultGjKnm2 = double.NaN };
        var resolver = new FemGjDefaultResolver(() => settings);

        var result = resolver.Resolve(null);

        Assert.Equal(FemGjValueSource.BuiltInFallback, result.Source);
        Assert.Equal(CalcSettings.DefaultOpenSeesGjKnm2 * 1000, result.GjNm2);
    }

    [Fact]
    public void DisabledSectionEstimateUsesGlobalDefault()
    {
        var settings = new CalcSettings { OpenSeesAutoGjFromSection = false, OpenSeesDefaultGjKnm2 = 321 };
        var resolver = new FemGjDefaultResolver(() => settings, _ => new TorsionProps { It = 0.0002 });

        var result = resolver.Resolve(CreateSection(30000));

        Assert.Equal(FemGjValueSource.GlobalDefault, result.Source);
        Assert.Equal(321000, result.GjNm2);
    }

    [Fact]
    public void SectionEstimateConvertsShearModulusAndItToGj()
    {
        var settings = new CalcSettings { OpenSeesDefaultGjKnm2 = 1 };
        var resolver = new FemGjDefaultResolver(() => settings, _ => new TorsionProps { It = 0.0002 });

        var result = resolver.Resolve(CreateSection(30000));

        Assert.Equal(FemGjValueSource.SectionEstimate, result.Source);
        Assert.Equal(2.5e6, result.GjNm2, precision: 6);
    }

    [Fact]
    public void InvalidGlobalDefaultDoesNotBlockValidSectionEstimate()
    {
        var settings = new CalcSettings { OpenSeesDefaultGjKnm2 = 0 };
        var resolver = new FemGjDefaultResolver(() => settings, _ => new TorsionProps { It = 0.0002 });

        var result = resolver.Resolve(CreateSection(30000));

        Assert.Equal(FemGjValueSource.SectionEstimate, result.Source);
        Assert.Equal(2.5e6, result.GjNm2, precision: 6);
    }

    [Fact]
    public void SectionEstimateIsCachedUntilGeometryOrBaseMaterialChanges()
    {
        int calls = 0;
        var material = new Material { E = 30000, Type = MatType.Concrete };
        var section = CreateSection(material);
        var resolver = new FemGjDefaultResolver(
            () => new CalcSettings(),
            _ =>
            {
                calls++;
                return new TorsionProps { It = 0.0002 };
            });

        resolver.Resolve(section);
        resolver.Resolve(section);
        material.E = 40000;
        var changed = resolver.Resolve(section);

        Assert.Equal(2, calls);
        Assert.Equal(40000 / (2.0 * 1.2) * 1e6 * 0.0002, changed.GjNm2, precision: 6);
    }

    [Fact]
    public void CompositeSectionUsesFirstAreaGeometryAndLargestConcreteMaterial()
    {
        var first = CreateArea(0.1, 0.1, new Material { E = 30000, Type = MatType.Concrete });
        var largest = CreateArea(0.3, 0.3, new Material { E = 40000, Type = MatType.Concrete });
        var section = new CrossSection { Id = 17, Areas = [first, largest] };
        double observedWidth = 0;
        var resolver = new FemGjDefaultResolver(
            () => new CalcSettings(),
            boundary =>
            {
                observedWidth = boundary.OuterX.Max() - boundary.OuterX.Min();
                return new TorsionProps { It = 0.0002 };
            });

        var result = resolver.Resolve(section);

        Assert.Equal(0.1, observedWidth, precision: 8);
        Assert.Equal(40000 / (2.0 * 1.2) * 1e6 * 0.0002, result.GjNm2, precision: 6);
    }

    [Fact]
    public void ProductionEstimatorReturnsPhysicalGjInNm2()
    {
        var resolver = new FemGjDefaultResolver(() => new CalcSettings());

        var result = resolver.Resolve(CreateSection(30000));

        Assert.Equal(FemGjValueSource.SectionEstimate, result.Source);
        Assert.True(double.IsFinite(result.GjNm2));
        Assert.InRange(result.GjNm2, 1e5, 1e9);
    }

    static CrossSection CreateSection(double e)
        => CreateSection(new Material { E = e, Type = MatType.Concrete });

    static CrossSection CreateSection(Material material)
        => new() { Id = 1, Areas = [CreateArea(0.3, 0.5, material)] };

    static MaterialArea CreateArea(double width, double height, Material material)
    {
        var area = new MaterialArea { Material = material, Category = AreaCategory.Region };
        var contour = new Contour(
            [
                new StressPoint(-width / 2, -height / 2),
                new StressPoint(width / 2, -height / 2),
                new StressPoint(width / 2, height / 2),
                new StressPoint(-width / 2, height / 2),
                new StressPoint(-width / 2, -height / 2),
            ],
            "hull")
        {
            Type = ContourType.Hull
        };
        area.Contours.Add(contour);
        return area;
    }
}
