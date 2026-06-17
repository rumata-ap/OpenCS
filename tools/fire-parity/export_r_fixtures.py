"""Экспорт фикстур паритета R-проверки (Python → JSON для CSfea.Tests)."""
from __future__ import annotations

import json
import sys
from pathlib import Path
from typing import Any

import numpy as np

GREEN_SECTION_PY = Path(r"C:\Users\palex\devel\GreenSection\GreenSectionPy")
FIXTURES_DIR = Path(__file__).resolve().parent / "fixtures"


def _ensure_path() -> None:
    gs = str(GREEN_SECTION_PY)
    if gs not in sys.path:
        sys.path.insert(0, gs)


def _make_project_with_thermal():
    from greensection.commands.base import Context, SimpleOutput
    from greensection.core.entities import (
        ContourDef,
        PolyDef,
        RebarGroupDef,
        RebarPointDef,
        SectionDef,
    )
    from greensection.core.entities.fire import FireBoundaryEdgeDef, FireSectionDef
    from greensection.core.entities.material import ConcreteDef, RebarMaterialDef
    from greensection.core.entities.task import SimpleTaskDef
    from greensection.core.project import Project
    from greensection.tasks.registry import task_registry
    import greensection.tasks.simple.fire_thermal_task  # noqa: F401

    proj = Project(name="r_parity_export")
    proj.add_material(ConcreteDef(
        id="conc1", name="B25",
        params={"Rb": 14500.0, "Rbt": 1050.0, "Eb": 30000000.0},
    ))
    proj.add_material(RebarMaterialDef(
        id="rebar1", name="A500",
        params={"Rs": 435000.0},
    ))
    proj.add_polygon(PolyDef(
        id="poly1", name="rect_outer",
        points=[(0.0, 0.0), (0.4, 0.0), (0.4, 0.4), (0.0, 0.4)],
    ))
    proj.add_contour(ContourDef(
        id="c1", name="concrete", outer_poly_id="poly1",
        material_id="conc1",
    ))
    proj.add_rebar_group(RebarGroupDef(
        id="rg1", name="d16",
        rebars=[
            RebarPointDef(id="r1", x=0.05, y=0.05, diameter=0.016, material_id="rebar1"),
            RebarPointDef(id="r2", x=0.35, y=0.05, diameter=0.016, material_id="rebar1"),
            RebarPointDef(id="r3", x=0.35, y=0.35, diameter=0.016, material_id="rebar1"),
            RebarPointDef(id="r4", x=0.05, y=0.35, diameter=0.016, material_id="rebar1"),
        ],
    ))
    proj.add_section(SectionDef(
        id="sec1", name="rect", contour_ids=["c1"],
        rebar_group_ids=["rg1"],
    ))
    fs = FireSectionDef(
        id="fs1", name="r_parity", section_id="sec1",
        fire_duration_min=5.0,
        time_step_s=10.0,
        snapshot_step_min=5.0,
        mesh_step_m=0.05,
        edges=[
            FireBoundaryEdgeDef(edge_index=0, bc_type="fire",
                                 alpha_conv=25.0, emissivity=0.7),
        ],
    )
    proj.add_fire_section(fs)

    ctx = Context(project=proj, repo=None, output=SimpleOutput())
    thermal_meta = task_registry.get_simple("fire_thermal")
    thermal_data = thermal_meta.handler_cls().run(ctx, SimpleTaskDef(
        id="t1", name="thermal", kind="fire_thermal",
        section_id="fs1", force_ref_id="",
    ))
    return proj, thermal_data


def _rebar_int_id(rebar_id: Any) -> int:
    s = str(rebar_id)
    if s.startswith("r") and s[1:].isdigit():
        return int(s[1:]) - 1
    if s.isdigit():
        return int(s)
    raise ValueError(f"Unsupported rebar_id: {rebar_id!r}")


def _mesh_payload(mesh: Any, rebar_xy: dict[str, tuple[float, float]] | None = None) -> dict[str, Any]:
    nodes = mesh.nodes
    x = [float(n.x) for n in nodes]
    y = [float(n.y) for n in nodes]
    elements = [[int(i), int(j), int(k)] for i, j, k in (el.nodes for el in mesh.elements)]
    rebars = []
    for loc in mesh.rebar_locations:
        rid = _rebar_int_id(loc.rebar_id)
        xy = (rebar_xy or {}).get(str(loc.rebar_id), (None, None))
        rebars.append({
            "id": rid,
            "x": float(xy[0]) if xy[0] is not None else None,
            "y": float(xy[1]) if xy[1] is not None else None,
            "element_index": int(loc.element_index) if loc.element_index is not None else -1,
            "xi1": float(loc.barycentric[0]),
            "xi2": float(loc.barycentric[1]),
            "xi3": float(loc.barycentric[2]),
        })
    return {"x": x, "y": y, "elements": elements, "rebars": rebars}


