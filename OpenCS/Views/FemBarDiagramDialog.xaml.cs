using System.Windows;
using OpenCS.Utilites;
using OpenCS.ViewModels;
using OpenCS.Views.Helpers;

namespace OpenCS.Views;

/// <summary>Окно эпюр вдоль стержней группы или конструктивного элемента: импортированные усилия
/// и подобранная арматура по сечениям КЭ.</summary>
public partial class FemBarDiagramDialog : Window
{
    readonly FemBarDiagramVM _vm;

    public FemBarDiagramDialog(FemBarDiagramVM vm)
    {
        InitializeComponent();
        _vm = vm;
        DataContext = vm;
        Title = string.Format(Loc.S("FemBarDiagramTitle"), vm.TargetTag);
        vm.Changed += (_, _) => Refresh();
        Refresh();
    }

    void Refresh()
    {
        static List<FemMemberForceCanvas.Segment> Convert(IReadOnlyList<CScore.Fem.BarDiagramSegment> segments) =>
            segments.Select(s => new FemMemberForceCanvas.Segment(s.S0, s.S1, s.V0, s.V1)).ToList();

        // Вторая линия — огибающая наименьших (подобранная арматура) либо опорная Кисп = 1.
        var secondary = _vm.ReferenceLine.Count > 0 ? _vm.ReferenceLine : _vm.Series.Lower;
        canvas.SetData(Convert(_vm.Series.Upper), _vm.Title, Convert(secondary));
        // Сечения с отказом подбора арматуры и непроверенные сечения — маркерами на оси.
        canvas.SetMarkers(_vm.Series.Points
            .Where(p => p.FailureCode != null || _vm.IsUtilization && p.Max == null)
            .Select(p => new FemMemberForceCanvas.Marker(p.S, false, p,
                p.FailureCode != null ? string.Format(Loc.S("FemBarDiagramFailure"), p.FailureCode)
                    : Loc.S("MosaicNotChecked")))
            .ToList());
        minColumn.Visibility = _vm.HasEnvelope ? Visibility.Visible : Visibility.Collapsed;
        valueColumn.Header = Loc.S(_vm.ComparesWithSelected ? "FemBarDiagramColAssigned"
            : _vm.HasEnvelope ? "FemBarDiagramColMax" : "FemBarDiagramColValue");
        minColumn.Header = Loc.S(_vm.ComparesWithSelected ? "FemBarDiagramColSelected" : "FemBarDiagramColMin");
    }
}
