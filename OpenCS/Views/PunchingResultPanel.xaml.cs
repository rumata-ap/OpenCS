using System.Windows.Controls;

namespace OpenCS.Views;

/// <summary>Панель результата продавливания: вердикт, рисунок, таблица вариантов (DataContext — <see cref="ViewModels.PunchingResultVM"/>).</summary>
public partial class PunchingResultPanel : UserControl
{
    /// <summary>Создаёт панель.</summary>
    public PunchingResultPanel() => InitializeComponent();
}
