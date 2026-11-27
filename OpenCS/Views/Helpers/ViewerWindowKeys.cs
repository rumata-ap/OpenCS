using System.Windows;
using System.Windows.Input;

namespace OpenCS.Views.Helpers;

/// <summary>Общие клавиши окон просмотра результатов (окна без кнопок OK/Отмена).</summary>
public static class ViewerWindowKeys
{
    /// <summary>
    /// Esc закрывает окно. Событие всплывает, поэтому открытый список, правка ячейки или отмена начатого разреза
    /// (они помечают Esc обработанным) срабатывают первыми, а окно закрывается следующим нажатием.
    /// </summary>
    public static void CloseOnEscape(Window window)
    {
        ArgumentNullException.ThrowIfNull(window);
        window.KeyDown += (_, e) =>
        {
            if (e.Key != Key.Escape || e.Handled) return;
            e.Handled = true;
            window.Close();
        };
    }
}
