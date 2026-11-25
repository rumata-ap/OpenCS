using System.Text.Json;
using System.Text.Json.Nodes;
using CScore;
using CScore.Fem;
using CScore.Fem.Editing;
using CScore.Planar;
using OpenCS.Services;
using OpenCS.Utilites;

namespace OpenCS.Cli.Model;

/// <summary>
/// Раздел «femSchemas»: родные МКЭ-схемы (созданные в OpenCS; импортные, фрагменты и субмодели — нет), ключ — имя
/// (<c>tag</c>). Схема — составной объект: указанный массив (<c>nodes</c>, <c>members</c>, <c>groups</c>) заменяет
/// состав схемы по этому виду (совпавшие по тегу объекты обновляются на месте, остальные удаляются с
/// предупреждением), неуказанный — не трогается. Узел — целый тег, координаты в м, закрепления <c>fix</c>. КонЭ —
/// стержень (<c>nodes</c>, сечение <c>section</c> из <c>sections</c>) или пластина (<c>region</c> — контур и
/// отверстия в общих координатах, м; <c>xAxis</c> — местная ось X; сечение <c>plateSection</c>). Сетка и результаты
/// не описываются: изменённая схема получает устаревшую сетку, она перестраивается перед расчётом или командой
/// <c>opencs run mesh</c>; изменившаяся сетка сбрасывает результаты постановок.
/// </summary>
sealed class FemSchemasKind : IModelKind
{
    public string Name => "femSchemas";
    public string SchemaResource => "OpenCS.Cli.Model.Schemas.femSchemas.schema.json";

    static readonly string[] TopKeys = ["tag", "mesh", "nodes", "members", "groups"];
    static readonly string[] MeshKeys = ["barStepM", "plateStepM", "autoRigidJunctions", "rigidJunctionAngleDeg"];
    static readonly string[] NodeKeys = ["tag", "x", "y", "z", "fix"];
    static readonly string[] BeamKeys = ["tag", "type", "nodes", "section", "rotationDeg", "meshStepM", "torsion"];
    static readonly string[] ShellKeys = ["tag", "type", "kind", "plateSection", "meshStepM", "region"];
    static readonly string[] RegionKeys = ["outline", "holes", "xAxis"];
    static readonly string[] TorsionKeys = ["strategy", "gj", "task"];
    static readonly string[] GroupKeys = ["tag", "memberType", "members"];
    static readonly string[] Dofs = ["x", "y", "z", "rx", "ry", "rz"];

    /// <summary>Допуск плоскостности контура пластины и совпадения геометрии области при обновлении, м.</summary>
    const double PlaneTol = 1e-3, SameTol = 1e-6;

    static double R(double v) => Math.Round(v, 9) + 0.0;

    // ── Выгрузка ────────────────────────────────────────────────────────────────────────────

    public JsonArray Export(HeadlessProjectContext ctx, Action<string> warn)
    {
        var db = ctx.Db;
        var items = new JsonArray();
        var seen = new HashSet<string>(StringComparer.CurrentCultureIgnoreCase);
        foreach (var schema in db.FemSchemas.OrderBy(s => s.Id))
        {
            string key = schema.Tag.Trim();
            if (!FemSchemaAuthoring.IsNative(schema))
            {
                warn(string.Format(Loc.S("CliFemSkippedNotNative"), key));
                continue;
            }
            if (!seen.Add(key))
            {
                warn(string.Format(Loc.S("CliFemDuplicateSkipped"), key));
                continue;
            }
            items.Add(ToJson(db, schema, warn));
        }
        return items;
    }

