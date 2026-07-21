# FEM Result View Toolbar Redesign Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Redesign the toolbar in `FemAnalysisResultView.xaml` to display scale values rounded to 2 decimal places, use standard WPF `<ToolBar>` with separators, and replace checkboxes for nodes and member selection mode with sticky `ToggleButton`s and icons matching `FemSchemaView3D`.

**Architecture:** Update XAML layout and resources in `FemAnalysisResultView.xaml` and handle `ToggleButton` events in `FemAnalysisResultView.xaml.cs`.

**Tech Stack:** WPF, C# 13, .NET 9.0

## Global Constraints
- Target Framework: `net9.0-windows`
- UI strings use `DynamicResource` localization keys
- Maintain full buildability of `OpenCS.sln`

---

### Task 1: Update XAML Resources and ToolBar Layout in FemAnalysisResultView

**Files:**
- Modify: `OpenCS/Views/FemAnalysisResultView.xaml:9-62`

**Interfaces:**
- Consumes: `IconToggleButton25`, `IconButton25` WPF styles and `Fem3DShowNodes`, `FemResultHighlightWholeMember`, `FemResultDeformScale`, `FemResultForceScale` dynamic resources.
- Produces: Updated XAML layout with WPF `<ToolBar>`, 2-decimal formatted text boxes, and sticky toggle buttons.

- [ ] **Step 1: Edit `FemAnalysisResultView.xaml`**

Add `IconToggleButton25` and `IconButton25` styles to `UserControl.Resources` and replace `StackPanel` with `<ToolBar>` containing `DeformScale` (with `StringFormat=F2`), reset button with SVG icon, `showNodesCheck` `ToggleButton` (with node SVG icon), `highlightWholeMemberCheck` `ToggleButton` (with member selection SVG icon), `ForceComponent` combo, `ForceScale` (with `StringFormat=F2`), and force reset button.

- [ ] **Step 2: Verify XAML syntax and build**

Run: `dotnet build OpenCS.sln`
Expected: PASS

- [ ] **Step 3: Commit XAML changes**

```bash
git add OpenCS/Views/FemAnalysisResultView.xaml
git commit -m "ui: redesign FEM result view toolbar in XAML"
```

---

### Task 2: Update Code-Behind Event Handlers in FemAnalysisResultView.xaml.cs

**Files:**
- Modify: `OpenCS/Views/FemAnalysisResultView.xaml.cs:103-115`

**Interfaces:**
- Consumes: `NodesToggle` and `HighlightWholeMemberToggle` events from `ToggleButton`s in `FemAnalysisResultView.xaml`.
- Produces: Updated C# handlers accepting `ToggleButton` as sender.

- [ ] **Step 1: Update event handlers in `FemAnalysisResultView.xaml.cs`**

Modify `NodesToggle` and `HighlightWholeMemberToggle` to check `sender is ToggleButton tb` instead of `CheckBox cb`.

- [ ] **Step 2: Build solution and verify clean compilation**

Run: `dotnet build OpenCS.sln`
Expected: PASS with 0 errors.

- [ ] **Step 3: Commit code-behind changes**

```bash
git add OpenCS/Views/FemAnalysisResultView.xaml.cs
git commit -m "ui: update event handlers in FemAnalysisResultView for ToggleButton controls"
```
