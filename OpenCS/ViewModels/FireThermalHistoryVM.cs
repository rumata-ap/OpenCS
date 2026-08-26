using CScore;
using CScore.Fire;
using CScore.Fire.Entities;
using OpenCS.Utilites;
using System.Collections.ObjectModel;
using System.Globalization;

namespace OpenCS.ViewModels;

/// <summary>Строка истории тепловых расчётов.</summary>
public sealed record FireThermalHistoryRow(
   int Id,
   string CreatedText,
   string SnapshotsText,
   string DurationText,
   string StateText,
   bool IsCurrent);

/// <summary>
/// История тепловых расчётов огневого сечения с отметкой актуальности.
/// </summary>
/// <remarks>
/// Результат считается актуальным, если хеш его снимка входных данных совпадает
/// с хешем текущего состояния огневого сечения и связанного поперечного сечения.
/// Строки, сохранённые до миграции v56, снимка не имеют — их состояние неизвестно.
/// </remarks>
public sealed class FireThermalHistoryVM : ViewModelBase
{
   readonly DatabaseService _db;

   public ObservableCollection<FireThermalHistoryRow> Rows { get; } = [];

   public FireThermalHistoryVM(DatabaseService db) => _db = db;

   /// <summary>Перечитать историю и пересчитать состояние актуальности.</summary>
   public void Reload(int fireSectionId, FireSectionDef def, CrossSection? section, string effectiveAggregate)
   {
      Rows.Clear();
      if (fireSectionId <= 0) return;

      string? currentHash = null;
      string? currentJson = null;
      if (section is not null)
      {
         var input = FireThermalInputSnapshot.Build(def, section, effectiveAggregate);
         currentHash = input.Hash;
         currentJson = input.Json;
      }

      foreach (var info in _db.ListFireThermalResults(fireSectionId))
      {
         bool isCurrent = currentHash is not null
                       && info.InputHash is not null
                       && string.Equals(info.InputHash, currentHash, StringComparison.Ordinal);

         string state;
         if (info.InputHash is null)
            state = Loc.S("FireStale_Unknown");
         else if (isCurrent)
            state = Loc.S("FireThermal_StateCurrent");
         else
         {
            string? reasonKey = FireThermalInputSnapshot.FirstDifference(
               _db.GetFireThermalResultInputJson(info.Id), currentJson);
            state = string.Format(Loc.S("FireThermal_StateStale"),
               Loc.S(reasonKey ?? "FireStale_Unknown"));
         }

         Rows.Add(new FireThermalHistoryRow(
            info.Id,
            info.Created,
            info.SnapshotCount?.ToString(CultureInfo.InvariantCulture) ?? "—",
            info.DurationMin?.ToString("F0", CultureInfo.InvariantCulture) ?? "—",
            state,
            isCurrent));
      }
   }

   /// <summary>Идентификатор расчёта, предлагаемого по умолчанию: свежий актуальный, иначе свежий.</summary>
   public int? PreferredId
      => Rows.FirstOrDefault(r => r.IsCurrent)?.Id ?? Rows.FirstOrDefault()?.Id;
}
