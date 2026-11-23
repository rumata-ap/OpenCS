using System.Text.Json.Nodes;
using CScore;
using CScore.PlateRebar;
using OpenCS.Services;
using OpenCS.Utilites;

namespace OpenCS.Cli.Model;

/// <summary>
/// Раздел «plateSections»: сечения пластин проекта, ключ — обозначение (tag). Толщина, Ø, шаг, z — мм, площадь слоя —
/// мм²/м; материалы — по обозначению (документ ∪ проект). Сечения, созданные плоской областью схемы МКЭ
/// (<see cref="PlateSection.GeneratedForRegionId"/>), не выгружаются и через <c>apply</c> не правятся (решение
/// пользователя 23.11): их пересоздаёт область при сохранении. Запись — после материалов; Id существующего сечения
/// сохраняется (на него ссылаются задачи, КонЭ и группы КЭ).
/// </summary>
sealed class PlateSectionsKind : IModelKind
{
    public string Name => "plateSections";
    public string SchemaResource => "OpenCS.Cli.Model.Schemas.plateSections.schema.json";

    const double MmPerM = 1000;
    static double Mm(double m) => ContourJson.Mm(m);
    static double Mm2PerM(double m2) => Math.Round(m2 * 1e6, 6) + 0.0;

    static readonly (string Json, string Value)[] Modes =
        [("spacing", "diameter_spacing"), ("count", "diameter_count"), ("area", "direct")];
    static readonly (string Json, string Value)[] Models =
        [("layered", "layered"), ("char1dPrincipal", "char1d_principal"), ("char1dAxial", "char1d_axial")];
    static readonly (string Json, string Value)[] Softenings = [("none", ""), ("vecchioCollins", "vecchio_collins")];
    static readonly (string Json, RebarFace Value)[] Faces = [("plus", RebarFace.PlusN), ("minus", RebarFace.MinusN)];

    /// <summary>Диаграммы бетона: подмножество обозначений раздела sections.</summary>
    static readonly (string Json, DiagrammType Value)[] Diagrams =
        SectionJson.Diagrams.Where(d => DiagrammCompatibility.Allowed(MatType.Concrete).Contains(d.Value)).ToArray();

    static string JsonOf<T>((string Json, T Value)[] options, T value, string fallback) =>
        options.FirstOrDefault(o => EqualityComparer<T>.Default.Equals(o.Value, value)).Json ?? fallback;

    public JsonArray Export(HeadlessProjectContext ctx, Action<string> warn)
    {
        var items = new JsonArray();
        var db = ctx.Db;
        var seen = new HashSet<string>();
        foreach (var ps in db.PlateSections.OrderBy(p => p.Num))
        {
            string key = ps.Tag.Trim();
            if (ps.GeneratedForRegionId != null)
            {
                warn(string.Format(Loc.S("CliPlateSectionGeneratedSkipped"), key));
                continue;
            }
            if (!seen.Add(key))
            {
                warn(string.Format(Loc.S("CliPlateSectionDuplicateSkipped"), key));
                continue;
            }
            var errors = PlateSectionAuthoring.Validate(ps, db.Materials);
            if (errors.Count > 0)
            {
                warn(string.Format(Loc.S("CliPlateSectionInvalidSkipped"), key, $"{errors[0].Path}: {errors[0].Message}"));
                continue;
            }
            items.Add(ToJson(ps, db.Materials));
        }
        return items;
    }

    static JsonObject ToJson(PlateSection ps, IReadOnlyList<Material> materials)
    {
        string Tag(int id) => materials.First(m => m.Id == id).Tag.Trim();
        string model = string.IsNullOrEmpty(ps.PlateModel) ? "layered" : ps.PlateModel;
        var o = new JsonObject
        {
            ["tag"] = ps.Tag.Trim(),
            ["h"] = Mm(ps.H),
            ["concrete"] = Tag(ps.ConcreteMaterialId),
            ["rebar"] = Tag(ps.RebarMaterialId),
            ["diagram"] = JsonOf(Diagrams, ps.ConcreteDiagramType, "l3"),
            ["model"] = JsonOf(Models, model, "layered"),
            ["nLayers"] = ps.NLayers,
            ["tension"] = ps.TensionConcrete,
        };
        if (!string.IsNullOrEmpty(ps.SofteningModel))
        {
            o["softening"] = JsonOf(Softenings, ps.SofteningModel, "none");
            o["epsC2"] = ps.SofteningEpsC2;
        }
        var layers = new JsonArray();
        foreach (var l in ps.RebarLayers)
        {
            var lo = new JsonObject { ["name"] = l.Name, ["mode"] = JsonOf(Modes, l.InputMode, "spacing") };
            if (Direction(l.InputMode, l.DiameterX, l.SpacingX, l.CountPerMeterX, l.Asx, l.Zsx) is { } x) lo["x"] = x;
            if (Direction(l.InputMode, l.DiameterY, l.SpacingY, l.CountPerMeterY, l.Asy, l.Zsy) is { } y) lo["y"] = y;
            if (l.MaterialId != 0) lo["material"] = Tag(l.MaterialId);
            if (l.Face != RebarFace.PlusN) lo["face"] = JsonOf(Faces, l.Face, "plus");
            if (l.Angle != 0) lo["angle"] = l.Angle;
            layers.Add(lo);
        }
        o["rebarLayers"] = layers;
        return o;
    }

