#!/usr/bin/env python3
"""Generate OpenCS steel-rebar CSVs from the pinned SP 63 catalog.

The generator is intentionally a development-time adapter.  OpenCS consumes
the generated CSV files and does not import Python or the source catalog at
runtime.
"""

from __future__ import annotations

import argparse
import re
import sys
from dataclasses import dataclass
from pathlib import Path
from typing import Iterable


ROOT = Path(__file__).resolve().parents[1]
SOURCE_PACKAGE = ROOT / "tools" / "structural-materials" / "structural_materials"
sys.path.insert(0, str(SOURCE_PACKAGE))

try:
    from sp63_materials import (  # type: ignore
        TABLE_6_13_RSN,
        TABLE_6_14_REBAR,
        Rebar,
    )
except ImportError as error:  # pragma: no cover - exercised by developer setup
    raise SystemExit(
        "Не удалось импортировать structural-materials. "
        "Установите NumPy и выполните git submodule update --init --recursive."
    ) from error


CSV_HEADER = (
    "Tag;Class;Fc;Ft;Ry;Ru;E;Ec0;Ec1;Ec2;Ec1Red;Et1Red;"
    "Et0;Et1;Et2;Type;TypeCalc;Dampness;\r\n"
)


@dataclass(frozen=True)
class RebarOpenCsProfile:
    """Explicit adaptation of one SP 63 grade to the OpenCS CSV schema."""

    source_grade: str
    tag: str
    diameter_text: str
    elastic_modulus_mpa: float
    material_type: int
    ultimate_strain: float

    @property
    def class_value(self) -> int:
        match = re.search(r"(\d+)$", self.source_grade)
        if match is None:
            raise ValueError(f"Не удалось определить числовой класс: {self.source_grade}")
        return int(match.group(1))


# The diameter strings are taken from the current SP 63 table 6.13.  A slash
# is used for K1750's two nominal diameters so that the display label remains
# one semicolon-separated CSV field.
PROFILES: tuple[RebarOpenCsProfile, ...] = (
    RebarOpenCsProfile("A240", "A240", "6-40 мм", 200_000, 2, 0.025),
    RebarOpenCsProfile("A400", "A400", "6-40 мм", 200_000, 2, 0.025),
    RebarOpenCsProfile("A500", "A500", "6-40 мм", 200_000, 2, 0.025),
    RebarOpenCsProfile("A600", "A600", "6-40 мм", 200_000, 3, 0.015),
    RebarOpenCsProfile("A800", "A800", "10-32 мм", 200_000, 3, 0.015),
    RebarOpenCsProfile("A1000", "A1000", "10-32 мм", 200_000, 3, 0.015),
    RebarOpenCsProfile("B500", "B500", "3-16 мм", 200_000, 2, 0.025),
    RebarOpenCsProfile("Bp500", "Вр500", "3-5 мм", 200_000, 2, 0.025),
    RebarOpenCsProfile("Bp1200", "Вр1200", "8 мм", 200_000, 3, 0.015),
    RebarOpenCsProfile("Bp1300", "Вр1300", "7 мм", 200_000, 3, 0.015),
    RebarOpenCsProfile("Bp1400", "Вр1400", "4,5,6 мм", 200_000, 3, 0.015),
    RebarOpenCsProfile("Bp1500", "Вр1500", "3 мм", 200_000, 3, 0.015),
    RebarOpenCsProfile("Bp1600", "Вр1600", "3-5 мм", 200_000, 3, 0.015),
    RebarOpenCsProfile("K1400", "К1400", "15,2 мм", 195_000, 3, 0.015),
    RebarOpenCsProfile("K1450", "К1450", "15,2 мм", 195_000, 3, 0.015),
    RebarOpenCsProfile("K1500", "К1500", "6,2-12,4 мм", 195_000, 3, 0.015),
    RebarOpenCsProfile("K1550", "К1550", "6,9-18,0 мм", 195_000, 3, 0.015),
    RebarOpenCsProfile("K1650", "К1650", "6,9-15,7 мм", 195_000, 3, 0.015),
    RebarOpenCsProfile("K1750", "К1750", "9,0/9,3 мм", 195_000, 3, 0.015),
    RebarOpenCsProfile("K1850", "К1850", "6,9 мм", 195_000, 3, 0.015),
    RebarOpenCsProfile("K1900", "К1900", "6,9 мм", 195_000, 3, 0.015),
)


TARGETS = {
    "C": ROOT / "OpenCS" / "DataSource" / "Арматура стальная_C.csv",
    "CL": ROOT / "OpenCS" / "DataSource" / "Арматура стальная_CL.csv",
    "N": ROOT / "OpenCS" / "DataSource" / "Арматура стальная_N.csv",
    "NL": ROOT / "OpenCS" / "DataSource" / "Арматура стальная_NL.csv",
}


