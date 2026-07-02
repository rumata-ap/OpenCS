using CScore;

namespace OpenCS.Tasks;

/// <summary>Разрешение усилий для расчётной задачи (набор / ParamsJson).</summary>
internal static class CalcTaskForceHelper
{
   internal static bool IsLimitSingleKind(string kind)
      => kind is "limit_force" or "limit_moment" or "limit_axial";

   internal static bool UsesManualForces(CalcTask task)
      => task.Kind == "strain_state" || IsLimitSingleKind(task.Kind);

   /// <summary>Задачи, для которых не нужна строка стержневого набора усилий (batch / ParamsJson / оболочки / сталь).</summary>
   internal static bool UsesDummyForceItem(CalcTask task) => task.Kind switch
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
          or "torsion_bem" or "torsion_fem" => true,
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
   internal static LoadItem ResolveOptionalForceItem(CalcTask task, IEnumerable<ForceSet> forceSets)
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