    /// <summary>Направление слоя для документа; null — без арматуры (не выгружается).</summary>
    static JsonObject? Direction(string mode, double d, double spacing, double count, double area, double z)
    {
        switch (mode)
        {
            case "direct":
                return area > 0 ? new JsonObject { ["area"] = Mm2PerM(area), ["z"] = Mm(z) } : null;
            case "diameter_count":
                return d == 0 && count == 0 ? null : new JsonObject { ["d"] = Mm(d), ["count"] = count, ["z"] = Mm(z) };
            default:
                return d == 0 && spacing == 0 ? null : new JsonObject { ["d"] = Mm(d), ["step"] = Mm(spacing), ["z"] = Mm(z) };
        }
    }

    /// <summary>Сечение документа: поля в единицах базы, материалы — обозначениями.</summary>
    sealed record PlateDoc(string Path, PlateSection Section, string Concrete, string Rebar, List<(string Path, string Tag, int Layer)> LayerMaterials);

    static PlateDoc? Parse(JsonObject o, string path, ModelErrors errors)
    {
        int before = errors.Count;
        string? tag = SectionJson.Str(o, "tag", path, errors, required: true);
        if (tag != null && tag.Length == 0) errors.Add($"{path}.tag", Loc.S("SectionErrTagEmpty"));
        double? h = SectionJson.Num(o, "h", path, errors, required: true);
        string? concrete = SectionJson.Str(o, "concrete", path, errors, required: true);
        string? rebar = SectionJson.Str(o, "rebar", path, errors, required: true);
        var ps = new PlateSection
        {
            Tag = tag ?? "",
            H = (h ?? 0) / MmPerM,
            ConcreteDiagramType = SectionJson.Pick(o, "diagram", Diagrams, DiagrammType.L3, path, errors),
            PlateModel = SectionJson.Pick(o, "model", Models, "layered", path, errors),
            NLayers = SectionJson.Int(o, "nLayers", path, errors) ?? 10,
            TensionConcrete = SectionJson.Bool(o, "tension", path, errors) ?? false,
            SofteningModel = SectionJson.Pick(o, "softening", Softenings, "", path, errors),
            SofteningEpsC2 = SectionJson.Num(o, "epsC2", path, errors) ?? 0.002,
        };
        var layerMaterials = new List<(string, string, int)>();
        if (o["rebarLayers"] is JsonArray arr)
        {
            for (int i = 0; i < arr.Count; i++)
            {
                string lp = $"{path}.rebarLayers[{i}]";
                if (arr[i] is not JsonObject lo)
                {
                    errors.Add(lp, Loc.S("CliModelErrNotObject"));
                    continue;
                }
                var l = new PlateRebarLayer
                {
                    Name = SectionJson.Str(lo, "name", lp, errors) ?? "",
                    InputMode = SectionJson.Pick(lo, "mode", Modes, "diameter_spacing", lp, errors),
                    Face = SectionJson.Pick(lo, "face", Faces, RebarFace.PlusN, lp, errors),
                    Angle = SectionJson.Num(lo, "angle", lp, errors) ?? 0,
                };
                ParseDirection(lo, "x", l, lp, errors);
                ParseDirection(lo, "y", l, lp, errors);
                if (SectionJson.Str(lo, "material", lp, errors) is { Length: > 0 } m) layerMaterials.Add(($"{lp}.material", m, ps.RebarLayers.Count));
                l.RecalcArea();
                ps.RebarLayers.Add(l);
            }
        }
        else if (o["rebarLayers"] is not null)
            errors.Add($"{path}.rebarLayers", Loc.S("CliModelErrNotArray"));
        return errors.Count > before ? null : new PlateDoc(path, ps, concrete!, rebar!, layerMaterials);
    }

