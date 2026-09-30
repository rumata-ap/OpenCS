using CScore.Planar;
using CScore.PlateRebar;

namespace CScore.Fem;

/// <summary>Раскладка армирования OpenCS на одном КЭ: слои в осях КЭ либо причина, по которой её нет.</summary>
/// <param name="Layers">Слои армирования КЭ (фон + зоны) в осях КЭ; null — раскладки нет.</param>
/// <param name="ThicknessM">Толщина КЭ, м.</param>
/// <param name="Background">Сечение, слои которого служат фоном (сечение конструктивного элемента КЭ).</param>
/// <param name="MemberTag">Конструктивный элемент КЭ.</param>
/// <param name="Label">Подпись: «Раскладка: фон + Зона 1».</param>
/// <param name="Reason">Почему раскладки нет (при <paramref name="Layers"/> == null).</param>
/// <param name="Mirrored">Нормаль КЭ направлена против нормали элемента — слои отражены по толщине.</param>
public sealed record PlateLayoutResolution(
    IReadOnlyList<PlateRebarLayer>? Layers, double ThicknessM, PlateSection? Background, string MemberTag,
    string Label, string? Reason, bool Mirrored)
{
    /// <summary>Оси выдачи усилий КЭ известны (у КЭ задан угол согласования местных осей).</summary>
    public bool AxesKnown { get; init; }

    /// <summary>
    /// Угол, град, оси x выдачи усилий КЭ относительно оси x раскладки (против часовой стрелки, если смотреть
    /// с конца нормали КЭ); 0 — оси совпадают (или противоположны) либо неизвестны.
    /// </summary>
    public double ForceAngleDeg { get; init; }
}

/// <summary>
/// Раскладка армирования OpenCS на КЭ сетки: фон (слои сечения конструктивного элемента) плюс зоны его
/// <see cref="PlanarRegion"/>, выбранные по центроиду КЭ в локальной плоскости элемента.
/// Оси раскладки — оси элемента. Ось выдачи усилий КЭ — направление «узел 1 → узел 2», повёрнутое на угол
/// согласования местных осей (<see cref="FemElement.LocalAxisAngleDeg"/>); если она не лежит вдоль оси x
/// элемента, усилия КЭ нужно повернуть в оси раскладки (<see cref="PlateLayoutResolution.ForceAngleDeg"/>).
/// Без угла согласования оси выдачи считаются совпадающими с осями элемента. У КЭ с нормалью против
/// <see cref="Frame3D.LocalZ"/> элемента грани «плюс» и «минус» усилий обратны граням раскладки, и слои
/// отражаются по толщине.
/// </summary>
public sealed class PlateLayoutResolver
{
    const string LabelPrefix = "Раскладка: ";
    /// <summary>|sin| угла между осью выдачи и осью раскладки, ниже которого оси считаются совпадающими.</summary>
    const double AlignedSinTolerance = 1e-4;

    readonly Dictionary<string, FemMember> _members = new(StringComparer.Ordinal);
    readonly Dictionary<int, PlanarRegion> _regions = [];
    readonly Dictionary<string, FemElement> _elements = new(StringComparer.Ordinal);
    readonly Dictionary<string, FemMeshNode> _importedNodes = new(StringComparer.Ordinal);
    readonly Dictionary<string, FemMeshNode> _generatedNodes = new(StringComparer.Ordinal);
    readonly Func<int, PlateSection?> _sectionById;
    readonly PlateSection? _fallbackSection;

    readonly Dictionary<string, PlateLayoutResolution> _byElement = new(StringComparer.Ordinal);
    // КЭ тысячи, сочетаний «элемент + набор зон» десятки: слои собираются один раз на сочетание.
    readonly Dictionary<(PlanarRegion, PlateSection, double, bool, string), PlateLayoutResolution> _byCombination = [];

    /// <param name="members">Конструктивные элементы схемы.</param>
    /// <param name="regions">Плоские регионы схемы (зоны армирования и локальные оси).</param>
    /// <param name="sectionById">Пластинчатое сечение по id.</param>
    /// <param name="meshElements">КЭ сетки схемы.</param>
    /// <param name="meshNodes">Узлы сетки схемы.</param>
    /// <param name="fallbackSection">Сечение-фон для элементов без своего сечения (сечение цели проверки).</param>
    public PlateLayoutResolver(
        IEnumerable<FemMember> members, IEnumerable<PlanarRegion> regions, Func<int, PlateSection?> sectionById,
        IEnumerable<FemElement> meshElements, IEnumerable<FemMeshNode> meshNodes, PlateSection? fallbackSection = null)
    {
        _sectionById = sectionById;
        _fallbackSection = fallbackSection;
        foreach (var r in regions) _regions.TryAdd(r.Id, r);
        foreach (var m in members)
            if (m.PlanarRegionId is int id && _regions.ContainsKey(id)) _members.TryAdd(m.ElemTag, m);
        foreach (var e in meshElements)
            if (e.ElemType == "shell") _elements.TryAdd(e.ElemTag.Trim(), e);
        // Теги узлов импортированной и сгенерированной сеток могут совпадать — узел берётся по происхождению КЭ.
        foreach (var n in meshNodes)
            (n.Origin == FemMember.MeshSourceImported ? _importedNodes : _generatedNodes).TryAdd(n.NodeTag, n);
    }

