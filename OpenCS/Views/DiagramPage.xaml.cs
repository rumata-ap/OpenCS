using CScore;
using CSmath;
using OpenCS.Services;
using OpenCS.Utilites;

using Microsoft.Win32;

using System;
using System.Collections.Generic;
using System.Data;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Controls;

namespace OpenCS.Views
{
   public partial class DiagramPage : UserControl
   {
      readonly AppViewModel mvm;
      readonly Diagramm diagram;
      IPlotService? _plotService;

      public DiagramPage(Diagramm diagram, AppViewModel mvm)
      {
         InitializeComponent();
         this.mvm = mvm;
         this.diagram = diagram;

         titleText.Text = diagram.Tag;
          infoText.Text = $"{Loc.S("MaterialColon")} {(diagram.MaterialId > 0 ? $"id={diagram.MaterialId}" : "—")}  |  {Loc.S("CalcTypeColon")} {diagram.CalcType}";

         _plotService = new WpfPlotService(plot);
         _plotService.ApplySettings(mvm.PlotSettings);
         DrawPlot();
         PopulateData();
      }

      void DrawPlot()
      {
         if (_plotService == null) return;
         _plotService.Clear();
          _plotService.SetTitle(string.Format(Loc.S("DiagramTitle"), diagram.Tag));
          _plotService.SetXLabel("ε");
          _plotService.SetYLabel(Loc.S("Sigma_MPa"));

          PlotBranch(diagram.Ic, Loc.S("Compression"), "#0000C8");
          PlotBranch(diagram.It, Loc.S("Tension"), "#C80000");

         _plotService.ShowLegend();
         _plotService.AutoScale();
         _plotService.Refresh();
      }

      void PlotBranch(ISpline? spline, string label, string colorHex)
      {
         if (spline?.X == null || spline.X.Length < 2) return;

         bool hasInvalidNodes = spline.X.Any(double.IsNaN) || spline.Y.Any(double.IsNaN);
         if (hasInvalidNodes)
         {
             _plotService?.AddMarkers(spline.X, spline.Y, 5, colorHex, label + " " + Loc.S("Nodes"));
            return;
         }

         int sampleCount = Math.Max(spline.X.Length * 10, 80);
         double xMin = spline.X.Min();
         double xMax = spline.X.Max();
         double step = (xMax - xMin) / (sampleCount - 1);

         var xs = new double[sampleCount];
         var ys = new double[sampleCount];
         int realCount = 0;
         for (int i = 0; i < sampleCount; i++)
         {
            double xi = xMin + step * i;
            double yi = spline.Interpolate(xi);
            if (double.IsFinite(yi))
            {
               xs[realCount] = xi;
               ys[realCount] = yi;
               realCount++;
            }
         }

         if (realCount < 2)
         {
             _plotService?.AddMarkers(spline.X, spline.Y, 4, colorHex, label + " " + Loc.S("Nodes"));
            return;
         }

         var trimXs = new double[realCount];
         var trimYs = new double[realCount];
         Array.Copy(xs, trimXs, realCount);
         Array.Copy(ys, trimYs, realCount);

          _plotService?.AddScatter(trimXs, trimYs, 2, colorHex, label);
          _plotService?.AddMarkers(spline.X, spline.Y, 4, colorHex, label + " " + Loc.S("Nodes"));
      }

