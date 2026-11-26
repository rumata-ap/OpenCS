using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using CScore;
using CScore.Fem;
using OpenCS.Services;
using OpenCS.Utilites;
using OpenCS.ViewModels;
using OpenCS.Views.Helpers;

namespace OpenCS.Views;

public partial class FemSlsCheckDialog : Window
{
    readonly AppViewModel _app;
    public FemCheck? ResultCheck { get; private set; }

    /// <param name="target">Группа, которую выбрать целью новой проверки.</param>
    public FemSlsCheckDialog(AppViewModel app, FemCheck? existing = null, IFemCheckable? target = null)
    {
        _app = app;
        InitializeComponent();
        var vm = new FemSlsCheckDialogVM(app, existing, ForceSetsBox);
        DataContext = vm;
        if (existing == null && target != null) vm.SelectTarget(target);
        Owner = Application.Current.MainWindow;
    }

    /// <summary>Выбрана ли целью заданная группа или элемент (в списке целей только цели с пластинчатыми КЭ).</summary>
    public bool HasTarget(IFemCheckable target) => ((FemSlsCheckDialogVM)DataContext).SelectedMember is { } t
        && (t.Group == target || t.Element is { } e && target is FemMember m && e.Id == m.Id);

    /// <summary>Числа полей — у поля, затем проверка службы; окно возвращает черновик (запись — служба).</summary>
    void Ok_Click(object sender, RoutedEventArgs e)
    {
        var vm = (FemSlsCheckDialogVM)DataContext;
        if (vm.SelectedMember == null || vm.SelectedSchema == null)
        {
            MessageBox.Show(this, Loc.S("FemCheckTargetMissing"), Title, MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        FieldCheck.Clear(LtFractionBox, Phi1Box, AcrcBox, Phi2Box);
        static bool Active(TextBox b) => b.IsVisible && b.IsEnabled;
        if (Active(LtFractionBox) && !FieldCheck.Read(this, LtFractionBox, "FemSlsDlgLtFraction", out _, FieldCheck.InRangeCC(0, 1))) return;
        if (Active(Phi1Box) && !FieldCheck.Read(this, Phi1Box, "FemCheckPhi1Label", out _, FieldCheck.Positive)) return;
        if (!FieldCheck.Read(this, AcrcBox, "FemCheckDlgAcrcLim", out _, FieldCheck.Positive)) return;
        if (!FieldCheck.Read(this, Phi2Box, "FemSlsDlgPhi2Label", out _, FieldCheck.Positive)) return;
        var draft = vm.BuildCheck();
        if (vm.NoSetsChosen)
        {
            MessageBox.Show(this, Loc.S("FemCheckSetsNoneChosen"), Title, MessageBoxButton.OK, MessageBoxImage.Warning);
            ForceSetsBox.Focus();
            return;
        }
        if (!FemCheckDialogErrors.Show(this, FemCheckAuthoring.Validate(FemCheckRefs.Of(_app.db, vm.SelectedSchema), draft),
                TagBox, TagErrorText))
            return;
        ResultCheck = draft;
        DialogResult = true;
    }

    void TagBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (TagErrorText != null) TagErrorText.Visibility = Visibility.Collapsed;
    }
}

public class FemSlsCheckDialogVM : FemCheckDialogVmBase
{
    readonly FemCheck?    _existing;

