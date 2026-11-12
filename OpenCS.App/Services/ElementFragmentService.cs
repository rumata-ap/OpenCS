using System.Globalization;
using System.Text.Json;
using CScore.Fem;
using CScore.Fem.Fragment;
using CScore.Planar;
using OpenCS.Utilites;

namespace OpenCS.Services;

/// <summary>
/// Выбор фрагмента вокруг стен и колонн: призма, перекрытия схемы и доступные крайние перекрытия i…k.
/// </summary>
/// <param name="Prism">Призма; null — отказ выбора (<see cref="Diagnostics"/>).</param>
/// <param name="MemberTags">Выбранные КонЭ.</param>
/// <param name="OffsetM">Отступ d, м.</param>
/// <param name="Levels">Перекрытия схемы (<see cref="StoreyFloorLevels.Find"/>); при отказе — пусто.</param>
/// <param name="Height">Доступные перекрытия, по умолчанию и «вся высота» (индексы в <see cref="Levels"/>).</param>
/// <param name="ElemZMin">Низ выбранных элементов, м.</param>
/// <param name="ElemZMax">Верх выбранных элементов, м.</param>
/// <param name="SchemaZMin">Низ схемы, м (край полосы, когда соседнего этажа нет).</param>
/// <param name="SchemaZMax">Верх схемы, м.</param>
public sealed record ElementFragmentSelection(
    FragmentPrism? Prism, IReadOnlyList<FemValidationDiagnostic> Diagnostics, IReadOnlyList<string> MemberTags, double OffsetM,
    IReadOnlyList<StoreyFloorLevel> Levels, ElementFragmentHeight Height,
    double ElemZMin, double ElemZMax, double SchemaZMin, double SchemaZMax)
{
    /// <summary>Есть отказ — фрагмент не строится.</summary>
    public bool HasErrors => Prism is null || Diagnostics.Any(d => d.IsError);

    /// <summary>Выбранные КонЭ-стержни (колонны): их сетка во фрагменте всегда строится заново
    /// (<see cref="StoreyFragmentRequest.MeshRebuildMemberTags"/>).</summary>
    public IReadOnlyList<string> BarTags { get; init; } = [];
}

/// <summary>
/// Сервис фрагмента вокруг стен и колонн (спека <c>2026-10-30-fem-element-fragment-prism-design.md</c>, разд. 4): призма по
/// выделению и перекрытия для диалога; запрос сборки по перекрытиям i…k или «вся высота». Сборка — как у этажного,
/// <see cref="StoreyFragmentService.Prepare"/>/<see cref="StoreyFragmentService.Commit"/>.
/// </summary>
public static class ElementFragmentService
{
    /// <summary>Отступ d по умолчанию, м.</summary>
    public const double DefaultOffsetM = 1.5;

    /// <summary>
    /// Призма по выбранным КонЭ <paramref name="memberTags"/> схемы <paramref name="schemaId"/> с отступом <paramref name="d"/>
    /// и перекрытия для выбора высоты. Перекрытия читаются по всей сетке схемы — вызывать в фоне.
    /// </summary>
    public static ElementFragmentSelection PrismFromSelection(DatabaseService db, int schemaId, IReadOnlyCollection<string> memberTags, double d) =>
        PrismFromSelection(db, schemaId, memberTags, d, null);

    /// <summary>
    /// То же с уже прочитанными перекрытиями схемы <paramref name="floors"/> (диалог пересчитывает призму при смене d без
    /// повторного чтения сетки); null — прочитать.
    /// </summary>
    public static ElementFragmentSelection PrismFromSelection(DatabaseService db, int schemaId, IReadOnlyCollection<string> memberTags, double d,
        StoreyFloorContext? floors)
    {
        ArgumentNullException.ThrowIfNull(db);
        ArgumentNullException.ThrowIfNull(memberTags);
        var tags = memberTags.Distinct(StringComparer.Ordinal).ToList();
        var parent = new StoreyFragmentParentSnapshot
        {
            Nodes = db.GetFemNodes(schemaId),
            Members = db.GetFemMembers(schemaId),
            Regions = db.GetPlanarRegions(schemaId),
            MeshElements = db.GetFemMeshBeamsOfMembers(schemaId, tags),
            SectionOutline = db.SectionOutlines(),
        };
        var prism = FragmentPrismBuilder.Build(parent, tags, d);
        var empty = new ElementFragmentHeight([], null, null, null, null);
        if (prism.Prism is null || prism.Diagnostics.Any(x => x.IsError))
            return new(null, prism.Diagnostics, tags, d, [], empty, 0, 0, 0, 0);

        var (zMin, zMax) = ElementZRange(parent, tags);
        floors ??= StoreyFloorService.Read(db, schemaId);
        var height = ElementFragmentLevels.Of(floors.Levels, prism.Prism, zMin, zMax);
        var bars = parent.Members.Where(m => m.ElemType == "beam").Select(m => m.ElemTag).ToHashSet(StringComparer.Ordinal);
        return new(prism.Prism, prism.Diagnostics, tags, d, floors.Levels, height, zMin, zMax, floors.ZMin, floors.ZMax)
        {
            BarTags = tags.Where(bars.Contains).ToList(),
        };
    }

