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

public class CalcTaskSolverItem
{
   public string Id { get; init; } = "";
   public string Label { get; init; } = "";
}

public class CalcTaskPropsDlgVM : ViewModelBase
{
   readonly AppViewModel _app;
   readonly Window _window;

   public CalcTask? Result { get; private set; }

    readonly List<CrossSection> _allSections;
    string tag = "";
    CalcTaskKindItem? selectedKind;
    CalcTaskSolverItem? selectedSolver;
    CrossSection? selectedSection;
    ForceSet? selectedForceSet;
    LoadItem? selectedForceItem;
    FireSectionDef? selectedFireSection;
    CalcType selectedCalcType = CalcType.C;
    string manualN = "0";
    string manualMx = "0";
    string manualMy = "0";
    // Shell simpl
    PlateSection? selectedShellSimplSection;
    ForceSet? selectedShellForceSet;
    ShellLoadItem? selectedShellForceItem;
    string shellSimplNx = "0", shellSimplNy = "0", shellSimplNxy = "0";
    string shellSimplMx = "0", shellSimplMy = "0", shellSimplMxy = "0";
    string shellSimplStepDeg = "10";
    string shellSimplAcrcLim = "0.3";
    string shellSimplPhi1 = "1.0";
    string shellSimplPhi2 = "0.5";
    ForceSet? stage1Set, stage2Set;
    LoadItem? stage1Item, stage2Item;
    bool stage1UseManual, stage2UseManual;
    string stage1ManualN = "0", stage1ManualMx = "0", stage1ManualMy = "0";
    string stage2ManualN = "0", stage2ManualMx = "0", stage2ManualMy = "0";

   public string Tag { get => tag; set { tag = value; OnPropertyChanged(); } }

   public string ManualN  { get => manualN;  set { manualN  = value; OnPropertyChanged(); } }
   public string ManualMx { get => manualMx; set { manualMx = value; OnPropertyChanged(); } }
   public string ManualMy { get => manualMy; set { manualMy = value; OnPropertyChanged(); } }

   public bool IsLimitSingle  => IsLimitSingleKind(Kind);
   public bool ShowManualForces => Kind == "strain_state" || IsLimitSingle;

