using System.Windows;
using CScore.Fem;
using OpenCS.Tasks;
using OpenCS.Utilites;

namespace OpenCS.Views;

/// <summary>Диалог создания постановки линейного OpenSees-расчёта схемы.</summary>
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

        if (existing != null)
        {
            Title = Loc.S("FemAnalysisEdit");
            TagBox.Text = existing.Tag;
            var pars = System.Text.Json.JsonSerializer.Deserialize<FemAnalysisParams>(existing.ParamsJson) ?? new FemAnalysisParams();
            ExeBox.Text = pars.ExecutablePath;
            TimeoutBox.Text = pars.TimeoutSeconds.ToString();
            
            var sel = sources.FirstOrDefault(s => s.Expr.ToJson() == existing.LoadExpressionJson);
            if (sel != null) LoadSourceBox.SelectedItem = sel;
            else if (sources.Count > 0) LoadSourceBox.SelectedIndex = 0;
        }
        else
        {
            if (LoadSourceBox.Items.Count > 0) LoadSourceBox.SelectedIndex = 0;
        }
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

    void Ok_Click(object sender, RoutedEventArgs e)
    {
        if (LoadSourceBox.SelectedItem is not LoadSource src) { DialogResult = false; return; }
        var pars = new FemAnalysisParams
        {
            ExecutablePath = string.IsNullOrWhiteSpace(ExeBox.Text) ? null : ExeBox.Text.Trim(),
            TimeoutSeconds = int.TryParse(TimeoutBox.Text, out var t) && t > 0 ? t : 120
        };
        Result = new FemAnalysis
        {
            Tag = string.IsNullOrWhiteSpace(TagBox.Text) ? src.Label : TagBox.Text.Trim(),
            Kind = "linear",
            LoadExpressionJson = src.Expr.ToJson(),
            ParamsJson = pars.ToJson()
        };
        DialogResult = true;
    }

    void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;
}
