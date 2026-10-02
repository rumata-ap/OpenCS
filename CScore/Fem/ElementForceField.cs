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
    public static bool HasStresses(ForceSet set) => set.ShellItems.Any(IsStressRow);

    static bool IsStressRow(ShellLoadItem i) => i.SigmaX != null || i.SigmaY != null || i.TauXY != null;

    /// <summary>Значения компоненты пластин по номеру КЭ.</summary>
    /// <param name="set">Набор усилий.</param>
    /// <param name="component">Компонента.</param>
    /// <param name="aggregate">Выбор значения при нескольких строках КЭ.</param>
    /// <param name="thicknessM">Толщина КЭ, м — для перевода σ в погонное усилие; null — неизвестна
    /// (тогда у строк с напряжениями Nx/Ny/Nxy нет).</param>
    public static Dictionary<int, double> Shell(
        ForceSet set, ShellForceComponent component, ForceRowAggregate aggregate, Func<int, double?> thicknessM)
    {
        var result = new Dictionary<int, double>();
        foreach (var row in set.ShellItems)
        {
            if (row.SourceElementNum is not int num) continue;
            if (ShellValue(row, component, num, thicknessM) is double v)
                Put(result, num, v, aggregate);
        }
        return result;
    }

    /// <summary>Значения компоненты стержней по номеру КЭ.</summary>
    public static Dictionary<int, double> Bar(ForceSet set, BarForceComponent component, ForceRowAggregate aggregate)
    {
        var result = new Dictionary<int, double>();
        foreach (var row in set.Items)
        {
            if (row.SourceElementNum is not int num) continue;
            Put(result, num, BarValue(row, component), aggregate);
        }
        return result;
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

    static double? ShellValue(ShellLoadItem row, ShellForceComponent component, int num, Func<int, double?> thicknessM)
    {
        switch (component)
        {
            case ShellForceComponent.Mx: return row.Mx;
            case ShellForceComponent.My: return row.My;
            case ShellForceComponent.Mxy: return row.Mxy;
            case ShellForceComponent.Qx: return row.Qx;
            case ShellForceComponent.Qy: return row.Qy;
            case ShellForceComponent.SigmaX: return IsStressRow(row) ? row.SigmaX ?? 0 : null;
            case ShellForceComponent.SigmaY: return IsStressRow(row) ? row.SigmaY ?? 0 : null;
            case ShellForceComponent.TauXY: return IsStressRow(row) ? row.TauXY ?? 0 : null;
        }

        double nx, ny, nxy;
        if (IsStressRow(row))
        {
            if (thicknessM(num) is not double h || !(h > 0)) return null;
            (nx, ny, nxy) = row.ResolveN(h);
        }
        else
            (nx, ny, nxy) = (row.Nx, row.Ny, row.Nxy);
        return component switch
        {
            ShellForceComponent.Nx => nx,
            ShellForceComponent.Ny => ny,
            _ => nxy,
        };
    }

    static void Put(Dictionary<int, double> result, int num, double value, ForceRowAggregate aggregate)
    {
        if (!double.IsFinite(value)) return;
        if (!result.TryGetValue(num, out double current)) { result[num] = value; return; }
        result[num] = aggregate switch
        {
            ForceRowAggregate.Max => Math.Max(current, value),
            ForceRowAggregate.Min => Math.Min(current, value),
            _ => Math.Abs(value) > Math.Abs(current) ? value : current,
        };
    }
}
