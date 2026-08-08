import csv
import math
from pathlib import Path

import matplotlib.pyplot as plt


CSV_PATH = Path(__file__).with_name("example47-moment-curvature-three.csv")
DETAIL_CSV_PATH = Path(__file__).with_name("example47-moment-curvature-crack-loop.csv")
OVERLAY_PATH = Path(__file__).with_name("example47-moment-curvature-three.png")
PANELS_PATH = Path(__file__).with_name("example47-moment-curvature-three-panels.png")
DETAIL_PLOT_PATH = Path(__file__).with_name("example47-moment-curvature-crack-loop.png")
DETAIL_STRAIN_MOMENT_PATH = Path(__file__).with_name("example47-crack-loop-steel-strain-moment.png")
STRAIN_FORCE_PATH = Path(__file__).with_name("example47-steel-strain-force.png")
STRAIN_MOMENT_PATH = Path(__file__).with_name("example47-steel-strain-moment.png")
MIN_OVERLAY_PATH = Path(__file__).with_name("example47-moment-curvature-min-psi.png")
MIN_PANELS_PATH = Path(__file__).with_name("example47-moment-curvature-min-psi-panels.png")
MIN_STRAIN_FORCE_PATH = Path(__file__).with_name("example47-steel-strain-force-min-psi.png")
MIN_STRAIN_MOMENT_PATH = Path(__file__).with_name("example47-steel-strain-moment-min-psi.png")

CURVES = {
    "without_psi": ("Без ψs", "#1f77b4", "-"),
    "psi_8232_no_concrete_tension": ("Без растяжения бетона + ψs по 8.2.32", "#d62728", "-"),
    "psi_8232_concrete_tension": ("Растяжение бетона + ψs по 8.2.32", "#2ca02c", "--"),
}

MIN_CURVES = {
    "without_psi": ("Без ψs", "#1f77b4", "-"),
    "psi_8232_min_no_concrete_tension": ("Без растяжения бетона + ψmin", "#ff7f0e", "-"),
    "psi_8232_min_concrete_tension": ("Растяжение бетона + ψmin", "#9467bd", "--"),
}

ALL_CURVES = {**CURVES, **MIN_CURVES}

DETAIL_CURVES = {
    "without_psi": CURVES["without_psi"],
    "psi_8232_no_concrete_tension": CURVES["psi_8232_no_concrete_tension"],
    "psi_8232_concrete_tension": CURVES["psi_8232_concrete_tension"],
}


def read_curves(path=CSV_PATH):
    curves = {name: [] for name in ALL_CURVES}
    with path.open(newline="", encoding="utf-8") as stream:
        for row in csv.DictReader(stream, delimiter=";"):
            if row["curve"] not in curves or row["state"] == "no_equilibrium":
                continue
            curvature = float(row["kappa_abs_1_per_m"])
            moment = float(row["Mx_abs_kNm"])
            strain = float(row["eps_s_max"])
            force = float(row["Fs_kN"])
            concrete_strain = float(row["eps_b_min_contour"])
            if (math.isfinite(curvature) and math.isfinite(moment)
                    and math.isfinite(force) and math.isfinite(concrete_strain)):
                curves[row["curve"]].append((curvature, moment, strain, force, concrete_strain))
    return curves


def first_peak(points):
    for index in range(1, len(points) - 1):
        previous = points[index - 1][1]
        current = points[index][1]
        following = points[index + 1][1]
        if current >= previous and current > following:
            return current
    raise RuntimeError("Не найден первый пик кривой с растянутым бетоном")


def format_axes(ax, cracking_moment):
    ax.set_xlabel("|κy|, 1/м")
    ax.set_ylabel("|Mx|, кН·м")
    ax.grid(True, alpha=0.25)
    ax.axhline(
        cracking_moment,
        color="black",
        linewidth=0.9,
        linestyle=":",
        label=f"Mcrc = {cracking_moment:.2f} кН·м",
    )


