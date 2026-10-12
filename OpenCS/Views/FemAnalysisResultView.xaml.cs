using System.ComponentModel;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using OpenCS.OpenSees.Structural;
using OpenCS.Utilites;
using OpenCS.ViewModels;

namespace OpenCS.Views;

/// <summary>
/// Таблицы и графики результата OpenSees-расчёта FEM-схемы: перемещения, реакции, усилия, точки сечений, история λ;
/// ползунок шага. Сам результат (деформации, эпюры, выбор и меню) — в 3D-виде схемы; меню строк таблиц — те же
/// пункты (2D-эпюра, сечение и набор усилий КонЭ, значения в узле, состояние сечения).
/// </summary>
public partial class FemAnalysisResultView : UserControl
{
    readonly FemAnalysisResultVM _vm;

    /// <summary>«Показать в 3D-виде схемы» — на шаге этого окна.</summary>
    public Action? ShowInSchema { get; init; }

    public FemAnalysisResultView(FemAnalysisResultVM vm)
    {
        InitializeComponent();
        _vm = vm;
        DataContext = _vm;
        displacementModeBox.ItemsSource = System.Enum.GetValues<FemDisplacementDisplayMode>();
        displacementModeBox.SelectedItem = _vm.DisplacementDisplayMode;
        displacementLengthUnitBox.ItemsSource = System.Enum.GetValues<FemLengthUnit>();
        displacementLengthUnitBox.SelectedItem = _vm.DisplacementLengthUnit;
        rotationDisplayScaleBox.ItemsSource = System.Enum.GetValues<FemRotationScale>();
        rotationDisplayScaleBox.SelectedItem = _vm.RotationDisplayScale;
        UpdateDisplacementTableColumns();
        BuildLoadFactorCanvas();
        loadFactorCanvas.StepClicked += idx => _vm.SelectedStepIndex = idx;
        controlDispCanvas.StepClicked += idx => _vm.SelectedStepIndex = idx;
        _vm.PropertyChanged += OnVmPropertyChanged;
    }

    void BuildLoadFactorCanvas()
    {
        loadFactorCanvas.SetData(_vm.LoadFactorPoints.Select(p => ((double)p.Step, p.LoadFactor, p.Converged, 0)).ToList(), _vm.SelectedStepIndex);
        controlDispCanvas.SetData(_vm.ControlDisplacementPoints, _vm.SelectedStepIndex);
    }

