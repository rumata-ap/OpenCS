using CScore;
using OpenCS.Utilites;
using OpenCS.Views.Helpers;

namespace OpenCS.Views;

/// <summary>Модальное read-only окно геометрии поперечного сечения конструктивного
/// стержня из результата расчёта OpenSees.</summary>
public partial class FemMemberSectionDialog : System.Windows.Window
{
    /// <summary>Canvas preview, доступный для STA-тестов и внешней проверки вида.</summary>
    public PlotCanvas PreviewCanvas => preview;

    /// <summary>Создаёт read-only preview обычного или двухстадийного сечения.</summary>
    public FemMemberSectionDialog(CrossSection section, PlotSettings settings)
    {
        InitializeComponent();
        Title = string.Format(Loc.S("FemMemberSectionDialogTitle"), section.Tag);

        preview.ApplySettings(settings);
        var data = CrossSectionPlotBuilder.Build(section);
        preview.Draw(data.Elements, data.XMin, data.XMax, data.YMin, data.YMax, squareAxes: true);
    }
}
