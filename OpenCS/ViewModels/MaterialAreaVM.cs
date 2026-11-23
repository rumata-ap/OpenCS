using CScore;
using OpenCS.Converters;
using OpenCS.Services;
using OpenCS.Utilites;
using OpenCS.Views;
using OpenCS.Views.Dialogs;
using OpenCS.Views.Helpers;

using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;

namespace OpenCS.ViewModels
{
   /// <summary>Режим отображения сетки фибр на превью.</summary>
   public enum FiberDisplayMode { Centroids, Elements }

   /// <summary>
   /// ViewModel области пула. Правки идут в объект области (превью и сечения видят их сразу), в базу — только по
   /// «Сохранить» (здесь или у сечения); <see cref="IsDirty"/> — по снимку <see cref="AreaSnapshot"/>, «Не сохранять»
   /// возвращает область к снимку. Сетка волокон пишется окном «Сетка» сразу.
   /// </summary>
   public class MaterialAreaVM : ViewModelBase, IUnsavedChanges
   {
      readonly MaterialArea _model;
      FiberDisplayMode _fiberDisplayMode = FiberDisplayMode.Elements;
      AreaSnapshot _saved;

      public MaterialAreaVM(MaterialArea model, AppViewModel app)
      {
         _model = model;
         App = app;
         _saved = AreaSnapshot.Capture(model);
         SetHullFromPoolCommand  = new RelayCommand(o => SetHullFromPool(o as Contour));
         ClearHullCommand        = new RelayCommand(_ => ClearHull());
         AddHoleCommand          = new RelayCommand(o => AddHole(o as Contour));
         RemoveHoleCommand       = new RelayCommand(o => RemoveHole(o as Contour));
         SaveCommand             = new RelayCommand(_ => Save());
         DeleteCommand           = new RelayCommand(_ => Delete());
         OpenMeshDialogCommand   = new RelayCommand(_ => OpenMeshDialog());
         ClearMeshCommand        = new RelayCommand(_ => ClearMesh());
         TranslateCommand        = new RelayCommand(_ => Translate());
         ShowPropertiesCommand   = new RelayCommand(_ => ShowProperties());
      }

      public AppViewModel App { get; }
      public MaterialArea Model => _model;

      /// <summary>Есть правки области, не записанные в проект.</summary>
      public bool IsDirty => !_saved.Matches(_model);

      /// <summary>Обозначение для вопроса о сохранении.</summary>
      public string DisplayName => !string.IsNullOrWhiteSpace(_model.Tag) ? _model.Tag
         : !string.IsNullOrWhiteSpace(_saved.Tag) ? _saved.Tag : Loc.S("NewArea");

      /// <summary>Уведомляет о возможной смене <see cref="IsDirty"/>.</summary>
      void Touch() => OnPropertyChanged(nameof(IsDirty));

      /// <summary>Запоминает текущее состояние как записанное (после сохранения области или сечения).</summary>
      public void MarkSaved()
      {
         _saved = AreaSnapshot.Capture(_model);
         Touch();
      }

      /// <summary>Возвращает область к записанному состоянию.</summary>
      public void Discard()
      {
         _saved.RestoreTo(_model, App.db.Diagrams, App.CalcSettings);
         OnPropertyChanged(string.Empty);
         RefreshPlot();
      }

      public string Tag
      {
         get => _model.Tag;
         set { _model.Tag = value; OnPropertyChanged(); Touch(); }
      }

      public Material? Material
      {
         get => _model.Material;
         set
         {
            _model.Material = value;
            _model.MaterialId = value?.Id ?? 0;
            _model.ResolveAndBuildDiagramms(App.CalcSettings.Sp63DescEtaMin, pool: App.db.Diagrams,
               rebarDifferentialDiagram: App.CalcSettings.RebarDifferentialDiagram, ekbEtaMin: App.CalcSettings.EkbDescEtaMin);
            OnPropertyChanged();
            OnPropertyChanged(nameof(MaterialType));
            OnPropertyChanged(nameof(IsCustomMaterial));
            OnPropertyChanged(nameof(EpsSp));
            OnPropertyChanged(nameof(DiagrammType));
            RefreshPlot();
            Touch();
         }
      }

      public MatType MaterialType => _model.Material?.Type ?? MatType.None;

      /// <summary>true если назначенный материал — Custom (для скрытия DiagrammType-комбо в UI).</summary>
      public bool IsCustomMaterial => _model.Material?.Type == MatType.Custom;

      /// <summary>Видимость блока преднапряжения — только для арматурных областей.</summary>
      public Visibility PrestressVisibility =>
         (_model.Category == AreaCategory.RebarGroup)
            ? Visibility.Visible : Visibility.Collapsed;

      /// <summary>Предварительное напряжение после потерь [МПа].</summary>
      public double SigSp
      {
         get => _model.SigSp;
         set
         {
            _model.SigSp = value;
            _model.PropagateEps_p();
            OnPropertyChanged();
            OnPropertyChanged(nameof(EpsSp));
            Touch();
         }
      }

