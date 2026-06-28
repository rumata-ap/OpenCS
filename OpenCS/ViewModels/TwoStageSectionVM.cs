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
   /// <summary>ViewModel для TwoStageSection.</summary>
   public class TwoStageSectionVM : ViewModelBase
   {
      readonly TwoStageSection _model;
      MaterialAreaVM? _selectedArea;

      public TwoStageSectionVM(TwoStageSection model, AppViewModel app)
      {
         _model = model;
         App = app;

         Stage1Areas = new ObservableCollection<MaterialAreaVM>(
            model.Stage1.Areas.Select(a => new MaterialAreaVM(a, app)));
         Stage2Areas = new ObservableCollection<MaterialAreaVM>(
            model.Areas.Select(a => new MaterialAreaVM(a, app)));

         foreach (var avm in Stage1Areas.Concat(Stage2Areas))
            avm.PropertyChanged += OnAreaPropertyChanged;

         AddToStage1Command           = new RelayCommand(o => AddToStage(Stage1Areas, _model.Stage1, o as MaterialArea));
         AddToStage2Command           = new RelayCommand(o => AddToStage(Stage2Areas, _model,        o as MaterialArea));
         RemoveAreaFromSectionCommand = new RelayCommand(o => RemoveArea(o as MaterialAreaVM));
         OpenMeshForAreaCommand       = new RelayCommand(o => OpenMeshForArea(o as MaterialAreaVM));
         SaveCommand                  = new RelayCommand(_ => Save());

         RefreshPlot();
      }

      public AppViewModel App { get; }
      public TwoStageSection Model => _model;

      public string Tag
      {
         get => _model.Tag;
         set { _model.Tag = value; OnPropertyChanged(); }
      }

      public ObservableCollection<MaterialAreaVM> Stage1Areas { get; }
      public ObservableCollection<MaterialAreaVM> Stage2Areas { get; }

      public MaterialAreaVM? SelectedArea
      {
         get => _selectedArea;
         set { _selectedArea = value; OnPropertyChanged(); }
      }

      public IReadOnlyList<PlotElement> PlotElements { get; private set; } = [];

      public ICommand AddToStage1Command { get; }
      public ICommand AddToStage2Command { get; }
      public ICommand RemoveAreaFromSectionCommand { get; }
      public ICommand OpenMeshForAreaCommand { get; }
      public ICommand SaveCommand { get; }

      void OnAreaPropertyChanged(object? sender, PropertyChangedEventArgs e)
      {
         if (e.PropertyName == nameof(MaterialAreaVM.PlotElements))
            RefreshPlot();
      }

      public void RefreshPlot()
      {
         var elements = new List<PlotElement>();
         foreach (var avm in Stage1Areas.Concat(Stage2Areas))
            CrossSectionVM.AddAreaElements(elements, avm.Model);
         PlotElements = elements;
         OnPropertyChanged(nameof(PlotElements));
      }

      void AddToStage(ObservableCollection<MaterialAreaVM> collection, CrossSection section, MaterialArea? area)
      {
         if (area == null || section.Areas.Contains(area)) return;
         section.Areas.Add(area);
         var avm = new MaterialAreaVM(area, App);
         avm.PropertyChanged += OnAreaPropertyChanged;
         collection.Add(avm);
         SelectedArea = avm;
         RefreshPlot();
         App.MarkDirty(SaveCategory.CrossSections);
      }

      void RemoveArea(MaterialAreaVM? avm)
      {
         if (avm == null) return;
         avm.PropertyChanged -= OnAreaPropertyChanged;

         bool inStage1 = Stage1Areas.Contains(avm);
         if (inStage1)
         {
            _model.Stage1.Areas.Remove(avm.Model);
            Stage1Areas.Remove(avm);
         }
         else
         {
            _model.Areas.Remove(avm.Model);
            Stage2Areas.Remove(avm);
         }

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
         if (_model.Stage1.Tag == "")
            _model.Stage1.Tag = $"{_model.Tag} (Этап 1)";

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
