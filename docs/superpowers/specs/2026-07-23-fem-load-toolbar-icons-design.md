# Spec: Redesign Load Assignment Toolbar Icons in FEA Editor (`FemSchemaView3D`)

## Overview
This specification outlines the visual redesign of the load assignment icons on the toolbar of the 3D FEA structural model editor (`FemSchemaView3D.xaml`). The existing icons used raw stroke paths without solid arrowheads or node geometry, appearing crude and visually unappealing.

The redesign introduces modern, two-tone vector graphics with crisp vector geometry, clear structural node/beam context, and distinct accent colors for nodal and member distributed loads.

## Objectives
1. **Visual Excellence**: Replace crude single-stroke line drawings with professional dual-tone vector graphics.
2. **Instant Recognition**: Distinctly separate nodal force ($F$) and member distributed load ($q$) using color and structural geometry.
3. **High Resolution Scale**: Render inside a clean 24x24 viewBox placed in standard WPF `Viewbox` controls for sharp rendering across all DPI scalings.

## Design Details

### 1. Nodal Load Icon (`FemNodeLoadToolTip`)
- **Target Button**: Node Load assignment button (`NodeLoadTool_Click`).
- **Base Element (Structural Node)**:
  - Color: `#2C3E50` (Dark Slate)
  - Horizontal ground support line: $x \in [6, 18]$, $y = 21.5$, stroke thickness 1.5px with round caps.
  - Node dot: Filled circle at center $(12, 18.5)$ with radius $3\text{px}$.
- **Force Vector ($F$)**:
  - Color: `#E74C3C` (Crimson Accent)
  - Force shaft: Vertical line $x = 12$, $y \in [2, 10]$, stroke thickness 2.5px with round top cap.
  - Arrowhead: Solid filled downward-pointing triangle from $y = 9$ to $y = 15$ pointing directly at the node center.

### 2. Member Load Icon (`FemMemberLoadToolTip`)
- **Target Button**: Member Load assignment button (`MemberLoadTool_Click`).
- **Base Element (Beam Member)**:
  - Color: `#2C3E50` (Dark Slate)
  - Beam line: Horizontal bar $x \in [3, 21]$, $y = 19$, stroke thickness 2.5px with round caps.
  - End nodes: Two filled circles with radius $2\text{px}$ at $(4, 19)$ and $(20, 19)$.
- **Distributed Load ($q$)**:
  - Color: `#1976D2` (Cobalt/Royal Blue Accent)
  - Top load distribution bar: Horizontal line $x \in [5, 19]$, $y = 5$, stroke thickness 1.5px.
  - Arrow shafts: 3 parallel vertical lines at $x = 6, 12, 18$, $y \in [5, 13]$, stroke thickness 1.5px.
  - Arrowheads: 3 solid filled downward-pointing triangles from $y = 11$ to $y = 15.5$ touching the top surface of the beam.

## Affected Files
- `OpenCS/Views/FemSchemaView3D.xaml`: Update the `<Button>` content for `NodeLoadTool_Click` and `MemberLoadTool_Click`.

## Verification
- Build solution with `dotnet build OpenCS.sln`.
- Launch `dotnet run --project OpenCS` to visually verify the toolbar buttons in `FemSchemaView3D`.
