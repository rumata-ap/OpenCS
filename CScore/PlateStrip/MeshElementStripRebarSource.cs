using CScore.Fem;
using CScore.Fem.Loads;
using CScore.Planar;
using CScore.PlateRebar;

namespace CScore.PlateStrip;

/// <summary>
/// Армирование вдоль полосы по КЭ сетки региона: точка полосы (u, v) в плоскости региона попадает в КЭ,
/// а сечение КЭ даёт любой <see cref="IPlateElementSectionSource"/> — заданное/подобранное армирование
/// импортированной схемы (ЛИРА, SCAD) или раскладка OpenCS. Расчёт полосы не зависит от программы-источника.
/// Слои возвращаются в гранях региона: у КЭ с нормалью против нормали региона слои отражены обратно.
/// Ось армирования — ось выдачи усилий КЭ (узел 1 → узел 2, повёрнутая на угол согласования), за вычетом
/// угла поворота усилий в оси армирования, который вернул источник.
/// </summary>
public sealed class MeshElementStripRebarSource : IStripRebarSource
{
    /// <summary>Расстояние, м, на которое точка вне всех КЭ ещё привязывается к ближайшему (округления
    /// координат узлов и точек полосы на границе КЭ).</summary>
    public const double SnapToleranceM = 1e-5;

    sealed record Cell(
        FemCheckScopeElement Element, (double U, double V)[] Polygon, double MinU, double MaxU, double MinV, double MaxV,
        double OutputAxisDeg, bool Mirrored, bool AxesKnown);

    readonly IPlateElementSectionSource _source;
    readonly List<Cell> _cells;
    readonly Dictionary<FemCheckScopeElement, StripRebarSample> _samples = new(ReferenceEqualityComparer.Instance);
    double _firstThicknessM = double.NaN;
    bool _thicknessWarned;

    MeshElementStripRebarSource(IPlateElementSectionSource source, List<Cell> cells, int skipped)
    {
        _source = source;
        _cells = cells;
        SkippedElements = skipped;
    }

    /// <summary>Число КЭ, у которых не удалось построить геометрию (нет узлов, вырожденный КЭ) — в поиске не участвуют.</summary>
    public int SkippedElements { get; }

    /// <summary>Число КЭ, по которым ищется точка.</summary>
    public int ElementCount => _cells.Count;

    /// <summary>У скольких КЭ угол согласования осей выдачи неизвестен (приняты оси региона).</summary>
    public int ElementsWithUnknownAxes => _cells.Count(c => !c.AxesKnown);

    /// <inheritdoc/>
    public string Key => _source.Key;

    /// <inheritdoc/>
    public bool IsSpatial => true;

    /// <summary>Источник сечений КЭ (для предупреждений о материалах и привязках).</summary>
    public IPlateElementSectionSource SectionSource => _source;

    /// <summary>КЭ, из которых собран поиск (для <see cref="IPlateElementSectionSource.Warnings"/>).</summary>
    public IReadOnlyList<FemCheckScopeElement> Elements => _cells.Select(c => c.Element).ToList();

    /// <summary>
    /// Собрать источник. Геометрия КЭ переводится в плоскость региона (<paramref name="regionFrame"/>).
    /// </summary>
    /// <param name="regionFrame">Система координат региона (оси U, V полосы).</param>
    /// <param name="elements">Пластинчатые КЭ региона (цель проверки).</param>
    /// <param name="nodes">Узлы сетки схемы.</param>
    /// <param name="source">Источник сечений КЭ.</param>
    public static MeshElementStripRebarSource Create(
        Frame3D regionFrame, IEnumerable<FemCheckScopeElement> elements, IEnumerable<FemMeshNode> nodes,
        IPlateElementSectionSource source)
    {
        ArgumentNullException.ThrowIfNull(regionFrame);
        ArgumentNullException.ThrowIfNull(elements);
        ArgumentNullException.ThrowIfNull(nodes);
        ArgumentNullException.ThrowIfNull(source);

        // Теги узлов импортированной и сгенерированной сеток могут совпадать — узел берётся по происхождению КЭ.
        var imported = new Dictionary<string, FemMeshNode>(StringComparer.Ordinal);
        var generated = new Dictionary<string, FemMeshNode>(StringComparer.Ordinal);
        foreach (var n in nodes)
            (n.Origin == FemMember.MeshSourceImported ? imported : generated).TryAdd(n.NodeTag, n);

        var cells = new List<Cell>();
        int skipped = 0;
        foreach (var scoped in elements)
        {
            var element = scoped.Element;
            var primary = element.Origin == FemMember.MeshSourceImported ? imported : generated;
            var secondary = ReferenceEquals(primary, imported) ? generated : imported;
            var geometry = FemElementGeometry.Of(element, primary) ?? FemElementGeometry.Of(element, secondary);
            if (geometry is not { IsShell: true } || geometry.Area <= 1e-12) { skipped++; continue; }

            var (x, _, z) = geometry.ShellFrame();
            double angle = (element.LocalAxisAngleDeg ?? 0.0) * Math.PI / 180.0;
            var outputX = x * Math.Cos(angle) + z.Cross(x) * Math.Sin(angle);
            double outputDeg = Math.Atan2(outputX.Dot(regionFrame.LocalY), outputX.Dot(regionFrame.LocalX)) * 180.0 / Math.PI;

            var polygon = geometry.Contour
                .Select(i => PlanarBoundaryFrameConverter.ToLocalPoint(regionFrame, geometry.Points[i]))
                .Select(p => (U: p.X, V: p.Y))
                .ToArray();
            cells.Add(new Cell(
                scoped, polygon,
                polygon.Min(p => p.U), polygon.Max(p => p.U), polygon.Min(p => p.V), polygon.Max(p => p.V),
                outputDeg, z.Dot(regionFrame.LocalZ) < 0, element.LocalAxisAngleDeg.HasValue));
        }
        return new MeshElementStripRebarSource(source, cells, skipped);
    }

