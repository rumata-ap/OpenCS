using CScore;
using CScore.Fire.Entities;
using CSfea.Thermal;

namespace CScore.Fire;

/// <summary>
/// Огневое фибровое сечение для предельной проверки по температурным полям.
/// </summary>
public sealed class FireFiberSection : ILimitSection, ISectionCutStressSource
{
    private readonly FireThermalResult _thermal;
    private readonly CrossSectionLimitAdapter _limitAdapter;
    private readonly List<ConcreteMeta> _concreteMeta;
    private readonly List<RebarMeta> _rebarMeta;
    private readonly Dictionary<int, Dictionary<CalcType, Diagramm>> _areaDiagramsByMaterialId;

    /// <summary>Текущие бетонные волокна.</summary>
    public IReadOnlyList<FireConcreteElement> ConcreteElements { get; }

    /// <summary>Текущие арматурные волокна.</summary>
    public IReadOnlyList<FireRebarElement> RebarElements { get; }

    /// <summary>true, если группа класса хотя бы одного стержня определена фолбэком.</summary>
    public bool RebarClassFallback { get; private set; }

    /// <summary>Индекс текущего температурного снапшота.</summary>
    public int SnapshotIndex { get; private set; }

    /// <inheritdoc/>
    public IEnumerable<(double X, double Y)> ContourVertices => _limitAdapter.ContourVertices;

    /// <inheritdoc/>
    public IEnumerable<(double X, double Y, double EpsSu, double EpsP)> RebarPoints => _limitAdapter.RebarPoints;

    /// <inheritdoc/>
    public double EpsCu => _limitAdapter.EpsCu;

    /// <summary>Исходное механическое сечение (для Guess и контурных лимитов).</summary>
    public CrossSection SourceSection { get; }

    /// <summary>Тип заполнителя бетона из результата теплового расчёта.</summary>
    public string AggregateType => _thermal.AggregateType;

    /// <summary>Середина ребра границы с типом fire и его длина.</summary>
    public readonly record struct FireEdgeMidpoint(double X, double Y, double Length);

    /// <summary>Середины огневых рёбер для определения направления нагрева.</summary>
    /// <param name="def">Параметры огневого сечения для восстановления типов ГУ.</param>
    public IEnumerable<FireEdgeMidpoint> FireBoundaryMidpoints(FireSectionDef? def = null)
    {
        var mesh = _thermal.MeshInfo.Mesh;
        if (def is null)
        {
            // Вызов без def сохраняется для диагностических клиентов старой версии;
            // без описания ГУ типы рёбер неизвестны, поэтому используем все рёбра.
            foreach (var edge in _thermal.MeshInfo.BoundaryEdges)
                yield return Midpoint(mesh, edge.NodeA, edge.NodeB);
            yield break;
        }

        foreach (var edge in FireBoundaryMapper.MapEdges(def, _thermal.MeshInfo, _thermal.FireCurve))
            yield return Midpoint(mesh, edge.NodeA, edge.NodeB);
    }

    static FireEdgeMidpoint Midpoint(HeatMesh mesh, int nodeA, int nodeB)
    {
        double x1 = mesh.X[nodeA], y1 = mesh.Y[nodeA];
        double x2 = mesh.X[nodeB], y2 = mesh.Y[nodeB];
        double len = Math.Sqrt((x2 - x1) * (x2 - x1) + (y2 - y1) * (y2 - y1));
        return new FireEdgeMidpoint(0.5 * (x1 + x2), 0.5 * (y1 + y2), len);
    }

    private FireFiberSection(
        FireThermalResult thermal,
        CrossSection sourceSection,
        List<ConcreteMeta> concreteMeta,
        List<RebarMeta> rebarMeta,
        List<FireConcreteElement> concreteElements,
        List<FireRebarElement> rebarElements)
    {
        _thermal = thermal;
        SourceSection = sourceSection;
        _limitAdapter = new CrossSectionLimitAdapter(sourceSection);
        _concreteMeta = concreteMeta;
        _rebarMeta = rebarMeta;
        _areaDiagramsByMaterialId = CollectAreaDiagrams(sourceSection);

        ConcreteElements = concreteElements;
        RebarElements = rebarElements;
    }