    static JsonObject ToJson(DatabaseService db, FemSchema schema, Action<string> warn)
    {
        var mesh = new JsonObject();
        var (bar, plateStep) = db.GetFemSchemaMeshSteps(schema.Id);
        var (auto, angle) = db.GetFemSchemaRigidJunctions(schema.Id);
        if (bar is double b) mesh["barStepM"] = b;
        if (plateStep is double p) mesh["plateStepM"] = p;
        mesh["autoRigidJunctions"] = auto;
        mesh["rigidJunctionAngleDeg"] = angle;

        var nodes = new JsonArray();
        foreach (var n in db.GetFemNodes(schema.Id).OrderBy(n => int.TryParse(n.NodeTag, out int t) ? t : int.MaxValue))
        {
            var o = new JsonObject { ["tag"] = int.TryParse(n.NodeTag, out int t) ? t : n.NodeTag, ["x"] = R(n.X), ["y"] = R(n.Y), ["z"] = R(n.Z) };
            if ((n.DofMask & FemBoundaryDofs.All) != 0)
                o["fix"] = new JsonArray(Dofs.Where((_, i) => (n.DofMask & (1 << i)) != 0).Select(d => (JsonNode)d).ToArray());
            nodes.Add(o);
        }

        var regions = db.GetPlanarRegions(schema.Id).ToDictionary(r => r.Id);
        var members = new JsonArray();
        foreach (var m in db.GetFemMembers(schema.Id))
        {
            if (m.ElemType == "shell")
            {
                if (m.PlanarRegionId is not int rid || !regions.TryGetValue(rid, out var region))
                {
                    warn(string.Format(Loc.S("CliFemShellNoRegion"), schema.Tag, m.ElemTag));
                    continue;
                }
                var o = new JsonObject { ["tag"] = m.ElemTag, ["type"] = "shell" };
                if (m.KindSource == "manual" && m.Kind != null) o["kind"] = m.Kind;
                if (m.PlateSectionId is int ps && db.PlateSections.FirstOrDefault(s => s.Id == ps) is { } plate) o["plateSection"] = plate.Tag.Trim();
                if (m.TargetMeshLengthM is double step) o["meshStepM"] = step;
                o["region"] = RegionJson(region);
                members.Add(o);
            }
            else
            {
                var ids = JsonSerializer.Deserialize<int[]>(m.NodeIdsJson) ?? [];
                var o = new JsonObject { ["tag"] = m.ElemTag, ["type"] = m.ElemType, ["nodes"] = new JsonArray(ids.Select(i => (JsonNode)i).ToArray()) };
                if (m.CrossSectionId is int cs && db.CrossSections.FirstOrDefault(s => s.Id == cs) is { } section) o["section"] = section.Tag.Trim();
                if (m.RotationDeg != 0) o["rotationDeg"] = m.RotationDeg;
                if (m.TargetMeshLengthM is double step) o["meshStepM"] = step;
                if (m.GjStrategy == "saint_venant")
                {
                    var t = new JsonObject { ["strategy"] = "saint_venant" };
                    if (m.GjTorsionTaskId is int tid && db.CalcTasks.FirstOrDefault(x => x.Id == tid) is { } task) t["task"] = task.Tag.Trim();
                    o["torsion"] = t;
                }
                else if (m.GjManualValue is double gj)
                    o["torsion"] = new JsonObject { ["strategy"] = "manual", ["gj"] = R(FemUnitConverter.NewtonMetersSquaredToKiloNewtonMetersSquared(gj)) };
                members.Add(o);
            }
        }

        var groups = new JsonArray();
        foreach (var g in schema.MemberGroups.Where(g => g.Kind == FemMemberGroup.KindMembers))
        {
            var o = new JsonObject { ["tag"] = g.Tag };
            if (g.MemberType != null) o["memberType"] = g.MemberType;
            o["members"] = new JsonArray(g.Tags.Select(t => (JsonNode)t).ToArray());
            groups.Add(o);
        }
        return new JsonObject
        {
            ["tag"] = schema.Tag.Trim(), ["mesh"] = mesh, ["nodes"] = nodes, ["members"] = members, ["groups"] = groups,
        };
    }

    /// <summary>Область пластины в общих координатах: контур без замыкающей вершины, отверстия, местная ось X.</summary>
    static JsonObject RegionJson(PlanarRegion region)
    {
        JsonArray Loop(Contour c)
        {
            var pts = Global(region.Frame, c);
            return new JsonArray(pts.Select(p => (JsonNode)new JsonArray(R(p.X), R(p.Y), R(p.Z))).ToArray());
        }
        var hull = region.Contours.First(c => c.Type == ContourType.Hull);
        var o = new JsonObject { ["outline"] = Loop(hull) };
        var holes = region.Contours.Where(c => c.Type == ContourType.Hole).ToList();
        if (holes.Count > 0) o["holes"] = new JsonArray(holes.Select(h => (JsonNode)Loop(h)).ToArray());
        var x = region.Frame.LocalX;
        o["xAxis"] = new JsonArray(R(x.X), R(x.Y), R(x.Z));
        return o;
    }

    /// <summary>Вершины контура области в общих координатах (без замыкающей).</summary>
    static List<PlanarVector3> Global(Frame3D f, Contour c)
    {
        int n = c.X.Count;
        if (n > 1 && Math.Abs(c.X[0] - c.X[n - 1]) < 1e-12 && Math.Abs(c.Y[0] - c.Y[n - 1]) < 1e-12) n--;
        var pts = new List<PlanarVector3>(n);
        for (int i = 0; i < n; i++) pts.Add(f.Origin + f.LocalX * c.X[i] + f.LocalY * c.Y[i]);
        return pts;
    }

    // ── Разбор ──────────────────────────────────────────────────────────────────────────────

    sealed record NodeDoc(string Path, int Tag, double X, double Y, double Z, int Mask);
    sealed record RegionDoc(string Path, List<PlanarVector3> Outline, List<List<PlanarVector3>> Holes, PlanarVector3? XAxis);
    sealed record MemberDoc(string Path, string Tag, string Type, int[] Nodes, string? Section, double Rotation,
        double? MeshStep, string Gj, double? GjKnm2, string? TorsionTask, string? Kind, string? PlateSection, RegionDoc? Region);
    sealed record GroupDoc(string Path, string Tag, string? MemberType, List<string> Members);
    sealed record SchemaDoc(string Path, string Tag, JsonObject? Mesh, List<NodeDoc>? Nodes, List<MemberDoc>? Members, List<GroupDoc>? Groups);

    static void Unknown(JsonObject o, string[] allowed, string path, ModelErrors errors)
    {
        foreach (var (key, _) in o)
            if (!allowed.Contains(key)) errors.Add($"{path}.{key}", Loc.S("CliModelErrUnknownField"));
    }