      void PopulateData()
      {
         var rows = new List<RowData>();
         var seenZero = false;

         void AddPoints(CSmath.ISpline? spline, string color)
         {
            if (spline?.X == null) return;
            for (int i = 0; i < spline.X.Length; i++)
            {
               double x = spline.X[i], y = spline.Y[i];
               bool isZero = Math.Abs(x) < 1e-12 && Math.Abs(y) < 1e-12;
               if (isZero)
               {
                  if (seenZero) continue;
                  seenZero = true;
                  rows.Add(new RowData { Eps = 0, Sig = 0, Color = "" });
               }
               else
                  rows.Add(new RowData { Eps = x, Sig = y, Color = color });
            }
         }

         AddPoints(diagram.Ic, "#D0D0FF");
         AddPoints(diagram.It, "#FFD0D0");

         dataGrid.ItemsSource = rows;
         dataGrid.AutoGenerateColumns = false;
         dataGrid.Columns.Clear();

         var epsCol = new DataGridTextColumn { Header = "ε", Binding = new System.Windows.Data.Binding("Eps"), Width = new DataGridLength(1, DataGridLengthUnitType.Star) };
         epsCol.Binding.StringFormat = "F6";
          var sigCol = new DataGridTextColumn { Header = Loc.S("Sigma_MPa"), Binding = new System.Windows.Data.Binding("Sig"), Width = new DataGridLength(1, DataGridLengthUnitType.Star) };
         sigCol.Binding.StringFormat = "F2";

         dataGrid.Columns.Add(epsCol);
         dataGrid.Columns.Add(sigCol);

         dataGrid.LoadingRow += (s, e) =>
         {
            if (e.Row.Item is RowData rd && rd.Color.Length > 0)
               e.Row.Background = ParseBrush(rd.Color);
         };
      }

      class RowData
      {
         public double Eps { get; set; }
         public double Sig { get; set; }
         public string Color { get; set; } = "";
      }

      static System.Windows.Media.Brush ParseBrush(string hex)
      {
         try { return new System.Windows.Media.SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(hex)); }
         catch { return System.Windows.Media.Brushes.Gray; }
      }

      void ExportCsv_Click(object sender, RoutedEventArgs e)
      {
         var dlg = new SaveFileDialog
         {
             Title = Loc.S("ExportDiagramCsv"),
             Filter = Loc.S("CsvFilter"),
            FileName = $"{diagram.Tag ?? "diagram"}.csv"
         };
         if (dlg.ShowDialog() != true) return;

         var settings = mvm.CsvSettings;
         var delim = settings.Delimiter == "," ? "," : ";";
         Encoding enc;
         if (settings.Encoding == "utf-8")
            enc = new UTF8Encoding(false);
         else
         {
            try { enc = Encoding.GetEncoding(1251); }
            catch { enc = Encoding.UTF8; }
         }

         var sb = new StringBuilder();
          sb.AppendLine(string.Join(delim, "ε", "σ, МПа"));

         bool seenZero = false;
         AppendBranchCsv(sb, diagram.Ic, delim, ref seenZero);
         AppendBranchCsv(sb, diagram.It, delim, ref seenZero);

         try { File.WriteAllText(dlg.FileName, sb.ToString(), enc); }
          catch (Exception ex) { MessageBox.Show(string.Format(Loc.S("ErrorSave"), ex.Message), Loc.S("Error"), MessageBoxButton.OK, MessageBoxImage.Error); }
      }

      void AppendBranchCsv(StringBuilder sb, ISpline? spline, string delim, ref bool seenZero)
      {
         if (spline?.X == null) return;
         for (int i = 0; i < spline.X.Length; i++)
         {
            double x = spline.X[i], y = spline.Y[i];
            if (Math.Abs(x) < 1e-12 && Math.Abs(y) < 1e-12)
            {
               if (seenZero) continue;
               seenZero = true;
            }
            sb.AppendLine($"{x.ToString(CultureInfo.InvariantCulture)}{delim}{y.ToString(CultureInfo.InvariantCulture)}");
         }
      }

      void Delete_Click(object sender, RoutedEventArgs e)
      {
          var res = MessageBox.Show(Loc.S("ConfirmDeleteDiagram"), Loc.S("Confirmation"),
             MessageBoxButton.YesNo, MessageBoxImage.Warning);
          if (res != MessageBoxResult.Yes) return;

          mvm.db.DeleteDiagram(diagram);
          mvm.db.Diagrams.Remove(diagram);
          mvm.CurrentPage = null;
          mvm.LogService.Info(string.Format(Loc.S("DiagramDeletedCode"), diagram.Tag));
      }

      void Close_Click(object sender, RoutedEventArgs e)
      {
         mvm.CurrentPage = null;
      }
   }
}
