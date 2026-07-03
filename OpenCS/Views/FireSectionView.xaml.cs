using CScore;
using CScore.Fire;
using CScore.Fire.Entities;
using OpenCS.Utilites;
using OpenCS.ViewModels;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace OpenCS.Views
{
   /// <summary>
   /// Базовое представление огневого сечения с запуском теплового расчёта.
   /// </summary>
   public partial class FireSectionView : UserControl
   {
      readonly FireSectionViewModel _vm;
      FireThermalResultView? _thermalResultView;
      bool _thermalTabLoaded;

      public FireSectionView(FireSectionDef model, AppViewModel app)
      {
         InitializeComponent();
         _vm = new FireSectionViewModel(model, app);
         DataContext = _vm;
         Loaded += (_, _) => Dispatcher.BeginInvoke(DispatcherPriority.Background, RefreshPreview);
      }

      void MainTab_SelectionChanged(object sender, SelectionChangedEventArgs e)
      {
         if (e.Source != mainTabs || mainTabs.SelectedItem != thermalResultTab || _thermalTabLoaded)
            return;

         _thermalTabLoaded = true;
         _thermalResultView = new FireThermalResultView();
         _thermalResultView.SetBinding(
            DataContextProperty,
            new System.Windows.Data.Binding(nameof(FireSectionViewModel.ThermalResult)) { Source = _vm });
         thermalResultTab.Content = _thermalResultView;
      }

      internal void BcPreset_SelectionChanged(object sender, SelectionChangedEventArgs e)
      {
         if (!IsLoaded) return;
         RefreshPreview();
      }

      internal void RefreshPreview()
      {
         _vm.RefreshPreview();
         noPreviewLabel.Visibility = _vm.Preview.HasGeometry
            ? Visibility.Collapsed
            : Visibility.Visible;
      }

      void ShowMesh_Changed(object sender, RoutedEventArgs e)
      {
         if (!IsLoaded) return;
         _vm.OnShowMeshChanged(_vm.Preview.ShowMesh);
      }

      void MeshStep_LostFocus(object sender, RoutedEventArgs e)
      {
         if (!IsLoaded) return;
         _vm.OnMeshStepChanged();
      }
   }

   /// <summary>
   /// ViewModel представления огневого сечения.
   /// </summary>
   public class FireSectionViewModel : ViewModelBase
   {
      readonly FireSectionDef _model;
      readonly AppViewModel _app;

      string tag;
      string fireDurationMinText;
      string fireCurve;
      string meshStepMText;
      string timeStepSText;
      string bcPreset;
      string aggregateType;
      string meshElementType;
      string lastRunInfo = "";
      int _thermalLoadToken;

      public ICommand SaveCommand { get; }
      public ICommand RunThermalCommand { get; }
      public FirePreviewVM Preview { get; }

      public FireSectionDef Model => _model;
      public AppViewModel App => _app;

      public string Tag
      {
         get => tag;
         set { tag = value; OnPropertyChanged(); }
      }

      public string LinkedCrossSectionName
      {
         get
         {
            var sec = _app.CrossSections.FirstOrDefault(x => x.Id == _model.SectionId);
            return sec?.Tag ?? Loc.S("FireSection_SectionNotFound");
         }
      }

      public string FireDurationMinText
      {
         get => fireDurationMinText;
         set { fireDurationMinText = value; OnPropertyChanged(); }
      }

      public string FireCurve
      {
         get => fireCurve;
         set { fireCurve = value; OnPropertyChanged(); }
      }

      public string MeshStepMText
      {
         get => meshStepMText;
         set { meshStepMText = value; OnPropertyChanged(); }
      }

      public string TimeStepSText
      {
         get => timeStepSText;
         set { timeStepSText = value; OnPropertyChanged(); }
      }

      public string BcPreset
      {
         get => bcPreset;
         set
         {
            bcPreset = value;
            OnPropertyChanged();
            RefreshPreview();
         }
      }

      public string AggregateType
      {
         get => aggregateType;
         set { aggregateType = value; OnPropertyChanged(); }
      }

      public string MeshElementType
      {
         get => meshElementType;
         set
         {
            meshElementType = value;
            OnPropertyChanged();
            RefreshPreview();
         }
      }

      public string LastRunInfo
      {
         get => lastRunInfo;
         set { lastRunInfo = value; OnPropertyChanged(); }
      }

      public FireThermalResultVM ThermalResult { get; private set; }

      public FireSectionViewModel(FireSectionDef model, AppViewModel app)
      {
         _model = model;
         _app = app;
         tag = model.Tag;
         fireDurationMinText = model.FireDurationMin.ToString("G", CultureInfo.InvariantCulture);
         fireCurve = model.FireCurve;
         meshStepMText = model.MeshStepM.ToString("G", CultureInfo.InvariantCulture);
         timeStepSText = model.TimeStepS.ToString("G", CultureInfo.InvariantCulture);
         bcPreset = model.BcPreset;
         aggregateType = string.IsNullOrWhiteSpace(model.AggregateType)
            ? ResolveDefaultAggregateType()
            : model.AggregateType;
         meshElementType = "linear";
         Preview = new FirePreviewVM();
         ThermalResult = FireThermalResultVM.CreateLoading(_model);
         SaveCommand = new RelayCommand(_ => Save());
         RunThermalCommand = new RelayCommand(_ => RunThermal());
         StartLoadThermalResultAsync();
      }

      void StartLoadThermalResultAsync()
      {
         if (_model.Id <= 0)
         {
            ThermalResult = new FireThermalResultVM(_model, null, null);
            OnPropertyChanged(nameof(ThermalResult));
            return;
         }

         int token = ++_thermalLoadToken;
         Task.Run<(FireThermalResult? thermal, int? rid)>(() =>
         {
            try
            {
               int? rid = _app.db.GetLatestFireThermalResultId(_model.Id);
               if (rid is null)
                  return (null, null);
               return (_app.db.LoadFireThermalResult(rid.Value), rid);
            }
            catch
            {
               return (null, null);
            }
         }).ContinueWith(t =>
         {
            Application.Current?.Dispatcher.BeginInvoke(DispatcherPriority.Background, () =>
            {
               if (token != _thermalLoadToken)
                  return;

               var (thermal, rid) = t.Result;
               ThermalResult = new FireThermalResultVM(_model, thermal, rid);
               OnPropertyChanged(nameof(ThermalResult));
            });
         });
      }

      string ResolveDefaultAggregateType()
      {
         var section = _app.CrossSections.FirstOrDefault(s => s.Id == _model.SectionId);
         if (section == null) return "silicate";
         foreach (var area in section.Areas)
         {
            var mat = area.Material ?? _app.Materials.FirstOrDefault(m => m.Id == area.MaterialId);
            if (mat?.Type == MatType.Concrete)
               return string.IsNullOrWhiteSpace(mat.AggregateType) ? "silicate" : mat.AggregateType;
         }
         return "silicate";
      }

      public void RefreshPreview()
      {
         var section = _app.CrossSections.FirstOrDefault(s => s.Id == _model.SectionId);
         double meshStep = ParsePositiveOrDefault(MeshStepMText, _model.MeshStepM, 0.01);
         Preview.Configure(
            _model,
            section,
            BcPreset,
            meshStep,
            _model.Algorithm,
            _model.SmoothIterTri,
            MeshElementType);
      }

      internal void OnShowMeshChanged(bool show) => Preview.OnShowMeshChanged(show);

      internal void OnMeshStepChanged()
      {
         double meshStep = ParsePositiveOrDefault(MeshStepMText, _model.MeshStepM, 0.01);
         Preview.OnMeshStepChanged(meshStep);
      }

      void RefreshThermalResult()
      {
         ThermalResult = FireThermalResultVM.CreateLoading(_model);
         OnPropertyChanged(nameof(ThermalResult));
         StartLoadThermalResultAsync();
      }

      void Save()
      {
         ApplyFormToModel();
         _app.db.SaveFireSection(_model);
         LastRunInfo = Loc.S("FireSection_Saved");
         _app.LogService.Info(string.Format(Loc.S("FireSection_SavedLog"), _model.Tag));
      }

      void RunThermal()
      {
         ApplyFormToModel();
         var section = _app.CrossSections.FirstOrDefault(s => s.Id == _model.SectionId);
         if (section == null)
         {
            LastRunInfo = Loc.S("FireSection_SectionNotFound");
            _app.LogService.Error(Loc.S("FireSection_SectionNotFound"));
            return;
         }

         try
         {
            if (_model.Id == 0)
               _app.db.SaveFireSection(_model);

            var result = FireThermalService.Run(_model, section, ResolveAggregateType(section));
            int resultId = _app.db.SaveFireThermalResult(_model.Id, result);
            RefreshThermalResult();
            LastRunInfo = string.Format(Loc.S("FireSection_RunOk"), resultId);
            _app.LogService.Info(string.Format(Loc.S("FireSection_RunOkLog"), _model.Tag, resultId));
         }
         catch (Exception ex)
         {
            LastRunInfo = string.Format(Loc.S("FireSection_RunError"), ex.Message);
            _app.LogService.Error(string.Format(Loc.S("FireSection_RunError"), ex.Message));
         }
      }

      void ApplyFormToModel()
      {
         _model.Tag = string.IsNullOrWhiteSpace(Tag)
            ? string.Format(Loc.S("FireSection_DefaultTag"), _app.FireSections.Count + 1)
            : Tag.Trim();
         _model.FireDurationMin = ParsePositiveOrDefault(FireDurationMinText, _model.FireDurationMin, 60.0);
         _model.FireCurve = string.IsNullOrWhiteSpace(FireCurve) ? "iso834" : FireCurve;
         _model.MeshStepM = ParsePositiveOrDefault(MeshStepMText, _model.MeshStepM, 0.01);
         _model.TimeStepS = ParsePositiveOrDefault(TimeStepSText, _model.TimeStepS, 5.0);
         _model.BcPreset = string.IsNullOrWhiteSpace(BcPreset) ? "manual" : BcPreset;
         _model.AggregateType = aggregateType ?? "";
         _model.MeshElementType = "linear";
      }

      static double ParsePositiveOrDefault(string? text, double current, double fallback)
      {
         if (string.IsNullOrWhiteSpace(text)) return current > 0.0 ? current : fallback;
         string normalized = text.Trim().Replace(',', '.');
         if (!double.TryParse(normalized, NumberStyles.Float, CultureInfo.InvariantCulture, out var value))
            return current > 0.0 ? current : fallback;
         return value > 0.0 ? value : (current > 0.0 ? current : fallback);
      }

      string ResolveAggregateType(CrossSection section)
      {
         if (!string.IsNullOrWhiteSpace(_model.AggregateType))
            return _model.AggregateType.Trim().ToLowerInvariant();
         if (!string.IsNullOrWhiteSpace(aggregateType))
            return aggregateType.Trim().ToLowerInvariant();
         foreach (var area in section.Areas)
         {
            var mat = area.Material ?? _app.Materials.FirstOrDefault(m => m.Id == area.MaterialId);
            if (mat?.Type == MatType.Concrete)
               return string.IsNullOrWhiteSpace(mat.AggregateType) ? "silicate" : mat.AggregateType;
         }
         return "silicate";
      }
   }

   /// <summary>
   /// Построение элементов превью огневого сечения: контур, рёбра с цветом ГУ, арматура.
   /// </summary>
   internal static class FirePreviewBuilder
   {
      static readonly Brush s_fireBrush = CreateFrozenBrush(224, 32, 32);
      static readonly Brush s_ambientBrush = CreateFrozenBrush(32, 80, 220);
      static readonly Brush s_adiabaticBrush = CreateFrozenBrush(128, 128, 128);
      static readonly Brush s_fillBrush = CreateFrozenBrush(240, 240, 240, alpha: 80);
      static readonly Brush s_holeFillBrush = CreateFrozenBrush(200, 200, 200, alpha: 50);
      static readonly Brush s_rebarFill = CreateFrozenBrush(204, 51, 51);

      static Brush CreateFrozenBrush(byte r, byte g, byte b, byte alpha = 255)
      {
         var br = new SolidColorBrush(Color.FromArgb(alpha, r, g, b));
         br.Freeze();
         return br;
      }

      public static MaterialArea? GetPrimaryConcreteArea(CrossSection? section)
      {
         if (section == null) return null;

         static bool IsPointOnly(MaterialArea area)
         {
            if (area.Fibers.Count == 0) return false;
            return area.Fibers.All(f => f.TypeFiber == FiberType.point);
         }

         var preferred = section.Areas.FirstOrDefault(a =>
             a.Hull != null &&
             !IsPointOnly(a) &&
             (a.Category == AreaCategory.Region || a.Material?.Type == MatType.Concrete));
         if (preferred != null) return preferred;

         return section.Areas.FirstOrDefault(a => a.Hull != null && !IsPointOnly(a));
      }

      public static List<PlotElement> BuildPreviewElements(
          FireSectionDef def, CrossSection section, MaterialArea area, string? bcPreset = null)
      {
         var elements = new List<PlotElement>();
         var hull = area.Hull!;

         int n = hull.X.Count;
         bool closed = n >= 2 && NearlyEqual(hull.X[0], hull.X[n - 1]) && NearlyEqual(hull.Y[0], hull.Y[n - 1]);
         if (closed) n--;

         double[] xs = new double[n];
         double[] ys = new double[n];
         for (int i = 0; i < n; i++) { xs[i] = hull.X[i]; ys[i] = hull.Y[i]; }

         if (n >= 3)
         {
            var polyXs = new double[n + 1];
            var polyYs = new double[n + 1];
            for (int i = 0; i < n; i++) { polyXs[i] = xs[i]; polyYs[i] = ys[i]; }
            polyXs[n] = xs[0]; polyYs[n] = ys[0];
            elements.Add(new PolygonElement
            {
               Xs = polyXs, Ys = polyYs,
               Fill = s_fillBrush, Stroke = Brushes.Black, StrokeThickness = 0.8
            });
         }

         foreach (var hole in area.Holes)
         {
            int hn = hole.X.Count;
            if (hn < 3) continue;
            var hx = new double[hn + 1];
            var hy = new double[hn + 1];
            for (int i = 0; i < hn; i++) { hx[i] = hole.X[i]; hy[i] = hole.Y[i]; }
            hx[hn] = hole.X[0]; hy[hn] = hole.Y[0];
            elements.Add(new PolygonElement
            {
               Xs = hx, Ys = hy,
               Fill = s_holeFillBrush, Stroke = Brushes.Black, StrokeThickness = 0.5
            });
         }

         var bcTypes = ResolveEdgeBcTypes(def, xs, ys, n, bcPreset ?? def.BcPreset);
         for (int i = 0; i < n; i++)
         {
            int j = (i + 1) % n;
            var brush = bcTypes[i] switch
            {
                "fire" => s_fireBrush,
                "ambient" => s_ambientBrush,
                _ => s_adiabaticBrush
            };
            elements.Add(new ScatterElement
            {
               Xs = [xs[i], xs[j]],
               Ys = [ys[i], ys[j]],
               Stroke = brush,
               StrokeThickness = 3
            });
         }

         foreach (var a in section.Areas)
         {
            foreach (var f in a.Fibers)
            {
               if (f.TypeFiber != FiberType.point) continue;
               double r = f.Diameter > 0 ? f.Diameter * 0.5 : 0.005;
               elements.Add(new CircleElement
               {
                  X = f.X, Y = f.Y, Radius = r,
                  Fill = s_rebarFill, Stroke = Brushes.Black, StrokeThickness = 0.5
               });
            }
         }

         return elements;
      }

      public static string[] ResolveEdgeBcTypes(FireSectionDef def, double[] xs, double[] ys, int n, string? bcPreset)
      {
         var result = new string[n];
         string preset = (bcPreset ?? def.BcPreset ?? "manual").Trim().ToLowerInvariant();

         for (int i = 0; i < n; i++)
         {
            var explicitDef = def.Edges.FirstOrDefault(e =>
                string.Equals((e.ContourType ?? "").Trim().ToLowerInvariant(), "outer", StringComparison.Ordinal) &&
                (e.HoleIndex ?? 0) == 0 &&
                e.EdgeIndex == i);

            if (explicitDef != null)
            {
               result[i] = (explicitDef.BcType ?? "adiabatic").Trim().ToLowerInvariant();
               continue;
            }

            if (preset == "all-sided")
               result[i] = "fire";
            else if (preset == "manual")
               result[i] = "adiabatic";
            else
               result[i] = "ambient";
         }

         if (preset is "1-sided" or "2-sided" or "3-sided")
         {
            double minX = double.MaxValue, maxX = double.MinValue;
            double minY = double.MaxValue, maxY = double.MinValue;
            for (int i = 0; i < n; i++)
            {
               int j = (i + 1) % n;
               double mx = (xs[i] + xs[j]) * 0.5;
               double my = (ys[i] + ys[j]) * 0.5;
               if (mx < minX) minX = mx;
               if (mx > maxX) maxX = mx;
               if (my < minY) minY = my;
               if (my > maxY) maxY = my;
            }

            double tolX = Math.Max((maxX - minX) * 0.05, 1e-9);
            double tolY = Math.Max((maxY - minY) * 0.05, 1e-9);

            for (int i = 0; i < n; i++)
            {
               int j = (i + 1) % n;
               double mx = (xs[i] + xs[j]) * 0.5;
               double my = (ys[i] + ys[j]) * 0.5;
               bool isBottom = Math.Abs(my - minY) <= tolY;
               bool isTop = Math.Abs(my - maxY) <= tolY;
               bool isLeft = Math.Abs(mx - minX) <= tolX;
               bool isRight = Math.Abs(mx - maxX) <= tolX;

               bool isFire = preset switch
               {
                  "1-sided" => isBottom,
                  "2-sided" => isLeft || isRight,
                  "3-sided" => !isTop,
                  _ => false
               };

               var explicitDef = def.Edges.FirstOrDefault(e =>
                   string.Equals((e.ContourType ?? "").Trim().ToLowerInvariant(), "outer", StringComparison.Ordinal) &&
                   (e.HoleIndex ?? 0) == 0 &&
                   e.EdgeIndex == i);

               if (explicitDef == null)
                  result[i] = isFire ? "fire" : "ambient";
            }
         }

         return result;
      }

      public static string[] ResolveHoleEdgeBcTypes(
          FireSectionDef def, double[] xs, double[] ys, int n, int holeIndex)
      {
         var result = new string[n];
         string holePreset = (def.HoleBcPreset ?? "ambient").Trim().ToLowerInvariant();
         for (int i = 0; i < n; i++)
         {
            var explicitDef = def.Edges.FirstOrDefault(e =>
                string.Equals((e.ContourType ?? "").Trim().ToLowerInvariant(), "hole", StringComparison.Ordinal) &&
                e.HoleIndex == holeIndex &&
                e.EdgeIndex == i);

            if (explicitDef != null)
               result[i] = (explicitDef.BcType ?? "adiabatic").Trim().ToLowerInvariant();
            else
               result[i] = holePreset == "ambient" ? "ambient" : "adiabatic";
         }
         return result;
      }

      public static void ComputeBounds(
          IReadOnlyList<PlotElement> elements,
          ref double xMin, ref double xMax,
          ref double yMin, ref double yMax)
      {
         foreach (var el in elements)
         {
            if (el is PolygonElement poly)
            {
               foreach (double x in poly.Xs) { if (x < xMin) xMin = x; if (x > xMax) xMax = x; }
               foreach (double y in poly.Ys) { if (y < yMin) yMin = y; if (y > yMax) yMax = y; }
            }
            else if (el is CircleElement circ)
            {
               double r = circ.Radius;
               if (circ.X - r < xMin) xMin = circ.X - r;
               if (circ.X + r > xMax) xMax = circ.X + r;
               if (circ.Y - r < yMin) yMin = circ.Y - r;
               if (circ.Y + r > yMax) yMax = circ.Y + r;
            }
            else if (el is ScatterElement sc)
            {
               foreach (double x in sc.Xs) { if (x < xMin) xMin = x; if (x > xMax) xMax = x; }
               foreach (double y in sc.Ys) { if (y < yMin) yMin = y; if (y > yMax) yMax = y; }
            }
         }
      }

      static bool NearlyEqual(double a, double b) => Math.Abs(a - b) <= 1e-12;
   }
}
