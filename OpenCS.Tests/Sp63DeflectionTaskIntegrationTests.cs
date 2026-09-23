using System.Text.Json;
using CScore;
using CScore.Sp63.Deflection;
using OpenCS.Tasks;
using OpenCS.Utilites;
using OpenCS.ViewModels;
using Xunit;

namespace OpenCS.Tests;

public sealed class Sp63DeflectionTaskIntegrationTests
{
    static CrossSection Section()
    {
        var concrete = new Material { Id = 1, Tag = "B25", Type = MatType.Concrete, E = 30_000_000 };
        concrete.N = new MaterialChars(CalcType.N)
        { Type = MatType.Concrete, Fc = -18_500, Ft = 1_550, E = 30_000_000, Class = 25 };
        var rebar = new Material { Id = 2, Tag = "A400", Type = MatType.ReSteelU, E = 200_000_000 };
        rebar.N = new MaterialChars(CalcType.N)
        { Type = MatType.ReSteelU, Fc = -500_000, Ft = 500_000, E = 200_000_000 };
        var section = new CrossSection { Tag = "test" };
        var region = new MaterialArea { Category = AreaCategory.Region, Material = concrete, MaterialId = 1 };
        region.Contours.Add(new Contour([-0.25, 0.25, 0.25, -0.25, -0.25],
            [-0.15, -0.15, 0.15, 0.15, -0.15], "hull") { Type = ContourType.Hull });
        region.SetWKT();
        section.Areas.Add(region);
        foreach (double y in new[] { -0.11, 0.11 })
        {
            var group = new MaterialArea { Category = AreaCategory.RebarGroup, Material = rebar, MaterialId = 2 };
            group.Fibers.Add(new Fiber { TypeFiber = FiberType.point, X = 0, Y = y, Area = 20e-4, Diameter = 0.02 });
            section.Areas.Add(group);
        }
        return section;
    }

    static CalcTask Task(double limit, double longMoment, double fullMoment = 80) => new()
    {
        Kind = "sp63_deflection",
        CalcType = CalcType.N,
        ParamsJson = new Sp63DeflectionTaskParams
        {
            UseManualForces = true, N = 10, Mx = fullMoment, My = 0,
            ForcesMode = "manual", NLongManual = 4, MxLongManual = longMoment,
            MyLongManual = 0, Scheme = "simply_supported_uniform", SpanM = 6,
            DeflectionLimitMm = limit
        }.ToJson()
    };

    [Fact]
    public void TaskRunner_MapsDeflectionFailureToNotPassed()
    {
        var task = Task(0.01, 20);
        var resolved = CalcTaskForceHelper.ResolveSingleForces(task, []);
        Assert.Equal(10, resolved!.N);
        Assert.Equal(80, resolved.Mx);
        Assert.Equal(CalcTaskGroups.Sls, CalcTaskGroups.Classify(task.Kind));
        Assert.Contains("sp63_deflection", TaskRunner.KindList);
        var result = TaskRunner.Run(task, Section(), new LoadItem(), CalcSettings.Default);

        Assert.Equal("not_passed", result.Status);
        Assert.Equal(Sp63DeflectionStatus.Calculated,
            JsonSerializer.Deserialize<Sp63DeflectionResult>(result.DataJson)!.Status);
    }

    [Fact]
    public void TaskRunner_MapsNotApplicableWithoutFailureVerdict()
    {
        var result = TaskRunner.Run(Task(20, -20), Section(), new LoadItem(), CalcSettings.Default);

        Assert.Equal("not_applicable", result.Status);
        var domain = JsonSerializer.Deserialize<Sp63DeflectionResult>(result.DataJson)!;
        Assert.Equal(Sp63DeflectionStatus.NotApplicable, domain.Status);
        Assert.Null(domain.DeflectionPassed);
        Assert.Equal(80, domain.Variables["M"]);
        Assert.Equal(-20, domain.Variables["Ml"]);
        Assert.Equal(10, domain.Variables["N"]);
        Assert.Equal(4, domain.Variables["Nl"]);
        Assert.Equal(0, domain.Variables["axis"]);
    }
}
