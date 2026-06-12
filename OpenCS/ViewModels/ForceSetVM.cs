using CScore;
using OpenCS.Utilites;

using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Windows.Input;

namespace OpenCS.ViewModels
{
   /// <summary>ViewModel для одной строки набора усилий.</summary>
   public class LoadItemVM : ViewModelBase
   {
      readonly LoadItem _model;

      public LoadItemVM(LoadItem model)
      {
         _model = model;
      }

      public LoadItem Model => _model;

      public int Num => _model.Num;

      public string Tag
      {
         get => _model.Tag;
         set { _model.Tag = value; OnPropertyChanged(); }
      }

      public double N
      {
         get => _model.N;
         set { _model.N = value; OnPropertyChanged(); }
      }

      public double My
      {
         get => _model.My;
         set { _model.My = value; OnPropertyChanged(); }
      }

      public double Mz
      {
         get => _model.Mz;
         set { _model.Mz = value; OnPropertyChanged(); }
      }

      public CalcType CalcType
      {
         get => _model.CalcType;
         set { _model.CalcType = value; OnPropertyChanged(); }
      }

      public static IEnumerable<CalcType> CalcTypeValues { get; }
         = (CalcType[])System.Enum.GetValues(typeof(CalcType));
   }

   /// <summary>ViewModel для ForceSet.</summary>
   public class ForceSetVM : ViewModelBase
   {
      readonly ForceSet _model;
      LoadItemVM? _selectedItem;

      public ForceSetVM(ForceSet model, AppViewModel app)
      {
         _model = model;
         App = app;
         Items = new ObservableCollection<LoadItemVM>(
            model.Items.ConvertAll(i => new LoadItemVM(i)));

         AddItemCommand    = new RelayCommand(_ => AddItem());
         DeleteItemCommand = new RelayCommand(_ => DeleteItem());
         DuplicateItemCommand = new RelayCommand(_ => DuplicateItem());
         SaveCommand       = new RelayCommand(_ => Save());
      }

      public AppViewModel App { get; }
      public ForceSet Model => _model;

      public string Tag
      {
         get => _model.Tag;
         set { _model.Tag = value; OnPropertyChanged(); }
      }

      public ObservableCollection<LoadItemVM> Items { get; }

      public LoadItemVM? SelectedItem
      {
         get => _selectedItem;
         set { _selectedItem = value; OnPropertyChanged(); }
      }

      public ICommand AddItemCommand { get; }
      public ICommand DeleteItemCommand { get; }
      public ICommand DuplicateItemCommand { get; }
      public ICommand SaveCommand { get; }

      void AddItem()
      {
         var item = new LoadItem { Tag = $"К{Items.Count + 1}" };
         _model.Items.Add(item);
         var vm = new LoadItemVM(item);
         Items.Add(vm);
         SelectedItem = vm;
         App.IsDirty = true;
      }

      void DuplicateItem()
      {
         if (_selectedItem == null) return;
         var src = _selectedItem.Model;
         var item = new LoadItem
         {
            Tag      = src.Tag + "'",
            N        = src.N,
            My       = src.My,
            Mz       = src.Mz,
            CalcType = src.CalcType
         };
         _model.Items.Add(item);
         var vm = new LoadItemVM(item);
         Items.Add(vm);
         SelectedItem = vm;
         App.IsDirty = true;
      }

      void DeleteItem()
      {
         if (_selectedItem == null) return;
         _model.Items.Remove(_selectedItem.Model);
         Items.Remove(_selectedItem);
         SelectedItem = null;
         App.IsDirty = true;
      }

      public void Save()
      {
         if (_model.Num == 0)
            _model.Num = App.ForceSets.Count > 0
               ? App.ForceSets.Max(s => s.Num) + 1 : 1;
         App.db.SaveForceSet(_model);
         if (!App.ForceSets.Contains(_model))
            App.ForceSets.Add(_model);
         App.IsDirty = true;
      }
   }
}