   static bool IsLimitSingleKind(string kind)
      => kind is "limit_force" or "limit_moment" or "limit_axial";

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
           OnPropertyChanged(nameof(IsStrainBatch));
           OnPropertyChanged(nameof(IsLimitBatch));
           OnPropertyChanged(nameof(IsLimitSingle));
           OnPropertyChanged(nameof(ShowForceItem));
           OnPropertyChanged(nameof(ShowManualForces));
           OnPropertyChanged(nameof(ShowSolverMethod));
           OnPropertyChanged(nameof(IsTwoStage));
           OnPropertyChanged(nameof(IsTwoStageBatch));
           OnPropertyChanged(nameof(IsShellSimpl));
           OnPropertyChanged(nameof(IsShellSimplCapri));
           OnPropertyChanged(nameof(IsShellSimplSls));
           OnPropertyChanged(nameof(ShowStandardForce));
           OnPropertyChanged(nameof(Stage1ShowSet));
           OnPropertyChanged(nameof(Stage1ShowManual));
           OnPropertyChanged(nameof(Stage2ShowSet));
           OnPropertyChanged(nameof(Stage2ShowManual));
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
           OnPropertyChanged(nameof(IsStrainBatch));
           OnPropertyChanged(nameof(IsLimitBatch));
           OnPropertyChanged(nameof(IsLimitSingle));
           OnPropertyChanged(nameof(ShowForceItem));
           OnPropertyChanged(nameof(ShowManualForces));
           OnPropertyChanged(nameof(ShowSolverMethod));
           OnPropertyChanged(nameof(IsTwoStage));
           OnPropertyChanged(nameof(IsTwoStageBatch));
           OnPropertyChanged(nameof(IsShellSimpl));
           OnPropertyChanged(nameof(IsShellSimplCapri));
           OnPropertyChanged(nameof(IsShellSimplSls));
           OnPropertyChanged(nameof(ShowStandardForce));
           OnPropertyChanged(nameof(Stage1ShowSet));
           OnPropertyChanged(nameof(Stage1ShowManual));
           OnPropertyChanged(nameof(Stage2ShowSet));
           OnPropertyChanged(nameof(Stage2ShowManual));
           FilterSections();
        }
     }

    public bool IsFireKind    => Kind.StartsWith("fire_", StringComparison.Ordinal);
   public bool IsStrainBatch => Kind == "strain_state_batch" || Kind == "strength_ndm_batch";
   public bool IsLimitBatch  => Kind is "limit_force_batch" or "limit_moment_batch" or "limit_axial_batch";
   public bool IsLimitKind   => Kind.StartsWith("limit_", StringComparison.Ordinal);
   public bool IsShellSimpl      => Kind.StartsWith("shell_simpl_", StringComparison.Ordinal);
   public bool IsShellSimplCapri => Kind is "shell_simpl_capri_sls" or "shell_simpl_capri_uls";
   public bool IsShellSimplSls   => Kind is "shell_simpl_wa_sls" or "shell_simpl_capri_sls";
   public bool IsTwoStage      => Kind is "two_stage_strain" or "two_stage_strain_batch";
   public bool IsTwoStageBatch => Kind == "two_stage_strain_batch";
   public bool ShowForceItem => !IsStrainBatch && !IsLimitBatch && !IsFireKind && !IsTwoStage;
   public bool ShowSolverMethod => IsLimitKind;

   /// <summary>Показывать стандартный одиночный выбор набора усилий (скрыт для two-stage).</summary>
   public bool ShowStandardForce => !IsTwoStage;

   void FilterSections()
   {
      Sections.Clear();
      IEnumerable<CrossSection> filtered;
      if (IsTwoStage)
         filtered = _allSections.Where(s => s is TwoStageSection);
      else
         filtered = _allSections.Where(s => s is not TwoStageSection);

      foreach (var s in filtered)
         Sections.Add(s);

      if (SelectedSection != null && !Sections.Contains(SelectedSection))
         SelectedSection = Sections.FirstOrDefault();
   }

   public CalcTaskSolverItem? SelectedSolver
   {
      get => selectedSolver;
      set { selectedSolver = value; OnPropertyChanged(); }
   }

   public string SolverId
   {
      get => selectedSolver?.Id ?? "bisection";
      set
      {
         selectedSolver = SolverMethods.FirstOrDefault(s => s.Id == value) ?? SolverMethods[0];
         OnPropertyChanged();
         OnPropertyChanged(nameof(SelectedSolver));
      }
   }

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
              SelectedSection = _allSections.FirstOrDefault(s => s.Id == value.SectionId);
           OnPropertyChanged();
        }
    }

   public ObservableCollection<PlateSection> ShellSimplSections { get; }
   public PlateSection? SelectedShellSimplSection
   {
       get => selectedShellSimplSection;
       set { selectedShellSimplSection = value; OnPropertyChanged(); }
   }

   public ForceSet? SelectedShellForceSet
   {
       get => selectedShellForceSet;
       set
       {
           selectedShellForceSet = value;
           ShellForceItems.Clear();
           if (value != null)
               foreach (var item in value.ShellItems) ShellForceItems.Add(item);
           SelectedShellForceItem = ShellForceItems.FirstOrDefault();
           OnPropertyChanged();
       }
   }
   public ShellLoadItem? SelectedShellForceItem
   {
       get => selectedShellForceItem;
       set
       {
           selectedShellForceItem = value;
           if (value != null)
           {
               var inv = System.Globalization.CultureInfo.InvariantCulture;
               ShellSimplNx  = value.Nx.ToString("G6", inv);
               ShellSimplNy  = value.Ny.ToString("G6", inv);
               ShellSimplNxy = value.Nxy.ToString("G6", inv);
               ShellSimplMx  = value.Mx.ToString("G6", inv);
               ShellSimplMy  = value.My.ToString("G6", inv);
               ShellSimplMxy = value.Mxy.ToString("G6", inv);
           }
           OnPropertyChanged();
       }
   }

   public ObservableCollection<ShellLoadItem> ShellForceItems { get; } = [];
   public ObservableCollection<ForceSet> ShellForceSets { get; }

   public string ShellSimplNx  { get => shellSimplNx;  set { shellSimplNx  = value; OnPropertyChanged(); } }
   public string ShellSimplNy  { get => shellSimplNy;  set { shellSimplNy  = value; OnPropertyChanged(); } }
   public string ShellSimplNxy { get => shellSimplNxy; set { shellSimplNxy = value; OnPropertyChanged(); } }
   public string ShellSimplMx  { get => shellSimplMx;  set { shellSimplMx  = value; OnPropertyChanged(); } }
   public string ShellSimplMy  { get => shellSimplMy;  set { shellSimplMy  = value; OnPropertyChanged(); } }
   public string ShellSimplMxy { get => shellSimplMxy; set { shellSimplMxy = value; OnPropertyChanged(); } }
   public string ShellSimplStepDeg  { get => shellSimplStepDeg;  set { shellSimplStepDeg  = value; OnPropertyChanged(); } }
   public string ShellSimplAcrcLim  { get => shellSimplAcrcLim;  set { shellSimplAcrcLim  = value; OnPropertyChanged(); } }
   public string ShellSimplPhi1     { get => shellSimplPhi1;     set { shellSimplPhi1     = value; OnPropertyChanged(); } }
   public string ShellSimplPhi2     { get => shellSimplPhi2;     set { shellSimplPhi2     = value; OnPropertyChanged(); } }

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
         if (value != null && ShowManualForces)
         {
            // Выбранная строка используется только для заполнения полей; выбор сразу сбрасывается
            var inv = System.Globalization.CultureInfo.InvariantCulture;
            ManualN  = value.N .ToString("G6", inv);
            ManualMx = value.Mx.ToString("G6", inv);
            ManualMy = value.My.ToString("G6", inv);
            selectedForceItem = null;
         }
         else
         {
            selectedForceItem = value;
         }
         OnPropertyChanged();
      }
   }

   public CalcType SelectedCalcType
   {
      get => selectedCalcType;
      set { selectedCalcType = value; OnPropertyChanged(); }
   }

   // ── Усилия этапов двухстадийной задачи ──────────────────────────────
   // Stage*Item == null трактуется как «весь набор» (режим "set", только пакетная).
   public ObservableCollection<LoadItem> Stage1Items { get; } = [];
   public ObservableCollection<LoadItem> Stage2Items { get; } = [];

    public ForceSet? Stage1Set
    {
       get => stage1Set;
       set
       {
          stage1Set = value;
          Stage1Items.Clear();
          if (value != null) foreach (var it in value.Items) Stage1Items.Add(it);
          Stage1Item = Stage1Items.FirstOrDefault();
          OnPropertyChanged();
       }
    }
    public LoadItem? Stage1Item { get => stage1Item; set { stage1Item = value; OnPropertyChanged(); } }

    public bool Stage1UseManual
    {
       get => stage1UseManual;
       set { stage1UseManual = value; OnPropertyChanged(); OnPropertyChanged(nameof(Stage1ShowSet)); OnPropertyChanged(nameof(Stage1ShowManual)); }
    }
    public bool Stage1UseSet { get => !stage1UseManual; set { Stage1UseManual = !value; } }
    public bool Stage1ShowSet    => IsTwoStage && !stage1UseManual;
    public bool Stage1ShowManual => IsTwoStage && stage1UseManual;
    public string Stage1ManualN  { get => stage1ManualN;  set { stage1ManualN  = value; OnPropertyChanged(); } }
    public string Stage1ManualMx { get => stage1ManualMx; set { stage1ManualMx = value; OnPropertyChanged(); } }
    public string Stage1ManualMy { get => stage1ManualMy; set { stage1ManualMy = value; OnPropertyChanged(); } }

    public ForceSet? Stage2Set
    {
       get => stage2Set;
       set
       {
          stage2Set = value;
          Stage2Items.Clear();
          if (value != null) foreach (var it in value.Items) Stage2Items.Add(it);
          Stage2Item = Stage2Items.FirstOrDefault();
          OnPropertyChanged();
       }
    }
    public LoadItem? Stage2Item { get => stage2Item; set { stage2Item = value; OnPropertyChanged(); } }

    public bool Stage2UseManual
    {
       get => stage2UseManual;
       set { stage2UseManual = value; OnPropertyChanged(); OnPropertyChanged(nameof(Stage2ShowSet)); OnPropertyChanged(nameof(Stage2ShowManual)); }
    }
    public bool Stage2UseSet { get => !stage2UseManual; set { Stage2UseManual = !value; } }
    public bool Stage2ShowSet    => IsTwoStage && !stage2UseManual;
    public bool Stage2ShowManual => IsTwoStage && stage2UseManual;
    public string Stage2ManualN  { get => stage2ManualN;  set { stage2ManualN  = value; OnPropertyChanged(); } }
    public string Stage2ManualMx { get => stage2ManualMx; set { stage2ManualMx = value; OnPropertyChanged(); } }
    public string Stage2ManualMy { get => stage2ManualMy; set { stage2ManualMy = value; OnPropertyChanged(); } }

   public List<CalcTaskKindItem> AvailableKinds { get; } =
   [
      new() { Id = "strain_state",         Label = Loc.S("CalcTaskKind_strain_state") },
      new() { Id = "strain_state_batch",   Label = Loc.S("CalcTaskKind_strain_state_batch") },
      new() { Id = "strength_ndm_batch",  Label = Loc.S("CalcTaskKind_strength_ndm_batch") },
      new() { Id = "two_stage_strain",       Label = Loc.S("CalcTaskKind_two_stage_strain") },
      new() { Id = "two_stage_strain_batch", Label = Loc.S("CalcTaskKind_two_stage_strain_batch") },
      new() { Id = "limit_force",          Label = Loc.S("CalcTaskKind_limit_force") },
      new() { Id = "limit_force_batch",    Label = Loc.S("CalcTaskKind_limit_force_batch") },
      new() { Id = "limit_moment",         Label = Loc.S("CalcTaskKind_limit_moment") },
      new() { Id = "limit_moment_batch",   Label = Loc.S("CalcTaskKind_limit_moment_batch") },
      new() { Id = "limit_axial",          Label = Loc.S("CalcTaskKind_limit_axial") },
      new() { Id = "limit_axial_batch",    Label = Loc.S("CalcTaskKind_limit_axial_batch") },
      new() { Id = "shell_simpl_wa_sls",     Label = Loc.S("CalcTaskKind_shell_simpl_wa_sls") },
      new() { Id = "shell_simpl_wa_uls",     Label = Loc.S("CalcTaskKind_shell_simpl_wa_uls") },
      new() { Id = "shell_simpl_capri_sls",  Label = Loc.S("CalcTaskKind_shell_simpl_capri_sls") },
      new() { Id = "shell_simpl_capri_uls",  Label = Loc.S("CalcTaskKind_shell_simpl_capri_uls") },
      new() { Id = "fire_r_check",         Label = Loc.S("CalcTaskKind_fire_r_check") },
      new() { Id = "fire_r_check_batch",   Label = Loc.S("CalcTaskKind_fire_r_check_batch") }
   ];

   public List<CalcTaskSolverItem> SolverMethods { get; } =
   [
      new() { Id = "bisection", Label = Loc.S("LimitForceSolver_Bisection") },
      new() { Id = "fast",      Label = Loc.S("LimitForceSolver_Fast") },
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

      _allSections = [..app.CrossSections];
      FireSections = new ObservableCollection<FireSectionDef>(app.FireSections);
      ForceSets = new ObservableCollection<ForceSet>(app.BarForceSets);
      Sections = new ObservableCollection<CrossSection>();
      ShellSimplSections = new ObservableCollection<PlateSection>(app.PlateSections);
      ShellForceSets = new ObservableCollection<ForceSet>(app.ShellForceSets);
      FilterSections();

      if (existing != null)
      {
         Tag = existing.Tag;
         Kind = existing.Kind;
         SelectedSection = _allSections.FirstOrDefault(s => s.Id == existing.SectionId);
         SelectedForceSet = ForceSets.FirstOrDefault(fs => fs.Id == existing.ForceSetId);
         SelectedForceItem = ForceItems.FirstOrDefault(fi => fi.Id == existing.ForceItemId);
         SelectedCalcType = existing.CalcType;

         var p = FireRCheckParams.Parse(existing.ParamsJson);
         if (p.FireSectionId > 0)
            SelectedFireSection = FireSections.FirstOrDefault(f => f.Id == p.FireSectionId);

         if ((existing.Kind == "strain_state" || IsLimitSingleKind(existing.Kind))
             && !string.IsNullOrWhiteSpace(existing.ParamsJson) && existing.ParamsJson != "{}")
         {
            var lp = LimitForceParams.Parse(existing.ParamsJson);
            if (existing.Kind != "strain_state")
               SolverId = lp.Solver;

            if (existing.ForceSetId == 0 || existing.Kind == "strain_state")
            {
               var inv = System.Globalization.CultureInfo.InvariantCulture;
               if (lp.N.HasValue)  ManualN  = lp.N.Value.ToString("G6", inv);
               if (lp.Mx.HasValue) ManualMx = lp.Mx.Value.ToString("G6", inv);
               if (lp.My.HasValue) ManualMy = lp.My.Value.ToString("G6", inv);
            }
            else if (existing.ForceItemId != 0)
            {
               var fi = ForceItems.FirstOrDefault(i => i.Id == existing.ForceItemId);
               if (fi != null)
               {
                  var inv = System.Globalization.CultureInfo.InvariantCulture;
                  ManualN  = fi.N .ToString("G6", inv);
                  ManualMx = fi.Mx.ToString("G6", inv);
                  ManualMy = fi.My.ToString("G6", inv);
               }
            }
         }
         else if (IsLimitKind)
            SolverId = LimitForceParams.Parse(existing.ParamsJson).Solver;

          if (existing.Kind is "two_stage_strain" or "two_stage_strain_batch")
          {
             var tp = TwoStageParams.Parse(existing.ParamsJson);
             var inv = System.Globalization.CultureInfo.InvariantCulture;
             Stage1UseManual = tp.Stage1.Mode == "manual";
             Stage1Set  = ForceSets.FirstOrDefault(f => f.Id == tp.Stage1.ForceSetId);
             Stage1Item = tp.Stage1.Mode == "set"
                ? null
                : Stage1Items.FirstOrDefault(i => i.Id == tp.Stage1.ForceItemId);
             if (Stage1UseManual)
             {
                Stage1ManualN  = tp.Stage1.N .ToString("G6", inv);
                Stage1ManualMx = tp.Stage1.Mx.ToString("G6", inv);
                Stage1ManualMy = tp.Stage1.My.ToString("G6", inv);
             }
             Stage2UseManual = tp.Stage2.Mode == "manual";
             Stage2Set  = ForceSets.FirstOrDefault(f => f.Id == tp.Stage2.ForceSetId);
             Stage2Item = tp.Stage2.Mode == "set"
                ? null
                : Stage2Items.FirstOrDefault(i => i.Id == tp.Stage2.ForceItemId);
             if (Stage2UseManual)
             {
                Stage2ManualN  = tp.Stage2.N .ToString("G6", inv);
                Stage2ManualMx = tp.Stage2.Mx.ToString("G6", inv);
                Stage2ManualMy = tp.Stage2.My.ToString("G6", inv);
             }
          }

          if (existing.Kind.StartsWith("shell_simpl_"))
          {
             var sp = ShellSimplParams.Parse(existing.ParamsJson);
             var inv = System.Globalization.CultureInfo.InvariantCulture;
             ShellSimplNx  = sp.Nx.ToString("G6", inv);
             ShellSimplNy  = sp.Ny.ToString("G6", inv);
             ShellSimplNxy = sp.Nxy.ToString("G6", inv);
             ShellSimplMx  = sp.Mx.ToString("G6", inv);
             ShellSimplMy  = sp.My.ToString("G6", inv);
             ShellSimplMxy = sp.Mxy.ToString("G6", inv);
             ShellSimplStepDeg = sp.StepDeg.ToString("G6", inv);
             ShellSimplAcrcLim = sp.AcrcLimMm.ToString("G6", inv);
             ShellSimplPhi1 = sp.Phi1.ToString("G6", inv);
             ShellSimplPhi2 = sp.Phi2.ToString("G6", inv);
             SelectedShellSimplSection = ShellSimplSections.FirstOrDefault(s => s.Id == existing.SectionId);
             if (existing.ForceSetId != 0)
             {
                 SelectedShellForceSet = ShellForceSets.FirstOrDefault(fs => fs.Id == existing.ForceSetId);
                 if (existing.ForceItemId != 0)
                     SelectedShellForceItem = ShellForceItems.FirstOrDefault(i => i.Id == existing.ForceItemId);
             }
          }
       }
       else
       {
          SelectedKind = AvailableKinds[0];
          SelectedSolver = SolverMethods[0];
          SelectedSection = Sections.FirstOrDefault();
          SelectedFireSection = FireSections.FirstOrDefault();
          SelectedForceSet = ForceSets.FirstOrDefault();
          SelectedCalcType = CalcType.C;
          SelectedShellSimplSection = ShellSimplSections.FirstOrDefault();
          SelectedShellForceSet = ShellForceSets.FirstOrDefault();
          Stage1Set = ForceSets.FirstOrDefault();
          Stage2Set = ForceSets.FirstOrDefault();
       }

      OkCommand = new RelayCommand(_ => Commit());
   }

    static StageForce BuildStageForceManual(string n, string mx, string my)
    {
       var inv = System.Globalization.CultureInfo.InvariantCulture;
       double Parse(string s) =>
          double.TryParse(s, System.Globalization.NumberStyles.Float, inv, out var v) ? v : 0;
       return new StageForce { Mode = "manual", N = Parse(n), Mx = Parse(mx), My = Parse(my) };
    }

    static StageForce BuildStageForce(ForceSet set, LoadItem? item, bool allowSet)
    {
       if (item == null && allowSet)
          return new StageForce { Mode = "set", ForceSetId = set.Id };
       return new StageForce { Mode = "item", ForceSetId = set.Id, ForceItemId = item?.Id ?? 0 };
    }

   void Commit()
   {
      if (IsTwoStage)
      {
         if (SelectedSection == null)
         {
            MessageBox.Show(Loc.S("CalcTaskNeedSection"), Loc.S("Warning"),
               MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
         }

         // Этап 1
         StageForce stage1;
         if (Stage1UseManual)
            stage1 = BuildStageForceManual(Stage1ManualN, Stage1ManualMx, Stage1ManualMy);
         else
         {
            if (Stage1Set == null)
            {
               MessageBox.Show(Loc.S("CalcTaskNeedForceSet"), Loc.S("Warning"),
                  MessageBoxButton.OK, MessageBoxImage.Warning);
               return;
            }
            stage1 = BuildStageForce(Stage1Set, Stage1Item, allowSet: IsTwoStageBatch);
         }

         // Этап 2
         StageForce stage2;
         if (Stage2UseManual)
            stage2 = BuildStageForceManual(Stage2ManualN, Stage2ManualMx, Stage2ManualMy);
         else
         {
            if (Stage2Set == null)
            {
               MessageBox.Show(Loc.S("CalcTaskNeedForceSet"), Loc.S("Warning"),
                  MessageBoxButton.OK, MessageBoxImage.Warning);
               return;
            }
            stage2 = BuildStageForce(Stage2Set, Stage2Item, allowSet: IsTwoStageBatch);
         }

         if (IsTwoStageBatch)
         {
            // Этап 2 — всегда весь набор; попарно — равное число строк.
            // Ручной ввод этапа 2 в пакетном режиме трактуется как одно усилие
            // для всех строк (κ1 считается один раз — в Batch-хендлере).
            if (!Stage2UseManual)
               stage2 = new StageForce { Mode = "set", ForceSetId = Stage2Set!.Id };
            if (stage1.Mode == "set" && stage2.Mode == "set"
                && Stage1Set!.Items.Count != Stage2Set!.Items.Count)
            {
               MessageBox.Show(Loc.S("TwoStageNeedEqualRows"), Loc.S("Warning"),
                  MessageBoxButton.OK, MessageBoxImage.Warning);
               return;
            }
         }
         else if (!Stage1UseManual && !Stage2UseManual
                  && (Stage1Item == null || Stage2Item == null))
         {
            // Детальная: при выборе из набора оба этапа — конкретная строка
            MessageBox.Show(Loc.S("CalcTaskNeedForceItem"), Loc.S("Warning"),
               MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
         }

         Result = new CalcTask
         {
             Tag = string.IsNullOrWhiteSpace(Tag) ? $"Задача {_app.CalcTasks.Count + 1}" : Tag,
             Kind = Kind,
             SectionId = SelectedSection.Id,
             ForceSetId = 0,
             ForceItemId = 0,
            CalcType = SelectedCalcType,
            ParamsJson = new TwoStageParams { Stage1 = stage1, Stage2 = stage2 }.ToJson()
         };
         _window.DialogResult = true;
         return;
      }

      if (IsShellSimpl)
      {
          if (SelectedShellSimplSection == null)
          {
             MessageBox.Show(Loc.S("CalcTaskNeedSection"), Loc.S("Warning"),
                MessageBoxButton.OK, MessageBoxImage.Warning);
             return;
          }

          var inv = System.Globalization.CultureInfo.InvariantCulture;
          double nx  = double.TryParse(ShellSimplNx,  System.Globalization.NumberStyles.Float, inv, out var nxv)  ? nxv : 0;
          double ny  = double.TryParse(ShellSimplNy,  System.Globalization.NumberStyles.Float, inv, out var nyv)  ? nyv : 0;
          double nxy = double.TryParse(ShellSimplNxy, System.Globalization.NumberStyles.Float, inv, out var nxyv) ? nxyv : 0;
          double mx  = double.TryParse(ShellSimplMx,  System.Globalization.NumberStyles.Float, inv, out var mxv)  ? mxv : 0;
          double my  = double.TryParse(ShellSimplMy,  System.Globalization.NumberStyles.Float, inv, out var myv)  ? myv : 0;
          double mxy = double.TryParse(ShellSimplMxy, System.Globalization.NumberStyles.Float, inv, out var mxyv) ? mxyv : 0;
          double stepDeg  = double.TryParse(ShellSimplStepDeg, System.Globalization.NumberStyles.Float, inv, out var sdv) ? sdv : 10.0;
          double acrcLim  = double.TryParse(ShellSimplAcrcLim, System.Globalization.NumberStyles.Float, inv, out var alv) ? alv : 0.3;
          double phi1     = double.TryParse(ShellSimplPhi1,    System.Globalization.NumberStyles.Float, inv, out var p1v) ? p1v : 1.0;
          double phi2     = double.TryParse(ShellSimplPhi2,    System.Globalization.NumberStyles.Float, inv, out var p2v) ? p2v : 0.5;

          Result = new CalcTask
          {
              Tag = string.IsNullOrWhiteSpace(Tag) ? $"Задача {_app.CalcTasks.Count + 1}" : Tag,
              Kind = Kind,
              SectionId = SelectedShellSimplSection.Id,
              ForceSetId  = SelectedShellForceSet?.Id ?? 0,
              ForceItemId = SelectedShellForceItem?.Id ?? 0,
              CalcType = SelectedCalcType,
              ParamsJson = JsonSerializer.Serialize(new ShellSimplParams
              {
                  Nx = nx, Ny = ny, Nxy = nxy,
                  Mx = mx, My = my, Mxy = mxy,
                  StepDeg = stepDeg, AcrcLimMm = acrcLim,
                  Phi1 = phi1, Phi2 = phi2
              })
          };
          _window.DialogResult = true;
          return;
      }

      if (IsFireKind)
      {
         if (SelectedFireSection == null)
         {
            MessageBox.Show(Loc.S("CalcTaskNeedFireSection"), Loc.S("Warning"),
               MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
         }
          SelectedSection = _allSections.FirstOrDefault(s => s.Id == SelectedFireSection.SectionId);
       }

       if (SelectedSection == null)
       {
          MessageBox.Show(Loc.S("CalcTaskNeedSection"), Loc.S("Warning"),
             MessageBoxButton.OK, MessageBoxImage.Warning);
          return;
       }

       if (IsLimitBatch && SelectedForceSet == null)
       {
          MessageBox.Show(Loc.S("CalcTaskNeedForceSet"), Loc.S("Warning"),
             MessageBoxButton.OK, MessageBoxImage.Warning);
          return;
       }

       if (!ShowManualForces && !IsLimitBatch && ShowForceItem && (SelectedForceSet == null || SelectedForceItem == null))
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
          if (IsLimitSingle)
          {
             paramsJson = new LimitForceParams
             {
                Solver = SolverId,
                N = n, Mx = mx, My = my
             }.ToJson();
          }
          else
             paramsJson = JsonSerializer.Serialize(new { N = n, Mx = mx, My = my });
       }
       else if (IsLimitKind)
       {
          paramsJson = new LimitForceParams { Solver = SolverId }.ToJson();
       }

      Result = new CalcTask
      {
         Tag = string.IsNullOrWhiteSpace(Tag) ? $"Задача {_app.CalcTasks.Count + 1}" : Tag,
         Kind = Kind,
         SectionId = SelectedSection.Id,
         // Для strain_state и limit_* (одиночных) силы в ParamsJson — ForceItemId не используется
         ForceSetId  = ShowManualForces ? 0 : (SelectedForceSet?.Id ?? 0),
         ForceItemId = ShowManualForces ? 0 : (ShowForceItem ? (SelectedForceItem?.Id ?? 0) : 0),
         CalcType = SelectedCalcType,
         ParamsJson = paramsJson
      };

      _window.DialogResult = true;
   }
}
