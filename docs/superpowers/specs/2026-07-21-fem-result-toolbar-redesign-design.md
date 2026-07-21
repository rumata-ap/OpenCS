# Design Spec: FEM Analysis Result View Toolbar Redesign

Date: 2026-07-21
Status: Approved

## Goal
Improve UX and visual consistency of the toolbar in `FemAnalysisResultView` (OpenSees analysis results tab) by:
1. Formatting deformation (`DeformScale`) and force diagram (`ForceScale`) scales to 2 decimal places (`F2`).
2. Replacing the custom `StackPanel` with a clean `<ToolBar>` control matching the layout and styling of `FemSchemaView3D`.
3. Replacing the "Show Nodes" checkbox with a sticky `ToggleButton` using the exact node icon from `FemSchemaView3D`.
4. Replacing the "Highlight Whole Member" checkbox with a sticky `ToggleButton` with a clear SVG icon for member vs element selection.

## Design Details

### 1. Toolbar & Scale Inputs
- Use `<ToolBar>` with `<Separator/>` logical dividers.
- Format `DeformScale` and `ForceScale` textboxes with `StringFormat=F2` and `UpdateSourceTrigger=LostFocus` / `PropertyChanged` with proper converter handling.
- Use `IconButton25` style for scale reset buttons with vector SVG icons.

### 2. Sticky Toggle Buttons
- **Show Nodes (`showNodesCheck`)**: `ToggleButton` with `IconToggleButton25` style using SVG path:
  `M12,4A2.5,2.5 0 0,1 14.5,6.5A2.5,2.5 0 0,1 12,9A2.5,2.5 0 0,1 9.5,6.5A2.5,2.5 0 0,1 12,4M5.5,14A2.5,2.5 0 0,1 8,16.5A2.5,2.5 0 0,1 5.5,19A2.5,2.5 0 0,1 3,16.5A2.5,2.5 0 0,1 5.5,14M18.5,14A2.5,2.5 0 0,1 21,16.5A2.5,2.5 0 0,1 18.5,19A2.5,2.5 0 0,1 16,16.5A2.5,2.5 0 0,1 18.5,14Z`
- **Highlight Whole Member (`highlightWholeMemberCheck`)**: `ToggleButton` with `IconToggleButton25` style using an intuitive structural member icon path and tooltip.

### 3. Code-Behind Updates
- Update `NodesToggle` and `HighlightWholeMemberToggle` in `FemAnalysisResultView.xaml.cs` to accept `ToggleButton` as `sender`.
