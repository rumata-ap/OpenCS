using CScore.Sp16;

namespace CScore.Import;

/// <summary>
/// Стальной профиль стержня импортированной схемы, нейтральный к программе-источнику. Размеры — в м, в
/// трактовке <see cref="ParametricSteel.ParametricSteelSectionDefinition"/>: H — высота (диаметр трубы и круга,
/// вертикальная полка уголка), B — ширина полки, Tw/Tf — толщины стенки и полки, R1/R2 — радиусы (у гнутых
/// профилей R1 — внутренний радиус гиба), FlangeSlope — уклон внутренних граней полок. Положение — каноническое
/// (стенка ‖ Z1 стержня, т. е. вертикальна в осях сечения OpenCS); <see cref="Flipped"/> — зеркальный уголок.
/// Справочные A, Iy, Iz — из сортамента источника (Iy — относительно оси, параллельной полкам, ‖ Y1).
/// </summary>
public sealed record ImportedSteelShape(
    SteelProfileKind Kind, SteelFabrication Fabrication,
    double H, double B, double Tw, double Tf, double R1, double R2, double FlangeSlope,
    string Standard, string Name,
    double? ACm2 = null, double? IyCm4 = null, double? IzCm4 = null,
    bool Flipped = false);
