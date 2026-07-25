using System.Globalization;
using System.Windows;
using CScore;
using CScore.Fem;
using OpenCS.Tasks;
using OpenCS.Utilites;

namespace OpenCS.Views;

/// <summary>Диалог создания постановки OpenSees-расчёта схемы (линейный/нелинейный). Solver-
/// настройки (исполняемый файл, сходимость, источник/модель материалов и т.п.) — глобальные,
/// см. вкладку «OpenSees» в диалоге настроек (SettingsWindow).</summary>
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

        if (existing != null)
        {
            Title = Loc.S("FemAnalysisEdit");
            TagBox.Text = existing.Tag;
            KindNonlinearRadio.IsChecked = existing.Kind == "nonlinear";
            var pars = FemAnalysisParams.Parse(existing.ParamsJson);
            CalcTypeBox.SelectedItem = pars.CalcType ?? CalcType.C;
            LoadFactorStepBox.Text = pars.LoadFactorStep.ToString(CultureInfo.InvariantCulture);
            MaxLoadFactorBox.Text = pars.MaxLoadFactor.ToString(CultureInfo.InvariantCulture);

            var sel = sources.FirstOrDefault(s => s.Expr.ToJson() == existing.LoadExpressionJson);
            if (sel != null) LoadSourceBox.SelectedItem = sel;
            else if (sources.Count > 0) LoadSourceBox.SelectedIndex = 0;
        }
        else
        {
            CalcTypeBox.SelectedItem = CalcType.C;
            if (LoadSourceBox.Items.Count > 0) LoadSourceBox.SelectedIndex = 0;
        }
        UpdateNonlinearPanelVisibility();
    }

    sealed record LoadSource(string Label, FemLoadExpression Expr);

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

    void Ok_Click(object sender, RoutedEventArgs e)
    {
        if (LoadSourceBox.SelectedItem is not LoadSource src) { DialogResult = false; return; }
        bool isNonlinear = KindNonlinearRadio.IsChecked == true;

        var pars = new FemAnalysisParams();
        if (isNonlinear)
        {
            pars.CalcType = CalcTypeBox.SelectedItem as CalcType? ?? CalcType.C;
            pars.LoadFactorStep = double.TryParse(LoadFactorStepBox.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out var loadStep) && loadStep > 0
                ? loadStep : 0.1;
            pars.MaxLoadFactor = double.TryParse(MaxLoadFactorBox.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out var maxLoad) && maxLoad >= pars.LoadFactorStep
                ? maxLoad : Math.Max(10.0, pars.LoadFactorStep);
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
