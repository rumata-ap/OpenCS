using CScore;
using CScore.Fire;
using CScore.Fire.Entities;
using OpenCS.Utilites;

namespace OpenCS.Tasks;

/// <summary>Тепловой расчёт огневого сечения, пригодный для огневой задачи.</summary>
/// <param name="ResultId">Идентификатор строки расчёта на момент счёта задачи.</param>
/// <param name="InputHash">Хеш входных данных расчёта; null для строк до миграции v56.</param>
/// <param name="Thermal">Температурное поле.</param>
public sealed record FireThermalReferenceResult(int ResultId, string? InputHash, FireThermalResult Thermal);

/// <summary>Тепловой расчёт огневого сечения нельзя использовать в задаче.</summary>
/// <param name="errorKey">Ключ строки ошибки (для машинной обработки).</param>
/// <param name="message">Локализованный текст с подставленными значениями.</param>
public sealed class FireThermalUnavailableException(string errorKey, string message)
   : InvalidOperationException(message)
{
   /// <summary>Ключ строки ошибки.</summary>
   public string ErrorKey { get; } = errorKey;
}

/// <summary>
/// Тепловой расчёт огневого сечения для огневой задачи с проверкой актуальности.
/// </summary>
/// <remarks>
/// У огневого сечения ровно один тепловой расчёт, задача ссылается на само огневое
/// сечение. Расчёт, выполненный по другим входным данным (длительность, сетка, сечение,
/// материалы — всё, что входит в <see cref="FireThermalInputSnapshot"/>), считается
/// устаревшим, и задача завершается ошибкой, а не считает по старому полю.
/// </remarks>
public static class FireThermalReference
{
   /// <summary>Получить актуальный тепловой расчёт огневого сечения.</summary>
   /// <param name="section">Сечение задачи с разрешёнными материалами.</param>
   /// <exception cref="FireThermalUnavailableException">Расчёта нет, он устарел или выполнен на сетке T6.</exception>
   public static FireThermalReferenceResult Resolve(DatabaseService db, FireSectionDef def, CrossSection section)
   {
      ArgumentNullException.ThrowIfNull(db);
      ArgumentNullException.ThrowIfNull(def);
      ArgumentNullException.ThrowIfNull(section);

      var info = db.GetFireThermalResultInfo(def.Id)
         ?? throw new FireThermalUnavailableException("FireThermal_NotComputed",
               string.Format(Loc.S("FireThermal_NotComputed"), def.Tag));

      string? staleReason = StaleReason(db, def, section, info);
      if (staleReason is not null)
         throw new FireThermalUnavailableException("FireThermal_Stale",
            string.Format(Loc.S("FireThermal_Stale"), def.Tag, staleReason));

      FireThermalResult thermal = db.LoadFireThermalResult(info.Id);
      if (thermal.MeshInfo.Mesh.Elements.Any(e => e.Length != 3))
         throw new FireThermalUnavailableException("FireThermal_T6MechanicalUnsupported",
            Loc.S("FireThermal_T6MechanicalUnsupported"));

      return new FireThermalReferenceResult(info.Id, info.InputHash, thermal);
   }

   /// <summary>
   /// Причина устаревания расчёта (локализованный текст); null, если расчёт актуален.
   /// Сверка возможна только по сечению, связанному с огневым сечением: для задачи
   /// на другом сечении хеш не сравнивается.
   /// </summary>
   public static string? StaleReason(
      DatabaseService db, FireSectionDef def, CrossSection section, DatabaseService.FireThermalResultInfo info)
   {
      if (info.InputHash is null)
         return Loc.S("FireStale_Unknown");
      if (section.Id != def.SectionId)
         return null;

      FireThermalInput current;
      try
      {
         current = FireThermalInputSnapshot.Build(def, section, EffectiveAggregate(def, section));
      }
      catch (InvalidOperationException)
      {
         // Снимок не строится (нет основного контура) — об актуальности судить не по чему.
         return null;
      }

      if (string.Equals(current.Hash, info.InputHash, StringComparison.Ordinal))
         return null;
      return Loc.S(FireThermalInputSnapshot.FirstDifference(
         db.GetFireThermalResultInputJson(info.Id), current.Json) ?? "FireStale_Unknown");
   }

   /// <summary>
   /// Индекс снимка для момента проверки: <paramref name="timeMin"/> = null — конец расчёта,
   /// иначе ближайший снимок. Для задач, не прошедших миграцию v62, учитывается старый
   /// <paramref name="legacyIndex"/>, если он в диапазоне.
   /// </summary>
   /// <exception cref="FireThermalUnavailableException">Время больше длительности расчёта.</exception>
   public static int ResolveSnapshotIndex(FireThermalResult thermal, double? timeMin, int legacyIndex = -1)
   {
      ArgumentNullException.ThrowIfNull(thermal);
      double[] times = thermal.TimesMin;
      if (timeMin is not double t)
         return legacyIndex >= 0 && legacyIndex < thermal.Snapshots.Length ? legacyIndex : -1;
      if (times.Length == 0)
         return -1;

      int best = 0;
      for (int i = 1; i < times.Length; i++)
         if (Math.Abs(times[i] - t) < Math.Abs(times[best] - t)) best = i;

      double last = times[^1];
      double halfStep = times.Length > 1 ? 0.5 * (last - times[^2]) : 0.0;
      if (t > last + halfStep + 1e-9)
         throw new FireThermalUnavailableException("FireThermal_TimeBeyondDuration",
            string.Format(Loc.S("FireThermal_TimeBeyondDuration"), t, last));

      return best;
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
