using CScore;
using OpenCS.Utilites;
using System.Collections.Generic;
using System.Text.Json;
using System.Windows.Controls;
using System.Windows.Media;

namespace OpenCS.Views
{
   public partial class CalcResultView : UserControl
   {
      public CalcResultView(CalcResult result)
      {
         InitializeComponent();
         DataContext = new CalcResultViewVM(result);
      }
   }

   public class CalcResultViewVM : ViewModelBase
   {
      public string  TaskTag    { get; }
      public string  Created    { get; }
      public string  StatusText { get; }
      public Brush   StatusBrush { get; }
      public List<ResultRow> Rows { get; } = [];

      public CalcResultViewVM(CalcResult result)
      {
         TaskTag = result.TaskTag;
         Created = result.Created;

         StatusText = result.Status switch
         {
            "ok"            => Loc.S("CalcResultOkLabel"),
            "not_converged" => Loc.S("CalcResultNotConvergedLabel"),
            _               => Loc.S("CalcResultErrorLabel")
         };
         StatusBrush = result.Status switch
         {
            "ok"   => Brushes.Green,
            "error" => Brushes.Red,
            _      => Brushes.OrangeRed
         };

         try
         {
            var doc = JsonDocument.Parse(result.DataJson);
            var root = doc.RootElement;

            if (result.Status == "error")
            {
               if (root.TryGetProperty("error", out var err))
                  Rows.Add(new ResultRow(Loc.S("CalcResultErrorMsg"), err.GetString() ?? ""));
               return;
            }

            AddIfExists(root, "converged",  Loc.S("CalcResultConverged"));
            AddIfExists(root, "iterations", Loc.S("CalcResultIterations"));
            AddIfExists(root, "residual",   Loc.S("CalcResultResidual") + ", кН");
            Rows.Add(new ResultRow("", ""));

            if (root.TryGetProperty("e0", out var e0))
               Rows.Add(new ResultRow("ε₀", $"{e0.GetDouble():G6}"));
            if (root.TryGetProperty("ky", out var ky))
               Rows.Add(new ResultRow("κy, 1/м", $"{ky.GetDouble():G6}"));
            if (root.TryGetProperty("kz", out var kz))
               Rows.Add(new ResultRow("κz, 1/м", $"{kz.GetDouble():G6}"));
            Rows.Add(new ResultRow("", ""));

            AddPair(root, "N_target",  "N_result",  "N, кН");
            AddPair(root, "Mx_target", "Mx_result", "Mx (My), кН·м");
            AddPair(root, "My_target", "My_result", "My (Mz), кН·м");
         }
         catch
         {
            Rows.Add(new ResultRow(Loc.S("CalcResultRawData"), result.DataJson));
         }
      }

      void AddIfExists(JsonElement root, string key, string label)
      {
         if (root.TryGetProperty(key, out var v))
            Rows.Add(new ResultRow(label, v.ToString()));
      }

      void AddPair(JsonElement root, string keyT, string keyR, string label)
      {
         root.TryGetProperty(keyT, out var t);
         root.TryGetProperty(keyR, out var r);
         Rows.Add(new ResultRow(
            label,
            $"{Loc.S("Target")}: {t.GetDouble():F4}    {Loc.S("Result")}: {r.GetDouble():F4}"));
      }

      public record ResultRow(string Label, string Value);
   }
}
