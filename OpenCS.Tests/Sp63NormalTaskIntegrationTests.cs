using CScore;
using CScore.Sp63.Normal;
using OpenCS.Services;
using OpenCS.Tasks;
using Xunit;

namespace OpenCS.Tests;

/// <summary>Проверки регистрации и разрешения усилий задачи нормального сечения.</summary>
public sealed class Sp63NormalTaskIntegrationTests
{
    [Fact]
    public void TaskRunner_KnowsSp63NormalKind()
    {
        Assert.Contains("sp63_normal", TaskRunner.KindList);
        Assert.Equal("sp63_normal", new Sp63NormalHandler().Kind);
    }

    [Fact]
    public void ManualForces_AreResolvedFromSp63NormalParams()
    {
        var parameters = new Sp63NormalTaskParams
        {
            UseManualForces = true,
            N = -800.0,
            Mx = -24.0,
            My = 0.0
        };
        var task = new CalcTask
        {
            Kind = "sp63_normal",
            ParamsJson = parameters.ToJson()
        };

        var item = CalcTaskForceHelper.ResolveSingleForces(task, []);

        Assert.NotNull(item);
        Assert.Equal(-800.0, item!.N);
        Assert.Equal(-24.0, item.Mx);
    }

    [Fact]
    public void NotApplicable_IsLoggedAsInformation()
    {
        var result = new CalcResult { Status = "not_applicable" };

        Assert.Equal(LogLevel.Info, CalcResultLogHelper.ResolveLevel(result));
    }
}
