using CScore;
using CScore.Combinations;
using OpenCS.Services;
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
         var vm = new SP20DialogVM(allSets, app);
         vm.Done += () => DialogResult = true;
         DataContext = vm;
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
   public class SP20ModeItem
   {
      public ForceSetAuthoring.Sp20Mode Mode { get; init; }
      public string Label       { get; init; } = "";
      public override string ToString() => Label;
   }

   /// <summary>
   /// Диалог сочетаний СП 20: выбор наборов и вида, предпросмотр распознанных загружений; расчёт и запись — через
   /// <see cref="ForceSetAuthoring.CombineSp20"/> (все виды считаются до записи, одна транзакция).
   /// </summary>
   public class SP20DialogVM : ViewModelBase
   {
      readonly AppViewModel _app;
      bool _makeEnvelope = true;
      bool _makeCases = true;
      SP20ModeItem _selectedMode;
      string _resultName = "";
      bool _nameEdited;

      /// <summary>Наборы созданы — закрыть окно.</summary>
      public event Action? Done;

      /// <summary>Сообщение пользователю (заменяется в тестах).</summary>
      public Action<string, MessageBoxImage> Show { get; set; } = (text, icon) =>
         MessageBox.Show(text, Loc.S("SP20DialogTitle"), MessageBoxButton.OK, icon);

      public SP20DialogVM(IEnumerable<ForceSet> allSets, AppViewModel app)
      {
         _app    = app;

         Items = new ObservableCollection<ForceSetSelectionVM>(
            allSets.Select(fs => new ForceSetSelectionVM(fs)));
         foreach (var it in Items)
            it.PropertyChanged += (_, _) => RefreshPreview();

         ModeOptions =
         [
            new SP20ModeItem { Mode = ForceSetAuthoring.Sp20Mode.Fundamental, Label = Loc.S("SP20ModeFundamental") },
            new SP20ModeItem { Mode = ForceSetAuthoring.Sp20Mode.Accidental,  Label = Loc.S("SP20ModeAccidental")  },
            new SP20ModeItem { Mode = ForceSetAuthoring.Sp20Mode.Both,        Label = Loc.S("SP20ModeBoth")        },
         ];
         _selectedMode = ModeOptions[2];
         _selectedGroup = GroupOptions[0];

         GenerateCommand = new RelayCommand(_ => Generate());
         RefreshPreview();
      }

      public ObservableCollection<ForceSetSelectionVM> Items { get; }

      /// <summary>Группа основного сочетания: те же C/CL/N/NL, что CalcType проверок (метка в имени набора).</summary>
      public List<string> GroupOptions { get; } = [.. ForceSetAuthoring.Sp20Groups];

      string _selectedGroup;
      public string SelectedGroup
      {
         get => _selectedGroup;
         set { _selectedGroup = value; OnPropertyChanged(); RefreshPreview(); }
      }

      /// <summary>Вид величин нагрузок в наборах — глобальная настройка расчёта.</summary>
      public string ValueKindText => string.Format(Loc.S("SP20ValueKindHint"),
         Loc.S(_app.CalcSettings.ToSp20LoadValueKind() == Sp20LoadValueKind.Design
            ? "CalcSettingsSp20Design" : "CalcSettingsSp20Normative"));
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

      /// <summary>Базовое имя результатов: следует за выбором наборов, пока пользователь его не правил.</summary>
      public string ResultName
      {
         get => _resultName;
         set { _resultName = value ?? ""; _nameEdited = _resultName.Trim().Length > 0; OnPropertyChanged(); }
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
         if (!_nameEdited)
         {
            _resultName = sets.Count > 0 ? ForceSetAuthoring.Sp20DefaultName(sets) : "";
            OnPropertyChanged(nameof(ResultName));
         }
         if (sets.Count == 0)
         {
            Preview = Loc.S("Sp20ErrNoSets");
            return;
         }

         try
         {
            var (loadings, warnings) = SP20Combinations.ForceSetsToLoadings(sets, _app.CalcSettings.ToSp20GammaDefaults());
            var sb = new System.Text.StringBuilder();
            sb.AppendLine(string.Format(Loc.S("Sp20PreviewLoadings"),
               loadings.Count(l => l.LoadType == NormLoadType.Permanent),
               loadings.Count(l => l.LoadType == NormLoadType.LongTerm),
               loadings.Count(l => l.LoadType == NormLoadType.ShortTerm),
               loadings.Count(l => l.LoadType == NormLoadType.Accidental)));
            sb.AppendLine(string.Format(Loc.S("Sp20PreviewSets"), sets.Count, SP20Combinations.RowCount(sets[0])));
            SP20Combinations.SecondOrderOf(sets, out bool mixedOrder);
            if (mixedOrder) sb.AppendLine(Loc.S("Sp20SecondOrderMixed"));
            SP20Combinations.ImperfectionsOf(sets, out bool mixedImperfections);
            if (mixedImperfections) sb.AppendLine(Loc.S("Sp20ImperfectionsMixed"));
            if (warnings.Count > 0)
            {
               sb.AppendLine(Loc.S("Sp20PreviewWarnings"));
               foreach (var w in warnings.Take(8))
                  sb.AppendLine($"  - {w}");
               if (warnings.Count > 8)
                  sb.AppendLine(string.Format(Loc.S("Sp20PreviewMore"), warnings.Count - 8));
            }
            Preview = sb.ToString().TrimEnd();
         }
         catch (Exception ex) when (ex is InvalidOperationException or ArgumentException)
         {
            Preview = string.Format(Loc.S("Sp20PreviewError"), ex.Message);
         }
      }

      /// <summary>Расчёт и запись сочетаний; ошибки — сообщением, без записи.</summary>
      internal bool Generate()
      {
         var request = new ForceSetAuthoring.Sp20Request(SelectedSets, _selectedMode.Mode, SelectedGroup, MakeEnvelope,
            MakeCases, ResultName, _app.CalcSettings.ToSp20LoadValueKind(), _app.CalcSettings.ToSp20GammaDefaults());
         var (created, _, errors) = ForceSetAuthoring.CombineSp20(_app.db, request);
         if (errors.Count > 0)
         {
            Show(string.Join(Environment.NewLine, errors), MessageBoxImage.Warning);
            return false;
         }
         foreach (var fs in created)
            _app.LogService.Info(string.Format(Loc.S("ForceSetSavedNewLog"), fs.Tag));
         Show(string.Format(Loc.S("Sp20Created"), created.Count)
              + Environment.NewLine + string.Join(Environment.NewLine, created.Select(n => $"  - {n.Tag}")),
            MessageBoxImage.Information);
         Done?.Invoke();
         return true;
      }
   }
}
