using OpenCS.Utilites;

namespace OpenCS.Tasks;

/// <summary>Результат разрешения ссылки задачи на тепловой расчёт.</summary>
/// <param name="ResultId">Фактически используемый идентификатор результата.</param>
/// <param name="IsLegacyFallback">Ссылка была нулевой и подставлен последний расчёт.</param>
/// <param name="ErrorKey">Ключ строки ошибки; null, если ссылка разрешена.</param>
public readonly record struct FireThermalReferenceResult(
   int ResultId, bool IsLegacyFallback, string? ErrorKey);

/// <summary>
/// Разрешение ссылки огневой задачи на конкретный тепловой расчёт с проверкой владельца.
/// </summary>
/// <remarks>
/// Проверка выполняется на уровне базы: <see cref="CScore.Fire.FireThermalResult"/>
/// идентификатора огневого сечения не несёт, поэтому по одному распакованному BLOB'у
/// принадлежность установить нельзя. Нулевой идентификатор допускается только для
/// задач, сохранённых до введения явной привязки, и помечается флагом.
/// </remarks>
public static class FireThermalReference
{
   /// <summary>Разрешить ссылку задачи на тепловой расчёт.</summary>
   public static FireThermalReferenceResult Resolve(
      DatabaseService db, int fireSectionId, int thermalResultId)
   {
      ArgumentNullException.ThrowIfNull(db);

      if (thermalResultId > 0)
      {
         int? owner = db.GetFireThermalResultOwner(thermalResultId);
         if (owner is null)
            return new FireThermalReferenceResult(0, false, "FireThermalResultNotFound");
         if (owner.Value != fireSectionId)
            return new FireThermalReferenceResult(0, false, "FireThermalResultOwnerMismatch");

         return new FireThermalReferenceResult(thermalResultId, false, null);
      }

      var history = db.ListFireThermalResults(fireSectionId);
      if (history.Count == 0)
         return new FireThermalReferenceResult(0, false, "FireThermalResultNotFound");

      return new FireThermalReferenceResult(history[0].Id, true, null);
   }
}
