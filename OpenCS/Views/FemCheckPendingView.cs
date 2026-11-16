using System.Windows;
using System.Windows.Controls;

namespace OpenCS.Views;

/// <summary>
/// Страница проверки МКЭ без результата (ещё не рассчитана): пояснение и кнопки «Запустить проверку» и
/// «Редактировать…» — вместо пустой центральной панели при выборе такой проверки в дереве.
/// </summary>
public sealed class FemCheckPendingView : UserControl
{
    /// <summary>Проверка, для которой показана страница.</summary>
    public CScore.Fem.FemCheck Check { get; }

    public FemCheckPendingView(CScore.Fem.FemCheck check, AppViewModel app)
    {
        Check = check;
        var title = new TextBlock { Text = check.DisplayTag, FontSize = 15, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 6) };
        var text = new TextBlock { TextWrapping = TextWrapping.Wrap, MaxWidth = 520, Margin = new Thickness(0, 0, 0, 12) };
        text.SetResourceReference(TextBlock.TextProperty, "FemCheckPendingText");

        var run = new Button { Padding = new Thickness(12, 4, 12, 4), MinWidth = 120 };
        run.SetResourceReference(ContentProperty, "RunFemCheck");
        run.Click += (_, _) => { if (app.RunFemCheckCommand.CanExecute(check)) app.RunFemCheckCommand.Execute(check); };
        var edit = new Button { Padding = new Thickness(12, 4, 12, 4), MinWidth = 120, Margin = new Thickness(8, 0, 0, 0) };
        edit.SetResourceReference(ContentProperty, "FemCheckEdit");
        edit.Click += (_, _) => { if (app.EditFemCheckCommand.CanExecute(check)) app.EditFemCheckCommand.Execute(check); };

        var buttons = new StackPanel { Orientation = Orientation.Horizontal };
        buttons.Children.Add(run);
        buttons.Children.Add(edit);
        var root = new StackPanel { Margin = new Thickness(16), HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top };
        root.Children.Add(title);
        root.Children.Add(text);
        root.Children.Add(buttons);
        Content = root;
    }
}
