using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using CScore;
using CScore.Fem;
using OpenCS.Utilites;

namespace OpenCS.Views;

public partial class FemCheckDialog : Window
{
    readonly AppViewModel _app;
    public FemCheck? ResultCheck { get; private set; }

    public FemCheckDialog(AppViewModel app, FemCheck? existing = null)
    {
        _app = app;
        InitializeComponent();
        DataContext = new FemCheckDialogVM(app, existing, ForceSetsBox);
        Owner = Application.Current.MainWindow;
    }

    void Ok_Click(object sender, RoutedEventArgs e)
    {
        var vm = (FemCheckDialogVM)DataContext;
        if (vm.SelectedMember == null) { MessageBox.Show("Выберите конструктивный элемент."); return; }
        ResultCheck = vm.BuildCheck();
        DialogResult = true;
    }
}

public class FemCheckDialogVM : ViewModelBase
{
    readonly AppViewModel _app;
    readonly ListBox      _setsBox;
    readonly FemCheck?    _existing;

    public ObservableCollection<FemSchema> Schemas { get; }
    public ObservableCollection<FemMemberGroup> Members { get; } = [];
    public ObservableCollection<ForceSet>  FilteredForceSets { get; } = [];

    FemSchema? _selectedSchema;
    public FemSchema? SelectedSchema
    {
        get => _selectedSchema;
        set { _selectedSchema = value; OnPropertyChanged(); RefreshMembers(); }
    }

    FemMemberGroup? _selectedMember;
    public FemMemberGroup? SelectedMember
    {
        get => _selectedMember;
        set { _selectedMember = value; OnPropertyChanged(); RefreshForceSets(); AutoFillTag(); }
    }

