using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using CScore;
using CScore.Fem;
using OpenCS.OpenSees.CScore;
using OpenCS.Tasks;
using OpenCS.Utilites;

namespace OpenCS.Views;

/// <summary>Диалог создания постановки OpenSees-расчёта схемы (линейный/нелинейный). Solver-
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

    public FemAnalysisDialog(FemSchema schema, IReadOnlyList<FemNode> nodes, FemAnalysis? existing = null)
    {
        _schema = schema;
        _nodes = nodes;
        InitializeComponent();
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

        if (existing != null)
        {
            Title = Loc.S("FemAnalysisEdit");
            TagBox.Text = existing.Tag;
            var pars = FemAnalysisParams.Parse(existing.ParamsJson);
            CalcTypeBox.SelectedItem = pars.CalcType ?? CalcType.C;
            ConsiderPhysicalNonlinearityCb.IsChecked = pars.ConsiderPhysicalNonlinearity;
            ConsiderConcreteTensionCb.IsChecked = pars.ConsiderConcreteTension;
            MaterialSourceBox.SelectedItem = materialSourceOptions.FirstOrDefault(o => o.Value == pars.MaterialSource) ?? materialSourceOptions[0];
            MainMaterialModelBox.SelectedItem = mainMaterialModelOptions.FirstOrDefault(o => o.Value == pars.MainMaterialModel) ?? mainMaterialModelOptions[1];
            SteelModelBox.SelectedItem = steelModelOptions.FirstOrDefault(o => o.Value == pars.SteelModel) ?? steelModelOptions[1];
            SteelHardeningRatioBox.Text = pars.SteelHardeningRatioOverride?.ToString(CultureInfo.InvariantCulture) ?? "";
            ElementFormulationBox.SelectedItem = elementFormulationOptions.FirstOrDefault(o => o.Value == pars.ElementFormulation) ?? elementFormulationOptions[0];

            bool isNonlinearExisting = existing.Kind == "nonlinear";
            if (isNonlinearExisting)
            {
                foreach (var stage in pars.ResolveStages(existing))
                {
                    var match = sources.FirstOrDefault(s => s.Expr.ToJson() == stage.LoadExpressionJson);
                    var row = new StageRow
                    {
                        Tag = stage.Tag, Source = match ?? sources.FirstOrDefault(),
                        LoadFactorStep = stage.LoadFactorStep ?? 0.1,
                        MaxLoadFactor = stage.MaxLoadFactor ?? 10.0
                    };
                    ApplyPathControlDto(row, stage.PathControl, isContinuation: false);
                    ApplyPathControlDto(row, stage.ContinueWith, isContinuation: true);
                    _stages.Add(row);
                }
            }
            // Устанавливается ПОСЛЕ заполнения _stages: RadioButton.IsChecked=true синхронно
            // поднимает Checked → UpdateNonlinearPanelVisibility, которая добавляет служебную
            // стадию-заглушку, если _stages ещё пуст — иначе к уже смигрированным стадиям
            // добавлялась лишняя дублирующая (баг: расчёт с одной стадией сохранялся с двумя).
            KindNonlinearRadio.IsChecked = isNonlinearExisting;

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

    void KindRadio_Changed(object sender, RoutedEventArgs e) => UpdateNonlinearPanelVisibility();

    void UpdateNonlinearPanelVisibility()
    {
        if (NonlinearPanel == null) return;
        bool nonlinear = KindNonlinearRadio.IsChecked == true;
        NonlinearPanel.Visibility = nonlinear ? Visibility.Visible : Visibility.Collapsed;
        LoadSourceRow.Visibility = nonlinear ? Visibility.Collapsed : Visibility.Visible;
        StagesGroup.Visibility = nonlinear ? Visibility.Visible : Visibility.Collapsed;
        if (nonlinear && _stages.Count == 0)
            _stages.Add(new StageRow { Tag = Loc.S("FemAnalysisStageDefaultTag"), Source = LoadSourceBox.SelectedItem as LoadSource ?? _loadSources.FirstOrDefault() });
    }

    void AddStage_Click(object sender, RoutedEventArgs e)
    {
        var last = _stages.LastOrDefault();
        _stages.Add(new StageRow
        {
            Tag = string.Format(Loc.S("FemAnalysisStageNumberedTag"), _stages.Count + 1),
            Source = _loadSources.FirstOrDefault(),
            LoadFactorStep = last?.LoadFactorStep ?? 0.1,
            MaxLoadFactor = last?.MaxLoadFactor ?? 10.0
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

        bool isNonlinear = KindNonlinearRadio.IsChecked == true;

        if (isNonlinear && _stages.Count == 0)
        {
            MessageBox.Show(Loc.S("FemAnalysisStagesEmpty"), Loc.S("FemAnalysisCreate"),
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        if (isNonlinear && _stages.Any(s => s.Source == null))
        {
            MessageBox.Show(Loc.S("FemAnalysisStageMissingSource"), Loc.S("FemAnalysisCreate"),
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        if (!isNonlinear && LoadSourceBox.SelectedItem is not LoadSource) { DialogResult = false; return; }

        var pars = new FemAnalysisParams();
        string tag = TagBox.Text.Trim();
        string loadExpressionJson;
        if (isNonlinear)
        {
            pars.CalcType = CalcTypeBox.SelectedItem as CalcType? ?? CalcType.C;
            pars.ConsiderPhysicalNonlinearity = ConsiderPhysicalNonlinearityCb.IsChecked == true;
            pars.ConsiderConcreteTension = ConsiderConcreteTensionCb.IsChecked == true;
            pars.MaterialSource = (MaterialSourceBox.SelectedItem as ComboOption)?.Value ?? "Translated";
            pars.MainMaterialModel = (MainMaterialModelBox.SelectedItem as ComboOption)?.Value ?? "Concrete04";
            pars.SteelModel = (SteelModelBox.SelectedItem as ComboOption)?.Value ?? "Steel02";
            pars.SteelHardeningRatioOverride =
                Pars.ParseAny(SteelHardeningRatioBox.Text, out var hardening) ? hardening : null;
            pars.ElementFormulation = (ElementFormulationBox.SelectedItem as ComboOption)?.Value ?? "forceBeamColumn";
            pars.Stages = _stages.Select(r =>
            {
                double step = r.LoadFactorStep > 0 ? r.LoadFactorStep : 0.1;
                double max = r.MaxLoadFactor >= step ? r.MaxLoadFactor : Math.Max(10.0, step);
                return new FemAnalysisStage
                {
                    Tag = r.Tag, LoadExpressionJson = r.Source!.Expr.ToJson(),
                    LoadFactorStep = step, MaxLoadFactor = max,
                    PathControl = BuildPathControlDto(r.PathControlMode, r.DisplacementControl, r.ArcLength),
                    ContinueWith = r.ContinueWithMode == null ? null
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

        Result = new FemAnalysis
        {
            Tag = tag,
            Kind = isNonlinear ? "nonlinear" : "linear",
            LoadExpressionJson = loadExpressionJson,
            ParamsJson = pars.ToJson()
        };
        DialogResult = true;
    }

    void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;
}
