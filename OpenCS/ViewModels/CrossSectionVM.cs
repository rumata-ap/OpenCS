using CScore;
using OpenCS.Utilites;
using OpenCS.Views;
using OpenCS.Views.Helpers;

using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Windows.Input;

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

         AddFromPoolCommand           = new RelayCommand(o => AddFromPool(o as MaterialArea));
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

      public ICommand AddFromPoolCommand { get; }
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
         PlotElements = CrossSectionPlotBuilder.Build(_model).Elements;
         OnPropertyChanged(nameof(PlotElements));
      }

      void AddFromPool(MaterialArea? area)
      {
         if (area == null || _model.Areas.Contains(area)) return;
         _model.Areas.Add(area);
         var avm = new MaterialAreaVM(area, App);
         avm.PropertyChanged += OnAreaPropertyChanged;
         Areas.Add(avm);
         SelectedArea = avm;
         RefreshPlot();
         App.MarkDirty(SaveCategory.CrossSections);
      }

      void RemoveArea(MaterialAreaVM? avm)
      {
         if (avm == null) return;
         avm.PropertyChanged -= OnAreaPropertyChanged;
         _model.Areas.Remove(avm.Model);
         Areas.Remove(avm);
         if (SelectedArea == avm) SelectedArea = null;
         RefreshPlot();
         App.MarkDirty(SaveCategory.CrossSections);
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
         if (_model.Num == 0)
            _model.Num = App.CrossSections.Count > 0
               ? App.CrossSections.Max(s => s.Num) + 1 : 1;
         App.db.SaveCrossSection(_model);
         if (!App.CrossSections.Contains(_model))
         {
            App.CrossSections.Add(_model);
            App.RefreshSectionLiveCollections();
         }
         App.MarkDirty(SaveCategory.CrossSections);
      }
   }
}
