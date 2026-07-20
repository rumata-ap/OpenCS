using System.Windows;
using System.Diagnostics;
using System.Windows.Input;
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
        app.RegisterFemSchemaEditor(_editorVm);
        DataContext = _editorVm;

        var fem3d = new Fem3DVM(schema, app.db) { Selection = _editorVm.Selection, EditMode = true };
        fem3d.LoadFromSession(_editorVm.Session);
        view3D.Editor = _editorVm;
        view3D.DataContext = fem3d;
        _editorVm.MeshDiscretized += async (_, _) =>
        {
            try
            {
                await fem3d.LoadMeshOverlayAsync();
                app.ReloadFemMeshSnapshotTree(schema.Id);
                view3D.ShowMeshOverlay();
            }
            catch (Exception exception)
            {
                Debug.WriteLine(exception);
            }
        };
        _editorVm.NodeLoadsApplied += fem3d.SelectDiagramLoadCase;

        view3D.NodeCreateRequested += p => _editorVm.CreateNodeAt(p.X, p.Y, p.Z);
        view3D.BarCreateRequested  += (a, b) => _editorVm.CreateBarBetween(a, b, view3D.PendingBarSectionTag);
        view3D.CreateNodeModeCloseRequested += () => _editorVm.CreateNodeMode = false;
        view3D.CreateBarModeCloseRequested  += () => _editorVm.CreateBarMode  = false;
        view3D.SetBarSectionItemsSource(_editorVm.CrossSections);
        view3D.MemberDeleteRequested += tag => _editorVm.DeleteMemberByTag(tag);
        view3D.MemberSplitRequested  += tag => _editorVm.SplitMemberByTag(tag);
        view3D.MemberPropertiesRequested  += OpenMemberProperties;
        view3D.MemberSectionEditRequested += OpenMemberProperties;
        view3D.MemberForcesRequested      += tag => app.ShowMemberForceDiagram(schema, tag);
        view3D.NodeMoveRequested += (tag, dx, dy, dz) => _editorVm.MoveNodeByTag(tag, dx, dy, dz);
        view3D.NodeCopyRequested += (tag, dx, dy, dz) => _editorVm.CopyNodeByTag(tag, dx, dy, dz);
        view3D.NodePropertiesRequested += tag =>
        {
            var node = _editorVm.Session.Nodes.FirstOrDefault(n => n.NodeTag == tag);
            if (node == null) return;
            var dlg = new FemNodePropertiesDialog(node, _editorVm) { Owner = Window.GetWindow(this) };
            dlg.MemberSelected += elemTag => _editorVm.Selection.ToggleElement(elemTag, additive: false);
            dlg.Show();
        };
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

        PreviewKeyDown += OnPreviewKeyDown;
    }

    void CreateMember_Click(object sender, RoutedEventArgs e)
        => _editorVm.CreateMemberGroupFromElements(barsGrid.SelectedItems.OfType<FemMember>());

    void OpenMemberProperties(string tag)
    {
        var member = _editorVm.Session.Members.FirstOrDefault(m => m.ElemTag == tag);
        if (member == null) return;
        new FemMemberPropertiesDialog(member, _editorVm) { Owner = Window.GetWindow(this) }.Show();
    }

    void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        bool ctrl = Keyboard.Modifiers == ModifierKeys.Control;
        if (!ctrl) return;

        if (e.Key == Key.C)
        {
            _editorVm.CopySelection();
        }
        else if (e.Key == Key.V && _editorVm.HasClipboard)
        {
            var dlg = new FemFragmentOffsetDialog { Owner = Window.GetWindow(this) };
            if (dlg.ShowDialog() == true) _editorVm.PasteClipboard(dlg.Dx, dlg.Dy, dlg.Dz);
        }
        else if (e.Key == Key.Z && _editorVm.UndoCommand.CanExecute(null))
        {
            _editorVm.UndoCommand.Execute(null);
        }
        else if (e.Key == Key.Y && _editorVm.RedoCommand.CanExecute(null))
        {
            _editorVm.RedoCommand.Execute(null);
        }
    }
}
