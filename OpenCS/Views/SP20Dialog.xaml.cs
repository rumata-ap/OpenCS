using CScore;
using CScore.Combinations;
using OpenCS.Utilites;
using OpenCS.ViewModels;

using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using System.Windows.Input;

namespace OpenCS.Views
{
   public partial class SP20Dialog : Window
   {
      public SP20Dialog(IEnumerable<ForceSet> allSets, AppViewModel app)
      {
         InitializeComponent();
         DataContext = new SP20DialogVM(allSets, app, this);
      }
   }
}

namespace OpenCS.ViewModels
{
   public class ForceSetSelectionVM : ViewModelBase
   {
      bool _isSelected;
      public ForceSet ForceSet { get; }

      public ForceSetSelectionVM(ForceSet fs) { ForceSet = fs; }

      public bool IsSelected
      {
         get => _isSelected;
         set { _isSelected = value; OnPropertyChanged(); }
      }
   }

   /// <summary>Вид сочетания в UI-выборе.</summary>
   public enum SP20ModeChoice { Fundamental, Accidental, Both }

   public class SP20ModeItem
   {
      public SP20ModeChoice Mode { get; init; }
      public string Label       { get; init; } = "";
      public override string ToString() => Label;
   }

   public class SP20DialogVM : ViewModelBase
   {
      readonly AppViewModel _app;
      readonly Window _window;
      bool _makeEnvelope = true;
      bool _makeCases = true;
      SP20ModeItem _selectedMode;
      string _resultName = "";

      public SP20DialogVM(IEnumerable<ForceSet> allSets, AppViewModel app, Window window)
      {
         _app    = app;
         _window = window;

         Items = new ObservableCollection<ForceSetSelectionVM>(
            allSets.Select(fs => new ForceSetSelectionVM(fs)));
         foreach (var it in Items)
            it.PropertyChanged += (_, _) => RefreshPreview();

         ModeOptions =
         [
            new SP20ModeItem { Mode = SP20ModeChoice.Fundamental, Label = Loc.S("SP20ModeFundamental") },
            new SP20ModeItem { Mode = SP20ModeChoice.Accidental,  Label = Loc.S("SP20ModeAccidental")  },
            new SP20ModeItem { Mode = SP20ModeChoice.Both,        Label = Loc.S("SP20ModeBoth")        },
         ];
         _selectedMode = ModeOptions[2]; // Both by default

         GenerateCommand = new RelayCommand(_ => Generate());
         RefreshPreview();
      }

      public ObservableCollection<ForceSetSelectionVM> Items { get; }
      public List<SP20ModeItem> ModeOptions { get; }

      public SP20ModeItem SelectedMode
      {
         get => _selectedMode;
         set { _selectedMode = value; OnPropertyChanged(); RefreshPreview(); }
      }

      public bool MakeEnvelope
      {
         get => _makeEnvelope;
         set { _makeEnvelope = value; OnPropertyChanged(); }
      }

      public bool MakeCases
      {
         get => _makeCases;
         set { _makeCases = value; OnPropertyChanged(); }
      }

      public string ResultName
      {
         get => _resultName;
         set { _resultName = value; OnPropertyChanged(); }
      }

      string _preview = "";
      public string Preview
      {
         get => _preview;
         private set { _preview = value; OnPropertyChanged(); }
      }

      public ICommand GenerateCommand { get; }

      List<ForceSet> SelectedSets =>
         Items.Where(i => i.IsSelected).Select(i => i.ForceSet).ToList();

