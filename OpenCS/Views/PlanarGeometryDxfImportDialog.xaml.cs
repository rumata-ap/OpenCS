using OpenCS.Services;
using OpenCS.Utilites;
using OpenCS.ViewModels;
using System.Windows;

namespace OpenCS.Views;

public partial class PlanarGeometryDxfImportDialog : Window
{
    readonly PlanarGeometryDxfImportVM _vm;

    public PlanarDxfPolygonCandidate? Hull { get; private set; }
    public IReadOnlyList<PlanarDxfPolygonCandidate> Holes { get; private set; } = [];

    public PlanarGeometryDxfImportDialog(IFileDialogService fileDialogService)
    {
        InitializeComponent();
        var app = Application.Current?.MainWindow?.DataContext as AppViewModel;
        _vm = new PlanarGeometryDxfImportVM(fileDialogService, app?.AcadImportSettings,
            app == null ? null : () => app.db.SaveAcadImportSettings(app.AcadImportSettings));
        DataContext = _vm;
    }

    void Ok_Click(object sender, RoutedEventArgs e)
    {
        var hullRow = _vm.SelectedHull;
        if (hullRow == null)
        {
            MessageBox.Show(Loc.S("PlanarDxfImportHullRequired"), Loc.S("Warning"),
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        Hull = hullRow.Candidate;
        Holes = _vm.SelectedHoles;
        DialogResult = true;
    }

    void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;
}
