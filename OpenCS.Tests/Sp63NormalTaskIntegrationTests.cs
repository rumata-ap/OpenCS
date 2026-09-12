using System;
using System.IO;
using System.Linq;
using CScore;
using CScore.Sp63.Normal;
using OpenCS.Services;
using OpenCS.Tasks;
using OpenCS.Utilites;
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

    [Fact]
    public void CalcTask_SurvivesSaveAndReloadThroughGsdb()
    {
        string path = Path.Combine(Path.GetTempPath(), $"opencs-sp63-normal-{Guid.NewGuid():N}.db");
        try
        {
            var parameters = new Sp63NormalTaskParams
            {
                ShapeKind = "rectangular",
                Axis = "My",
                StructuralScheme = "statically_determinate",
                ElementLengthOrRestraintDistance = 3.6,
                EffectiveLengthL0 = 2.5,
                StabilityMode = "section_only_explicit",
                Psi = 0.35,
                SlendernessThreshold = 20.0,
                UseManualForces = true,
                N = -650.0,
                Mx = 0.0,
                My = 18.4
            };

            int taskId;
            using (var db = new DatabaseService(path))
            {
                var task = new CalcTask
                {
                    Tag = "Колонна К-1",
                    Kind = "sp63_normal",
                    CalcType = CalcType.C,
                    ParamsJson = parameters.ToJson()
                };
                db.SaveCalcTask(task);
                taskId = task.Id;
                Assert.NotEqual(0, taskId);
            }

            using var reopened = new DatabaseService(path);
            reopened.LoadAll();
            var loaded = Assert.Single(reopened.CalcTasks, t => t.Id == taskId);
            Assert.Equal("sp63_normal", loaded.Kind);
            Assert.Equal("Колонна К-1", loaded.Tag);

            var loadedParams = Sp63NormalTaskParams.Parse(loaded.ParamsJson);
            Assert.Equal(parameters.ShapeKind, loadedParams.ShapeKind);
            Assert.Equal(parameters.Axis, loadedParams.Axis);
            Assert.Equal(parameters.StructuralScheme, loadedParams.StructuralScheme);
            Assert.Equal(parameters.ElementLengthOrRestraintDistance, loadedParams.ElementLengthOrRestraintDistance);
            Assert.Equal(parameters.EffectiveLengthL0, loadedParams.EffectiveLengthL0);
            Assert.Equal(parameters.StabilityMode, loadedParams.StabilityMode);
            Assert.Equal(parameters.Psi, loadedParams.Psi);
            Assert.Equal(parameters.SlendernessThreshold, loadedParams.SlendernessThreshold);
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
