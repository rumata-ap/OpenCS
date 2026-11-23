using CScore;
using OpenCS.Services;
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
   /// <summary>
   /// ViewModel страницы произвольного сечения: обозначение, состав из областей пула и правка выбранной области.
   /// В базу — только по «Сохранить» через <see cref="CrossSectionAuthoring"/>: состав и правленые области вместе;
   /// «*» и вопрос при уходе — по снимку обозначения, состава и областей.
   /// </summary>
   public class CrossSectionVM : ViewModelBase, IUnsavedChanges
   {
      readonly CrossSection _model;
      MaterialAreaVM? _selectedArea;
      string _savedTag;
      List<MaterialArea> _savedAreas;
      /// <summary>VM каждой области, побывавшей в составе за время правки (её снимок — для «*» и отката).</summary>
      readonly Dictionary<MaterialArea, MaterialAreaVM> _areaVMs = [];

      public CrossSectionVM(CrossSection model, AppViewModel app)
      {
         _model = model;
         App = app;
         Areas = new ObservableCollection<MaterialAreaVM>(model.Areas.Select(VmFor));
         _savedTag = model.Tag;
         _savedAreas = [.. model.Areas];

         AddFromPoolCommand           = new RelayCommand(o => AddFromPool(o as MaterialArea));
         SaveCommand                  = new RelayCommand(_ => Save());
         RemoveAreaFromSectionCommand = new RelayCommand(o => RemoveArea(o as MaterialAreaVM));
         OpenMeshForAreaCommand       = new RelayCommand(o => OpenMeshForArea(o as MaterialAreaVM));

         RefreshPlot();
      }

      MaterialAreaVM VmFor(MaterialArea area)
      {
         if (_areaVMs.TryGetValue(area, out var vm)) return vm;
         vm = new MaterialAreaVM(area, App);
         vm.PropertyChanged += OnAreaPropertyChanged;
         _areaVMs[area] = vm;
         return vm;
      }

      public AppViewModel App { get; }
      public CrossSection Model => _model;

      public string Tag
      {
         get => _model.Tag;
         set { _model.Tag = value; OnPropertyChanged(); Touch(); }
      }

      public ObservableCollection<MaterialAreaVM> Areas { get; }

      public MaterialAreaVM? SelectedArea
      {
         get => _selectedArea;
         set { _selectedArea = value; OnPropertyChanged(); }
      }

      /// <summary>Полные данные preview, включая единые границы всех элементов.</summary>
      public CrossSectionPlotData PlotData { get; private set; } =
         new([], -0.1, 0.1, -0.1, 0.1);

      public IReadOnlyList<PlotElement> PlotElements => PlotData.Elements;

      public ICommand AddFromPoolCommand { get; }
      public ICommand SaveCommand { get; }
      public ICommand RemoveAreaFromSectionCommand { get; }
      public ICommand OpenMeshForAreaCommand { get; }

      /// <summary>Есть правки обозначения, состава или областей, не записанные в проект.</summary>
      public bool IsDirty =>
         _model.Tag != _savedTag || !_model.Areas.SequenceEqual(_savedAreas) || _areaVMs.Values.Any(v => v.IsDirty);

      /// <summary>Обозначение для вопроса о сохранении.</summary>
      public string DisplayName => string.IsNullOrWhiteSpace(_model.Tag) ? Loc.S("NewCrossSection") : _model.Tag;

      void Touch() => OnPropertyChanged(nameof(IsDirty));

      void OnAreaPropertyChanged(object? sender, PropertyChangedEventArgs e)
      {
         if (e.PropertyName == nameof(MaterialAreaVM.PlotElements))
            RefreshPlot();
         if (e.PropertyName is nameof(MaterialAreaVM.IsDirty) or "" or null)
            Touch();
      }

      public void RefreshPlot()
      {
         PlotData = CrossSectionPlotBuilder.Build(_model);
         OnPropertyChanged(nameof(PlotData));
         OnPropertyChanged(nameof(PlotElements));
      }

      void AddFromPool(MaterialArea? area)
      {
         if (area == null || _model.Areas.Contains(area)) return;
         _model.Areas.Add(area);
         var avm = VmFor(area);
         Areas.Add(avm);
         SelectedArea = avm;
         RefreshPlot();
         Touch();
      }

      void RemoveArea(MaterialAreaVM? avm)
      {
         if (avm == null) return;
         _model.Areas.Remove(avm.Model);
         Areas.Remove(avm);
         if (SelectedArea == avm) SelectedArea = null;
         RefreshPlot();
         Touch();
      }

      void OpenMeshForArea(MaterialAreaVM? avm)
      {
         if (avm == null) return;
         // Сетка пишется по Id области; области состава уже в пуле. Правки самой области окно сетки не трогает.
         if (avm.Model.Id == 0) return;
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

      /// <summary>Проверяет и записывает сечение вместе с правлеными областями; ошибки — в журнал.</summary>
      public bool Save()
      {
         bool isNew = _model.Id == 0;
         var dirtyAreas = _areaVMs.Values.Where(v => v.IsDirty && _model.Areas.Contains(v.Model)).Select(v => v.Model).ToList();
         var errors = CrossSectionAuthoring.Save(App.db, _model, App.CalcSettings, dirtyAreas);
         if (errors.Count > 0)
         {
            foreach (var e in errors)
               App.LogService.Error(string.Format(Loc.S("SectionSaveErrorLog"), DisplayName, Describe(e)));
            return false;
         }
         foreach (var a in dirtyAreas) _areaVMs[a].MarkSaved();
         _savedTag = _model.Tag;
         _savedAreas = [.. _model.Areas];
         App.CirclesRenumber();
         App.RefreshMaterialAreaLiveCollections();
         App.RefreshSectionLiveCollections();
         App.MarkDirty(SaveCategory.CrossSections);
         Touch();
         App.LogService.Info(string.Format(Loc.S(isNew ? "SectionSavedNewLog" : "SectionSavedLog"), _model.Tag));
         return true;
      }

      /// <summary>Ошибка с обозначением области: «Низ: стержень 3 вне бетона…».</summary>
      string Describe(FieldError e)
      {
         if (e.Path.StartsWith("areas[") && int.TryParse(e.Path[6..e.Path.IndexOf(']')], out int i) && i < _model.Areas.Count)
            return $"{_model.Areas[i].Tag}: {e.Message}";
         return e.Message;
      }

      bool IUnsavedChanges.SaveChanges() => Save();

      /// <summary>Обозначение, состав и правленые области — к записанному состоянию.</summary>
      void IUnsavedChanges.DiscardChanges()
      {
         foreach (var v in _areaVMs.Values.Where(v => v.IsDirty)) v.Discard();
         _model.Tag = _savedTag;
         _model.Areas.Clear();
         _model.Areas.AddRange(_savedAreas);
         Areas.Clear();
         foreach (var a in _model.Areas) Areas.Add(VmFor(a));
         SelectedArea = null;
         OnPropertyChanged(nameof(Tag));
         RefreshPlot();
         Touch();
      }
   }
}