      /// <summary>Индекс γ_sp для ComboBox: 0→1.0 (по умолч.), 1→0.9 (благопр.), 2→1.1 (неблагопр.).</summary>
      public int GammaSpIndex
      {
         get => _model.GammaSp switch { 0.9 => 1, 1.1 => 2, _ => 0 };
         set
         {
            _model.GammaSp = value switch { 1 => 0.9, 2 => 1.1, _ => 1.0 };
            _model.PropagateEps_p();
            OnPropertyChanged();
            OnPropertyChanged(nameof(EpsSp));
            Touch();
         }
      }

      /// <summary>Вычисленная ε_sp в промиллях, только для чтения.</summary>
      public string EpsSp
      {
         get
         {
            double e = _model.Material?.E ?? 0;
            if (e < 1) return "—";
            return $"{_model.SigSp * 1000.0 * _model.GammaSp / e:F6}";
         }
      }

      public DiagrammType DiagrammType
      {
         get => _model.DiagrammType;
         set
         {
            _model.DiagrammType = value;
            _model.ResolveAndBuildDiagramms(App.CalcSettings.Sp63DescEtaMin, pool: App.db.Diagrams,
               rebarDifferentialDiagram: App.CalcSettings.RebarDifferentialDiagram, ekbEtaMin: App.CalcSettings.EkbDescEtaMin);
            OnPropertyChanged();
            Touch();
         }
      }

      public AreaCategory Category => _model.Category;

      public Contour? Hull => _model.Hull;

      public ObservableCollection<Contour> ProjectContours => App.Contours;

      public IReadOnlyList<PlotElement> PlotElements { get; private set; } = [];

      public FiberDisplayMode FiberDisplayMode
      {
         get => _fiberDisplayMode;
         set { _fiberDisplayMode = value; OnPropertyChanged(); RefreshPlot(); }
      }

      public int FiberDisplayModeIndex
      {
         get => (int)_fiberDisplayMode;
         set => FiberDisplayMode = (FiberDisplayMode)value;
      }

      public int FibersCount
         => _model.Fibers.Count(f => f.TypeFiber is FiberType.poly or FiberType.tri);

      public ICommand OpenMeshDialogCommand { get; }
      public ICommand ClearMeshCommand { get; }

      public ICommand SetHullFromPoolCommand { get; }
      public ICommand ClearHullCommand { get; }
      public ICommand AddHoleCommand { get; }
      public ICommand RemoveHoleCommand { get; }
      public ICommand SaveCommand { get; }
      public ICommand DeleteCommand { get; }
      public ICommand TranslateCommand { get; }
      public ICommand ShowPropertiesCommand { get; }

      public void RefreshPlot()
      {
         var elements = new List<PlotElement>();
         var hull = _model.Hull;
         var typeBrush = MatTypeToBrushConverter.GetBrush(_model.Material?.Type ?? MatType.None);
         var fillBrush = new SolidColorBrush(
            Color.FromArgb(120, typeBrush.Color.R, typeBrush.Color.G, typeBrush.Color.B));

         if (hull != null && hull.X.Count > 0)
            elements.Add(new PolygonElement
            {
               Xs = [.. hull.X], Ys = [.. hull.Y],
               Fill = fillBrush, Stroke = typeBrush, StrokeThickness = 1.5
            });

         var meshFibers = _model.Fibers
            .Where(f => f.TypeFiber is FiberType.poly or FiberType.tri)
            .ToArray();
         if (meshFibers.Length > 0)
         {
            var ps = App.PlotSettings;
            Brush centroidBrush;
            try { centroidBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(ps.CentroidColor)); }
            catch { centroidBrush = Brushes.RoyalBlue; }
            elements.Add(new FiberMeshElement
            {
               Fibers = meshFibers,
               ShowCentroids = (_fiberDisplayMode == FiberDisplayMode.Centroids),
               Fill = null,
               CentroidFill = centroidBrush,
               MarkerSize = ps.CentroidSize
            });
         }

         foreach (var hole in _model.Holes)
            if (hole.X.Count > 0)
               elements.Add(new PolygonElement
               {
                  Xs = [.. hole.X], Ys = [.. hole.Y],
                  Fill = Brushes.White, Stroke = Brushes.Gray, StrokeThickness = 1
               });

         CrossSectionPlotBuilder.AddRebarElements(elements, _model);
         CrossSectionPlotBuilder.AddStirrupElements(elements, _model);

         PlotElements = elements;
         OnPropertyChanged(nameof(PlotElements));
      }

      /// <summary>Контур пула, выбранный в списке (назначается областью только кнопкой «Из пула»).</summary>
      public Contour? SelectedPoolContour
      {
         get => _selectedPoolContour ?? _model.PoolContour;
         set { _selectedPoolContour = value; OnPropertyChanged(); }
      }
      Contour? _selectedPoolContour;