    bool _allSets = true;
    public bool AllSets
    {
        get => _allSets;
        set
        {
            _allSets = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(CanSelectSets));
            if (value) _setsBox.SelectAll();
        }
    }

    public bool CanSelectSets => !_allSets;

    // ── NormCode ──────────────────────────────────────────────────────────────
    public record NormCodeItem(string Code, string Label);
    public List<NormCodeItem> NormCodes { get; } =
    [
        new("steel_check",    Loc.S("FemCheckNormCodeSteel")),
        new("rc_check",       Loc.S("FemCheckNormCodeRcBar")),
        new("rc_plate_check", Loc.S("FemCheckNormCodeRcPlate")),
    ];

    NormCodeItem? _selectedNormCode;
    public NormCodeItem? SelectedNormCode
    {
        get => _selectedNormCode;
        set
        {
            _selectedNormCode = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(PlateRowVisibility));
            OnPropertyChanged(nameof(AcrcRowVisibility));
            AutoFillTag();
        }
    }

    bool IsPlate => _selectedNormCode?.Code == "rc_plate_check";
    public Visibility PlateRowVisibility => IsPlate ? Visibility.Visible : Visibility.Collapsed;
    public Visibility AcrcRowVisibility  =>
        IsPlate && (_selectedPlateKind?.Kind.EndsWith("sls") == true)
            ? Visibility.Visible : Visibility.Collapsed;

    // ── Plate kind ────────────────────────────────────────────────────────────
    public record PlateKindItem(string Kind, string Label);
    public List<PlateKindItem> PlateKinds { get; } =
    [
        new("shell_simpl_wa_uls",    Loc.S("PlateKindWaUls")),
        new("shell_simpl_capri_uls", Loc.S("PlateKindCapriUls")),
        new("shell_layered",         Loc.S("PlateKindLayered")),
    ];

    PlateKindItem? _selectedPlateKind;
    public PlateKindItem? SelectedPlateKind
    {
        get => _selectedPlateKind;
        set
        {
            _selectedPlateKind = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(AcrcRowVisibility));
        }
    }

    string _acrcLimMm = "0.3";
    public string AcrcLimMm
    {
        get => _acrcLimMm;
        set { _acrcLimMm = value; OnPropertyChanged(); }
    }

    public record Phi1ModeItem(string Mode, string Label);
    public List<Phi1ModeItem> Phi1Modes { get; } =
    [
        new("auto",   Loc.S("Phi1ModeAuto")),
        new("manual", Loc.S("Phi1ModeManual")),
    ];

    Phi1ModeItem? _selectedPhi1Mode;
    public Phi1ModeItem? SelectedPhi1Mode
    {
        get => _selectedPhi1Mode;
        set { _selectedPhi1Mode = value; OnPropertyChanged(); OnPropertyChanged(nameof(Phi1ManualVisibility)); }
    }

    public Visibility Phi1ManualVisibility =>
        _selectedPhi1Mode?.Mode == "manual" ? Visibility.Visible : Visibility.Collapsed;

    string _phi1 = "1.0";
    public string Phi1 { get => _phi1; set { _phi1 = value; OnPropertyChanged(); } }

    // ── CalcType ──────────────────────────────────────────────────────────────
    public record CalcTypeOption(string? Code, string Label);
    public List<CalcTypeOption> CalcTypeOptions { get; } =
    [
        new(null,  Loc.S("FemCheckDlgCalcTypeAuto")),
        new("C",   "C — расчётные"),
        new("CL",  "CL — расчётные длительные"),
        new("N",   "N — нормативные"),
        new("NL",  "NL — нормативные длительные"),
    ];

    CalcTypeOption? _selectedCalcTypeOption;
    public CalcTypeOption? SelectedCalcTypeOption
    {
        get => _selectedCalcTypeOption;
        set { _selectedCalcTypeOption = value; OnPropertyChanged(); }
    }

    string _tag = "";
    public string Tag { get => _tag; set { _tag = value; OnPropertyChanged(); } }

    // ── Constructor ───────────────────────────────────────────────────────────
    public FemCheckDialogVM(AppViewModel app, FemCheck? existing, ListBox setsBox)
    {
        _app      = app;
        _existing = existing;
        _setsBox  = setsBox;
        Schemas   = app.FemSchemas;

        _selectedNormCode       = NormCodes[0];
        _selectedCalcTypeOption = CalcTypeOptions[0];
        _selectedPlateKind      = PlateKinds[0];
        _selectedPhi1Mode       = Phi1Modes[0]; // auto

        if (existing != null) LoadFromExisting(existing);
        else if (Schemas.Count > 0) SelectedSchema = Schemas[0];
    }

    void LoadFromExisting(FemCheck check)
    {
        SelectedSchema        = Schemas.FirstOrDefault(s => s.Id == check.SchemaId);
        SelectedMember        = Members.FirstOrDefault(m => m.Id == check.MemberId);
        SelectedNormCode      = NormCodes.FirstOrDefault(n => n.Code == check.NormCode) ?? NormCodes[0];
        SelectedCalcTypeOption = CalcTypeOptions.FirstOrDefault(o => o.Code == check.CalcTypeOverride)
                                 ?? CalcTypeOptions[0];
        Tag     = check.Tag;
        AllSets = check.IsAllSets;

        if (check.NormCode == "rc_plate_check" && !string.IsNullOrWhiteSpace(check.ParamsJson))
        {
            var p = PlateCheckParams.Parse(check.ParamsJson);
            SelectedPlateKind  = PlateKinds.FirstOrDefault(k => k.Kind == p.Kind) ?? PlateKinds[0];
            AcrcLimMm          = p.AcrcLimMm.ToString("G");
            SelectedPhi1Mode   = Phi1Modes.FirstOrDefault(m => m.Mode == p.Phi1Mode) ?? Phi1Modes[0];
            Phi1               = p.Phi1.ToString("G");
        }

        if (!AllSets)
        {
            var ids = check.GetForceSetIds().ToHashSet();
            foreach (var fs in FilteredForceSets.Where(f => ids.Contains(f.Id)))
                _setsBox.SelectedItems.Add(fs);
        }
    }

    void RefreshMembers()
    {
        Members.Clear();
        if (_selectedSchema == null) return;
        foreach (var m in _selectedSchema.MemberGroups) Members.Add(m);
        SelectedMember = Members.FirstOrDefault();
    }

    void RefreshForceSets()
    {
        FilteredForceSets.Clear();
        if (_selectedMember == null) return;
        foreach (var fs in _app.ForceSets.Where(f => f.SourceMemberId == _selectedMember.Id))
            FilteredForceSets.Add(fs);
        if (AllSets) _setsBox.SelectAll();
    }

    void AutoFillTag()
    {
        if (_selectedMember == null || _selectedNormCode == null) return;
        Tag = $"{_selectedMember.Tag} / {_selectedNormCode.Code}";
    }

    public FemCheck BuildCheck()
    {
        string forceSetIdsJson = "[]";
        if (!AllSets)
        {
            var ids = _setsBox.SelectedItems.OfType<ForceSet>().Select(f => f.Id).ToArray();
            forceSetIdsJson = ids.Length > 0 ? JsonSerializer.Serialize(ids) : "[]";
        }

        string? paramsJson = null;
        if (IsPlate && _selectedPlateKind != null)
        {
            var inv = System.Globalization.CultureInfo.InvariantCulture;
            double acrc = double.TryParse(AcrcLimMm.Replace(',', '.'),
                System.Globalization.NumberStyles.Float, inv, out var va) ? va : 0.3;
            double phi1 = double.TryParse(Phi1.Replace(',', '.'),
                System.Globalization.NumberStyles.Float, inv, out var vp) ? vp : 1.0;

            paramsJson = new PlateCheckParams
            {
                Kind       = _selectedPlateKind.Kind,
                AcrcLimMm  = acrc,
                Phi1Mode   = _selectedPhi1Mode?.Mode ?? "auto",
                Phi1       = phi1,
                CheckGroup = "uls",
            }.ToJson();
        }

        var check = _existing ?? new FemCheck();
        check.SchemaId         = _selectedSchema!.Id;
        check.MemberId         = _selectedMember!.Id;
        check.NormCode         = _selectedNormCode?.Code ?? "steel_check";
        check.Tag              = string.IsNullOrWhiteSpace(Tag) ? $"{_selectedMember.Tag}/{check.NormCode}" : Tag;
        check.ForceSetIdsJson  = forceSetIdsJson;
        check.CalcTypeOverride = _selectedCalcTypeOption?.Code;
        check.ParamsJson       = paramsJson;
        return check;
    }
}
