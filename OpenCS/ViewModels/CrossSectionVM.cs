using CScore;
using OpenCS.Converters;
using OpenCS.Utilites;
using OpenCS.Views;

using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Windows.Input;
using System.Windows.Media;

namespace OpenCS.ViewModels
{
   /// <summary>ViewModel для CrossSection.</summary>
   public class CrossSectionVM : ViewModelBase
   {
      readonly CrossSection _model;
      MaterialAreaVM? _selectedArea;

      public CrossSectionVM(CrossSection model, AppViewModel app)
      {
         _model = model;
         App = app;
         Areas = new ObservableCollection<MaterialAreaVM>(
            model.Areas.Select(a => new MaterialAreaVM(a, app)));
         foreach (var avm in Areas)
            avm.PropertyChanged += OnAreaPropertyChanged;

         AddConcreteAreaCommand       = new RelayCommand(_ => AddArea(MatType.Concrete));
         AddRebarAreaCommand          = new RelayCommand(_ => AddArea(MatType.ReSteelF));
         AddSteelAreaCommand          = new RelayCommand(_ => AddArea(MatType.Steel));
         SaveCommand                  = new RelayCommand(_ => Save());
         RemoveAreaFromSectionCommand = new RelayCommand(o => RemoveArea(o as MaterialAreaVM));
         OpenMeshForAreaCommand       = new RelayCommand(o => OpenMeshForArea(o as MaterialAreaVM));

         RefreshPlot();
      }

      public AppViewModel App { get; }
      public CrossSection Model => _model;

      public string Tag
      {
         get => _model.Tag;
         set { _model.Tag = value; OnPropertyChanged(); }
      }

      public ObservableCollection<MaterialAreaVM> Areas { get; }

      public MaterialAreaVM? SelectedArea
      {
         get => _selectedArea;
         set { _selectedArea = value; OnPropertyChanged(); }
      }

      public IReadOnlyList<PlotElement> PlotElements { get; private set; } = [];

      public ICommand AddConcreteAreaCommand { get; }
      public ICommand AddRebarAreaCommand { get; }
      public ICommand AddSteelAreaCommand { get; }
      public ICommand SaveCommand { get; }
      public ICommand RemoveAreaFromSectionCommand { get; }
      public ICommand OpenMeshForAreaCommand { get; }

      void OnAreaPropertyChanged(object? sender, PropertyChangedEventArgs e)
      {
         if (e.PropertyName == nameof(MaterialAreaVM.PlotElements))
            RefreshPlot();
      }

      public void RefreshPlot()
      {
         var elements = new List<PlotElement>();
         foreach (var avm in Areas)
            AddAreaElements(elements, avm.Model);
         PlotElements = elements;
         OnPropertyChanged(nameof(PlotElements));
      }

      internal static void AddAreaElements(List<PlotElement> elements, MaterialArea area)
      {
         var hull = area.Hull;
         var brush = MatTypeToBrushConverter.GetBrush(area.Material?.Type ?? MatType.None);
         var fill  = new SolidColorBrush(Color.FromArgb(120, brush.Color.R, brush.Color.G, brush.Color.B));
         if (hull != null && hull.X.Count > 0)
            elements.Add(new PolygonElement
            {
               Xs = [.. hull.X], Ys = [.. hull.Y],
               Fill = fill, Stroke = brush, StrokeThickness = 1.5
            });
         foreach (var hole in area.Holes)
            if (hole.X.Count > 0)
               elements.Add(new PolygonElement
               {
                  Xs = [.. hole.X], Ys = [.. hole.Y],
                  Fill = Brushes.White, Stroke = Brushes.Gray, StrokeThickness = 1
               });
         foreach (var f in area.Fibers.Where(f => f.TypeFiber == FiberType.point))
            elements.Add(new CircleElement
            {
               X = f.X, Y = f.Y, Radius = f.Diameter / 2,
               Fill = Brushes.OrangeRed, Stroke = Brushes.DarkRed, StrokeThickness = 0.5
            });
      }

      void AddArea(MatType type)
      {
         var area = new MaterialArea { Tag = $"Область {Areas.Count + 1}" };
         _model.Areas.Add(area);
         var avm = new MaterialAreaVM(area, App);
         avm.PropertyChanged += OnAreaPropertyChanged;
         Areas.Add(avm);
         SelectedArea = avm;
         App.IsDirty = true;
      }

      void RemoveArea(MaterialAreaVM? avm)
      {
         if (avm == null) return;
         avm.PropertyChanged -= OnAreaPropertyChanged;
         _model.Areas.Remove(avm.Model);
         Areas.Remove(avm);
         if (SelectedArea == avm) SelectedArea = null;
         RefreshPlot();
         App.IsDirty = true;
      }

      void OpenMeshForArea(MaterialAreaVM? avm)
      {
         if (avm == null) return;
         if (_model.Id == 0) Save();
         var dlg = new MeshDialog(avm.Model, App)
         {
            Owner = System.Windows.Application.Current.MainWindow
         };
         if (dlg.ShowDialog() == true)
         {
            avm.RefreshPlot();
            RefreshPlot();
         }
      }

      public void Save()
      {
         for (int i = 0; i < _model.Areas.Count; i++)
            _model.Areas[i].Num = i + 1;
         if (_model.Num == 0)
            _model.Num = App.CrossSections.Count > 0
               ? App.CrossSections.Max(s => s.Num) + 1 : 1;
         App.db.SaveCrossSection(_model);
         if (!App.CrossSections.Contains(_model))
         {
            App.CrossSections.Add(_model);
            App.RefreshSectionLiveCollections();
         }
         App.IsDirty = true;
      }
   }
}