    /// <summary>Тег — строка или число (как его пишет пользователь).</summary>
    static string? Tag(JsonObject o, string path, ModelErrors errors)
    {
        if (o["tag"] is JsonValue v)
        {
            if (v.TryGetValue(out string? s) && s.Trim().Length > 0) return s.Trim();
            if (v.TryGetValue(out long l)) return l.ToString(System.Globalization.CultureInfo.InvariantCulture);
        }
        errors.Add($"{path}.tag", o["tag"] is null ? Loc.S("CliModelErrRequired") : Loc.S("CliModelErrBadValue"));
        return null;
    }

    static PlanarVector3? Point(JsonNode? node, string path, ModelErrors errors)
    {
        if (node is JsonArray a && a.Count == 3 && a.All(x => x is JsonValue v && v.TryGetValue(out double d) && double.IsFinite(d)))
            return new PlanarVector3(a[0]!.GetValue<double>(), a[1]!.GetValue<double>(), a[2]!.GetValue<double>());
        errors.Add(path, Loc.S("CliFemErrPoint"));
        return null;
    }

    static List<PlanarVector3>? Loop(JsonNode? node, string path, ModelErrors errors)
    {
        if (node is not JsonArray a || a.Count < 3)
        {
            errors.Add(path, Loc.S("CliFemErrLoop"));
            return null;
        }
        var pts = new List<PlanarVector3>();
        for (int i = 0; i < a.Count; i++)
            if (Point(a[i], $"{path}[{i}]", errors) is { } p) pts.Add(p);
        return pts.Count == a.Count ? pts : null;
    }

    static SchemaDoc? Parse(JsonObject o, string path, ModelErrors errors)
    {
        int before = errors.Count;
        Unknown(o, TopKeys, path, errors);
        string? tag = SectionJson.Str(o, "tag", path, errors, required: true);
        JsonObject? mesh = null;
        if (o["mesh"] is JsonObject mo) { Unknown(mo, MeshKeys, $"{path}.mesh", errors); mesh = mo; }
        else if (o["mesh"] is not null) errors.Add($"{path}.mesh", Loc.S("CliModelErrNotObject"));
        if (mesh != null)
        {
            foreach (var k in new[] { "barStepM", "plateStepM" })
                if (SectionJson.Num(mesh, k, $"{path}.mesh", errors) is <= 0) errors.Add($"{path}.mesh.{k}", Loc.S("FieldErrPositive"));
            SectionJson.Bool(mesh, "autoRigidJunctions", $"{path}.mesh", errors);
            if (SectionJson.Num(mesh, "rigidJunctionAngleDeg", $"{path}.mesh", errors) is double a && (a <= 0 || a >= 90))
                errors.Add($"{path}.mesh.rigidJunctionAngleDeg", string.Format(Loc.S("CliFemErrRange"), "0", "90"));
        }

        List<NodeDoc>? nodes = null;
        if (o["nodes"] is JsonArray na)
        {
            nodes = [];
            for (int i = 0; i < na.Count; i++)
            {
                string p = $"{path}.nodes[{i}]";
                if (na[i] is not JsonObject no) { errors.Add(p, Loc.S("CliModelErrNotObject")); continue; }
                Unknown(no, NodeKeys, p, errors);
                string? t = Tag(no, p, errors);
                int tagInt = 0;
                if (t != null && (!int.TryParse(t, out tagInt) || tagInt <= 0)) errors.Add($"{p}.tag", Loc.S("CliFemErrNodeTag"));
                double x = SectionJson.Num(no, "x", p, errors, required: true) ?? 0;
                double y = SectionJson.Num(no, "y", p, errors, required: true) ?? 0;
                double z = SectionJson.Num(no, "z", p, errors, required: true) ?? 0;
                int mask = 0;
                if (no["fix"] is JsonArray fa)
                    for (int k = 0; k < fa.Count; k++)
                    {
                        int dof = fa[k] is JsonValue fv && fv.TryGetValue(out string? s) ? Array.IndexOf(Dofs, s) : -1;
                        if (dof < 0) errors.Add($"{p}.fix[{k}]", string.Format(Loc.S("CliModelErrOneOf"), string.Join(", ", Dofs)));
                        else mask |= 1 << dof;
                    }
                else if (no["fix"] is not null) errors.Add($"{p}.fix", Loc.S("CliModelErrNotArray"));
                nodes.Add(new NodeDoc(p, tagInt, x, y, z, mask));
            }
        }
        else if (o["nodes"] is not null) errors.Add($"{path}.nodes", Loc.S("CliModelErrNotArray"));

        List<MemberDoc>? members = null;
        if (o["members"] is JsonArray ma)
        {
            members = [];
            for (int i = 0; i < ma.Count; i++)
            {
                string p = $"{path}.members[{i}]";
                if (ma[i] is not JsonObject mo2) { errors.Add(p, Loc.S("CliModelErrNotObject")); continue; }
                if (ParseMember(mo2, p, errors) is { } md) members.Add(md);
            }
        }
        else if (o["members"] is not null) errors.Add($"{path}.members", Loc.S("CliModelErrNotArray"));

        List<GroupDoc>? groups = null;
        if (o["groups"] is JsonArray ga)
        {
            groups = [];
            for (int i = 0; i < ga.Count; i++)
            {
                string p = $"{path}.groups[{i}]";
                if (ga[i] is not JsonObject go) { errors.Add(p, Loc.S("CliModelErrNotObject")); continue; }
                Unknown(go, GroupKeys, p, errors);
                string? gt = SectionJson.Str(go, "tag", p, errors, required: true);
                string? type = SectionJson.Str(go, "memberType", p, errors);
                if (type != null && !FemMemberTypes.All.Contains(type))
                    errors.Add($"{p}.memberType", string.Format(Loc.S("CliModelErrOneOf"), string.Join(", ", FemMemberTypes.All)));
                var list = new List<string>();
                if (go["members"] is JsonArray gm)
                    for (int k = 0; k < gm.Count; k++)
                    {
                        if (gm[k] is JsonValue gv && (gv.TryGetValue(out string? s) || gv.TryGetValue(out long _)))
                            list.Add(gv.TryGetValue(out string? s2) ? s2!.Trim() : gv.GetValue<long>().ToString(System.Globalization.CultureInfo.InvariantCulture));
                        else errors.Add($"{p}.members[{k}]", Loc.S("CliModelErrBadValue"));
                    }
                else errors.Add($"{p}.members", go["members"] is null ? Loc.S("CliModelErrRequired") : Loc.S("CliModelErrNotArray"));
                if (gt != null) groups.Add(new GroupDoc(p, gt, type, list));
            }
        }
        else if (o["groups"] is not null) errors.Add($"{path}.groups", Loc.S("CliModelErrNotArray"));

        // Документ возвращается и при ошибках разбора — план проверит ещё и ссылки, чтобы показать все ошибки сразу.
        return tag == null ? null : new SchemaDoc(path, tag, mesh, nodes, members, groups);
    }

