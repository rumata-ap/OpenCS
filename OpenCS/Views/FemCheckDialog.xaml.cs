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

public partial class FemCheckDialog : Window
{
    readonly AppViewModel _app;
    public FemCheck? ResultCheck { get; private set; }

    /// <param name="target">Группа, из меню которой вызвана команда: выбирается целью новой проверки.</param>
    public FemCheckDialog(AppViewModel app, FemCheck? existing = null, IFemCheckable? target = null)
    {
        _app = app;
        InitializeComponent();
        var vm = new FemCheckDialogVM(app, existing, ForceSetsBox);
        DataContext = vm;
        if (existing == null && target != null) vm.SelectTarget(target);
        Owner = Application.Current.MainWindow;
    }

    /// <summary>
    /// Числа полей — у поля (<see cref="FieldCheck"/>), затем проверка службы (имя без повтора в схеме, цель, наборы,
    /// параметры). Окно возвращает черновик; запись и сброс результата — <see cref="FemCheckAuthoring.Save"/>.
    /// </summary>
    void Ok_Click(object sender, RoutedEventArgs e)
    {
        var vm = (FemCheckDialogVM)DataContext;
        if (vm.SelectedMember == null || vm.SelectedSchema == null)
        {
            MessageBox.Show(this, Loc.S("FemCheckTargetMissing"), Title, MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        FieldCheck.Clear(AcrcBox, Phi1Box);
        if (AcrcBox.IsVisible && !FieldCheck.Read(this, AcrcBox, "FemCheckDlgAcrcLim", out _, FieldCheck.Positive)) return;
        if (Phi1Box.IsVisible && !FieldCheck.Read(this, Phi1Box, "FemCheckPhi1Label", out _, FieldCheck.Positive)) return;
        if (!BucklingEditor.CheckFields(this)) return;
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

/// <summary>Ошибки службы в окне проверки: сообщение; ошибка имени — ещё и текстом под полем (рамку поля в фокусе
/// перекрашивает шаблон TextBox).</summary>
static class FemCheckDialogErrors
{
    /// <summary>true — ошибок нет.</summary>
    public static bool Show(Window owner, IReadOnlyList<FieldError> errors, TextBox tagBox, TextBlock tagError)
    {
        tagError.Visibility = Visibility.Collapsed;
        if (errors.Count == 0) return true;
        MessageBox.Show(owner, string.Join(Environment.NewLine, errors.Select(err => err.Message).Distinct()), owner.Title,
            MessageBoxButton.OK, MessageBoxImage.Warning);
        if (errors.FirstOrDefault(err => err.Path == "tag") is { } tagErr)
        {
            tagError.Text = tagErr.Message;
            tagError.Visibility = Visibility.Visible;
            tagBox.Focus();
            tagBox.SelectAll();
        }
        return false;
    }
}

/// <summary>Обёртка над целью проверки для комбобокса диалога: либо группа (FemMemberGroup),
/// либо одиночный конструктивный элемент (FemMember). Кладём оба варианта в один список,
/// чтобы не плодить два выпадающих списка.</summary>
public sealed class FemCheckTarget
{
    public required string Kind { get; init; } // "group" | "element"
    public required int    Id   { get; init; }
    public required string Tag  { get; init; }
    public FemMemberGroup? Group   { get; init; }
    public FemMember?      Element { get; init; }
}

public class FemCheckDialogVM : FemCheckDialogVmBase
{
    readonly FemCheck?    _existing;

    public ObservableCollection<FemSchema> Schemas { get; }
    public ObservableCollection<FemCheckTarget> Members { get; } = [];

    FemSchema? _selectedSchema;
    public FemSchema? SelectedSchema
    {
        get => _selectedSchema;
        set
        {
            _selectedSchema = value;
            OnPropertyChanged();
            Buckling.SetAnalyses(value == null ? [] : value.Analyses.Count > 0 ? value.Analyses.ToList() : App.db.GetFemAnalyses(value.Id));
            ReloadSchema();
            RefreshMembers();
        }
    }

    FemCheckTarget? _selectedMember;
    public FemCheckTarget? SelectedMember
    {
        get => _selectedMember;
        set { _selectedMember = value; OnPropertyChanged(); RefreshTarget(); AutoFillTag(); }
    }

    protected override IFemCheckable? Target => (IFemCheckable?)_selectedMember?.Group ?? _selectedMember?.Element;
    protected override int? SchemaId => _selectedSchema?.Id;
    protected override bool IsPlateCheck => IsPlate;
    protected override bool IsBarRcCheck => _selectedNormCode?.Code == "rc_check";
    protected override FemCheck DraftCheck() => FillCheck(new FemCheck());

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
            Buckling.NormCode = value?.Code;
            // Вид проверки меняет вид КЭ цели (пластины / стержни) — наборы и готовность другие.
            RefreshTarget();
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
            OnPropertyChanged(nameof(StripModelVisibility));
            OnPropertyChanged(nameof(CapriCheckModeVisibility));
            AutoFillTag();
        }
    }

    /// <summary>Модель полосы — только у упрощённых видов (Вуд–Армер, Капра–Мори).</summary>
    public Visibility StripModelVisibility =>
        _selectedPlateKind?.Kind.StartsWith("shell_simpl") == true ? Visibility.Visible : Visibility.Collapsed;

    public Visibility CapriCheckModeVisibility => CapriCheckModeVisibilityOf(_selectedPlateKind?.Kind);

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

    // ── Продольный изгиб: η у ЖБ, расчётные длины по сетке у стали ─────────────────
    public FemCheckBucklingVM Buckling { get; } = new();

    // ── CalcType ──────────────────────────────────────────────────────────────
    public record CalcTypeOption(string? Code, string Label);
    public List<CalcTypeOption> CalcTypeOptions { get; } =
    [
        new(null,  Loc.S("FemCheckDlgCalcTypeAuto")),
        new("C",   Loc.S("FemCheckCalcTypeC")),
        new("CL",  Loc.S("FemCheckCalcTypeCL")),
        new("N",   Loc.S("FemCheckCalcTypeN")),
        new("NL",  Loc.S("FemCheckCalcTypeNL")),
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
    public FemCheckDialogVM(AppViewModel app, FemCheck? existing, ListBox setsBox) : base(app, setsBox)
    {
        _existing = existing;
        Schemas   = app.FemSchemas;
        if (existing != null) KeptForceSetIds = existing.GetForceSetIds().ToHashSet();

        _selectedNormCode       = NormCodes[0];
        Buckling.NormCode       = _selectedNormCode.Code;
        _selectedCalcTypeOption = CalcTypeOptions[0];
        _selectedPlateKind      = PlateKinds[0];
        _selectedPhi1Mode       = Phi1Modes[0]; // auto

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

    /// <summary>Выбрать целью группу или конструктивный элемент (схема — его схема).</summary>
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
        SelectedSchema        = Schemas.FirstOrDefault(s => s.Id == check.SchemaId);
        SelectedMember        = check.TargetsElement
            ? Members.FirstOrDefault(t => t.Kind == "element" && t.Id == check.ElementId)
            : Members.FirstOrDefault(t => t.Kind == "group"   && t.Id == check.MemberId);
        SelectedNormCode      = NormCodes.FirstOrDefault(n => n.Code == check.NormCode) ?? NormCodes[0];
        SelectedCalcTypeOption = CalcTypeOptions.FirstOrDefault(o => o.Code == check.CalcTypeOverride)
                                 ?? CalcTypeOptions[0];
        Tag     = check.Tag;
        AllSets = check.IsAllSets;
        Buckling.Load(check);

        if (check.NormCode == "rc_plate_check" && !string.IsNullOrWhiteSpace(check.ParamsJson))
        {
            var p = PlateCheckParams.Parse(check.ParamsJson);
            SelectedPlateKind  = PlateKinds.FirstOrDefault(k => k.Kind == p.Kind) ?? PlateKinds[0];
            AcrcLimMm          = p.AcrcLimMm.ToString("G");
            SelectedPhi1Mode   = Phi1Modes.FirstOrDefault(m => m.Mode == p.Phi1Mode) ?? Phi1Modes[0];
            Phi1               = p.Phi1.ToString("G");
            StripModelIndex    = StripModelIndexOf(p.StripModel);
            CapriCheckModeIndex = p.CapriCheckMode == CScore.CapriCheckMode.Constraints ? 1 : 0;
            LoadRebarSources(p);
        }
        else if (check.NormCode == "rc_check")
        {
            var p = BarCheckParams.Parse(check.ParamsJson);
            LoadRebarSources(p.RebarSources);
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
        foreach (var e in App.GetFemMembers(_selectedSchema))
            Members.Add(new FemCheckTarget { Kind = "element", Id = e.Id, Tag = string.Format(Loc.S("FemCheckDlgTargetElement"), e.ElemTag), Element = e });
        SelectedMember = Members.FirstOrDefault();
    }

    /// <summary>Имя цели без пометки «[Группа]»/«[Элемент]».</summary>
    string TargetTag => _selectedMember?.Group?.Tag ?? _selectedMember?.Element?.ElemTag ?? "";

    /// <summary>Автоимя «цель / вид» без повтора в схеме.</summary>
    string AutoTag() => _selectedSchema == null ? "" : FemCheckAuthoring.FreeTag(App.db.FemChecks, _selectedSchema.Id,
        FemCheckAuthoring.AutoTag(TargetTag, _selectedNormCode?.Code ?? FemCheckAuthoring.Steel,
            IsPlate ? _selectedPlateKind?.Kind : null), _existing?.Id ?? 0);

    /// <summary>Автоимя — только пока пользователь имя не менял (пусто или равно последнему подставленному).</summary>
    void AutoFillTag()
    {
        if (_loading || _selectedMember == null || _selectedNormCode == null) return;
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

        string? paramsJson = null;
        // Поля, которых нет в окне (шаг Капра–Мори, σs,crc, γ), берутся из правимой проверки того же вида.
        bool sameKind = _existing?.NormCode == _selectedNormCode?.Code;
        if (IsPlate && _selectedPlateKind != null)
        {
            bool sls = AcrcRowVisibility == Visibility.Visible;
            var basis = sameKind ? PlateCheckParams.Parse(_existing!.ParamsJson) : new PlateCheckParams();
            paramsJson = (basis with
            {
                Kind       = _selectedPlateKind.Kind,
                AcrcLimMm  = sls ? Num(AcrcLimMm) : basis.AcrcLimMm,
                Phi1Mode   = _selectedPhi1Mode?.Mode ?? "auto",
                Phi1       = sls && _selectedPhi1Mode?.Mode == "manual" ? Num(Phi1) : basis.Phi1,
                CheckGroup = "uls",
                StripModel = SelectedStripModel,
                CapriCheckMode = SelectedCapriCheckMode,
                RebarSources = SelectedRebarSources(),
                Eta = Buckling.BuildEta(),
            }).ToJson();
        }
        else if (IsBarRcCheck)
            paramsJson = ((sameKind ? BarCheckParams.Parse(_existing!.ParamsJson) : new BarCheckParams())
                with { RebarSources = SelectedRebarSources(), Eta = Buckling.BuildEta() }).ToJson();
        else if (_selectedNormCode?.Code == "steel_check")
            paramsJson = Buckling.BuildSteel().ToJson();

        check.SchemaId         = _selectedSchema!.Id;
        check.MemberId         = _selectedMember!.Kind == "group"   ? _selectedMember.Id : 0;
        check.ElementId        = _selectedMember!.Kind == "element" ? _selectedMember.Id : null;
        check.NormCode         = _selectedNormCode?.Code ?? "steel_check";
        check.Tag              = string.IsNullOrWhiteSpace(Tag) ? AutoTag() : Tag.Trim();
        check.ForceSetIdsJson  = forceSetIdsJson;
        check.CalcTypeOverride = _selectedCalcTypeOption?.Code;
        check.ParamsJson       = paramsJson;
        return check;
    }
}
