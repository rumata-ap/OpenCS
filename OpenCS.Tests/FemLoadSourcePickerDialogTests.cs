using System.Windows;
using CScore.Combinations;
using CScore.Fem;
using OpenCS.ViewModels;
using OpenCS.Views;
using Xunit;

namespace OpenCS.Tests;

/// <summary>Разбор XAML окна выбора нагрузки и диалога постановки с ячейкой нагрузки стадии (без запуска программы).</summary>
public sealed class FemLoadSourcePickerDialogTests
{
    [Fact]
    public void Dialogs_ParseXaml_AndShowSources()
    {
        Exception? error = null;
        var thread = new Thread(() =>
        {
            try
            {
                // Без Application: глобальный экземпляр из этого потока ломал соседние STA-тесты окон. Иконки
                // загружений в обоих окнах — DynamicResource, без ресурсов приложения просто пустые.
                var cases = new List<FemLoadCase>
                {
                    new() { Id = 1, Tag = "Вес", Sp20Type = "permanent" },
                    new() { Id = 2, Tag = "Снег", Sp20Type = "short_term" },
                };
                var definition = new FemLoadDefinition { Id = 7, Tag = "Огибающая C 001", CombinationType = "C", SourceKind = "envelope" };
                definition.SetExpression(new FemLoadExpression
                {
                    Mode = FemLoadExpressionMode.Sum,
                    Terms = [new FemLoadTerm { LoadCaseId = 1, Coefficient = 1.35 }, new FemLoadTerm { LoadCaseId = 2, Coefficient = 1.05 }],
                });
                var sources = FemLoadSources.Build([definition], cases, Sp20GammaDefaults.Standard);

                var picker = new FemLoadSourcePickerDialog(sources, sources[0], "Стадия 1");
                Assert.Equal(sources[0], ((FemLoadSourcePickerVM)picker.DataContext).Selected);
                ShowOffscreen(picker);
                Assert.Equal(1, picker.CombinationsGrid.Items.Count);
                picker.Close();

                var schema = new FemSchema { Id = 1, Tag = "Схема" };
                foreach (var lc in cases) schema.LoadCases.Add(lc);
                schema.LoadDefinitions.Add(definition);
                var analysis = new FemAnalysis
                {
                    SchemaId = 1, Tag = "Нелинейный", Kind = OpenCS.Services.FemCsfeaRunner.AnalysisKind,
                    LoadExpressionJson = definition.ExpressionJson,
                    ParamsJson = new OpenCS.Tasks.FemAnalysisParams
                    {
                        Stages = [new OpenCS.Tasks.FemAnalysisStage { Tag = "Огибающая C 001", LoadExpressionJson = definition.ExpressionJson }],
                    }.ToJson(),
                };
                var dialog = new FemAnalysisDialog(schema, [], analysis, Sp20GammaDefaults.Standard);
                ShowOffscreen(dialog);
                Assert.Equal(1, dialog.StagesGrid.Items.Count);
                dialog.Close();
            }
            catch (Exception ex) { error = ex; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();

        Assert.Null(error);
    }

    /// <summary>Показ за пределами экрана — чтобы шаблоны ячеек создались и привязки отработали.</summary>
    static void ShowOffscreen(Window window)
    {
        window.WindowStartupLocation = WindowStartupLocation.Manual;
        window.Left = -20000;
        window.Top = -20000;
        window.ShowActivated = false;
        window.Show();
        window.UpdateLayout();
        window.Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
    }
}
