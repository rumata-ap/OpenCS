using System.Globalization;
using System.Windows;
using CScore;
using CScore.Fem;
using OpenCS.Tasks;
using OpenCS.Utilites;

namespace OpenCS.Views;

/// <summary>Диалог создания постановки OpenSees-расчёта схемы (линейный/нелинейный).</summary>
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
        GeomTransfBox.ItemsSource = new[] { "Linear", "PDelta", "Corotational" };

        if (existing != null)
        {
            Title = Loc.S("FemAnalysisEdit");
            TagBox.Text = existing.Tag;
            KindNonlinearRadio.IsChecked = existing.Kind == "nonlinear";
            var pars = FemAnalysisParams.Parse(existing.ParamsJson);
            ExeBox.Text = pars.ExecutablePath;
            TimeoutBox.Text = pars.TimeoutSeconds.ToString();
            CalcTypeBox.SelectedItem = pars.CalcType ?? CalcType.C;
            LoadStepsBox.Text = pars.LoadSteps.ToString();
            ToleranceBox.Text = pars.Tolerance.ToString(CultureInfo.InvariantCulture);
            MaxIterationsBox.Text = pars.MaxIterations.ToString();
            GeomTransfBox.SelectedItem = pars.GeomTransfKind;
            IntegrationPointsBox.Text = pars.IntegrationPoints.ToString();

            var sel = sources.FirstOrDefault(s => s.Expr.ToJson() == existing.LoadExpressionJson);
            if (sel != null) LoadSourceBox.SelectedItem = sel;
            else if (sources.Count > 0) LoadSourceBox.SelectedIndex = 0;
        }
        else
        {
            CalcTypeBox.SelectedItem = CalcType.C;
            GeomTransfBox.SelectedItem = "Linear";
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

        var pars = new FemAnalysisParams
        {
            ExecutablePath = string.IsNullOrWhiteSpace(ExeBox.Text) ? null : ExeBox.Text.Trim(),
            TimeoutSeconds = int.TryParse(TimeoutBox.Text, out var t) && t > 0 ? t : 120
        };
        if (isNonlinear)
        {
            pars.CalcType = CalcTypeBox.SelectedItem as CalcType? ?? CalcType.C;
            pars.LoadSteps = int.TryParse(LoadStepsBox.Text, out var steps) && steps > 0 ? steps : 10;
            pars.Tolerance = double.TryParse(ToleranceBox.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out var tol) && tol > 0
                ? tol : 1e-6;
            pars.MaxIterations = int.TryParse(MaxIterationsBox.Text, out var iters) && iters > 0 ? iters : 50;
            pars.GeomTransfKind = GeomTransfBox.SelectedItem as string ?? "Linear";
            pars.IntegrationPoints = int.TryParse(IntegrationPointsBox.Text, out var ip) && ip > 0 ? ip : 5;
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
