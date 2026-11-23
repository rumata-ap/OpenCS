using System.Text.Json.Nodes;
using CScore;
using OpenCS.Services;
using OpenCS.Utilites;

namespace OpenCS.Cli.Model;

/// <summary>
/// Раздел «sections»: произвольные и стадийные сечения проекта, ключ — обозначение (tag). Области — внутри сечения
/// (ключ — сечение и обозначение области); область пула, общая для нескольких сечений, выгружается в каждое и при
/// <c>apply</c> правится как один объект. Материалы — по обозначению, контуры — по ключу пула (<see cref="ContourJson.Key"/>);
/// ссылки разрешаются по документу и проекту. Запись — после материалов и контуров; сначала произвольные, затем
/// стадийные (части ссылаются на сечения).
/// </summary>
sealed class SectionsKind : IModelKind
{
    public string Name => "sections";
    public string SchemaResource => "OpenCS.Cli.Model.Schemas.sections.schema.json";

    public JsonArray Export(HeadlessProjectContext ctx, Action<string> warn)
    {
        var items = new JsonArray();
        var db = ctx.Db;
        var contourKeys = db.Contours.GroupBy(c => ContourJson.Key(c.GeometrySet, c.Tag))
            .Where(g => g.Count() == 1).ToDictionary(g => g.Single(), g => g.Key);
        var seen = new HashSet<string>();
        var exported = new HashSet<MaterialArea>();
        // Стадийные — после произвольных, как при записи: ссылки частей на сечения разрешаются по документу.
        foreach (var s in db.CrossSections.OrderBy(s => s is StagedSection ? 1 : 0).ThenBy(s => s.Num))
        {
            string key = s.Tag.Trim();
            if (!seen.Add(key))
            {
                warn(string.Format(Loc.S("CliSectionDuplicateSkipped"), key));
                continue;
            }
            var errors = CrossSectionAuthoring.Validate(s, db.Materials, db.CrossSections);
            if (errors.Count > 0)
            {
                warn(string.Format(Loc.S("CliSectionInvalidSkipped"), key, $"{errors[0].Path}: {errors[0].Message}"));
                continue;
            }
            items.Add(SectionJson.ToJson(s, db.Materials, db.CrossSections, c => contourKeys.GetValueOrDefault(c)));
            exported.UnionWith(CrossSectionAuthoring.AllOwnAreas(s));
        }
        int outside = db.MaterialAreas.Count(a => !exported.Contains(a) && !db.CrossSections.Any(s => CrossSectionAuthoring.AllOwnAreas(s).Contains(a)));
        if (outside > 0) warn(string.Format(Loc.S("CliAreasOutsideSectionsSkipped"), outside));
        return items;
    }

    /// <summary>Сечение документа с разрешённым целевым объектом проекта.</summary>
    sealed record Planned(SectionDoc Doc, CrossSection? Target);

    public IReadOnlyList<ModelChange> Plan(JsonArray items, JsonObject doc, HeadlessProjectContext ctx, ModelErrors errors)
    {
        var db = ctx.Db;
        var materials = PlanMaterials(doc, ctx);
        var contours = PlanContours(doc, ctx);
        var planned = new List<Planned>();
        var seen = new HashSet<string>();
        for (int i = 0; i < items.Count; i++)
        {
            string path = $"sections[{i}]";
            if (items[i] is not JsonObject item)
            {
                errors.Add(path, Loc.S("CliModelErrNotObject"));
                continue;
            }
            var d = SectionJson.Parse(item, path, errors);
            if (d == null) continue;
            if (!seen.Add(d.Tag))
            {
                errors.Add($"{path}.tag", string.Format(Loc.S("CliModelErrDuplicateKey"), d.Tag));
                continue;
            }
            var existing = db.CrossSections.Where(s => s.Tag.Trim() == d.Tag).ToList();
            if (existing.Count > 1)
            {
                errors.Add($"{path}.tag", string.Format(Loc.S("CliModelErrAmbiguousKey"), existing.Count, d.Tag));
                continue;
            }
            var target = existing.SingleOrDefault();
            if (target != null && (target is StagedSection) != d.IsStaged)
            {
                errors.Add(path, Loc.S("CliSectionErrKindChange"));
                continue;
            }
            planned.Add(new Planned(d, target));
        }

        // Сечения, на которые могут ссылаться части стадийных: документ ∪ проект (произвольные).
        var simpleInDoc = planned.Where(p => !p.Doc.IsStaged).Select(p => p.Doc.Tag).ToHashSet();
        var placeholders = new List<CrossSection>();
        int fakeId = -1;
        foreach (var tag in simpleInDoc)
            placeholders.Add(db.CrossSections.FirstOrDefault(s => s.Tag.Trim() == tag && s is not StagedSection)
                             ?? new CrossSection { Id = fakeId--, Tag = tag, Areas = [new MaterialArea { Category = AreaCategory.Region }] });
        var sectionsForCheck = db.CrossSections.Where(s => !simpleInDoc.Contains(s.Tag.Trim())).Concat(placeholders).ToList();

        var changes = new List<ModelChange>();
        foreach (var p in planned.OrderBy(p => p.Doc.IsStaged ? 1 : 0))
        {
            int before = errors.Count;
            CheckReferences(p.Doc, materials, contours, sectionsForCheck, errors);
            if (errors.Count > before) continue;
            // Проверка службой на черновике (материалы и контуры документа — временные объекты).
            var draft = Build(p.Doc, null, t => materials[t], k => contours[k], t => sectionsForCheck.First(s => s.Tag.Trim() == t));
            foreach (var e in CrossSectionAuthoring.Validate(draft, materials.Values.ToList(), sectionsForCheck))
                errors.Add(DocPath(p.Doc, e.Path), e.Message);
            if (errors.Count > before) continue;
            var target = p.Target;
            var d = p.Doc;
            changes.Add(new ModelChange(Name, d.Tag, target == null, c => Commit(c, d, target)));
        }
        return changes;
    }

