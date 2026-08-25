using System.Windows.Controls;

using CScore;
using OpenCS.ViewModels;

namespace OpenCS.Views;

/// <summary>Страница редактирования группы поперечного армирования.</summary>
public partial class StirrupGroupPage : UserControl
{
    /// <summary>Создаёт страницу и её ViewModel.</summary>
    public StirrupGroupPage(MaterialArea area, AppViewModel app)
    {
        InitializeComponent();
        DataContext = new StirrupGroupVM(area, app);
    }
}
