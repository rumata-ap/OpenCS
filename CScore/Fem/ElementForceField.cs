namespace CScore.Fem;

/// <summary>Компонента усилий пластинчатого КЭ для мозаики.</summary>
public enum ShellForceComponent
{
    /// <summary>Погонное усилие по X, кН/м.</summary>
    Nx,
    /// <summary>Погонное усилие по Y, кН/м.</summary>
    Ny,
    /// <summary>Погонное сдвигающее усилие, кН/м.</summary>
    Nxy,
    /// <summary>Погонный изгибающий момент Mx, кН·м/м.</summary>
    Mx,
    /// <summary>Погонный изгибающий момент My, кН·м/м.</summary>
    My,
    /// <summary>Погонный крутящий момент, кН·м/м.</summary>
    Mxy,
    /// <summary>Погонная поперечная сила Qx, кН/м.</summary>
    Qx,
    /// <summary>Погонная поперечная сила Qy, кН/м.</summary>
    Qy,
    /// <summary>Импортированное напряжение σx, кПа.</summary>
    SigmaX,
    /// <summary>Импортированное напряжение σy, кПа.</summary>
    SigmaY,
    /// <summary>Импортированное напряжение τxy, кПа.</summary>
    TauXY,
}

/// <summary>Компонента усилий стержневого КЭ для мозаики.</summary>
public enum BarForceComponent
{
    /// <summary>Продольная сила, кН.</summary>
    N,
    /// <summary>Изгибающий момент Mx, кН·м.</summary>
    Mx,
    /// <summary>Изгибающий момент My, кН·м.</summary>
    My,
    /// <summary>Поперечная сила Vx, кН.</summary>
    Vx,
    /// <summary>Поперечная сила Vy, кН.</summary>
    Vy,
    /// <summary>Крутящий момент, кН·м.</summary>
    T,
}

/// <summary>Какое значение показывать, когда у КЭ в наборе несколько строк (сечения, сочетания РСУ).</summary>
public enum ForceRowAggregate
{
    /// <summary>Наибольшее по модулю, со своим знаком.</summary>
    MaxAbs,
    /// <summary>Наибольшее.</summary>
    Max,
    /// <summary>Наименьшее.</summary>
    Min,
}

/// <summary>
/// Поле усилий по КЭ внешней расчётной схемы: значение компоненты набора усилий на каждом КЭ
/// (по номеру КЭ строки — <see cref="LoadItem.SourceElementNum"/> / <see cref="ShellLoadItem.SourceElementNum"/>).
/// </summary>
public static class ElementForceField
{
    /// <summary>В наборе есть строки пластин с номером КЭ.</summary>
    public static bool HasShellRows(ForceSet set) => set.ElementStats(shell: true).HasElementRows;

    /// <summary>В наборе есть строки стержней с номером КЭ.</summary>
    public static bool HasBarRows(ForceSet set) => set.ElementStats(shell: false).HasElementRows;

    /// <summary>Мембранные усилия строк набора заданы напряжениями (σ·h считается по толщине КЭ).</summary>
    public static bool HasStresses(ForceSet set) => set.Envelope(shell: true).HasStresses;

    /// <summary>Значения компоненты пластин по номеру КЭ (по огибающей набора — строки в память не грузятся).</summary>
    /// <param name="set">Набор усилий.</param>
    /// <param name="component">Компонента.</param>
    /// <param name="aggregate">Выбор значения при нескольких строках КЭ.</param>
    /// <param name="thicknessM">Толщина КЭ, м — для перевода σ в погонное усилие; null — неизвестна
    /// (тогда у строк с напряжениями Nx/Ny/Nxy нет).</param>
    public static Dictionary<int, double> Shell(
        ForceSet set, ShellForceComponent component, ForceRowAggregate aggregate, Func<int, double?> thicknessM)
    {
        int c = (int)component;
        // Nx/Ny/Nxy строк с напряжениями — σ·h, где σ — канал σx/σy/τxy (порядок каналов тот же, сдвиг на 8).
        int? stress = component <= ShellForceComponent.Nxy ? c + (int)ShellForceComponent.SigmaX : null;
        var result = new Dictionary<int, double>();
        foreach (var sections in set.Envelope(shell: true).ByElement())
        {
            double min = double.NaN, max = double.NaN;
            double? h = null;
            foreach (var e in sections)
            {
                Merge(ref min, ref max, e.Min[c], e.Max[c]);
                if (stress is not int s || double.IsNaN(e.Min[s])) continue;
                h ??= thicknessM(e.Elem) is double t && t > 0 ? t : double.NaN;
                if (h > 0) Merge(ref min, ref max, e.Min[s] * h.Value, e.Max[s] * h.Value);
            }
            if (ForceSetEnvelope.Entry.Pick(min, max, aggregate) is double v)
                result[sections.Key] = v;
        }
        return result;
    }

    /// <summary>Значения компоненты стержней по номеру КЭ (по огибающей набора — строки в память не грузятся).</summary>
    public static Dictionary<int, double> Bar(ForceSet set, BarForceComponent component, ForceRowAggregate aggregate)
    {
        int c = (int)component;
        var result = new Dictionary<int, double>();
        foreach (var sections in set.Envelope(shell: false).ByElement())
        {
            double min = double.NaN, max = double.NaN;
            foreach (var e in sections) Merge(ref min, ref max, e.Min[c], e.Max[c]);
            if (ForceSetEnvelope.Entry.Pick(min, max, aggregate) is double v)
                result[sections.Key] = v;
        }
        return result;
    }

    /// <summary>Расширяет диапазон [<paramref name="min"/>, <paramref name="max"/>] (NaN — пустой) другим.</summary>
    static void Merge(ref double min, ref double max, double otherMin, double otherMax)
    {
        if (double.IsNaN(otherMin)) return;
        if (double.IsNaN(min) || otherMin < min) min = otherMin;
        if (double.IsNaN(max) || otherMax > max) max = otherMax;
    }

    /// <summary>Значение компоненты в строке усилий стержня.</summary>
    public static double BarValue(LoadItem row, BarForceComponent component) => component switch
    {
        BarForceComponent.N => row.N,
        BarForceComponent.Mx => row.Mx,
        BarForceComponent.My => row.My,
        BarForceComponent.Vx => row.Vx,
        BarForceComponent.Vy => row.Vy,
        _ => row.T,
    };
}
