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
   /// <summary>ViewModel для одной строки набора усилий.</summary>
   public class LoadItemVM : ViewModelBase
   {
      readonly LoadItem _model;
      readonly Action? _onChanged;

      public LoadItemVM(LoadItem model, Action? onChanged = null)
      {
         _model = model;
         _onChanged = onChanged;
      }

      void Touch() => _onChanged?.Invoke();

      public LoadItem Model => _model;

      public int Num => _model.Num;

      public string Label
      {
         get => _model.Label;
         set { _model.Label = value; Touch(); OnPropertyChanged(); }
      }

      public double N
      {
         get => _model.N;
         set { _model.N = value; Touch(); OnPropertyChanged(); }
      }

      public double Mx
      {
         get => _model.Mx;
         set { _model.Mx = value; Touch(); OnPropertyChanged(); }
      }

      public double My
      {
         get => _model.My;
         set { _model.My = value; Touch(); OnPropertyChanged(); }
      }

      public double Vx
      {
         get => _model.Vx;
         set { _model.Vx = value; Touch(); OnPropertyChanged(); }
      }

      public double Vy
      {
         get => _model.Vy;
         set { _model.Vy = value; Touch(); OnPropertyChanged(); }
      }

      public double T
      {
         get => _model.T;
         set { _model.T = value; Touch(); OnPropertyChanged(); }
      }
   }

   /// <summary>ViewModel для набора усилий стержня (Kind="bar").</summary>
   public class BarForceSetVM : ViewModelBase
   {
      readonly ForceSet _model;
      readonly Action _touchSet;
      LoadItemVM? _selectedItem;

      public BarForceSetVM(ForceSet model, AppViewModel app)
      {
         _model = model;
         App = app;
         _touchSet = () => App.TouchForceSet(_model);
         Items = new ObservableCollection<LoadItemVM>(
            model.Items.ConvertAll(i => new LoadItemVM(i, _touchSet)));

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
         set { _model.Tag = value; App.TouchForceSet(_model); OnPropertyChanged(); }
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
      public ICommand ExportCsvCommand { get; }
      public ICommand ImportCsvCommand { get; }

      void AddItem()
      {
         var item = new LoadItem { Label = $"{Items.Count + 1}" };
         _model.Items.Add(item);
         var vm = new LoadItemVM(item, _touchSet);
         Items.Add(vm);
         SelectedItem = vm;
         App.TouchForceSet(_model);
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

         var vm = new LoadItemVM(item, _touchSet);
         int vmIdx = Items.IndexOf(_selectedItem);
         if (vmIdx >= 0) Items.Insert(vmIdx + 1, vm);
         else Items.Add(vm);

         SelectedItem = vm;
         App.TouchForceSet(_model);
      }

      void DeleteItem()
      {
         if (_selectedItem == null) return;
         _model.Items.Remove(_selectedItem.Model);
         Items.Remove(_selectedItem);
         SelectedItem = null;
         App.TouchForceSet(_model);
      }

      void ExportCsv()
      {
         var path = App.FileDialogService.SaveFile(
            "CSV файлы (*.csv)|*.csv", ".csv", Loc.S("ExportCsvBtn"));
         if (path == null) return;

         var cfg = new CsvConfiguration(CultureInfo.InvariantCulture) { Delimiter = ";" };
         using var writer = new StreamWriter(path, false, System.Text.Encoding.UTF8);
         using var csv = new CsvWriter(writer, cfg);

         foreach (var col in new[] { "Label", "N", "Mx", "My", "Vx", "Vy", "T" })
            csv.WriteField(col);
         csv.NextRecord();

         foreach (var item in _model.Items)
         {
            csv.WriteField(item.Label);
            csv.WriteField(item.N);
            csv.WriteField(item.Mx);
            csv.WriteField(item.My);
            csv.WriteField(item.Vx);
            csv.WriteField(item.Vy);
            csv.WriteField(item.T);
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
               Delimiter        = ";",
               MissingFieldFound = null,
               HeaderValidated  = null,
            };

            var rows = new List<LoadItem>();
            using (var reader = new StreamReader(path, System.Text.Encoding.UTF8))
            using (var csv = new CsvReader(reader, cfg))
            {
               csv.Read();
               csv.ReadHeader();
               while (csv.Read())
               {
                  rows.Add(new LoadItem
                  {
                     Label = csv.GetField("Label") ?? "",
                     N  = GetDouble(csv, "N"),
                     Mx = GetDouble(csv, "Mx"),
                     My = GetDouble(csv, "My"),
                     Vx = GetDouble(csv, "Vx"),
                     Vy = GetDouble(csv, "Vy"),
                     T  = GetDouble(csv, "T"),
                  });
               }
            }

            _model.Items.Clear();
            Items.Clear();
            for (int i = 0; i < rows.Count; i++)
            {
               rows[i].Num = i + 1;
               _model.Items.Add(rows[i]);
               Items.Add(new LoadItemVM(rows[i], _touchSet));
            }
            App.TouchForceSet(_model);
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

      void OpenSP20Dialog()
      {
         var dlg = new Views.SP20Dialog(App.BarForceSets, App)
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