    /// <summary>
    /// Создаёт огневое фибровое сечение по результату теплового расчёта.
    /// </summary>
    /// <param name="thermal">Тепловой расчёт со снапшотами температуры.</param>
    /// <param name="section">Исходное механическое сечение.</param>
    /// <param name="snapshotIndex">Индекс снапшота; -1 означает последний.</param>
    public static FireFiberSection FromThermalResult(
        FireThermalResult thermal,
        CrossSection section,
        int snapshotIndex = -1)
    {
        ArgumentNullException.ThrowIfNull(thermal);
        ArgumentNullException.ThrowIfNull(section);
        if (thermal.MeshInfo?.Mesh is null)
            throw new FireCalculationException("FireThermal_MeshMissing");

        int resolvedSnapshot = ResolveSnapshotIndex(thermal, snapshotIndex);
        var mesh = thermal.MeshInfo.Mesh;

        Material concreteMaterial = ResolveConcreteMaterial(section);
        Material rebarFallbackMaterial = ResolveRebarMaterial(section);

        var concreteMeta = new List<ConcreteMeta>(mesh.Elements.Length);
        var concreteElements = new List<FireConcreteElement>(mesh.Elements.Length);
        for (int e = 0; e < mesh.Elements.Length; e++)
        {
            var tri = mesh.Elements[e];
            if (tri.Length != 3)
                throw new FireCalculationException("FireThermal_T6MechanicalUnsupported");

            double x1 = mesh.X[tri[0]];
            double y1 = mesh.Y[tri[0]];
            double x2 = mesh.X[tri[1]];
            double y2 = mesh.Y[tri[1]];
            double x3 = mesh.X[tri[2]];
            double y3 = mesh.Y[tri[2]];

            double area = Math.Abs(0.5 * ((x2 - x1) * (y3 - y1) - (x3 - x1) * (y2 - y1)));
            double cx = (x1 + x2 + x3) / 3.0;
            double cy = (y1 + y2 + y3) / 3.0;

            concreteMeta.Add(new ConcreteMeta(e, tri[0], tri[1], tri[2]));
            concreteElements.Add(new FireConcreteElement
            {
                Area = area,
                Cx = cx,
                Cy = cy,
                Material = concreteMaterial
            });
        }

        var pointFibers = section.Areas
            .SelectMany(a => a.Fibers.Where(f => f.TypeFiber == FiberType.point).Select(f => (Fiber: f, Area: a)))
            .ToList();

        var byFiberId = pointFibers
            .Where(x => x.Fiber.Id != 0)
            .ToDictionary(x => x.Fiber.Id, x => x);

        var rebarMeta = new List<RebarMeta>(thermal.MeshInfo.Rebars.Count);
        var rebarElements = new List<FireRebarElement>(thermal.MeshInfo.Rebars.Count);
        for (int i = 0; i < thermal.MeshInfo.Rebars.Count; i++)
        {
            var r = thermal.MeshInfo.Rebars[i];
            (Fiber Fiber, MaterialArea Area)? match = null;

            if (byFiberId.TryGetValue(r.Id, out var byId))
            {
                match = byId;
            }
            else if (r.Id >= 0 && r.Id < pointFibers.Count)
            {
                match = pointFibers[r.Id];
            }
            else if (i < pointFibers.Count)
            {
                match = pointFibers[i];
            }

            Material mat = match?.Area.Material ?? rebarFallbackMaterial;
            double diameter = match?.Fiber.Diameter ?? 0.0;
            double area = match?.Fiber.Area ?? 0.0;
            if (area <= 0.0 && diameter > 0.0)
            {
                double rad = diameter * 0.5;
                area = Math.PI * rad * rad;
            }

            rebarMeta.Add(new RebarMeta(r.Id));
            var resolution = FireRebarClassResolver.Resolve(mat);
            rebarElements.Add(new FireRebarElement
            {
                RebarId = r.Id,
                X = r.X,
                Y = r.Y,
                Diameter = diameter,
                Area = area,
                Material = mat,
                ClassGroup = resolution.Group,
                ClassSource = resolution.Source
            });
        }

        var fireSection = new FireFiberSection(
            thermal,
            section,
            concreteMeta,
            rebarMeta,
            concreteElements,
            rebarElements);
        fireSection.RebarClassFallback = rebarElements.Exists(e => e.ClassSource == "fallback");
        fireSection.SetSnapshot(resolvedSnapshot);
        return fireSection;
    }