      void SetHullFromPool(Contour? contour)
      {
         if (contour == null) return;
         _model.Hull = contour;
         _model.PoolContour = contour;
         _model.PoolContourId = contour.Id;
         _model.SetWKT();
         OnPropertyChanged(nameof(Hull));
         OnPropertyChanged(nameof(SelectedPoolContour));
         RefreshPlot();
         Touch();
      }

      void ClearHull()
      {
         _model.Contours.RemoveAll(c => c.Type == ContourType.Hull);
         _model.PoolContour = null;
         _model.PoolContourId = null;
         _model.WKT = null;
         OnPropertyChanged(nameof(Hull));
         RefreshPlot();
         Touch();
      }

      void AddHole(Contour? contour)
      {
         if (contour == null) return;
         var hole = new Contour([.. contour.X], [.. contour.Y], contour.Tag) { Type = ContourType.Hole };
         _model.Contours.Add(hole);
         _model.SetWKT();
         RefreshPlot();
         Touch();
      }

      void RemoveHole(Contour? contour)
      {
         if (contour == null) return;
         var toRemove = _model.Contours
            .FirstOrDefault(c => c.Type == ContourType.Hole && c.Tag == contour.Tag);
         if (toRemove != null)
         {
            _model.Contours.Remove(toRemove);
            _model.SetWKT();
            RefreshPlot();
            Touch();
         }
      }

      /// <summary>Проверяет и записывает область через службу; ошибки — в журнал, правки остаются.</summary>
      public bool Save()
      {
         bool isNew = _model.Id == 0;
         var errors = CrossSectionAuthoring.SaveArea(App.db, _model, App.CalcSettings);
         if (errors.Count > 0)
         {
            foreach (var e in errors)
               App.LogService.Error(string.Format(Loc.S("AreaSaveErrorLog"), DisplayName, e.Message));
            return false;
         }
         App.RefreshMaterialAreaLiveCollections();
         App.MarkDirty(SaveCategory.CrossSections);
         MarkSaved();
         App.LogService.Info(string.Format(Loc.S(isNew ? "AreaSavedNewLog" : "AreaSavedLog"), _model.Tag));
         return true;
      }

      bool IUnsavedChanges.SaveChanges() => Save();

      void IUnsavedChanges.DiscardChanges() => Discard();

      void Delete() => App.DeleteArea(_model);

      void OpenMeshDialog()
      {
         // Сетка пишется в базу по Id области: новую сначала сохранить (с проверкой).
         if (_model.Id == 0 && !Save()) return;
         var dlg = new Views.MeshDialog(_model, App)
         {
            Owner = System.Windows.Application.Current.MainWindow
         };
         if (dlg.ShowDialog() == true)
         {
            OnPropertyChanged(nameof(FibersCount));
            RefreshPlot();
         }
      }

      void ClearMesh()
      {
         _model.Fibers.RemoveAll(f => f.TypeFiber is FiberType.poly or FiberType.tri);
         App.db.SaveMeshFibers(_model);
         OnPropertyChanged(nameof(FibersCount));
         RefreshPlot();
      }

      /// <summary>
      /// Сдвигает область: контур и отверстия — копии (контур пула, общий с другими областями, не трогается, связь
      /// с ним снимается), сетка волокон устаревает и удаляется. Записывается по «Сохранить».
      /// </summary>
      void Translate()
      {
         var hull = _model.Hull;
         if (hull == null) return;

         var dlg = new DoubleInputDialog(
            Loc.S("AreaTranslateTitle"),
            Loc.S("AreaTranslateDxMm"),
            Loc.S("AreaTranslateDyMm"));
         if (dlg.ShowDialog() != true) return;

         double dx = dlg.Value1 / 1000, dy = dlg.Value2 / 1000;
         if (dx == 0 && dy == 0) return;

         _model.Contours = _model.Contours.Select(c => new Contour(
               c.X.Select(x => x + dx).ToList(), c.Y.Select(y => y + dy).ToList(), c.Tag) { Type = c.Type })
            .ToList();
         _model.PoolContour = null;
         _model.PoolContourId = null;
         _model.SetWKT();
         foreach (var f in _model.Fibers.Where(f => f.TypeFiber == FiberType.point))
         {
            f.X += dx;
            f.Y += dy;
         }
         if (_model.Fibers.Any(f => f.TypeFiber is FiberType.poly or FiberType.tri))
         {
            ClearMesh();
            App.LogService.Info(Loc.S("AreaTranslateMeshCleared"));
         }

         OnPropertyChanged(nameof(Hull));
         OnPropertyChanged(nameof(SelectedPoolContour));
         RefreshPlot();
         Touch();
      }

      void ShowProperties()
      {
         var hull = _model.Hull;
         if (hull == null) return;
         var dlg = new ContourPropsWindow(hull, hull.Tag);
         dlg.ShowDialog();
      }
   }
}
