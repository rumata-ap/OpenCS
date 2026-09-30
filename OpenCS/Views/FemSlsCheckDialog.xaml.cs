using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using CScore;
using CScore.Fem;
using OpenCS.Utilites;
using OpenCS.ViewModels;

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

public class FemSlsCheckDialogVM : FemCheckDialogVmBase
{
    readonly FemCheck?    _existing;

    public ObservableCollection<FemSchema> Schemas { get; }
    /// <summary>Цели проверки: группы схемы и её пластинчатые конструктивные элементы.</summary>
    public ObservableCollection<FemCheckTarget> Members { get; } = [];
    public ObservableCollection<ForceSet>  NlForceSets { get; } = [];

    FemSchema? _selectedSchema;
    public FemSchema? SelectedSchema
    {
        get => _selectedSchema;
        set { _selectedSchema = value; OnPropertyChanged(); ReloadSchema(); RefreshMembers(); }
    }

    FemCheckTarget? _selectedMember;
    public FemCheckTarget? SelectedMember
    {
        get => _selectedMember;
        set { _selectedMember = value; OnPropertyChanged(); RefreshTarget(); AutoFillTag(); }
    }

    protected override IFemCheckable? Target => (IFemCheckable?)_selectedMember?.Group ?? _selectedMember?.Element;
    protected override int? SchemaId => _selectedSchema?.Id;
    protected override bool IsPlateCheck => true;
    protected override FemCheck DraftCheck() => FillCheck(new FemCheck());
    protected override bool AcceptsForceSet(ForceSet fs) => fs.Kind == "shell";

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
    public FemSlsCheckDialogVM(AppViewModel app, FemCheck? existing, ListBox setsBox) : base(app, setsBox)
    {
        _existing = existing;
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
        SelectedMember         = check.TargetsElement
            ? Members.FirstOrDefault(t => t.Kind == "element" && t.Id == check.ElementId)
            : Members.FirstOrDefault(t => t.Kind == "group"   && t.Id == check.MemberId);
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
            LoadRebarSources(p);
        }

        if (!AllSets)
            SelectForceSets(check.GetForceSetIds());
    }

    void RefreshMembers()
    {
        Members.Clear();
        if (_selectedSchema == null) return;
        foreach (var g in _selectedSchema.MemberGroups)
            Members.Add(new FemCheckTarget { Kind = "group", Id = g.Id, Tag = string.Format(Loc.S("FemCheckDlgTargetGroup"), g.Tag), Group = g });
        foreach (var e in App.GetFemMembers(_selectedSchema).Where(e => e.ElemType == "shell"))
            Members.Add(new FemCheckTarget { Kind = "element", Id = e.Id, Tag = string.Format(Loc.S("FemCheckDlgTargetElement"), e.ElemTag), Element = e });
        SelectedMember = Members.FirstOrDefault();
    }

    /// <summary>NL-набор выбирается из тех же наборов цели.</summary>
    protected override void OnForceSetsRefreshed()
    {
        NlForceSets.Clear();
        NlForceSets.Add(new ForceSet { Id = 0, Tag = Loc.S("FemSlsDlgNlNone") });
        foreach (var item in FilteredForceSets)
            NlForceSets.Add(item.ForceSet);
        SelectedNlForceSet = NlForceSets[0];
    }

    void AutoFillTag()
    {
        if (_selectedMember == null || _selectedSlsKind == null) return;
        Tag = $"{_selectedMember.Tag} / {_selectedSlsKind.Kind}";
    }

    public FemCheck BuildCheck() => FillCheck(_existing ?? new FemCheck());

    /// <summary>Записывает состояние диалога в проверку.</summary>
    FemCheck FillCheck(FemCheck check)
    {
        string forceSetIdsJson = "[]";
        if (!AllSets)
        {
            var ids = SelectedForceSets().Select(f => f.Id).ToArray();
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
            RebarSources = SelectedRebarSources(),
        }.ToJson();

        check.SchemaId         = _selectedSchema!.Id;
        check.MemberId         = _selectedMember!.Kind == "group"   ? _selectedMember.Id : 0;
        check.ElementId        = _selectedMember!.Kind == "element" ? _selectedMember.Id : null;
        check.NormCode         = "rc_plate_check";
        check.Tag              = string.IsNullOrWhiteSpace(Tag) ? $"{_selectedMember.Tag}/sls" : Tag;
        check.ForceSetIdsJson  = forceSetIdsJson;
        check.CalcTypeOverride = _selectedCalcTypeOption?.Code;
        check.ParamsJson       = paramsJson;
        return check;
    }
}
