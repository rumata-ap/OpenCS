using CScore.Sp16;
using OpenCS.ViewModels;
using Xunit;

namespace OpenCS.Tests;

public class SteelDesignParamsEditorTests
{
    [Fact]
    public void LoadThenBuildPreservesAllFields()
    {
        var p = new SteelDesignParams
        {
            Profile = new SteelProfile { Kind = SteelProfileKind.IBeam, H = .3, Bf1 = .15, Tf1 = .012, Tw = .008 },
            ManualForces = new SteelManualForces { N = -5, Qy = 2, Mz = 1 },
            GammaC = .9, LefX = 4.5, LefY = 1.5, NetAreaRatio = .85, TensionYieldAllowed = true, UseGammaRes = true,
            DynamicLoad = true, LefB = 2, LtbLoad = LtbLoadKind.PureBending, LtbRestraints = LtbRestraints.OneAtMid,
            LtbLoadOnTensionFlange = true, LtbFixedEnds = true, Cantilever = true, ContinuousRigidDeck = true,
            ItSource = TorsionConstantSource.MinAppendixDFem, AllowPlastic = true, GammaFEq = 1.2, PureBendingZone = true, LocalForce = 30, BearingLength = .1,
            FlangeWeldLeg = .006, RibSpacing = 1.2, OneSidedFlangeWelds = true, FrictionFlangeJoints = true,
            WebHoleSpacing = .08, WebHoleDiameter = .023,
            MomentShape = MomentShape.LinearEndMoments, EndMomentRatio = -.5, MiddleThirdMomentRatio = .7,
            CantileverColumn = true, UseFormula121a = true, CompressionCategory = CompressionMemberCategory.Bracing,
            TensionCategory = TensionMemberCategory.OtherBracing, TensionLoad = TensionLoadKind.Dynamic, Group4 = true,
            SlendernessGovernsSection = true, CurveX = SectionCurve.c, CurveY = SectionCurve.a, EtaOverride = 1.4,
        };
        var vm = new SteelDesignParamsEditorVM();
        vm.Load(p);
        Assert.True(vm.TryBuild(out var built, out var error), error);
        Assert.Equal(p, built);
    }

    [Fact]
    public void CommaDecimalIsAcceptedAndEmptyEtaMeansAuto()
    {
        var vm = new SteelDesignParamsEditorVM { LefX = "3,5", EtaOverride = " " };
        Assert.True(vm.TryBuild(out var p, out _));
        Assert.Equal(3.5, p.LefX);
        Assert.Null(p.EtaOverride);
    }

    [Theory]
    [InlineData(nameof(SteelDesignParamsEditorVM.LefX), "0")]
    [InlineData(nameof(SteelDesignParamsEditorVM.LefY), "abc")]
    [InlineData(nameof(SteelDesignParamsEditorVM.GammaC), "")]
    [InlineData(nameof(SteelDesignParamsEditorVM.NetAreaRatio), "1.2")]
    [InlineData(nameof(SteelDesignParamsEditorVM.EndMomentRatio), "-2")]
    [InlineData(nameof(SteelDesignParamsEditorVM.RibSpacing), "-1")]
    public void InvalidFieldIsRejected(string property, string value)
    {
        var vm = new SteelDesignParamsEditorVM();
        typeof(SteelDesignParamsEditorVM).GetProperty(property)!.SetValue(vm, value);
        Assert.False(vm.TryBuild(out _, out var error));
        Assert.NotEmpty(error);
    }

    [Theory]
    [InlineData("0.08", "0")]
    [InlineData("0", "0.023")]
    [InlineData("0.02", "0.023")]
    public void InvalidWebHolesAreRejected(string spacing, string diameter)
    {
        var vm = new SteelDesignParamsEditorVM { WebHoleSpacing = spacing, WebHoleDiameter = diameter };
        Assert.False(vm.TryBuild(out _, out var error));
        Assert.NotEmpty(error);
    }

    [Fact]
    public void ResultViewTreatsFailedUndefinedRatioAsWorst()
    {
        var vm = new OpenCS.Views.SteelCheckResultVM("""
            {"schemaVersion":2,"utilization":null,"passed":false,"sectionTag":"s","steelTag":"C245",
             "context":{"lefX":3,"lefY":3,"lefB":3,"gammaC":1},
             "forces":{"name":"","n":0,"mx":10,"my":0,"qx":0,"qy":0,"t":0},
             "details":[
               {"clause":"8.2.1","formula":"(41)","description":"a","category":"strength","ratio":0.5,"passed":true,"status":"Ok"},
               {"clause":"8.4.1","formula":"(69)","description":"b","category":"stability","ratio":null,"passed":false,"status":"Fail"}]}
            """);
        Assert.False(vm.Passed);
        var failed = vm.Details.Single(d => d.Formula == "(69)");
        Assert.Equal("∞", failed.RatioText);
        Assert.Same(failed, vm.Worst);
        Assert.True(vm.Groups.Single(g => g.Items.Contains(failed)).AnyFailed);
    }
}