    public ObservableCollection<FemSchema> Schemas { get; }
    /// <summary>Цели проверки: группы схемы с пластинчатыми КЭ и её пластинчатые конструктивные элементы.</summary>
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
            OnPropertyChanged(nameof(StripModelVisibility));
            OnPropertyChanged(nameof(CapriCheckModeVisibility));
            AutoFillTag();
        }
    }

    bool IsLayered => _selectedSlsKind?.Kind == "shell_layered";
    /// <summary>Модель полосы — только у упрощённых видов (Вуд–Армер, Капра–Мори).</summary>
    public Visibility StripModelVisibility =>
        _selectedSlsKind?.Kind.StartsWith("shell_simpl") == true ? Visibility.Visible : Visibility.Collapsed;
    public Visibility CapriCheckModeVisibility => CapriCheckModeVisibilityOf(_selectedSlsKind?.Kind);
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
        new("N",  Loc.S("FemCheckCalcTypeN")),
        new("NL", Loc.S("FemCheckCalcTypeNL")),
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
        if (existing != null)
            KeptForceSetIds = existing.GetForceSetIds().Append(PlateCheckParams.Parse(existing.ParamsJson).NlForceSetId)
                .Where(id => id > 0).ToHashSet();

        _selectedSlsKind        = SlsKinds[0];
        _selectedPhi1Mode       = Phi1Modes[0]; // auto
        _selectedCalcTypeOption = CalcTypeOptions[0];

        if (existing != null)
        {
            // Загрузка правки: выбор цели и вида не подставляет автоимя — имя проверки сохраняется.
            _loading = true;
            LoadFromExisting(existing);
            _loading = false;
        }
        else if (Schemas.Count > 0) SelectedSchema = Schemas[0];
    }

    bool _loading;
    /// <summary>Последнее подставленное автоимя: пока имя равно ему (или пусто), смена цели или вида его обновляет.</summary>
    string? _lastAutoTag;

    /// <summary>Выбрать целью группу или конструктивный элемент; если его нет среди целей, выбор не меняется.</summary>
    public void SelectTarget(IFemCheckable target)
    {
        int schemaId = target switch { FemMemberGroup g => g.SchemaId, FemMember m => m.SchemaId, _ => 0 };
        if (Schemas.FirstOrDefault(s => s.Id == schemaId) is not { } schema) return;
        if (schema != SelectedSchema) SelectedSchema = schema;
        var item = target switch
        {
            FemMemberGroup g => Members.FirstOrDefault(t => t.Group == g),
            FemMember m      => Members.FirstOrDefault(t => t.Kind == "element" && t.Id == m.Id),
            _                => null,
        };
        if (item != null) SelectedMember = item;
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
            StripModelIndex  = StripModelIndexOf(p.StripModel);
            CapriCheckModeIndex = p.CapriCheckMode == CScore.CapriCheckMode.Constraints ? 1 : 0;
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
        // Проверка только для пластин: группы стержней (балки, колонны) целью быть не могут.
        foreach (var g in _selectedSchema.MemberGroups.Where(HasShellElements))
            Members.Add(new FemCheckTarget { Kind = "group", Id = g.Id, Tag = string.Format(Loc.S("FemCheckDlgTargetGroup"), g.Tag), Group = g });
        foreach (var e in App.GetFemMembers(_selectedSchema).Where(e => e.ElemType == "shell"))
            Members.Add(new FemCheckTarget { Kind = "element", Id = e.Id, Tag = string.Format(Loc.S("FemCheckDlgTargetElement"), e.ElemTag), Element = e });
        SelectedMember = Members.FirstOrDefault();
    }

    /// <summary>NL-набор выбирается из тех же наборов цели.</summary>
    protected override void OnForceSetsRefreshed()
    {
        int keep = _selectedNlForceSet?.Id ?? 0;
        NlForceSets.Clear();
        NlForceSets.Add(new ForceSet { Id = 0, Tag = Loc.S("FemSlsDlgNlNone") });
        foreach (var item in FilteredForceSets)
            NlForceSets.Add(item.ForceSet);
        // Выбранный набор NL переживает пересборку списка (смена цели), если он в новом списке.
        SelectedNlForceSet = NlForceSets.FirstOrDefault(f => f.Id == keep && keep > 0) ?? NlForceSets[0];
    }

    /// <summary>Автоимя «цель / метод (ПС2)» без повтора в схеме.</summary>
    string AutoTag() => _selectedSchema == null ? "" : FemCheckAuthoring.FreeTag(App.db.FemChecks, _selectedSchema.Id,
        FemCheckAuthoring.AutoTag(_selectedMember?.Group?.Tag ?? _selectedMember?.Element?.ElemTag ?? "",
            FemCheckAuthoring.RcPlate, _selectedSlsKind?.Kind, sls: true), _existing?.Id ?? 0);

    /// <summary>Автоимя — только пока пользователь имя не менял (пусто или равно последнему подставленному).</summary>
    void AutoFillTag()
    {
        if (_loading || _selectedMember == null || _selectedSlsKind == null) return;
        if (Tag.Trim().Length > 0 && Tag != _lastAutoTag) return;
        Tag = _lastAutoTag = AutoTag();
    }

    /// <summary>Черновик проверки по полям окна (Id и результат правимой); правимая проверка не меняется.</summary>
    public FemCheck BuildCheck() => FillCheck(new FemCheck { Id = _existing?.Id ?? 0, ResultId = _existing?.ResultId });

    /// <summary>Число поля; нераспознанное — −1 (отклонит проверка службы; поля окна до этого проверяет FieldCheck).</summary>
    static double Num(string text) => NumberInput.TryParse(text, out double v) && double.IsFinite(v) ? v : -1;

    /// <summary>Записывает состояние диалога в проверку.</summary>
    FemCheck FillCheck(FemCheck check)
    {
        string forceSetIdsJson = "[]";
        if (!AllSets)
        {
            var ids = SelectedForceSets().Select(f => f.Id).ToArray();
            forceSetIdsJson = ids.Length > 0 ? JsonSerializer.Serialize(ids) : "[]";
        }

        int nlId = (_selectedNlForceSet?.Id ?? 0) > 0 ? _selectedNlForceSet!.Id : 0;
        // Поля, которых нет в окне (σs,crc, γ, шаг Капра–Мори), — из правимой проверки; скрытые поля — прежние.
        var basis = _existing?.NormCode == "rc_plate_check" ? PlateCheckParams.Parse(_existing.ParamsJson) : new PlateCheckParams();
        bool layered = IsLayered;
        bool phi1Manual = !layered && _selectedPhi1Mode?.Mode == "manual";

        var paramsJson = (basis with
        {
            Kind         = _selectedSlsKind?.Kind ?? "shell_layered",
            AcrcLimMm    = Num(AcrcLimMm),
            Phi2         = Num(Phi2),
            Phi1Mode     = _selectedPhi1Mode?.Mode ?? "auto",
            Phi1         = phi1Manual ? Num(Phi1) : basis.Phi1,
            CheckGroup   = "sls",
            StripModel   = SelectedStripModel,
            CapriCheckMode = SelectedCapriCheckMode,
            NlForceSetId = layered ? nlId : 0,
            LtFraction   = layered && nlId == 0 ? Num(LtFraction) : layered ? 0.0 : basis.LtFraction,
            RebarSources = SelectedRebarSources(),
        }).ToJson();

        check.SchemaId         = _selectedSchema!.Id;
        check.MemberId         = _selectedMember!.Kind == "group"   ? _selectedMember.Id : 0;
        check.ElementId        = _selectedMember!.Kind == "element" ? _selectedMember.Id : null;
        check.NormCode         = "rc_plate_check";
        check.Tag              = string.IsNullOrWhiteSpace(Tag) ? AutoTag() : Tag.Trim();
        check.ForceSetIdsJson  = forceSetIdsJson;
        check.CalcTypeOverride = _selectedCalcTypeOption?.Code;
        check.ParamsJson       = paramsJson;
        return check;
    }
}