    /// <summary>Путь ошибки службы в путь документа: «areas[1].polygon.outer[2]» → «sections[0].areas[1]…».</summary>
    static string DocPath(SectionDoc d, string servicePath)
    {
        // У области со ссылкой на контур пула вершины проверял раздел contours — указать на ссылку.
        string path = $"{d.Path}.{servicePath}";
        foreach (var a in d.OwnAreas.Where(a => a.Contour != null))
            if (path.StartsWith($"{a.Path}.polygon.outer", StringComparison.Ordinal))
                return $"{a.Path}.contour";
        return path;
    }

    /// <summary>Материалы проекта и документа по обозначению (документ поверх проекта; новые — с временным Id &lt; 0).</summary>
    static Dictionary<string, Material> PlanMaterials(JsonObject doc, HeadlessProjectContext ctx)
    {
        var map = new Dictionary<string, Material>();
        foreach (var g in ctx.Materials.GroupBy(m => m.Tag.Trim()))
            if (g.Count() == 1) map[g.Key] = g.Single();
        if (doc["materials"] is JsonArray arr)
        {
            int fake = -1000;
            for (int i = 0; i < arr.Count; i++)
                if (arr[i] is JsonObject o && MaterialJson.FromJson(o, "", new ModelErrors(), out _) is { } m)
                {
                    var tag = m.Tag.Trim();
                    if (map.TryGetValue(tag, out var old)) m.Id = old.Id;
                    else m.Id = fake--;
                    map[tag] = m;
                }
        }
        return map;
    }

    /// <summary>Контуры пула по ключу: проект и документ (документ поверх проекта).</summary>
    static Dictionary<string, Contour> PlanContours(JsonObject doc, HeadlessProjectContext ctx)
    {
        var map = new Dictionary<string, Contour>();
        foreach (var g in ctx.Db.Contours.GroupBy(c => ContourJson.Key(c.GeometrySet, c.Tag)))
            if (g.Count() == 1) map[g.Key] = g.Single();
        if (doc["contours"] is JsonArray arr)
            for (int i = 0; i < arr.Count; i++)
                if (arr[i] is JsonObject o && ContourJson.FromJson(o, "", new ModelErrors()) is { } c)
                {
                    c.PointsToXYs();
                    c.SetWKT();
                    map[ContourJson.Key(c.GeometrySet, c.Tag)] = c;
                }
        return map;
    }

