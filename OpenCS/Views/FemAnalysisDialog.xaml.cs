using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using CScore;
using CScore.Fem;
using CScore.Fem.Combinations;
using OpenCS.OpenSees.CScore;
using OpenCS.Services;
using OpenCS.Tasks;
using OpenCS.Utilites;

namespace OpenCS.Views;

/// <summary>Диалог создания постановки расчёта схемы: OpenSees (линейный/нелинейный) или секущий CSfea. Solver-
/// механика (исполняемый файл, сходимость, geomTransf и т.п.) — глобальная, см. вкладку
/// «OpenSees» в диалоге настроек (SettingsWindow). Настройки материалов специфичны для
/// конкретной постановки и хранятся здесь.</summary>
public partial class FemAnalysisDialog : Window
{
    readonly FemSchema _schema;
    readonly IReadOnlyList<FemNode> _nodes;
    readonly System.Collections.ObjectModel.ObservableCollection<StageRow> _stages = [];
    /// <summary>Комбинации и загружения схемы для окна выбора нагрузки.</summary>
    readonly List<ViewModels.FemLoadSource> _loadSources;
    /// <summary>Нагрузка одиночного расчёта (линейный OpenSees) — та же ячейка, что у стадии.</summary>
    readonly StageRow _single = new();
    readonly ViewModels.FemLinearCasesVM _linearCases;
    /// <summary>Элементы пакета секущего CSfea (комбинации и загружения, отмеченные в окне выбора).</summary>
    readonly System.Collections.ObjectModel.ObservableCollection<ViewModels.FemLoadSource> _batch = [];
    /// <summary>Узлы, оболочки и стержни сетки схемы для оценки объёма пакета; null — ещё не прочитаны или нет БД.</summary>
    (int Nodes, int Shells, int Beams)? _meshCounts;

    /// <summary>Сформированная постановка (валидна после DialogResult == true).</summary>
    public FemAnalysis Result { get; private set; } = new();

    /// <summary>Режим параметров нелинейного расчёта субмодели: тег, вид, источник нагрузки и стадии скрыты
    /// (стадию до λ = 1 строит сервис), задаётся только шаг λ.</summary>
    public bool IsSubmodelMode { get; private set; }

    /// <summary>Параметры нелинейного расчёта субмодели (валидны после DialogResult == true в режиме субмодели).</summary>
    public FemAnalysisParams? ResultParams { get; private set; }

    FemCsfeaRunContext? _csfeaContext;

    /// <summary>Окружение кнопки «Проверить вход» секущего расчёта CSfea; null — кнопка скрыта.</summary>
    public FemCsfeaRunContext? CsfeaContext
    {
        get => _csfeaContext;
        set
        {
            _csfeaContext = value;
            _meshCounts = null;
            LoadStiffnessSources();
            UpdateNonlinearPanelVisibility();
            UpdateBatchSummary();
        }
    }

    /// <summary>Пригодные источники жёсткостей схемы; null — БД недоступна (окружение не задано).</summary>
    List<FemCsfeaStiffnessCandidate>? _stiffnessCandidates;
    /// <summary>Источник жёсткостей правимой постановки.</summary>
    FemCsfeaStiffnessSource? _savedStiffnessSource;
    /// <summary>Состояние сохранённого источника по текущим записям; null — не проверялось.</summary>
    FemCsfeaStiffnessSourceStatus? _savedStiffnessStatus;
    bool _stiffnessLoading;

    /// <summary>Секущая постановка в списке источников жёсткостей.</summary>
    sealed record StiffnessAnalysisItem(int Id, string Label);

    /// <summary>Запись источника в списке состояний постановки.</summary>
    sealed record StiffnessStateItem(FemCsfeaStiffnessCandidate Candidate, string Label);

    /// <summary>
    /// Диалог параметров нелинейного расчёта субмодели. Начальные значения — из <paramref name="current"/>
    /// (параметры, заданные ранее в этом сеансе), иначе из существующей постановки <paramref name="existing"/>.
    /// </summary>
    public static FemAnalysisDialog ForSubmodelNonlinear(FemSchema schema, IReadOnlyList<FemNode> nodes,
        FemAnalysis? existing, FemAnalysisParams? current)
    {
        var source = current is not null
            ? new FemAnalysis { Kind = "nonlinear", Tag = existing?.Tag ?? "", ParamsJson = current.ToJson(), LoadExpressionJson = "{}" }
            : existing is { Kind: "nonlinear" } ? existing : null;
        var dialog = new FemAnalysisDialog(schema, nodes, source) { IsSubmodelMode = true };
        dialog.SelectKind("nonlinear");
        var pars = source is null ? null : FemAnalysisParams.Parse(source.ParamsJson);
        double step = pars?.Stages.FirstOrDefault()?.LoadFactorStep ?? pars?.LoadFactorStep ?? 0.1;
        dialog.LoadFactorStepBox.Text = step.ToString(CultureInfo.CurrentCulture);
        dialog.Title = Loc.S("SubmodelNonlinearParamsTitle");
        dialog.TagRow.Visibility = Visibility.Collapsed;
        dialog.KindRow.Visibility = Visibility.Collapsed;
        dialog.NonlinearSeparator.Visibility = Visibility.Collapsed;
        dialog.LoadFactorStepRow.Visibility = Visibility.Visible;
        dialog.UpdateNonlinearPanelVisibility();
        return dialog;
    }

