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
/// (ПС1 — limit_surface; ПС2, образование трещин — crack_surface; ширина раскрытия — подпроект 4).
/// </summary>
public sealed class StrainSurfaceHandler : ITaskHandler
{
   /// <inheritdoc />
   public string Kind => "limit_surface";

   /// <summary>Критерий поверхности для вида задачи; ПС2 строится только по характеристикам N/NL с растянутым бетоном.</summary>
   internal static IStrainSurfaceCriterion CriterionFor(string kind, CalcType calc, bool ten) => kind switch
   {
      "limit_surface" => new UlsStrainCriterion(calc, ten),
      "crack_surface" => new CrackStrainCriterion(calc),
      _ => throw new InvalidOperationException($"Вид поверхности «{kind}» не поддерживается."),
   };

   /// <summary>Код критерия в DataJson.</summary>
   internal static string CriterionCode(IStrainSurfaceCriterion criterion) =>
      criterion is CrackStrainCriterion ? LimitSurfaceResultData.CriterionCrack : LimitSurfaceResultData.CriterionUls;

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
         var criterion = CriterionFor(task.Kind, task.CalcType, settings.ResolveConcreteTension(task.CalcType));
         bool ten = criterion.Ten;
         var parameters = LimitSurfaceTaskParams.Parse(task.ParamsJson);
         var options = parameters.ToOptions();

         var sw = Stopwatch.StartNew();
         var mesh = new StrainDomainMesher(section, criterion, options)
            .Build(ctx?.CancellationToken ?? default);
         sw.Stop();

         var data = new LimitSurfaceResultData
         {
            Criterion = CriterionCode(criterion),
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
            InitialMeridians = options.InitialMeridians,
            ThetaIntervals = options.ThetaIntervals,
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