    static MemberDoc? ParseMember(JsonObject o, string p, ModelErrors errors)
    {
        int before = errors.Count;
        string type = SectionJson.Pick(o, "type", [("beam", "beam"), ("truss", "truss"), ("shell", "shell")], "beam", p, errors);
        Unknown(o, type == "shell" ? ShellKeys : BeamKeys, p, errors);
        string? tag = Tag(o, p, errors);
        double? step = SectionJson.Num(o, "meshStepM", p, errors);
        if (step is <= 0) errors.Add($"{p}.meshStepM", Loc.S("FieldErrPositive"));
        if (type == "shell")
        {
            string? kind = o["kind"] is null ? null : SectionJson.Pick<string?>(o, "kind", [("plate", "plate"), ("wall", "wall")], null, p, errors);
            string? plate = SectionJson.Str(o, "plateSection", p, errors);
            RegionDoc? region = null;
            if (o["region"] is JsonObject ro)
            {
                string rp = $"{p}.region";
                Unknown(ro, RegionKeys, rp, errors);
                var outline = Loop(ro["outline"], $"{rp}.outline", errors);
                var holes = new List<List<PlanarVector3>>();
                if (ro["holes"] is JsonArray ha)
                    for (int k = 0; k < ha.Count; k++)
                        if (Loop(ha[k], $"{rp}.holes[{k}]", errors) is { } h) holes.Add(h);
                else if (ro["holes"] is not null) errors.Add($"{rp}.holes", Loc.S("CliModelErrNotArray"));
                PlanarVector3? xAxis = ro["xAxis"] is null ? null : Point(ro["xAxis"], $"{rp}.xAxis", errors);
                if (outline != null) region = new RegionDoc(rp, outline, holes, xAxis);
            }
            else errors.Add($"{p}.region", o["region"] is null ? Loc.S("CliModelErrRequired") : Loc.S("CliModelErrNotObject"));
            return errors.Count > before ? null
                : new MemberDoc(p, tag!, type, [], null, 0, step, "manual", null, null, kind, plate, region);
        }

        var ids = new List<int>();
        if (o["nodes"] is JsonArray na && na.Count == 2)
            for (int k = 0; k < 2; k++)
            {
                if (na[k] is JsonValue v && (v.TryGetValue(out int id) || (v.TryGetValue(out string? s) && int.TryParse(s, out id))) && id > 0) ids.Add(id);
                else errors.Add($"{p}.nodes[{k}]", Loc.S("CliFemErrNodeTag"));
            }
        else errors.Add($"{p}.nodes", Loc.S("CliFemErrBeamNodes"));
        if (ids.Count == 2 && ids[0] == ids[1]) errors.Add($"{p}.nodes", Loc.S("CliFemErrBeamNodes"));
        string? section = SectionJson.Str(o, "section", p, errors);
        double rot = SectionJson.Num(o, "rotationDeg", p, errors) ?? 0;
        string gj = "manual";
        double? gjValue = null;
        string? task = null;
        if (o["torsion"] is JsonObject to)
        {
            string tp = $"{p}.torsion";
            Unknown(to, TorsionKeys, tp, errors);
            gj = SectionJson.Pick(to, "strategy", [("manual", "manual"), ("saint_venant", "saint_venant")], "manual", tp, errors);
            gjValue = SectionJson.Num(to, "gj", tp, errors);
            task = SectionJson.Str(to, "task", tp, errors);
            if (gj == "manual" && gjValue is < 0) errors.Add($"{tp}.gj", Loc.S("FieldErrNonNegative"));
            if (gj == "manual" && task != null) errors.Add($"{tp}.task", Loc.S("CliModelErrUnknownField"));
            if (gj == "saint_venant" && gjValue != null) errors.Add($"{tp}.gj", Loc.S("CliModelErrUnknownField"));
        }
        else if (o["torsion"] is not null) errors.Add($"{p}.torsion", Loc.S("CliModelErrNotObject"));
        return errors.Count > before ? null
            : new MemberDoc(p, tag!, type, [.. ids], section, rot, step, gj, gjValue, task, null, null, null);
    }

