using CScore;
using CScore.ParametricRc;
using CScore.Sp63.CrackWidth;
using CScore.Sp63.Deflection;
using CScore.Sp63.Normal;
using Xunit;

namespace CScore.Tests.Sp63Deflection;

public sealed class Sp63DeflectionCheckerTests
{
    static Material Concrete()
    {
        var m = new Material { Id = 1, Tag = "B25", Type = MatType.Concrete, E = 30_000_000 };
        m.N = new MaterialChars(CalcType.N)
        {
            Type = MatType.Concrete, Fc = -18_500, Ft = 1_550, E = 30_000_000, Class = 25
        };
        return m;
    }

    static Material Rebar()
    {
        var m = new Material { Id = 2, Tag = "A400", Type = MatType.ReSteelU, E = 200_000_000 };
        m.N = new MaterialChars(CalcType.N)
        {
            Type = MatType.ReSteelU, Fc = -500_000, Ft = 500_000, E = 200_000_000
        };
        return m;
    }

    static CrossSection Section(double areaT = 20e-4, double areaC = 20e-4,
        Sp63NormalAxis axis = Sp63NormalAxis.Mx)
    {
        var concrete = Concrete();
        var section = new CrossSection { Tag = "test" };
        var region = new MaterialArea { Category = AreaCategory.Region, Material = concrete, MaterialId = 1 };
        region.Contours.Add(new Contour([-0.25, 0.25, 0.25, -0.25, -0.25],
            [-0.15, -0.15, 0.15, 0.15, -0.15], "hull") { Type = ContourType.Hull });
        region.SetWKT();
        section.Areas.Add(region);
        AddBar(section, axis == Sp63NormalAxis.Mx ? 0 : 0.11,
            axis == Sp63NormalAxis.Mx ? 0.11 : 0, areaT);
        AddBar(section, axis == Sp63NormalAxis.Mx ? 0 : -0.11,
            axis == Sp63NormalAxis.Mx ? -0.11 : 0, areaC);
        return section;
    }

    static void AddBar(CrossSection section, double x, double y, double area)
    {
        var rebar = Rebar();
        var group = new MaterialArea { Category = AreaCategory.RebarGroup, Material = rebar, MaterialId = 2 };
        group.Fibers.Add(new Fiber { TypeFiber = FiberType.point, X = x, Y = y, Area = area, Diameter = 0.02 });
        section.Areas.Add(group);
    }

    static Sp63DeflectionOptions Options(Sp63DeflectionStaticScheme scheme = Sp63DeflectionStaticScheme.SimplySupportedUniform,
        Sp63NormalAxis axis = Sp63NormalAxis.Mx, double span = 6, double limit = 20) =>
        new(Sp63NormalShapeKind.Rectangular, axis, scheme, span, limit, Sp63Humidity.From40To75,
            Sp63DeflectionForcesMode.Manual, 1);

    [Theory]
    [InlineData(Sp63DeflectionStaticScheme.SimplySupportedUniform, 5.0 / 48.0)]
    [InlineData(Sp63DeflectionStaticScheme.SimplySupportedMidpoint, 1.0 / 12.0)]
    [InlineData(Sp63DeflectionStaticScheme.CantileverTip, 1.0 / 3.0)]
    public void Check_ReturnsFormulaDeflection(Sp63DeflectionStaticScheme scheme, double coefficient)
    {
        var result = Sp63DeflectionChecker.Check(Section(), new LoadItem { N = 10, Mx = 80 },
            new LoadItem { N = 4, Mx = 20 }, CalcType.N, Options(scheme));

        Assert.Equal(Sp63DeflectionStatus.Calculated, result.Status);
        Assert.Equal(1000 * coefficient * 36 * Math.Abs(result.Curvature!.Total), result.DeflectionMm, 9);
        Assert.Equal(result.DeflectionMm <= 20, result.DeflectionPassed);
        Assert.Equal(10, result.Variables["N"]);
        Assert.Equal(80, result.Variables["M"]);
        Assert.Equal(4, result.Variables["Nl"]);
        Assert.Equal(20, result.Variables["Ml"]);
        Assert.Equal(Sp63DeflectionStatus.Calculated, result.Status);
    }

