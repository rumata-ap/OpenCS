using System.Windows;
using OpenCS.ViewModels;
using OpenCS.Views.Helpers;

namespace OpenCS.Views;

/// <summary>
/// Детальные эпюры вдоль выбранных стержней результата CSfea: НДС сечения в n точках КЭ; щелчок по маркеру точки
/// Лобатто — окно «НДС сечения» этой точки (<see cref="SectionStateRequested"/>).
/// </summary>
public partial class FemCsfeaBarDetailDialog : Window
{
    readonly FemCsfeaBarDetailVM _vm;

    /// <summary>Щелчок по маркеру точки Лобатто: номер КЭ и точка (0 — i, 1 — ½, 2 — j).</summary>
    public event Action<int, int>? SectionStateRequested;

    public FemCsfeaBarDetailDialog(FemCsfeaBarDetailVM vm, string title)
    {
        InitializeComponent();
        _vm = vm;
        DataContext = vm;
        Title = title;
        vm.Changed += (_, _) => Refresh();
        canvas.MarkerClicked += key =>
        {
            if (key is FemCsfeaBarDetailMarker { LobattoPoint: int point } m) SectionStateRequested?.Invoke(m.ElemNum, point);
        };
        Refresh();
    }

    void Refresh()
    {
        canvas.SetData(_vm.Segments.Select(s => new FemMemberForceCanvas.Segment(s.S0, s.S1, s.V0, s.V1)).ToList(), _vm.Title);
        canvas.SetMarkers(_vm.Markers.Select(m => new FemMemberForceCanvas.Marker(m.S, !m.Failed, m, m.Label, m.Failed)).ToList());
        valueColumn.Header = _vm.Title;
    }
}
