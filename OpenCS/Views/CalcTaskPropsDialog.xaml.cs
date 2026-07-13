using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Data;
using System.Windows.Input;
using CScore;
using CScore.Fire.Entities;
using CSfea.Torsion;
using OpenCS.Tasks;
using OpenCS.Utilites;
using OpenCS.ViewModels;

namespace OpenCS.Views;

public partial class CalcTaskPropsDialog : Window
{
   public CalcTaskPropsDialog(AppViewModel app, CalcTask? existing = null, string? groupKey = null)
   {
      InitializeComponent();
      DataContext = new CalcTaskPropsDlgVM(app, existing, this, groupKey);
   }

   public CalcTask? Result => (DataContext as CalcTaskPropsDlgVM)?.Result;
}

public class CalcTaskKindItem
{
   public string Id       { get; init; } = "";
   public string Label    { get; init; } = "";
   public string GroupKey { get; init; } = "other";
   public string Group    { get; init; } = "";
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
    // Eta (п. 8.1.15 СП63.13330)
    bool etaEnabled, etaIterative;
    string etaL = "6.0", etaMuX = "1.0", etaMuY = "1.0", etaPsiX = "1.0", etaPsiY = "1.0";
    string etaSlendernessThreshold = "14";
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
    // Crack width
    string crackWidthAcrcUltLong = "0.3";
    string crackWidthAcrcUltShort = "0.4";
    string crackWidthForcesMode = "total_only";
    string crackWidthLongShare = "0.7";
    string crackWidthManualNLong = "0";
    string crackWidthManualMxLong = "0";
    string crackWidthManualMyLong = "0";
    ForceSet? crackWidthLongForceSet;
    LoadItem? crackWidthLongForceItem;
    string _crackWidthLongForceItemFilter = "";
   ForceSet? stage1Set, stage2Set;
   LoadItem? stage1Item, stage2Item;
   bool stage1UseManual, stage2UseManual;
   string stage1ManualN = "0", stage1ManualMx = "0", stage1ManualMy = "0";
   string stage2ManualN = "0", stage2ManualMx = "0", stage2ManualMy = "0";
   // Steel check
   string steelDesignLengthX = "3.0", steelDesignLengthY = "3.0";
   string steelMuX = "1.0", steelMuY = "1.0";
   string steelBetaM = "1.0", steelGammaM = "1.025";
    string torsionElementSize = "0.05", torsionMk = "";
    string torsionAutoH0 = "0.05";
    string torsionAutoRuns = "3";
    double _lastTorsionLmin = double.NaN;
    bool _torsionH0UserOverride;
    int _torsionTriangulationIndex;
    int _torsionFemOrderIndex;
    bool torsionAutoConverge;
   string _forceItemFilter = "", _stage1ItemFilter = "", _stage2ItemFilter = "", _shellForceItemFilter = "";
   CancellationTokenSource? _torsionPreviewDebounceCts;

   public string Tag { get => tag; set { tag = value; OnPropertyChanged(); } }

   public string ManualN  { get => manualN;  set { manualN  = value; OnPropertyChanged(); } }
   public string ManualMx { get => manualMx; set { manualMx = value; OnPropertyChanged(); } }
   public string ManualMy { get => manualMy; set { manualMy = value; OnPropertyChanged(); } }

   public bool IsStrainState => Kind == "strain_state";

   /// <summary>
   /// Виды задач, поддерживающие блок η (п. 8.1.15): состояние деформаций
   /// (N фиксирован по определению) и поиск предельного момента при
   /// фиксированном N (limit_moment) — в обоих случаях N не меняется в ходе
   /// решения, что позволяет пересчитывать η без риска потери устойчивости
   /// поиска. limit_force/limit_axial (N — искомая величина) пока не
   /// поддерживаются — см. RodEtaWiring/LimitForceSolver.MomentFactor.
   /// </summary>
   public bool SupportsEta => Kind is "strain_state" or "strain_state_batch"
      or "limit_moment" or "limit_moment_batch"
      or "crack_width" or "crack_width_batch";

   public bool EtaEnabled
   {
      get => etaEnabled;
      set
      {
         etaEnabled = value;
         OnPropertyChanged();
         OnPropertyChanged(nameof(ShowEtaFields));
         OnPropertyChanged(nameof(ShowEtaFormulaFields));
         OnPropertyChanged(nameof(ShowEtaPsiFields));
         OnPropertyChanged(nameof(ShowEtaAutoPsiHint));
      }
   }

   public bool EtaIterative
   {
      get => etaIterative;
      set
      {
         etaIterative = value;
         OnPropertyChanged();
         OnPropertyChanged(nameof(ShowEtaFormulaFields));
         OnPropertyChanged(nameof(ShowEtaPsiFields));
      }
   }

   public string EtaL    { get => etaL;    set { etaL    = value; OnPropertyChanged(); } }
   public string EtaMuX  { get => etaMuX;  set { etaMuX  = value; OnPropertyChanged(); } }
   public string EtaMuY  { get => etaMuY;  set { etaMuY  = value; OnPropertyChanged(); } }
   public string EtaPsiX { get => etaPsiX; set { etaPsiX = value; OnPropertyChanged(); } }
   public string EtaPsiY { get => etaPsiY; set { etaPsiY = value; OnPropertyChanged(); } }
   public string EtaSlendernessThreshold { get => etaSlendernessThreshold; set { etaSlendernessThreshold = value; OnPropertyChanged(); } }

   /// <summary>Показывать блок η целиком — для strain_state и strain_state_batch при включённой галке.</summary>
   public bool ShowEtaFields => SupportsEta && EtaEnabled;

   /// <summary>Поля ψ (доля длительности момента) — только для буквального (формульного) режима.</summary>
   public bool ShowEtaFormulaFields => ShowEtaFields && !EtaIterative;

   /// <summary>ψx/ψy вручную — не для трещин (там авто из M_long/M_total).</summary>
   public bool ShowEtaPsiFields => ShowEtaFormulaFields && !IsCrackWidthAny;

   /// <summary>Подсказка про авто-ψ для задач ширины трещин.</summary>
   public bool ShowEtaAutoPsiHint => SupportsEta && IsCrackWidthAny;

   public bool IsLimitSingle  => IsLimitSingleKind(Kind);
   public bool ShowManualForces => Kind == "strain_state" || IsLimitSingle || IsSteelCheck || IsCracking || IsCrackWidth;

   static bool IsLimitSingleKind(string kind)
      => kind is "limit_force" or "limit_moment" or "limit_axial";

