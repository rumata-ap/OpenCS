using CScore;
using OpenCS.Utilites;

using System.Collections.ObjectModel;
using System.Windows.Input;

namespace OpenCS.ViewModels
{
   /// <summary>ViewModel страницы набора усилий пластины (Kind = "shell"): строки черновика, пересчёт σ·h.</summary>
   public class ShellForceSetVM : ForceSetEditorVM
   {
      ShellLoadItemVM? _selectedItem;
      double _thicknessMm = 200;

      public ShellForceSetVM(ForceSet model, AppViewModel app) : base(model, app)
      {
         Items = [];
         LoadRows();
         AddItemCommand       = new RelayCommand(_ => AddItem());
         DeleteItemCommand    = new RelayCommand(_ => DeleteItem(), _ => _selectedItem != null);
         DuplicateItemCommand = new RelayCommand(_ => DuplicateItem(), _ => _selectedItem != null);
         ConvertStressToForceCommand = new RelayCommand(_ => ConvertStressToForce());
      }

      public ObservableCollection<ShellLoadItemVM> Items { get; }

      public ShellLoadItemVM? SelectedItem
      {
         get => _selectedItem;
         set { _selectedItem = value; OnPropertyChanged(); }
      }

      public ICommand AddItemCommand       { get; }
      public ICommand DeleteItemCommand    { get; }
      public ICommand DuplicateItemCommand { get; }
      public ICommand ConvertStressToForceCommand { get; }

      /// <summary>Толщина для кнопки «Напряжения → усилия», мм. Не сохраняется в проекте.</summary>
      public double ThicknessMm
      {
         get => _thicknessMm;
         set { _thicknessMm = value; OnPropertyChanged(); }
      }

      protected override void LoadRows()
      {
         Items.Clear();
         foreach (var i in _draft.ShellItems) Items.Add(new ShellLoadItemVM(i, RowsChanged));
         _selectedItem = null;
         OnPropertyChanged(nameof(SelectedItem));
      }

      protected override void ReplaceRows(List<LoadItem> bar, List<ShellLoadItem> shell) => _draft.ShellItems = shell;

      internal void AddItem()
      {
         var item = new ShellLoadItem { Label = $"{Items.Count + 1}", Num = NextRowNum(Items.Count) };
         _draft.ShellItems.Add(item);
         var vm = new ShellLoadItemVM(item, RowsChanged);
         Items.Add(vm);
         SelectedItem = vm;
         RowsChanged();
      }

      void DuplicateItem()
      {
         if (_selectedItem == null) return;
         var src = _selectedItem.Model;
         var item = new ShellLoadItem
         {
            Label = ForceSetRowLabels.Duplicate(src.Label, _draft.ShellItems.Select(i => i.Label)),
            Num = NextRowNum(Items.Count),
            Nx = src.Nx, Ny = src.Ny, Nxy = src.Nxy,
            Mx = src.Mx, My = src.My, Mxy = src.Mxy,
            Qx = src.Qx, Qy = src.Qy,
            SigmaX = src.SigmaX, SigmaY = src.SigmaY, TauXY = src.TauXY,
            SourceElementNum = src.SourceElementNum, SourceSectionNum = src.SourceSectionNum,
         };
         int idx = _draft.ShellItems.IndexOf(src);
         _draft.ShellItems.Insert(idx + 1, item);
         var vm = new ShellLoadItemVM(item, RowsChanged);
         Items.Insert(Items.IndexOf(_selectedItem) + 1, vm);
         SelectedItem = vm;
         RowsChanged();
      }

      /// <summary>Nx/Ny/Nxy = σ·h у строк с заданными σ; h ≤ 0 — сообщение в журнал.</summary>
      internal void ConvertStressToForce()
      {
         if (!double.IsFinite(_thicknessMm) || _thicknessMm <= 0)
         {
            App.LogService.Warning(Loc.S("ShellForceThicknessInvalid"));
            return;
         }
         double h = _thicknessMm / 1000.0;
         int changed = 0;
         foreach (var vm in Items)
         {
            if (!vm.HasSigma) continue;
            var (nx, ny, nxy) = vm.Model.ResolveN(h);
            vm.Nx = nx; vm.Ny = ny; vm.Nxy = nxy;
            changed++;
         }
         App.LogService.Info(string.Format(Loc.S("ShellForceConvertedLog"), changed, _thicknessMm));
      }

      internal void DeleteItem()
      {
         if (_selectedItem == null) return;
         _draft.ShellItems.Remove(_selectedItem.Model);
         Items.Remove(_selectedItem);
         SelectedItem = null;
         RowsChanged();
      }
   }
}
