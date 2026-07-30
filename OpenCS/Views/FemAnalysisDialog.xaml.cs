using System.Globalization;
using System.Windows;
using CScore;
using CScore.Fem;
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
    readonly System.Collections.ObjectModel.ObservableCollection<StageRow> _stages = [];
    List<LoadSource> _loadSources = [];

    /// <summary>Сформированная постановка (валидна после DialogResult == true).</summary>
    public FemAnalysis Result { get; private set; } = new();

    public FemAnalysisDialog(FemSchema schema, FemAnalysis? existing = null)
    {
        _schema = schema;
        InitializeComponent();
        var sources = BuildLoadSources();
        _loadSources = sources;
        LoadSourceBox.ItemsSource = sources;
        StagesGrid.ItemsSource = _stages;
        StagesSourceColumn.ItemsSource = sources;
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
                    _stages.Add(new StageRow
                    {
                        Tag = stage.Tag, Source = match ?? sources.FirstOrDefault(),
                        LoadFactorStep = stage.LoadFactorStep ?? 0.1,
                        MaxLoadFactor = stage.MaxLoadFactor ?? 10.0
                    });
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

    sealed record LoadSource(string Label, FemLoadExpression Expr);

    /// <summary>Строка редактора стадий: имя + выбранный источник нагрузки (переиспользует
    /// LoadSource — тот же список, что и для линейного расчёта).</summary>
    sealed class StageRow
    {
        public string Tag { get; set; } = "";
        public LoadSource? Source { get; set; }
        public double LoadFactorStep { get; set; } = 0.1;
        public double MaxLoadFactor { get; set; } = 10.0;
    }

    /// <summary>Пара «значение для Tcl/хранения» + «локализованная подпись для UI».</summary>
    sealed record ComboOption(string Value, string Label);

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
                    LoadFactorStep = step, MaxLoadFactor = max
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
