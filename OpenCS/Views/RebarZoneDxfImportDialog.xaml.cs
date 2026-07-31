using OpenCS.Services;
using OpenCS.Utilites;
using OpenCS.ViewModels;
using System.Windows;

namespace OpenCS.Views;

public partial class RebarZoneDxfImportDialog : Window
{
    readonly RebarZoneDxfImportVM _vm;

    public IReadOnlyList<PlanarDxfPolygonCandidate> AcceptedIncluded { get; private set; } = [];

    public RebarZoneDxfImportDialog(IFileDialogService fileDialogService)
    {
        InitializeComponent();
        _vm = new RebarZoneDxfImportVM(fileDialogService);
        DataContext = _vm;
    }

    void Ok_Click(object sender, RoutedEventArgs e)
    {
        var included = _vm.GetIncludedCandidates();
        if (included.Count == 0)
        {
            MessageBox.Show(Loc.S("PlanarDxfImportNothingSelected"), Loc.S("Warning"),
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        AcceptedIncluded = included;
        DialogResult = true;
    }

    void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;
}
