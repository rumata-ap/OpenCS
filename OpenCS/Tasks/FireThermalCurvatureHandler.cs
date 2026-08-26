using System.Text.Json;
using CScore;
using CScore.Fire;
using CScore.Fire.Entities;
using OpenCS.Utilites;

namespace OpenCS.Tasks;

/// <summary>Обработчик задачи расчёта температурной кривизны по п. 8.44б СП 468.</summary>
public sealed class FireThermalCurvatureHandler : ITaskHandler
{
   /// <summary>Идентификатор вида задачи.</summary>
   public string Kind => "fire_thermal_curvature";

   /// <inheritdoc/>
   public CalcResult Run(CalcTask task, CrossSection section, LoadItem item,
      CalcSettings settings, TaskRunContext? ctx = null)
   {
      string created = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
      try
      {
         if (ctx?.Database is null)
            return Error(task, created, "FireThermal_ContextRequired");

         var p = FireThermalCurvatureParams.Parse(task.ParamsJson);
         if (p.FireSectionId <= 0)
            return Error(task, created, "FireThermal_FireSectionIdMissing");

         FireSectionDef? fireDef = ctx.FireSections?.FirstOrDefault(f => f.Id == p.FireSectionId);
         if (fireDef is null)
            return Error(task, created, "FireThermal_FireSectionNotFound");

         var reference = FireThermalReference.Resolve(ctx.Database, p.FireSectionId, p.ThermalResultId);
         if (reference.ErrorKey is not null)
            return Error(task, created, reference.ErrorKey);

         FireThermalResult thermal = ctx.Database.LoadFireThermalResult(reference.ResultId);
         if (thermal.MeshInfo.Mesh.Elements.Any(e => e.Length != 3))
            return Error(task, created, "FireThermal_T6MechanicalUnsupported");

         section.ResolveAndBuildDiagramms(settings.Sp63DescEtaMin,
            pool: ctx.Database.Diagrams,
            rebarDifferentialDiagram: settings.RebarDifferentialDiagram,
            ekbEtaMin: settings.EkbDescEtaMin);

         var fiber = FireFiberSection.FromThermalResult(thermal, section, p.SnapshotIndex);

         string method = string.IsNullOrWhiteSpace(p.CompressionZoneMethod)
            || p.CompressionZoneMethod.Equals("auto", StringComparison.OrdinalIgnoreCase)
            ? settings.FireCompressionZoneMethod
            : p.CompressionZoneMethod;

         var input = new FireThermalCurvatureInput(
            Fiber: fiber,
            Def: fireDef,
            MeshStepM: fireDef.MeshStepM,
            NormalizedLimitMin: p.NormalizedLimitMin > 0.0 ? p.NormalizedLimitMin : 120.0,
            TensionRebarAtHeatedFace: p.TensionRebarAtHeatedFace,
            CompressionZoneMethod: method);

         FireThermalCurvatureResult result = FireThermalCurvature.Run(input);
         var data = new
         {
            criterion = "curvature",
            norm_edition = "SP468-2019/izm1",
            thermal_result_id = reference.ResultId,
            legacy_thermal_reference = reference.IsLegacyFallback,
            fire_section_id = fireDef.Id,
            fire_section_name = fireDef.Tag,
            snapshot_index = fiber.SnapshotIndex,
            normalized_limit_min = input.NormalizedLimitMin,
            tension_rebar_at_heated_face = input.TensionRebarAtHeatedFace,
            compression_zone_method = input.CompressionZoneMethod,
            chi_t = result.ChiT,
            eps_t = result.EpsT,
            D = result.D,
            t_hot_concrete = result.THotConcrete,
            t_cold_concrete = result.TColdConcrete,
            t_rebar = result.TRebar,
            height_m = result.HeightM,
            h = result.HeightM,
            h0 = result.H0M,
            A_s = result.AsM2,
            alpha_bt = result.AlphaBt,
            alpha_st = result.AlphaSt,
            E_st = result.EstPa,
            xi_r = result.XiR,
            x_t = result.XtM,
            x_t_method = result.XtMethod,
            x_t_method_fallback = result.XtMethodFallback,
            xi_capped = result.XiCapped,
            z = result.ZM,
            z_simplified = result.ZSimplifiedM,
            phi1 = result.Phi1,
            axis_x = result.AxisX,
            axis_y = result.AxisY,
            axis_from_inertia = result.AxisFromInertia,
            uniform_heating = result.UniformHeating,
            rebar_both_faces = result.RebarBothFaces,
            eps_b2_out_of_range = result.EpsB2OutOfRange,
            aggregate_not_silicate = result.AggregateNotSilicate,
            profile_quality = result.ProfileQuality,
            d_unsupported = result.DUnsupportedReasonKey,
            profile = result.Profile.Select(x => new
            {
               s = x.S,
               t_actual = x.TActual,
               t_linear = x.TLinear
            }),
            rebar_details = result.RebarDetails.Select(x => new
            {
               class_group = x.ClassGroup.ToString(),
               class_source = x.ClassSource,
               temperature_c = x.TemperatureCelsius,
               area_m2 = x.AreaM2,
               gamma_st = x.GammaSt,
               gamma_st_e = x.GammaStE
            })
         };

         return new CalcResult
         {
            TaskId = task.Id,
            TaskKind = task.Kind,
            TaskTag = task.Tag,
            Created = created,
            Status = "ok",
            DataJson = JsonSerializer.Serialize(data)
         };
      }
      catch (FireCalculationException ex)
      {
         return Error(task, created, ex.ErrorKey);
      }
      catch (Exception)
      {
         return Error(task, created, "FireCurvature_CalculationError");
      }
   }

   static CalcResult Error(CalcTask task, string created, string errorKey)
      => new()
      {
         TaskId = task.Id,
         TaskKind = task.Kind,
         TaskTag = task.Tag,
         Created = created,
         Status = "error",
         DataJson = JsonSerializer.Serialize(new { error = Loc.S(errorKey), error_key = errorKey })
      };
}
