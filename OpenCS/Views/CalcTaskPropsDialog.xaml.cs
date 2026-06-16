using System.Collections.ObjectModel;
using System.Text.Json;
using System.Windows;
using System.Windows.Input;
using CScore;
using CScore.Fire.Entities;
using OpenCS.Tasks;
using OpenCS.Utilites;
using OpenCS.ViewModels;

namespace OpenCS.Views;

public partial class CalcTaskPropsDialog : Window
{
   public CalcTaskPropsDialog(AppViewModel app, CalcTask? existing = null)
   {
      InitializeComponent();
      DataContext = new CalcTaskPropsDlgVM(app, existing, this);
   }

   public CalcTask? Result => (DataContext as CalcTaskPropsDlgVM)?.Result;
}

public class CalcTaskKindItem
{
   public string Id { get; init; } = "";
   public string Label { get; init; } = "";
}

public class CalcTaskPropsDlgVM : ViewModelBase
{
   readonly AppViewModel _app;
   readonly Window _window;

   public CalcTask? Result { get; private set; }

   string tag = "";
   CalcTaskKindItem? selectedKind;
   CrossSection? selectedSection;
   ForceSet? selectedForceSet;
   LoadItem? selectedForceItem;
   FireSectionDef? selectedFireSection;
   CalcType selectedCalcType = CalcType.C;
   string manualN = "0";
   string manualMx = "0";
   string manualMy = "0";

   public string Tag { get => tag; set { tag = value; OnPropertyChanged(); } }

   public string ManualN  { get => manualN;  set { manualN  = value; OnPropertyChanged(); } }
   public string ManualMx { get => manualMx; set { manualMx = value; OnPropertyChanged(); } }
   public string ManualMy { get => manualMy; set { manualMy = value; OnPropertyChanged(); } }

   public bool ShowManualForces => Kind == "strain_state";

   public CalcTaskKindItem? SelectedKind
   {
      get => selectedKind;
      set
      {
         selectedKind = value;
         if (value != null)
            Kind = value.Id;
         OnPropertyChanged();
         OnPropertyChanged(nameof(IsFireKind));
         OnPropertyChanged(nameof(ShowManualForces));
      }
   }

   public string Kind
   {
      get => selectedKind?.Id ?? "strain_state";
      set
      {
         selectedKind = AvailableKinds.FirstOrDefault(k => k.Id == value) ?? AvailableKinds[0];
         OnPropertyChanged();
         OnPropertyChanged(nameof(SelectedKind));
         OnPropertyChanged(nameof(IsFireKind));
         OnPropertyChanged(nameof(ShowManualForces));
      }
   }

   public bool IsFireKind => Kind.StartsWith("fire_", StringComparison.Ordinal);

   public CrossSection? SelectedSection
   {
      get => selectedSection;
      set { selectedSection = value; OnPropertyChanged(); }
   }

   public FireSectionDef? SelectedFireSection
   {
      get => selectedFireSection;
      set
      {
         selectedFireSection = value;
         if (value != null)
            SelectedSection = Sections.FirstOrDefault(s => s.Id == value.SectionId);
         OnPropertyChanged();
      }
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
      set
      {
         selectedForceItem = value;
         if (value != null && ShowManualForces)
         {
            ManualN  = value.N .ToString("G6", System.Globalization.CultureInfo.InvariantCulture);
            ManualMx = value.Mx.ToString("G6", System.Globalization.CultureInfo.InvariantCulture);
            ManualMy = value.My.ToString("G6", System.Globalization.CultureInfo.InvariantCulture);
         }
         OnPropertyChanged();
      }
   }

   public CalcType SelectedCalcType
   {
      get => selectedCalcType;
      set { selectedCalcType = value; OnPropertyChanged(); }
   }

   public List<CalcTaskKindItem> AvailableKinds { get; } =
   [
      new() { Id = "strain_state", Label = Loc.S("CalcTaskKind_strain_state") },
      new() { Id = "fire_r_check", Label = Loc.S("CalcTaskKind_fire_r_check") },
      new() { Id = "fire_r_check_batch", Label = Loc.S("CalcTaskKind_fire_r_check_batch") }
   ];

   public ObservableCollection<CrossSection> Sections { get; }
   public ObservableCollection<FireSectionDef> FireSections { get; }
   public ObservableCollection<ForceSet> ForceSets { get; }
   public ObservableCollection<LoadItem> ForceItems { get; } = [];

   public List<CalcType> CalcTypes { get; } = [CalcType.C, CalcType.CL, CalcType.N, CalcType.NL];

