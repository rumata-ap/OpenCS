using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using CScore;
using CScore.PrestressLoss;
using OpenCS.Utilites;
using OpenCS.ViewModels;

namespace OpenCS.Views;

public partial class PrestressLossResultView : UserControl
{
    public PrestressLossResultView(CalcResult result, AppViewModel app)
    {
        InitializeComponent();
        DataContext = new PrestressLossResultVM(result, app);
    }
}

class MessageRow
{
    public string Text  { get; init; } = "";
    public Brush  Brush { get; init; } = Brushes.Red;
}

class PrestressGroupResultRow
{
    public string AreaTag     { get; init; } = "";
    public double AreaMm2     { get; init; }
    public double SigSp0      { get; init; }
    public double DSp1        { get; init; }
    public double DSp2        { get; init; }
    public double DSp3        { get; init; }
    public double DSp4        { get; init; }
    public double DSp7        { get; init; }
    public double TotalFirst  { get; init; }
    public double SigSp1      { get; init; }
    public double SigmaBpj    { get; init; }
    public double DSp5        { get; init; }
    public double DSp6        { get; init; }
    public double TotalAll    { get; init; }
    public double SigSp2      { get; init; }
    public string MinLossText { get; init; } = "";
}

class PrestressLossResultVM : ViewModelBase
{
    readonly AppViewModel        _app;
    readonly PrestressLossResult _result;

    public List<MessageRow>              Messages  { get; }
    public bool                          HasMessages => Messages.Count > 0;
    public List<PrestressGroupResultRow> GroupRows { get; }

    public string PrecompForceFirstText => $"{_result.PrecompForceFirst:F1}";
    public string PrecompForceTotalText => $"{_result.PrecompForceTotal:F1}";

    public ICommand ApplyCommand { get; }

    public PrestressLossResultVM(CalcResult result, AppViewModel app)
    {
        _app = app;

        PrestressLossResult r;
        try { r = JsonSerializer.Deserialize<PrestressLossResult>(result.DataJson) ?? new(); }
        catch { r = new PrestressLossResult { Errors = ["Ошибка разбора DataJson"] }; }
        _result = r;

        Messages = r.Errors.Select(e => new MessageRow { Text = e, Brush = Brushes.Red }).ToList();
        Messages.AddRange(r.Warnings.Select(w => new MessageRow { Text = w, Brush = Brushes.DarkOrange }));

        GroupRows = r.Groups.Select(g => new PrestressGroupResultRow
        {
            AreaTag    = g.AreaTag,
            AreaMm2    = g.AreaMm2,
            SigSp0     = g.SigSp0,
            DSp1       = g.DSp1,
            DSp2       = g.DSp2,
            DSp3       = g.DSp3,
            DSp4       = g.DSp4,
            DSp7       = g.DSp7,
            TotalFirst = g.TotalFirst,
            SigSp1     = g.SigSp1,
            SigmaBpj   = g.SigmaBpj,
            DSp5       = g.DSp5,
            DSp6       = g.DSp6,
            TotalAll   = g.TotalAll,
            SigSp2     = g.SigSp2,
            MinLossText = g.MinLossWarning ? "!" : "OK"
        }).ToList();

        ApplyCommand = new RelayCommand(_ => Apply(), _ => r.Errors.Count == 0 && r.Groups.Count > 0);
    }

    void Apply()
    {
        int count = 0;
        foreach (var gr in _result.Groups)
        {
            var area = _app.CrossSections
                .SelectMany(s => s.Areas)
                .FirstOrDefault(a => a.Id == gr.AreaId);
            if (area == null) continue;
            area.SigSp = gr.SigSp2;
            area.PropagateEps_p();
            _app.db.SaveMaterialArea(area);
            count++;
        }
        MessageBox.Show(
            string.Format(Loc.S("PrestressApplied"), count),
            Loc.S("Info"),
            MessageBoxButton.OK, MessageBoxImage.Information);
    }
}
