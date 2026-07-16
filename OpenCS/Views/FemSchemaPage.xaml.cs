using System.Windows.Media.Media3D;
using CScore.Fem;
using OpenCS.ViewModels;
using System.Windows.Controls;

namespace OpenCS.Views;

public partial class FemSchemaPage : UserControl
{
    public FemSchemaPage(FemSchema schema, AppViewModel app)
    {
        InitializeComponent();
        var editorVm = new FemSchemaEditorVM(schema, app.db);
        DataContext = editorVm;

        var fem3d = new Fem3DVM(schema, app.db) { Selection = editorVm.Selection, EditMode = true };
        view3D.DataContext = fem3d;

        view3D.NodeCreateRequested += p => editorVm.CreateNodeAt(p.X, p.Y, p.Z);
        view3D.BarCreateRequested  += (a, b) => editorVm.CreateBarBetween(a, b);
        editorVm.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(FemSchemaEditorVM.CreateNodeMode))
                view3D.SetCreateNodeMode(editorVm.CreateNodeMode);
            else if (args.PropertyName == nameof(FemSchemaEditorVM.CreateBarMode))
                view3D.SetCreateBarMode(editorVm.CreateBarMode);
            else if (args.PropertyName == nameof(FemSchemaEditorVM.Session) && !fem3d.IsLoading)
                fem3d.LoadFromSession(editorVm.Session);
        };
    }
}
