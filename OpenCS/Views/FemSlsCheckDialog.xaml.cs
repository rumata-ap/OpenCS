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

public partial class FemSlsCheckDialog : Window
{
    readonly AppViewModel _app;
    public FemCheck? ResultCheck { get; private set; }

    public FemSlsCheckDialog(AppViewModel app, FemCheck? existing = null)
    {
        _app = app;
        InitializeComponent();
        DataContext = new FemSlsCheckDialogVM(app, existing, ForceSetsBox);
        Owner = Application.Current.MainWindow;
    }

    void Ok_Click(object sender, RoutedEventArgs e)
    {
        var vm = (FemSlsCheckDialogVM)DataContext;
        if (vm.SelectedMember == null) { MessageBox.Show("Выберите конструктивный элемент."); return; }
        ResultCheck = vm.BuildCheck();
        DialogResult = true;
    }
}

public class FemSlsCheckDialogVM : ViewModelBase
{
    readonly AppViewModel _app;
    readonly ListBox      _setsBox;
    readonly FemCheck?    _existing;

    public ObservableCollection<FemSchema> Schemas { get; }
    public ObservableCollection<FemMember> Members { get; } = [];
    public ObservableCollection<ForceSet>  FilteredForceSets { get; } = [];
    public ObservableCollection<ForceSet>  NlForceSets { get; } = [];

    FemSchema? _selectedSchema;
    public FemSchema? SelectedSchema
    {
        get => _selectedSchema;
        set { _selectedSchema = value; OnPropertyChanged(); RefreshMembers(); }
    }

    FemMember? _selectedMember;
    public FemMember? SelectedMember
    {
        get => _selectedMember;
        set { _selectedMember = value; OnPropertyChanged(); RefreshForceSets(); AutoFillTag(); }
    }

    bool _allSets = true;
    public bool AllSets
    {
        get => _allSets;
        set { _allSets = value; OnPropertyChanged(); OnPropertyChanged(nameof(CanSelectSets));
              if (value) _setsBox.SelectAll(); }
    }
    public bool CanSelectSets => !_allSets;

    // ── SLS kind ─────────────────────────────────────────────────────────────
    public record SlsKindItem(string Kind, string Label);
    public List<SlsKindItem> SlsKinds { get; } =
    [
        new("shell_simpl_wa_sls",    Loc.S("PlateKindWaSls")),
        new("shell_simpl_capri_sls", Loc.S("PlateKindCapriSls")),
        new("shell_layered",         Loc.S("PlateKindLayeredSls")),
    ];

