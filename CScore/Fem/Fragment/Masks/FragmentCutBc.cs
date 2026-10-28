using CScore.Planar;

namespace CScore.Fem.Fragment.Masks;

/// <summary>
/// Режим DOF разреза (спека масок разд. 5.1): закреплён (0), свободен, перемещение из маски, сила из маски. Значения —
/// байты <c>fem_fragment_cut_bc.modes</c>.
/// </summary>
public enum FragmentDofMode : byte { Fixed = 0, Free = 1, MaskDisplacement = 2, MaskForce = 3 }

/// <summary>
/// Пресет ГУ разрезов фрагмента (<see cref="FemSchema.FragmentBcPreset"/>, имя члена): «Как в родителе»,
/// «Перемещения из родителя, повороты свободны», «Усилия из родителя», «Изоляция».
/// </summary>
public enum FragmentBcPreset { Kinematic, KinematicTranslations, Static, Isolation }

/// <summary>Режимы DOF разрезов: пресет схемы, ручные правки поверх него, сверлильный поворот стены.</summary>
public static class FragmentCutBc
{
    /// <summary>Байт правки «по пресету».</summary>
    public const byte ByPreset = 255;

    /// <summary>Нормаль стены параллельна общей оси, если косинус не меньше этого (1°).</summary>
    const double AxisAlignedCos = 0.99985;

    /// <summary>Пресет по значению БД: null или неизвестное имя — <see cref="FragmentBcPreset.Kinematic"/>.</summary>
    public static FragmentBcPreset ParsePreset(string? value) =>
        Enum.TryParse<FragmentBcPreset>(value, ignoreCase: false, out var p) && Enum.IsDefined(p) ? p : FragmentBcPreset.Kinematic;

    /// <summary>Режимы пресета для стороны разреза, общие оси <c>Ux Uy Uz Rx Ry Rz</c> (таблица 5.1).</summary>
    public static FragmentDofMode[] PresetModes(FragmentBcPreset preset, FragmentCutSide side)
    {
        const FragmentDofMode F = FragmentDofMode.Fixed, O = FragmentDofMode.Free,
            D = FragmentDofMode.MaskDisplacement, S = FragmentDofMode.MaskForce;
        return preset switch
        {
            FragmentBcPreset.Kinematic => [D, D, D, D, D, D],
            FragmentBcPreset.KinematicTranslations => [D, D, D, O, O, O],
            FragmentBcPreset.Static => [S, S, S, S, S, S],
            FragmentBcPreset.Isolation => side == FragmentCutSide.Bottom ? [F, F, F, O, O, O] : [F, F, O, O, O, O],
            _ => throw new ArgumentOutOfRangeException(nameof(preset), preset, null),
        };
    }

    /// <summary>
    /// Действующие режимы разреза: пресет, у кромки стены в кинематических пресетах поворот вокруг нормали (сверлильный)
    /// свободен, затем ручные правки <paramref name="overrides"/> (6 байт, <see cref="ByPreset"/> — по пресету; null —
    /// правок нет). <paramref name="obliqueWall"/> — нормаль стены не вдоль общей оси: сверлильный поворот не выделяется
    /// одним DOF и остаётся по пресету.
    /// </summary>
    public static FragmentDofMode[] Resolve(FragmentBcPreset preset, FragmentCut cut, byte[]? overrides,
        PlanarVector3? wallNormal, out bool obliqueWall)
    {
        ArgumentNullException.ThrowIfNull(cut);
        var modes = PresetModes(preset, cut.Side);
        obliqueWall = false;
        if (cut.Kind == FragmentCutKind.Edge && preset is FragmentBcPreset.Kinematic or FragmentBcPreset.KinematicTranslations
            && wallNormal is { } n && n.Length > 0)
        {
            var u = n.Normalize();
            int axis = DrillingAxis(u);
            if (axis >= 0) modes[3 + axis] = FragmentDofMode.Free;
            else obliqueWall = true;
        }
        if (overrides != null)
        {
            if (overrides.Length != 6) throw new ArgumentException("Правка ГУ разреза — 6 байт.", nameof(overrides));
            for (int d = 0; d < 6; d++)
                if (overrides[d] != ByPreset) modes[d] = Decode(overrides[d]);
        }
        return modes;
    }

    /// <summary>Общая ось (0..2), вдоль которой нормаль стены, иначе −1.</summary>
    static int DrillingAxis(PlanarVector3 unitNormal)
    {
        double[] c = [Math.Abs(unitNormal.X), Math.Abs(unitNormal.Y), Math.Abs(unitNormal.Z)];
        for (int i = 0; i < 3; i++)
            if (c[i] >= AxisAlignedCos) return i;
        return -1;
    }

    /// <summary>Байт БД → режим; неизвестное значение — ошибка данных.</summary>
    public static FragmentDofMode Decode(byte value) => Enum.IsDefined((FragmentDofMode)value)
        ? (FragmentDofMode)value
        : throw new InvalidDataException($"Неизвестный режим DOF разреза: {value}.");

    /// <summary>Режимы правки → 6 байт БД; null в элементе — «по пресету».</summary>
    public static byte[] Encode(IReadOnlyList<FragmentDofMode?> modes)
    {
        if (modes.Count != 6) throw new ArgumentException("Нужно 6 режимов DOF.", nameof(modes));
        return modes.Select(m => m is { } v ? (byte)v : ByPreset).ToArray();
    }

    /// <summary>Режим требует маску (перемещения или силы).</summary>
    public static bool NeedsMask(FragmentDofMode mode) => mode is FragmentDofMode.MaskDisplacement or FragmentDofMode.MaskForce;
}