   public ICommand OkCommand { get; }

   public CalcTaskPropsDlgVM(AppViewModel app, CalcTask? existing, Window window)
   {
      _app = app;
      _window = window;

      Sections = new ObservableCollection<CrossSection>(app.CrossSections);
      FireSections = new ObservableCollection<FireSectionDef>(app.FireSections);
      ForceSets = new ObservableCollection<ForceSet>(app.BarForceSets);

      if (existing != null)
      {
         Tag = existing.Tag;
         Kind = existing.Kind;
         SelectedSection = Sections.FirstOrDefault(s => s.Id == existing.SectionId);
         SelectedForceSet = ForceSets.FirstOrDefault(fs => fs.Id == existing.ForceSetId);
         SelectedForceItem = ForceItems.FirstOrDefault(fi => fi.Id == existing.ForceItemId);
         SelectedCalcType = existing.CalcType;

         var p = FireRCheckParams.Parse(existing.ParamsJson);
         if (p.FireSectionId > 0)
            SelectedFireSection = FireSections.FirstOrDefault(f => f.Id == p.FireSectionId);

         if (existing.Kind == "strain_state" && !string.IsNullOrWhiteSpace(existing.ParamsJson) && existing.ParamsJson != "{}")
         {
            try
            {
               using var doc = System.Text.Json.JsonDocument.Parse(existing.ParamsJson);
               var root = doc.RootElement;
               var inv = System.Globalization.CultureInfo.InvariantCulture;
               if (root.TryGetProperty("N",  out var nEl))  ManualN  = nEl.GetDouble().ToString("G6", inv);
               if (root.TryGetProperty("Mx", out var mxEl)) ManualMx = mxEl.GetDouble().ToString("G6", inv);
               if (root.TryGetProperty("My", out var myEl)) ManualMy = myEl.GetDouble().ToString("G6", inv);
            }
            catch { /* оставить значения по умолчанию */ }
         }
      }
      else
      {
         SelectedKind = AvailableKinds[0];
         SelectedSection = Sections.FirstOrDefault();
         SelectedFireSection = FireSections.FirstOrDefault();
         SelectedForceSet = ForceSets.FirstOrDefault();
         SelectedCalcType = CalcType.C;
      }

      OkCommand = new RelayCommand(_ => Commit());
   }

   void Commit()
   {
      if (IsFireKind)
      {
         if (SelectedFireSection == null)
         {
            MessageBox.Show(Loc.S("CalcTaskNeedFireSection"), Loc.S("Warning"),
               MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
         }
         SelectedSection = Sections.FirstOrDefault(s => s.Id == SelectedFireSection.SectionId);
      }

      if (SelectedSection == null)
      {
         MessageBox.Show(Loc.S("CalcTaskNeedSection"), Loc.S("Warning"),
            MessageBoxButton.OK, MessageBoxImage.Warning);
         return;
      }

      if (!ShowManualForces && (SelectedForceSet == null || SelectedForceItem == null))
      {
         MessageBox.Show(Loc.S("CalcTaskNeedForceItem"), Loc.S("Warning"),
            MessageBoxButton.OK, MessageBoxImage.Warning);
         return;
      }

      string paramsJson = "{}";
      if (IsFireKind && SelectedFireSection != null)
      {
         paramsJson = JsonSerializer.Serialize(new FireRCheckParams
         {
            FireSectionId = SelectedFireSection.Id,
            Method = "fiber",
            SnapshotIndex = -1
         });
      }
      else if (ShowManualForces)
      {
         var inv = System.Globalization.CultureInfo.InvariantCulture;
         double n  = double.TryParse(ManualN,  System.Globalization.NumberStyles.Float, inv, out var nv)  ? nv : 0;
         double mx = double.TryParse(ManualMx, System.Globalization.NumberStyles.Float, inv, out var mxv) ? mxv : 0;
         double my = double.TryParse(ManualMy, System.Globalization.NumberStyles.Float, inv, out var myv) ? myv : 0;
         paramsJson = JsonSerializer.Serialize(new { N = n, Mx = mx, My = my });
      }

      Result = new CalcTask
      {
         Tag = string.IsNullOrWhiteSpace(Tag) ? $"Задача {_app.CalcTasks.Count + 1}" : Tag,
         Kind = Kind,
         SectionId = SelectedSection.Id,
         ForceSetId  = SelectedForceSet?.Id  ?? 0,
         ForceItemId = SelectedForceItem?.Id ?? 0,
         CalcType = SelectedCalcType,
         ParamsJson = paramsJson
      };

      _window.DialogResult = true;
   }
}
