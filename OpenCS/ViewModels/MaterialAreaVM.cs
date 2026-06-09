using CScore;
using OpenCS.Converters;
using OpenCS.Utilites;
using OpenCS.Views;

using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows.Input;
using System.Windows.Media;

namespace OpenCS.ViewModels
{
   /// <summary>ViewModel для MaterialArea.</summary>
   public class MaterialAreaVM : ViewModelBase
   {
      readonly MaterialArea _model;

      public MaterialAreaVM(MaterialArea model, AppViewModel app)
      {
         _model = model;
         App = app;
         RemoveAreaCommand       = new RelayCommand(_ => App.RemoveMaterialArea(this));
         SetHullFromPoolCommand  = new RelayCommand(o => SetHullFromPool(o as Contour));
         ClearHullCommand        = new RelayCommand(_ => ClearHull());
         AddHoleCommand          = new RelayCommand(o => AddHole(o as Contour));
         RemoveHoleCommand       = new RelayCommand(o => RemoveHole(o as Contour));
         SaveCommand             = new RelayCommand(_ => Save());
         DeleteCommand           = new RelayCommand(_ => Delete());
      }

      public AppViewModel App { get; }
      public MaterialArea Model => _model;

      public string Tag
      {
         get => _model.Tag;
         set { _model.Tag = value; OnPropertyChanged(); }
      }

      public Material? Material
      {
         get => _model.Material;
         set
         {
            _model.Material = value;
            _model.MaterialId = value?.Id ?? 0;
            _model.ResolveAndBuildDiagramms();
            OnPropertyChanged();
            OnPropertyChanged(nameof(MaterialType));
            RefreshPlot();
         }
      }

      public MatType MaterialType => _model.Material?.Type ?? MatType.None;

      public DiagrammType DiagrammType
      {
         get => _model.DiagrammType;
         set
         {
            _model.DiagrammType = value;
            _model.ResolveAndBuildDiagramms();
            OnPropertyChanged();
         }
      }

      public AreaCategory Category
      {
         get => _model.Category;
         set { _model.Category = value; OnPropertyChanged(); }
      }

      public int NX
      {
         get => _model.NX;
         set { _model.NX = value; OnPropertyChanged(); }
      }

      public int NY
      {
         get => _model.NY;
         set { _model.NY = value; OnPropertyChanged(); }
      }

      public Contour? Hull => _model.Hull;

      public ObservableCollection<Contour> ProjectContours => App.Contours;

      public IReadOnlyList<PlotElement> PlotElements { get; private set; } = [];

      public ICommand RemoveAreaCommand { get; }
      public ICommand SetHullFromPoolCommand { get; }
      public ICommand ClearHullCommand { get; }
      public ICommand AddHoleCommand { get; }
      public ICommand RemoveHoleCommand { get; }
      public ICommand SaveCommand { get; }
      public ICommand DeleteCommand { get; }

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

         foreach (var hole in _model.Holes)
            if (hole.X.Count > 0)
               elements.Add(new PolygonElement
               {
                  Xs = [.. hole.X], Ys = [.. hole.Y],
                  Fill = Brushes.White, Stroke = Brushes.Gray, StrokeThickness = 1
               });

         foreach (var f in _model.Fibers.Where(f => f.TypeFiber == FiberType.point))
            elements.Add(new CircleElement
            {
               X = f.X, Y = f.Y, Radius = f.Diameter / 2,
               Fill = Brushes.OrangeRed, Stroke = Brushes.DarkRed, StrokeThickness = 0.5
            });

         PlotElements = elements;
         OnPropertyChanged(nameof(PlotElements));
      }

      void SetHullFromPool(Contour? contour)
      {
         if (contour == null) return;
         _model.Hull = contour;
         _model.SetWKT();
         OnPropertyChanged(nameof(Hull));
         RefreshPlot();
      }

      void ClearHull()
      {
         _model.Contours.RemoveAll(c => c.Type == ContourType.Hull);
         _model.WKT = null;
         OnPropertyChanged(nameof(Hull));
         RefreshPlot();
      }

      void AddHole(Contour? contour)
      {
         if (contour == null) return;
         var hole = new Contour(contour.X, contour.Y, contour.Tag) { Type = ContourType.Hole };
         _model.Contours.Add(hole);
         _model.SetWKT();
         RefreshPlot();
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
         }
      }

      void Save()
      {
         int newNum = App.MaterialAreas.Count > 0
            ? App.MaterialAreas.Max(a => a.Num) + 1 : 1;
         if (_model.Num == 0) _model.Num = newNum;
         App.db.SaveMaterialArea(_model);
         App.RefreshMaterialAreaLiveCollections();
      }

      void Delete()
      {
         App.db.DeleteMaterialArea(_model);
         App.RefreshMaterialAreaLiveCollections();
         App.CurrentPage = null;
      }
   }
}
