using System.Text.Json;
using CScore;
using CScore.Fem;
using OpenCS.Tasks;
using Xunit;

namespace OpenCS.Tests;

public class SteelTaskHandlerTests
{
    static CrossSection Box()
    {
        var area = new MaterialArea { Material = new Material
        {
            Type = MatType.Steel, E = 2.06e8,
            MaterialChars = [new MaterialChars(CalcType.C) { Ry = 240000, Ru = 360000 }]
        }};
        area.Contours.Add(new Contour([-.1,.1,.1,-.1,-.1], [-.2,-.2,.2,.2,-.2], "outer") { Type = ContourType.Hull });
        area.Contours.Add(new Contour([-.09,.09,.09,-.09,-.09], [-.19,-.19,.19,.19,-.19], "hole") { Type = ContourType.Hole });
        return new CrossSection { Areas = [area] };
    }

    [Fact]
    public void TensionUsesNetPolygonAreaAndKpa()
    {
        var r = TaskRunner.Run(new CalcTask { Kind = "steel_central_tension" }, Box(), new LoadItem { N = 100 });
        Assert.Equal("ok", r.Status);
        using var doc = JsonDocument.Parse(r.DataJson);
        var strength = doc.RootElement.GetProperty("details").EnumerateArray().Single(d => d.GetProperty("formula").GetString() == "(5)");
        Assert.Equal(100 / ((.2*.4 - .18*.38)*240000), strength.GetProperty("ratio").GetDouble(), 8);
        Assert.True(doc.RootElement.GetProperty("notes").GetArrayLength() > 0);
    }

    [Fact]
    public void FemPreservesNewParametersAndLegacyWarning()
    {
        var check = new FemCheck { NormCode = "steel_central_tension" };
        var member = new FemMember { DesignParamsJson = "{\"DesignLengthX\":4,\"MuX\":0.5,\"GammaM\":1.025}" };
        var task = FemCheckRunner.BuildCalcTask(check, member);
        var r = TaskRunner.Run(task, Box(), new LoadItem { N = 100 });
        using var doc = JsonDocument.Parse(r.DataJson);
        Assert.Equal(2, doc.RootElement.GetProperty("context").GetProperty("lefX").GetDouble());
        Assert.Contains(doc.RootElement.GetProperty("notes").EnumerateArray(), n => n.GetString()!.Contains("старом формате"));
    }

    [Fact]
    public void OldTorsionTaskIsRejected()
    {
        var r = TaskRunner.Run(new CalcTask { Kind = "steel_torsion" }, Box(), new LoadItem { T = 1 });
        Assert.Equal("error", r.Status);
        Assert.DoesNotContain("steel_torsion", TaskRunner.KindList);
    }

    [Fact]
    public void ShearUsesVyForMxPlaneAndPreservesManualMapping()
    {
        var fromLoad = TaskRunner.Run(new CalcTask { Kind = "steel_shear" }, Box(), new LoadItem { Vy = 20 });
        var manual = TaskRunner.Run(new CalcTask { Kind = "steel_shear",
            ParamsJson = "{\"ManualForces\":{\"Qz\":20}}" }, Box(), new LoadItem { Vx = 999 });
        Assert.Equal("ok", fromLoad.Status);
        using var a = JsonDocument.Parse(fromLoad.DataJson);
        using var b = JsonDocument.Parse(manual.DataJson);
        Assert.Equal(0, a.RootElement.GetProperty("forces").GetProperty("qx").GetDouble());
        Assert.Equal(20, a.RootElement.GetProperty("forces").GetProperty("qy").GetDouble());
        Assert.Equal(a.RootElement.GetProperty("utilization").GetDouble(), b.RootElement.GetProperty("utilization").GetDouble(), 10);
        // Короб 200×400×10: Ix=(.2*.4³-.18*.38³)/12; S=(.2*.4²-.18*.38²)/8; хорда=2*.01.
        double expected = 20 * ((.2*.4*.4 - .18*.38*.38)/8) /
            ((.2*Math.Pow(.4,3)-.18*Math.Pow(.38,3))/12 * .02 * .58 * 240000);
        Assert.Equal(expected, a.RootElement.GetProperty("utilization").GetDouble(), 8);
    }