    // ── Геометрия области ───────────────────────────────────────────────────────────────────

    /// <summary>
    /// Система координат пластины по контуру, как в редакторе: горизонтальная — оси общие (X, Y, нормаль вверх), начало
    /// на уровне плиты над (0, 0); вертикальная — X горизонтальна вдоль первой стороны, Y вверх; наклонная — X вдоль
    /// первой стороны, нормаль с положительной Z. <paramref name="xAxis"/> — своя местная ось X (проецируется на плоскость).
    /// </summary>
    static Frame3D FrameFor(List<PlanarVector3> outline, PlanarVector3? xAxis)
    {
        var f = Frame3D.FromPolygon(outline.Select(p => p.X).ToList(), outline.Select(p => p.Y).ToList(), outline.Select(p => p.Z).ToList());
        var n = f.LocalZ;
        if (n.Z < 0) n = n * -1;
        bool horizontal = Math.Abs(n.Z) > 1 - 1e-9;
        bool vertical = Math.Abs(n.Z) < 1e-9;
        PlanarVector3 x;
        if (xAxis is { } xa)
            x = xa - n * xa.Dot(n);
        else if (horizontal)
            x = new PlanarVector3(1, 0, 0);
        else
        {
            var e = outline[1] - outline[0];
            x = vertical ? new PlanarVector3(e.X, e.Y, 0) : e - n * e.Dot(n);
        }
        if (x.Length < 1e-9) throw new InvalidOperationException(Loc.S("CliFemErrXAxis"));
        x = x.Normalize();
        PlanarVector3 y, z;
        if (vertical && xAxis == null)
        {
            y = new PlanarVector3(0, 0, 1);
            z = x.Cross(y);
        }
        else
        {
            z = horizontal ? new PlanarVector3(0, 0, 1) : n;
            y = z.Cross(x);
        }
        var origin = horizontal ? new PlanarVector3(0, 0, outline[0].Z) : outline[0];
        var frame = new Frame3D(origin, x, y, z);
        frame.Validate();
        return frame;
    }

    /// <summary>Контур в координатах области (замкнутый); точка вне плоскости — ошибка.</summary>
    static Contour? Local(Frame3D f, List<PlanarVector3> pts, string tag, ContourType type, string path, ModelErrors errors)
    {
        var xs = new List<double>(); var ys = new List<double>();
        for (int i = 0; i < pts.Count; i++)
        {
            var d = pts[i] - f.Origin;
            if (Math.Abs(d.Dot(f.LocalZ)) > PlaneTol)
            {
                errors.Add($"{path}[{i}]", string.Format(Loc.S("CliFemErrNotPlanar"), Math.Round(Math.Abs(d.Dot(f.LocalZ)) * 1000, 1)));
                return null;
            }
            xs.Add(d.Dot(f.LocalX)); ys.Add(d.Dot(f.LocalY));
        }
        xs.Add(xs[0]); ys.Add(ys[0]);
        return new Contour(xs, ys, tag) { Type = type };
    }

    /// <summary>Область по документу (без записи); ошибки геометрии — с путём.</summary>
    static PlanarRegion? BuildRegion(RegionDoc r, string tag, ModelErrors errors)
    {
        Frame3D frame;
        try { frame = FrameFor(r.Outline, r.XAxis); }
        catch (Exception e) when (e is InvalidOperationException or ArgumentException)
        {
            errors.Add($"{r.Path}.outline", e.Message);
            return null;
        }
        var hull = Local(frame, r.Outline, tag, ContourType.Hull, $"{r.Path}.outline", errors);
        var holes = new List<Contour>();
        for (int k = 0; k < r.Holes.Count; k++)
            if (Local(frame, r.Holes[k], $"{tag}-{k + 1}", ContourType.Hole, $"{r.Path}.holes[{k}]", errors) is { } h) holes.Add(h);
        if (hull == null || holes.Count != r.Holes.Count) return null;
        var (region, diagnostics) = PlanarRegionCreation.TryCreate(hull, holes, frame, tag);
        foreach (var d in diagnostics.Where(d => d.IsError)) errors.Add(r.Path, d.Message);
        return region;
    }

    /// <summary>Та же геометрия в общих координатах и та же ось X — существующая область не перезаписывается.</summary>
    static bool SameGeometry(PlanarRegion a, PlanarRegion b)
    {
        if ((a.Frame.LocalX - b.Frame.LocalX).Length > SameTol || (a.Frame.LocalZ - b.Frame.LocalZ).Length > SameTol) return false;
        var ca = a.Contours.OrderBy(c => c.Type).ToList();
        var cb = b.Contours.OrderBy(c => c.Type).ToList();
        if (ca.Count != cb.Count) return false;
        for (int i = 0; i < ca.Count; i++)
        {
            var pa = Global(a.Frame, ca[i]); var pb = Global(b.Frame, cb[i]);
            if (ca[i].Type != cb[i].Type || pa.Count != pb.Count) return false;
            for (int k = 0; k < pa.Count; k++)
                if ((pa[k] - pb[k]).Length > SameTol) return false;
        }
        return true;
    }

