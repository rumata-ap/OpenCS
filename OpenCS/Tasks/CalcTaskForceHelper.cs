using CScore;

namespace OpenCS.Tasks;

/// <summary>Разрешение усилий для расчётной задачи (набор / ParamsJson).</summary>
internal static class CalcTaskForceHelper
{
   internal static bool IsLimitSingleKind(string kind)
      => kind is "limit_force" or "limit_moment" or "limit_axial";

   internal static bool UsesManualForces(CalcTask task)
      => task.Kind == "strain_state" || IsLimitSingleKind(task.Kind);

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
}
