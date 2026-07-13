using CScore;

namespace CScore.Fire;

/// <summary>
/// Огневое фибровое сечение для предельной проверки по температурным полям.
/// </summary>
public sealed class FireFiberSection : ILimitSection
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

    /// <summary>Индекс текущего температурного снапшота.</summary>
    public int SnapshotIndex { get; private set; }

    /// <inheritdoc/>
    public IEnumerable<(double X, double Y)> ContourVertices => _limitAdapter.ContourVertices;

    /// <inheritdoc/>
    public IEnumerable<(double X, double Y, double EpsSu)> RebarPoints => _limitAdapter.RebarPoints;

    /// <inheritdoc/>
    public double EpsCu => _limitAdapter.EpsCu;

    /// <summary>Исходное механическое сечение (для Guess и контурных лимитов).</summary>
    public CrossSection SourceSection { get; }

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
            throw new InvalidOperationException("В FireThermalResult отсутствует MeshInfo.Mesh.");

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
                throw new InvalidOperationException($"Элемент #{e} тепловой сетки не является T3.");

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
            rebarElements.Add(new FireRebarElement
            {
                RebarId = r.Id,
                X = r.X,
                Y = r.Y,
                Diameter = diameter,
                Area = area,
                Material = mat
            });
        }

        var fireSection = new FireFiberSection(
            thermal,
            section,
            concreteMeta,
            rebarMeta,
            concreteElements,
            rebarElements);
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
            c.GammaBt = FireMaterials.GammaBt("", _thermal.AggregateType, t);
        }

        for (int i = 0; i < RebarElements.Count; i++)
        {
            int rebarId = _rebarMeta[i].RebarId;
            double t = ResolveRebarTemperature(_thermal, rebarId, idx);
            var r = (FireRebarElement)RebarElements[i];
            r.Temperature = t;
            r.GammaStC = FireMaterials.GammaSt("", t, "compression");
            r.GammaStT = FireMaterials.GammaSt("", t, "tension");
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
            Diagramm d = ResolveDiagram(c.Material, calc);
            double sigma = d.Sig(eps, out _) * c.GammaBt;

            double fi = sigma * c.Area;
            n += fi;
            mx += fi * c.Cy;
            my += fi * c.Cx;
        }

        foreach (var r in RebarElements)
        {
            double eps = k.e0 + k.ky * r.Y + k.kz * r.X;
            Diagramm d = ResolveDiagram(r.Material, calc);
            double gamma = eps < 0.0 ? r.GammaStC : r.GammaStT;
            double sigma = d.Sig(eps, out _) * gamma;

            double fi = sigma * r.Area;
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

    private Diagramm ResolveDiagram(Material material, CalcType calc)
    {
        if (_areaDiagramsByMaterialId.TryGetValue(material.Id, out var areaDiagrams) &&
            areaDiagrams.TryGetValue(calc, out var fromArea))
            return fromArea;

        var fromMaterial = material.GetDiagramms(DiagrammType.L2);
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
            throw new InvalidOperationException("В FireThermalResult нет температурных снапшотов.");

        int resolved = idx < 0 ? thermal.Snapshots.Length - 1 : idx;
        if (resolved < 0 || resolved >= thermal.Snapshots.Length)
            throw new ArgumentOutOfRangeException(nameof(idx), $"Индекс снапшота {idx} вне диапазона [0..{thermal.Snapshots.Length - 1}].");
        return resolved;
    }

    private sealed record ConcreteMeta(int ElementIndex, int N1, int N2, int N3);
    private sealed record RebarMeta(int RebarId);
}