    SlsKindItem? _selectedSlsKind;
    public SlsKindItem? SelectedSlsKind
    {
        get => _selectedSlsKind;
        set
        {
            _selectedSlsKind = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(NlRowVisibility));
            OnPropertyChanged(nameof(Phi1RowVisibility));
            OnPropertyChanged(nameof(LtFractionEnabled));
            AutoFillTag();
        }
    }

    bool IsLayered => _selectedSlsKind?.Kind == "shell_layered";
    public Visibility NlRowVisibility   => IsLayered ? Visibility.Visible : Visibility.Collapsed;
    public Visibility Phi1RowVisibility => !IsLayered ? Visibility.Visible : Visibility.Collapsed;

    // ── NL force set ─────────────────────────────────────────────────────────
    ForceSet? _selectedNlForceSet;
    public ForceSet? SelectedNlForceSet
    {
        get => _selectedNlForceSet;
        set { _selectedNlForceSet = value; OnPropertyChanged(); OnPropertyChanged(nameof(LtFractionEnabled)); }
    }

    public bool LtFractionEnabled => IsLayered && (_selectedNlForceSet?.Id ?? 0) == 0;

    string _ltFraction = "0.0";
    public string LtFraction
    {
        get => _ltFraction;
        set { _ltFraction = value; OnPropertyChanged(); }
    }

    // ── Phi1 (wa_sls / capri_sls) ────────────────────────────────────────────
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

    // ── AcrcLim + Phi2 ───────────────────────────────────────────────────────
    string _acrcLimMm = "0.3";
    public string AcrcLimMm { get => _acrcLimMm; set { _acrcLimMm = value; OnPropertyChanged(); } }

    string _phi2 = "0.5";
    public string Phi2 { get => _phi2; set { _phi2 = value; OnPropertyChanged(); } }

    // ── CalcType ─────────────────────────────────────────────────────────────
    public record CalcTypeOption(string? Code, string Label);
    public List<CalcTypeOption> CalcTypeOptions { get; } =
    [
        new(null, Loc.S("FemCheckDlgCalcTypeAuto")),
        new("N",  "N — нормативные"),
        new("NL", "NL — нормативные длительные"),
    ];

    CalcTypeOption? _selectedCalcTypeOption;
    public CalcTypeOption? SelectedCalcTypeOption
    {
        get => _selectedCalcTypeOption;
        set { _selectedCalcTypeOption = value; OnPropertyChanged(); }
    }

    string _tag = "";
    public string Tag { get => _tag; set { _tag = value; OnPropertyChanged(); } }

    // ── Constructor ──────────────────────────────────────────────────────────
    public FemSlsCheckDialogVM(AppViewModel app, FemCheck? existing, ListBox setsBox)
    {
        _app      = app;
        _existing = existing;
        _setsBox  = setsBox;
        Schemas   = app.FemSchemas;

        _selectedSlsKind        = SlsKinds[0];
        _selectedPhi1Mode       = Phi1Modes[0]; // auto
        _selectedCalcTypeOption = CalcTypeOptions[0];

        if (existing != null) LoadFromExisting(existing);
        else if (Schemas.Count > 0) SelectedSchema = Schemas[0];
    }

    void LoadFromExisting(FemCheck check)
    {
        SelectedSchema         = Schemas.FirstOrDefault(s => s.Id == check.SchemaId);
        SelectedMember         = Members.FirstOrDefault(m => m.Id == check.MemberId);
        SelectedCalcTypeOption = CalcTypeOptions.FirstOrDefault(o => o.Code == check.CalcTypeOverride)
                                 ?? CalcTypeOptions[0];
        Tag     = check.Tag;
        AllSets = check.IsAllSets;

        if (check.NormCode == "rc_plate_check" && !string.IsNullOrWhiteSpace(check.ParamsJson))
        {
            var p = PlateCheckParams.Parse(check.ParamsJson);
            SelectedSlsKind  = SlsKinds.FirstOrDefault(k => k.Kind == p.Kind) ?? SlsKinds[0];
            AcrcLimMm        = p.AcrcLimMm.ToString("G");
            Phi2             = p.Phi2.ToString("G");
            LtFraction       = p.LtFraction.ToString("G");
            SelectedPhi1Mode = Phi1Modes.FirstOrDefault(m => m.Mode == p.Phi1Mode) ?? Phi1Modes[0];
            Phi1             = p.Phi1.ToString("G");
            if (p.NlForceSetId > 0)
                SelectedNlForceSet = NlForceSets.FirstOrDefault(f => f.Id == p.NlForceSetId);
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
        foreach (var m in _selectedSchema.Members) Members.Add(m);
        SelectedMember = Members.FirstOrDefault();
    }

    void RefreshForceSets()
    {
        FilteredForceSets.Clear();
        NlForceSets.Clear();
        NlForceSets.Add(new ForceSet { Id = 0, Tag = Loc.S("FemSlsDlgNlNone") });
        SelectedNlForceSet = NlForceSets[0];

        if (_selectedMember == null) return;
        foreach (var fs in _app.ForceSets.Where(f => f.SourceMemberId == _selectedMember.Id && f.Kind == "shell"))
        {
            FilteredForceSets.Add(fs);
            NlForceSets.Add(fs);
        }
        if (AllSets) _setsBox.SelectAll();
    }

    void AutoFillTag()
    {
        if (_selectedMember == null || _selectedSlsKind == null) return;
        Tag = $"{_selectedMember.Tag} / {_selectedSlsKind.Kind}";
    }

    public FemCheck BuildCheck()
    {
        string forceSetIdsJson = "[]";
        if (!AllSets)
        {
            var ids = _setsBox.SelectedItems.OfType<ForceSet>().Select(f => f.Id).ToArray();
            forceSetIdsJson = ids.Length > 0 ? JsonSerializer.Serialize(ids) : "[]";
        }

        var inv = System.Globalization.CultureInfo.InvariantCulture;
        double acrc = double.TryParse(AcrcLimMm.Replace(',', '.'),
            System.Globalization.NumberStyles.Float, inv, out var va) ? va : 0.3;
        double phi2 = double.TryParse(Phi2.Replace(',', '.'),
            System.Globalization.NumberStyles.Float, inv, out var vp2) ? vp2 : 0.5;
        double ltFrac = double.TryParse(LtFraction.Replace(',', '.'),
            System.Globalization.NumberStyles.Float, inv, out var vlt) ? vlt : 0.0;
        double phi1 = double.TryParse(Phi1.Replace(',', '.'),
            System.Globalization.NumberStyles.Float, inv, out var vph) ? vph : 1.0;

        int nlId = (_selectedNlForceSet?.Id ?? 0) > 0 ? _selectedNlForceSet!.Id : 0;

        var paramsJson = new PlateCheckParams
        {
            Kind         = _selectedSlsKind?.Kind ?? "shell_layered",
            AcrcLimMm    = acrc,
            Phi2         = phi2,
            Phi1Mode     = _selectedPhi1Mode?.Mode ?? "auto",
            Phi1         = phi1,
            CheckGroup   = "sls",
            NlForceSetId = nlId,
            LtFraction   = nlId == 0 ? ltFrac : 0.0,
        }.ToJson();

        var check = _existing ?? new FemCheck();
        check.SchemaId         = _selectedSchema!.Id;
        check.MemberId         = _selectedMember!.Id;
        check.NormCode         = "rc_plate_check";
        check.Tag              = string.IsNullOrWhiteSpace(Tag) ? $"{_selectedMember.Tag}/sls" : Tag;
        check.ForceSetIdsJson  = forceSetIdsJson;
        check.CalcTypeOverride = _selectedCalcTypeOption?.Code;
        check.ParamsJson       = paramsJson;
        return check;
    }
}
