using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using CScore;
using OpenCS.Converters;
using OpenCS.Services;
using OpenCS.Utilites;
using OpenCS.Views;
using Xunit;

namespace OpenCS.Tests;

/// <summary>
/// Окно результата load_history в настоящем окне WPF. Карте полей нужны ресурсы приложения (StaticResource),
/// а Application в общем прогоне ломает соседние STA-тесты окон, поэтому тест ручной — в отдельном процессе:
/// <c>OPENCS_UI_APP_TESTS=1 dotnet test OpenCS.Tests --filter FullyQualifiedName~LoadHistoryResultViewTests</c>.
/// Без переменной тест выходит.
/// </summary>
public sealed class LoadHistoryResultViewTests
{
    /// <summary>
    /// На вкладке «Поля» отмечен «относительно включения части» — при первом открытии, после перехода на другую
    /// вкладку и обратно и в окне результата следующей задачи (окно подменяется, как при выборе в дереве).
    /// </summary>
    [Fact]
    public void FieldsTab_RelativeCheckedByDefault()
    {
        if (Environment.GetEnvironmentVariable("OPENCS_UI_APP_TESTS") != "1") return;
        Exception? error = null;
        var thread = new Thread(() =>
        {
            try
            {
                var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
                app.Resources.Add("BoolToVisibility", new BooleanToVisibilityConverter());
                app.Resources.Add("EnumToBoolConverter", new EnumToBoolConverter());

                var (result, section) = LoadHistoryResultVMTests.Run();
                var host = new ContentControl();
                var window = new Window { Width = 1200, Height = 900, Content = host };
                ShowOffscreen(window);

                var first = new LoadHistoryResultView(result, section, CalcType.C, CalcSettings.Default);
                host.Content = first;
                AssertRelativeChecked(SelectTab(first, 2), "первое открытие");
                SelectTab(first, 0);
                AssertRelativeChecked(SelectTab(first, 2), "возврат на вкладку");
                // следующее окно создаётся, пока текущее на «Шагах»: переключатели скрытых вкладок «Поля» обоих окон
                // вне визуального дерева, и при общем GroupName WPF считал их одной группой (корень null)
                SelectTab(first, 0);

                var second = new LoadHistoryResultView(result, section, CalcType.C, CalcSettings.Default);
                host.Content = second;
                AssertRelativeChecked(SelectTab(second, 2), "следующая задача");
                window.Close();
                app.Shutdown();
            }
            catch (Exception ex) { error = ex; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (error != null) throw error;
    }

    /// <summary>
    /// То же на демобазе (<c>OPENCS_UI_DEMO_DB</c> — путь к load-history-demo.db; берётся копия): результаты всех
    /// задач load_history открываются по очереди в одном окне, у каждого — вкладка «Поля».
    /// </summary>
    [Fact]
    public void FieldsTab_RelativeChecked_DemoDbTasks()
    {
        var src = Environment.GetEnvironmentVariable("OPENCS_UI_DEMO_DB");
        if (Environment.GetEnvironmentVariable("OPENCS_UI_APP_TESTS") != "1" || string.IsNullOrEmpty(src)) return;
        var path = Path.Combine(Path.GetTempPath(), $"lh-ui-{Guid.NewGuid():N}.db");
        File.Copy(src, path);
        Exception? error = null;
        var thread = new Thread(() =>
        {
            try
            {
                var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
                app.Resources.Add("BoolToVisibility", new BooleanToVisibilityConverter());
                app.Resources.Add("EnumToBoolConverter", new EnumToBoolConverter());
                var settings = CalcSettings.Default;
                using var db = new DatabaseService(path);
                db.LoadAll();
                var host = new ContentControl();
                var window = new Window { Width = 1200, Height = 900, Content = host };
                ShowOffscreen(window);
                foreach (var task in db.CalcTasks.Where(t => t.Kind == "load_history").OrderBy(t => t.Num))
                {
                    var header = db.CalcResults.Where(r => r.TaskId == task.Id).OrderBy(r => r.Id).Last();
                    var result = db.GetCalcResultById(header.Id)!;
                    var staged = (StagedSection)db.CrossSections.Single(s => s.Id == task.SectionId);
                    staged.ResolveAndBuildDiagramms(settings.Sp63DescEtaMin, pool: db.Diagrams,
                        rebarDifferentialDiagram: settings.RebarDifferentialDiagram, ekbEtaMin: settings.EkbDescEtaMin);
                    var view = new LoadHistoryResultView(result, staged, task.CalcType, settings);
                    host.Content = view;
                    var radios = SelectTab(view, 2);
                    AssertRelativeChecked(radios, task.Tag);
                    // ползунок шага: поля пересобираются (при гистерезисе — воспроизведением)
                    var slider = FindVisual<Slider>(view);
                    slider.Value = slider.Maximum / 2;
                    Thread.Sleep(300); // Delay привязки — 150 мс
                    view.Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
                    AssertRelativeChecked(Radios(view), $"{task.Tag}, ползунок");
                    // щелчок «полные», затем снова «относительно» — как пользователь
                    Click(radios[1]);
                    Assert.True(radios[1].IsChecked == true && radios[0].IsChecked == false, $"{task.Tag}: после «полные» — {State(radios)}");
                    Click(radios[0]);
                    Assert.True(radios[0].IsChecked == true && radios[1].IsChecked == false, $"{task.Tag}: после «относительно» — {State(radios)}");
                    SelectTab(view, 0); // следующую задачу открывают из дерева, пока эта на «Шагах»
                }
                window.Close();
                app.Shutdown();
            }
            catch (Exception ex) { error = ex; }
            finally { Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools(); }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        try { File.Delete(path); } catch (IOException) { }
        if (error != null) throw error;
    }

    static T FindVisual<T>(DependencyObject d) where T : DependencyObject
    {
        if (d is T t) return t;
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(d); i++)
            if (FindVisualOrNull<T>(VisualTreeHelper.GetChild(d, i)) is { } found) return found;
        throw new InvalidOperationException($"{typeof(T).Name} не найден");
    }

    static T? FindVisualOrNull<T>(DependencyObject d) where T : DependencyObject
    {
        if (d is T t) return t;
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(d); i++)
            if (FindVisualOrNull<T>(VisualTreeHelper.GetChild(d, i)) is { } found) return found;
        return null;
    }

    static void Click(RadioButton r)
    {
        var peer = new System.Windows.Automation.Peers.RadioButtonAutomationPeer(r);
        ((System.Windows.Automation.Provider.ISelectionItemProvider)peer.GetPattern(
            System.Windows.Automation.Peers.PatternInterface.SelectionItem)).Select();
        r.Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
    }

    static string State(List<RadioButton> radios) => string.Join(", ", radios.Select(r => r.IsChecked?.ToString() ?? "null"));

    static List<RadioButton> SelectTab(LoadHistoryResultView view, int index)
    {
        ((TabControl)view.FindName("MainTabs")).SelectedIndex = index;
        view.UpdateLayout();
        view.Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
        return Radios(view);
    }

    /// <summary>Переключатели «относительно включения / полные» вкладки «Поля» (в визуальном дереве).</summary>
    static List<RadioButton> Radios(DependencyObject root)
    {
        var list = new List<RadioButton>();
        void Walk(DependencyObject d)
        {
            if (d is RadioButton r && r.GetBindingExpression(System.Windows.Controls.Primitives.ToggleButton.IsCheckedProperty)
                    ?.ParentBinding.Path.Path is "FieldsRelative" or "FieldsTotal")
                list.Add(r);
            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(d); i++) Walk(VisualTreeHelper.GetChild(d, i));
        }
        Walk(root);
        return list;
    }

    static void AssertRelativeChecked(List<RadioButton> radios, string when)
    {
        Assert.True(radios.Count == 2, $"{when}: переключателей {radios.Count}");
        Assert.True(radios[0].IsChecked == true, $"{when}: «относительно включения части» не отмечен");
        Assert.True(radios[1].IsChecked == false, $"{when}: «полные деформации» отмечен");
    }

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
