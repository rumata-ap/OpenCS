# Redesign Load Assignment Toolbar Icons Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Redesign the crude single-stroke node load and member load icons on the toolbar of `FemSchemaView3D.xaml` with professional dual-tone vector graphics.

**Architecture:** Replace the single `<Path>` children of `NodeLoadTool_Click` and `MemberLoadTool_Click` buttons in `FemSchemaView3D.xaml` with a clean 24x24 `Viewbox` containing multi-path vector geometry for the structural node/beam base (`#2C3E50`) and load vectors (`#E74C3C` for nodal force $F$, `#1976D2` for member distributed load $q$).

**Tech Stack:** .NET 9.0, WPF XAML (Viewbox, Canvas, Path).

## Global Constraints

- Modify XAML elements in `OpenCS/Views/FemSchemaView3D.xaml` without breaking event handlers (`NodeLoadTool_Click`, `MemberLoadTool_Click`).
- All localizable text relies on existing resource keys `{DynamicResource FemNodeLoadToolTip}` and `{DynamicResource FemMemberLoadToolTip}`.
- Solution build command: `dotnet build OpenCS.sln`.

---

### Task 1: Redesign Toolbar Icons in `FemSchemaView3D.xaml`

**Files:**
- Modify: `OpenCS/Views/FemSchemaView3D.xaml:53-62`

**Interfaces:**
- Consumes: `NodeLoadTool_Click`, `MemberLoadTool_Click` event handlers, `{DynamicResource FemNodeLoadToolTip}`, `{DynamicResource FemMemberLoadToolTip}`.
- Produces: Updated XAML layout for load toolbar buttons.

- [ ] **Step 1: Replace raw stroke paths with crisp dual-tone vector icons in `FemSchemaView3D.xaml`**

In `OpenCS/Views/FemSchemaView3D.xaml`, replace lines 53-62:

```xml
            <Button Style="{StaticResource IconButton25}" Click="NodeLoadTool_Click"
                    ToolTip="{DynamicResource FemNodeLoadToolTip}">
                <Path Data="M12,2 L12,16 M12,2 L8,6 M12,2 L16,6 M5,20 H19"
                      Stroke="Crimson" StrokeThickness="2" Stretch="Uniform" Margin="2"/>
            </Button>
            <Button Style="{StaticResource IconButton25}" Click="MemberLoadTool_Click"
                    ToolTip="{DynamicResource FemMemberLoadToolTip}">
                <Path Data="M3,12 H21 M7,12 V5 M12,12 V3 M17,12 V7 M5,5 L7,7 M10,3 L12,5 M15,7 L17,9"
                      Stroke="DarkGreen" StrokeThickness="2" Stretch="Uniform" Margin="2"/>
            </Button>
```

with:

```xml
            <Button Style="{StaticResource IconButton25}" Click="NodeLoadTool_Click"
                    ToolTip="{DynamicResource FemNodeLoadToolTip}">
                <Viewbox Width="20" Height="20">
                    <Canvas Width="24" Height="24">
                        <Path Data="M6,21.5 H18" Stroke="#2C3E50" StrokeThickness="1.5" StrokeLineCap="Round"/>
                        <Path Data="M12,18.5 m-3,0 a3,3 0 1,0 6,0 a3,3 0 1,0 -6,0" Fill="#2C3E50"/>
                        <Path Data="M12,2 V10" Stroke="#E74C3C" StrokeThickness="2.5" StrokeLineCap="Round"/>
                        <Path Data="M8,9 L12,15 L16,9 Z" Fill="#E74C3C"/>
                    </Canvas>
                </Viewbox>
            </Button>
            <Button Style="{StaticResource IconButton25}" Click="MemberLoadTool_Click"
                    ToolTip="{DynamicResource FemMemberLoadToolTip}">
                <Viewbox Width="20" Height="20">
                    <Canvas Width="24" Height="24">
                        <Path Data="M3,19 H21" Stroke="#2C3E50" StrokeThickness="2.5" StrokeLineCap="Round"/>
                        <Path Data="M4,19 m-2,0 a2,2 0 1,0 4,0 a2,2 0 1,0 -4,0" Fill="#2C3E50"/>
                        <Path Data="M20,19 m-2,0 a2,2 0 1,0 4,0 a2,2 0 1,0 -4,0" Fill="#2C3E50"/>
                        <Path Data="M5,5 H19" Stroke="#1976D2" StrokeThickness="1.5" StrokeLineCap="Round"/>
                        <Path Data="M6,5 V13 M12,5 V13 M18,5 V13" Stroke="#1976D2" StrokeThickness="1.5"/>
                        <Path Data="M4.5,11 L6,15.5 L7.5,11 Z M10.5,11 L12,15.5 L13.5,11 Z M16.5,11 L18,15.5 L19.5,11 Z" Fill="#1976D2"/>
                    </Canvas>
                </Viewbox>
            </Button>
```

- [ ] **Step 2: Build solution to verify zero compilation errors**

Run: `dotnet build OpenCS.sln`
Expected: Build succeeded with 0 errors.

- [ ] **Step 3: Commit changes**

```bash
git add OpenCS/Views/FemSchemaView3D.xaml
git commit -m "style(ui): redesign load assignment toolbar icons with dual-tone vector geometry"
```