    /// <summary>В схеме есть КЭ, принадлежащие плоским конструктивным элементам (раскладку есть куда наложить).</summary>
    public bool HasElements => _elements.Values.Any(Covers);

    /// <summary>КЭ принадлежит конструктивному элементу с плоским регионом.</summary>
    public bool Covers(FemElement element) =>
        element.ElemType == "shell" && element.SourceMemberTag is { } tag && _members.ContainsKey(tag);

    /// <summary>Раскладка на КЭ по его тегу.</summary>
    public PlateLayoutResolution Resolve(string elemTag)
    {
        elemTag = elemTag.Trim();
        if (!_byElement.TryGetValue(elemTag, out var r))
            _byElement[elemTag] = r = Compute(elemTag);
        return r;
    }

    PlateLayoutResolution Compute(string elemTag)
    {
        if (!_elements.TryGetValue(elemTag, out var element))
            return Missing("КЭ нет в сетке схемы");
        if (element.SourceMemberTag is not { } memberTag || !_members.TryGetValue(memberTag, out var member))
            return Missing("КЭ не принадлежит плоскому конструктивному элементу");
        var region = _regions[member.PlanarRegionId!.Value];

        var background = (member.PlateSectionId is int sid ? _sectionById(sid) : null) ?? _fallbackSection;
        if (background == null)
            return Missing($"у элемента «{member.ElemTag}» не задано сечение пластины", member.ElemTag);
        if (!TryGeometry(element, out var centroid, out var normal, out var nodeAxis))
            return Missing("у КЭ нет узлов в сетке схемы", member.ElemTag);

        var local = PlanarBoundaryFrameConverter.ToLocalPoint(region.Frame, centroid);
        var resolved = PlateRebarFieldResolver.Resolve(
            new PlateRebarField(background.RebarLayers, region.RebarZones), local.X, local.Y);
        if (resolved.Diagnostics.Count > 0)
            return Missing("в точке КЭ перекрываются зоны с одинаковым приоритетом", member.ElemTag);

        bool mirrored = normal.Dot(region.Frame.LocalZ) < 0;
        double h = element.ThicknessM is > 0 and var own ? own : background.H;
        string zonesKey = string.Join(',', resolved.AppliedZones.Select(z => region.RebarZones.IndexOf(z)));
        var key = (region, background, h, mirrored, zonesKey);
        if (!_byCombination.TryGetValue(key, out var result))
            _byCombination[key] = result = Build(resolved, background, member.ElemTag, h, mirrored);
        if (element.LocalAxisAngleDeg is not double axisAngle)
            return result;

        // Ось выдачи усилий: узловая ось, повёрнутая на угол согласования вокруг нормали КЭ.
        double a = axisAngle * Math.PI / 180.0;
        var outputX = nodeAxis * Math.Cos(a) + normal.Cross(nodeAxis) * Math.Sin(a);
        // Оси проверки: x раскладки и y, дополняющая её до правой тройки с нормалью КЭ.
        var checkY = mirrored ? region.Frame.LocalY * -1 : region.Frame.LocalY;
        double sin = outputX.Dot(checkY), cos = outputX.Dot(region.Frame.LocalX);
        return result with
        {
            AxesKnown = true,
            ForceAngleDeg = Math.Abs(sin) < AlignedSinTolerance ? 0 : Math.Atan2(sin, cos) * 180.0 / Math.PI,
        };
    }