def _thermal_payload(result: Any, rebar_xy: dict[str, tuple[float, float]] | None = None) -> dict[str, Any]:
    mesh = result.mesh
    hist: dict[str, list[float]] = {}
    for rid, arr in (result.rebar_temperature_history or {}).items():
        key = str(_rebar_int_id(rid))
        hist[key] = [float(v) if v == v else 20.0 for v in np.asarray(arr, dtype=float).tolist()]
    max_t = {}
    for rid, v in (result.rebar_max_temperatures or {}).items():
        key = str(_rebar_int_id(rid))
        max_t[key] = float(v) if v == v else 20.0

    payload = _mesh_payload(mesh, rebar_xy)
    payload["times_min"] = [float(t) for t in np.asarray(result.times_min).tolist()]
    payload["snapshots"] = [
        [float(v) for v in row]
        for row in np.asarray(result.snapshots, dtype=float).tolist()
    ]
    payload["rebar_temperature_history"] = hist
    payload["rebar_max_temperatures"] = max_t
    payload["cold_face_node_ids"] = [int(i) for i in (result.cold_face_node_ids or [])]
    return payload


def _expected_r(details: dict[str, Any], passed: bool, margin: float) -> dict[str, Any]:
    keys = (
        "factor", "utilization", "converged", "governing",
        "gamma_bt_min", "gamma_bt_avg", "gamma_bt_max",
        "gamma_st_c_min", "gamma_st_t_min",
        "n_concrete_elements", "n_rebar_elements",
        "N_limit", "Mx_limit", "My_limit",
        "eps_contour_min", "eps_cu",
    )
    out: dict[str, Any] = {"passed": bool(passed), "margin": float(margin)}
    for k in keys:
        if k in details:
            v = details[k]
            if v is None:
                out[k] = None
            elif isinstance(v, (bool, np.bool_)):
                out[k] = bool(v)
            elif isinstance(v, (int, np.integer)):
                out[k] = int(v)
            else:
                out[k] = float(v)
    return out


def export_rect_400_r_check() -> dict[str, Any]:
    from greensection.core.fire_r_check_fiber import run_fire_r_check_fiber

    proj, thermal_data = _make_project_with_thermal()
    thermal_result_id = thermal_data["fire_thermal_result_id"]
    thermal = proj.load_fire_thermal_result(thermal_result_id)

    rebar_xy = {
        "r1": (0.05, 0.05),
        "r2": (0.35, 0.05),
        "r3": (0.35, 0.35),
        "r4": (0.05, 0.35),
    }

    n, mx, my = -500.0, 30.0, 0.0
    check = run_fire_r_check_fiber(
        project=proj,
        fire_section_id="fs1",
        N=n, Mx=mx, My=my,
        aggregate_type="silicate",
        snapshot_index=-1,
    )

    return {
        "name": "rect_400x400_5min_r_check_fiber",
        "aggregate_type": "silicate",
        "loads": {"N": n, "Mx": mx, "My": 0.0},
        "method": "fiber",
        "snapshot_index": -1,
        "section": {
            "outer": [[0.0, 0.0], [0.4, 0.0], [0.4, 0.4], [0.0, 0.4]],
            "holes": [],
            "rebars": [
                {"id": 0, "x": 0.05, "y": 0.05, "diameter": 0.016},
                {"id": 1, "x": 0.35, "y": 0.05, "diameter": 0.016},
                {"id": 2, "x": 0.35, "y": 0.35, "diameter": 0.016},
                {"id": 3, "x": 0.05, "y": 0.35, "diameter": 0.016},
            ],
        },
        "materials": {
            "concrete": {"fc_mpa": 14.5, "ft_mpa": 1.05, "e_mpa": 30000.0},
            "rebar": {"rs_mpa": 435.0, "e_mpa": 200000.0},
        },
        "fire_section": {
            "fire_duration_min": 5.0,
            "fire_curve": "iso834",
            "mesh_step_m": 0.05,
            "time_step_s": 10.0,
            "snapshot_step_min": 5.0,
            "theta": 1.0,
            "picard_tol_celsius": 0.5,
            "picard_max_iter": 20,
            "bc_preset": "manual",
            "algorithm": "ruppert",
            "smooth_iter_tri": 2,
            "edges": [
                {
                    "original_edge_index": 0,
                    "bc_type": "fire",
                    "alpha_conv": 25.0,
                    "emissivity": 0.7,
                    "fire_curve": "iso834",
                    "t_ambient_celsius": 20.0,
                }
            ],
        },
        "thermal": _thermal_payload(thermal, rebar_xy),
        "expected_fiber": _expected_r(check.details, check.passed, check.margin),
        "python_thermal_result_id": thermal_result_id,
    }


def main() -> int:
    _ensure_path()
    FIXTURES_DIR.mkdir(parents=True, exist_ok=True)
    fixture = export_rect_400_r_check()
    out_path = FIXTURES_DIR / f"{fixture['name']}.json"
    out_path.write_text(json.dumps(fixture, ensure_ascii=False, indent=2), encoding="utf-8")
    print(f"Exported: {out_path}")
    print(f"  factor={fixture['expected_fiber']['factor']:.6f}")
    print(f"  margin={fixture['expected_fiber']['margin']:.6f}")
    print(f"  passed={fixture['expected_fiber']['passed']}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
