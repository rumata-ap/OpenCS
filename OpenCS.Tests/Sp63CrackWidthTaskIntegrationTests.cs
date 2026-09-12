using System;
using System.IO;
using CScore;
using CScore.Sp63.CrackWidth;
using OpenCS.Services;
using OpenCS.Tasks;
using OpenCS.Utilites;
using Xunit;

namespace OpenCS.Tests;

/// <summary>Проверки регистрации и разрешения усилий задачи ширины раскрытия трещин СП 63.</summary>
public sealed class Sp63CrackWidthTaskIntegrationTests
{
    [Fact]
    public void TaskRunner_KnowsSp63CrackWidthKind()
    {
        Assert.Contains("sp63_crack_width", TaskRunner.KindList);
        Assert.Equal("sp63_crack_width", new Sp63CrackWidthHandler().Kind);
    }

    [Fact]
    public void ManualForces_AreResolvedFromSp63CrackWidthParams()
    {
        var parameters = new Sp63CrackWidthTaskParams
        {
            UseManualForces = true,
            N = 120.0,
            Mx = 90.0,
            My = 0.0
        };
        var task = new CalcTask
        {
            Kind = "sp63_crack_width",
            ParamsJson = parameters.ToJson()
        };

        var item = CalcTaskForceHelper.ResolveSingleForces(task, []);

        Assert.NotNull(item);
        Assert.Equal(120.0, item!.N);
        Assert.Equal(90.0, item.Mx);
    }

    [Fact]
    public void NotApplicable_IsLoggedAsInformation()
    {
        var result = new CalcResult { Status = "not_applicable" };

        Assert.Equal(LogLevel.Info, CalcResultLogHelper.ResolveLevel(result));
    }

    [Fact]
    public void CalcTask_SurvivesSaveAndReloadThroughGsdb()
    {
        string path = Path.Combine(Path.GetTempPath(), $"opencs-sp63-crack-width-{Guid.NewGuid():N}.db");
        try
        {
            var parameters = new Sp63CrackWidthTaskParams
            {
                ShapeKind = "rectangular",
                Axis = "My",
                Phi1 = 1.4,
                Phi2 = 0.5,
                AcrcLimMm = 0.4,
                UseManualForces = true,
                N = 120.0,
                Mx = 0.0,
                My = 90.0
            };

            int taskId;
            using (var db = new DatabaseService(path))
            {
                var task = new CalcTask
                {
                    Tag = "Балка Б-1",
                    Kind = "sp63_crack_width",
                    CalcType = CalcType.N,
                    ParamsJson = parameters.ToJson()
                };
                db.SaveCalcTask(task);
                taskId = task.Id;
                Assert.NotEqual(0, taskId);
            }

            using var reopened = new DatabaseService(path);
            reopened.LoadAll();
            var loaded = Assert.Single(reopened.CalcTasks, t => t.Id == taskId);
            Assert.Equal("sp63_crack_width", loaded.Kind);
            Assert.Equal("Балка Б-1", loaded.Tag);

            var loadedParams = Sp63CrackWidthTaskParams.Parse(loaded.ParamsJson);
            Assert.Equal(parameters.ShapeKind, loadedParams.ShapeKind);
            Assert.Equal(parameters.Axis, loadedParams.Axis);
            Assert.Equal(parameters.Phi1, loadedParams.Phi1);
            Assert.Equal(parameters.Phi2, loadedParams.Phi2);
            Assert.Equal(parameters.AcrcLimMm, loadedParams.AcrcLimMm);
            Assert.Equal(parameters.UseManualForces, loadedParams.UseManualForces);
            Assert.Equal(parameters.N, loadedParams.N);
            Assert.Equal(parameters.Mx, loadedParams.Mx);
            Assert.Equal(parameters.My, loadedParams.My);

            Assert.True(loadedParams.TryToOptions(out _, out _));
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }
}
