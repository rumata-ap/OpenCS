using CScore;
using CScore.Fire;
using CScore.Fire.Entities;
using OpenCS.Utilites;

namespace OpenCS.Tasks;

/// <summary>
/// Актуальность теплового расчёта, на который ссылается огневая задача: есть ли более
/// новый расчёт того же огневого сечения и совпадают ли входные данные использованного
/// расчёта с текущими параметрами огневого сечения (по хешу снимка, как в истории расчётов).
/// </summary>
/// <param name="UsedResultId">Использованный тепловой расчёт.</param>
/// <param name="NewerResultId">Более новый расчёт этого огневого сечения; null, если использован последний.</param>
/// <param name="StaleReason">Что изменилось после теплового расчёта; null, если данные совпадают или снимка нет.</param>
public readonly record struct FireThermalFreshness(int UsedResultId, int? NewerResultId, string? StaleReason)
{
   /// <summary>Есть о чём предупредить пользователя.</summary>
   public bool HasWarning => NewerResultId is not null || StaleReason is not null;

   /// <summary>Текст предупреждения для результата задачи; null, если предупреждать не о чем.</summary>
   public string? WarningText
   {
      get
      {
         if (!HasWarning) return null;
         var parts = new List<string>();
         if (StaleReason is not null)
            parts.Add(string.Format(Loc.S("FireRCheck_ThermalStale"), UsedResultId, StaleReason));
         if (NewerResultId is int newer)
            parts.Add(string.Format(Loc.S("FireRCheck_ThermalNewer"), UsedResultId, newer));
         return string.Join(" ", parts);
      }
   }

   /// <summary>Проверить актуальность использованного теплового расчёта.</summary>
   /// <param name="section">Связанное сечение с разрешёнными материалами.</param>
   public static FireThermalFreshness Check(DatabaseService db, FireSectionDef def, CrossSection section, int usedResultId)
   {
      var history = db.ListFireThermalResults(def.Id);   // новые — первыми
      int? newer = history.Count > 0 && history[0].Id != usedResultId ? history[0].Id : null;

      string? reason = null;
      var used = history.FirstOrDefault(h => h.Id == usedResultId);
      if (used?.InputHash is not null)
      {
         try
         {
            var current = FireThermalInputSnapshot.Build(def, section, EffectiveAggregate(def, section));
            if (!string.Equals(current.Hash, used!.InputHash, StringComparison.Ordinal))
               reason = Loc.S(FireThermalInputSnapshot.FirstDifference(
                  db.GetFireThermalResultInputJson(usedResultId), current.Json) ?? "FireStale_Unknown");
         }
         catch (InvalidOperationException)
         {
            // Снимок не строится (нет основного контура) — об актуальности судить не по чему.
         }
      }

      return new FireThermalFreshness(usedResultId, newer, reason);
   }

   /// <summary>Эффективный тип заполнителя — так же, как при запуске теплового расчёта.</summary>
   static string EffectiveAggregate(FireSectionDef def, CrossSection section)
   {
      if (!string.IsNullOrWhiteSpace(def.AggregateType))
         return def.AggregateType.Trim().ToLowerInvariant();
      var concrete = section.Areas.Select(a => a.Material).FirstOrDefault(m => m?.Type == MatType.Concrete);
      return concrete is null || string.IsNullOrWhiteSpace(concrete.AggregateType)
         ? "silicate" : concrete.AggregateType;
   }
}