    [Fact]
    public void FemCountsOverloadedSteelRowAsFailed()
    {
        var check = new FemCheck { SchemaId = 1, ElementId = 7, NormCode = "steel_central_tension" };
        var member = new FemMember { Id = 7, SchemaId = 1, ElemTag = "7", ElemType = "beam" };
        var fs = new ForceSet { Id = 1, Items = [new LoadItem { N = 100 }, new LoadItem { N = 10000 }] };
        var result = FemCheckRunner.RunMulti(check, member, Box(), null, [fs],
            (task, section, item) => TaskRunner.Run(task, section, item));
        using var doc = JsonDocument.Parse(result.DataJson);
        Assert.Equal(1, doc.RootElement.GetProperty("passedRows").GetInt32());
        Assert.Equal(1, doc.RootElement.GetProperty("failedRows").GetInt32());
        Assert.Equal("not_passed", result.Status);
    }

    [Fact]
    public void ResultViewReadsNullableValuesAndNotApplicableChecks()
    {
        var result = TaskRunner.Run(new CalcTask { Kind = "steel_bending" }, Box(), new LoadItem { Mx = 10 });
        Assert.Equal("ok", result.Status);
        var vm = new OpenCS.Views.SteelCheckResultVM(result.DataJson);
        Assert.True(vm.Passed);
        Assert.NotEmpty(vm.Details);
        Assert.Contains(vm.Details, d => d.NotApplicable && !d.Passed);
    }

    [Fact]
    public void CompositeSectionIsNotSilentlyReducedToFirstArea()
    {
        var section = Box();
        section.Areas.Add(new MaterialArea { Material = new Material { Type = MatType.Concrete } });
        var result = TaskRunner.Run(new CalcTask { Kind = "steel_check" }, section, new LoadItem { N = 100 });
        Assert.Equal("error", result.Status);
    }

    [Fact]
    public void CustomMaterialWithRyIsAccepted()
    {
        var section = Box();
        section.Areas[0].Material!.Type = MatType.Custom;
        var r = TaskRunner.Run(new CalcTask { Kind = "steel_central_tension" }, section, new LoadItem { N = 100 });
        Assert.Equal("ok", r.Status);
    }

    [Theory]
    [InlineData("{\"LefX\":0}")]
    [InlineData("{\"LefY\":0}")]
    [InlineData("{\"LefB\":-1}")]
    public void ZeroEffectiveLengthIsRejected(string json)
    {
        var r = TaskRunner.Run(new CalcTask { Kind = "steel_central_compression", ParamsJson = json }, Box(),
            new LoadItem { N = -100 });
        Assert.Equal("error", r.Status);
    }

    [Fact]
    public void MemberCacheFollowsContourChanges()
    {
        var task = new CalcTask { Kind = "steel_central_tension" };
        var small = Box();
        var r1 = TaskRunner.Run(task, small, new LoadItem { N = 100 });
        var big = Box();
        big.Areas[0].Contours[1] = new Contour([-.08,.08,.08,-.08,-.08], [-.18,-.18,.18,.18,-.18], "hole") { Type = ContourType.Hole };
        var r2 = TaskRunner.Run(task, big, new LoadItem { N = 100 });
        var r3 = TaskRunner.Run(task, small, new LoadItem { N = 100 });
        double U(CalcResult r) { using var d = JsonDocument.Parse(r.DataJson); return d.RootElement.GetProperty("utilization").GetDouble(); }
        Assert.True(U(r2) > U(r1));   // большее отверстие — меньше площадь нетто
        Assert.Equal(U(r1), U(r3), 12);
    }

    [Fact]
    public void ResultViewShowsSetupErrorWithoutThrowing()
    {
        var result = TaskRunner.Run(new CalcTask { Kind = "steel_central_compression" }, Box(), new LoadItem { N = 100 });
        Assert.Equal("error", result.Status);
        var vm = new OpenCS.Views.SteelCheckResultVM(result.DataJson);
        Assert.False(vm.Passed);
        Assert.NotEmpty(vm.VerdictText);
    }
}
