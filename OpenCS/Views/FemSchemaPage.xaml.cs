using System.Windows;
using System.Windows.Media.Media3D;
using CScore.Fem;
using OpenCS.Utilites;
using OpenCS.ViewModels;
using System.Windows.Controls;

namespace OpenCS.Views;

public partial class FemSchemaPage : UserControl
{
    readonly FemSchemaEditorVM _editorVm;

    public FemSchemaPage(FemSchema schema, AppViewModel app)
    {
        InitializeComponent();
        _editorVm = new FemSchemaEditorVM(schema, app);
        DataContext = _editorVm;

        var fem3d = new Fem3DVM(schema, app.db) { Selection = _editorVm.Selection, EditMode = true };
        view3D.DataContext = fem3d;

        view3D.NodeCreateRequested += p => _editorVm.CreateNodeAt(p.X, p.Y, p.Z);
        view3D.BarCreateRequested  += (a, b) => _editorVm.CreateBarBetween(a, b);
        _editorVm.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(FemSchemaEditorVM.CreateNodeMode))
                view3D.SetCreateNodeMode(_editorVm.CreateNodeMode);
            else if (args.PropertyName == nameof(FemSchemaEditorVM.CreateBarMode))
                view3D.SetCreateBarMode(_editorVm.CreateBarMode);
            else if (args.PropertyName == nameof(FemSchemaEditorVM.Session) && !fem3d.IsLoading)
                fem3d.LoadFromSession(_editorVm.Session);
        };
        _editorVm.SaveBlocked += errors => MessageBox.Show(
            string.Join("\n", errors.Select(d => d.Message)),
            Loc.S("FemSaveBlockedTitle"), MessageBoxButton.OK, MessageBoxImage.Warning);
    }

    void CreateMember_Click(object sender, RoutedEventArgs e)
        => _editorVm.CreateMemberFromElements(barsGrid.SelectedItems.OfType<FemElement>());
}
