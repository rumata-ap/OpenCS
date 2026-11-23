using CScore;
using OpenCS.Utilites;

using System.Collections.ObjectModel;
using System.Windows.Input;

namespace OpenCS.ViewModels
{
   /// <summary>ViewModel одной строки набора усилий стержня (строка черновика).</summary>
   public class LoadItemVM : ViewModelBase
   {
      readonly LoadItem _model;
      readonly Action? _onChanged;

      public LoadItemVM(LoadItem model, Action? onChanged = null)
      {
         _model = model;
         _onChanged = onChanged;
      }

      void Set(Action apply, [System.Runtime.CompilerServices.CallerMemberName] string? name = null)
      {
         apply();
         _onChanged?.Invoke();
         OnPropertyChanged(name);
      }

      public LoadItem Model => _model;

      public int Num => _model.Num;

      public string Label { get => _model.Label; set => Set(() => _model.Label = value ?? ""); }
      public double N  { get => _model.N;  set => Set(() => _model.N = value); }
      public double Mx { get => _model.Mx; set => Set(() => _model.Mx = value); }
      public double My { get => _model.My; set => Set(() => _model.My = value); }
      public double Vx { get => _model.Vx; set => Set(() => _model.Vx = value); }
      public double Vy { get => _model.Vy; set => Set(() => _model.Vy = value); }
      public double T  { get => _model.T;  set => Set(() => _model.T = value); }
   }

   /// <summary>ViewModel страницы набора усилий стержня (Kind = "bar"): строки черновика.</summary>
   public class BarForceSetVM : ForceSetEditorVM
   {
      LoadItemVM? _selectedItem;

      public BarForceSetVM(ForceSet model, AppViewModel app) : base(model, app)
      {
         Items = [];
         LoadRows();
         AddItemCommand       = new RelayCommand(_ => AddItem());
         DeleteItemCommand    = new RelayCommand(_ => DeleteItem(), _ => _selectedItem != null);
         DuplicateItemCommand = new RelayCommand(_ => DuplicateItem(), _ => _selectedItem != null);
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

      protected override void LoadRows()
      {
         Items.Clear();
         foreach (var i in _draft.Items) Items.Add(new LoadItemVM(i, RowsChanged));
         _selectedItem = null;
         OnPropertyChanged(nameof(SelectedItem));
      }

      protected override void ReplaceRows(List<LoadItem> bar, List<ShellLoadItem> shell) => _draft.Items = bar;

      internal void AddItem()
      {
         var item = new LoadItem { Label = $"{Items.Count + 1}", Num = NextRowNum(Items.Count) };
         _draft.Items.Add(item);
         var vm = new LoadItemVM(item, RowsChanged);
         Items.Add(vm);
         SelectedItem = vm;
         RowsChanged();
      }

      void DuplicateItem()
      {
         if (_selectedItem == null) return;
         var src = _selectedItem.Model;
         var item = new LoadItem
         {
            Label = ForceSetRowLabels.Duplicate(src.Label, _draft.Items.Select(i => i.Label)),
            Num = NextRowNum(Items.Count),
            N = src.N, Mx = src.Mx, My = src.My, Vx = src.Vx, Vy = src.Vy, T = src.T,
            SourceElementNum = src.SourceElementNum, SourceSectionNum = src.SourceSectionNum
         };
         int idx = _draft.Items.IndexOf(src);
         _draft.Items.Insert(idx + 1, item);
         var vm = new LoadItemVM(item, RowsChanged);
         Items.Insert(Items.IndexOf(_selectedItem) + 1, vm);
         SelectedItem = vm;
         RowsChanged();
      }

      internal void DeleteItem()
      {
         if (_selectedItem == null) return;
         _draft.Items.Remove(_selectedItem.Model);
         Items.Remove(_selectedItem);
         SelectedItem = null;
         RowsChanged();
      }
   }

   /// <summary>Метка копии строки (порт _make_duplicate_label из GreenSectionPy).</summary>
   public static class ForceSetRowLabels
   {
      /// <summary>Числовая метка — следующий номер; иначе — «метка (копия)».</summary>
      public static string Duplicate(string src, IEnumerable<string> labels)
      {
         if (int.TryParse(src, out int srcNum))
         {
            int maxNum = srcNum;
            foreach (var l in labels)
               if (int.TryParse(l, out int n) && n > maxNum) maxNum = n;
            return (maxNum + 1).ToString();
         }
         return string.Format(Loc.S("ForceSetCopyName"), src);
      }
   }
}
