using CScore;

namespace OpenCS.Tasks;

/// <summary>Разрешение усилий для расчётной задачи (набор / ParamsJson).</summary>
public static class CalcTaskForceHelper
{
   internal static bool IsLimitSingleKind(string kind)
      => kind is "limit_force" or "limit_moment" or "limit_axial";

   public static bool UsesManualForces(CalcTask task)
      => task.Kind == "strain_state" || task.Kind == "cracking" || task.Kind == "crack_width"
         || task.Kind == "total_curvature" || task.Kind == "moment_curvature_biaxial"
         || task.Kind == "shear_inclined"
         || IsLimitSingleKind(task.Kind);

   /// <summary>Задачи, для которых не нужна строка стержневого набора усилий (batch / ParamsJson / оболочки / сталь).</summary>
   public static bool UsesDummyForceItem(CalcTask task) => task.Kind switch
   {
      "strain_state_batch" or "limit_force_batch" or "limit_moment_batch" or "limit_axial_batch"
         or "two_stage_strain" or "two_stage_strain_batch"
         or "shell_simpl_wa_sls" or "shell_simpl_wa_uls"
         or "shell_simpl_capri_sls" or "shell_simpl_capri_uls"
         or "shell_simpl_wa_sls_batch" or "shell_simpl_wa_uls_batch"
         or "shell_simpl_capri_sls_batch" or "shell_simpl_capri_uls_batch"
         or "shell_strain_state" or "shell_strain_state_batch"
         or "shell_layered_uls" or "shell_layered_uls_batch"
         or "strength_ndm_batch" or "prestress_loss"
          or "steel_check"
          or "steel_central_compression" or "steel_central_tension"
          or "steel_bending" or "steel_compression_bending"
          or "steel_tension_bending" or "steel_shear"
          or "steel_torsion" or "steel_constructive"
          or "torsion_bem" or "torsion_fem"
         or "cracking_batch" or "crack_width_batch" or "total_curvature_batch"
         or "fire_r_check_batch" or "fire_thermal_curvature" => true,
      "opensees_section_interaction_n_mx_my" => true,
      _ => false
   };

   /// <summary>
   /// Получить усилия для одиночной задачи с ручным вводом или из набора (устаревшие limit_*).
   /// </summary>
   internal static LoadItem? ResolveSingleForces(CalcTask task, IEnumerable<ForceSet> forceSets)
   {
      if (IsLimitSingleKind(task.Kind) && task.ForceSetId != 0 && task.ForceItemId != 0)
      {
         var fromSet = forceSets.FirstOrDefault(f => f.Id == task.ForceSetId)
            ?.Items.FirstOrDefault(i => i.Id == task.ForceItemId);
         if (fromSet != null)
            return fromSet;
      }

      if (task.Kind == "total_curvature")
         return TotalCurvatureTaskParams.Parse(task.ParamsJson).ToLoadItem();

      if (task.Kind == "moment_curvature_biaxial")
         return MomentCurvatureBiaxialTaskParams.Parse(task.ParamsJson).ToLoadItem();

      // Наклонные сечения: усилия либо введены вручную, либо (в задачах, сохранённых
      // до появления ручного ввода) берутся из строки набора — тогда null.
      if (task.Kind == "shear_inclined")
      {
         var shear = ShearInclinedParams.Parse(task.ParamsJson);
         if (shear.ManualForces is { } manual)
            return manual.ToLoadItem();
         // Профиль из FEM строит эпюру Q(s)/M(s) сам — строка усилий ему не нужна.
         return shear.ForceSource == "fem_profile" ? new LoadItem() : null;
      }

      try
      {
         return LimitForceParams.Parse(task.ParamsJson).ToLoadItem();
      }
      catch
      {
         return null;
      }
   }

   /// <summary>
   /// Строка набора усилий для задач с UsesDummyForceItem (сталь, кручение и т.д.).
   /// Если ForceItemId задан — подставляет T/N/M из набора, иначе пустой LoadItem.
   /// </summary>
   public static LoadItem ResolveOptionalForceItem(CalcTask task, IEnumerable<ForceSet> forceSets)
   {
      if (task.ForceItemId != 0)
      {
         var fromSet = forceSets.FirstOrDefault(f => f.Id == task.ForceSetId)
            ?.Items.FirstOrDefault(i => i.Id == task.ForceItemId);
         if (fromSet != null)
            return fromSet;
      }
      return new LoadItem();
   }
}