    /// <param name="gammaDefaults">γf по умолчанию (настройки расчёта) — для столбца γf списка загружений линейного CSfea.</param>
    public FemAnalysisDialog(FemSchema schema, IReadOnlyList<FemNode> nodes, FemAnalysis? existing = null,
        CScore.Combinations.Sp20GammaDefaults? gammaDefaults = null)
    {
        _schema = schema;
        _nodes = nodes;
        InitializeComponent();
        // Начальный выбор — в коде: SelectionChanged из разметки пришёл бы до создания панелей ниже.
        BucklingMethodCombo.SelectedIndex = 0;
        BucklingSwayCombo.SelectedIndex = 0;
        BucklingForcesCombo.SelectedIndex = 0;
        // Загружения линейного CSfea: у правимой линейной постановки — отмечены её стадии, иначе все.
        var linearStages = existing?.Kind == FemCsfeaRunner.LinearAnalysisKind
            ? FemAnalysisParams.Parse(existing.ParamsJson).ResolveStages(existing) : null;
        _linearCases = new ViewModels.FemLinearCasesVM(schema.LoadCases.ToList(),
            gammaDefaults ?? CScore.Combinations.Sp20GammaDefaults.Standard, linearStages);
        LinearCasesGroup.DataContext = _linearCases;
        KindBox.ItemsSource = _kindOptions;
        _loadSources = ViewModels.FemLoadSources.Build(schema.LoadDefinitions, schema.LoadCases,
            gammaDefaults ?? CScore.Combinations.Sp20GammaDefaults.Standard);
        LoadSourceBox.DataContext = _single;
        StagesGrid.ItemsSource = _stages;
        BatchGrid.ItemsSource = _batch;
        BatchStepBox.Text = 0.1.ToString(CultureInfo.CurrentCulture);
        BatchMaxBox.Text = 1.0.ToString(CultureInfo.CurrentCulture);
        StagesPathControlColumn.ItemsSource = BuildPathControlModeOptions();
        CalcTypeBox.ItemsSource = Enum.GetValues<CalcType>();
        var materialSourceOptions = BuildMaterialSourceOptions();
        var mainMaterialModelOptions = BuildMainMaterialModelOptions();
        var steelModelOptions = BuildSteelModelOptions();
        var elementFormulationOptions = BuildElementFormulationOptions();
        MaterialSourceBox.ItemsSource = materialSourceOptions;
        MainMaterialModelBox.ItemsSource = mainMaterialModelOptions;
        SteelModelBox.ItemsSource = steelModelOptions;
        ElementFormulationBox.ItemsSource = elementFormulationOptions;
        MaterialSourceBox.SelectionChanged += (_, _) => UpdateNativeMaterialPanelVisibility();
        ConsiderPhysicalNonlinearityCb.Checked += (_, _) => UpdateMaterialNonlinearityPanelVisibility();
        ConsiderPhysicalNonlinearityCb.Unchecked += (_, _) => UpdateMaterialNonlinearityPanelVisibility();
        CsfeaCalcTypeBox.ItemsSource = Enum.GetValues<CalcType>();
        CsfeaCalcTypeBox.SelectionChanged += (_, _) => { UpdateBatchSummary(); UpdateCsfeaStiffnessState(); };
        CsfeaPlateRebarSourceBox.ItemsSource = CsfeaRebarSourceOptions;
        CsfeaTensionConcreteBox.ItemsSource = CsfeaTensionOptions;
        CsfeaPlateCrackRuleBox.ItemsSource = CsfeaCrackRuleOptions;
        CsfeaControlDofBox.ItemsSource = CsfeaDofLabels;

        bool isCsfeaExisting = FemCsfeaRunner.IsCsfeaKind(existing?.Kind);
        var existingPars = existing == null ? null : FemAnalysisParams.Parse(existing.ParamsJson);
        LoadCsfeaParams(isCsfeaExisting ? existingPars!.Csfea ?? new FemCsfeaParams() : new FemCsfeaParams(),
            isCsfeaExisting ? existingPars!.CalcType ?? CalcType.N : CalcType.N);

        if (existing != null)
        {
            Title = Loc.S("FemAnalysisEdit");
            TagBox.Text = existing.Tag;
            var pars = existingPars!;
            CalcTypeBox.SelectedItem = pars.CalcType ?? CalcType.C;
            ConsiderPhysicalNonlinearityCb.IsChecked = pars.ConsiderPhysicalNonlinearity;
            ConsiderConcreteTensionCb.IsChecked = pars.ConsiderConcreteTension;
            MaterialSourceBox.SelectedItem = materialSourceOptions.FirstOrDefault(o => o.Value == pars.MaterialSource) ?? materialSourceOptions[0];
            MainMaterialModelBox.SelectedItem = mainMaterialModelOptions.FirstOrDefault(o => o.Value == pars.MainMaterialModel) ?? mainMaterialModelOptions[1];
            SteelModelBox.SelectedItem = steelModelOptions.FirstOrDefault(o => o.Value == pars.SteelModel) ?? steelModelOptions[1];
            SteelHardeningModulusBox.Text = pars.SteelHardeningModulusMpa?.ToString(CultureInfo.InvariantCulture) ?? "";
            ElementFormulationBox.SelectedItem = elementFormulationOptions.FirstOrDefault(o => o.Value == pars.ElementFormulation) ?? elementFormulationOptions[0];

            bool isNonlinearExisting = existing.Kind == "nonlinear";
            if (isNonlinearExisting || isCsfeaExisting)
            {
                foreach (var stage in pars.ResolveStages(existing))
                {
                    var row = new StageRow
                    {
                        Tag = stage.Tag, Source = ResolveSource(stage.LoadExpressionJson),
                        LoadFactorStep = stage.LoadFactorStep ?? 0.1,
                        MaxLoadFactor = stage.MaxLoadFactor ?? (isCsfeaExisting ? 1.0 : 10.0)
                    };
                    ApplyPathControlDto(row, stage.PathControl, isContinuation: false);
                    ApplyPathControlDto(row, stage.ContinueWith, isContinuation: true);
                    _stages.Add(row);
                }
            }
            if (existing.Kind == FemBucklingRunner.AnalysisKind)
            {
                foreach (var stage in pars.ResolveStages(existing)) _batch.Add(ResolveSource(stage.LoadExpressionJson));
                var bp = pars.Buckling ?? new FemBucklingParams();
                BucklingModesBox.Text = bp.Modes.ToString(CultureInfo.CurrentCulture);
                SelectTag(BucklingMethodCombo, bp.Method);
                SelectTag(BucklingForcesCombo, bp.Envelope ? FemBucklingParams.ForcesEnvelope : FemBucklingParams.ForcesGoverning);
                SelectTag(BucklingSwayCombo, bp.Sway);
                BucklingSwayXCb.IsChecked = bp.SwayX;
                BucklingSwayYCb.IsChecked = bp.SwayY;
                BucklingSwayStiffnessBox.Text = bp.SwayStiffness.ToString(CultureInfo.CurrentCulture);
                BucklingGFixedBox.Text = bp.GFixed.ToString(CultureInfo.CurrentCulture);
                BucklingGPinnedBox.Text = bp.GPinned.ToString(CultureInfo.CurrentCulture);
                UpdateBucklingMethod();
            }
            if (isCsfeaExisting && existing.Kind == FemCsfeaRunner.AnalysisKind && pars.Batch is { } batch)
            {
                foreach (var item in batch.Items) _batch.Add(ResolveSource(item.LoadExpressionJson));
                BatchStepBox.Text = batch.LoadFactorStep.ToString(CultureInfo.CurrentCulture);
                BatchMaxBox.Text = batch.MaxLoadFactor.ToString(CultureInfo.CurrentCulture);
                BatchStopAtLimitCb.IsChecked = batch.StopAtLimit;
                BatchLongNCb.IsChecked = batch.HasLongTermPairs(FemCombinationTypes.N);
                BatchLongDeflectionCb.IsChecked = batch.LongTermDeflection;
                BatchLongCCb.IsChecked = batch.HasLongTermPairs(FemCombinationTypes.C);
                (batch.Recording == FemCsfeaBatchRecording.Envelopes ? BatchRecordEnvelopesRadio : BatchRecordFieldsRadio).IsChecked = true;
                BatchRadio.IsChecked = true;
            }
            // Устанавливается ПОСЛЕ заполнения _stages: выбор вида синхронно
            // поднимает Checked → UpdateNonlinearPanelVisibility, которая добавляет служебную
            // стадию-заглушку, если _stages ещё пуст — иначе к уже смигрированным стадиям
            // добавлялась лишняя дублирующая (баг: расчёт с одной стадией сохранялся с двумя).
            SelectKind(existing.Kind);

            if (existing.Kind == "linear") _single.Source = ResolveSource(existing.LoadExpressionJson);
        }
        else
        {
            CalcTypeBox.SelectedItem = CalcType.C;
            MaterialSourceBox.SelectedItem = materialSourceOptions[0];
            MainMaterialModelBox.SelectedItem = mainMaterialModelOptions[1];
            SteelModelBox.SelectedItem = steelModelOptions[1];
            ElementFormulationBox.SelectedItem = elementFormulationOptions[0];
            SelectKind(_kindOptions[0].Value);   // по умолчанию — свой решатель
        }
        UpdateNonlinearPanelVisibility();
        UpdateNativeMaterialPanelVisibility();
        UpdateMaterialNonlinearityPanelVisibility();
        UpdateBatchSummary();
    }

    /// <summary>Пара «значение для Tcl/хранения» + «локализованная подпись для UI».
    /// `internal`, не default `private` — нужен извне сборки-члена `FemAnalysisDialog`:
    /// `FemPathControlDialog` (отдельный класс того же namespace, другого файла) читает
    /// `StageRow.PathControlMode`/`ContinueWithMode` (тип `ComboOption`) и создаёт новые
    /// значения этого типа.</summary>
    internal sealed record ComboOption(string Value, string Label);

    static List<ComboOption> BuildPathControlModeOptions() =>
    [
        new("LoadControl", Loc.S("FemPathControlModeLoadControl")),
        new("DisplacementControl", Loc.S("FemPathControlModeDisplacementControl")),
        new("ArcLength", Loc.S("FemPathControlModeArcLength")),
    ];

    /// <summary>Строка редактора стадий: имя + выбранный источник нагрузки + способ
    /// управления траекторией. С уведомлением — имя и источник (меняются окном выбора нагрузки) и PathControlMode
    /// (CellStyle-триггер затемняет колонки «Шаг λ»/«Предел λ» для не-LoadControl режимов); остальные свойства
    /// читает только код сборки при Ok_Click.</summary>
    internal sealed class StageRow : System.ComponentModel.INotifyPropertyChanged
    {
        string _tag = "";
        public string Tag
        {
            get => _tag;
            set
            {
                if (_tag == value) return;
                _tag = value;
                PropertyChanged?.Invoke(this, new(nameof(Tag)));
            }
        }

        ViewModels.FemLoadSource? _source;
        public ViewModels.FemLoadSource? Source
        {
            get => _source;
            set
            {
                if (ReferenceEquals(_source, value)) return;
                _source = value;
                PropertyChanged?.Invoke(this, new(nameof(Source)));
            }
        }
        public double LoadFactorStep { get; set; } = 0.1;
        public double MaxLoadFactor { get; set; } = 10.0;

        ComboOption _pathControlMode = BuildPathControlModeOptions()[0];
        public ComboOption PathControlMode
        {
            get => _pathControlMode;
            set
            {
                if (Equals(value, _pathControlMode)) return;
                _pathControlMode = value;
                PropertyChanged?.Invoke(this, new(nameof(PathControlMode)));
            }
        }