    static void ParseDirection(JsonObject lo, string key, PlateRebarLayer l, string lp, ModelErrors errors)
    {
        if (lo[key] is null) return;   // направление без арматуры
        string dp = $"{lp}.{key}";
        if (lo[key] is not JsonObject d)
        {
            errors.Add(dp, Loc.S("CliModelErrNotObject"));
            return;
        }
        double z = (SectionJson.Num(d, "z", dp, errors, required: true) ?? 0) / MmPerM;
        double dia = 0, step = 0, count = 0, area = 0;
        switch (l.InputMode)
        {
            case "direct":
                area = (SectionJson.Num(d, "area", dp, errors, required: true) ?? 0) / 1e6;
                break;
            case "diameter_count":
                dia = (SectionJson.Num(d, "d", dp, errors, required: true) ?? 0) / MmPerM;
                count = SectionJson.Num(d, "count", dp, errors, required: true) ?? 0;
                break;
            default:
                dia = (SectionJson.Num(d, "d", dp, errors, required: true) ?? 0) / MmPerM;
                step = (SectionJson.Num(d, "step", dp, errors, required: true) ?? 0) / MmPerM;
                break;
        }
        if (key == "x") { l.Zsx = z; l.DiameterX = dia; l.SpacingX = step; l.CountPerMeterX = count; l.Asx = area; }
        else { l.Zsy = z; l.DiameterY = dia; l.SpacingY = step; l.CountPerMeterY = count; l.Asy = area; }
    }

    public IReadOnlyList<ModelChange> Plan(JsonArray items, JsonObject doc, HeadlessProjectContext ctx, ModelErrors errors)
    {
        var db = ctx.Db;
        var materials = SectionsKind.PlanMaterials(doc, ctx);
        var changes = new List<ModelChange>();
        var seen = new HashSet<string>();
        for (int i = 0; i < items.Count; i++)
        {
            string path = $"plateSections[{i}]";
            if (items[i] is not JsonObject item)
            {
                errors.Add(path, Loc.S("CliModelErrNotObject"));
                continue;
            }
            var d = Parse(item, path, errors);
            if (d == null) continue;
            string key = d.Section.Tag;
            if (!seen.Add(key))
            {
                errors.Add($"{path}.tag", string.Format(Loc.S("CliModelErrDuplicateKey"), key));
                continue;
            }
            var existing = db.PlateSections.Where(p => p.Tag.Trim() == key).ToList();
            if (existing.Count > 1)
            {
                errors.Add($"{path}.tag", string.Format(Loc.S("CliModelErrAmbiguousKey"), existing.Count, key));
                continue;
            }
            var target = existing.SingleOrDefault();
            if (target?.GeneratedForRegionId != null)
            {
                errors.Add($"{path}.tag", string.Format(Loc.S("CliPlateSectionErrGenerated"), key));
                continue;
            }

            int before = errors.Count;
            var refs = new List<(string Path, string Tag)> { ($"{path}.concrete", d.Concrete), ($"{path}.rebar", d.Rebar) };
            refs.AddRange(d.LayerMaterials.Select(m => (m.Path, m.Tag)));
            foreach (var (p, t) in refs.Where(r => !materials.ContainsKey(r.Tag)))
                errors.Add(p, string.Format(Loc.S("CliSectionErrUnknownMaterial"), t));
            if (errors.Count > before) continue;

            // Проверка службой на черновике: материалы документа — временные объекты с Id < 0.
            var draft = Resolve(d, t => materials[t].Id);
            foreach (var e in PlateSectionAuthoring.Validate(draft, materials.Values.ToList()))
                errors.Add($"{path}.{e.Path}", e.Message);
            if (errors.Count > before) continue;
            changes.Add(new ModelChange(Name, key, target == null, c => Commit(c, d, target)));
        }
        return changes;
    }

    /// <summary>Сечение документа с Id материалов по обозначению.</summary>
    static PlateSection Resolve(PlateDoc d, Func<string, int> materialId)
    {
        var ps = new PlateSection();
        PlateSectionAuthoring.CopyInto(d.Section, ps);
        ps.ConcreteMaterialId = materialId(d.Concrete);
        ps.RebarMaterialId = materialId(d.Rebar);
        foreach (var (_, t, k) in d.LayerMaterials)
            ps.RebarLayers[k].MaterialId = materialId(t);
        return ps;
    }

    /// <summary>Новое — добавить; существующее — заменить поля на месте (тот же Id, ссылки задач и КонЭ целы).</summary>
    static void Commit(HeadlessProjectContext ctx, PlateDoc d, PlateSection? target)
    {
        var db = ctx.Db;
        var made = Resolve(d, t => db.Materials.First(m => m.Tag.Trim() == t).Id);
        var ps = target ?? new PlateSection();
        PlateSectionAuthoring.CopyInto(made, ps);
        var errors = PlateSectionAuthoring.Save(db, ps);
        if (errors.Count > 0)   // не должно случиться: Plan проверил тем же Validate
            throw new CliException(CliExitCode.Failed, string.Join("; ", errors.Select(e => $"{d.Path}.{e.Path}: {e.Message}")));
    }
}