    /// <inheritdoc/>
    public StripRebarSample Resolve(double u, double v)
    {
        var cell = Locate(u, v);
        if (cell == null)
            return new([], [new("plate_strip_point_outside_mesh",
                $"Точка полосы (u = {u:F3}, v = {v:F3}) не попала ни в один КЭ сетки региона — армирование неизвестно.")]);
        return SampleOf(cell);
    }

    /// <summary>КЭ, в который попала точка (на общей границе — первый по порядку); при выходе за все КЭ —
    /// ближайший не дальше <see cref="SnapToleranceM"/>. Null — точка вне сетки.</summary>
    public FemCheckScopeElement? Find(double u, double v) => Locate(u, v)?.Element;

    Cell? Locate(double u, double v)
    {
        Cell? nearest = null;
        double best = SnapToleranceM;
        foreach (var cell in _cells)
        {
            if (u < cell.MinU - SnapToleranceM || u > cell.MaxU + SnapToleranceM ||
                v < cell.MinV - SnapToleranceM || v > cell.MaxV + SnapToleranceM) continue;
            if (Inside(cell.Polygon, u, v)) return cell;
            double d = DistanceToBoundary(cell.Polygon, u, v);
            if (d <= best) { best = d; nearest = cell; }
        }
        return nearest;
    }

    StripRebarSample SampleOf(Cell cell)
    {
        if (_samples.TryGetValue(cell.Element, out var cached)) return Warned(cached);

        var section = _source.Resolve(cell.Element);
        StripRebarSample sample;
        string tag = cell.Element.Element.ElemTag;
        if (section.Section == null)
            sample = new([], [new("plate_strip_mesh_rebar_missing",
                $"У КЭ {tag} нет армирования из источника «{_source.Key}»: {section.Reason ?? "причина не указана"}.")]);
        else
        {
            var layers = new List<PlateRebarLayer>(section.Section.RebarLayers.Count);
            foreach (var l in section.Section.RebarLayers)
            {
                var copy = l.Clone();
                if (cell.Mirrored)
                {
                    // Слои заданы в гранях КЭ; полоса работает в гранях региона.
                    copy.Zsx = -copy.Zsx;
                    copy.Zsy = -copy.Zsy;
                    copy.Face = copy.Face == RebarFace.PlusN ? RebarFace.MinusN : RebarFace.PlusN;
                }
                layers.Add(copy);
            }
            // ForceAngleDeg — угол от оси армирования до оси выдачи по часовой/против, если смотреть с конца
            // нормали КЭ; в плоскости региона у зеркального КЭ он меняет знак.
            double force = cell.Mirrored ? -section.ForceAngleDeg : section.ForceAngleDeg;
            sample = new(layers, [], cell.OutputAxisDeg - force) { ThicknessM = section.Section.H };
        }
        _samples[cell.Element] = sample;
        return Warned(sample);
    }

    /// <summary>Толщина КЭ вдоль полосы меняется: сечение полосы берёт толщину фонового сечения — предупреждение один раз.</summary>
    StripRebarSample Warned(StripRebarSample sample)
    {
        if (sample.ThicknessM <= 0) return sample;
        if (double.IsNaN(_firstThicknessM)) { _firstThicknessM = sample.ThicknessM; return sample; }
        if (_thicknessWarned || Math.Abs(sample.ThicknessM - _firstThicknessM) < 1e-9) return sample;
        _thicknessWarned = true;
        return new(sample.Layers,
            [.. sample.Diagnostics, new FemValidationDiagnostic("plate_strip_mesh_thickness_varies",
                $"Толщина КЭ вдоль полосы различается ({_firstThicknessM * 1000:0.#} и {sample.ThicknessM * 1000:0.#} мм): " +
                "полоса считается по толщине фонового сечения.", false)],
            sample.RebarAxisDeg) { ThicknessM = sample.ThicknessM };
    }

    static bool Inside((double U, double V)[] poly, double u, double v)
    {
        bool inside = false;
        for (int i = 0, j = poly.Length - 1; i < poly.Length; j = i++)
        {
            var (ui, vi) = poly[i];
            var (uj, vj) = poly[j];
            if ((vi > v) != (vj > v) && u < (uj - ui) * (v - vi) / (vj - vi) + ui) inside = !inside;
        }
        return inside;
    }

    static double DistanceToBoundary((double U, double V)[] poly, double u, double v)
    {
        double best = double.MaxValue;
        for (int i = 0, j = poly.Length - 1; i < poly.Length; j = i++)
        {
            var (ai, bi) = poly[j];
            var (ci, di) = poly[i];
            double du = ci - ai, dv = di - bi;
            double len2 = du * du + dv * dv;
            double t = len2 > 0 ? Math.Clamp(((u - ai) * du + (v - bi) * dv) / len2, 0, 1) : 0;
            best = Math.Min(best, Math.Sqrt(Math.Pow(u - (ai + t * du), 2) + Math.Pow(v - (bi + t * dv), 2)));
        }
        return best;
    }
}
