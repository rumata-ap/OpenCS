using CScore.Fem;
using CScore.Planar;
using CScore.Submodel;

namespace CScore.Import;

/// <summary>
/// Местные оси стержней сетки импорта ЛИРЫ/SCAD — как у родных стержней OpenCS (решение 18.11.2026): местная Y = Z1
/// программы, местная Z = X1 × Z1 = −Y1, то есть поворот сечения на 90° больше, чем «Y = Y1». У балки местная Y
/// вертикальна, как у родной балки (<see cref="BeamLocalAxisConvention"/>), поэтому «естественное» сечение импорта
/// (x сечения ‖ Y1 — ширина B, y ‖ Z1 — высота H, <see cref="BarSections.RcSectionBuilder"/>) совпадает с геометрией
/// OpenCS: 3D-вид, АЖТ по контуру, продавливание, CSfea по сечению и наборы из результатов CSfea. Перевод усилий таблиц
/// ЛИРЫ/SCAD в сечение (<see cref="LiraForceMapper"/>: Mx ← My₁) не меняется.
/// <para>Связь компонент (вектор в осях программы → в осях сетки): Y = Z₁, Z = −Y₁.</para>
/// </summary>
public static class ImportedBarAxes
{
    /// <summary>
    /// Поворот сечения стержня i → j от осей <see cref="BeamLocalAxisConvention"/>, град, по оси Y1 программы
    /// (единичный вектор в общих осях): местная Y сетки — Z1 = X1 × Y1. Null — нулевая длина или Y1 вдоль стержня.
    /// </summary>
    public static double? RotationDeg(PlanarVector3 i, PlanarVector3 j, PlanarVector3 y1)
    {
        var d = j - i;
        if (!(d.Length > 1e-12)) return null;
        var z1 = d.Cross(y1);
        return z1.Length < 1e-12 * d.Length ? null : BeamLocalAxisConvention.RotationDeg(i, j, z1);
    }

    /// <summary>Жёсткость в осях программы (Iy — вокруг Y1, Iz — вокруг Z1) → в осях сетки: Iy ↔ Iz.</summary>
    public static FemBarStiffness ToMesh(FemBarStiffness s) => s with { Iy = s.Iz, Iz = s.Iy };

    /// <summary>Силы и моменты в осях программы (X1, Y1, Z1) → в осях сетки: Y = Z₁, Z = −Y₁.</summary>
    public static Dof6 ToMesh(Dof6 p) => new(p.X, p.Z, -p.Y, p.Rx, p.Rz, -p.Ry);

    /// <summary>
    /// Маска освобождений конца стержня (биты 0–5: N, Qy, Qz, T, My, Mz) в осях программы → в осях сетки: Qy₁ ↔ Qz,
    /// My₁ ↔ Mz (знак у освобождения не важен). Прочие биты — как есть.
    /// </summary>
    public static int ReleaseToMesh(int mask)
    {
        static int Bit(int m, int i) => (m >> i) & 1;
        int swapped = Bit(mask, 1) << 2 | Bit(mask, 2) << 1 | Bit(mask, 4) << 5 | Bit(mask, 5) << 4;
        return mask & ~0b110110 | swapped;
    }

    /// <summary>
    /// Ось сетки местной нагрузки стержня по направлению программы 1–3 (X1, Y1, Z1) и множитель значения:
    /// X1 → x, Y1 → −z, Z1 → y.
    /// </summary>
    public static (string Axis, double Sign) LocalLoadAxis(int direction) => direction switch
    {
        1 => ("x", 1),
        2 => ("z", -1),
        _ => ("y", 1),
    };
}