    // ── План и запись ───────────────────────────────────────────────────────────────────────

    static HashSet<string> DocKeys(JsonObject doc, string kind) =>
        (doc[kind] as JsonArray ?? []).OfType<JsonObject>()
            .Select(o => o["tag"] is JsonValue v && v.TryGetValue(out string? s) ? s.Trim() : null)
            .OfType<string>().ToHashSet();

    public IReadOnlyList<ModelChange> Plan(JsonArray items, JsonObject doc, HeadlessProjectContext ctx, ModelErrors errors)
    {
        var db = ctx.Db;
        var docSections = DocKeys(doc, "sections");
        var docPlates = DocKeys(doc, "plateSections");
        var docTasks = DocKeys(doc, "tasks");
        var changes = new List<ModelChange>();
        var seen = new HashSet<string>(StringComparer.CurrentCultureIgnoreCase);
        for (int i = 0; i < items.Count; i++)
        {
            string path = $"femSchemas[{i}]";
            if (items[i] is not JsonObject item)
            {
                errors.Add(path, Loc.S("CliModelErrNotObject"));
                continue;
            }
            int itemStart = errors.Count;
            var d = Parse(item, path, errors);
            if (d == null) continue;
            if (!seen.Add(d.Tag))
            {
                errors.Add($"{path}.tag", string.Format(Loc.S("CliModelErrDuplicateKey"), d.Tag));
                continue;
            }
            var existing = db.FemSchemas.Where(s => string.Equals(s.Tag.Trim(), d.Tag, StringComparison.CurrentCultureIgnoreCase)).ToList();
            if (existing.Count > 1)
            {
                errors.Add($"{path}.tag", string.Format(Loc.S("CliModelErrAmbiguousKey"), existing.Count, d.Tag));
                continue;
            }
            var target = existing.SingleOrDefault();
            if (target != null && !FemSchemaAuthoring.IsNative(target))
            {
                errors.Add($"{path}.tag", string.Format(Loc.S("CliFemErrNotNative"), d.Tag));
                continue;
            }

            int before = errors.Count;
            // Ссылки: сечение — из проекта или создаваемое документом (Id появится при записи).
            int? SectionId(string key, string p)
            {
                if (docSections.Contains(key)) return -1;
                var found = db.CrossSections.Where(s => s.Tag.Trim() == key).ToList();
                if (found.Count == 1) return found[0].Id;
                errors.Add(p, found.Count > 1 ? string.Format(Loc.S("CliModelErrAmbiguousKey"), found.Count, key)
                    : string.Format(Loc.S("CliTaskErrUnknownSection"), key));
                return null;
            }
            int? PlateId(string key, string p)
            {
                if (docPlates.Contains(key)) return -1;
                var found = db.PlateSections.Where(s => s.Tag.Trim() == key).ToList();
                if (found.Count == 1) return found[0].Id;
                errors.Add(p, found.Count > 1 ? string.Format(Loc.S("CliModelErrAmbiguousKey"), found.Count, key)
                    : string.Format(Loc.S("CliFemErrUnknownPlateSection"), key));
                return null;
            }
            int? TaskId(string key, string p)
            {
                if (docTasks.Contains(key)) return -1;
                var found = db.CalcTasks.Where(t => t.Tag.Trim() == key && t.Kind is "torsion_bem" or "torsion_fem").ToList();
                if (found.Count == 1) return found[0].Id;
                errors.Add(p, string.Format(Loc.S("CliFemErrUnknownTorsionTask"), key));
                return null;
            }
            foreach (var m in d.Members ?? [])
            {
                if (m.Section != null) SectionId(m.Section, $"{m.Path}.section");
                if (m.PlateSection != null) PlateId(m.PlateSection, $"{m.Path}.plateSection");
                if (m.TorsionTask != null) TaskId(m.TorsionTask, $"{m.Path}.torsion.task");
            }
            if (errors.Count > before) continue;

            if (errors.Count > itemStart) continue;
            // Пробная сборка без записи: те же проверки, что у «Сохранить» редактора.
            var probe = Build(ctx, d, target, errors, resolve: false);
            if (probe == null || errors.Count > before) continue;
            changes.Add(new ModelChange(Name, d.Tag, target == null, c => Commit(c, d, target)));
        }
        return changes;
    }

    /// <summary>Итог сборки: сессия, новые и изменённые области (ключ — тег КонЭ), что удалено.</summary>
    sealed record Built(FemSchemaEditSession Session, Dictionary<string, PlanarRegion> Regions, int NodesRemoved,
        int MembersRemoved, int GroupsRemoved);

