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
}