def draw_curve(ax, name, points):
    label, color, style = ALL_CURVES[name]
    x = [point[0] for point in points]
    y = [point[1] for point in points]
    ax.plot(x, y, color=color, linestyle=style, linewidth=2.0, label=label)
    yield_points = [point for point in points if point[2] >= 0.002]
    if yield_points:
        x_yield, y_yield, _, _, _ = yield_points[0]
        ax.scatter([x_yield], [y_yield], color=color, s=34, zorder=4)


def draw_response(ax, name, points, response_index):
    label, color, style = ALL_CURVES[name]
    x = [point[2] * 1000.0 for point in points]
    y = [point[response_index] for point in points]
    ax.plot(x, y, color=color, linestyle=style, linewidth=2.0, label=label)
    yield_points = [point for point in points if point[2] >= 0.002]
    if yield_points:
        point = yield_points[0]
        ax.scatter([point[2] * 1000.0], [point[response_index]], color=color, s=34, zorder=4)


def format_response_axes(ax, ylabel):
    ax.set_xlabel("εs,max, ‰")
    ax.set_ylabel(ylabel)
    ax.grid(True, alpha=0.25)


def main():
    curves = read_curves()
    detail_curves = read_curves(DETAIL_CSV_PATH)
    cracking_moment = first_peak(curves["without_psi"])

    fig, ax = plt.subplots(figsize=(11, 7), dpi=150)
    for name in CURVES:
        points = curves[name]
        draw_curve(ax, name, points)
    format_axes(ax, cracking_moment)
    ax.set_title("Пример 47: диаграмма кривизна–момент, N = 0")
    ax.legend()
    fig.tight_layout()
    fig.savefig(OVERLAY_PATH, bbox_inches="tight")
    plt.close(fig)

    fig, axes = plt.subplots(3, 1, figsize=(11, 12), dpi=150, sharex=True, sharey=True)
    for axis, name in zip(axes, CURVES):
        points = curves[name]
        draw_curve(axis, name, points)
        format_axes(axis, cracking_moment)
        axis.legend(loc="best")
    fig.suptitle("Пример 47: три варианта диаграммы κ–M", y=0.995)
    fig.tight_layout()
    fig.savefig(PANELS_PATH, bbox_inches="tight")
    plt.close(fig)

    fig, ax = plt.subplots(figsize=(11, 7), dpi=150)
    for name in CURVES:
        points = curves[name]
        draw_response(ax, name, points, response_index=3)
    format_response_axes(ax, "Fs, кН")
    ax.set_title("Пример 47: деформация арматуры–усилие в арматуре")
    ax.legend()
    fig.tight_layout()
    fig.savefig(STRAIN_FORCE_PATH, bbox_inches="tight")
    plt.close(fig)

    fig, ax = plt.subplots(figsize=(11, 7), dpi=150)
    for name in CURVES:
        points = curves[name]
        draw_response(ax, name, points, response_index=1)
    format_response_axes(ax, "|Mx|, кН·м")
    ax.axhline(
        cracking_moment,
        color="black",
        linewidth=0.9,
        linestyle=":",
        label=f"Mcrc = {cracking_moment:.2f} кН·м",
    )
    ax.set_title("Пример 47: деформация арматуры–момент")
    ax.legend()
    fig.tight_layout()
    fig.savefig(STRAIN_MOMENT_PATH, bbox_inches="tight")
    plt.close(fig)

    fig, ax = plt.subplots(figsize=(11, 7), dpi=150)
    for name in MIN_CURVES:
        draw_curve(ax, name, curves[name])
    format_axes(ax, cracking_moment)
    ax.set_title("Пример 47: κ–M с явным скачком ψmin = 1/1,8")
    ax.legend()
    fig.tight_layout()
    fig.savefig(MIN_OVERLAY_PATH, bbox_inches="tight")
    plt.close(fig)

    fig, axes = plt.subplots(3, 1, figsize=(11, 12), dpi=150, sharex=True, sharey=True)
    for axis, name in zip(axes, MIN_CURVES):
        draw_curve(axis, name, curves[name])
        format_axes(axis, cracking_moment)
        axis.legend(loc="best")
    fig.suptitle("Пример 47: варианты с явным скачком ψmin", y=0.995)
    fig.tight_layout()
    fig.savefig(MIN_PANELS_PATH, bbox_inches="tight")
    plt.close(fig)

    fig, ax = plt.subplots(figsize=(11, 7), dpi=150)
    for name in MIN_CURVES:
        draw_response(ax, name, curves[name], response_index=3)
    format_response_axes(ax, "Fs, кН")
    ax.set_title("Пример 47: εs–Fs с явным скачком ψmin = 1/1,8")
    ax.legend()
    fig.tight_layout()
    fig.savefig(MIN_STRAIN_FORCE_PATH, bbox_inches="tight")
    plt.close(fig)

    fig, ax = plt.subplots(figsize=(11, 7), dpi=150)
    for name in MIN_CURVES:
        draw_response(ax, name, curves[name], response_index=1)
    format_response_axes(ax, "|Mx|, кН·м")
    ax.axhline(
        cracking_moment,
        color="black",
        linewidth=0.9,
        linestyle=":",
        label=f"Mcrc = {cracking_moment:.2f} кН·м",
    )
    ax.set_title("Пример 47: εs–M с явным скачком ψmin = 1/1,8")
    ax.legend()
    fig.tight_layout()
    fig.savefig(MIN_STRAIN_MOMENT_PATH, bbox_inches="tight")
    plt.close(fig)

    detail_points = [point for name in DETAIL_CURVES for point in detail_curves[name]]
    fig, ax = plt.subplots(figsize=(11, 7), dpi=150)
    for name in DETAIL_CURVES:
        draw_curve(ax, name, detail_curves[name])
    format_axes(ax, cracking_moment)
    ax.set_xlim(
        min(point[0] for point in detail_points),
        max(point[0] for point in detail_points),
    )
    ax.set_ylim(
        min(point[1] for point in detail_points) * 0.95,
        max(point[1] for point in detail_points) * 1.05,
    )
    ax.set_title("Пример 47: детализация петли сразу после Mcrc")
    ax.legend()
    fig.tight_layout()
    fig.savefig(DETAIL_PLOT_PATH, bbox_inches="tight")
    plt.close(fig)

    fig, ax = plt.subplots(figsize=(11, 7), dpi=150)
    for name in DETAIL_CURVES:
        draw_response(ax, name, detail_curves[name], response_index=1)
    format_response_axes(ax, "|Mx|, кН·м")
    ax.set_xlim(
        min(point[2] * 1000.0 for point in detail_points),
        max(point[2] * 1000.0 for point in detail_points),
    )
    ax.set_ylim(
        min(point[1] for point in detail_points) * 0.95,
        max(point[1] for point in detail_points) * 1.05,
    )
    ax.axhline(
        cracking_moment,
        color="black",
        linewidth=0.9,
        linestyle=":",
        label=f"Mcrc = {cracking_moment:.2f} кН·м",
    )
    ax.set_title("Пример 47: детализация петли в координатах εs–M")
    ax.legend()
    fig.tight_layout()
    fig.savefig(DETAIL_STRAIN_MOMENT_PATH, bbox_inches="tight")
    plt.close(fig)

    print(OVERLAY_PATH)
    print(PANELS_PATH)
    print(STRAIN_FORCE_PATH)
    print(STRAIN_MOMENT_PATH)
    print(MIN_OVERLAY_PATH)
    print(MIN_PANELS_PATH)
    print(MIN_STRAIN_FORCE_PATH)
    print(MIN_STRAIN_MOMENT_PATH)
    print(DETAIL_PLOT_PATH)
    print(DETAIL_STRAIN_MOMENT_PATH)


if __name__ == "__main__":
    main()
