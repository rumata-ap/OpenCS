using System.Diagnostics;
using System.Text.Json;
using CScore;
using CScore.LimitSurface;
using OpenCS.LimitSurface;
using OpenCS.Utilites;

namespace OpenCS.Tasks;

/// <summary>
/// Задачи поверхностей усилий (N, Mx, My) из области деформаций: строит сетку ядром, снимает точки набора и
/// сохраняет всё в DataJson. Общий для всех видов поверхностей; критерий — по виду задачи
/// (ПС1 — limit_surface; ПС2 добавятся в подпроектах 3, 4).
/// </summary>
public sealed class StrainSurfaceHandler : ITaskHandler
{
   /// <inheritdoc />
   public string Kind => "limit_surface";

   /// <summary>Критерий поверхности для вида задачи.</summary>
   internal static IStrainSurfaceCriterion CriterionFor(string kind, CalcType calc, bool ten) => kind switch
   {
      "limit_surface" => new UlsStrainCriterion(calc, ten),
      _ => throw new InvalidOperationException($"Вид поверхности «{kind}» не поддерживается."),
   };

   /// <inheritdoc />
   public CalcResult Run(CalcTask task, CrossSection section, LoadItem item, CalcSettings settings, TaskRunContext? ctx = null)
   {
      string created = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
      try
      {
         if (section is TwoStageSection)
            throw new InvalidOperationException("Поверхность для сечения двух стадий не строится.");

         ForceSet? forceSet = null;
         if (task.ForceSetId != 0)
            forceSet = ctx?.Database?.ForceSets.FirstOrDefault(fs => fs.Id == task.ForceSetId)
               ?? throw new InvalidOperationException($"Набор усилий id={task.ForceSetId} не найден.");

         section.ResolveAndBuildDiagramms(settings.Sp63DescEtaMin,
            pool: ctx?.Database?.Diagrams,
            rebarDifferentialDiagram: settings.RebarDifferentialDiagram, ekbEtaMin: settings.EkbDescEtaMin);
         bool ten = settings.ResolveConcreteTension(task.CalcType);
         var criterion = CriterionFor(task.Kind, task.CalcType, ten);
         var parameters = LimitSurfaceTaskParams.Parse(task.ParamsJson);

         var sw = Stopwatch.StartNew();
         var mesh = new StrainDomainMesher(section, criterion, parameters.ToOptions())
            .Build(ctx?.CancellationToken ?? default);
         sw.Stop();

         var data = new LimitSurfaceResultData
         {
            Criterion = "uls",
            CalcType = task.CalcType,
            Ten = ten,
            Accuracy = parameters.Accuracy,
            ControlPoints = criterion.BuildDomain(section).Points
               .Select(p => new LimitSurfaceControlPointRecord(p.Kind, p.X, p.Y, p.AreaIndex)).ToList(),
            Mesh = LimitSurfaceResultData.EncodeMesh(mesh),
            ForceSetId = forceSet?.Id ?? 0,
            ForceSetName = forceSet?.Tag,
            Points = forceSet?.Items.Select(i => new LimitSurfacePointRecord(i.Num, i.Label, i.N, i.Mx, i.My)).ToList() ?? [],
            VertexCount = mesh.Vertices.Count,
            MeridianCount = mesh.MeridianCount,
            BuildSeconds = sw.Elapsed.TotalSeconds,
         };
         if (forceSet is { SecondOrder: false } && data.Points.Count > 0)
            data.Warnings.Add(LimitSurfaceWarnings.EtaNotApplied);

         return new CalcResult
         {
            TaskId = task.Id, TaskKind = task.Kind, TaskTag = task.Tag, Created = created,
            Status = "ok", DataJson = data.ToJson(),
         };
      }
      catch (Exception ex)
      {
         return new CalcResult
         {
            TaskId = task.Id, TaskKind = task.Kind, TaskTag = task.Tag, Created = created,
            Status = "error", DataJson = JsonSerializer.Serialize(new { error = ex.Message }),
         };
      }
   }
}
