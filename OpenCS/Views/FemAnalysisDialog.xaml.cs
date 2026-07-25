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

    /// <summary>Сформированная постановка (валидна после DialogResult == true).</summary>
    public FemAnalysis Result { get; private set; } = new();

    public FemAnalysisDialog(FemSchema schema, FemAnalysis? existing = null)
    {
        _schema = schema;
        InitializeComponent();
        var sources = BuildLoadSources();
        LoadSourceBox.ItemsSource = sources;
        CalcTypeBox.ItemsSource = Enum.GetValues<CalcType>();
        var materialSourceOptions = BuildMaterialSourceOptions();
        var concreteModelOptions = BuildConcreteModelOptions();
        var steelModelOptions = BuildSteelModelOptions();
        var elementFormulationOptions = BuildElementFormulationOptions();
        MaterialSourceBox.ItemsSource = materialSourceOptions;
        ConcreteModelBox.ItemsSource = concreteModelOptions;
        SteelModelBox.ItemsSource = steelModelOptions;
        ElementFormulationBox.ItemsSource = elementFormulationOptions;
        MaterialSourceBox.SelectionChanged += (_, _) => UpdateNativeMaterialPanelVisibility();

        if (existing != null)
        {
            Title = Loc.S("FemAnalysisEdit");
            TagBox.Text = existing.Tag;
            KindNonlinearRadio.IsChecked = existing.Kind == "nonlinear";
            var pars = FemAnalysisParams.Parse(existing.ParamsJson);
            CalcTypeBox.SelectedItem = pars.CalcType ?? CalcType.C;
            LoadFactorStepBox.Text = pars.LoadFactorStep.ToString(CultureInfo.InvariantCulture);
            MaxLoadFactorBox.Text = pars.MaxLoadFactor.ToString(CultureInfo.InvariantCulture);
            ConsiderConcreteTensionCb.IsChecked = pars.ConsiderConcreteTension;
            MaterialSourceBox.SelectedItem = materialSourceOptions.FirstOrDefault(o => o.Value == pars.MaterialSource) ?? materialSourceOptions[0];
            ConcreteModelBox.SelectedItem = concreteModelOptions.FirstOrDefault(o => o.Value == pars.ConcreteModel) ?? concreteModelOptions[1];
            SteelModelBox.SelectedItem = steelModelOptions.FirstOrDefault(o => o.Value == pars.SteelModel) ?? steelModelOptions[1];
            SteelHardeningRatioBox.Text = pars.SteelHardeningRatioOverride?.ToString(CultureInfo.InvariantCulture) ?? "";
            ElementFormulationBox.SelectedItem = elementFormulationOptions.FirstOrDefault(o => o.Value == pars.ElementFormulation) ?? elementFormulationOptions[0];

            var sel = sources.FirstOrDefault(s => s.Expr.ToJson() == existing.LoadExpressionJson);
            if (sel != null) LoadSourceBox.SelectedItem = sel;
            else if (sources.Count > 0) LoadSourceBox.SelectedIndex = 0;
        }
        else
        {
            CalcTypeBox.SelectedItem = CalcType.C;
            MaterialSourceBox.SelectedItem = materialSourceOptions[0];
            ConcreteModelBox.SelectedItem = concreteModelOptions[1];
            SteelModelBox.SelectedItem = steelModelOptions[1];
            ElementFormulationBox.SelectedItem = elementFormulationOptions[0];
            if (LoadSourceBox.Items.Count > 0) LoadSourceBox.SelectedIndex = 0;
        }
        UpdateNonlinearPanelVisibility();
        UpdateNativeMaterialPanelVisibility();
    }

    sealed record LoadSource(string Label, FemLoadExpression Expr);

    /// <summary>Пара «значение для Tcl/хранения» + «локализованная подпись для UI».</summary>
    sealed record ComboOption(string Value, string Label);

    static List<ComboOption> BuildMaterialSourceOptions() =>
    [
        new("Translated", Loc.S("FemMaterialSourceTranslated")),
        new("Native", Loc.S("FemMaterialSourceNative")),
    ];

    static List<ComboOption> BuildConcreteModelOptions() =>
    [
        new("Concrete0102", Loc.S("FemConcreteModelConcrete0102")),
        new("Concrete04", Loc.S("FemConcreteModelConcrete04")),
    ];

    static List<ComboOption> BuildSteelModelOptions() =>
    [
        new("Steel01", "Steel01"),
        new("Steel02", "Steel02"),
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
        NonlinearPanel.Visibility = KindNonlinearRadio.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
    }

    void UpdateNativeMaterialPanelVisibility()
    {
        if (NativeMaterialPanel == null) return;
        NativeMaterialPanel.Visibility =
            (MaterialSourceBox.SelectedItem as ComboOption)?.Value == "Native" ? Visibility.Visible : Visibility.Collapsed;
    }

    void Ok_Click(object sender, RoutedEventArgs e)
    {
        if (LoadSourceBox.SelectedItem is not LoadSource src) { DialogResult = false; return; }
        bool isNonlinear = KindNonlinearRadio.IsChecked == true;

        var pars = new FemAnalysisParams();
        if (isNonlinear)
        {
            pars.CalcType = CalcTypeBox.SelectedItem as CalcType? ?? CalcType.C;
            pars.LoadFactorStep = Pars.ParseAny(LoadFactorStepBox.Text, out var loadStep) && loadStep > 0
                ? loadStep : 0.1;
            pars.MaxLoadFactor = Pars.ParseAny(MaxLoadFactorBox.Text, out var maxLoad) && maxLoad >= pars.LoadFactorStep
                ? maxLoad : Math.Max(10.0, pars.LoadFactorStep);
            pars.ConsiderConcreteTension = ConsiderConcreteTensionCb.IsChecked == true;
            pars.MaterialSource = (MaterialSourceBox.SelectedItem as ComboOption)?.Value ?? "Translated";
            pars.ConcreteModel = (ConcreteModelBox.SelectedItem as ComboOption)?.Value ?? "Concrete04";
            pars.SteelModel = (SteelModelBox.SelectedItem as ComboOption)?.Value ?? "Steel02";
            pars.SteelHardeningRatioOverride =
                Pars.ParseAny(SteelHardeningRatioBox.Text, out var hardening) ? hardening : null;
            pars.ElementFormulation = (ElementFormulationBox.SelectedItem as ComboOption)?.Value ?? "forceBeamColumn";
        }

        Result = new FemAnalysis
        {
            Tag = string.IsNullOrWhiteSpace(TagBox.Text) ? src.Label : TagBox.Text.Trim(),
            Kind = isNonlinear ? "nonlinear" : "linear",
            LoadExpressionJson = src.Expr.ToJson(),
            ParamsJson = pars.ToJson()
        };
        DialogResult = true;
    }

    void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;
}
