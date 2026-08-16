using System;
using System.Linq;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using CScore;
using OpenCS.Utilites;
using OpenCS.ViewModels;

namespace OpenCS.Views;

public partial class TotalCurvatureResultView : UserControl
{
    readonly CalcResult _result;
    readonly AppViewModel _app;
    readonly CalcTask _task;

    public TotalCurvatureResultView(CalcResult result, AppViewModel app, CalcTask task)
    {
        _result = result;
        _app = app;
        _task = task;
        InitializeComponent();
        var section = _app.CrossSections.FirstOrDefault(s => s.Id == _task.SectionId);
        SummaryView.DataContext = new TotalCurvatureSummaryVM(
            result, section, _app.CalcSettings, _app.Diagrams);
        SummaryView.StagePlotRequested += OnStagePlotRequested;
    }

    void OnStagePlotRequested(object? sender, int stageNumber)
    {
        var section = _app.CrossSections.FirstOrDefault(s => s.Id == _task.SectionId);
        if (section == null)
        {
            ShowGraphicsError(Loc.S("CalcTaskSectionNotFound"));
            return;
        }

        try
        {
            using var document = JsonDocument.Parse(_result.DataJson);
            bool cracked = document.RootElement.TryGetProperty("cracked", out var crackedValue)
                && crackedValue.ValueKind == JsonValueKind.True
                && crackedValue.GetBoolean();
            var stage = TotalCurvatureStageVM.Parse(
                document.RootElement, stageNumber, cracked);
            if (stage == null || !stage.Converged)
            {
                ShowGraphicsError(Loc.S("TotalCurvature_GraphicsUnavailable"));
                return;
            }

            var window = new TotalCurvatureStageWindow(section, stage, _app, _task)
            {
                Owner = Window.GetWindow(this)
            };
            window.ShowDialog();
        }
        catch (Exception ex)
        {
            ShowGraphicsError(ex.Message);
        }
    }

    static void ShowGraphicsError(string message) =>
        MessageBox.Show(message, Loc.S("Error"),
            MessageBoxButton.OK, MessageBoxImage.Error);
}