      void RefreshPreview()
      {
         var sets = SelectedSets;
         if (sets.Count == 0)
         {
            Preview = "Не выбрано ни одного набора.";
            return;
         }

         try
         {
            var (loadings, warnings) = SP20Combinations.ForceSetsToLoadings(sets, _app.CalcSettings.ToSp20GammaDefaults());
            int perm = loadings.Count(l => l.LoadType == NormLoadType.Permanent);
            int lt   = loadings.Count(l => l.LoadType == NormLoadType.LongTerm);
            int st   = loadings.Count(l => l.LoadType == NormLoadType.ShortTerm);
            int acc  = loadings.Count(l => l.LoadType == NormLoadType.Accidental);

            var sb = new System.Text.StringBuilder();
            sb.AppendLine("Распознано загружений:");
            sb.AppendLine($"  постоянная:       {perm}");
            sb.AppendLine($"  длительная:       {lt}");
            sb.AppendLine($"  кратковременная:  {st}");
            sb.AppendLine($"  особая:           {acc}");
            sb.AppendLine();
            sb.AppendLine($"Выбрано наборов: {sets.Count}");
            sb.AppendLine($"Строк в каждом:  {sets[0].Items.Count}");
            if (warnings.Count > 0)
            {
               sb.AppendLine();
               sb.AppendLine("Предупреждения:");
               foreach (var w in warnings.Take(8))
                  sb.AppendLine($"  - {w}");
               if (warnings.Count > 8)
                  sb.AppendLine($"  ... ещё {warnings.Count - 8}");
            }
            Preview = sb.ToString().TrimEnd();

            if (ResultName.Length == 0)
               ResultName = "СП20 — " + string.Join(", ", sets.Select(s => s.Tag));
         }
         catch (System.Exception ex)
         {
            Preview = $"Ошибка: {ex.Message}";
         }
      }

      void Generate()
      {
         var sets = SelectedSets;
         if (sets.Count == 0)
         {
            MessageBox.Show("Не выбрано ни одного набора.", _window.Title,
               MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
         }
         if (!MakeEnvelope && !MakeCases)
         {
            MessageBox.Show("Нужно выбрать хотя бы один вид вывода.", _window.Title,
               MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
         }

         string baseName = ResultName.Trim().Length > 0
            ? ResultName.Trim()
            : "СП20 — " + string.Join(", ", sets.Select(s => s.Tag));

         var rowLabels = sets[0].Items.Select(i => i.Label).ToList();
         var created   = new List<string>();

         try
         {
            var mode = _selectedMode.Mode;
            if (mode is SP20ModeChoice.Fundamental or SP20ModeChoice.Both)
               RunMode(sets, CombType.Fundamental, baseName, "основное", "Cm", rowLabels, created);
            if (mode is SP20ModeChoice.Accidental or SP20ModeChoice.Both)
               RunMode(sets, CombType.Accidental, baseName, "особое", "Cs", rowLabels, created);
         }
         catch (System.Exception ex)
         {
            MessageBox.Show($"Ошибка при генерации:\n{ex.Message}", _window.Title,
               MessageBoxButton.OK, MessageBoxImage.Error);
            return;
         }

         MessageBox.Show(
            "Созданы наборы:\n" + string.Join("\n", created.Select(n => $"  - {n}")),
            _window.Title, MessageBoxButton.OK, MessageBoxImage.Information);

         _window.DialogResult = true;
      }

      void RunMode(List<ForceSet> sets, CombType combType, string baseName,
                   string suffix, string labelPrefix,
                   List<string> rowLabels, List<string> created)
      {
         var (env, cases, loadings, _) =
            SP20Combinations.SP20EnvelopeAndCasesFromForceSets(sets, combType, _app.CalcSettings.ToSp20GammaDefaults());

         string kind = sets[0].Kind;

         if (MakeEnvelope)
         {
            var fs = SP20Combinations.EnvelopeToForceSet(
               env, kind, $"{baseName} ({suffix})", labelPrefix);
            SaveAndAdd(fs);
            created.Add(fs.Tag);
         }

         if (MakeCases)
         {
            var fs = SP20Combinations.CasesToForceSet(
               cases, kind, $"{baseName} ({suffix} — список)");
            SaveAndAdd(fs);
            created.Add(fs.Tag);
         }
      }

      void SaveAndAdd(ForceSet fs)
      {
         fs.Num = _app.ForceSets.Count > 0
            ? _app.ForceSets.Max(s => s.Num) + 1 : 1;
         _app.db.SaveForceSet(fs);
         _app.ForceSets.Add(fs);
      }
   }
}