    /// <summary>
    /// Делит КонЭ <paramref name="memberTags"/> на вертикальные колонны и стены (<see cref="FragmentPrismBuilder.IsVertical"/>) и
    /// остальные (нет в схеме — тоже в остальных). Порядок выбора сохраняется.
    /// </summary>
    public static (IReadOnlyList<string> Vertical, IReadOnlyList<string> Rejected) SplitVertical(
        IEnumerable<FemNode> nodes, IEnumerable<FemMember> members, IEnumerable<PlanarRegion> regions, IEnumerable<string> memberTags)
    {
        var nodeByTag = nodes.GroupBy(n => n.NodeTag).ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);
        var memberByTag = members.GroupBy(m => m.ElemTag).ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);
        var regionById = regions.Where(r => r.Id > 0).GroupBy(r => r.Id).ToDictionary(g => g.Key, g => g.First());
        var vertical = new List<string>();
        var rejected = new List<string>();
        foreach (var tag in memberTags.Distinct(StringComparer.Ordinal))
            (memberByTag.TryGetValue(tag, out var m) && FragmentPrismBuilder.IsVertical(m, nodeByTag, regionById) ? vertical : rejected).Add(tag);
        return (vertical, rejected);
    }

    /// <summary>Запрос сборки по перекрытиям <paramref name="i"/>…<paramref name="k"/> (индексы в <see cref="ElementFragmentSelection.Levels"/>).</summary>
    public static StoreyFragmentRequest Request(ElementFragmentSelection selection, int i, int k, string name)
    {
        var prism = RequirePrism(selection);
        var (z1, z2) = ElementFragmentLevels.Range(selection.Levels, i, k, selection.SchemaZMin, selection.SchemaZMax);
        var info = new FragmentPrismInfo(prism, selection.OffsetM, selection.MemberTags, selection.Levels[i].Z, selection.Levels[k].Z);
        return new StoreyFragmentRequest(z1, z2, name, Prism: prism, PrismInfo: info, MeshRebuildMemberTags: selection.BarTags);
    }

    /// <summary>
    /// Запрос «вся высота элементов»: перекрытия <see cref="ElementFragmentHeight.FullFrom"/>…<see cref="ElementFragmentHeight.FullTo"/>;
    /// перекрытий нет — низ и верх элементов с запасом.
    /// </summary>
    public static StoreyFragmentRequest FullHeightRequest(ElementFragmentSelection selection, string name)
    {
        var prism = RequirePrism(selection);
        if (selection.Height is { FullFrom: int i, FullTo: int k })
        {
            var r = Request(selection, i, k, name);
            return r with { PrismInfo = r.PrismInfo! with { FullHeight = true } };
        }
        var (z1, z2) = ElementFragmentLevels.FullHeightNoFloors(selection.ElemZMin, selection.ElemZMax);
        return new StoreyFragmentRequest(z1, z2, name, Prism: prism,
            PrismInfo: new FragmentPrismInfo(prism, selection.OffsetM, selection.MemberTags, FullHeight: true),
            MeshRebuildMemberTags: selection.BarTags);
    }

    static FragmentPrism RequirePrism(ElementFragmentSelection selection)
    {
        ArgumentNullException.ThrowIfNull(selection);
        return selection.HasErrors || selection.Prism is null
            ? throw new InvalidOperationException("Выбор фрагмента содержит отказы — запрос не строится.")
            : selection.Prism;
    }

    /// <summary>Низ и верх выбранных элементов: узлы стержней, вершины контуров областей в общих осях.</summary>
    static (double Min, double Max) ElementZRange(StoreyFragmentParentSnapshot parent, IReadOnlyCollection<string> tags)
    {
        var wanted = tags.ToHashSet(StringComparer.Ordinal);
        var nodes = parent.Nodes.GroupBy(n => n.NodeTag).ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);
        var regions = parent.Regions.Where(r => r.Id > 0).GroupBy(r => r.Id).ToDictionary(g => g.Key, g => g.First());
        var zs = new List<double>();
        foreach (var m in parent.Members.Where(m => wanted.Contains(m.ElemTag)))
        {
            if (m.PlanarRegionId is int id && regions.TryGetValue(id, out var region) && region.Hull is { } hull)
            {
                for (int i = 0; i < hull.X.Count; i++)
                    zs.Add(PlanarBoundaryFrameConverter.ToGlobalPoint(region.Frame, new PlanarVector3(hull.X[i], hull.Y[i], 0)).Z);
                continue;
            }
            long[] ids;
            try { ids = JsonSerializer.Deserialize<long[]>(m.NodeIdsJson) ?? []; }
            catch (JsonException) { continue; }
            foreach (var t in ids)
                if (nodes.TryGetValue(t.ToString(CultureInfo.InvariantCulture), out var n)) zs.Add(n.Z);
        }
        return zs.Count == 0 ? (0, 0) : (zs.Min(), zs.Max());
    }
}
