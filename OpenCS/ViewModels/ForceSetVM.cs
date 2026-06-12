using CScore;
using OpenCS.Utilites;

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

      public string Label
      {
         get => _model.Label;
         set { _model.Label = value; OnPropertyChanged(); }
      }

      public double N
      {
         get => _model.N;
         set { _model.N = value; OnPropertyChanged(); }
      }

      public double Mx
      {
         get => _model.Mx;
         set { _model.Mx = value; OnPropertyChanged(); }
      }

      public double My
      {
         get => _model.My;
         set { _model.My = value; OnPropertyChanged(); }
      }

      public double Vx
      {
         get => _model.Vx;
         set { _model.Vx = value; OnPropertyChanged(); }
      }

      public double Vy
      {
         get => _model.Vy;
         set { _model.Vy = value; OnPropertyChanged(); }
      }

      public double T
      {
         get => _model.T;
         set { _model.T = value; OnPropertyChanged(); }
      }
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

         AddItemCommand       = new RelayCommand(_ => AddItem());
         DeleteItemCommand    = new RelayCommand(_ => DeleteItem());
         DuplicateItemCommand = new RelayCommand(_ => DuplicateItem());
         SaveCommand          = new RelayCommand(_ => Save());
         SP20Command          = new RelayCommand(_ => OpenSP20Dialog());
      }

      public AppViewModel App { get; }
      public ForceSet Model => _model;

      public string Tag
      {
         get => _model.Tag;
         set { _model.Tag = value; OnPropertyChanged(); }
      }

      public string Kind => _model.Kind;

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
      public ICommand SP20Command { get; }

      void AddItem()
      {
         var item = new LoadItem { Label = $"{Items.Count + 1}" };
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
         // Метка-дублирование по аналогии с GreenSectionPy
         string newLabel = MakeDuplicateLabel(src.Label, _model.Items);
         var item = new LoadItem
         {
            Label = newLabel,
            N = src.N, Mx = src.Mx, My = src.My,
            Vx = src.Vx, Vy = src.Vy, T = src.T
         };
         int idx = _model.Items.IndexOf(src);
         if (idx >= 0) _model.Items.Insert(idx + 1, item);
         else _model.Items.Add(item);

         var vm = new LoadItemVM(item);
         int vmIdx = Items.IndexOf(_selectedItem);
         if (vmIdx >= 0) Items.Insert(vmIdx + 1, vm);
         else Items.Add(vm);

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

      void OpenSP20Dialog()
      {
         var dlg = new Views.SP20Dialog(App.ForceSets, App)
         {
            Owner = System.Windows.Application.Current.MainWindow
         };
         dlg.ShowDialog();
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

      // Порт _make_duplicate_label из GreenSectionPy
      static string MakeDuplicateLabel(string src, System.Collections.Generic.List<LoadItem> items)
      {
         if (int.TryParse(src, out int srcNum))
         {
            int maxNum = srcNum;
            foreach (var it in items)
               if (int.TryParse(it.Label, out int n) && n > maxNum)
                  maxNum = n;
            return (maxNum + 1).ToString();
         }
         return src + " (копия)";
      }
   }
}
