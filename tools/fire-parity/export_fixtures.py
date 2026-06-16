from __future__ import annotations

import json
import sys
from dataclasses import dataclass
from pathlib import Path
from typing import Any

import numpy as np


GREEN_SECTION_PY = Path(r"C:\Users\palex\devel\GreenSection\GreenSectionPy")
FIXTURES_DIR = Path(__file__).resolve().parent / "fixtures"


@dataclass(frozen=True)
class ProbeSpec:
    name: str
    x: float
    y: float


@dataclass(frozen=True)
class ScenarioSpec:
    name: str
    section: dict[str, Any]
    fire_duration_min: float
    fire_curve: str
    mesh_step_m: float
    time_step_s: float
    snapshot_step_min: float
    theta: float
    picard_tol_celsius: float
    picard_max_iter: int
    bc_preset: str
    algorithm: str
    smooth_iter_tri: int
    aggregate_type: str
    moisture_fraction: float
    t_init_celsius: float
    probes: list[ProbeSpec]


def _ensure_greensection_path() -> None:
    if not GREEN_SECTION_PY.exists():
        raise FileNotFoundError(
            "GreenSectionPy path does not exist: "
            f"{GREEN_SECTION_PY}. Update GREEN_SECTION_PY in export_fixtures.py."
        )

    gs_path = str(GREEN_SECTION_PY)
    if gs_path not in sys.path:
        sys.path.insert(0, gs_path)


def _import_solver_api() -> tuple[Any, Any, Any]:
    _ensure_greensection_path()
    try:
        from solvers.fire_thermal._fire_thermal_meshing import build_mesh
        from solvers.fire_thermal.fire_thermal import SolverParams, solve_fire_thermal
    except Exception as exc:  # pragma: no cover - CLI diagnostic path
        print(
            "[ERROR] Failed to import GreenSectionPy thermal solver modules.\n"
            f"  GreenSectionPy path: {GREEN_SECTION_PY}\n"
            "  Ensure dependencies are installed and path is correct.\n"
            f"  Details: {exc}",
            file=sys.stderr,
        )
        raise
    return build_mesh, SolverParams, solve_fire_thermal


def _edge_defs_for_3sided_manual() -> list[dict[str, Any]]:
    return [
        {
            "original_edge_index": 0,
            "bc_type": "fire",
            "alpha_conv": 25.0,
            "emissivity": 0.7,
            "fire_curve": "iso834",
            "t_ambient_celsius": 20.0,
        },
        {
            "original_edge_index": 1,
            "bc_type": "fire",
            "alpha_conv": 25.0,
            "emissivity": 0.7,
            "fire_curve": "iso834",
            "t_ambient_celsius": 20.0,
        },
        {
            "original_edge_index": 2,
            "bc_type": "adiabatic",
            "alpha_conv": 0.0,
            "emissivity": 0.0,
            "fire_curve": "iso834",
            "t_ambient_celsius": 20.0,
        },
        {
            "original_edge_index": 3,
            "bc_type": "fire",
            "alpha_conv": 25.0,
            "emissivity": 0.7,
            "fire_curve": "iso834",
            "t_ambient_celsius": 20.0,
        },
    ]


def _apply_manual_3sided_bc(mesh: Any) -> None:
    fire_edges = {0, 1, 3}
    for edge in mesh.boundary_edges:
        if edge.original_edge_index in fire_edges:
            edge.bc_type = "fire"
            edge.alpha_conv = 25.0
            edge.emissivity = 0.7
            edge.fire_curve = "iso834"
            edge.T_ambient_celsius = 20.0
        else:
            edge.bc_type = "adiabatic"
            edge.alpha_conv = 0.0
            edge.emissivity = 0.0
            edge.fire_curve = "iso834"
            edge.T_ambient_celsius = 20.0


def _nearest_node_index(mesh: Any, x: float, y: float) -> int:
    distances = [(n.x - x) ** 2 + (n.y - y) ** 2 for n in mesh.nodes]
    return int(np.argmin(distances))


def _float_list(values: np.ndarray) -> list[float]:
    return [float(v) for v in values.tolist()]


