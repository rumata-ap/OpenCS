using System.Text.Json;
using CScore;
using CScore.History;
using OpenCS.Utilites;

namespace OpenCS.Tasks;

/// <summary>
/// Задача «История нагружения» стадийного сечения: этапы по нагрузке с включением частей, доведение до
/// разрушения по кривизне, сравнение «без истории». Работает на копии сечения (решатель меняет его состояние).
/// </summary>
public sealed class LoadHistoryHandler : ITaskHandler
{
    /// <inheritdoc/>
    public string Kind => "load_history";

    /// <inheritdoc/>
    public CalcResult Run(CalcTask task, CrossSection section, LoadItem item, CalcSettings settings, TaskRunContext? ctx = null)
    {
        var created = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
        CalcResult Fail(string message) => new()
        {
            TaskId = task.Id, TaskKind = task.Kind, TaskTag = task.Tag, Created = created,
            Status = "error", DataJson = JsonSerializer.Serialize(new { error = message }),
        };
        try
        {
            if (section is not StagedSection source)
                return Fail("Сечение задачи не является стадийным.");
            var p = LoadHistoryParams.Parse(task.ParamsJson);
            var errors = p.Validate(source.Parts.Count);
            if (errors.Count > 0)
                return Fail("Параметры истории неверны: " + string.Join(", ", errors));

            var forceSets = ctx?.Database?.ForceSets ?? [];
            var stages = p.Stages.Select(st =>
            {
                var f = StageForceResolver.Resolve(st.Force, forceSets).Single();
                return new HistoryStage(st.Tag, st.Kind, st.ActivatePart,
                    new Load { N = f.N, Mx = f.Mx, My = f.My }, Math.Max(1, st.Steps));
            }).ToList();

            var s = (StagedSection)source.CloneForCalc();
            s.ResolveAndBuildDiagramms(settings.Sp63DescEtaMin, pool: ctx?.Database?.Diagrams,
                rebarDifferentialDiagram: settings.RebarDifferentialDiagram, ekbEtaMin: settings.EkbDescEtaMin);
            // гистерезису нужны волокна для состояния — режим «по волокнам» независимо от настройки
            if (p.Hysteresis is not null || settings.StageStrainMode == "fibers")
            {
                s.StrainMode = StageStrainMode.Fibers;
                s.PrepareFiberMode();
            }

            var o = new LoadHistoryOptions
            {
                Calc = task.CalcType,
                Ten = settings.ResolveConcreteTension(task.CalcType),
                Tol = settings.NewtonTolerance,
                MaxIter = settings.NewtonMaxIter,
                H = settings.NewtonDeltaH,
                CentralJacobian = settings.NewtonJacobian == "central",
                Failure = p.Failure,
                Roles = p.ToRoles(),
                Fallback = p.Fallback,
                Hysteresis = p.Hysteresis,
                Token = ctx?.CancellationToken ?? default,
                Progress = f => ctx?.Progress?.Report(new CalcTaskProgress { Fraction = f }),
            };
            var result = new LoadHistorySolver(s, o).Run(stages, p.CompareWithoutHistory);
            var data = LoadHistoryResultData.Build(result, s, stages, o);
            return new CalcResult
            {
                TaskId = task.Id, TaskKind = task.Kind, TaskTag = task.Tag, Created = created,
                Status = data.Status, DataJson = data.ToJson(),
            };
        }
        catch (OperationCanceledException) { return Fail("Отменено"); }
        catch (Exception ex) { return Fail(ex.Message); }
    }
}