    /// <summary>
    /// Состав схемы по документу поверх текущего (или новой схемы): указанные массивы заменяют вид, совпавшие по тегу
    /// объекты обновляются на месте. <paramref name="resolve"/> — ссылки на сечения и задачи разрешаются в Id (при
    /// записи); в плане — нет. Ошибки проверки состава — с путём документа.
    /// </summary>
    static Built? Build(HeadlessProjectContext ctx, SchemaDoc d, FemSchema? target, ModelErrors errors, bool resolve)
    {
        var db = ctx.Db;
        var schema = target ?? new FemSchema { Tag = d.Tag, SourceType = "internal" };
        var session = target != null ? FemSchemaAuthoring.OpenSession(db, target) : new FemSchemaEditSession(schema);
        if (!resolve)
        {
            // План (и --dry-run) не трогает группы схемы в памяти: команды и правки состава — на копиях.
            var copies = session.MemberGroups.Select(CloneGroup).ToList();
            session.MemberGroups.Clear();
            session.MemberGroups.AddRange(copies);
        }
        var regions = new Dictionary<string, PlanarRegion>();
        var existingRegions = target != null ? db.GetPlanarRegions(target.Id).ToDictionary(r => r.Id) : [];
        int nodesRemoved = 0, membersRemoved = 0, groupsRemoved = 0;

        // КонЭ — раньше узлов: удаление узла уносит КонЭ на нём.
        if (d.Members != null)
        {
            var keep = d.Members.Select(m => m.Tag).ToHashSet();
            foreach (var gone in session.Members.Where(m => !keep.Contains(m.ElemTag)).ToList())
            {
                session.Execute(new DeleteMemberCommand(gone));
                membersRemoved++;
            }
            var seenTags = new HashSet<string>();
            foreach (var m in d.Members)
            {
                if (!seenTags.Add(m.Tag))
                {
                    errors.Add($"{m.Path}.tag", string.Format(Loc.S("CliModelErrDuplicateKey"), m.Tag));
                    continue;
                }
                var member = session.Members.FirstOrDefault(x => x.ElemTag == m.Tag);
                if (member != null && (member.ElemType == "shell") != (m.Type == "shell"))
                {
                    session.Execute(new DeleteMemberCommand(member));
                    member = null;
                }
                bool added = member == null;
                member ??= new FemMember { SchemaId = schema.Id, ElemTag = m.Tag };
                member.ElemType = m.Type;
                member.TargetMeshLengthM = m.MeshStep;
                if (m.Type == "shell")
                {
                    member.NodeIdsJson = "[]";
                    member.Kind = m.Kind ?? member.Kind;
                    member.KindSource = m.Kind != null ? "manual" : "auto";
                    member.PlateSectionId = m.PlateSection == null ? null
                        : resolve ? db.PlateSections.Single(s => s.Tag.Trim() == m.PlateSection).Id : -1;
                    if (BuildRegion(m.Region!, m.Tag, errors) is { } region)
                    {
                        var old = member.PlanarRegionId is int rid && existingRegions.TryGetValue(rid, out var r) ? r : null;
                        if (old == null || !SameGeometry(old, region))
                        {
                            if (old != null)
                            {
                                region.Id = old.Id;
                                region.RebarZones = old.RebarZones;
                                region.RebarSectionGridStep = old.RebarSectionGridStep;
                                region.MeshMaxElementSizeM = old.MeshMaxElementSizeM;
                            }
                            regions[m.Tag] = region;
                        }
                    }
                }
                else
                {
                    member.NodeIdsJson = JsonSerializer.Serialize(m.Nodes);
                    member.CrossSectionId = m.Section == null ? null
                        : resolve ? db.CrossSections.Single(s => s.Tag.Trim() == m.Section).Id : -1;
                    member.RotationDeg = m.Rotation;
                    member.GjStrategy = m.Gj;
                    member.GjManualValue = m.Gj == "manual" && m.GjKnm2 is double gj
                        ? FemUnitConverter.KiloNewtonMetersSquaredToNewtonMetersSquared(gj) : m.Gj == "manual" ? member.GjManualValue : null;
                    member.GjTorsionTaskId = m.Gj == "saint_venant" && m.TorsionTask != null
                        ? resolve ? db.CalcTasks.Single(t => t.Tag.Trim() == m.TorsionTask && t.Kind is "torsion_bem" or "torsion_fem").Id : -1
                        : null;
                }
                if (added) session.Execute(new AddMemberCommand(member));
            }
        }

        if (d.Nodes != null)
        {
            var keep = d.Nodes.Select(n => n.Tag.ToString(System.Globalization.CultureInfo.InvariantCulture)).ToHashSet();
            var gone = session.Nodes.Where(n => !keep.Contains(n.NodeTag)).ToList();
            if (gone.Count > 0)
            {
                nodesRemoved = gone.Count;
                session.Execute(new DeleteNodesCommand(gone));
            }
            var seenTags = new HashSet<int>();
            foreach (var n in d.Nodes)
            {
                if (!seenTags.Add(n.Tag))
                {
                    errors.Add($"{n.Path}.tag", string.Format(Loc.S("CliModelErrDuplicateKey"), n.Tag));
                    continue;
                }
                string t = n.Tag.ToString(System.Globalization.CultureInfo.InvariantCulture);
                var node = session.Nodes.FirstOrDefault(x => x.NodeTag == t);
                if (node == null)
                {
                    node = new FemNode { Id = session.AllocateTemporaryNodeId(), SchemaId = schema.Id, NodeTag = t };
                    session.Execute(new AddNodeCommand(node));
                }
                node.X = n.X; node.Y = n.Y; node.Z = n.Z;
                node.DofMask = (node.DofMask & ~FemBoundaryDofs.All) | n.Mask;
            }
        }

        if (d.Groups != null)
        {
            var memberTags = session.Members.Select(m => m.ElemTag).ToHashSet();
            var keep = d.Groups.Select(g => g.Tag).ToHashSet();
            var removed = session.MemberGroups.Where(g => g.Kind == FemMemberGroup.KindMembers && !keep.Contains(g.Tag)).ToList();
            foreach (var g in removed) session.MemberGroups.Remove(g);
            groupsRemoved = removed.Count;
            var seenTags = new HashSet<string>();
            foreach (var g in d.Groups)
            {
                if (!seenTags.Add(g.Tag))
                {
                    errors.Add($"{g.Path}.tag", string.Format(Loc.S("CliModelErrDuplicateKey"), g.Tag));
                    continue;
                }
                for (int k = 0; k < g.Members.Count; k++)
                    if (!memberTags.Contains(g.Members[k]))
                        errors.Add($"{g.Path}.members[{k}]", string.Format(Loc.S("CliFemErrUnknownMember"), g.Members[k]));
                var group = session.MemberGroups.FirstOrDefault(x => x.Kind == FemMemberGroup.KindMembers && x.Tag == g.Tag);
                if (group == null)
                    session.MemberGroups.Add(FemGroupComposition.NewMembersGroup(schema.Id, g.Members, g.Tag, g.MemberType));
                else
                {
                    group.SetTags(g.Members);
                    group.MemberType = g.MemberType;
                }
            }
        }

        // Проверка состава — та же, что у «Сохранить» редактора; ошибки — к объекту документа по тегу.
        var nodePaths = (d.Nodes ?? []).ToDictionary(n => n.Tag.ToString(System.Globalization.CultureInfo.InvariantCulture), n => n.Path);
        var memberPaths = (d.Members ?? []).GroupBy(m => m.Tag).ToDictionary(g => g.Key, g => g.First().Path);
        // Состав групп документа уже проверен выше с точным путём — повтор проверки групп не печатается.
        foreach (var diag in FemSchemaAuthoring.Validate(db, session).Where(x => x.IsError && x.Code != "member_element_missing"))
        {
            string? p = diag.SourceKeys?.Select(k => memberPaths.GetValueOrDefault(k) ?? nodePaths.GetValueOrDefault(k)).FirstOrDefault(x => x != null);
            errors.Add(p ?? d.Path, diag.Message);
        }
        return new Built(session, regions, nodesRemoved, membersRemoved, groupsRemoved);
    }