    /// <summary>
    /// Обновляет температурные коэффициенты по указанному снапшоту.
    /// </summary>
    public void SetSnapshot(int idx)
    {
        idx = ResolveSnapshotIndex(_thermal, idx);
        double[] nodeT = _thermal.Snapshots[idx];
        if (nodeT.Length != _thermal.MeshInfo.Mesh.NNodes)
            throw new InvalidOperationException(
                $"Размер снапшота ({nodeT.Length}) не совпадает с числом узлов сетки ({_thermal.MeshInfo.Mesh.NNodes}).");

        for (int i = 0; i < ConcreteElements.Count; i++)
        {
            var meta = _concreteMeta[i];
            double t = (nodeT[meta.N1] + nodeT[meta.N2] + nodeT[meta.N3]) / 3.0;
            var c = (FireConcreteElement)ConcreteElements[i];
            c.Temperature = t;
            c.GammaBt = Sp468Tables.GammaBt(_thermal.AggregateType, t);
        }

        for (int i = 0; i < RebarElements.Count; i++)
        {
            int rebarId = _rebarMeta[i].RebarId;
            double t = ResolveRebarTemperature(_thermal, rebarId, idx);
            var r = (FireRebarElement)RebarElements[i];
            r.Temperature = t;
            r.GammaSt = Sp468Tables.GammaSt(r.ClassGroup, t);
        }

        SnapshotIndex = idx;
    }

    /// <inheritdoc/>
    public Load Integral(Kurvature k, CalcType calc, bool ten = true)
    {
        double n = 0.0;
        double mx = 0.0;
        double my = 0.0;

        foreach (var c in ConcreteElements)
        {
            double eps = k.e0 + k.ky * c.Cy + k.kz * c.Cx;
            if (eps >= 0.0) continue;

            double fi = ConcreteStress(c, eps, calc) * c.Area;
            n += fi;
            mx += fi * c.Cy;
            my += fi * c.Cx;
        }

        foreach (var r in RebarElements)
        {
            double eps = k.e0 + k.ky * r.Y + k.kz * r.X;
            double fi = RebarStress(r, eps, calc) * r.Area;
            n += fi;
            mx += fi * r.Y;
            my += fi * r.X;
        }

        return new Load
        {
            Calc = calc,
            N = n,
            Mx = mx,
            My = my
        };
    }

    /// <summary>Напряжение в бетонном волокне с учётом γ_bt.</summary>
    public double ConcreteStress(FireConcreteElement c, double eps, CalcType calc)
        => ResolveDiagram(c.Material, calc).Sig(eps, out _) * c.GammaBt;

    /// <summary>Напряжение в арматурном волокне с учётом γ_st.</summary>
    public double RebarStress(FireRebarElement r, double eps, CalcType calc)
        => ResolveDiagram(r.Material, calc).Sig(eps, out _) * r.GammaSt;

    /// <summary>Напряжение бетона, кПа, при заданном γ_bt (например, по узловой температуре).</summary>
    public double ConcreteStress(Material material, double eps, CalcType calc, double gammaBt)
        => ResolveDiagram(material, calc).Sig(eps, out _) * gammaBt;

    /// <summary>Температуры узлов сетки на текущем снапшоте, °C.</summary>
    public IReadOnlyList<double> NodeTemperatures => _thermal.Snapshots[SnapshotIndex];

    /// <summary>γ_bt при температуре t, °C, для заполнителя теплового расчёта.</summary>
    public double GammaBtAt(double tCelsius) => Sp468Tables.GammaBt(_thermal.AggregateType, tCelsius);

    /// <summary>
    /// Температура в точке (x, y), м, на текущем снапшоте: линейная интерполяция в треугольнике T3;
    /// точка вне сетки (на границе с погрешностью) — температура ближайшего элемента.
    /// </summary>
    public double TemperatureAt(double x, double y)
    {
        var mesh = _thermal.MeshInfo.Mesh;
        double[] nodeT = _thermal.Snapshots[SnapshotIndex];
        double bestD2 = double.MaxValue;
        double bestT = double.NaN;
        foreach (var m in _concreteMeta)
        {
            double x1 = mesh.X[m.N1], y1 = mesh.Y[m.N1];
            double x2 = mesh.X[m.N2], y2 = mesh.Y[m.N2];
            double x3 = mesh.X[m.N3], y3 = mesh.Y[m.N3];
            double det = (y2 - y3) * (x1 - x3) + (x3 - x2) * (y1 - y3);
            if (Math.Abs(det) < 1e-18) continue;
            double l1 = ((y2 - y3) * (x - x3) + (x3 - x2) * (y - y3)) / det;
            double l2 = ((y3 - y1) * (x - x3) + (x1 - x3) * (y - y3)) / det;
            double l3 = 1.0 - l1 - l2;
            const double eps = -1e-9;
            if (l1 >= eps && l2 >= eps && l3 >= eps)
                return l1 * nodeT[m.N1] + l2 * nodeT[m.N2] + l3 * nodeT[m.N3];

            double cx = (x1 + x2 + x3) / 3.0 - x, cy = (y1 + y2 + y3) / 3.0 - y;
            double d2 = cx * cx + cy * cy;
            if (d2 < bestD2)
            {
                bestD2 = d2;
                bestT = (nodeT[m.N1] + nodeT[m.N2] + nodeT[m.N3]) / 3.0;
            }
        }
        return bestT;
    }