    static PlateLayoutResolution Build(
        ResolvedRebarLayout resolved, PlateSection background, string memberTag, double h, bool mirrored)
    {
        string label = LabelPrefix + (resolved.AppliedZones.Count == 0
            ? "фон"
            : "фон + " + string.Join(", ", resolved.AppliedZones.Select(z => z.Name)));
        PlateLayoutResolution Fail(string reason) => new(null, h, background, memberTag, label, reason, mirrored);

        var zoneByLayout = new Dictionary<PlateRebarLayer, RebarZone>(ReferenceEqualityComparer.Instance);
        foreach (var zone in resolved.AppliedZones) zoneByLayout[zone.Layout] = zone;

        var layers = new List<PlateRebarLayer>(resolved.Layers.Count);
        foreach (var source in resolved.Layers)
        {
            if (source.Asx <= 0 && source.Asy <= 0) continue;
            zoneByLayout.TryGetValue(source, out var zone);
            string owner = zone != null ? $"зоне «{zone.Name}»" : $"слое «{source.Name}» сечения «{background.Tag}»";
            // Проверки пластин считают арматуру вдоль осей элемента; поворот усилий в оси арматуры не выполняется.
            if (Math.Abs(source.Angle) > 1e-9)
                return Fail($"арматура в {owner} задана под углом к осям элемента — проверка по КЭ её не поддерживает");

            var layer = source.Clone();
            if (zone != null)
            {
                // Грань зоны задана явно, отметка слоя — вручную: расхождение дало бы арматуру не у той грани.
                int sign = zone.Face == RebarFace.PlusN ? +1 : -1;
                if ((layer.Asx > 0 && Math.Sign(layer.Zsx) != sign) || (layer.Asy > 0 && Math.Sign(layer.Zsy) != sign))
                    return Fail($"в {owner} отметка арматуры z не соответствует грани зоны");
                layer.Face = zone.Face;
                if (layer.Name.Length == 0) layer.Name = zone.Name;
            }

            // Расстояние от грани сохраняется при толщине КЭ, отличной от толщины сечения.
            layer.Zsx = Shift(layer.Zsx, background.H, h);
            layer.Zsy = Shift(layer.Zsy, background.H, h);
            if (mirrored)
            {
                layer.Zsx = -layer.Zsx;
                layer.Zsy = -layer.Zsy;
                layer.Face = layer.Face == RebarFace.PlusN ? RebarFace.MinusN : RebarFace.PlusN;
            }
            layers.Add(layer);
        }

        return layers.Count == 0
            ? Fail("в раскладке нет арматуры")
            : new PlateLayoutResolution(layers, h, background, memberTag, label, null, mirrored);
    }

    static double Shift(double z, double sectionH, double elementH) =>
        z == 0 || sectionH == elementH ? z : Math.Sign(z) * (elementH / 2.0 - (sectionH / 2.0 - Math.Abs(z)));

    static PlateLayoutResolution Missing(string reason, string memberTag = "") =>
        new(null, 0, null, memberTag, "", reason, false);

    /// <summary>
    /// Центроид (среднее узлов), нормаль и узловая ось («узел 1 → узел 2») КЭ. Нормаль — по узлам 1, 2, 3:
    /// у четырёхузлового КЭ третий узел лежит по ту же сторону от ребра 1–2 и при обходе «1 2 3 4»,
    /// и при обходе ЛИРЫ/SCAD «1 2 4 3».
    /// </summary>
    bool TryGeometry(FemElement element, out PlanarVector3 centroid, out PlanarVector3 normal, out PlanarVector3 nodeAxis)
    {
        centroid = normal = nodeAxis = PlanarVector3.Zero;
        if (FemMeshTopology.ReadNodeTags(element) is not { } tags) return false;
        var (first, second) = element.Origin == FemMember.MeshSourceImported
            ? (_importedNodes, _generatedNodes) : (_generatedNodes, _importedNodes);

        var points = new List<PlanarVector3>(4);
        foreach (string tag in tags.Distinct())
        {
            if (!first.TryGetValue(tag, out var n) && !second.TryGetValue(tag, out n)) return false;
            points.Add(new PlanarVector3(n.X, n.Y, n.Z));
        }
        if (points.Count < 3) return false;

        var sum = PlanarVector3.Zero;
        foreach (var p in points) sum += p;
        centroid = sum * (1.0 / points.Count);

        for (int i = 2; i < points.Count; i++)
        {
            var cross = (points[1] - points[0]).Cross(points[i] - points[0]);
            if (cross.Length < 1e-12) continue;
            normal = cross.Normalize();
            nodeAxis = (points[1] - points[0]).Normalize();
            return true;
        }
        return false;
    }
}

/// <summary>
/// Источник <see cref="FemCheckRebarSource.Layout"/>: сечение КЭ из раскладки армирования OpenCS —
/// фон (слои сечения конструктивного элемента) плюс зоны его региона по центроиду КЭ.
/// </summary>
public sealed class LayoutPlateSectionSource(PlateSection template, PlateLayoutResolver resolver) : IPlateElementSectionSource
{
    readonly Dictionary<PlateSection, PlateElementSectionFactory> _factories = new(ReferenceEqualityComparer.Instance);

    /// <inheritdoc/>
    public string Key => FemCheckRebarSource.Layout;

