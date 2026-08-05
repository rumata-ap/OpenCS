using CScore.PlateStrip;

namespace OpenCS.Views;

/// <summary>Страница контроля сохранённого эквивалентного сечения.</summary>
public partial class EquivalentSectionPage : System.Windows.Controls.UserControl
{
    public EquivalentSectionPage(EquivalentSection section, AppViewModel app)
    {
        InitializeComponent();
        DataContext = new ViewModels.EquivalentSectionVM(section, app);
    }
}
