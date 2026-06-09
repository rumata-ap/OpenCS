using CScore;
using OpenCS.Utilites;
using OpenCS.Views;

using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;

namespace OpenCS.ViewModels
{
   /// <summary>ViewModel диалога настройки и генерации сетки фибр MaterialArea.</summary>
   public class MeshDialogVM : ViewModelBase
   {
      readonly MaterialArea _area;
      readonly List<Fiber> _backup;
      readonly Window _window;

      MeshMethod _meshMethod;
      int _nx, _ny;
      double _maxArea, _minAngle;
      IReadOnlyList<PlotElement> _plotElements = [];
      int _fibersCount;

      public MeshDialogVM(MaterialArea area, AppViewModel app, Window window)
      {
         _area   = area;
         App     = app;
         _window = window;

         // Snapshot текущих волокон — восстанавливается при отмене
         _backup = area.Fibers.ToList();

         // Начальные значения из модели
         _meshMethod = area.MeshMethod;
         _nx         = area.NX;
         _ny         = area.NY;
         _maxArea    = area.MeshMaxArea;
         _minAngle   = area.MeshMinAngle;
         _fibersCount = area.Fibers.Count(f => f.TypeFiber is FiberType.poly or FiberType.tri);

         GenerateCommand = new RelayCommand(_ => Generate());
         ApplyCommand    = new RelayCommand(_ => Apply());
         CancelCommand   = new RelayCommand(_ => Cancel());

         RefreshPreview();
      }

      public AppViewModel App { get; }

      public MeshMethod MeshMethod
      {
         get => _meshMethod;
         set
         {
            _meshMethod = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(IsGrid));
            OnPropertyChanged(nameof(IsTriangulation));
            OnPropertyChanged(nameof(MeshMethodIndex));
         }
      }

      /// <summary>Индекс выбранного метода для ComboBox (0=Grid, 1=Ruppert, 2=AdvancingFront).</summary>
      public int MeshMethodIndex
      {
         get => (int)_meshMethod;
         set => MeshMethod = (MeshMethod)value;
      }

      public bool IsGrid          => _meshMethod == MeshMethod.Grid;
      public bool IsTriangulation => _meshMethod != MeshMethod.Grid;

      public int NX
      {
         get => _nx;
         set { _nx = value; OnPropertyChanged(); }
      }

      public int NY
      {
         get => _ny;
         set { _ny = value; OnPropertyChanged(); }
      }

      public double MaxArea
      {
         get => _maxArea;
         set { _maxArea = value; OnPropertyChanged(); }
      }

      public double MinAngle
      {
         get => _minAngle;
         set { _minAngle = value; OnPropertyChanged(); }
      }

      public IReadOnlyList<PlotElement> PlotElements
      {
         get => _plotElements;
         private set { _plotElements = value; OnPropertyChanged(); }
      }

      public int FibersCount
      {
         get => _fibersCount;
         private set { _fibersCount = value; OnPropertyChanged(); }
      }

      public ICommand GenerateCommand { get; }
      public ICommand ApplyCommand    { get; }
      public ICommand CancelCommand   { get; }

      void Generate()
      {
         switch (_meshMethod)
         {
            case MeshMethod.Grid:
               _area.SliceXY(_nx, _ny);
               break;
            case MeshMethod.Ruppert:
               _area.Triangulate(_maxArea, _minAngle, MeshMethod.Ruppert);
               break;
            case MeshMethod.AdvancingFront:
               _area.Triangulate(_maxArea, _minAngle, MeshMethod.AdvancingFront);
               break;
         }
         FibersCount = _area.Fibers.Count(f => f.TypeFiber is FiberType.poly or FiberType.tri);
         RefreshPreview();
      }

      void Apply()
      {
         _area.MeshMethod   = _meshMethod;
         _area.MeshMaxArea  = _maxArea;
         _area.MeshMinAngle = _minAngle;
         _area.NX           = _nx;
         _area.NY           = _ny;
         App.db.SaveMeshFibers(_area);
         _window.DialogResult = true;
      }

      void Cancel()
      {
         _area.Fibers.Clear();
         _area.Fibers.AddRange(_backup);
         _window.DialogResult = false;
      }

      void RefreshPreview()
      {
         var elements = new List<PlotElement>();
         var hull = _area.Hull;

         if (hull != null && hull.X.Count > 0)
            elements.Add(new PolygonElement
            {
               Xs = [.. hull.X], Ys = [.. hull.Y],
               Fill = new SolidColorBrush(System.Windows.Media.Color.FromArgb(60, 100, 149, 237)),
               Stroke = Brushes.SteelBlue, StrokeThickness = 1.5
            });

         foreach (var hole in _area.Holes)
            if (hole.X.Count > 0)
               elements.Add(new PolygonElement
               {
                  Xs = [.. hole.X], Ys = [.. hole.Y],
                  Fill = Brushes.White, Stroke = Brushes.Gray, StrokeThickness = 1
               });

         var meshFibers = _area.Fibers
            .Where(f => f.TypeFiber is FiberType.poly or FiberType.tri)
            .ToArray();
         if (meshFibers.Length > 0)
            elements.Add(new FiberMeshElement
            {
               Fibers = meshFibers,
               ShowCentroids = false
            });

         PlotElements = elements;
      }
   }
}
