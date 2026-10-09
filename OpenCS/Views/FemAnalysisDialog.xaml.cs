using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using CScore;
using CScore.Fem;
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
    List<LoadSource> _loadSources = [];

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
        set { _csfeaContext = value; UpdateNonlinearPanelVisibility(); }
    }

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

    public FemAnalysisDialog(FemSchema schema, IReadOnlyList<FemNode> nodes, FemAnalysis? existing = null)
    {
        _schema = schema;
        _nodes = nodes;
        InitializeComponent();
        KindBox.ItemsSource = _kindOptions;
        var sources = BuildLoadSources();
        _loadSources = sources;
        LoadSourceBox.ItemsSource = sources;
        StagesGrid.ItemsSource = _stages;
        StagesSourceColumn.ItemsSource = sources;
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
        CsfeaPlateRebarSourceBox.ItemsSource = CsfeaRebarSourceOptions;
        CsfeaTensionConcreteBox.ItemsSource = CsfeaTensionOptions;
        CsfeaPlateCrackRuleBox.ItemsSource = CsfeaCrackRuleOptions;
        CsfeaControlDofBox.ItemsSource = CsfeaDofLabels;

        bool isCsfeaExisting = existing?.Kind == FemCsfeaRunner.AnalysisKind;
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
                    var match = sources.FirstOrDefault(s => s.Expr.ToJson() == stage.LoadExpressionJson);
                    var row = new StageRow
                    {
                        Tag = stage.Tag, Source = match ?? sources.FirstOrDefault(),
                        LoadFactorStep = stage.LoadFactorStep ?? 0.1,
                        MaxLoadFactor = stage.MaxLoadFactor ?? (isCsfeaExisting ? 1.0 : 10.0)
                    };
                    ApplyPathControlDto(row, stage.PathControl, isContinuation: false);
                    ApplyPathControlDto(row, stage.ContinueWith, isContinuation: true);
                    _stages.Add(row);
                }
            }
            // Устанавливается ПОСЛЕ заполнения _stages: выбор вида синхронно
            // поднимает Checked → UpdateNonlinearPanelVisibility, которая добавляет служебную
            // стадию-заглушку, если _stages ещё пуст — иначе к уже смигрированным стадиям
            // добавлялась лишняя дублирующая (баг: расчёт с одной стадией сохранялся с двумя).
            SelectKind(existing.Kind);

            var sel = sources.FirstOrDefault(s => s.Expr.ToJson() == existing.LoadExpressionJson);
            if (sel != null) LoadSourceBox.SelectedItem = sel;
            else if (sources.Count > 0) LoadSourceBox.SelectedIndex = 0;
        }
        else
        {
            CalcTypeBox.SelectedItem = CalcType.C;
            MaterialSourceBox.SelectedItem = materialSourceOptions[0];
            MainMaterialModelBox.SelectedItem = mainMaterialModelOptions[1];
            SteelModelBox.SelectedItem = steelModelOptions[1];
            ElementFormulationBox.SelectedItem = elementFormulationOptions[0];
            if (LoadSourceBox.Items.Count > 0) LoadSourceBox.SelectedIndex = 0;
            SelectKind(_kindOptions[0].Value);   // по умолчанию — свой решатель
        }
        UpdateNonlinearPanelVisibility();
        UpdateNativeMaterialPanelVisibility();
        UpdateMaterialNonlinearityPanelVisibility();
    }

    internal sealed record LoadSource(string Label, FemLoadExpression Expr);

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
    /// управления траекторией. PathControlMode — единственное свойство с уведомлением
    /// (INotifyPropertyChanged) — на него реагирует CellStyle-триггер, затемняющий колонки
    /// «Шаг λ»/«Предел λ» для не-LoadControl режимов; остальные свойства читает только код
    /// сборки при Ok_Click, реактивность им не нужна.</summary>
    internal sealed class StageRow : System.ComponentModel.INotifyPropertyChanged
    {
        public string Tag { get; set; } = "";
        public LoadSource? Source { get; set; }
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
        CsfeaPsiCb.IsChecked = c.Psi;
        CsfeaBeamShearCb.IsChecked = c.BeamShear;
        CsfeaGeomNonlinearCb.IsChecked = c.GeomNonlinear;
        CsfeaMaxIterationsBox.Text = c.MaxIterations.ToString(culture);
        CsfeaMaxBisectionsBox.Text = c.MaxBisections.ToString(culture);
        CsfeaTolDisplacementBox.Text = c.TolDisplacement.ToString(culture);
        CsfeaTolStiffnessBox.Text = c.TolStiffness.ToString(culture);
        CsfeaOmega0Box.Text = c.Omega0.ToString(culture);
        CsfeaRecordStepsBox.Text = c.RecordSteps;
        CsfeaRecordStageEndsCb.IsChecked = c.RecordStageEnds;
        (c.ResultRecording switch
        {
            FemCsfeaRecording.Selected => CsfeaRecordSelectedRadio,
            FemCsfeaRecording.All => CsfeaRecordAllRadio,
            _ => CsfeaRecordFinalRadio,
        }).IsChecked = true;
        CsfeaControlNodeBox.Text = c.ControlNodeTag;
        CsfeaControlDofBox.SelectedIndex = c.ControlDof is >= 0 and <= 5 ? c.ControlDof : 2;
        UpdateCsfeaRecordingState();
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
            PoissonUncracked = poisson,
            GeomNonlinear = CsfeaGeomNonlinearCb.IsChecked == true,
            MaxIterations = maxIter, TolDisplacement = tolU, TolStiffness = tolK, MaxBisections = bisections, Omega0 = omega,
            ResultRecording = recording,
            RecordSteps = CsfeaRecordStepsBox.Text.Trim(),
            RecordStageEnds = stageEnds,
            ControlNodeTag = CsfeaControlNodeBox.Text.Trim(),
            ControlDof = Math.Max(0, CsfeaControlDofBox.SelectedIndex),
        };
    }

    void CsfeaRecording_Changed(object sender, RoutedEventArgs e) => UpdateCsfeaRecordingState();

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

    List<LoadSource> BuildLoadSources()
    {
        var list = new List<LoadSource>();
        foreach (var d in _schema.LoadDefinitions)
            list.Add(new(d.Tag, d.GetExpression()));
        foreach (var c in _schema.LoadCases)
            list.Add(new(c.Tag, new FemLoadExpression
            {
                Mode = FemLoadExpressionMode.Single,
                LoadCaseIds = [c.Id]
            }));
        return list;
    }

    /// <summary>Виды постановки: свой решатель CSfea первым, OpenSees — запасной.</summary>
    readonly List<ComboOption> _kindOptions =
    [
        new(FemCsfeaRunner.AnalysisKind, Loc.S("FemAnalysisKindCsfea")),
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
        bool csfea = SelectedKind == FemCsfeaRunner.AnalysisKind && !IsSubmodelMode;
        bool staged = nonlinear || csfea;
        NonlinearPanel.Visibility = nonlinear ? Visibility.Visible : Visibility.Collapsed;
        CsfeaPanel.Visibility = csfea ? Visibility.Visible : Visibility.Collapsed;
        CsfeaCheckInputButton.Visibility = csfea && _csfeaContext != null ? Visibility.Visible : Visibility.Collapsed;
        LoadSourceRow.Visibility = staged || IsSubmodelMode ? Visibility.Collapsed : Visibility.Visible;
        StagesGroup.Visibility = staged && !IsSubmodelMode ? Visibility.Visible : Visibility.Collapsed;
        // Секущий расчёт ведётся только по нагрузке: path control скрыт, а заданный в OpenSees-виде —
        // сбрасывается, чтобы не гасить колонки шага и предела λ.
        var pathControl = nonlinear ? Visibility.Visible : Visibility.Collapsed;
        StagesPathControlColumn.Visibility = pathControl;
        StagesPathControlButtonColumn.Visibility = pathControl;
        StagesHintText.SetResourceReference(TextBlock.TextProperty, csfea ? "FemCsfeaStagesHint" : "FemAnalysisStagesHint");
        if (csfea)
        {
            var loadControl = BuildPathControlModeOptions()[0];
            foreach (var row in _stages.Where(r => r.PathControlMode.Value != loadControl.Value))
                row.PathControlMode = loadControl;
        }
        if (staged && _stages.Count == 0)
            _stages.Add(new StageRow
            {
                Tag = Loc.S("FemAnalysisStageDefaultTag"),
                Source = LoadSourceBox.SelectedItem as LoadSource ?? _loadSources.FirstOrDefault(),
                MaxLoadFactor = csfea ? 1.0 : 10.0
            });
    }

    void AddStage_Click(object sender, RoutedEventArgs e)
    {
        var last = _stages.LastOrDefault();
        _stages.Add(new StageRow
        {
            Tag = string.Format(Loc.S("FemAnalysisStageNumberedTag"), _stages.Count + 1),
            Source = _loadSources.FirstOrDefault(),
            LoadFactorStep = last?.LoadFactorStep ?? 0.1,
            MaxLoadFactor = last?.MaxLoadFactor ?? (SelectedKind == FemCsfeaRunner.AnalysisKind ? 1.0 : 10.0)
        });
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

        if (SelectedKind == "linear" && LoadSourceBox.SelectedItem is not LoadSource) { DialogResult = false; return; }
        if (BuildAnalysis(Loc.S("FemAnalysisCreate")) is not { } analysis) return;
        Result = analysis;
        DialogResult = true;
    }

    /// <summary>
    /// Постановка по состоянию диалога (вне режима субмодели); null — значения неверны (сообщение уже показано с
    /// заголовком <paramref name="caption"/>).
    /// </summary>
    FemAnalysis? BuildAnalysis(string caption)
    {
        bool isNonlinear = SelectedKind == "nonlinear";
        bool isCsfea = SelectedKind == FemCsfeaRunner.AnalysisKind;
        bool staged = isNonlinear || isCsfea;

        if (staged && _stages.Count == 0)
        {
            MessageBox.Show(Loc.S("FemAnalysisStagesEmpty"), caption, MessageBoxButton.OK, MessageBoxImage.Warning);
            return null;
        }
        if (staged && _stages.Any(s => s.Source == null))
        {
            MessageBox.Show(Loc.S("FemAnalysisStageMissingSource"), caption, MessageBoxButton.OK, MessageBoxImage.Warning);
            return null;
        }
        if (!staged && LoadSourceBox.SelectedItem is not LoadSource) return null;

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
                pars.Csfea = csfea;
            }
            else
                pars = ReadNonlinearParams();
            pars.Stages = _stages.Select(r =>
            {
                double step = r.LoadFactorStep > 0 ? r.LoadFactorStep : 0.1;
                double max = r.MaxLoadFactor >= step ? r.MaxLoadFactor : isCsfea ? step : Math.Max(10.0, step);
                return new FemAnalysisStage
                {
                    Tag = r.Tag, LoadExpressionJson = r.Source!.Expr.ToJson(),
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
            var src = (LoadSource)LoadSourceBox.SelectedItem!;
            loadExpressionJson = src.Expr.ToJson();
            if (string.IsNullOrWhiteSpace(tag)) tag = src.Label;
        }

        return new FemAnalysis
        {
            SchemaId = _schema.Id,
            Tag = tag,
            Kind = isCsfea ? FemCsfeaRunner.AnalysisKind : isNonlinear ? "nonlinear" : "linear",
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