        public FemDisplacementControlInput? DisplacementControl { get; set; }
        public FemArcLengthInput? ArcLength { get; set; }
        public ComboOption? ContinueWithMode { get; set; }
        public FemDisplacementControlInput? ContinueWithDisplacementControl { get; set; }
        public FemArcLengthInput? ContinueWithArcLength { get; set; }

        public event System.ComponentModel.PropertyChangedEventHandler? PropertyChanged;
    }

    readonly List<ComboOption> CsfeaRebarSourceOptions =
    [
        new(FemCheckRebarSource.Section, Loc.S("FemCheckSourceSection")),
        new(FemCheckRebarSource.Assigned, Loc.S("FemCsfeaRebarSourceAssigned")),
        new(FemCheckRebarSource.Selected, Loc.S("FemCsfeaRebarSourceSelected")),
        new(FemCheckRebarSource.Layout, Loc.S("FemCheckSourceLayout")),
    ];

    /// <summary>Растяжение бетона: «как в сечении» (null), да, нет.</summary>
    readonly List<ComboOption> CsfeaTensionOptions =
    [
        new("", Loc.S("FemCsfeaTensionInherit")),
        new("true", Loc.S("FemCsfeaTensionYes")),
        new("false", Loc.S("FemCsfeaTensionNo")),
    ];

    readonly List<ComboOption> CsfeaCrackRuleOptions =
    [
        new(nameof(CSfea.CScoreBridge.Structural.PlateCrackRule.Layer), Loc.S("FemCsfeaCrackRuleLayer")),
        new(nameof(CSfea.CScoreBridge.Structural.PlateCrackRule.Section), Loc.S("FemCsfeaCrackRuleSection")),
    ];

    /// <summary>DOF контрольного узла по индексу: перемещения, затем повороты.</summary>
    static readonly string[] CsfeaDofLabels = ["ux", "uy", "uz", "rx", "ry", "rz"];

    void LoadCsfeaParams(FemCsfeaParams c, CalcType calc)
    {
        var culture = CultureInfo.CurrentCulture;
        CsfeaCalcTypeBox.SelectedItem = calc;
        CsfeaPlateRebarSourceBox.SelectedItem = CsfeaRebarSourceOptions.FirstOrDefault(o => o.Value == c.PlateRebarSource)
            ?? CsfeaRebarSourceOptions[0];
        string tension = c.TensionConcrete switch { true => "true", false => "false", null => "" };
        CsfeaTensionConcreteBox.SelectedItem = CsfeaTensionOptions.First(o => o.Value == tension);
        CsfeaPlateCrackRuleBox.SelectedItem = CsfeaCrackRuleOptions.FirstOrDefault(o => o.Value == c.PlateCrackRule.ToString())
            ?? CsfeaCrackRuleOptions[0];
        CsfeaPoissonBox.Text = c.PoissonUncracked?.ToString(culture) ?? "";
        CsfeaPhysicalNonlinearityCb.IsChecked = c.PhysicalNonlinearity;
        CsfeaPsiCb.IsChecked = c.Psi;
        CsfeaBeamShearCb.IsChecked = c.BeamShear;
        CsfeaImportedFactorsCb.IsChecked = c.ImportedStiffnessFactors;
        CsfeaGeomNonlinearCb.IsChecked = c.GeomNonlinear;
        CsfeaShellInPlanePDeltaCb.IsChecked = c.ShellInPlanePDelta;
        CsfeaLargeDisplacementsCb.IsChecked = c.GeomLargeDisplacements;
        CsfeaMaxIterationsBox.Text = c.MaxIterations.ToString(culture);
        CsfeaMaxBisectionsBox.Text = c.MaxBisections.ToString(culture);
        CsfeaTolDisplacementBox.Text = c.TolDisplacement.ToString(culture);
        CsfeaTolStiffnessBox.Text = c.TolStiffness.ToString(culture);
        CsfeaOmega0Box.Text = c.Omega0.ToString(culture);
        CsfeaRecordStepsBox.Text = c.RecordSteps;
        CsfeaRecordStageEndsCb.IsChecked = c.RecordStageEnds;
        CsfeaRecordStrainsCb.IsChecked = c.RecordSectionStrains;
        (c.ResultRecording switch
        {
            FemCsfeaRecording.Selected => CsfeaRecordSelectedRadio,
            FemCsfeaRecording.All => CsfeaRecordAllRadio,
            _ => CsfeaRecordFinalRadio,
        }).IsChecked = true;
        CsfeaControlNodeBox.Text = c.ControlNodeTag;
        CsfeaControlDofBox.SelectedIndex = c.ControlDof is >= 0 and <= 5 ? c.ControlDof : 2;
        _savedStiffnessSource = c.StiffnessSource;
        (c.StiffnessSource != null ? CsfeaStiffnessSecantRadio : CsfeaStiffnessInitialRadio).IsChecked = true;
        UpdateCsfeaRecordingState();
    }

    /// <summary>
    /// Список источников жёсткостей схемы из БД окружения; выбор — сохранённый источник правимой постановки или первая
    /// постановка. Состояние сохранённого источника (пересчитан, пропал) — для предупреждения.
    /// </summary>
    void LoadStiffnessSources()
    {
        _stiffnessCandidates = _csfeaContext?.Db is { } db ? FemCsfeaStiffnessSources.List(db, _schema.Id) : null;
        _savedStiffnessStatus = null;
        if (_csfeaContext?.Db is { } db2 && _savedStiffnessSource is { } saved)
            _savedStiffnessStatus = FemCsfeaStiffnessSources.Status(db2, _schema.Id, saved);
        _stiffnessLoading = true;
        try
        {
            var analyses = (_stiffnessCandidates ?? []).GroupBy(c => c.Analysis.Id)
                .Select(g => new StiffnessAnalysisItem(g.Key, $"{g.First().Analysis.Tag} — {g.First().Calc}")).ToList();
            CsfeaStiffnessAnalysisBox.ItemsSource = analyses;
            CsfeaStiffnessAnalysisBox.SelectedItem = analyses.FirstOrDefault(a => a.Id == _savedStiffnessSource?.AnalysisId)
                ?? analyses.FirstOrDefault();
            FillStiffnessStates();
        }
        finally { _stiffnessLoading = false; }
        UpdateCsfeaStiffnessState();
    }

    /// <summary>Записи выбранной постановки: конечное состояние или варианты пакета.</summary>
    void FillStiffnessStates()
    {
        int? id = (CsfeaStiffnessAnalysisBox.SelectedItem as StiffnessAnalysisItem)?.Id;
        var states = (_stiffnessCandidates ?? []).Where(c => c.Analysis.Id == id)
            .Select(c => new StiffnessStateItem(c, c.IsFinalState ? Loc.S("FemCsfeaStiffnessFinalState") : c.Label)).ToList();
        CsfeaStiffnessStateBox.ItemsSource = states;
        CsfeaStiffnessStateBox.SelectedItem = states.FirstOrDefault(s => s.Candidate.Matches(_savedStiffnessSource))
            ?? states.FirstOrDefault();
    }

    void CsfeaStiffness_Changed(object sender, RoutedEventArgs e) => UpdateCsfeaStiffnessState();

