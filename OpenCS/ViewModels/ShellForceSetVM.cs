using CScore;
using CsvHelper;
using CsvHelper.Configuration;
using OpenCS.Utilites;

using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Windows.Input;

namespace OpenCS.ViewModels
{
   /// <summary>ViewModel для набора усилий пластины (Kind="shell").</summary>
   public class ShellForceSetVM : ViewModelBase
   {
      readonly ForceSet _model;
      ShellLoadItemVM? _selectedItem;

      public ShellForceSetVM(ForceSet model, AppViewModel app)
      {
         _model = model;
         App    = app;
         Items  = new ObservableCollection<ShellLoadItemVM>(
            model.ShellItems.ConvertAll(i => new ShellLoadItemVM(i)));

         AddItemCommand       = new RelayCommand(_ => AddItem());
         DeleteItemCommand    = new RelayCommand(_ => DeleteItem());
         DuplicateItemCommand = new RelayCommand(_ => DuplicateItem());
         SaveCommand          = new RelayCommand(_ => Save());
         SP20Command          = new RelayCommand(_ => OpenSP20Dialog());
         ExportCsvCommand     = new RelayCommand(_ => ExportCsv());
         ImportCsvCommand     = new RelayCommand(_ => ImportCsv());
      }

      public AppViewModel App { get; }
      public ForceSet Model => _model;

      public string Tag
      {
         get => _model.Tag;
         set { _model.Tag = value; OnPropertyChanged(); }
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
      public ICommand SaveCommand          { get; }
      public ICommand SP20Command          { get; }
      public ICommand ExportCsvCommand     { get; }
      public ICommand ImportCsvCommand     { get; }

      void AddItem()
      {
         var item = new ShellLoadItem { Label = $"{Items.Count + 1}" };
         _model.ShellItems.Add(item);
         var vm = new ShellLoadItemVM(item);
         Items.Add(vm);
         SelectedItem = vm;
         App.IsDirty = true;
      }

      void DuplicateItem()
      {
         if (_selectedItem == null) return;
         var src      = _selectedItem.Model;
         string newLabel = MakeDuplicateLabel(src.Label, _model.ShellItems);
         var item = new ShellLoadItem
         {
            Label = newLabel,
            Nx = src.Nx, Ny = src.Ny, Nxy = src.Nxy,
            Mx = src.Mx, My = src.My, Mxy = src.Mxy,
            Qx = src.Qx, Qy = src.Qy
         };
         int idx = _model.ShellItems.IndexOf(src);
         if (idx >= 0) _model.ShellItems.Insert(idx + 1, item);
         else _model.ShellItems.Add(item);

         var vm = new ShellLoadItemVM(item);
         int vmIdx = Items.IndexOf(_selectedItem);
         if (vmIdx >= 0) Items.Insert(vmIdx + 1, vm);
         else Items.Add(vm);

         SelectedItem = vm;
         App.IsDirty = true;
      }

      void DeleteItem()
      {
         if (_selectedItem == null) return;
         _model.ShellItems.Remove(_selectedItem.Model);
         Items.Remove(_selectedItem);
         SelectedItem = null;
         App.IsDirty = true;
      }

      void OpenSP20Dialog()
      {
         var dlg = new Views.SP20Dialog(App.ShellForceSets, App)
         {
            Owner = System.Windows.Application.Current.MainWindow
         };
         dlg.ShowDialog();
      }

      void ExportCsv()
      {
         var path = App.FileDialogService.SaveFile(
            "CSV файлы (*.csv)|*.csv", ".csv", Loc.S("ExportCsvBtn"));
         if (path == null) return;

         var cfg = new CsvConfiguration(CultureInfo.InvariantCulture) { Delimiter = ";" };
         using var writer = new StreamWriter(path, false, System.Text.Encoding.UTF8);
         using var csv    = new CsvWriter(writer, cfg);

         foreach (var col in new[] { "Label", "Nx", "Ny", "Nxy", "Mx", "My", "Mxy", "Qx", "Qy" })
            csv.WriteField(col);
         csv.NextRecord();

         foreach (var item in _model.ShellItems)
         {
            csv.WriteField(item.Label);
            csv.WriteField(item.Nx);  csv.WriteField(item.Ny);  csv.WriteField(item.Nxy);
            csv.WriteField(item.Mx);  csv.WriteField(item.My);  csv.WriteField(item.Mxy);
            csv.WriteField(item.Qx);  csv.WriteField(item.Qy);
            csv.NextRecord();
         }
      }

      void ImportCsv()
      {
         var path = App.FileDialogService.OpenFile(
            "CSV файлы (*.csv)|*.csv", Loc.S("ImportCsvBtn"));
         if (path == null) return;

         try
         {
            var cfg = new CsvConfiguration(CultureInfo.InvariantCulture)
            {
               Delimiter         = ";",
               MissingFieldFound = null,
               HeaderValidated   = null,
            };

            var rows = new List<ShellLoadItem>();
            using (var reader = new StreamReader(path, System.Text.Encoding.UTF8))
            using (var csv    = new CsvReader(reader, cfg))
            {
               csv.Read(); csv.ReadHeader();
               while (csv.Read())
               {
                  rows.Add(new ShellLoadItem
                  {
                     Label = csv.GetField("Label") ?? "",
                     Nx  = GetDouble(csv, "Nx"),  Ny  = GetDouble(csv, "Ny"),  Nxy = GetDouble(csv, "Nxy"),
                     Mx  = GetDouble(csv, "Mx"),  My  = GetDouble(csv, "My"),  Mxy = GetDouble(csv, "Mxy"),
                     Qx  = GetDouble(csv, "Qx"),  Qy  = GetDouble(csv, "Qy"),
                  });
               }
            }

            _model.ShellItems.Clear();
            Items.Clear();
            for (int i = 0; i < rows.Count; i++)
            {
               rows[i].Num = i + 1;
               _model.ShellItems.Add(rows[i]);
               Items.Add(new ShellLoadItemVM(rows[i]));
            }
            App.IsDirty = true;
         }
         catch (System.Exception ex)
         {
            System.Windows.MessageBox.Show(
               $"Ошибка импорта CSV:\n{ex.Message}",
               Loc.S("ImportCsvBtn"),
               System.Windows.MessageBoxButton.OK,
               System.Windows.MessageBoxImage.Error);
         }
      }

      static double GetDouble(CsvReader csv, string field)
         => csv.TryGetField<double>(field, out var v) ? v : 0.0;

      public void Save()
      {
         if (_model.Num == 0)
            _model.Num = App.ShellForceSets.Count > 0
               ? App.ShellForceSets.Max(s => s.Num) + 1 : 1;
         App.db.SaveForceSet(_model);
         if (!App.ForceSets.Contains(_model))
            App.ForceSets.Add(_model);  // CollectionChanged в AppViewModel добавит в ShellForceSets
         App.IsDirty = true;
      }

      static string MakeDuplicateLabel(string src, System.Collections.Generic.List<ShellLoadItem> items)
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