    void OnVmPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(FemAnalysisResultVM.SelectedStepIndex))
        {
            BuildLoadFactorCanvas();
        }
        else if (e.PropertyName is nameof(FemAnalysisResultVM.SelectedDisplayedDisplacementRow)
            or nameof(FemAnalysisResultVM.SelectedDisplacementRow))
        {
            if (_vm.SelectedDisplayedDisplacementRow is { } dispRow)
                displacementsGrid.ScrollIntoView(dispRow);
        }
        else if (e.PropertyName is nameof(FemAnalysisResultVM.DisplayedDisplacements)
            or nameof(FemAnalysisResultVM.DisplacementDisplayMode)
            or nameof(FemAnalysisResultVM.DisplacementLengthUnit)
            or nameof(FemAnalysisResultVM.RotationDisplayScale))
        {
            UpdateDisplacementTableColumns();
        }
        else if (e.PropertyName == nameof(FemAnalysisResultVM.SelectedReactionRow) && _vm.SelectedReactionRow is { } reactRow)
        {
            reactionsGrid.ScrollIntoView(reactRow);
        }
        else if (e.PropertyName == nameof(FemAnalysisResultVM.SelectedForceRow) && _vm.SelectedForceRow is { } forceRow)
        {
            elementForcesGrid.ScrollIntoView(forceRow);
        }
    }

    void ShowInSchema_Click(object sender, RoutedEventArgs e) => ShowInSchema?.Invoke();

    /// <summary>КонЭ выбранной строки усилий (ПКМ по строке таблицы её выбирает).</summary>
    string? SelectedMemberTag => _vm.SelectedElemTag is int elem ? _vm.ResolveMemberTag(elem) : null;

    void ElementForcesGrid_ContextMenuOpening(object sender, ContextMenuEventArgs e)
    {
        if (sender is not FrameworkElement { ContextMenu: { } menu }) return;
        string? member = SelectedMemberTag;
        foreach (var item in menu.Items.OfType<MenuItem>())
            item.IsEnabled = member != null && (item.Name != "createMemberForceSetMenu" || _vm.CanCreateMemberForceSet(member));
    }

    void SectionGrid_ContextMenuOpening(object sender, ContextMenuEventArgs e)
    {
        if (sender is FrameworkElement { ContextMenu: { } menu } && menu.Items.Count > 0 && menu.Items[0] is MenuItem item)
            item.IsEnabled = _vm.CanRequestSectionState;
    }

    void MemberShow2DCtx_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedMemberTag is { } member) _vm.RequestShowMemberForce(member);
    }

    void MemberSectionCtx_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedMemberTag is { } member) _vm.RequestShowMemberSection(member);
    }

    void MemberCreateForceSetCtx_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedMemberTag is { } member) _vm.RequestCreateMemberForceSet(member);
    }

    void NodeValuesCtx_Click(object sender, RoutedEventArgs e)
    {
        if (_vm.SelectedNodeTag is int node) _vm.RequestShowNodeValues(node.ToString(CultureInfo.InvariantCulture));
    }

    void SectionStateCtx_Click(object sender, RoutedEventArgs e) => _vm.RequestSectionState(_vm.SelectedSectionLocation);

    void DisplacementsGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (e.AddedItems.Count > 0 && e.AddedItems[0] is FemNodeDisplacementRow row)
            _vm.SelectNode(row.NodeTag);
    }

    void DisplacementModeBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (displacementModeBox.SelectedItem is FemDisplacementDisplayMode mode)
            _vm.DisplacementDisplayMode = mode;
    }

    void DisplacementLengthUnitBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (displacementLengthUnitBox.SelectedItem is FemLengthUnit unit)
            _vm.DisplacementLengthUnit = unit;
    }

    void RotationDisplayScaleBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (rotationDisplayScaleBox.SelectedItem is FemRotationScale scale)
            _vm.RotationDisplayScale = scale;
    }

    void UpdateDisplacementTableColumns()
    {
        if (displacementsGrid.Columns.Count < 9) return;
        bool extremes = _vm.DisplacementDisplayMode == FemDisplacementDisplayMode.ExtremesOnly;
        displacementsGrid.Columns[1].Visibility = extremes ? Visibility.Visible : Visibility.Collapsed;
        displacementsGrid.Columns[8].Visibility = extremes ? Visibility.Visible : Visibility.Collapsed;

        string lengthUnit = Loc.S($"FemLength{_vm.DisplacementLengthUnit}");
        string rotationUnit = string.Format(Loc.S("FemRotationDisplayUnit"),
            Loc.S("FemUnitRad"), (int)_vm.RotationDisplayScale);
        displacementsGrid.Columns[2].Header = string.Format(Loc.S("FemResultColumnHeader"), Loc.S("FemResultNodalUx"), lengthUnit);
        displacementsGrid.Columns[3].Header = string.Format(Loc.S("FemResultColumnHeader"), Loc.S("FemResultNodalUy"), lengthUnit);
        displacementsGrid.Columns[4].Header = string.Format(Loc.S("FemResultColumnHeader"), Loc.S("FemResultNodalUz"), lengthUnit);
        displacementsGrid.Columns[5].Header = string.Format(Loc.S("FemResultColumnHeader"), Loc.S("FemResultNodalRx"), rotationUnit);
        displacementsGrid.Columns[6].Header = string.Format(Loc.S("FemResultColumnHeader"), Loc.S("FemResultNodalRy"), rotationUnit);
        displacementsGrid.Columns[7].Header = string.Format(Loc.S("FemResultColumnHeader"), Loc.S("FemResultNodalRz"), rotationUnit);
    }

    void ReactionsGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (e.AddedItems.Count > 0 && e.AddedItems[0] is FemNodeReaction row)
            _vm.SelectNode(row.NodeTag);
    }

    void ElementForcesGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (e.AddedItems.Count > 0 && e.AddedItems[0] is FemElementEndForces row)
            _vm.SelectElement(row.ElemTag);
    }

    void OpenSectionState_Click(object sender, System.Windows.RoutedEventArgs e)
        => _vm.RequestSectionState(_vm.SelectedSectionLocation);

    void SectionGrid_MouseDoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
        => _vm.RequestSectionState(_vm.SelectedSectionLocation);
}
