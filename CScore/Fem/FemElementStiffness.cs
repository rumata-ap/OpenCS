using CScore.Fem.Loads;

namespace CScore.Fem;

/// <summary>Упругие свойства пластинчатого КЭ: модуль упругости, Па; коэффициент Пуассона; толщина, м.</summary>
public sealed record FemShellStiffness(double E, double Nu, double H);

/// <summary>
/// Упругие свойства стержневого КЭ в местных осях (СИ): E, G — Па; A — м²; Iy, Iz — м⁴ (Iy — изгиб вокруг местной Y);
/// J — момент инерции при кручении, м⁴.
/// </summary>
public sealed record FemBarStiffness(double E, double G, double A, double Iy, double Iz, double J);

/// <summary>
/// Свойства КЭ сетки из программы-источника схемы (жёсткости SCAD/ЛИРЫ) или из сечений проекта: упругие
/// характеристики для КЭ без нелинейного сечения и данные собственного веса. Общий путь для адаптера CSfea, ΣF загружений
/// и 3D-показа нагрузок.
/// </summary>
public interface IFemElementStiffnessSource : IFemSelfWeightSource
{
    /// <summary>Упругие свойства пластины; null — источник их не знает.</summary>
    FemShellStiffness? Shell(FemElement element);

    /// <summary>Упругие свойства стержня; null — источник их не знает.</summary>
    FemBarStiffness? Bar(FemElement element);
}
