using OpenCS.Utilites;
using CScore;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Text.Json;

namespace OpenCS.ViewModels;

/// <summary>Строка таблицы factor(t).</summary>
public sealed record FireRTimeRowVM(string TimeText, string FactorText, string GoverningText, bool Converged);

/// <summary>Результат задачи собственного предела огнестойкости.</summary>
public sealed class FireRTimeResultVM
{
   public string RMinText { get; }
   public string StatusNote { get; }
   public string RefinementText { get; }
   public bool HasStatusNote => StatusNote.Length > 0;
   public ObservableCollection<FireRTimeRowVM> Rows { get; } = [];
   public FireLineChartVM? FactorChart { get; }
   public bool HasFactorChart => FactorChart is { Series.Count: > 0 };

   public FireRTimeResultVM(CalcResult result)
   {
      if (FireResultJson.TryGetError(result.DataJson, out string error))
      {
         RMinText = "—";
         StatusNote = error;
         RefinementText = "";
         return;
      }

      JsonElement root = FireResultJson.Root(result.DataJson);

      double? rMin = FireResultJson.DblOrNull(root, "r_min");
      RMinText = rMin?.ToString("F1", CultureInfo.InvariantCulture) ?? "—";

      var notes = new List<string>();
      if (FireResultJson.Bool(root, "failed_at_start"))
         notes.Add(Loc.S("FireRTime_FailedAtStart"));
      if (FireResultJson.Bool(root, "limit_not_reached"))
         notes.Add(string.Format(CultureInfo.InvariantCulture, Loc.S("FireRTime_NotReached"),
            FireResultJson.Dbl(root, "r_min_lower_bound")));
      if (FireResultJson.Bool(root, "non_monotone"))
         notes.Add(Loc.S("FireRTime_NonMonotone"));

      if (root.TryGetProperty("unreliable_snapshots", out JsonElement unreliable)
          && unreliable.ValueKind == JsonValueKind.Array)
      {
         string indices = string.Join(", ", unreliable.EnumerateArray()
            .Where(x => x.ValueKind == JsonValueKind.Number)
            .Select(x => x.GetInt32().ToString(CultureInfo.InvariantCulture)));
         if (indices.Length > 0)
            notes.Add(string.Format(Loc.S("FireRTime_Unreliable"), indices));
      }
      StatusNote = string.Join(Environment.NewLine, notes);

      RefinementText = FireResultJson.Str(root, "refinement", "none") == "linear_between_snapshots"
         ? string.Format(CultureInfo.InvariantCulture, Loc.S("FireRTime_RefinementLinear"),
              FireResultJson.Dbl(root, "refinement_bracket_min"),
              FireResultJson.Dbl(root, "refinement_bracket_max"))
         : "";

      var times = new List<double>();
      var factors = new List<double>();
      if (root.TryGetProperty("rows", out JsonElement rows) && rows.ValueKind == JsonValueKind.Array)
      {
         foreach (JsonElement row in rows.EnumerateArray())
         {
            double t = FireResultJson.Dbl(row, "time_min");
            double? f = FireResultJson.DblOrNull(row, "factor");
            bool converged = FireResultJson.Bool(row, "converged");

            Rows.Add(new FireRTimeRowVM(
               t.ToString("F1", CultureInfo.InvariantCulture),
               f?.ToString("F3", CultureInfo.InvariantCulture) ?? "—",
               FireResultJson.Str(row, "governing", ""),
               converged));

            if (converged && f.HasValue) { times.Add(t); factors.Add(f.Value); }
         }
      }

      if (times.Count > 1)
      {
         FactorChart = new FireLineChartVM(
            Loc.S("FireRTime_ColFactor"),
            Loc.S("FireRTime_ColTime"),
            Loc.S("FireRTime_ColFactor"),
            [new FireLineSeries(
               Loc.S("FireRTime_ColFactor"), times.ToArray(), factors.ToArray(), "#2563EB")]);
      }
   }
}
