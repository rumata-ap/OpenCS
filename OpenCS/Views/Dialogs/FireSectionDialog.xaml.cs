using CScore;
using CScore.Fire.Entities;
using OpenCS.Services;
using OpenCS.Utilites;
using System.Windows;

namespace OpenCS.Views.Dialogs
{
   /// <summary>
   /// Окно создания огневого сечения: имя, сечение, длительность, огневая кривая и шаблон ГУ; остальные параметры —
   /// по умолчанию и правятся на странице огневого сечения.
   /// </summary>
   public partial class FireSectionDialog : Window
   {
      public FireSectionDef? Result { get; private set; }

      public FireSectionDialog(AppViewModel app)
      {
         InitializeComponent();
         Owner = Application.Current?.MainWindow;
         DataContext = new FireSectionDialogVM(app);
      }

      void Ok_Click(object sender, RoutedEventArgs e)
      {
         if (DataContext is not FireSectionDialogVM vm) return;
         Result = vm.TryBuild();
         if (Result != null) DialogResult = true;
      }
   }

   /// <summary>VM окна создания огневого сечения: строгий разбор длительности и проверка службой.</summary>
   public class FireSectionDialogVM : ViewModelBase
   {
      readonly AppViewModel _app;
      string _tag, _durationText = "60", _fireCurve = "iso834", _bcPreset = "3-sided", _errorText = "";
      CrossSection? _selectedSection;

      public FireSectionDialogVM(AppViewModel app)
      {
         _app = app;
         Sections = app.CrossSections.Where(s => s.ParametricSteel == null).ToList();
         _selectedSection = app.CurrentCrossSection is { ParametricSteel: null } cur && Sections.Contains(cur)
            ? cur : Sections.FirstOrDefault();
         _tag = FireSectionAuthoring.DefaultTag(FireSectionAuthoring.NextNum(app.db));
      }

      public IReadOnlyList<CrossSection> Sections { get; }

      public string Tag { get => _tag; set { _tag = value ?? ""; OnPropertyChanged(); } }
      public CrossSection? SelectedSection { get => _selectedSection; set { _selectedSection = value; OnPropertyChanged(); } }
      public string FireDurationText { get => _durationText; set { _durationText = value ?? ""; OnPropertyChanged(); } }
      public string FireCurve { get => _fireCurve; set { _fireCurve = value ?? "iso834"; OnPropertyChanged(); } }
      public string BcPreset { get => _bcPreset; set { _bcPreset = value ?? "manual"; OnPropertyChanged(); } }

      public string ErrorText
      {
         get => _errorText;
         private set { _errorText = value; OnPropertyChanged(); OnPropertyChanged(nameof(HasErrorText)); }
      }
      public bool HasErrorText => _errorText.Length > 0;

      /// <summary>Новое огневое сечение (не записано) или null — ошибки в <see cref="ErrorText"/>.</summary>
      public FireSectionDef? TryBuild()
      {
         var d = FireSectionAuthoring.CreateDefault(_app.db, _selectedSection?.Id ?? 0);
         d.Tag = string.IsNullOrWhiteSpace(_tag) ? d.Tag : _tag.Trim();
         d.FireCurve = _fireCurve;
         d.BcPreset = _bcPreset;
         var messages = new List<string>();
         if (NumberInput.TryParse(_durationText, out double dur)) d.FireDurationMin = dur;
         else messages.Add($"{Loc.S("FireSection_Duration")}: {Loc.S("FireErrNotNumber")}");
         if (messages.Count == 0)
            messages.AddRange(FireSectionAuthoring.Validate(_app.db, d).Select(e => e.Message));
         ErrorText = string.Join("\n", messages);
         return messages.Count == 0 ? d : null;
      }
   }
}
