using CScore;
using CScore.Fire.Entities;
using OpenCS.Utilites;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows;

namespace OpenCS.Views.Dialogs
{
   /// <summary>
   /// Диалог создания и редактирования огневого сечения.
   /// </summary>
   public partial class FireSectionDialog : Window
   {
      readonly AppViewModel _app;
      readonly FireSectionDef? _source;

      public FireSectionDef? Result { get; private set; }

      public FireSectionDialog(AppViewModel app, FireSectionDef? existing = null)
      {
         InitializeComponent();
         _app = app;
         _source = existing;
         Owner = Application.Current.MainWindow;
         DataContext = new FireSectionDialogVM(app, existing);
      }

      void Ok_Click(object sender, RoutedEventArgs e)
      {
         if (DataContext is not FireSectionDialogVM vm) return;
         if (vm.SelectedSection == null)
         {
            MessageBox.Show(Loc.S("FireSection_NeedCrossSection"), Loc.S("Warning"),
               MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
         }

         Result = new FireSectionDef
         {
            Id = _source?.Id ?? 0,
            Num = _source?.Num ?? 0,
            Tag = string.IsNullOrWhiteSpace(vm.Tag)
               ? string.Format(Loc.S("FireSection_DefaultTag"), _app.FireSections.Count + 1)
               : vm.Tag.Trim(),
            SectionId = vm.SelectedSection.Id,
            FireDurationMin = vm.ParseFireDurationMin(),
            FireCurve = vm.FireCurve,
            MeshStepM = vm.ParseMeshStepM(),
            TimeStepS = vm.ParseTimeStepS(),
            BcPreset = vm.BcPreset,
            HoleBcPreset = _source?.HoleBcPreset ?? "ambient",
            Theta = _source?.Theta ?? 1.0,
            PicardTolCelsius = _source?.PicardTolCelsius ?? 0.5,
            PicardMaxIter = _source?.PicardMaxIter ?? 20,
            SnapshotStepMin = _source?.SnapshotStepMin ?? 5.0,
            Algorithm = _source?.Algorithm ?? "ruppert",
            SmoothIterTri = _source?.SmoothIterTri ?? 5,
            Edges = _source?.Edges ?? []
         };

         DialogResult = true;
      }
   }

   /// <summary>
   /// ViewModel диалога параметров огневого сечения.
   /// </summary>
   public class FireSectionDialogVM : ViewModelBase
   {
      public ObservableCollection<CrossSection> CrossSections { get; }

      string tag = "";
      CrossSection? selectedSection;
      string fireDurationMinText = "60";
      string fireCurve = "iso834";
      string meshStepMText = "0.01";
      string timeStepSText = "5";
      string bcPreset = "manual";

      public string Tag
      {
         get => tag;
         set { tag = value; OnPropertyChanged(); }
      }

      public CrossSection? SelectedSection
      {
         get => selectedSection;
         set { selectedSection = value; OnPropertyChanged(); }
      }

      public string FireDurationMinText
      {
         get => fireDurationMinText;
         set { fireDurationMinText = value; OnPropertyChanged(); }
      }

      public string FireCurve
      {
         get => fireCurve;
         set { fireCurve = value; OnPropertyChanged(); }
      }

      public string MeshStepMText
      {
         get => meshStepMText;
         set { meshStepMText = value; OnPropertyChanged(); }
      }

      public string TimeStepSText
      {
         get => timeStepSText;
         set { timeStepSText = value; OnPropertyChanged(); }
      }

      public string BcPreset
      {
         get => bcPreset;
         set { bcPreset = value; OnPropertyChanged(); }
      }

      public FireSectionDialogVM(AppViewModel app, FireSectionDef? existing)
      {
         CrossSections = new ObservableCollection<CrossSection>(app.CrossSections);
         if (existing != null)
         {
            Tag = existing.Tag;
            SelectedSection = CrossSections.FirstOrDefault(s => s.Id == existing.SectionId);
            FireDurationMinText = existing.FireDurationMin.ToString("G", CultureInfo.InvariantCulture);
            FireCurve = existing.FireCurve;
            MeshStepMText = existing.MeshStepM.ToString("G", CultureInfo.InvariantCulture);
            TimeStepSText = existing.TimeStepS.ToString("G", CultureInfo.InvariantCulture);
            BcPreset = existing.BcPreset;
         }
         else
         {
            SelectedSection = CrossSections.FirstOrDefault();
         }
      }

      public double ParseFireDurationMin() => ParsePositiveOrDefault(FireDurationMinText, 60.0);
      public double ParseMeshStepM() => ParsePositiveOrDefault(MeshStepMText, 0.01);
      public double ParseTimeStepS() => ParsePositiveOrDefault(TimeStepSText, 5.0);

      static double ParsePositiveOrDefault(string? text, double fallback)
      {
         if (string.IsNullOrWhiteSpace(text)) return fallback;
         string normalized = text.Trim().Replace(',', '.');
         if (!double.TryParse(normalized, NumberStyles.Float, CultureInfo.InvariantCulture, out var value))
            return fallback;
         return value > 0.0 ? value : fallback;
      }
   }
}