    [Fact]
    public void Check_ReversedLongMomentReturnsNotApplicable()
    {
        var result = Sp63DeflectionChecker.Check(Section(), new LoadItem { Mx = 80 },
            new LoadItem { Mx = -20 }, CalcType.N, Options());

        Assert.Equal(Sp63DeflectionStatus.NotApplicable, result.Status);
        Assert.Contains(result.ApplicabilityMessages, m => m.Code == "long_moment_reversed");
        Assert.Null(result.DeflectionPassed);
    }

    [Fact]
    public void Check_LongMomentGreaterThanFullReturnsInvalidInput()
    {
        var result = Sp63DeflectionChecker.Check(Section(), new LoadItem { Mx = 80 },
            new LoadItem { Mx = 81 }, CalcType.N, Options());

        Assert.Equal(Sp63DeflectionStatus.InvalidInput, result.Status);
        Assert.Contains(result.ApplicabilityMessages, m => m.Code == "long_moment_exceeds_total");
    }

    [Fact]
    public void Check_RejectsTwoAxisLongLoad()
    {
        var result = Sp63DeflectionChecker.Check(Section(), new LoadItem { Mx = 80 },
            new LoadItem { Mx = 20, My = 1 }, CalcType.N, Options());

        Assert.Equal(Sp63DeflectionStatus.NotApplicable, result.Status);
        Assert.Contains(result.ApplicabilityMessages, m => m.Code == "biaxial_load");
    }

    [Fact]
    public void Check_ReturnsUncrackedCurvatureForMomentBelowMcrc()
    {
        var result = Sp63DeflectionChecker.Check(Section(), new LoadItem { N = 2, Mx = 1 },
            new LoadItem { N = 0.5, Mx = 0.25 }, CalcType.N, Options());

        Assert.Equal(Sp63DeflectionStatus.Calculated, result.Status);
        Assert.False(result.Cracked);
        Assert.Equal("not_cracked", result.Branch);
        Assert.Equal(2, result.Curvature!.Terms.Count);
    }

    [Fact]
    public void Check_SupportsMyAxis()
    {
        var result = Sp63DeflectionChecker.Check(Section(axis: Sp63NormalAxis.My),
            new LoadItem { My = 80 }, new LoadItem { My = 20 }, CalcType.N,
            Options(axis: Sp63NormalAxis.My));

        Assert.Equal(Sp63DeflectionStatus.Calculated, result.Status);
        Assert.Equal(1, result.Variables["axis"]);
    }

    [Fact]
    public void Check_ZeroMomentReturnsNotApplicable()
    {
        var result = Sp63DeflectionChecker.Check(Section(), new LoadItem { N = 10 },
            new LoadItem { N = 5 }, CalcType.N, Options());

        Assert.Equal(Sp63DeflectionStatus.NotApplicable, result.Status);
        Assert.Contains(result.ApplicabilityMessages, m => m.Code == "zero_moment");
    }

    [Fact]
    public void Check_RejectsOneRebarLayerAndPrestress()
    {
        var oneLayer = Section();
        oneLayer.Areas.RemoveAt(oneLayer.Areas.Count - 1);
        var insufficient = Sp63DeflectionChecker.Check(oneLayer, new LoadItem { Mx = 80 },
            new LoadItem { Mx = 20 }, CalcType.N, Options());
        var prestressed = Section();
        prestressed.Areas.First(area => area.Category == AreaCategory.RebarGroup).SigSp = 1;
        var prestressResult = Sp63DeflectionChecker.Check(prestressed, new LoadItem { Mx = 80 },
            new LoadItem { Mx = 20 }, CalcType.N, Options());

        Assert.Equal(Sp63DeflectionStatus.NotApplicable, insufficient.Status);
        Assert.Contains(insufficient.ApplicabilityMessages, m => m.Code == "insufficient_rebar_layers");
        Assert.Equal(Sp63DeflectionStatus.NotApplicable, prestressResult.Status);
        Assert.Contains(prestressResult.ApplicabilityMessages, m => m.Code == "prestressed_rebar");
    }

