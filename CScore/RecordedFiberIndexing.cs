namespace CScore;

/// <summary>Хелпер обхода волокон сечения в порядке плоских индексов, в котором
/// генератор OpenSees записывает состояния в nonlinear_fiber_states.out.</summary>
public static class RecordedFiberIndexing
{
    /// <summary>Перечисляет волокна в порядке, совпадающем с обходом SectionPlotVM:
    /// области по порядку EnumerateAreas(k); область без диаграммы для calcType
    /// пропускается, но её волокна занимают индексы; внутри области — сначала
    /// неповерностные волокна (в порядке списка Fibers), затем точечные (арматура).</summary>
    public static IEnumerable<(MaterialArea Area, Fiber Fiber, int Index)> EnumerateRecordedFibers(
        this CrossSection section, Kurvature k, CalcType calcType)
    {
        int index = 0;
        foreach (var (area, _) in section.EnumerateAreas(k))
        {
            if (!area.Diagramms.TryGetValue(calcType, out _))
            {
                index += area.Fibers.Count;
                continue;
            }
            foreach (var fiber in area.Fibers.Where(f => f.TypeFiber != FiberType.point))
                yield return (area, fiber, index++);
            foreach (var fiber in area.Fibers.Where(f => f.TypeFiber == FiberType.point))
                yield return (area, fiber, index++);
        }
    }
}