    static void CheckReferences(SectionDoc d, Dictionary<string, Material> materials, Dictionary<string, Contour> contours,
                                List<CrossSection> sections, ModelErrors errors)
    {
        void Areas(IReadOnlyList<AreaDoc> areas)
        {
            foreach (var a in areas)
            {
                if (!materials.ContainsKey(a.Material))
                    errors.Add($"{a.Path}.material", string.Format(Loc.S("CliSectionErrUnknownMaterial"), a.Material));
                foreach (var g in a.Stirrups.Where(g => g.Material != null && !materials.ContainsKey(g.Material)))
                    errors.Add($"{g.Path}.material", string.Format(Loc.S("CliSectionErrUnknownMaterial"), g.Material));
                if (a.Category == AreaCategory.Region && a.Stirrups.Any(g => g.Material == null))
                    errors.Add($"{a.Stirrups.First(g => g.Material == null).Path}.material", Loc.S("CliModelErrRequired"));
                if (a.Contour != null && !contours.ContainsKey(a.Contour))
                    errors.Add($"{a.Path}.contour", string.Format(Loc.S("CliSectionErrUnknownContour"), a.Contour));
                if (a.Host != null && !areas.Any(h => h.Tag == a.Host))
                    errors.Add($"{a.Path}.host", string.Format(Loc.S("SectionErrHostNotInSection"), a.Host));
            }
        }
        if (d.Areas != null) Areas(d.Areas);
        foreach (var p in d.Parts ?? [])
        {
            Areas(p.Areas);
            if (p.Section != null && !sections.Any(s => s.Tag.Trim() == p.Section && s is not StagedSection))
                errors.Add($"{p.Path}.section", string.Format(Loc.S("CliSectionErrUnknownSection"), p.Section));
        }
    }

    /// <summary>
    /// Сечение из документа: новое или существующее <paramref name="target"/> (тот же объект и Id). Области с тем же
    /// обозначением правятся на месте (тот же объект пула), прочие создаются.
    /// </summary>
    static CrossSection Build(SectionDoc d, CrossSection? target, Func<string, Material?> material,
                              Func<string, Contour?> contour, Func<string, CrossSection> section)
    {
        var old = target == null ? [] : CrossSectionAuthoring.AllOwnAreas(target).ToList();
        List<MaterialArea> Areas(IReadOnlyList<AreaDoc> docs)
        {
            var list = docs.Select(a =>
            {
                var area = old.FirstOrDefault(o => o.Tag == a.Tag) ?? new MaterialArea();
                SectionJson.Fill(area, a, material, contour);
                return area;
            }).ToList();
            SectionJson.ResolveHosts(list, docs);
            return list;
        }

        CrossSection s;
        if (d.IsStaged)
        {
            var st = target as StagedSection ?? new StagedSection();
            st.Parts = d.Parts!.Select((p, k) =>
            {
                var part = new SectionPart { Index = k, Tag = p.Tag };
                if (p.Section != null)
                {
                    var src = section(p.Section);
                    part.SourceSectionId = src.Id;
                    part.Areas = src.Areas;
                }
                else part.Areas = Areas(p.Areas);
                return part;
            }).ToList();
            st.SyncAreas();
            s = st;
        }
        else
        {
            s = target ?? new CrossSection();
            s.Areas = Areas(d.Areas!);
        }
        s.Tag = d.Tag;
        s.Description = d.Description;
        return s;
    }

    static void Commit(HeadlessProjectContext ctx, SectionDoc d, CrossSection? target)
    {
        var db = ctx.Db;
        Material? material(string tag) => db.Materials.FirstOrDefault(m => m.Tag.Trim() == tag);
        Contour? contour(string key) => db.Contours.FirstOrDefault(c => ContourJson.Key(c.GeometrySet, c.Tag) == key);
        CrossSection section(string tag) => db.CrossSections.First(s => s.Tag.Trim() == tag && s is not StagedSection);
        var old = target == null ? [] : CrossSectionAuthoring.AllOwnAreas(target).ToList();
        var s = Build(d, target, material, contour, section);
        var areas = CrossSectionAuthoring.AllOwnAreas(s).ToList();
        var errors = CrossSectionAuthoring.Save(db, s, ctx.CalcSettings, areas);
        if (errors.Count > 0)   // не должно случиться: Plan проверил тем же Validate
            throw new CliException(CliExitCode.Failed, string.Join("; ", errors.Select(e => $"{d.Path}.{e.Path}: {e.Message}")));
        // Сетка волокон: у области со сменённой геометрией она сброшена в памяти — переписать строки базы.
        foreach (var a in areas.Where(a => a.Id > 0)) db.SaveMeshFibers(a);
        // Области, убранные из сечения и больше нигде не используемые, удаляются (как пересоздание состава).
        foreach (var a in old.Where(a => !areas.Contains(a)))
            CrossSectionAuthoring.TryDeleteArea(db, a, out _);
    }
}
