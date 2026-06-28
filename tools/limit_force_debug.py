#!/usr/bin/env python3
"""Диагностика задачи предельного нагружения из SQLite БД OpenCS."""

from __future__ import annotations

import json
import sqlite3
import subprocess
import sys
from pathlib import Path

REPO = Path(__file__).resolve().parents[1]
DEFAULT_DB = Path(r"C:\Users\palex\Downloads\test_prj.db")
DEBUG_PROJECT = REPO / "tools" / "LimitForceDebug" / "LimitForceDebug.csproj"


def connect(db_path: Path) -> sqlite3.Connection:
    conn = sqlite3.connect(str(db_path))
    conn.row_factory = sqlite3.Row
    return conn


def fetch_task(conn: sqlite3.Connection, task_num: int) -> dict | None:
    row = conn.execute(
        """
        SELECT id, num, tag, kind, section_id, force_set_id, force_item_id,
               calc_type, params_json
        FROM calc_tasks WHERE num = ?
        """,
        (task_num,),
    ).fetchone()
    return dict(row) if row else None


def fetch_latest_result(conn: sqlite3.Connection, task_id: int) -> dict | None:
    row = conn.execute(
        """
        SELECT id, status, data_json, created
        FROM calc_results WHERE task_id = ?
        ORDER BY id DESC LIMIT 1
        """,
        (task_id,),
    ).fetchone()
    if not row:
        return None
    data = json.loads(row["data_json"] or "{}")
    return {
        "id": row["id"],
        "status": row["status"],
        "created": row["created"],
        "data": data,
    }


def fetch_section_summary(conn: sqlite3.Connection, section_id: int) -> dict:
    sec = conn.execute(
        "SELECT id, num, tag, description, type FROM cross_sections WHERE id = ?",
        (section_id,),
    ).fetchone()
    areas = conn.execute(
        """
        SELECT ma.id, ma.tag, ma.category, ma.nx, ma.ny, ma.diagramm_type,
               ma.material_id, m.tag AS mat_tag, m.type AS mat_type
        FROM cross_section_areas csa
        JOIN material_areas ma ON ma.id = csa.area_id
        LEFT JOIN materials m ON m.id = ma.material_id
        WHERE csa.section_id = ?
        ORDER BY csa.sort_order
        """,
        (section_id,),
    ).fetchall()

    area_ids = [a["id"] for a in areas]
    point_fibers: dict[int, int] = {}
    mesh_fibers: dict[int, int] = {}
    if area_ids:
        ph = ",".join("?" * len(area_ids))
        for row in conn.execute(
            f"SELECT area_id, COUNT(*) AS c FROM point_fibers WHERE area_id IN ({ph}) GROUP BY area_id",
            area_ids,
        ):
            point_fibers[row["area_id"]] = row["c"]
        for row in conn.execute(
            f"SELECT area_id, COUNT(*) AS c FROM mesh_fibers WHERE area_id IN ({ph}) GROUP BY area_id",
            area_ids,
        ):
            mesh_fibers[row["area_id"]] = row["c"]

    return {
        "section": dict(sec) if sec else None,
        "areas": [
            {
                **dict(a),
                "point_fibers": point_fibers.get(a["id"], 0),
                "mesh_fibers": mesh_fibers.get(a["id"], 0),
            }
            for a in areas
        ],
    }


def detect_fallback(data: dict) -> dict:
    """Эвристика: fast-решатель ушёл во внешний BisectFallback (не внутренний bracket)."""
    solver = data.get("solver_method", "")
    iters = int(data.get("iterations") or 0)
    newton = int(data.get("newton_iterations") or 0)
    # Внешний BisectFallback: iterations = шаги бисекции (часто 6..60),
    # newton >> iterations, и iterations обычно не совпадает с числом шагов Ньютона fast-пути.
    # Fast-путь успешный: iterations = nIter Ньютона (мало), newton = innerIters + nIter (innerIters ~80).
    likely_outer_bisect = (
        solver == "fast"
        and iters >= 10
        and newton > iters * 3
    )
    likely_inner_bracket = (
        solver == "fast"
        and 1 <= iters <= 15
        and newton > 50
        and not likely_outer_bisect
    )
    return {
        "requested_solver": solver,
        "iterations": iters,
        "newton_iterations": newton,
        "likely_outer_bisect_fallback": likely_outer_bisect,
        "likely_inner_bracket_in_fast_solver": likely_inner_bracket,
        "note": (
            "solver_method — ЗАПРОШЕННЫЙ решатель из params_json, не фактический путь. "
            "При likely_inner_bracket внешняя бисекция не вызывается — "
            "медленный этап это TryBracketK внутри TryEstimateCompressionStart."
            if likely_inner_bracket
            else ""
        ),
    }


def run_csharp_debug(db_path: Path) -> int:
    if not DEBUG_PROJECT.exists():
        print(f"C# debug project not found: {DEBUG_PROJECT}", file=sys.stderr)
        return 1
    import os
    env = os.environ.copy()
    env["LIMIT_FORCE_DB"] = str(db_path)
    subprocess.run(
        ["dotnet", "build", str(DEBUG_PROJECT)],
        cwd=str(REPO),
        check=True,
    )
    return subprocess.run(
        ["dotnet", "run", "--project", str(DEBUG_PROJECT), "--no-build"],
        cwd=str(REPO),
        env=env,
    ).returncode


def main() -> int:
    db_path = Path(sys.argv[1]) if len(sys.argv) > 1 else DEFAULT_DB
    task_num = int(sys.argv[2]) if len(sys.argv) > 2 else 3
    run_solver = "--run-solver" in sys.argv

    if not db_path.exists():
        print(f"DB not found: {db_path}", file=sys.stderr)
        return 1

    print(f"DB: {db_path}")
    print(f"Task num: {task_num}\n")

    with connect(db_path) as conn:
        task = fetch_task(conn, task_num)
        if not task:
            print(f"Task {task_num} not found")
            return 1

        params = json.loads(task["params_json"] or "{}")
        print("=== Задача ===")
        print(json.dumps({**task, "params": params}, ensure_ascii=False, indent=2))

        result = fetch_latest_result(conn, task["id"])
        print("\n=== Последний результат ===")
        if result:
            fb = detect_fallback(result["data"])
            print(f"status: {result['status']}, created: {result['created']}")
            print(json.dumps(result["data"], ensure_ascii=False, indent=2))
            print("\n=== Анализ fallback ===")
            print(json.dumps(fb, ensure_ascii=False, indent=2))
        else:
            print("(нет calc_results)")

        sec = fetch_section_summary(conn, task["section_id"])
        print("\n=== Сечение ===")
        print(json.dumps(sec, ensure_ascii=False, indent=2))

        # Усилия для limit_moment: N из params, Mx/My из params или force_item
        n = params.get("N")
        mx = params.get("Mx")
        my = params.get("My")
        if n is None and task["force_item_id"]:
            fi = conn.execute(
                "SELECT n, mx, my, label FROM force_items WHERE id = ?",
                (task["force_item_id"],),
            ).fetchone()
            if fi:
                n, mx, my = fi["n"], fi["mx"], fi["my"]
                print(f"\nУсилия из force_items: N={n}, Mx={mx}, My={my} ({fi['label']})")
        else:
            print(f"\nУсилия из params_json: N={n}, Mx={mx}, My={my}")

    if run_solver:
        print("\n=== Запуск C# диагностики (LimitForceSolverFast.DebugTrace) ===")
        return run_csharp_debug(db_path)

    print("\nДля трассировки быстрого решателя: python tools/limit_force_debug.py [db] [task_num] --run-solver")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
