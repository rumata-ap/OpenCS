using CScore;
using OpenCS.Utilites;
using OpenCS.ViewModels;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using System.Windows.Input;

namespace OpenCS.Views
{
   public partial class CalcTaskPropsDialog : Window
   {
      public CalcTaskPropsDialog(AppViewModel app, CalcTask? existing = null)
      {
         InitializeComponent();
         DataContext = new CalcTaskPropsDlgVM(app, existing, this);
      }

      /// <summary>Результирующая задача после закрытия диалога (заполнена если OK).</summary>
      public CalcTask? Result => (DataContext as CalcTaskPropsDlgVM)?.Result;
   }

   public class CalcTaskPropsDlgVM : ViewModelBase
   {
      readonly AppViewModel _app;
      readonly Window       _window;

      public CalcTask? Result { get; private set; }

      string tag  = "";
      string kind = "strain_state";
      CrossSection?   selectedSection   = null;
      ForceSet?       selectedForceSet  = null;
      LoadItem?       selectedForceItem = null;
      CalcType        selectedCalcType  = CalcType.C;

      public string Tag  { get => tag;  set { tag = value;  OnPropertyChanged(); } }
      public string Kind { get => kind; set { kind = value; OnPropertyChanged(); } }

      public CrossSection? SelectedSection
      {
         get => selectedSection;
         set { selectedSection = value; OnPropertyChanged(); }
      }

      public ForceSet? SelectedForceSet
      {
         get => selectedForceSet;
         set
         {
            selectedForceSet = value;
            ForceItems.Clear();
            if (value != null)
               foreach (var item in value.Items) ForceItems.Add(item);
            SelectedForceItem = ForceItems.FirstOrDefault();
            OnPropertyChanged();
         }
      }

      public LoadItem? SelectedForceItem
      {
         get => selectedForceItem;
         set { selectedForceItem = value; OnPropertyChanged(); }
      }

      public CalcType SelectedCalcType
      {
         get => selectedCalcType;
         set { selectedCalcType = value; OnPropertyChanged(); }
      }

      public List<string>      AvailableKinds { get; } = ["strain_state"];
      public ObservableCollection<CrossSection> Sections   { get; }
      public ObservableCollection<ForceSet>     ForceSets  { get; }
      public ObservableCollection<LoadItem>     ForceItems { get; } = [];
      public List<CalcType> CalcTypes { get; } = [CalcType.C, CalcType.CL, CalcType.N, CalcType.NL];

      public ICommand OkCommand { get; }

      public CalcTaskPropsDlgVM(AppViewModel app, CalcTask? existing, Window window)
      {
         _app    = app;
         _window = window;

         Sections  = new ObservableCollection<CrossSection>(app.CrossSections);
         ForceSets = new ObservableCollection<ForceSet>(app.BarForceSets);

         if (existing != null)
         {
            Tag              = existing.Tag;
            Kind             = existing.Kind;
            SelectedSection  = Sections.FirstOrDefault(s => s.Id == existing.SectionId);
            SelectedForceSet = ForceSets.FirstOrDefault(fs => fs.Id == existing.ForceSetId);
            SelectedForceItem = ForceItems.FirstOrDefault(fi => fi.Id == existing.ForceItemId);
            SelectedCalcType  = existing.CalcType;
         }
         else
         {
            SelectedSection  = Sections.FirstOrDefault();
            SelectedForceSet = ForceSets.FirstOrDefault();
            SelectedCalcType = CalcType.C;
         }

         OkCommand = new RelayCommand(_ => Commit());
      }

      void Commit()
      {
         if (SelectedSection == null)
         {
            MessageBox.Show(Loc.S("CalcTaskNeedSection"), Loc.S("Warning"),
               MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
         }
         if (SelectedForceSet == null || SelectedForceItem == null)
         {
            MessageBox.Show(Loc.S("CalcTaskNeedForceItem"), Loc.S("Warning"),
               MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
         }

         Result = new CalcTask
         {
            Tag         = string.IsNullOrWhiteSpace(Tag) ? $"Задача {_app.CalcTasks.Count + 1}" : Tag,
            Kind        = Kind,
            SectionId   = SelectedSection.Id,
            ForceSetId  = SelectedForceSet.Id,
            ForceItemId = SelectedForceItem.Id,
            CalcType    = SelectedCalcType
         };

         _window.DialogResult = true;
      }
   }
}