    /// <inheritdoc/>
    public PlateElementSection Resolve(FemCheckScopeElement element)
    {
        var r = resolver.Resolve(element.Element.ElemTag);
        if (r.Layers == null)
            return PlateElementSection.Missing(r.Reason ?? "нет раскладки", r.Label);

        var background = r.Background!;
        if (!_factories.TryGetValue(background, out var factory))
            _factories[background] = factory = new PlateElementSectionFactory(background);
        var (section, key) = factory.Get(r.ThicknessM, r.Layers as List<PlateRebarLayer> ?? [.. r.Layers]);
        // Одинаковые слои у разных сечений-фонов (другой бетон, модель) — разные сечения КЭ.
        return new PlateElementSection(section, r.Label, ReferenceEquals(background, template) ? key : $"{background.Id}:{key}", null)
        {
            ForceAngleDeg = r.ForceAngleDeg,
        };
    }

    /// <inheritdoc/>
    public IReadOnlyList<string> Warnings(IReadOnlyList<FemCheckScopeElement> elements, Material? concrete, Material? rebar)
    {
        int mirrored = 0, rotated = 0, unknownAxes = 0;
        double maxAngle = 0;
        bool ownRebarMaterial = false;
        var otherMaterials = new SortedSet<string>(StringComparer.Ordinal);
        foreach (var e in elements)
        {
            var r = resolver.Resolve(e.Element.ElemTag);
            if (r.Layers == null || r.Background is not { } background) continue;
            if (r.Mirrored) mirrored++;
            if (!r.AxesKnown) unknownAxes++;
            if (r.ForceAngleDeg != 0)
            {
                rotated++;
                // Разворот на 180° усилий не меняет — в сообщении угол отклонения от оси раскладки.
                double deviation = Math.Abs(r.ForceAngleDeg);
                maxAngle = Math.Max(maxAngle, Math.Min(deviation, 180 - deviation));
            }
            ownRebarMaterial |= r.Layers.Any(l => l.MaterialId != 0 && l.MaterialId != background.RebarMaterialId);
            if (background.ConcreteMaterialId != template.ConcreteMaterialId || background.RebarMaterialId != template.RebarMaterialId)
                otherMaterials.Add($"«{r.MemberTag}» (сечение «{background.Tag}»)");
        }

        var warnings = new List<string>();
        if (rotated > 0)
            warnings.Add($"У {rotated} КЭ оси выдачи усилий не совпадают с осями конструктивного элемента (отклонение до " +
                         $"{maxAngle:0.#}°) — усилия этих КЭ повёрнуты в оси раскладки.");
        if (unknownAxes > 0)
            warnings.Add($"У {unknownAxes} КЭ оси выдачи усилий неизвестны (угол согласования местных осей пластин не " +
                         "импортирован) — принято, что они совпадают с осями конструктивного элемента.");
        if (mirrored > 0)
            warnings.Add($"У {mirrored} КЭ нормаль направлена против нормали конструктивного элемента — раскладка для них " +
                         "взята зеркально (армирование граней поменяно местами).");
        if (ownRebarMaterial)
            warnings.Add("В раскладке есть слои со своим материалом арматуры — расчёт выполнен по материалу арматуры сечения.");
        if (otherMaterials.Count > 0)
            warnings.Add($"Материалы сечения элементов {string.Join(", ", otherMaterials)} отличаются от материалов сечения цели " +
                         $"«{template.Tag}» — расчёт выполнен по материалам сечения цели.");
        return warnings;
    }
}

/// <summary>
/// Раскладка армирования OpenCS как источник мозаики: суммарная площадь слоёв грани по направлению, см²/м,
/// в осях и гранях КЭ (Z− — «нижняя»), как у подобранного и заданного армирования программы-источника.
/// </summary>
public sealed class PlateLayoutRebarSource(PlateLayoutResolver resolver) : IPlateRebarFieldSource
{
    /// <inheritdoc/>
    public bool Supports(PlateRebarMosaicComponent component) => component != PlateRebarMosaicComponent.Transverse;

    /// <inheritdoc/>
    public PlateRebarValue Get(string elemTag, PlateRebarMosaicComponent component)
    {
        if (!Supports(component) || resolver.Resolve(elemTag).Layers is not { } layers)
            return PlateRebarValue.Missing;
        double area = component switch
        {
            PlateRebarMosaicComponent.BottomX => layers.Where(l => l.Zsx < 0).Sum(l => l.Asx),
            PlateRebarMosaicComponent.TopX => layers.Where(l => l.Zsx > 0).Sum(l => l.Asx),
            PlateRebarMosaicComponent.BottomY => layers.Where(l => l.Zsy < 0).Sum(l => l.Asy),
            _ => layers.Where(l => l.Zsy > 0).Sum(l => l.Asy),
        };
        return PlateRebarValue.Of(area * 1e4);
    }
}