    static FemMemberGroup CloneGroup(FemMemberGroup g) => new()
    {
        Id = g.Id, SchemaId = g.SchemaId, Tag = g.Tag, MemberType = g.MemberType, Kind = g.Kind, Origin = g.Origin,
        MemberTagsJson = g.MemberTagsJson, PlateSectionId = g.PlateSectionId, ForceSetId = g.ForceSetId,
        DesignParamsJson = g.DesignParamsJson,
    };

    /// <summary>Новая схема — создать; существующая — состав по документу (те же Id групп), области, предупреждения.</summary>
    static void Commit(HeadlessProjectContext ctx, SchemaDoc d, FemSchema? target)
    {
        var db = ctx.Db;
        var schema = target ?? FemSchemaAuthoring.CreateNew(db, d.Tag);
        if (d.Mesh is { } mesh)
        {
            if (mesh["barStepM"] != null) schema.MeshBarStepM = mesh["barStepM"]!.GetValue<double>();
            if (mesh["plateStepM"] != null) schema.MeshPlateStepM = mesh["plateStepM"]!.GetValue<double>();
            if (mesh["autoRigidJunctions"] != null) schema.AutoRigidJunctions = mesh["autoRigidJunctions"]!.GetValue<bool>();
            if (mesh["rigidJunctionAngleDeg"] != null) schema.RigidJunctionAngleDeg = mesh["rigidJunctionAngleDeg"]!.GetValue<double>();
            db.UpdateFemSchemaMeshSteps(schema);
            db.UpdateFemSchemaRigidJunctions(schema);
        }
        var errors = new ModelErrors();
        var built = Build(ctx, d, schema, errors, resolve: true);
        errors.ThrowIfAny();
        foreach (var (tag, region) in built!.Regions)
        {
            if (region.Id > 0) db.UpdatePlanarRegion(region, schema.Id);
            else region.Id = db.AddPlanarRegion(region, schema.Id);
            built.Session.Members.Single(m => m.ElemTag == tag).PlanarRegionId = region.Id;
        }
        bool hadMesh = target != null && db.GetFemMeshElements(schema.Id).Count > 0;
        if (!FemSchemaAuthoring.Save(db, built.Session, out var diagnostics))
            throw new CliException(CliExitCode.Usage, string.Join("; ", diagnostics.Where(x => x.IsError).Select(x => $"{d.Path}: {x.Message}")));
        if (built.NodesRemoved + built.MembersRemoved + built.GroupsRemoved > 0)
            ctx.LogService.Warning(string.Format(Loc.S("CliFemRemoved"), d.Tag, built.NodesRemoved, built.MembersRemoved, built.GroupsRemoved));
        if (hadMesh)
            ctx.LogService.Warning(string.Format(Loc.S("CliFemMeshStale"), d.Tag));
    }
}