def _build_fixture_dict(
    scenario: ScenarioSpec,
    mesh: Any,
    result: Any,
) -> dict[str, Any]:
    probes_payload: list[dict[str, Any]] = []
    for probe in scenario.probes:
        node_idx = _nearest_node_index(mesh, probe.x, probe.y)
        probe_t = result.snapshots[:, node_idx]
        probes_payload.append(
            {
                "name": probe.name,
                "x": probe.x,
                "y": probe.y,
                "nearest_node_index": node_idx,
                "snapshots_c": _float_list(probe_t),
            }
        )

    return {
        "name": scenario.name,
        "section": scenario.section,
        "fire_section": {
            "fire_duration_min": scenario.fire_duration_min,
            "fire_curve": scenario.fire_curve,
            "mesh_step_m": scenario.mesh_step_m,
            "time_step_s": scenario.time_step_s,
            "snapshot_step_min": scenario.snapshot_step_min,
            "theta": scenario.theta,
            "picard_tol_celsius": scenario.picard_tol_celsius,
            "picard_max_iter": scenario.picard_max_iter,
            "bc_preset": scenario.bc_preset,
            "algorithm": scenario.algorithm,
            "smooth_iter_tri": scenario.smooth_iter_tri,
            "edges": _edge_defs_for_3sided_manual(),
        },
        "aggregate_type": scenario.aggregate_type,
        "probes": probes_payload,
        "mesh_stats": {
            "n_nodes": int(mesh.n_nodes),
            "n_elements": int(mesh.n_elements),
        },
        "snapshots_summary": {
            "times_min": _float_list(result.times_min),
            "max_t_c": float(np.max(result.snapshots)),
        },
    }


def _scenario_specs() -> list[ScenarioSpec]:
    base_section = {
        "outer": [[0.0, 0.0], [0.2, 0.0], [0.2, 0.4], [0.0, 0.4]],
        "holes": [],
        "rebars": [],
    }
    return [
        ScenarioSpec(
            name="beam_200x400_R60_3sided",
            section=base_section,
            fire_duration_min=60.0,
            fire_curve="iso834",
            mesh_step_m=0.01,
            time_step_s=10.0,
            snapshot_step_min=10.0,
            theta=1.0,
            picard_tol_celsius=0.5,
            picard_max_iter=10,
            bc_preset="manual",
            algorithm="ruppert",
            smooth_iter_tri=2,
            aggregate_type="silicate",
            moisture_fraction=0.025,
            t_init_celsius=20.0,
            probes=[ProbeSpec(name="center_30mm", x=0.1, y=0.03)],
        ),
        ScenarioSpec(
            name="rectangle_200x400_5min_3sided",
            section=base_section,
            fire_duration_min=5.0,
            fire_curve="iso834",
            mesh_step_m=0.05,
            time_step_s=15.0,
            snapshot_step_min=1.0,
            theta=1.0,
            picard_tol_celsius=0.5,
            picard_max_iter=20,
            bc_preset="3-sided",
            algorithm="ruppert",
            smooth_iter_tri=2,
            aggregate_type="silicate",
            moisture_fraction=0.025,
            t_init_celsius=20.0,
            probes=[ProbeSpec(name="bottom_center", x=0.1, y=0.0)],
        ),
    ]


def _run_scenario(
    scenario: ScenarioSpec,
    build_mesh: Any,
    SolverParams: Any,
    solve_fire_thermal: Any,
) -> dict[str, Any]:
    mesh = build_mesh(
        scenario.section,
        mesh_step_m=scenario.mesh_step_m,
        algorithm=scenario.algorithm,
        smooth_iter_tri=scenario.smooth_iter_tri,
    )
    _apply_manual_3sided_bc(mesh)
    for el in mesh.elements:
        el.material_id = "B25"

    params = SolverParams(
        duration_min=scenario.fire_duration_min,
        time_step_s=scenario.time_step_s,
        snapshot_step_min=scenario.snapshot_step_min,
        theta=scenario.theta,
        picard_max_iter=scenario.picard_max_iter,
        picard_tol_celsius=scenario.picard_tol_celsius,
        aggregate_type=scenario.aggregate_type,
        moisture_fraction=scenario.moisture_fraction,
        T_init_celsius=scenario.t_init_celsius,
        fire_curve=scenario.fire_curve,
    )
    result = solve_fire_thermal(mesh, params)
    return _build_fixture_dict(scenario, mesh, result)


def main() -> int:
    try:
        build_mesh, SolverParams, solve_fire_thermal = _import_solver_api()
    except Exception:
        return 1

    FIXTURES_DIR.mkdir(parents=True, exist_ok=True)
    specs = _scenario_specs()
    written_paths: list[Path] = []

    for scenario in specs:
        fixture = _run_scenario(scenario, build_mesh, SolverParams, solve_fire_thermal)
        fixture_path = FIXTURES_DIR / f"{scenario.name}.json"
        fixture_path.write_text(
            json.dumps(fixture, ensure_ascii=False, indent=2),
            encoding="utf-8",
        )
        written_paths.append(fixture_path)

    print("Exported fixtures:")
    for p in written_paths:
        print(f" - {p}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
