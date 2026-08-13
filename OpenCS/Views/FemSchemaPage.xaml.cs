using System.Windows;
using System.Diagnostics;
using System.Windows.Input;
using System.Windows.Media.Media3D;
using CScore.Fem;
using OpenCS.Utilites;
using OpenCS.ViewModels;
using System.Windows.Controls;
using CScore.Fem.Editing;

namespace OpenCS.Views;

public partial class FemSchemaPage : UserControl
{
    readonly FemSchemaEditorVM _editorVm;
    readonly Fem3DVM _fem3d;
    readonly AppViewModel _app;
    readonly FemSchema _schema;

    public FemSchemaPage(FemSchema schema, AppViewModel app)
    {
        _app = app;
        _schema = schema;
        InitializeComponent();
        _editorVm = new FemSchemaEditorVM(schema, app);
        app.RegisterFemSchemaEditor(_editorVm);
        DataContext = _editorVm;

        _fem3d = new Fem3DVM(schema, app.db) { Selection = _editorVm.Selection, EditMode = true };
        _fem3d.LoadFromSession(_editorVm.Session);
        view3D.Editor = _editorVm;
        view3D.DataContext = _fem3d;
        _editorVm.MeshDiscretized += async (_, _) =>
        {
            try
            {
                await _fem3d.LoadMeshOverlayAsync();
                app.ReloadFemMeshSnapshotTree(schema.Id);
                view3D.ShowMeshOverlay();
            }
            catch (Exception exception)
            {
                Debug.WriteLine(exception);
            }
        };
        _editorVm.NodeLoadsApplied += _fem3d.SelectDiagramLoadCase;

        view3D.NodeCreateRequested += p => _editorVm.CreateNodeAt(p.X, p.Y, p.Z);
        view3D.BarCreateRequested  += (a, b) => _editorVm.CreateBarBetween(a, b, view3D.PendingBarSectionTag);
        view3D.CreateNodeModeCloseRequested += () => _editorVm.CreateNodeMode = false;
        view3D.CreateBarModeCloseRequested  += () => _editorVm.CreateBarMode  = false;
        view3D.PlateFrameRequested += tag =>
        {
            var frame = _editorVm.BuildPlateFrame(tag);
            if (frame != null) OpenPlanarRegionMemberDialog(frame);
        };
        view3D.WallFrameRequested += (a, b) =>
        {
            var frame = _editorVm.BuildWallFrame(a, b);
            if (frame != null) OpenPlanarRegionMemberDialog(frame);
        };
        view3D.SpatialPlateFrameRequested += (a, b, c) =>
        {
            var frame = _editorVm.BuildSpatialPlateFrame(a, b, c);
            if (frame != null) OpenPlanarRegionMemberDialog(frame);
        };
        view3D.PlanarRegionEditRequested += OpenPlanarRegionEdit;
        view3D.PlanarRegionDeleteRequested += DeletePlanarRegionMember;
        view3D.SetBarSectionItemsSource(_editorVm.CrossSections);
        view3D.MemberDeleteRequested += tag => _editorVm.DeleteMemberByTag(tag);
        view3D.MemberSplitRequested  += tag => _editorVm.SplitMemberByTag(tag);
        view3D.MemberPropertiesRequested  += OpenMemberProperties;
        view3D.MemberSectionEditRequested += OpenMemberProperties;
        view3D.MemberRotationRequested    += OpenMemberRotation;
        view3D.MemberForcesRequested      += tag => app.ShowMemberForceDiagram(schema, tag);
        view3D.NodeMoveRequested += (tag, dx, dy, dz) => _editorVm.MoveNodeByTag(tag, dx, dy, dz);
        view3D.NodeCopyRequested += (tag, dx, dy, dz) => _editorVm.CopyNodeByTag(tag, dx, dy, dz);
        view3D.NodeDeleteRequested += ConfirmAndDeleteNodes;
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
            else if (args.PropertyName == nameof(FemSchemaEditorVM.CreatePlateMode))
                view3D.SetCreatePlateMode(_editorVm.CreatePlateMode);
            else if (args.PropertyName == nameof(FemSchemaEditorVM.CreateWallMode))
                view3D.SetCreateWallMode(_editorVm.CreateWallMode);
            else if (args.PropertyName == nameof(FemSchemaEditorVM.CreateSpatialPlateMode))
                view3D.SetCreateSpatialPlateMode(_editorVm.CreateSpatialPlateMode);
            else if (args.PropertyName == nameof(FemSchemaEditorVM.Session) && !_fem3d.IsLoading)
                _fem3d.LoadFromSession(_editorVm.Session);
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

    void ConfirmAndDeleteNodes(IReadOnlyList<string> nodeTags)
    {
        var impact = _editorVm.GetNodeDeletionImpact(nodeTags);
        if (impact.NodeCount == 0) return;

        string resourceKey = impact.NodeCount == 1
            ? "FemNodeDeleteConfirm"
            : "FemNodesDeleteConfirm";
        string message = impact.NodeCount == 1
            ? string.Format(Loc.S(resourceKey), nodeTags[0], impact.MemberCount)
            : string.Format(Loc.S(resourceKey), impact.NodeCount, impact.MemberCount);

        if (MessageBox.Show(
                Window.GetWindow(this), message, Loc.S("FemNodeDeleteConfirmTitle"),
                MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
            return;

        _editorVm.DeleteNodesByTags(nodeTags);
    }

    void OpenPlanarRegionMemberDialog(CScore.Planar.Frame3D frame,
        FemMember? existingMember = null, CScore.Planar.PlanarRegion? existingRegion = null)
    {
        var dlg = new PlanarRegionMemberDialog(_app, _schema, frame, existingMember, existingRegion)
        {
            Owner = Window.GetWindow(this)
        };
        dlg.ShowDialog();

        if (dlg.SavedMember is { } saved)
        {
            _editorVm.Session.Members.RemoveAll(m => m.Id == saved.Id);
            _editorVm.Session.Members.Add(saved);
            _editorVm.RefreshCollections();
            _fem3d.LoadFromSession(_editorVm.Session);
        }
        else if (dlg.DeletedMember is { } deleted)
        {
            _editorVm.Session.Members.RemoveAll(m => m.Id == deleted.Id);
            _editorVm.RefreshCollections();
            _fem3d.LoadFromSession(_editorVm.Session);
        }
    }

    void OpenPlanarRegionEdit(string elemTag)
    {
        var member = _editorVm.Session.Members.FirstOrDefault(m => m.ElemTag == elemTag);
        if (member?.PlanarRegionId is not int regionId) return;
        var region = _app.db.GetPlanarRegions(_schema.Id).FirstOrDefault(r => r.Id == regionId);
        if (region == null) return;
        OpenPlanarRegionMemberDialog(region.Frame, member, region);
    }

    void DeletePlanarRegionMember(string elemTag)
    {
        var member = _editorVm.Session.Members.FirstOrDefault(m => m.ElemTag == elemTag);
        if (member == null) return;
        _app.db.DeleteFemMember(member);
        if (member.PlanarRegionId is int regionId) _app.db.DeletePlanarRegion(regionId);

        _editorVm.Session.Members.RemoveAll(m => m.Id == member.Id);
        _editorVm.RefreshCollections();
        _fem3d.LoadFromSession(_editorVm.Session);
    }

    void OpenMemberRotation(string tag)
    {
        var member = _editorVm.Session.Members.FirstOrDefault(m => m.ElemTag == tag);
        if (member == null) return;

        new FemMemberRotationDialog(member.RotationDeg, value =>
        {
            _editorVm.Session.Execute(new SetMemberRotationCommand(member, value));
            _editorVm.RefreshCollections();
            _fem3d.LoadFromSession(_editorVm.Session);
        }) { Owner = Window.GetWindow(this) }.Show();
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
