using CScore.Fem;
using OpenCS.Utilites;
using OpenCS.ViewModels;

namespace OpenCS.Views;

/// <summary>Модальное окно локальной 2D-эпюры усилий конструктивного стержня
/// в результате расчёта OpenSees.</summary>
public partial class FemMemberForceDialog : System.Windows.Window
{
    /// <summary>Создаёт окно с существующим VM результата и видом локальной эпюры.</summary>
    public FemMemberForceDialog(DatabaseService db, FemSchema schema, string memberTag,
        FemAnalysisResultVM vm)
    {
        InitializeComponent();
        Title = string.Format(Loc.S("FemMemberForceDialogTitle"), memberTag);
        ContentHost.Content = new FemMemberForceView(db, schema, memberTag, vm);
    }

    /// <summary>Esc закрывает окно просмотра (кнопок у него нет); открытый список сначала закрывается сам.</summary>
    protected override void OnKeyDown(System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key == System.Windows.Input.Key.Escape)
        {
            e.Handled = true;
            Close();
            return;
        }
        base.OnKeyDown(e);
    }
}