    /// <summary>Стержень огневого сечения, совпадающий по положению с точечной фиброй исходного сечения.</summary>
    public FireRebarElement? FindRebar(double x, double y)
    {
        const double tol = 1e-6;
        FireRebarElement? best = null;
        double bestD2 = tol * tol;
        foreach (var r in RebarElements)
        {
            double dx = r.X - x, dy = r.Y - y;
            double d2 = dx * dx + dy * dy;
            if (d2 <= bestD2) { bestD2 = d2; best = r; }
        }
        return best;
    }

    /// <inheritdoc/>
    /// <remarks>Как в <see cref="Integral"/>: растянутый бетон не работает (п. 8.42 СП 468).</remarks>
    public double? ConcreteSigMpa(MaterialArea area, double x, double y, double eps, CalcType calcType)
    {
        if (eps >= 0.0) return 0.0;
        if (ConcreteElements.Count == 0) return null;
        double t = TemperatureAt(x, y);
        if (double.IsNaN(t)) return null;
        return ConcreteStress(ConcreteElements[0].Material, eps, calcType, GammaBtAt(t)) / 1000.0;
    }

    /// <inheritdoc/>
    public double? RebarSigMpa(Fiber fiber, double eps, CalcType calcType)
        => FindRebar(fiber.X, fiber.Y) is { } r ? RebarStress(r, eps, calcType) / 1000.0 : null;

    private Diagramm ResolveDiagram(Material material, CalcType calc)
    {
        if (_areaDiagramsByMaterialId.TryGetValue(material.Id, out var areaDiagrams) &&
            areaDiagrams.TryGetValue(calc, out var fromArea))
            return fromArea;

        // Фолбэк, когда у области нет собственных диаграмм: тип приводится к допустимому
        // для материала (арматура с условным пределом текучести — только L3).
        var fromMaterial = material.GetDiagramms(
            DiagrammCompatibility.Coerce(material.Type, DiagrammType.L2));
        if (fromMaterial is not null && fromMaterial.TryGetValue(calc, out var diagram))
            return diagram;

        throw new InvalidOperationException(
            $"Не удалось получить диаграмму материала '{material.Tag}' для расчёта {calc}.");
    }

    private static Dictionary<int, Dictionary<CalcType, Diagramm>> CollectAreaDiagrams(CrossSection section)
    {
        var result = new Dictionary<int, Dictionary<CalcType, Diagramm>>();
        foreach (var area in section.Areas)
        {
            if (area.Material is null || area.Diagramms.Count == 0)
                continue;
            if (result.ContainsKey(area.Material.Id))
                continue;

            result[area.Material.Id] = area.Diagramms;
        }
        return result;
    }

    private static Material ResolveConcreteMaterial(CrossSection section)
    {
        var area = section.Areas.FirstOrDefault(a => a.Material?.Type == MatType.Concrete && a.Material is not null);
        if (area?.Material is null)
            throw new InvalidOperationException("В исходном сечении не найден материал бетона.");
        return area.Material;
    }

    private static Material ResolveRebarMaterial(CrossSection section)
    {
        var area = section.Areas.FirstOrDefault(a =>
            a.Material is not null &&
            (a.Material.Type == MatType.ReSteelF || a.Material.Type == MatType.ReSteelU || a.Material.Type == MatType.Steel));
        if (area?.Material is null)
            throw new InvalidOperationException("В исходном сечении не найден материал арматуры.");
        return area.Material;
    }

    private static double ResolveRebarTemperature(FireThermalResult thermal, int rebarId, int snapshot)
    {
        if (thermal.RebarTemperatureHistory.TryGetValue(rebarId, out var history) &&
            snapshot >= 0 && snapshot < history.Length)
            return history[snapshot];

        if (thermal.RebarMaxTemperatures.TryGetValue(rebarId, out var maxT))
            return maxT;

        return 20.0;
    }

    private static int ResolveSnapshotIndex(FireThermalResult thermal, int idx)
    {
        if (thermal.Snapshots.Length == 0)
            throw new FireCalculationException("FireThermal_EmptySnapshot");

        int resolved = idx < 0 ? thermal.Snapshots.Length - 1 : idx;
        if (resolved < 0 || resolved >= thermal.Snapshots.Length)
            throw new ArgumentOutOfRangeException(nameof(idx), $"Индекс снапшота {idx} вне диапазона [0..{thermal.Snapshots.Length - 1}].");
        return resolved;
    }

    private sealed record ConcreteMeta(int ElementIndex, int N1, int N2, int N3);
    private sealed record RebarMeta(int RebarId);
}