    [Fact]
    public void Check_MissingConcreteClassReturnsNotApplicable()
    {
        var section = Section();
        section.Areas.First(area => area.Category == AreaCategory.Region).Material!.N!.Class = 0;
        var result = Sp63DeflectionChecker.Check(section, new LoadItem { Mx = 80 },
            new LoadItem { Mx = 20 }, CalcType.N, Options());

        Assert.Equal(Sp63DeflectionStatus.NotApplicable, result.Status);
        Assert.Contains(result.ApplicabilityMessages, m => m.Code == "missing_concrete_class");
    }

    [Fact]
    public void Check_ThroughTensionReturnsNotApplicableInsteadOfFailedVerdict()
    {
        var result = Sp63DeflectionChecker.Check(Section(), new LoadItem { N = 400, Mx = 10 },
            new LoadItem { N = 200, Mx = 5 }, CalcType.N, Options());

        Assert.Equal(Sp63DeflectionStatus.NotApplicable, result.Status);
        Assert.Null(result.DeflectionPassed);
        Assert.Contains(result.ApplicabilityMessages, message => message.Code == "curvature_through_tension");
    }

    [Fact]
    public void Check_InvalidSpanAndNonFiniteLoadsReturnInvalidInput()
    {
        var invalidSpan = Sp63DeflectionChecker.Check(Section(), new LoadItem { Mx = 80 },
            new LoadItem { Mx = 20 }, CalcType.N, Options(span: 0));
        var nonFinite = Sp63DeflectionChecker.Check(Section(), new LoadItem { Mx = double.NaN },
            new LoadItem { Mx = 0 }, CalcType.N, Options());

        Assert.Equal(Sp63DeflectionStatus.InvalidInput, invalidSpan.Status);
        Assert.Contains(invalidSpan.ApplicabilityMessages, message => message.Code == "invalid_span");
        Assert.Equal(Sp63DeflectionStatus.InvalidInput, nonFinite.Status);
        Assert.Contains(nonFinite.ApplicabilityMessages, message => message.Code == "non_finite_load");
    }

    [Fact]
    public void Check_AcceptsUniaxialIdealizedLayersAndRejectsAnotherAxis()
    {
        var section = ParametricRcSectionGenerator.Generate(
            ParametricRcSectionDefinition.Rectangle(0.30, 0.50) with
            {
                LowerRebar = ParametricLongitudinalLayer.Idealized(0.0012, 0.020, -0.21, IdealizedRebarAxis.Mx),
                UpperRebar = ParametricLongitudinalLayer.Idealized(0.0012, 0.020, 0.21, IdealizedRebarAxis.Mx)
            }).Section;
        foreach (var area in section.Areas)
        {
            if (area.Category == AreaCategory.Region) area.Material = Concrete();
            else if (area.Category == AreaCategory.RebarGroup) area.Material = Rebar();
        }
        var full = new LoadItem { Mx = -80 };
        var longLoad = new LoadItem { Mx = -20 };

        var supported = Sp63DeflectionChecker.Check(section, full, longLoad, CalcType.N,
            Options(axis: Sp63NormalAxis.Mx));
        var wrongAxis = Sp63DeflectionChecker.Check(section, new LoadItem { My = 80 },
            new LoadItem { My = 20 }, CalcType.N, Options(axis: Sp63NormalAxis.My));

        Assert.Equal(Sp63DeflectionStatus.Calculated, supported.Status);
        Assert.Equal(Sp63DeflectionStatus.NotApplicable, wrongAxis.Status);
        Assert.Contains(wrongAxis.ApplicabilityMessages, message => message.Code == "idealized_rebar_axis_mismatch");
    }
}