def validate_catalog() -> None:
    """Reject stale or incomplete adapter mappings before writing anything."""

    assert TABLE_6_13_RSN["A400"] == 390.0
    assert TABLE_6_14_REBAR["A800"] == {
        "Rs": 695.0,
        "Rsc_short": 400.0,
        "Rsc_long": 500.0,
    }
    assert TABLE_6_14_REBAR["B500"] == {
        "Rs": 415.0,
        "Rsc_short": 380.0,
        "Rsc_long": 415.0,
    }

    source_grades = tuple(TABLE_6_14_REBAR)
    profile_grades = tuple(profile.source_grade for profile in PROFILES)
    if len(set(profile_grades)) != len(profile_grades):
        raise ValueError("Адаптация содержит повторяющийся класс арматуры.")
    if profile_grades != source_grades:
        missing = sorted(set(source_grades) - set(profile_grades))
        extra = sorted(set(profile_grades) - set(source_grades))
        raise ValueError(
            "Номенклатура адаптера не совпадает с источником: "
            f"missing={missing}, extra={extra}"
        )

    for profile in PROFILES:
        if profile.material_type not in {2, 3}:
            raise ValueError(f"Недопустимый тип материала для {profile.source_grade}.")
        if profile.material_type == 2 and profile.ultimate_strain != 0.025:
            raise ValueError(f"Физический предел текучести требует eps2=0.025: {profile.source_grade}")
        if profile.material_type == 3 and profile.ultimate_strain != 0.015:
            raise ValueError(f"Условный предел текучести требует eps2=0.015: {profile.source_grade}")


def number(value: float | int) -> str:
    """Format numeric CSV fields reproducibly and without scientific noise."""

    if isinstance(value, int) or float(value).is_integer():
        return str(int(value))
    return format(float(value), ".12g")


def row(
    profile: RebarOpenCsProfile,
    *,
    calc_type: int,
    strength_tension_mpa: float,
    strength_compression_mpa: float,
    include_first_group_tail: bool,
) -> str:
    """Build one row, keeping the legacy OpenCS column order."""

    e_kpa = profile.elastic_modulus_mpa * 1000.0
    ft_kpa = strength_tension_mpa * 1000.0
    fc_kpa = -strength_compression_mpa * 1000.0

    if profile.material_type == 2:
        ec0 = -strength_compression_mpa * 1000.0 / e_kpa
        ec1 = 0.0
        et0 = strength_tension_mpa * 1000.0 / e_kpa
        et1 = 0.0
    else:
        # СП 63 p. 6.2.11: conditional yield uses eps_s0 = Rs/E + 0.002.
        # OpenCS's existing L3 adapter stores the symmetric tensile profile in
        # these fields; D3L uses the same values together with Fc/Ft.
        eps_s0 = ft_kpa / e_kpa + 0.002
        eps_s1 = 0.9 * ft_kpa / e_kpa
        ec0 = -eps_s0
        ec1 = -eps_s1
        et0 = eps_s0
        et1 = eps_s1

    ru_kpa = 1.1 * ft_kpa if calc_type in {3, 4} and profile.material_type == 3 else 0.0
    tail = number(strength_tension_mpa) if include_first_group_tail else ""
    fields = (
        f"{profile.tag} ({profile.diameter_text})",
        number(profile.class_value),
        number(fc_kpa),
        number(ft_kpa),
        "0",
        number(ru_kpa),
        number(e_kpa),
        number(ec0),
        number(ec1),
        "-0.0035",
        "0",
        "0",
        number(et0),
        number(et1),
        number(profile.ultimate_strain),
        number(profile.material_type),
        number(calc_type),
        "0",
        tail,
    )
    return ";".join(fields) + "\r\n"


def generate() -> dict[str, str]:
    """Return generated UTF-8 CSV text for all four calculation types."""

    validate_catalog()
    output: dict[str, str] = {}
    for name, calc_type in (("C", 1), ("CL", 2), ("N", 3), ("NL", 4)):
        lines = [CSV_HEADER]
        for profile in PROFILES:
            rebar = Rebar(profile.source_grade, long_term=name == "CL" or name == "NL")
            if name in {"C", "CL"}:
                lines.append(
                    row(
                        profile,
                        calc_type=calc_type,
                        strength_tension_mpa=rebar.Rs,
                        strength_compression_mpa=rebar.Rsc,
                        include_first_group_tail=True,
                    )
                )
            else:
                lines.append(
                    row(
                        profile,
                        calc_type=calc_type,
                        strength_tension_mpa=rebar.Rs_ser,
                        strength_compression_mpa=rebar.Rs_ser,
                        include_first_group_tail=False,
                    )
                )
        output[name] = "".join(lines)
    return output


def encoded(text: str) -> bytes:
    """Encode like the checked-in Windows CSV files (UTF-8 with BOM)."""

    return text.encode("utf-8-sig")


def check(generated: dict[str, str]) -> int:
    stale = []
    for name, path in TARGETS.items():
        if not path.exists() or path.read_bytes() != encoded(generated[name]):
            stale.append(path.relative_to(ROOT).as_posix())
    for path in stale:
        print(f"stale: {path}")
    if stale:
        return 1
    print("SP 63 rebar CSVs are up to date.")
    return 0


def write(generated: dict[str, str]) -> int:
    for name, path in TARGETS.items():
        path.write_bytes(encoded(generated[name]))
        print(f"rewrote: {path.relative_to(ROOT).as_posix()}")
    return 0


def parse_args(argv: Iterable[str] | None = None) -> argparse.Namespace:
    parser = argparse.ArgumentParser(description=__doc__)
    mode = parser.add_mutually_exclusive_group(required=True)
    mode.add_argument("--check", action="store_true", help="compare CSVs with generated output")
    mode.add_argument("--write", action="store_true", help="rewrite the four generated CSVs")
    return parser.parse_args(argv)


def main(argv: Iterable[str] | None = None) -> int:
    args = parse_args(argv)
    generated = generate()
    return check(generated) if args.check else write(generated)


if __name__ == "__main__":
    raise SystemExit(main())