   static bool Pass(string filter, string? label) =>
      string.IsNullOrEmpty(filter) ||
      (label ?? "").Contains(filter, StringComparison.OrdinalIgnoreCase);

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
           OnPropertyChanged(nameof(IsShellSimplBatch));
           OnPropertyChanged(nameof(IsShellStrain));
           OnPropertyChanged(nameof(IsShellStrainBatch));
           OnPropertyChanged(nameof(IsShellLayered));
           OnPropertyChanged(nameof(IsShellLayeredBatch));
           OnPropertyChanged(nameof(IsPlatePanel));
           OnPropertyChanged(nameof(IsPlateBatch));
           OnPropertyChanged(nameof(FilteredCalcTypes));
           OnPropertyChanged(nameof(ShowStandardForce));
           OnPropertyChanged(nameof(Stage1ShowSet));
           OnPropertyChanged(nameof(Stage1ShowManual));
           OnPropertyChanged(nameof(Stage2ShowSet));
           OnPropertyChanged(nameof(Stage2ShowManual));
         OnPropertyChanged(nameof(IsPrestressLoss));
         OnPropertyChanged(nameof(IsSteelCheck));
         OnPropertyChanged(nameof(IsCracking));
         OnPropertyChanged(nameof(IsCrackingBatch));
         OnPropertyChanged(nameof(IsCrackWidth));
         OnPropertyChanged(nameof(IsCrackWidthBatch));
         OnPropertyChanged(nameof(IsCrackWidthAny));
         OnPropertyChanged(nameof(CrackWidthForcesModeItems));
         if (CrackWidthForcesModeItems.TrueForAll(i => i.Id != CrackWidthForcesMode))
            CrackWidthForcesMode = "total_only";
         OnPropertyChanged(nameof(ShowCrackWidthShare));
         OnPropertyChanged(nameof(ShowCrackWidthManual));
         OnPropertyChanged(nameof(ShowCrackWidthForceItemLong));
         OnPropertyChanged(nameof(ShowCrackWidthTwoSets));
         OnPropertyChanged(nameof(IsTorsion));
         OnPropertyChanged(nameof(IsTorsionFem));
         OnPropertyChanged(nameof(ShowManualForces));
         OnPropertyChanged(nameof(IsStrainState));
         OnPropertyChanged(nameof(SupportsEta));
         OnPropertyChanged(nameof(ShowEtaFields));
         OnPropertyChanged(nameof(ShowEtaFormulaFields));
         OnPropertyChanged(nameof(ShowEtaPsiFields));
         OnPropertyChanged(nameof(ShowEtaAutoPsiHint));
         NotifyTorsionForceProps();
         RefreshTorsionMeshPreview();
         RefreshTorsionLmin();
         OnPropertyChanged(nameof(ShowTorsionAutoParams));
         OnPropertyChanged(nameof(ShowTorsionElementSize));
         OnPropertyChanged(nameof(ShowTorsionAutoRunsWarn));
         if (!FilteredCalcTypes.Contains(SelectedCalcType))
             SelectedCalcType = FilteredCalcTypes[0];
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
           OnPropertyChanged(nameof(IsShellSimplBatch));
           OnPropertyChanged(nameof(IsShellStrain));
           OnPropertyChanged(nameof(IsShellStrainBatch));
           OnPropertyChanged(nameof(IsShellLayered));
           OnPropertyChanged(nameof(IsShellLayeredBatch));
           OnPropertyChanged(nameof(IsPlatePanel));
           OnPropertyChanged(nameof(IsPlateBatch));
           OnPropertyChanged(nameof(FilteredCalcTypes));
           OnPropertyChanged(nameof(ShowStandardForce));
           OnPropertyChanged(nameof(Stage1ShowSet));
           OnPropertyChanged(nameof(Stage1ShowManual));
           OnPropertyChanged(nameof(Stage2ShowSet));
           OnPropertyChanged(nameof(Stage2ShowManual));
            OnPropertyChanged(nameof(IsPrestressLoss));
            OnPropertyChanged(nameof(IsSteelCheck));
            OnPropertyChanged(nameof(IsCracking));
            OnPropertyChanged(nameof(IsCrackingBatch));
            OnPropertyChanged(nameof(IsCrackWidth));
            OnPropertyChanged(nameof(IsCrackWidthBatch));
            OnPropertyChanged(nameof(IsCrackWidthAny));
            OnPropertyChanged(nameof(CrackWidthForcesModeItems));
            if (CrackWidthForcesModeItems.TrueForAll(i => i.Id != CrackWidthForcesMode))
               CrackWidthForcesMode = "total_only";
            OnPropertyChanged(nameof(ShowCrackWidthShare));
            OnPropertyChanged(nameof(ShowCrackWidthManual));
            OnPropertyChanged(nameof(ShowCrackWidthForceItemLong));
            OnPropertyChanged(nameof(ShowCrackWidthTwoSets));
            OnPropertyChanged(nameof(IsTorsion));
            OnPropertyChanged(nameof(IsTorsionFem));
            OnPropertyChanged(nameof(ShowManualForces));
            OnPropertyChanged(nameof(IsStrainState));
            OnPropertyChanged(nameof(ShowEtaFields));
            OnPropertyChanged(nameof(ShowEtaFormulaFields));
            OnPropertyChanged(nameof(ShowEtaPsiFields));
            OnPropertyChanged(nameof(ShowEtaAutoPsiHint));
            NotifyTorsionForceProps();
            if (!FilteredCalcTypes.Contains(SelectedCalcType))
                SelectedCalcType = FilteredCalcTypes[0];
            FilterSections();
            RefreshTorsionMeshPreview();
            RefreshTorsionLmin();
            OnPropertyChanged(nameof(ShowTorsionAutoParams));
            OnPropertyChanged(nameof(ShowTorsionElementSize));
            OnPropertyChanged(nameof(ShowTorsionAutoRunsWarn));
        }
     }

    public bool IsFireKind    => Kind.StartsWith("fire_", StringComparison.Ordinal);
   public bool IsStrainBatch => Kind == "strain_state_batch" || Kind == "strength_ndm_batch";
   public bool IsLimitBatch  => Kind is "limit_force_batch" or "limit_moment_batch" or "limit_axial_batch";
   public bool IsLimitKind   => Kind.StartsWith("limit_", StringComparison.Ordinal);
   public bool IsShellSimpl      => Kind.StartsWith("shell_simpl_", StringComparison.Ordinal);
   public bool IsShellSimplCapri => Kind is "shell_simpl_capri_sls" or "shell_simpl_capri_uls"
        or "shell_simpl_capri_sls_batch" or "shell_simpl_capri_uls_batch";
   public bool IsShellSimplSls   => Kind is "shell_simpl_wa_sls" or "shell_simpl_capri_sls"
        or "shell_simpl_wa_sls_batch" or "shell_simpl_capri_sls_batch";
   public bool IsShellSimplBatch => IsShellSimpl && Kind.EndsWith("_batch", StringComparison.Ordinal);
   public bool IsShellStrain      => Kind is "shell_strain_state" or "shell_strain_state_batch";
   public bool IsShellStrainBatch => Kind == "shell_strain_state_batch";
   public bool IsShellLayered      => Kind is "shell_layered_uls" or "shell_layered_uls_batch";
   public bool IsShellLayeredBatch => Kind == "shell_layered_uls_batch";
   /// <summary>Панель пластины (выбор PlateSection + усилия) — для simpl, strain и layered.</summary>
   public bool IsPlatePanel => IsShellSimpl || IsShellStrain || IsShellLayered;
   /// <summary>Пакетный режим плитной задачи (simpl, strain или layered).</summary>
   public bool IsPlateBatch => IsShellSimplBatch || IsShellStrainBatch || IsShellLayeredBatch;
   public bool IsTwoStage      => Kind is "two_stage_strain" or "two_stage_strain_batch";
   public bool IsTwoStageBatch => Kind == "two_stage_strain_batch";
   public bool IsPrestressLoss => Kind == "prestress_loss";
    public bool IsSteelCheck => Kind is "steel_check" or
        "steel_central_compression" or "steel_central_tension" or
        "steel_bending" or "steel_compression_bending" or
        "steel_tension_bending" or "steel_shear" or
        "steel_torsion" or "steel_constructive";
   public bool IsTorsion => Kind is "torsion_bem" or "torsion_fem";
   public bool IsTorsionFem => Kind == "torsion_fem";
   public TorsionMeshPreviewVM TorsionMeshPreview { get; } = new();
   public bool IsCracking      => Kind == "cracking";
   public bool IsCrackingBatch => Kind == "cracking_batch";
   public bool IsCrackWidth      => Kind == "crack_width";
   public bool IsCrackWidthBatch => Kind == "crack_width_batch";
   public bool IsCrackWidthAny   => IsCrackWidth || IsCrackWidthBatch;
   public bool ShowForceItem => !IsStrainBatch && !IsLimitBatch && !IsFireKind && !IsTwoStage && !IsPlatePanel && !IsPrestressLoss
      && !IsCrackingBatch && !IsCrackWidthBatch;
   public bool ShowSolverMethod => IsLimitKind;

   /// <summary>Показывать стандартный одиночный выбор набора усилий (скрыт для two-stage и потерь).</summary>
   public bool ShowStandardForce => !IsTwoStage && !IsPlatePanel && !IsPrestressLoss;

   void FilterSections()
   {
      Sections.Clear();
      IEnumerable<CrossSection> filtered;
      if (IsSteelCheck)
         filtered = _allSections.Where(s => s.Areas.Any(a =>
            a.Material?.Type is MatType.Steel or MatType.Custom));
      else if (IsTwoStage)
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
      set { selectedSection = value; OnPropertyChanged(); RefreshTorsionMeshPreview(); RefreshTorsionLmin(); }
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
           ShellForceItemFilter = "";
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
   public ListCollectionView ShellForceItemsView { get; private set; } = null!;
   public string ShellForceItemFilter
   {
      get => _shellForceItemFilter;
      set { _shellForceItemFilter = value; OnPropertyChanged(); ShellForceItemsView?.Refresh(); }
   }
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

   public string CrackWidthAcrcUltLong  { get => crackWidthAcrcUltLong;  set { crackWidthAcrcUltLong  = value; OnPropertyChanged(); } }
   public string CrackWidthAcrcUltShort { get => crackWidthAcrcUltShort; set { crackWidthAcrcUltShort = value; OnPropertyChanged(); } }
   public string CrackWidthLongShare    { get => crackWidthLongShare;    set { crackWidthLongShare    = value; OnPropertyChanged(); } }
   public string CrackWidthManualNLong  { get => crackWidthManualNLong;  set { crackWidthManualNLong  = value; OnPropertyChanged(); } }
   public string CrackWidthManualMxLong { get => crackWidthManualMxLong; set { crackWidthManualMxLong = value; OnPropertyChanged(); } }
   public string CrackWidthManualMyLong { get => crackWidthManualMyLong; set { crackWidthManualMyLong = value; OnPropertyChanged(); } }

   public string CrackWidthForcesMode
   {
      get => crackWidthForcesMode;
      set
      {
         crackWidthForcesMode = value;
         OnPropertyChanged();
         OnPropertyChanged(nameof(ShowCrackWidthShare));
         OnPropertyChanged(nameof(ShowCrackWidthManual));
         OnPropertyChanged(nameof(ShowCrackWidthForceItemLong));
         OnPropertyChanged(nameof(ShowCrackWidthTwoSets));
      }
   }

   /// <summary>Режимы длительной нагрузки (Id в ParamsJson, Label через локализацию).</summary>
   public List<CalcTaskSolverItem> CrackWidthForcesModeItems => IsCrackWidthBatch
      ? [
           new() { Id = "total_only", Label = Loc.S("CrackWidth_Mode_total_only") },
           new() { Id = "share",      Label = Loc.S("CrackWidth_Mode_share") },
           new() { Id = "two_sets",   Label = Loc.S("CrackWidth_Mode_two_sets") },
        ]
      : [
           new() { Id = "total_only",       Label = Loc.S("CrackWidth_Mode_total_only") },
           new() { Id = "share",            Label = Loc.S("CrackWidth_Mode_share") },
           new() { Id = "manual",           Label = Loc.S("CrackWidth_Mode_manual") },
           new() { Id = "force_item_long",  Label = Loc.S("CrackWidth_Mode_force_item_long") },
        ];

   public bool ShowCrackWidthShare         => CrackWidthForcesMode == "share";
   public bool ShowCrackWidthManual        => IsCrackWidth && CrackWidthForcesMode == "manual";
   public bool ShowCrackWidthForceItemLong => IsCrackWidth && CrackWidthForcesMode == "force_item_long";
   public bool ShowCrackWidthTwoSets       => IsCrackWidthBatch && CrackWidthForcesMode == "two_sets";

   // Переиспользует существующую публичную коллекцию ForceSets (тот же список,
   // что и у стандартного picker'а) — отдельная коллекция не нужна.
   public ForceSet? SelectedCrackWidthLongForceSet
   {
      get => crackWidthLongForceSet;
      set
      {
         crackWidthLongForceSet = value;
         CrackWidthLongForceItems.Clear();
         if (value != null)
            foreach (var it in value.Items) CrackWidthLongForceItems.Add(it);
         SelectedCrackWidthLongForceItem = CrackWidthLongForceItems.FirstOrDefault();
         OnPropertyChanged();
      }
   }

   public ObservableCollection<LoadItem> CrackWidthLongForceItems { get; } = [];
   public ListCollectionView CrackWidthLongForceItemsView { get; private set; } = null!;
   public string CrackWidthLongForceItemFilter
   {
      get => _crackWidthLongForceItemFilter;
      set { _crackWidthLongForceItemFilter = value; OnPropertyChanged(); CrackWidthLongForceItemsView?.Refresh(); }
   }
   public LoadItem? SelectedCrackWidthLongForceItem
   {
      get => crackWidthLongForceItem;
      set { crackWidthLongForceItem = value; OnPropertyChanged(); }
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
         ForceItemFilter = "";
         SelectedForceItem = ForceItems.FirstOrDefault();
         OnPropertyChanged();
         NotifyTorsionForceProps();
      }
   }

   void NotifyTorsionForceProps()
   {
      if (!IsTorsion) return;
      OnPropertyChanged(nameof(TorsionMkFromSetText));
      OnPropertyChanged(nameof(HasTorsionMkFromSet));
      OnPropertyChanged(nameof(ShowTorsionManualMk));
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
         NotifyTorsionForceProps();
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
   public ListCollectionView Stage1ItemsView { get; private set; } = null!;
   public string Stage1ItemFilter
   {
      get => _stage1ItemFilter;
      set { _stage1ItemFilter = value; OnPropertyChanged(); Stage1ItemsView?.Refresh(); }
   }
   public ObservableCollection<LoadItem> Stage2Items { get; } = [];
   public ListCollectionView Stage2ItemsView { get; private set; } = null!;
   public string Stage2ItemFilter
   {
      get => _stage2ItemFilter;
      set { _stage2ItemFilter = value; OnPropertyChanged(); Stage2ItemsView?.Refresh(); }
   }

    public ForceSet? Stage1Set
    {
       get => stage1Set;
       set
       {
          stage1Set = value;
          Stage1Items.Clear();
          if (value != null) foreach (var it in value.Items) Stage1Items.Add(it);
          Stage1ItemFilter = "";
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
          Stage2ItemFilter = "";
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

    // Steel check parameters
    public string SteelDesignLengthX { get => steelDesignLengthX; set { steelDesignLengthX = value; OnPropertyChanged(); } }
    public string SteelDesignLengthY { get => steelDesignLengthY; set { steelDesignLengthY = value; OnPropertyChanged(); } }
    public string SteelMuX { get => steelMuX; set { steelMuX = value; OnPropertyChanged(); } }
    public string SteelMuY { get => steelMuY; set { steelMuY = value; OnPropertyChanged(); } }
    public string SteelBetaM { get => steelBetaM; set { steelBetaM = value; OnPropertyChanged(); } }
    public string SteelGammaM { get => steelGammaM; set { steelGammaM = value; OnPropertyChanged(); } }

   public string TorsionElementSize
   {
      get => torsionElementSize;
      set { torsionElementSize = value; OnPropertyChanged(); RefreshTorsionMeshPreview(); }
   }
    public string TorsionMk { get => torsionMk; set { torsionMk = value; OnPropertyChanged(); } }

    public string TorsionAutoH0
    {
       get => torsionAutoH0;
       set
       {
          if (torsionAutoH0 == value) return;
          torsionAutoH0 = value;
          _torsionH0UserOverride = true;
          OnPropertyChanged();
       }
    }

    public string TorsionAutoRuns
    {
       get => torsionAutoRuns;
       set
       {
          torsionAutoRuns = value;
          OnPropertyChanged();
          OnPropertyChanged(nameof(ShowTorsionAutoRunsWarn));
       }
    }

    public string TorsionLminText { get; private set; } = "—";

    public bool ShowTorsionAutoParams => IsTorsion && TorsionAutoConverge;

    public bool ShowTorsionAutoRunsWarn =>
       ShowTorsionAutoParams
       && int.TryParse((TorsionAutoRuns ?? "").Trim(), out var n)
       && n >= 5;

    public int TorsionTriangulationIndex
    {
       get => _torsionTriangulationIndex;
       set { _torsionTriangulationIndex = value; OnPropertyChanged(); RefreshTorsionMeshPreview(); }
    }

    public int TorsionFemOrderIndex
    {
       get => _torsionFemOrderIndex;
       set { _torsionFemOrderIndex = value; OnPropertyChanged(); }
    }

    public bool TorsionAutoConverge
    {
       get => torsionAutoConverge;
       set
       {
          torsionAutoConverge = value;
          OnPropertyChanged();
          OnPropertyChanged(nameof(ShowTorsionElementSize));
          OnPropertyChanged(nameof(ShowTorsionAutoParams));
          OnPropertyChanged(nameof(ShowTorsionAutoRunsWarn));
          RefreshTorsionMeshPreview();
       }
    }

    /// <summary>Поле "размер элемента" скрывается, когда включена автосходимость.</summary>
    public bool ShowTorsionElementSize => IsTorsion && !TorsionAutoConverge;

   /// <summary>T (кручение) из выбранной строки набора усилий, кН·м.</summary>
   public string TorsionMkFromSetText
   {
      get
      {
         if (selectedForceItem == null) return "—";
         var inv = System.Globalization.CultureInfo.InvariantCulture;
         return Math.Abs(selectedForceItem.T).ToString("G6", inv);
      }
   }

   public bool HasTorsionMkFromSet => IsTorsion && selectedForceItem != null;
   public bool ShowTorsionManualMk => IsTorsion && selectedForceItem == null;

   public List<CalcTaskKindItem> AvailableKinds { get; } =
   [
      // НДС
      new() { Id = "strain_state",             Label = Loc.S("CalcTaskKind_strain_state"),             GroupKey = "nds",   Group = Loc.S("CalcTaskGroupNds") },
      new() { Id = "strain_state_batch",       Label = Loc.S("CalcTaskKind_strain_state_batch"),       GroupKey = "nds",   Group = Loc.S("CalcTaskGroupNds") },
      new() { Id = "two_stage_strain",         Label = Loc.S("CalcTaskKind_two_stage_strain"),         GroupKey = "nds",   Group = Loc.S("CalcTaskGroupNds") },
      new() { Id = "two_stage_strain_batch",   Label = Loc.S("CalcTaskKind_two_stage_strain_batch"),   GroupKey = "nds",   Group = Loc.S("CalcTaskGroupNds") },
      new() { Id = "shell_strain_state",       Label = Loc.S("CalcTaskKind_shell_strain_state"),       GroupKey = "nds",   Group = Loc.S("CalcTaskGroupNds") },
      new() { Id = "shell_strain_state_batch", Label = Loc.S("CalcTaskKind_shell_strain_state_batch"), GroupKey = "nds",   Group = Loc.S("CalcTaskGroupNds") },
      // 1-я ГПС
      new() { Id = "limit_force",              Label = Loc.S("CalcTaskKind_limit_force"),              GroupKey = "uls",   Group = Loc.S("CalcTaskGroupUls") },
      new() { Id = "limit_force_batch",        Label = Loc.S("CalcTaskKind_limit_force_batch"),        GroupKey = "uls",   Group = Loc.S("CalcTaskGroupUls") },
      new() { Id = "limit_moment",             Label = Loc.S("CalcTaskKind_limit_moment"),             GroupKey = "uls",   Group = Loc.S("CalcTaskGroupUls") },
      new() { Id = "limit_moment_batch",       Label = Loc.S("CalcTaskKind_limit_moment_batch"),       GroupKey = "uls",   Group = Loc.S("CalcTaskGroupUls") },
      new() { Id = "limit_axial",              Label = Loc.S("CalcTaskKind_limit_axial"),              GroupKey = "uls",   Group = Loc.S("CalcTaskGroupUls") },
      new() { Id = "limit_axial_batch",        Label = Loc.S("CalcTaskKind_limit_axial_batch"),        GroupKey = "uls",   Group = Loc.S("CalcTaskGroupUls") },
      new() { Id = "strength_ndm_batch",       Label = Loc.S("CalcTaskKind_strength_ndm_batch"),       GroupKey = "uls",   Group = Loc.S("CalcTaskGroupUls") },
      new() { Id = "shell_simpl_wa_uls",       Label = Loc.S("CalcTaskKind_shell_simpl_wa_uls"),       GroupKey = "uls",   Group = Loc.S("CalcTaskGroupUls") },
      new() { Id = "shell_simpl_wa_uls_batch", Label = Loc.S("CalcTaskKind_shell_simpl_wa_uls_batch"), GroupKey = "uls",   Group = Loc.S("CalcTaskGroupUls") },
      new() { Id = "shell_simpl_capri_uls",    Label = Loc.S("CalcTaskKind_shell_simpl_capri_uls"),    GroupKey = "uls",   Group = Loc.S("CalcTaskGroupUls") },
      new() { Id = "shell_simpl_capri_uls_batch", Label = Loc.S("CalcTaskKind_shell_simpl_capri_uls_batch"), GroupKey = "uls", Group = Loc.S("CalcTaskGroupUls") },
      new() { Id = "shell_layered_uls",       Label = Loc.S("CalcTaskKind_shell_layered_uls"),       GroupKey = "uls",   Group = Loc.S("CalcTaskGroupUls") },
      new() { Id = "shell_layered_uls_batch", Label = Loc.S("CalcTaskKind_shell_layered_uls_batch"), GroupKey = "uls",   Group = Loc.S("CalcTaskGroupUls") },
       new() { Id = "steel_check",              Label = Loc.S("CalcTaskKind_steel_check"),              GroupKey = "uls",   Group = Loc.S("CalcTaskGroupUls") },
       new() { Id = "steel_central_compression",Label = Loc.S("CalcTaskKind_steel_central_compression"),GroupKey = "uls",   Group = Loc.S("CalcTaskGroupUls") },
       new() { Id = "steel_central_tension",    Label = Loc.S("CalcTaskKind_steel_central_tension"),    GroupKey = "uls",   Group = Loc.S("CalcTaskGroupUls") },
       new() { Id = "steel_bending",            Label = Loc.S("CalcTaskKind_steel_bending"),            GroupKey = "uls",   Group = Loc.S("CalcTaskGroupUls") },
       new() { Id = "steel_compression_bending",Label = Loc.S("CalcTaskKind_steel_compression_bending"),GroupKey = "uls",   Group = Loc.S("CalcTaskGroupUls") },
       new() { Id = "steel_tension_bending",    Label = Loc.S("CalcTaskKind_steel_tension_bending"),    GroupKey = "uls",   Group = Loc.S("CalcTaskGroupUls") },
       new() { Id = "steel_shear",              Label = Loc.S("CalcTaskKind_steel_shear"),              GroupKey = "uls",   Group = Loc.S("CalcTaskGroupUls") },
       new() { Id = "steel_torsion",            Label = Loc.S("CalcTaskKind_steel_torsion"),            GroupKey = "uls",   Group = Loc.S("CalcTaskGroupUls") },
       new() { Id = "steel_constructive",       Label = Loc.S("CalcTaskKind_steel_constructive"),       GroupKey = "other", Group = Loc.S("CalcTaskGroupOther") },
      // Прочее
      new() { Id = "torsion_bem",              Label = Loc.S("CalcTaskKind_torsion_bem"),              GroupKey = "other", Group = Loc.S("CalcTaskGroupOther") },
      new() { Id = "torsion_fem",              Label = Loc.S("CalcTaskKind_torsion_fem"),              GroupKey = "other", Group = Loc.S("CalcTaskGroupOther") },
      // 2-я ГПС
      new() { Id = "shell_simpl_wa_sls",       Label = Loc.S("CalcTaskKind_shell_simpl_wa_sls"),       GroupKey = "sls",   Group = Loc.S("CalcTaskGroupSls") },
      new() { Id = "shell_simpl_wa_sls_batch", Label = Loc.S("CalcTaskKind_shell_simpl_wa_sls_batch"), GroupKey = "sls",   Group = Loc.S("CalcTaskGroupSls") },
      new() { Id = "shell_simpl_capri_sls",    Label = Loc.S("CalcTaskKind_shell_simpl_capri_sls"),    GroupKey = "sls",   Group = Loc.S("CalcTaskGroupSls") },
      new() { Id = "shell_simpl_capri_sls_batch", Label = Loc.S("CalcTaskKind_shell_simpl_capri_sls_batch"), GroupKey = "sls", Group = Loc.S("CalcTaskGroupSls") },
      new() { Id = "cracking",           Label = Loc.S("CalcTaskKind_cracking"),           GroupKey = "sls", Group = Loc.S("CalcTaskGroupSls") },
      new() { Id = "cracking_batch",     Label = Loc.S("CalcTaskKind_cracking_batch"),     GroupKey = "sls", Group = Loc.S("CalcTaskGroupSls") },
      new() { Id = "crack_width",        Label = Loc.S("CalcTaskKind_crack_width"),        GroupKey = "sls", Group = Loc.S("CalcTaskGroupSls") },
      new() { Id = "crack_width_batch",  Label = Loc.S("CalcTaskKind_crack_width_batch"),  GroupKey = "sls", Group = Loc.S("CalcTaskGroupSls") },
      // Огнестойкость
      new() { Id = "fire_r_check",             Label = Loc.S("CalcTaskKind_fire_r_check"),             GroupKey = "fire",  Group = Loc.S("CalcTaskGroupFire") },
      new() { Id = "fire_r_check_batch",       Label = Loc.S("CalcTaskKind_fire_r_check_batch"),       GroupKey = "fire",  Group = Loc.S("CalcTaskGroupFire") },
      // Прочие
      new() { Id = "prestress_loss",           Label = Loc.S("CalcTaskKind_prestress_loss"),           GroupKey = "other", Group = Loc.S("CalcTaskGroupOther") },
   ];

   public ListCollectionView AvailableKindsView { get; }

   public List<CalcTaskSolverItem> SolverMethods { get; } =
   [
      new() { Id = "bisection", Label = Loc.S("LimitForceSolver_Bisection") },
      new() { Id = "fast",      Label = Loc.S("LimitForceSolver_Fast") },
   ];

   public ObservableCollection<CrossSection> Sections { get; }
   public ObservableCollection<FireSectionDef> FireSections { get; }
   public ObservableCollection<ForceSet> ForceSets { get; }
   public ObservableCollection<LoadItem> ForceItems { get; } = [];
   public ListCollectionView ForceItemsView { get; private set; } = null!;
   public string ForceItemFilter
   {
      get => _forceItemFilter;
      set { _forceItemFilter = value; OnPropertyChanged(); ForceItemsView?.Refresh(); }
   }

   public List<CalcType> CalcTypes { get; } = [CalcType.C, CalcType.CL, CalcType.N, CalcType.NL];
   public List<CalcType> FilteredCalcTypes
   {
       get
       {
           if (IsShellSimpl)
               return IsShellSimplSls
                   ? [CalcType.N, CalcType.NL]
                   : [CalcType.C, CalcType.CL];
           if (IsShellLayered)
               return [CalcType.C, CalcType.CL];
           return CalcTypes;
       }
   }

   public ICommand OkCommand { get; }

   public CalcTaskPropsDlgVM(AppViewModel app, CalcTask? existing, Window window, string? groupKey = null)
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

      AvailableKindsView = new ListCollectionView(AvailableKinds);
      AvailableKindsView.GroupDescriptions.Add(new PropertyGroupDescription(nameof(CalcTaskKindItem.Group)));

      ForceItemsView      = new ListCollectionView(ForceItems);
      Stage1ItemsView     = new ListCollectionView(Stage1Items);
      Stage2ItemsView     = new ListCollectionView(Stage2Items);
      ShellForceItemsView = new ListCollectionView(ShellForceItems);
      ForceItemsView.Filter      = o => Pass(_forceItemFilter,      ((LoadItem)o).Label);
      Stage1ItemsView.Filter     = o => Pass(_stage1ItemFilter,     ((LoadItem)o).Label);
      Stage2ItemsView.Filter     = o => Pass(_stage2ItemFilter,     ((LoadItem)o).Label);
      ShellForceItemsView.Filter = o => Pass(_shellForceItemFilter, ((ShellLoadItem)o).Label);

      CrackWidthLongForceItemsView = new ListCollectionView(CrackWidthLongForceItems);
      CrackWidthLongForceItemsView.Filter = o => Pass(_crackWidthLongForceItemFilter, ((LoadItem)o).Label);

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

          if ((existing.Kind is "strain_state" or "strain_state_batch" or "limit_moment" or "limit_moment_batch"
              or "crack_width" or "crack_width_batch")
              && !string.IsNullOrWhiteSpace(existing.ParamsJson) && existing.ParamsJson != "{}")
          {
             var ep = LimitForceParams.Parse(existing.ParamsJson);
             var inv = System.Globalization.CultureInfo.InvariantCulture;
             EtaEnabled   = ep.EtaEnabled;
             EtaIterative = ep.EtaIterative;
             if (ep.EtaL.HasValue)    EtaL    = ep.EtaL.Value.ToString("G6", inv);
             if (ep.EtaMuX.HasValue)  EtaMuX  = ep.EtaMuX.Value.ToString("G6", inv);
             if (ep.EtaMuY.HasValue)  EtaMuY  = ep.EtaMuY.Value.ToString("G6", inv);
             if (ep.EtaPsiX.HasValue) EtaPsiX = ep.EtaPsiX.Value.ToString("G6", inv);
             if (ep.EtaPsiY.HasValue) EtaPsiY = ep.EtaPsiY.Value.ToString("G6", inv);
             if (ep.EtaSlendernessThreshold.HasValue)
                EtaSlendernessThreshold = ep.EtaSlendernessThreshold.Value.ToString("G6", inv);
          }

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

          if (existing.Kind is "shell_strain_state" or "shell_strain_state_batch"
              or "shell_layered_uls" or "shell_layered_uls_batch")
          {
             var sp = ShellStrainParams.Parse(existing.ParamsJson);
             var inv = System.Globalization.CultureInfo.InvariantCulture;
             ShellSimplNx  = sp.Nx.ToString("G6", inv);
             ShellSimplNy  = sp.Ny.ToString("G6", inv);
             ShellSimplNxy = sp.Nxy.ToString("G6", inv);
             ShellSimplMx  = sp.Mx.ToString("G6", inv);
             ShellSimplMy  = sp.My.ToString("G6", inv);
             ShellSimplMxy = sp.Mxy.ToString("G6", inv);
             SelectedShellSimplSection = ShellSimplSections.FirstOrDefault(s => s.Id == existing.SectionId);
             if (existing.ForceSetId != 0)
                 SelectedShellForceSet = ShellForceSets.FirstOrDefault(fs => fs.Id == existing.ForceSetId);
          }

          // Загрузка параметров стальных задач при редактировании
          if (IsSteelCheck && !string.IsNullOrWhiteSpace(existing.ParamsJson) && existing.ParamsJson != "{}")
          {
              var sp = SteelCheckParams.Parse(existing.ParamsJson);
              var inv = System.Globalization.CultureInfo.InvariantCulture;
              SteelDesignLengthX = sp.DesignLengthX.ToString("G6", inv);
              SteelDesignLengthY = sp.DesignLengthY.ToString("G6", inv);
              SteelMuX = sp.MuX.ToString("G6", inv);
              SteelMuY = sp.MuY.ToString("G6", inv);
              SteelBetaM = sp.BetaM.ToString("G6", inv);
              SteelGammaM = sp.GammaM.ToString("G6", inv);

              if (sp.ManualForces != null)
              {
                  ManualN  = sp.ManualForces.N .ToString("G6", inv);
                  ManualMx = sp.ManualForces.Mx.ToString("G6", inv);
                  ManualMy = sp.ManualForces.My.ToString("G6", inv);
              }
          }

          if (existing.Kind is "crack_width" or "crack_width_batch")
          {
             var cwp = CrackWidthTaskParams.Parse(existing.ParamsJson);
             var inv = System.Globalization.CultureInfo.InvariantCulture;
             CrackWidthAcrcUltLong  = cwp.AcrcUltLong.ToString("G6", inv);
             CrackWidthAcrcUltShort = cwp.AcrcUltShort.ToString("G6", inv);
             CrackWidthForcesMode   = cwp.ForcesMode;
             CrackWidthLongShare    = cwp.LongShare.ToString("G6", inv);
             if (cwp.NLongManual.HasValue)  CrackWidthManualNLong  = cwp.NLongManual.Value.ToString("G6", inv);
             if (cwp.MxLongManual.HasValue) CrackWidthManualMxLong = cwp.MxLongManual.Value.ToString("G6", inv);
             if (cwp.MyLongManual.HasValue) CrackWidthManualMyLong = cwp.MyLongManual.Value.ToString("G6", inv);
             if (cwp.ForceSetLongId.HasValue)
             {
                SelectedCrackWidthLongForceSet = ForceSets.FirstOrDefault(fs => fs.Id == cwp.ForceSetLongId.Value);
                if (cwp.ForceItemLongId.HasValue)
                   SelectedCrackWidthLongForceItem = CrackWidthLongForceItems.FirstOrDefault(i => i.Id == cwp.ForceItemLongId.Value);
             }
             if (cwp.N.HasValue)  ManualN  = cwp.N.Value.ToString("G6", inv);
             if (cwp.Mx.HasValue) ManualMx = cwp.Mx.Value.ToString("G6", inv);
             if (cwp.My.HasValue) ManualMy = cwp.My.Value.ToString("G6", inv);
          }

          if (IsTorsion && !string.IsNullOrWhiteSpace(existing.ParamsJson) && existing.ParamsJson != "{}")
          {
              var tp = TorsionParams.Parse(existing.ParamsJson);
              var inv = System.Globalization.CultureInfo.InvariantCulture;
              TorsionElementSize = tp.ElementSize.ToString("G6", inv);
              if (tp.MkKNm != 0) TorsionMk = tp.MkKNm.ToString("G6", inv);
              TorsionTriangulationIndex = tp.Triangulation == CSTriangulation.TriangulationMethod.Ruppert ? 1 : 0;
              TorsionAutoConverge = tp.AutoConverge;
              TorsionFemOrderIndex = tp.FemOrder == "quadratic" ? 1 : 0;
              if (tp.AutoH0 > 0)
              {
                  torsionAutoH0 = tp.AutoH0.ToString("G6", inv);
                  _torsionH0UserOverride = true;
                  OnPropertyChanged(nameof(TorsionAutoH0));
              }
              if (tp.AutoRuns >= 2)
                  TorsionAutoRuns = tp.AutoRuns.ToString(inv);
          }

          NotifyTorsionForceProps();
          RefreshTorsionLmin();
       }
       else
       {
          SelectedKind = groupKey != null
              ? AvailableKinds.FirstOrDefault(k => k.GroupKey == groupKey) ?? AvailableKinds[0]
              : AvailableKinds[0];
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
      RefreshTorsionMeshPreview();
      RefreshTorsionLmin();
   }

   void RefreshTorsionLmin()
   {
      if (!IsTorsion || SelectedSection?.Areas == null || SelectedSection.Areas.Count == 0)
      {
         TorsionLminText = "—";
         _lastTorsionLmin = double.NaN;
         OnPropertyChanged(nameof(TorsionLminText));
         return;
      }

      try
      {
         var boundary = SelectedSection.Areas[0].FromMaterialArea();
         double lmin = TorsionBoundaryMetrics.MinEdgeLength(boundary);
         var inv = System.Globalization.CultureInfo.InvariantCulture;
         if (!double.IsFinite(lmin) || lmin <= 0)
         {
            TorsionLminText = "—";
            _lastTorsionLmin = double.NaN;
         }
         else
         {
            TorsionLminText = lmin.ToString("G6", inv) + " м";
            bool shouldFillH0 = !_torsionH0UserOverride
               || !double.IsFinite(_lastTorsionLmin)
               || (double.TryParse(torsionAutoH0.Replace(',', '.'),
                      System.Globalization.NumberStyles.Float, inv, out var cur)
                   && Math.Abs(cur - _lastTorsionLmin) < 1e-15 * Math.Max(1, Math.Abs(_lastTorsionLmin)));
            _lastTorsionLmin = lmin;
            if (shouldFillH0)
            {
               _torsionH0UserOverride = false;
               torsionAutoH0 = lmin.ToString("G6", inv);
               OnPropertyChanged(nameof(TorsionAutoH0));
            }
         }
         OnPropertyChanged(nameof(TorsionLminText));
      }
      catch
      {
         TorsionLminText = "—";
         _lastTorsionLmin = double.NaN;
         OnPropertyChanged(nameof(TorsionLminText));
      }
   }

   void RefreshTorsionMeshPreview()
   {
      UpdateDialogWidth();
      _torsionPreviewDebounceCts?.Cancel();
      _torsionPreviewDebounceCts?.Dispose();
      var cts = new CancellationTokenSource();
      _torsionPreviewDebounceCts = cts;
      Task.Run(async () =>
      {
         try { await Task.Delay(350, cts.Token).ConfigureAwait(false); }
         catch (OperationCanceledException) { return; }
         Application.Current?.Dispatcher.Invoke(ApplyTorsionMeshPreview);
      });
   }

   void ApplyTorsionMeshPreview()
   {
      if (!IsTorsionFem || TorsionAutoConverge)
      {
         TorsionMeshPreview.Configure(null, 0);
         return;
      }

      var inv = System.Globalization.CultureInfo.InvariantCulture;
      string raw = (TorsionElementSize ?? "").Trim().Replace(',', '.');
      double elem = double.TryParse(raw, System.Globalization.NumberStyles.Float, inv, out var es) && es > 0
         ? es : 0.05;
      TorsionMeshPreview.Configure(SelectedSection, elem, _torsionTriangulationIndex == 0
          ? CSTriangulation.TriangulationMethod.AdvancingFront
          : CSTriangulation.TriangulationMethod.Ruppert);
   }

   void UpdateDialogWidth() => _window.Width = IsTorsionFem ? 860 : 480;

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

      if (IsShellStrain)
      {
          if (SelectedShellSimplSection == null)
          {
             MessageBox.Show(Loc.S("CalcTaskNeedSection"), Loc.S("Warning"),
                MessageBoxButton.OK, MessageBoxImage.Warning);
             return;
          }

          var invs = System.Globalization.CultureInfo.InvariantCulture;
          if (IsShellStrainBatch)
          {
              if (SelectedShellForceSet == null)
              {
                  MessageBox.Show(Loc.S("CalcTaskNeedForceSet"), Loc.S("Warning"),
                     MessageBoxButton.OK, MessageBoxImage.Warning);
                  return;
              }
              Result = new CalcTask
              {
                  Tag = string.IsNullOrWhiteSpace(Tag) ? $"Задача {_app.CalcTasks.Count + 1}" : Tag,
                  Kind = Kind,
                  SectionId = SelectedShellSimplSection.Id,
                  ForceSetId = SelectedShellForceSet.Id,
                  ForceItemId = 0,
                  CalcType = SelectedCalcType,
                  ParamsJson = JsonSerializer.Serialize(new ShellStrainParams())
              };
              _window.DialogResult = true;
              return;
          }

          double snx  = double.TryParse(ShellSimplNx,  System.Globalization.NumberStyles.Float, invs, out var snxv)  ? snxv  : 0;
          double sny  = double.TryParse(ShellSimplNy,  System.Globalization.NumberStyles.Float, invs, out var snyv)  ? snyv  : 0;
          double snxy = double.TryParse(ShellSimplNxy, System.Globalization.NumberStyles.Float, invs, out var snxyv) ? snxyv : 0;
          double smx  = double.TryParse(ShellSimplMx,  System.Globalization.NumberStyles.Float, invs, out var smxv)  ? smxv  : 0;
          double smy  = double.TryParse(ShellSimplMy,  System.Globalization.NumberStyles.Float, invs, out var smyv)  ? smyv  : 0;
          double smxy = double.TryParse(ShellSimplMxy, System.Globalization.NumberStyles.Float, invs, out var smxyv) ? smxyv : 0;

          Result = new CalcTask
          {
              Tag = string.IsNullOrWhiteSpace(Tag) ? $"Задача {_app.CalcTasks.Count + 1}" : Tag,
              Kind = Kind,
              SectionId = SelectedShellSimplSection.Id,
              ForceSetId = 0,
              ForceItemId = 0,
              CalcType = SelectedCalcType,
              ParamsJson = JsonSerializer.Serialize(new ShellStrainParams
              {
                  Nx = snx, Ny = sny, Nxy = snxy, Mx = smx, My = smy, Mxy = smxy
              })
          };
          _window.DialogResult = true;
          return;
      }

      if (IsShellLayered)
      {
          if (SelectedShellSimplSection == null)
          {
             MessageBox.Show(Loc.S("CalcTaskNeedSection"), Loc.S("Warning"),
                MessageBoxButton.OK, MessageBoxImage.Warning);
             return;
          }

          var invs = System.Globalization.CultureInfo.InvariantCulture;
          if (IsShellLayeredBatch)
          {
              if (SelectedShellForceSet == null)
              {
                  MessageBox.Show(Loc.S("CalcTaskNeedForceSet"), Loc.S("Warning"),
                     MessageBoxButton.OK, MessageBoxImage.Warning);
                  return;
              }
              Result = new CalcTask
              {
                  Tag = string.IsNullOrWhiteSpace(Tag) ? $"Задача {_app.CalcTasks.Count + 1}" : Tag,
                  Kind = Kind,
                  SectionId = SelectedShellSimplSection.Id,
                  ForceSetId = SelectedShellForceSet.Id,
                  ForceItemId = 0,
                  CalcType = SelectedCalcType,
                  ParamsJson = JsonSerializer.Serialize(new ShellStrainParams())
              };
              _window.DialogResult = true;
              return;
          }

          double snx  = double.TryParse(ShellSimplNx,  System.Globalization.NumberStyles.Float, invs, out var snxv)  ? snxv  : 0;
          double sny  = double.TryParse(ShellSimplNy,  System.Globalization.NumberStyles.Float, invs, out var snyv)  ? snyv  : 0;
          double snxy = double.TryParse(ShellSimplNxy, System.Globalization.NumberStyles.Float, invs, out var snxyv) ? snxyv : 0;
          double smx  = double.TryParse(ShellSimplMx,  System.Globalization.NumberStyles.Float, invs, out var smxv)  ? smxv  : 0;
          double smy  = double.TryParse(ShellSimplMy,  System.Globalization.NumberStyles.Float, invs, out var smyv)  ? smyv  : 0;
          double smxy = double.TryParse(ShellSimplMxy, System.Globalization.NumberStyles.Float, invs, out var smxyv) ? smxyv : 0;

          Result = new CalcTask
          {
              Tag = string.IsNullOrWhiteSpace(Tag) ? $"Задача {_app.CalcTasks.Count + 1}" : Tag,
              Kind = Kind,
              SectionId = SelectedShellSimplSection.Id,
              ForceSetId = 0,
              ForceItemId = 0,
              CalcType = SelectedCalcType,
              ParamsJson = JsonSerializer.Serialize(new ShellStrainParams
              {
                  Nx = snx, Ny = sny, Nxy = snxy, Mx = smx, My = smy, Mxy = smxy
              })
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
          double stepDeg  = double.TryParse(ShellSimplStepDeg, System.Globalization.NumberStyles.Float, inv, out var sdv) ? sdv : 10.0;
          double acrcLim  = double.TryParse(ShellSimplAcrcLim, System.Globalization.NumberStyles.Float, inv, out var alv) ? alv : 0.3;
          double phi1     = double.TryParse(ShellSimplPhi1,    System.Globalization.NumberStyles.Float, inv, out var p1v) ? p1v : 1.0;
          double phi2     = double.TryParse(ShellSimplPhi2,    System.Globalization.NumberStyles.Float, inv, out var p2v) ? p2v : 0.5;

          if (IsShellSimplBatch)
          {
              if (SelectedShellForceSet == null)
              {
                  MessageBox.Show(Loc.S("CalcTaskNeedForceSet"), Loc.S("Warning"),
                     MessageBoxButton.OK, MessageBoxImage.Warning);
                  return;
              }
              Result = new CalcTask
              {
                  Tag = string.IsNullOrWhiteSpace(Tag) ? $"Задача {_app.CalcTasks.Count + 1}" : Tag,
                  Kind = Kind,
                  SectionId = SelectedShellSimplSection.Id,
                  ForceSetId  = SelectedShellForceSet.Id,
                  ForceItemId = 0,
                  CalcType = SelectedCalcType,
                  ParamsJson = JsonSerializer.Serialize(new ShellSimplParams
                  {
                      StepDeg = stepDeg, AcrcLimMm = acrcLim,
                      Phi1 = phi1, Phi2 = phi2
                  })
              };
              _window.DialogResult = true;
              return;
          }

          double nx  = double.TryParse(ShellSimplNx,  System.Globalization.NumberStyles.Float, inv, out var nxv)  ? nxv : 0;
          double ny  = double.TryParse(ShellSimplNy,  System.Globalization.NumberStyles.Float, inv, out var nyv)  ? nyv : 0;
          double nxy = double.TryParse(ShellSimplNxy, System.Globalization.NumberStyles.Float, inv, out var nxyv) ? nxyv : 0;
          double mx  = double.TryParse(ShellSimplMx,  System.Globalization.NumberStyles.Float, inv, out var mxv)  ? mxv : 0;
          double my  = double.TryParse(ShellSimplMy,  System.Globalization.NumberStyles.Float, inv, out var myv)  ? myv : 0;
          double mxy = double.TryParse(ShellSimplMxy, System.Globalization.NumberStyles.Float, inv, out var mxyv) ? mxyv : 0;

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

      if (IsCrackWidthAny)
      {
         if (SelectedSection == null)
         {
            MessageBox.Show(Loc.S("CalcTaskNeedSection"), Loc.S("Warning"),
               MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
         }

         var invCw = System.Globalization.CultureInfo.InvariantCulture;
         double.TryParse(CrackWidthAcrcUltLong,  System.Globalization.NumberStyles.Float, invCw, out var acrcLong);
         double.TryParse(CrackWidthAcrcUltShort, System.Globalization.NumberStyles.Float, invCw, out var acrcShort);
         double.TryParse(CrackWidthLongShare,    System.Globalization.NumberStyles.Float, invCw, out var longShare);

         var cwp = new CrackWidthTaskParams
         {
            AcrcUltLong = acrcLong,
            AcrcUltShort = acrcShort,
            ForcesMode = CrackWidthForcesMode,
            LongShare = longShare
         };

         if (IsCrackWidthBatch)
         {
            if (SelectedForceSet == null)
            {
               MessageBox.Show(Loc.S("CalcTaskNeedForceSet"), Loc.S("Warning"),
                  MessageBoxButton.OK, MessageBoxImage.Warning);
               return;
            }
            if (CrackWidthForcesMode == "two_sets")
               cwp.ForceSetLongId = SelectedCrackWidthLongForceSet?.Id;

            Result = new CalcTask
            {
               Tag = string.IsNullOrWhiteSpace(Tag) ? $"Задача {_app.CalcTasks.Count + 1}" : Tag,
               Kind = Kind,
               SectionId = SelectedSection.Id,
               ForceSetId = SelectedForceSet.Id,
               ForceItemId = 0,
               CalcType = SelectedCalcType,
               ParamsJson = MergeCrackParamsWithEta(cwp)
            };
            _window.DialogResult = true;
            return;
         }

         // Одиночная crack_width: полная нагрузка — через ManualN/Mx/My (ShowManualForces=true).
         double.TryParse(ManualN,  System.Globalization.NumberStyles.Float, invCw, out var nTotal);
         double.TryParse(ManualMx, System.Globalization.NumberStyles.Float, invCw, out var mxTotal);
         double.TryParse(ManualMy, System.Globalization.NumberStyles.Float, invCw, out var myTotal);
         cwp.N = nTotal; cwp.Mx = mxTotal; cwp.My = myTotal;

         if (CrackWidthForcesMode == "manual")
         {
            double.TryParse(CrackWidthManualNLong,  System.Globalization.NumberStyles.Float, invCw, out var nLongM);
            double.TryParse(CrackWidthManualMxLong, System.Globalization.NumberStyles.Float, invCw, out var mxLongM);
            double.TryParse(CrackWidthManualMyLong, System.Globalization.NumberStyles.Float, invCw, out var myLongM);
            cwp.NLongManual = nLongM; cwp.MxLongManual = mxLongM; cwp.MyLongManual = myLongM;
         }
         else if (CrackWidthForcesMode == "force_item_long")
         {
            if (SelectedCrackWidthLongForceSet == null || SelectedCrackWidthLongForceItem == null)
            {
               MessageBox.Show(Loc.S("CalcTaskNeedForceItem"), Loc.S("Warning"),
                  MessageBoxButton.OK, MessageBoxImage.Warning);
               return;
            }
            cwp.ForceSetLongId = SelectedCrackWidthLongForceSet.Id;
            cwp.ForceItemLongId = SelectedCrackWidthLongForceItem.Id;
         }

         Result = new CalcTask
         {
            Tag = string.IsNullOrWhiteSpace(Tag) ? $"Задача {_app.CalcTasks.Count + 1}" : Tag,
            Kind = Kind,
            SectionId = SelectedSection.Id,
            ForceSetId = 0,
            ForceItemId = 0,
            CalcType = SelectedCalcType,
            ParamsJson = MergeCrackParamsWithEta(cwp)
         };
         _window.DialogResult = true;
         return;
      }

      if (IsTorsion)
      {
          if (SelectedSection == null)
          {
              MessageBox.Show(Loc.S("CalcTaskNeedSection"), Loc.S("Warning"),
                  MessageBoxButton.OK, MessageBoxImage.Warning);
              return;
          }

          var inv = System.Globalization.CultureInfo.InvariantCulture;
          double elem = double.TryParse(TorsionElementSize, System.Globalization.NumberStyles.Float, inv, out var es) ? es : 0.05;
          if (elem <= 0) elem = 0.05;
          double.TryParse(TorsionMk, System.Globalization.NumberStyles.Float, inv, out var mkManual);

          double autoH0 = 0;
          int autoRuns = 0;
          if (TorsionAutoConverge)
          {
              string hRaw = (TorsionAutoH0 ?? "").Trim().Replace(',', '.');
              if (!double.TryParse(hRaw, System.Globalization.NumberStyles.Float, inv, out autoH0) || autoH0 <= 0)
              {
                  MessageBox.Show(Loc.S("TorsionAutoH0Invalid"), Loc.S("Warning"),
                      MessageBoxButton.OK, MessageBoxImage.Warning);
                  return;
              }
              if (!int.TryParse((TorsionAutoRuns ?? "").Trim(), out autoRuns) || autoRuns < 2)
              {
                  MessageBox.Show(Loc.S("TorsionAutoRunsInvalid"), Loc.S("Warning"),
                      MessageBoxButton.OK, MessageBoxImage.Warning);
                  return;
              }
          }

          if (SelectedForceSet != null && SelectedForceItem == null && mkManual <= 0)
          {
              MessageBox.Show(Loc.S("CalcTaskNeedForceItem"), Loc.S("Warning"),
                  MessageBoxButton.OK, MessageBoxImage.Warning);
              return;
          }

          Result = new CalcTask
          {
              Tag = string.IsNullOrWhiteSpace(Tag) ? $"Задача {_app.CalcTasks.Count + 1}" : Tag,
              Kind = Kind,
              SectionId = SelectedSection.Id,
              ForceSetId = SelectedForceSet?.Id ?? 0,
              ForceItemId = SelectedForceItem?.Id ?? 0,
              CalcType = SelectedCalcType,
              ParamsJson = new TorsionParams
              {
                  ElementSize = elem,
                  MkKNm = mkManual,
                  Triangulation = _torsionTriangulationIndex == 0
                      ? CSTriangulation.TriangulationMethod.AdvancingFront
                      : CSTriangulation.TriangulationMethod.Ruppert,
                  AutoConverge = TorsionAutoConverge,
                  FemOrder = _torsionFemOrderIndex == 1 ? "quadratic" : "linear",
                  AutoH0 = TorsionAutoConverge ? autoH0 : 0,
                  AutoRuns = TorsionAutoConverge ? autoRuns : 0
              }.ToJson()
          };
          _window.DialogResult = true;
          return;
      }

      if (IsSteelCheck)
      {
          if (SelectedSection == null)
          {
              MessageBox.Show(Loc.S("CalcTaskNeedSection"), Loc.S("Warning"),
                  MessageBoxButton.OK, MessageBoxImage.Warning);
              return;
          }

          var inv = System.Globalization.CultureInfo.InvariantCulture;
          double.TryParse(SteelDesignLengthX, System.Globalization.NumberStyles.Float, inv, out var dlx);
          double.TryParse(SteelDesignLengthY, System.Globalization.NumberStyles.Float, inv, out var dly);
          double.TryParse(SteelMuX, System.Globalization.NumberStyles.Float, inv, out var mux);
          double.TryParse(SteelMuY, System.Globalization.NumberStyles.Float, inv, out var muy);
          double.TryParse(SteelBetaM, System.Globalization.NumberStyles.Float, inv, out var bm);
          double.TryParse(SteelGammaM, System.Globalization.NumberStyles.Float, inv, out var gm);

          SteelManualForces? mf = null;
          if (ShowManualForces)
          {
              double.TryParse(ManualN,  System.Globalization.NumberStyles.Float, inv, out var n);
              double.TryParse(ManualMx, System.Globalization.NumberStyles.Float, inv, out var mx);
              double.TryParse(ManualMy, System.Globalization.NumberStyles.Float, inv, out var my);
              mf = new SteelManualForces { N = n, Mx = mx, My = my };
          }

          Result = new CalcTask
          {
              Tag = string.IsNullOrWhiteSpace(Tag) ? $"Задача {_app.CalcTasks.Count + 1}" : Tag,
              Kind = Kind,
              SectionId = SelectedSection.Id,
              ForceSetId = ShowManualForces ? 0 : (SelectedForceSet?.Id ?? 0),
              ForceItemId = ShowManualForces ? 0 : (ShowForceItem ? (SelectedForceItem?.Id ?? 0) : 0),
              CalcType = SelectedCalcType,
              ParamsJson = new SteelCheckParams
              {
                  DesignLengthX = dlx,
                  DesignLengthY = dly,
                  MuX = mux,
                  MuY = muy,
                  BetaM = bm,
                  GammaM = gm,
                  ManualForces = mf
              }.ToJson()
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

       if (SupportsEta && EtaEnabled)
       {
          var invEta = System.Globalization.CultureInfo.InvariantCulture;
          bool validL   = double.TryParse(EtaL, System.Globalization.NumberStyles.Float, invEta, out var lCheck) && lCheck > 0;
          bool validMuX = !double.TryParse(EtaMuX, System.Globalization.NumberStyles.Float, invEta, out var muxCheck) || muxCheck > 0;
          bool validMuY = !double.TryParse(EtaMuY, System.Globalization.NumberStyles.Float, invEta, out var muyCheck) || muyCheck > 0;
          bool validThreshold = !double.TryParse(EtaSlendernessThreshold, System.Globalization.NumberStyles.Float, invEta, out var thCheck) || thCheck > 0;
          if (!validL || !validMuX || !validMuY)
          {
             MessageBox.Show(Loc.S("EtaNeedL0"), Loc.S("Warning"),
                MessageBoxButton.OK, MessageBoxImage.Warning);
             return;
          }
          if (!validThreshold)
          {
             MessageBox.Show(Loc.S("EtaInvalidThreshold"), Loc.S("Warning"),
                MessageBoxButton.OK, MessageBoxImage.Warning);
             return;
          }
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
             var lfp = new LimitForceParams { Solver = SolverId, N = n, Mx = mx, My = my };
             if (Kind == "limit_moment" && EtaEnabled) ApplyEtaParams(lfp, inv);
             paramsJson = lfp.ToJson();
          }
          else if (IsStrainState)
          {
             var lfp = new LimitForceParams { N = n, Mx = mx, My = my };
             if (EtaEnabled) ApplyEtaParams(lfp, inv);
             paramsJson = lfp.ToJson();
          }
          else
             paramsJson = JsonSerializer.Serialize(new { N = n, Mx = mx, My = my });
       }
       else if (Kind == "strain_state_batch")
       {
          if (EtaEnabled)
          {
             var inv = System.Globalization.CultureInfo.InvariantCulture;
             var lfp = new LimitForceParams();
             ApplyEtaParams(lfp, inv);
             paramsJson = lfp.ToJson();
          }
       }
       else if (IsLimitKind)
       {
          var lfp = new LimitForceParams { Solver = SolverId };
          if (Kind == "limit_moment_batch" && EtaEnabled)
             ApplyEtaParams(lfp, System.Globalization.CultureInfo.InvariantCulture);
          paramsJson = lfp.ToJson();
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

   /// <summary>
   /// Переносит поля блока η (п. 8.1.15) диалога в <see cref="LimitForceParams"/>.
   /// Общая логика для одиночной (strain_state, ручные усилия) и пакетной
   /// (strain_state_batch, усилия из ForceSet) задач — набор η-параметров
   /// (L, μx/μy, ψx/ψy, порог гибкости) одинаков и не зависит от конкретной
   /// силовой позиции.
   /// </summary>
   void ApplyEtaParams(LimitForceParams lfp, System.Globalization.CultureInfo inv)
   {
      lfp.EtaEnabled   = true;
      lfp.EtaIterative = EtaIterative;
      if (double.TryParse(EtaL,   System.Globalization.NumberStyles.Float, inv, out var l))   lfp.EtaL   = l;
      if (double.TryParse(EtaMuX, System.Globalization.NumberStyles.Float, inv, out var mux)) lfp.EtaMuX = mux;
      if (double.TryParse(EtaMuY, System.Globalization.NumberStyles.Float, inv, out var muy)) lfp.EtaMuY = muy;
      if (double.TryParse(EtaSlendernessThreshold, System.Globalization.NumberStyles.Float, inv, out var th) && th > 0)
         lfp.EtaSlendernessThreshold = th;
      if (!EtaIterative && !IsCrackWidthAny)
      {
         if (double.TryParse(EtaPsiX, System.Globalization.NumberStyles.Float, inv, out var psix)) lfp.EtaPsiX = psix;
         if (double.TryParse(EtaPsiY, System.Globalization.NumberStyles.Float, inv, out var psiy)) lfp.EtaPsiY = psiy;
      }
   }

   /// <summary>Объединяет ParamsJson трещин с полями η (если включены).</summary>
   string MergeCrackParamsWithEta(CrackWidthTaskParams cwp)
   {
      string crackJson = cwp.ToJson();
      if (!EtaEnabled)
         return crackJson;

      var lfp = new LimitForceParams();
      ApplyEtaParams(lfp, System.Globalization.CultureInfo.InvariantCulture);
      using var crackDoc = System.Text.Json.JsonDocument.Parse(crackJson);
      using var etaDoc = System.Text.Json.JsonDocument.Parse(lfp.ToJson());
      var dict = new Dictionary<string, System.Text.Json.JsonElement>();
      foreach (var prop in crackDoc.RootElement.EnumerateObject())
         dict[prop.Name] = prop.Value.Clone();
      foreach (var prop in etaDoc.RootElement.EnumerateObject())
      {
         // Не затирать N/Mx/My трещин нулями из LimitForceParams.ToJson().
         if (prop.Name is "N" or "Mx" or "My" or "solver")
            continue;
         dict[prop.Name] = prop.Value.Clone();
      }
      return System.Text.Json.JsonSerializer.Serialize(dict);
   }
}