    void CsfeaStiffnessAnalysis_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (_stiffnessLoading) return;
        FillStiffnessStates();
        UpdateCsfeaStiffnessState();
    }

    void CsfeaStiffnessState_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (!_stiffnessLoading) UpdateCsfeaStiffnessState();
    }

    /// <summary>Выбранный источник жёсткостей; null — начальные жёсткости или не выбран.</summary>
    FemCsfeaStiffnessCandidate? SelectedStiffnessCandidate => CsfeaStiffnessSecantRadio?.IsChecked == true
        ? (CsfeaStiffnessStateBox.SelectedItem as StiffnessStateItem)?.Candidate : null;

    /// <summary>
    /// Доступность списков, подпись источника и предупреждения: не дошёл до предела λ, вид расчёта не совпадает с видом
    /// постановки, сохранённый источник пересчитан или пропал. Флажок коэффициентов импорта — только при начальных.
    /// </summary>
    void UpdateCsfeaStiffnessState()
    {
        if (CsfeaStiffnessInfoText == null || CsfeaStiffnessWarningText == null || CsfeaStiffnessStateBox == null) return;
        bool secant = CsfeaStiffnessSecantRadio.IsChecked == true;
        bool any = _stiffnessCandidates is { Count: > 0 };
        CsfeaStiffnessAnalysisBox.IsEnabled = secant && any;
        CsfeaStiffnessStateBox.IsEnabled = secant && any;
        UpdateCsfeaImportedFactorsState();

        var warnings = new List<string>();
        string info = "";
        if (secant)
        {
            if (_stiffnessCandidates == null) info = Loc.S("FemCsfeaStiffnessNoDb");
            else if (!any) info = Loc.S("FemCsfeaStiffnessNoSources");
            if (SelectedStiffnessCandidate is { } c)
            {
                info = string.Format(Loc.S("FemCsfeaStiffnessSourceLabel"), c.Describe());
                if (!c.Completed) warnings.Add(Loc.S("FemCsfeaStiffnessNotCompleted"));
                var calc = CsfeaCalcTypeBox.SelectedItem as CalcType? ?? CalcType.N;
                if (c.Calc != calc) warnings.Add(string.Format(Loc.S("FemCsfeaStiffnessCalcMismatch"), c.Calc, calc));
                if (c.Matches(_savedStiffnessSource) && _savedStiffnessStatus == FemCsfeaStiffnessSourceStatus.Changed)
                    warnings.Add(Loc.S("FemCsfeaStiffnessChanged"));
            }
            if (_savedStiffnessStatus == FemCsfeaStiffnessSourceStatus.Missing)
                warnings.Add(Loc.S("FemCsfeaStiffnessMissing"));
        }
        CsfeaStiffnessInfoText.Text = info;
        CsfeaStiffnessInfoText.Visibility = info.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
        CsfeaStiffnessWarningText.Text = string.Join(Environment.NewLine, warnings);
        CsfeaStiffnessWarningText.Visibility = warnings.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    /// <summary>
    /// Источник жёсткостей линейной постановки с отпечатком текущей записи; null — начальные жёсткости. Без БД остаётся
    /// сохранённый источник. Ошибка (не выбран, нет записи или матриц) — <paramref name="error"/>.
    /// </summary>
    FemCsfeaStiffnessSource? ReadStiffnessSource(out string? error)
    {
        error = null;
        if (CsfeaStiffnessSecantRadio.IsChecked != true) return null;
        if (_csfeaContext?.Db is not { } db)
        {
            if (_savedStiffnessSource == null) error = Loc.S("FemCsfeaStiffnessNotSelected");
            return _savedStiffnessSource;
        }
        if (SelectedStiffnessCandidate is not { } c)
        {
            error = Loc.S("FemCsfeaStiffnessNotSelected");
            return null;
        }
        return FemCsfeaStiffnessSources.Fingerprint(db, c, out error);
    }

    /// <summary>Параметры CSfea из панели; null и текст ошибки — если значение вне допустимого.</summary>
    FemCsfeaParams? ReadCsfeaParams(out string? error)
    {
        error = null;
        var culture = CultureInfo.CurrentCulture;
        double? poisson = null;
        if (!string.IsNullOrWhiteSpace(CsfeaPoissonBox.Text))
        {
            if (!Pars.ParseAny(CsfeaPoissonBox.Text, out var nu) || !double.IsFinite(nu) || nu < 0 || nu >= 0.5)
            { error = Loc.S("FemCsfeaPoissonInvalid"); return null; }
            poisson = nu;
        }
        if (!int.TryParse(CsfeaMaxIterationsBox.Text, NumberStyles.Integer, culture, out int maxIter) || maxIter < 1)
        { error = Loc.S("FemCsfeaMaxIterationsInvalid"); return null; }
        if (!int.TryParse(CsfeaMaxBisectionsBox.Text, NumberStyles.Integer, culture, out int bisections) || bisections < 0)
        { error = Loc.S("FemCsfeaMaxBisectionsInvalid"); return null; }
        if (!Pars.ParseAny(CsfeaTolDisplacementBox.Text, out var tolU) || !double.IsFinite(tolU) || tolU <= 0
            || !Pars.ParseAny(CsfeaTolStiffnessBox.Text, out var tolK) || !double.IsFinite(tolK) || tolK <= 0)
        { error = Loc.S("FemCsfeaToleranceInvalid"); return null; }
        if (!Pars.ParseAny(CsfeaOmega0Box.Text, out var omega) || !double.IsFinite(omega) || omega <= 0 || omega > 1)
        { error = Loc.S("FemCsfeaOmega0Invalid"); return null; }

        string recording = CsfeaRecordSelectedRadio.IsChecked == true ? FemCsfeaRecording.Selected
            : CsfeaRecordAllRadio.IsChecked == true ? FemCsfeaRecording.All : FemCsfeaRecording.Final;
        bool stageEnds = CsfeaRecordStageEndsCb.IsChecked == true;
        if (recording == FemCsfeaRecording.Selected)
        {
            var steps = FemCsfeaParams.ParseRecordSteps(CsfeaRecordStepsBox.Text, out var stepsError);
            if (steps == null) { error = stepsError; return null; }
            if (steps.Count == 0 && !stageEnds) { error = Loc.S("FemCsfeaRecordStepsEmpty"); return null; }
        }

        string tension = (CsfeaTensionConcreteBox.SelectedItem as ComboOption)?.Value ?? "";
        return new FemCsfeaParams
        {
            PlateRebarSource = (CsfeaPlateRebarSourceBox.SelectedItem as ComboOption)?.Value ?? FemCheckRebarSource.Section,
            TensionConcrete = tension == "" ? null : tension == "true",
            Psi = CsfeaPsiCb.IsChecked == true,
            PlateCrackRule = Enum.TryParse<CSfea.CScoreBridge.Structural.PlateCrackRule>(
                (CsfeaPlateCrackRuleBox.SelectedItem as ComboOption)?.Value, out var rule) ? rule : default,
            BeamShear = CsfeaBeamShearCb.IsChecked == true,
            ImportedStiffnessFactors = CsfeaImportedFactorsCb.IsChecked == true,
            PoissonUncracked = poisson,
            PhysicalNonlinearity = CsfeaPhysicalNonlinearityCb.IsChecked == true,
            GeomNonlinear = CsfeaGeomNonlinearCb.IsChecked == true,
            ShellInPlanePDelta = CsfeaShellInPlanePDeltaCb.IsChecked == true,
            GeomLargeDisplacements = CsfeaLargeDisplacementsCb.IsChecked == true,
            MaxIterations = maxIter, TolDisplacement = tolU, TolStiffness = tolK, MaxBisections = bisections, Omega0 = omega,
            ResultRecording = recording,
            RecordSteps = CsfeaRecordStepsBox.Text.Trim(),
            RecordStageEnds = stageEnds,
            RecordSectionStrains = CsfeaRecordStrainsCb.IsChecked == true,
            ControlNodeTag = CsfeaControlNodeBox.Text.Trim(),
            ControlDof = Math.Max(0, CsfeaControlDofBox.SelectedIndex),
        };
    }

    void CsfeaRecording_Changed(object sender, RoutedEventArgs e) => UpdateCsfeaRecordingState();

    void CsfeaRecordStrains_Click(object sender, RoutedEventArgs e) => UpdateBatchSummary();

    void CsfeaPhysicalNonlinearity_Changed(object sender, RoutedEventArgs e) => UpdateCsfeaImportedFactorsState();

    /// <summary>
    /// Коэффициенты жёсткости импорта действуют в линейном расчёте и в секущем без физической нелинейности — иначе
    /// флажок недоступен.
    /// </summary>
    void UpdateCsfeaImportedFactorsState()
    {
        if (CsfeaImportedFactorsCb == null || CsfeaPhysicalNonlinearityCb == null || CsfeaStiffnessSecantRadio == null) return;
        bool linear = SelectedKind == FemCsfeaRunner.LinearAnalysisKind;
        // На жёсткостях секущего результата у ЖБ-КЭ жёсткость — из матрицы источника.
        CsfeaImportedFactorsCb.IsEnabled = linear ? CsfeaStiffnessSecantRadio.IsChecked != true
            : CsfeaPhysicalNonlinearityCb.IsChecked != true;
    }

    void UpdateCsfeaRecordingState()
    {
        if (CsfeaRecordStepsBox == null || CsfeaRecordStageEndsCb == null) return;
        bool selected = CsfeaRecordSelectedRadio.IsChecked == true;
        CsfeaRecordStepsBox.IsEnabled = selected;
        CsfeaRecordStageEndsCb.IsEnabled = selected;
    }

    static List<ComboOption> BuildMaterialSourceOptions() =>
    [
        new("Translated", Loc.S("FemMaterialSourceTranslated")),
        new("Native", Loc.S("FemMaterialSourceNative")),
    ];

    static List<ComboOption> BuildMainMaterialModelOptions() =>
    [
        new("Concrete0102", Loc.S("FemMainMaterialModelConcrete0102")),
        new("Concrete04", Loc.S("FemMainMaterialModelConcrete04")),
        new("Steel01", Loc.S("FemMainMaterialModelSteel01")),
        new("Steel02", Loc.S("FemMainMaterialModelSteel02")),
    ];

    static List<ComboOption> BuildSteelModelOptions() =>
    [
        new("Steel01", Loc.S("FemReinforcementModelSteel01")),
        new("Steel02", Loc.S("FemReinforcementModelSteel02")),
    ];

    static List<ComboOption> BuildElementFormulationOptions() =>
    [
        new("forceBeamColumn", Loc.S("FemElementFormulationForce")),
        new("dispBeamColumn", Loc.S("FemElementFormulationDisp")),
    ];

    static void ApplyPathControlDto(StageRow row, FemAnalysisPathControl? dto, bool isContinuation)
    {
        if (dto == null) return;
        var options = BuildPathControlModeOptions();
        var option = options.FirstOrDefault(o => o.Value == dto.Mode) ?? options[0];
        var dc = dto.ControlNodeId is { } nid && dto.ControlDof is { } cd && dto.InitialIncrement is { } ii &&
                 dto.MinIncrement is { } mi && dto.MaxIncrement is { } ma && dto.TargetDisplacement is { } td && dto.MaxSteps is { } ms
            ? new FemDisplacementControlInput(nid, cd, ii, mi, ma, td, ms) : null;
        var al = dto.ArcLengthS is { } s && dto.ArcLengthAlpha is { } alpha && dto.ArcLengthMinS is { } mins &&
                 dto.MaxSteps is { } ms2 && dto.MonitorNodeId is { } mnid && dto.MonitorDof is { } mdof
            ? new FemArcLengthInput(s, alpha, mins, ms2, mnid, mdof) : null;

        if (isContinuation)
        {
            row.ContinueWithMode = option;
            row.ContinueWithDisplacementControl = dc;
            row.ContinueWithArcLength = al;
        }
        else
        {
            row.PathControlMode = option;
            row.DisplacementControl = dc;
            row.ArcLength = al;
        }
    }

    static FemAnalysisPathControl BuildPathControlDto(ComboOption mode, FemDisplacementControlInput? dc, FemArcLengthInput? al) => new()
    {
        Mode = mode.Value,
        ControlNodeId = dc?.ControlNodeId, ControlDof = dc?.ControlDof,
        InitialIncrement = dc?.InitialIncrement, MinIncrement = dc?.MinIncrement, MaxIncrement = dc?.MaxIncrement,
        TargetDisplacement = dc?.TargetDisplacement, MaxSteps = dc?.MaxSteps ?? al?.MaxSteps,
        ArcLengthS = al?.S, ArcLengthAlpha = al?.Alpha, ArcLengthMinS = al?.MinS,
        MonitorNodeId = al?.MonitorNodeId, MonitorDof = al?.MonitorDof
    };

    void ConfigurePathControl_Click(object sender, RoutedEventArgs e)
    {
        // Клик по кнопке в той же строке не проходит через обычную навигацию между ячейками
        // DataGrid, поэтому только что выбранный в ComboBoxColumn режим может быть ещё не
        // протолкнут в StageRow.PathControlMode — принудительно завершаем редактирование ячейки.
        StagesGrid.CommitEdit(DataGridEditingUnit.Cell, true);
        StagesGrid.CommitEdit(DataGridEditingUnit.Row, true);

        if ((sender as FrameworkElement)?.Tag is not StageRow row) return;
        var dlg = new FemPathControlDialog(row, _nodes) { Owner = this };
        dlg.ShowDialog();
    }

    /// <summary>Источник сохранённой стадии: из схемы или, если его там нет, выражение как было (не подменяется).</summary>
    ViewModels.FemLoadSource ResolveSource(string? expressionJson) =>
        ViewModels.FemLoadSources.Resolve(_loadSources, expressionJson, _schema.LoadCases);

    /// <summary>Окно выбора нагрузки; null — отмена.</summary>
    ViewModels.FemLoadSource? PickSource(ViewModels.FemLoadSource? current, string? stageTag)
    {
        var dlg = new FemLoadSourcePickerDialog(_loadSources, current, stageTag) { Owner = this };
        return dlg.ShowDialog() == true ? dlg.Result : null;
    }

    /// <summary>Выбор нагрузки строки: стадии (с подстановкой имени) или одиночного расчёта.</summary>
    void ChooseSource(StageRow row)
    {
        bool isStage = !ReferenceEquals(row, _single);
        if (PickSource(row.Source, isStage ? row.Tag : null) is not { } picked) return;
        if (isStage && IsAutoStageTag(row)) row.Tag = picked.Tag;
        row.Source = picked;
    }

    /// <summary>
    /// Имя стадии не задано пользователем: пустое, «Стадия N» или совпадает с именем прежней нагрузки — тогда при
    /// выборе нагрузки имя стадии берётся из неё.
    /// </summary>
    static bool IsAutoStageTag(StageRow row)
    {
        string tag = row.Tag.Trim();
        if (tag.Length == 0 || tag == Loc.S("FemAnalysisStageDefaultTag")) return true;
        if (row.Source is { } old && tag == old.Tag) return true;
        string pattern = "^" + System.Text.RegularExpressions.Regex.Escape(Loc.S("FemAnalysisStageNumberedTag")).Replace(@"\{0}", @"\d+") + "$";
        return System.Text.RegularExpressions.Regex.IsMatch(tag, pattern);
    }

    void ChooseSource_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is StageRow row) ChooseSource(row);
    }

    void SourceCell_MouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (e.ClickCount != 2 || (sender as FrameworkElement)?.DataContext is not StageRow row) return;
        e.Handled = true;
        ChooseSource(row);
    }

    /// <summary>F2 или пробел в столбце нагрузки — окно выбора (ячейка только для чтения).</summary>
    void StagesGrid_PreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key is not (System.Windows.Input.Key.F2 or System.Windows.Input.Key.Space)) return;
        if (StagesGrid.CurrentColumn != StagesSourceColumn || StagesGrid.CurrentItem is not StageRow row) return;
        e.Handled = true;
        ChooseSource(row);
    }

    /// <summary>Виды постановки: свой решатель CSfea первым, OpenSees — запасной.</summary>
    readonly List<ComboOption> _kindOptions =
    [
        new(FemCsfeaRunner.AnalysisKind, Loc.S("FemAnalysisKindCsfea")),
        new(FemCsfeaRunner.LinearAnalysisKind, Loc.S("FemAnalysisKindCsfeaLinear")),
        new(FemBucklingRunner.AnalysisKind, Loc.S("FemAnalysisKindCsfeaBuckling")),
        new("linear", Loc.S("FemAnalysisKindLinearOpenSees")),
        new("nonlinear", Loc.S("FemAnalysisKindNonlinearOpenSees")),
    ];

    /// <summary>Выбранный вид постановки (<see cref="FemAnalysis.Kind"/>).</summary>
    string SelectedKind => (KindBox.SelectedItem as ComboOption)?.Value ?? FemCsfeaRunner.AnalysisKind;

    void SelectKind(string kind) => KindBox.SelectedItem = _kindOptions.FirstOrDefault(o => o.Value == kind) ?? _kindOptions[0];

    void KindBox_SelectionChanged(object sender, SelectionChangedEventArgs e) => UpdateNonlinearPanelVisibility();

    void UpdateNonlinearPanelVisibility()
    {
        // Checked радиокнопок приходит ещё из InitializeComponent — до создания панелей ниже по разметке.
        if (NonlinearPanel == null || CsfeaPanel == null || CsfeaCheckInputButton == null) return;
        bool nonlinear = SelectedKind == "nonlinear";
        bool csfea = FemCsfeaRunner.IsCsfeaKind(SelectedKind) && !IsSubmodelMode;
        // Линейный CSfea: вместо стадий — загружения галочками, из панели — только вид расчёта, источник армирования
        // пластин и контрольный узел.
        bool csfeaLinear = csfea && SelectedKind == FemCsfeaRunner.LinearAnalysisKind;
        // Расчётные длины: сочетания отметками в списке пакета (без шага, предела λ и хранения) и число форм.
        bool buckling = SelectedKind == FemBucklingRunner.AnalysisKind && !IsSubmodelMode;
        bool staged = nonlinear || csfea || buckling;
        bool batch = IsBatchMode;
        LoadingModePanel.Visibility = csfea && !csfeaLinear ? Visibility.Visible : Visibility.Collapsed;
        StagedPanel.Visibility = batch || buckling ? Visibility.Collapsed : Visibility.Visible;
        BatchPanel.Visibility = batch || buckling ? Visibility.Visible : Visibility.Collapsed;
        BatchOptionsPanel.Visibility = buckling ? Visibility.Collapsed : Visibility.Visible;
        BucklingModesRow.Visibility = buckling ? Visibility.Visible : Visibility.Collapsed;
        StagesGroup.SetResourceReference(HeaderedContentControl.HeaderProperty,
            buckling ? "FemBucklingCombinationsLabel" : "FemAnalysisStagesLabel");
        NonlinearPanel.Visibility = nonlinear ? Visibility.Visible : Visibility.Collapsed;
        CsfeaPanel.Visibility = csfea ? Visibility.Visible : Visibility.Collapsed;
        CsfeaCheckInputButton.Visibility = csfea && _csfeaContext != null ? Visibility.Visible : Visibility.Collapsed;
        LoadSourceRow.Visibility = staged || IsSubmodelMode ? Visibility.Collapsed : Visibility.Visible;
        StagesGroup.Visibility = staged && !csfeaLinear && !IsSubmodelMode ? Visibility.Visible : Visibility.Collapsed;
        LinearCasesGroup.Visibility = csfeaLinear ? Visibility.Visible : Visibility.Collapsed;
        // Секущий расчёт ведётся только по нагрузке: path control скрыт, а заданный в OpenSees-виде —
        // сбрасывается, чтобы не гасить колонки шага и предела λ.
        var pathControl = nonlinear ? Visibility.Visible : Visibility.Collapsed;
        StagesPathControlColumn.Visibility = pathControl;
        StagesPathControlButtonColumn.Visibility = pathControl;
        StagesHintText.SetResourceReference(TextBlock.TextProperty,
            buckling ? "FemBucklingCombinationsHint" : batch ? "FemCsfeaBatchHint" : csfea ? "FemCsfeaStagesHint" : "FemAnalysisStagesHint");
        var secantOnly = csfeaLinear ? Visibility.Collapsed : Visibility.Visible;
        var secantRow = new GridLength(csfeaLinear ? 0 : 28);
        CsfeaTensionRow.Height = secantRow;
        CsfeaCrackRuleRow.Height = secantRow;
        CsfeaPoissonRow.Height = secantRow;
        CsfeaFlagsPanel.Visibility = secantOnly;
        CsfeaStiffnessGroup.Visibility = csfeaLinear ? Visibility.Visible : Visibility.Collapsed;
        UpdateCsfeaImportedFactorsState();
        CsfeaIterationsGroup.Visibility = secantOnly;
        // Пакет пишет только конечное состояние каждого варианта — свой выбор хранения в панели пакета.
        CsfeaRecordingGroup.Visibility = batch ? Visibility.Collapsed : secantOnly;
        if (csfea)
        {
            var loadControl = BuildPathControlModeOptions()[0];
            foreach (var row in _stages.Where(r => r.PathControlMode.Value != loadControl.Value))
                row.PathControlMode = loadControl;
        }
        // Переход со стадийного вида на линейный OpenSees — нагрузка первой стадии.
        if (!staged && _single.Source == null) _single.Source = _stages.FirstOrDefault(s => s.Source != null)?.Source;
        // Первая стадия — без нагрузки (ячейка «Выберите нагрузку…»): из сотен комбинаций первая попавшаяся не годится.
        if (staged && _stages.Count == 0)
            _stages.Add(new StageRow
            {
                Tag = Loc.S("FemAnalysisStageDefaultTag"),
                Source = _single.Source,
                MaxLoadFactor = csfea ? 1.0 : 10.0
            });
    }

    /// <summary>Пакет вариантов: секущий CSfea (не линейный) и выбран режим «Пакет».</summary>
    bool IsBatchMode => BatchRadio?.IsChecked == true && SelectedKind == FemCsfeaRunner.AnalysisKind && !IsSubmodelMode;

    void LoadingMode_Changed(object sender, RoutedEventArgs e) => UpdateNonlinearPanelVisibility();

    void BatchRecording_Changed(object sender, RoutedEventArgs e) => UpdateBatchSummary();

    void ChooseBatch_Click(object sender, RoutedEventArgs e) => ChooseBatch();

    void BatchGrid_MouseDoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e) => ChooseBatch();

    /// <summary>Окно отметок: результат заменяет состав пакета (нагрузки, которых нет в схеме, выбраны заново).</summary>
    void ChooseBatch()
    {
        var dlg = FemLoadSourcePickerDialog.ForBatch(_loadSources, _batch.Where(s => !s.IsUnresolved));
        dlg.Owner = this;
        if (dlg.ShowDialog() != true) return;
        _batch.Clear();
        foreach (var s in dlg.Results) _batch.Add(s);
        UpdateBatchSummary();
    }

    /// <summary>«Отмечено 136: C 110 · CL 17 · ЗГ 8» и оценка числа расчётов и объёма полей.</summary>
    void UpdateBatchSummary()
    {
        if (BatchSummaryText == null || BatchEstimateText == null) return;
        if (_batch.Count == 0)
        {
            BatchSummaryText.Text = Loc.S("FemCsfeaBatchNone");
            BatchEstimateText.Text = "";
            return;
        }
        BatchSummaryText.Text = ViewModels.FemLoadSources.BatchSummary(_batch);
        var draft = BatchDraft();
        bool hasN = FemCsfeaBatchLongTerm.HasCombinations(draft.Items, FemCombinationTypes.N);
        BatchLongNCb.IsEnabled = hasN;
        BatchLongDeflectionCb.IsEnabled = hasN && BatchLongNCb.IsChecked == true;
        BatchLongCCb.IsEnabled = FemCsfeaBatchLongTerm.HasCombinations(draft.Items, FemCombinationTypes.C);
        var longTerm = FemCsfeaBatchLongTerm.Estimate(draft, _schema.LoadCases);
        bool anyLong = draft.LongTermPairs.Count > 0
            && longTerm is not { Pairs: 0, SameAsTotal: 0, Zero: 0, Invalid: 0 };

        string size = "";
        if (BatchRecordFieldsRadio.IsChecked == true && MeshCounts() is { } m)
            size = string.Format(Loc.S("FemCsfeaBatchEstimateFields"),
                FemCsfeaBatchEstimate.Format(FemCsfeaBatchEstimate.FieldBytes(m.Nodes, m.Shells, m.Beams,
                    strains: CsfeaRecordStrainsCb.IsChecked == true, matrices: true) * (_batch.Count + longTerm.Runs)));
        string text = string.Format(Loc.S("FemCsfeaBatchEstimate"), _batch.Count, size);
        if (anyLong)
            text += string.Format(Loc.S("FemCsfeaBatchEstimateLong"), longTerm.Pairs, longTerm.SameAsTotal, longTerm.Zero,
                longTerm.Invalid > 0 ? string.Format(Loc.S("FemCsfeaBatchEstimateLongInvalid"), longTerm.Invalid) : "",
                longTerm.Deflections > 0 ? string.Format(Loc.S("FemCsfeaBatchEstimateLongDeflection"), longTerm.Deflections) : "");
        var calc = CsfeaCalcTypeBox.SelectedItem as CalcType? ?? CalcType.N;
        foreach (var type in draft.LongTermPairs.Where(t => t != calc.ToString() && FemCsfeaBatchLongTerm.HasCombinations(draft.Items, t)))
            text += " " + string.Format(Loc.S("FemCsfeaBatchCalcTypeMismatch"), type, calc);
        BatchEstimateText.Text = text;
    }

    void BatchLongTerm_Changed(object sender, RoutedEventArgs e) => UpdateBatchSummary();

    /// <summary>Пакет из панели без проверки шага и предела λ (для оценки и записи).</summary>
    FemCsfeaBatch BatchDraft()
    {
        var pairs = new List<string>();
        if (BatchLongNCb.IsChecked == true) pairs.Add(FemCombinationTypes.N);
        if (BatchLongCCb.IsChecked == true) pairs.Add(FemCombinationTypes.C);
        return new FemCsfeaBatch
        {
            Items = _batch.Where(s => !s.IsUnresolved).Select(s => new FemCsfeaBatchItem
            {
                Tag = s.Tag, LoadExpressionJson = s.ExpressionJson, Type = s.TypeBadge,
                Kind = s.Kind == ViewModels.FemLoadSourceKind.LoadCase ? FemCsfeaBatchItemKind.LoadCase : FemCsfeaBatchItemKind.Combination,
            }).ToList(),
            StopAtLimit = BatchStopAtLimitCb.IsChecked == true,
            Recording = BatchRecordEnvelopesRadio.IsChecked == true ? FemCsfeaBatchRecording.Envelopes : FemCsfeaBatchRecording.Fields,
            LongTermPairs = pairs,
            LongTermDeflection = BatchLongDeflectionCb.IsChecked == true,
        };
    }

    /// <summary>Размер сетки схемы из БД (окружение «Проверить вход»); нет БД или сетки — null.</summary>
    (int Nodes, int Shells, int Beams)? MeshCounts()
    {
        if (_meshCounts == null && _csfeaContext?.Db is { } db)
        {
            var elements = db.GetFemMeshElements(_schema.Id);
            _meshCounts = (db.GetFemMeshNodes(_schema.Id).Count, elements.Count(e => e.ElemType == "shell"),
                elements.Count(e => e.ElemType == "beam"));
        }
        return _meshCounts is { Nodes: > 0 } ? _meshCounts : null;
    }

    /// <summary>Пакет из панели; null — значения неверны (сообщение показано с заголовком <paramref name="caption"/>).</summary>
    FemCsfeaBatch? ReadBatch(string caption)
    {
        string? error = null;
        if (_batch.Count == 0) error = Loc.S("FemCsfeaBatchEmpty");
        else if (_batch.Any(s => s.IsUnresolved)) error = Loc.S("FemCsfeaBatchUnresolved");
        else if (!Pars.ParseAny(BatchStepBox.Text, out var step) || !double.IsFinite(step) || step <= 0 || step > 1
            || !Pars.ParseAny(BatchMaxBox.Text, out var max) || !double.IsFinite(max) || max < step)
            error = Loc.S("FemCsfeaBatchFactorsInvalid");
        else
        {
            var batch = BatchDraft();
            batch.LoadFactorStep = step;
            batch.MaxLoadFactor = max;
            // Флажок длительных частей типа, которого нет в пакете, не сохраняется.
            batch.LongTermPairs.RemoveAll(t => !FemCsfeaBatchLongTerm.HasCombinations(batch.Items, t));
            return batch;
        }
        MessageBox.Show(error, caption, MessageBoxButton.OK, MessageBoxImage.Warning);
        return null;
    }

    void LinearCasesAll_Click(object sender, RoutedEventArgs e) => _linearCases.CheckAll();

    void LinearCasesNone_Click(object sender, RoutedEventArgs e) => _linearCases.CheckNone();

    /// <summary>«Добавить…»: сразу окно выбора нагрузки; новая стадия получает её имя, шаг и предел λ — от последней.</summary>
    void AddStage_Click(object sender, RoutedEventArgs e)
    {
        string tag = string.Format(Loc.S("FemAnalysisStageNumberedTag"), _stages.Count + 1);
        if (PickSource(null, tag) is not { } picked) return;
        var last = _stages.LastOrDefault();
        var row = new StageRow
        {
            Tag = picked.Tag,
            Source = picked,
            LoadFactorStep = last?.LoadFactorStep ?? 0.1,
            MaxLoadFactor = last?.MaxLoadFactor ?? (FemCsfeaRunner.IsCsfeaKind(SelectedKind) ? 1.0 : 10.0)
        };
        _stages.Add(row);
        StagesGrid.SelectedItem = row;
        StagesGrid.ScrollIntoView(row);
    }

    void RemoveStage_Click(object sender, RoutedEventArgs e)
    {
        if (StagesGrid.SelectedItem is StageRow row) _stages.Remove(row);
    }

    void MoveStageUp_Click(object sender, RoutedEventArgs e)
    {
        if (StagesGrid.SelectedItem is not StageRow row) return;
        int i = _stages.IndexOf(row);
        if (i > 0) _stages.Move(i, i - 1);
    }

    void MoveStageDown_Click(object sender, RoutedEventArgs e)
    {
        if (StagesGrid.SelectedItem is not StageRow row) return;
        int i = _stages.IndexOf(row);
        if (i >= 0 && i < _stages.Count - 1) _stages.Move(i, i + 1);
    }

    void UpdateNativeMaterialPanelVisibility()
    {
        if (NativeMaterialPanel == null) return;
        NativeMaterialPanel.Visibility =
            (MaterialSourceBox.SelectedItem as ComboOption)?.Value == "Native" ? Visibility.Visible : Visibility.Collapsed;
    }

    void UpdateMaterialNonlinearityPanelVisibility()
    {
        if (MaterialNonlinearityPanel == null) return;
        MaterialNonlinearityPanel.IsEnabled = ConsiderPhysicalNonlinearityCb.IsChecked == true;
    }

    void Ok_Click(object sender, RoutedEventArgs e)
    {
        StagesGrid.CommitEdit(DataGridEditingUnit.Cell, true);
        StagesGrid.CommitEdit(DataGridEditingUnit.Row, true);

        if (IsSubmodelMode)
        {
            if (!Pars.ParseAny(LoadFactorStepBox.Text, out var step) || !double.IsFinite(step) || step <= 0 || step > 1)
            {
                MessageBox.Show(Loc.S("SubmodelLoadFactorStepInvalid"), Title, MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            var submodelParams = ReadNonlinearParams();
            submodelParams.LoadFactorStep = step;
            ResultParams = submodelParams;
            DialogResult = true;
            return;
        }

        if (BuildAnalysis(Loc.S("FemAnalysisCreate")) is not { } analysis) return;
        Result = analysis;
        DialogResult = true;
    }

    /// <summary>
    /// Постановка «Расчётные длины — CSfea»: отмеченные сочетания — стадии (целиком, без шагов), число форм; null —
    /// значения неверны.
    /// </summary>
    static void SelectTag(ComboBox combo, string tag)
    {
        foreach (var item in combo.Items.OfType<ComboBoxItem>())
            if ((string)item.Tag == tag) { combo.SelectedItem = item; return; }
    }

    static string SelectedTag(ComboBox combo) => (combo.SelectedItem as ComboBoxItem)?.Tag as string ?? "mode";

    void BucklingMethod_Changed(object sender, SelectionChangedEventArgs e) => UpdateBucklingMethod();

    // Номограммные параметры видны только у номограммных методов; порог — у смещения по жёсткости, оси — у ручного.
    void UpdateBucklingMethod()
    {
        if (!IsInitialized || BucklingMethodCombo == null || BucklingNomogramPanel == null || BucklingSwayCombo == null
            || BucklingSwayStiffnessRow == null || BucklingSwayManualRow == null) return;
        bool nomogram = SelectedTag(BucklingMethodCombo) != "mode";
        string sway = SelectedTag(BucklingSwayCombo);
        BucklingNomogramPanel.Visibility = nomogram ? Visibility.Visible : Visibility.Collapsed;
        BucklingSwayStiffnessRow.Visibility = sway == "stiffness" ? Visibility.Visible : Visibility.Collapsed;
        BucklingSwayManualRow.Visibility = sway == "manual" ? Visibility.Visible : Visibility.Collapsed;
    }

    FemAnalysis? BuildBucklingAnalysis(string caption)
    {
        string? error = null;
        static bool Num(TextBox box, out double v) =>
            double.TryParse(box.Text.Trim(), NumberStyles.Float, CultureInfo.CurrentCulture, out v) && double.IsFinite(v);
        if (_batch.Count == 0) error = Loc.S("FemBucklingCombinationsEmpty");
        else if (_batch.Any(s => s.IsUnresolved)) error = Loc.S("FemCsfeaBatchUnresolved");
        else if (!int.TryParse(BucklingModesBox.Text.Trim(), NumberStyles.Integer, CultureInfo.CurrentCulture, out int modes)
            || modes is < 1 or > 200)
            error = Loc.S("FemBucklingModesInvalid");
        else if (!Num(BucklingGFixedBox, out double gFixed) || gFixed < 0 || !Num(BucklingGPinnedBox, out double gPinned)
                 || gPinned <= 0 || !Num(BucklingSwayStiffnessBox, out double swayStiffness) || swayStiffness <= 0)
            error = Loc.S("FemBucklingNomogramInvalid");
        else
        {
            var pars = new FemAnalysisParams
            {
                Buckling = new FemBucklingParams
                {
                    Modes = modes, Method = SelectedTag(BucklingMethodCombo), Sway = SelectedTag(BucklingSwayCombo),
                    Forces = (BucklingForcesCombo.SelectedItem as ComboBoxItem)?.Tag as string ?? FemBucklingParams.ForcesEnvelope,
                    SwayX = BucklingSwayXCb.IsChecked == true, SwayY = BucklingSwayYCb.IsChecked == true,
                    SwayStiffness = swayStiffness, GFixed = gFixed, GPinned = gPinned,
                },
                Stages = _batch.Select(s => new FemAnalysisStage
                {
                    Tag = s.Tag, LoadExpressionJson = s.ExpressionJson, LoadFactorStep = 1.0, MaxLoadFactor = 1.0,
                }).ToList(),
            };
            string tag = TagBox.Text.Trim();
            if (string.IsNullOrWhiteSpace(tag)) tag = Loc.S("FemAnalysisKindCsfeaBuckling");
            return new FemAnalysis
            {
                SchemaId = _schema.Id, Tag = tag, Kind = FemBucklingRunner.AnalysisKind,
                LoadExpressionJson = pars.Stages[0].LoadExpressionJson, ParamsJson = pars.ToJson(),
            };
        }
        MessageBox.Show(error, caption, MessageBoxButton.OK, MessageBoxImage.Warning);
        return null;
    }

    /// <summary>
    /// Постановка по состоянию диалога (вне режима субмодели); null — значения неверны (сообщение уже показано с
    /// заголовком <paramref name="caption"/>).
    /// </summary>
    FemAnalysis? BuildAnalysis(string caption)
    {
        bool isNonlinear = SelectedKind == "nonlinear";
        bool isCsfea = FemCsfeaRunner.IsCsfeaKind(SelectedKind);
        bool isCsfeaLinear = SelectedKind == FemCsfeaRunner.LinearAnalysisKind;
        bool staged = isNonlinear || isCsfea;
        bool isBatch = IsBatchMode;

        if (SelectedKind == FemBucklingRunner.AnalysisKind) return BuildBucklingAnalysis(caption);
        if (isCsfeaLinear && _linearCases.CheckedCount == 0)
        {
            MessageBox.Show(Loc.S("FemCsfeaLinearCasesEmpty"), caption, MessageBoxButton.OK, MessageBoxImage.Warning);
            return null;
        }
        if (staged && !isCsfeaLinear && !isBatch && _stages.Count == 0)
        {
            MessageBox.Show(Loc.S("FemAnalysisStagesEmpty"), caption, MessageBoxButton.OK, MessageBoxImage.Warning);
            return null;
        }
        if (staged && !isCsfeaLinear && !isBatch && _stages.FirstOrDefault(s => s.Source == null) is { } missing)
        {
            StagesGrid.SelectedItem = missing;
            StagesGrid.ScrollIntoView(missing);
            MessageBox.Show(Loc.S("FemAnalysisStageMissingSource"), caption, MessageBoxButton.OK, MessageBoxImage.Warning);
            return null;
        }
        if (!staged && _single.Source == null)
        {
            MessageBox.Show(Loc.S("FemAnalysisLoadMissing"), caption, MessageBoxButton.OK, MessageBoxImage.Warning);
            return null;
        }

        var pars = new FemAnalysisParams();
        string tag = TagBox.Text.Trim();
        string loadExpressionJson;
        if (staged)
        {
            if (isCsfea)
            {
                if (ReadCsfeaParams(out var error) is not { } csfea)
                {
                    MessageBox.Show(error, caption, MessageBoxButton.OK, MessageBoxImage.Warning);
                    return null;
                }
                pars.CalcType = CsfeaCalcTypeBox.SelectedItem as CalcType? ?? CalcType.N;
                if (isCsfeaLinear)
                {
                    csfea.StiffnessSource = ReadStiffnessSource(out var stiffnessError);
                    if (stiffnessError != null)
                    {
                        MessageBox.Show(stiffnessError, caption, MessageBoxButton.OK, MessageBoxImage.Warning);
                        return null;
                    }
                }
                pars.Csfea = csfea;
            }
            else
                pars = ReadNonlinearParams();
            if (isBatch)
            {
                if (ReadBatch(caption) is not { } batch) return null;
                pars.Batch = batch;
                if (string.IsNullOrWhiteSpace(tag)) tag = string.Format(Loc.S("FemCsfeaBatchDefaultTag"), batch.Items.Count);
                return new FemAnalysis
                {
                    SchemaId = _schema.Id, Tag = tag, Kind = SelectedKind,
                    LoadExpressionJson = batch.Items[0].LoadExpressionJson, ParamsJson = pars.ToJson()
                };
            }
            // Линейный CSfea: отмеченные загружения целиком, без шагов и множителя.
            pars.Stages = isCsfeaLinear ? _linearCases.BuildStages() : _stages.Select(r =>
            {
                double step = r.LoadFactorStep > 0 ? r.LoadFactorStep : 0.1;
                double max = r.MaxLoadFactor >= step ? r.MaxLoadFactor : isCsfea ? step : Math.Max(10.0, step);
                return new FemAnalysisStage
                {
                    Tag = r.Tag, LoadExpressionJson = r.Source!.ExpressionJson,
                    LoadFactorStep = step, MaxLoadFactor = max,
                    PathControl = isCsfea ? null : BuildPathControlDto(r.PathControlMode, r.DisplacementControl, r.ArcLength),
                    ContinueWith = isCsfea || r.ContinueWithMode == null ? null
                        : BuildPathControlDto(r.ContinueWithMode, r.ContinueWithDisplacementControl, r.ContinueWithArcLength)
                };
            }).ToList();
            loadExpressionJson = pars.Stages[0].LoadExpressionJson;
            if (string.IsNullOrWhiteSpace(tag)) tag = pars.Stages[0].Tag;
        }
        else
        {
            var src = _single.Source!;
            loadExpressionJson = src.ExpressionJson;
            if (string.IsNullOrWhiteSpace(tag)) tag = src.Tag;
        }

        return new FemAnalysis
        {
            SchemaId = _schema.Id,
            Tag = tag,
            Kind = isCsfea ? SelectedKind : isNonlinear ? "nonlinear" : "linear",
            LoadExpressionJson = loadExpressionJson,
            ParamsJson = pars.ToJson()
        };
    }

    /// <summary>
    /// «Проверить вход»: подготовка секущего расчёта без построения сетки и без расчёта (сетка, перенос SCAD, вход,
    /// адаптер) — отчёт в окне с копированием.
    /// </summary>
    async void CsfeaCheckInput_Click(object sender, RoutedEventArgs e)
    {
        if (_csfeaContext is not { } ctx) return;
        StagesGrid.CommitEdit(DataGridEditingUnit.Cell, true);
        StagesGrid.CommitEdit(DataGridEditingUnit.Row, true);
        string caption = Loc.S("FemCsfeaCheckInputTitle");
        if (BuildAnalysis(caption) is not { } analysis) return;

        var lines = new List<string>();
        CsfeaCheckInputButton.IsEnabled = false;
        Cursor = System.Windows.Input.Cursors.Wait;
        try
        {
            var prepared = await FemCsfeaRunner.PrepareAsync(ctx, _schema, analysis, buildMesh: false, CancellationToken.None);
            lines.Add(Loc.S(prepared.HasErrors ? "FemCsfeaCheckInputFailed" : "FemCsfeaCheckInputOk"));
            lines.AddRange(prepared.Describe());
            lines.AddRange(prepared.Report);
        }
        catch (Exception ex)
        {
            lines.Add(Loc.S("FemCsfeaCheckInputFailed"));
            lines.Add(ex.Message);
        }
        finally
        {
            Cursor = null;
            CsfeaCheckInputButton.IsEnabled = true;
        }
        new Dialogs.TextReportWindow($"{caption} — «{analysis.Tag}»", string.Join(Environment.NewLine, lines)) { Owner = this }
            .ShowDialog();
    }

    /// <summary>Параметры нелинейной панели (тип расчёта, формулировка элемента, материалы) без стадий.</summary>
    FemAnalysisParams ReadNonlinearParams() => new()
    {
        CalcType = CalcTypeBox.SelectedItem as CalcType? ?? CalcType.C,
        ConsiderPhysicalNonlinearity = ConsiderPhysicalNonlinearityCb.IsChecked == true,
        ConsiderConcreteTension = ConsiderConcreteTensionCb.IsChecked == true,
        MaterialSource = (MaterialSourceBox.SelectedItem as ComboOption)?.Value ?? "Translated",
        MainMaterialModel = (MainMaterialModelBox.SelectedItem as ComboOption)?.Value ?? "Concrete04",
        SteelModel = (SteelModelBox.SelectedItem as ComboOption)?.Value ?? "Steel02",
        SteelHardeningModulusMpa =
            Pars.ParseAny(SteelHardeningModulusBox.Text, out var hardening) &&
            double.IsFinite(hardening) && hardening >= 0
                ? hardening
                : 0,
        ElementFormulation = (ElementFormulationBox.SelectedItem as ComboOption)?.Value ?? "forceBeamColumn"
    };

    void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;
}
