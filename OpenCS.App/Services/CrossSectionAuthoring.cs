using CScore;
using OpenCS.Utilites;

namespace OpenCS.Services;

/// <summary>
/// Проверка, сохранение и удаление произвольных и стадийных сечений и областей пула (бетонные области, группы
/// стержней, хомуты) — одна для страниц сечений и консоли (<c>opencs apply</c>). Единицы — базы (м, м², МПа для σsp).
/// Пути ошибок — как в документе opencs-model: <c>tag</c>, <c>material</c>, <c>bars[3].d</c>, <c>areas[2].host</c>,
/// <c>staged.parts[1].section</c>.
/// </summary>
public static class CrossSectionAuthoring
{
    /// <summary>Наибольший минимальный угол треугольника для метода Рупперта, градусы.</summary>
    public const double MaxMeshMinAngle = 35;

    /// <summary>Допустимые γsp (п. 9.2.6 СП 63): 1 — не учитывать, 0,9 — благоприятное, 1,1 — неблагоприятное.</summary>
    public static readonly double[] GammaSpValues = [1.0, 0.9, 1.1];

    /// <summary>Материал бетонной (полигональной) области: бетон, сталь, произвольный.</summary>
    public static bool IsRegionMaterial(MatType t) => t is MatType.Concrete or MatType.Steel or MatType.Custom;

    /// <summary>Материал группы стержней: арматура или произвольный.</summary>
    public static bool IsRebarMaterial(MatType t) => t is MatType.ReSteelF or MatType.ReSteelU or MatType.Custom;

    /// <summary>Материал хомутов: арматура.</summary>
    public static bool IsStirrupMaterial(MatType t) => t is MatType.ReSteelF or MatType.ReSteelU;

    /// <summary>Носитель группы стержней: бетонная область с материалом-бетоном (или произвольным).</summary>
    public static bool IsHostCandidate(MaterialArea a) =>
        a.Category == AreaCategory.Region && a.Material?.Type is MatType.Concrete or MatType.Custom;

    static string P(string prefix, string path) => prefix.Length == 0 ? path : $"{prefix}.{path}";

    /// <summary>Проверка области перед записью; <paramref name="prefix"/> — путь области в документе.</summary>
    public static IReadOnlyList<FieldError> ValidateArea(MaterialArea a, IReadOnlyList<Material> materials, string prefix = "")
    {
        var errors = new List<FieldError>();
        if (string.IsNullOrWhiteSpace(a.Tag)) errors.Add(new(P(prefix, "tag"), Loc.S("SectionErrTagEmpty")));
        if (!Enum.IsDefined(a.Category)) errors.Add(new(P(prefix, "category"), Loc.S("SectionErrCategory")));

        var material = a.Material ?? materials.FirstOrDefault(m => m.Id == a.MaterialId && a.MaterialId > 0);
        if (material == null)
            errors.Add(new(P(prefix, "material"), Loc.S("SectionErrNoMaterial")));
        else
        {
            bool ok = a.Category switch
            {
                AreaCategory.Region => IsRegionMaterial(material.Type),
                AreaCategory.RebarGroup => IsRebarMaterial(material.Type),
                _ => IsStirrupMaterial(material.Type),
            };
            if (!ok)
                errors.Add(new(P(prefix, "material"), string.Format(Loc.S(a.Category switch
                {
                    AreaCategory.Region => "SectionErrRegionMaterial",
                    AreaCategory.RebarGroup => "SectionErrRebarMaterial",
                    _ => "SectionErrStirrupMaterial",
                }), material.Tag)));
        }

        switch (a.Category)
        {
            case AreaCategory.Region: ValidateRegion(a, prefix, errors); break;
            case AreaCategory.RebarGroup: ValidateBars(a, prefix, errors); break;
            case AreaCategory.Stirrups:
                if (a.HostAreaId != null || a.HostArea != null)
                    errors.Add(new(P(prefix, "host"), Loc.S("SectionErrStirrupHost")));
                if (a.Stirrups.Count == 0)
                    errors.Add(new(P(prefix, "stirrups"), Loc.S("SectionErrStirrupsEmpty")));
                break;
        }
        for (int g = 0; g < a.Stirrups.Count; g++)
            ValidateStirrupGroup(a, a.Stirrups[g], materials, P(prefix, $"stirrups[{g}]"), errors);
        return errors;
    }

    static void ValidateRegion(MaterialArea a, string prefix, List<FieldError> errors)
    {
        var hull = a.Hull;
        if (hull == null || hull.X.Count == 0)
            errors.Add(new(P(prefix, "contour"), Loc.S("SectionErrNoContour")));
        else
        {
            foreach (var e in ContourAuthoring.ValidateRing(Ring(hull), P(prefix, "polygon.outer")))
                errors.Add(e);
            var outer = Ring(hull);
            var outerX = outer.Select(p => p.X).ToList();
            var outerY = outer.Select(p => p.Y).ToList();
            var holes = a.Holes;
            for (int i = 0; i < holes.Count; i++)
            {
                string hp = P(prefix, $"polygon.holes[{i}]");
                var ring = Ring(holes[i]);
                var holeErrors = ContourAuthoring.ValidateRing(ring, hp);
                errors.AddRange(holeErrors);
                if (holeErrors.Count == 0 && !ring.All(p => WktHelper.PointInPolygon(outerX, outerY, p.X, p.Y)))
                    errors.Add(new(hp, Loc.S("SectionErrHoleOutside")));
            }
        }
        if (a.NX < 1) errors.Add(new(P(prefix, "mesh.nx"), Loc.S("SectionErrMeshCount")));
        if (a.NY < 1) errors.Add(new(P(prefix, "mesh.ny"), Loc.S("SectionErrMeshCount")));
        if (!double.IsFinite(a.MeshMaxArea) || a.MeshMaxArea <= 0)
            errors.Add(new(P(prefix, "mesh.maxArea"), Loc.S("SectionErrPositive")));
        if (!double.IsFinite(a.MeshMinAngle) || a.MeshMinAngle <= 0 || a.MeshMinAngle > MaxMeshMinAngle)
            errors.Add(new(P(prefix, "mesh.minAngle"), string.Format(Loc.S("SectionErrMeshAngle"), MaxMeshMinAngle)));
        if (!double.IsFinite(a.MeshMaxEdgeLen) || a.MeshMaxEdgeLen < 0)
            errors.Add(new(P(prefix, "mesh.maxEdge"), Loc.S("SectionErrNonNegative")));
        if (a.MeshSmoothIter < 0)
            errors.Add(new(P(prefix, "mesh.smooth"), Loc.S("SectionErrNonNegative")));
    }

    static void ValidateBars(MaterialArea a, string prefix, List<FieldError> errors)
    {
        var bars = a.Fibers.Where(f => f.TypeFiber == FiberType.point).ToList();
        if (bars.Count == 0) errors.Add(new(P(prefix, "bars"), Loc.S("SectionErrNoBars")));
        var host = a.HostArea;
        if (host != null && !IsHostCandidate(host))
            errors.Add(new(P(prefix, "host"), string.Format(Loc.S("SectionErrHostNotConcrete"), host.Tag)));
        string? hostWkt = host != null && IsHostCandidate(host) && !string.IsNullOrEmpty(host.WKT) ? host.WKT : null;
        for (int i = 0; i < bars.Count; i++)
        {
            var b = bars[i];
            if (!double.IsFinite(b.X) || !double.IsFinite(b.Y))
                errors.Add(new(P(prefix, $"bars[{i}]"), Loc.S("ContourErrNotFinite")));
            else if (hostWkt != null && !WktHelper.PointInPolygon(hostWkt, b.X, b.Y))
                errors.Add(new(P(prefix, $"bars[{i}]"), string.Format(Loc.S("SectionErrBarOutsideHost"), i + 1, host!.Tag)));
            if (!double.IsFinite(b.Diameter) || b.Diameter <= 0)
                errors.Add(new(P(prefix, $"bars[{i}].d"), string.Format(Loc.S("SectionErrBarDiameter"), i + 1)));
            else if (!double.IsFinite(b.Area) || b.Area <= 0)   // площадь из Ø — ошибка Ø уже названа
                errors.Add(new(P(prefix, $"bars[{i}].area"), string.Format(Loc.S("SectionErrBarArea"), i + 1)));
        }
        if (!double.IsFinite(a.SigSp) || a.SigSp < 0)
            errors.Add(new(P(prefix, "prestress.sigma"), Loc.S("SectionErrNonNegative")));
        if (!GammaSpValues.Any(g => Math.Abs(g - a.GammaSp) < 1e-9))
            errors.Add(new(P(prefix, "prestress.gamma"), Loc.S("SectionErrGammaSp")));
        if (a.RebarRepresentation == RebarRepresentation.IdealizedLayer && a.IdealizedAxis == null)
            errors.Add(new(P(prefix, "axis"), Loc.S("SectionErrIdealizedAxis")));
    }

    static void ValidateStirrupGroup(MaterialArea a, StirrupGroup g, IReadOnlyList<Material> materials, string path,
                                     List<FieldError> errors)
    {
        if (a.Category is not (AreaCategory.Stirrups or AreaCategory.Region))
        {
            errors.Add(new(path, Loc.S("SectionErrStirrupsOnRebar")));
            return;
        }
        int materialId = a.Category == AreaCategory.Stirrups && a.MaterialId > 0 ? a.MaterialId : g.MaterialId;
        var m = materials.FirstOrDefault(x => x.Id == materialId);
        if (a.Category == AreaCategory.Region && (m == null || !IsStirrupMaterial(m.Type)))
            errors.Add(new($"{path}.material", string.Format(Loc.S("SectionErrStirrupMaterial"), m?.Tag ?? "?")));
        if (!double.IsFinite(g.SpacingM) || g.SpacingM <= 0)
            errors.Add(new($"{path}.spacing", Loc.S("SectionErrPositive")));
        if (g.OffsetM is double off && (!double.IsFinite(off) || off < 0))
            errors.Add(new($"{path}.offset", Loc.S("SectionErrNonNegative")));
        if (g.Elements.Count == 0)
            errors.Add(new($"{path}.elements", Loc.S("SectionErrStirrupsEmpty")));
        for (int i = 0; i < g.Elements.Count; i++)
        {
            var e = g.Elements[i];
            string ep = $"{path}.elements[{i}]";
            if (!double.IsFinite(e.BarDiameterM) || e.BarDiameterM <= 0)
                errors.Add(new($"{ep}.d", string.Format(Loc.S("SectionErrStirrupDiameter"), i + 1)));
            if (!double.IsFinite(e.BarAreaM2) || e.BarAreaM2 <= 0)
                errors.Add(new($"{ep}.area", string.Format(Loc.S("SectionErrStirrupArea"), i + 1)));
            var line = e.CenterlineContour;
            var pts = line.X.Zip(line.Y, (x, y) => (x, y)).ToList();
            if (pts.Any(p => !double.IsFinite(p.x) || !double.IsFinite(p.y)))
                errors.Add(new($"{ep}.points", Loc.S("ContourErrNotFinite")));
            else if (e.IsClosed)
            {
                var ring = ContourAuthoring.OpenRing(pts);
                if (ring.Distinct().Count() < 3)
                    errors.Add(new($"{ep}.points", string.Format(Loc.S("SectionErrStirrupGeometry"), i + 1)));
            }
            else if (pts.Count < 2 || Length(pts) <= 1e-9)
                errors.Add(new($"{ep}.points", string.Format(Loc.S("SectionErrStirrupGeometry"), i + 1)));
        }
    }

    static double Length(List<(double x, double y)> pts)
    {
        double s = 0;
        for (int i = 1; i < pts.Count; i++)
            s += Math.Sqrt(Math.Pow(pts[i].x - pts[i - 1].x, 2) + Math.Pow(pts[i].y - pts[i - 1].y, 2));
        return s;
    }

    static List<(double X, double Y)> Ring(Contour c) =>
        c.X.Count > 0 ? c.X.Zip(c.Y, (x, y) => (x, y)).ToList() : c.Points.Select(p => (p.X, p.Y)).ToList();

    /// <summary>
    /// Проверка сечения: обозначение, состав (не пуст, без повтора обозначений), каждая область, носитель группы
    /// стержней — в том же сечении (у стадийного — в той же части), части стадийного.
    /// </summary>
    public static IReadOnlyList<FieldError> Validate(CrossSection s, IReadOnlyList<Material> materials,
                                                    IEnumerable<CrossSection> allSections)
    {
        var errors = new List<FieldError>();
        if (string.IsNullOrWhiteSpace(s.Tag)) errors.Add(new("tag", Loc.S("SectionErrTagEmpty")));
        if (s is StagedSection staged)
        {
            ValidateStaged(staged, materials, allSections, errors);
            return errors;
        }
        ValidateAreaSet(s.Areas, materials, "areas", errors);
        return errors;
    }

    static void ValidateAreaSet(IReadOnlyList<MaterialArea> areas, IReadOnlyList<Material> materials, string path,
                                List<FieldError> errors)
    {
        if (!areas.Any(MaterialArea.IsCalcActive))
            errors.Add(new(path, Loc.S("SectionErrNoAreas")));
        var tags = new HashSet<string>();
        for (int i = 0; i < areas.Count; i++)
        {
            var a = areas[i];
            string ap = $"{path}[{i}]";
            if (!string.IsNullOrWhiteSpace(a.Tag) && !tags.Add(a.Tag.Trim()))
                errors.Add(new($"{ap}.tag", string.Format(Loc.S("SectionErrAreaTagDuplicate"), a.Tag.Trim())));
            errors.AddRange(ValidateArea(a, materials, ap));
            if (a.Category == AreaCategory.RebarGroup && a.HostArea != null && !areas.Contains(a.HostArea))
                errors.Add(new($"{ap}.host", string.Format(Loc.S("SectionErrHostNotInSection"), a.HostArea.Tag)));
        }
    }

    static void ValidateStaged(StagedSection s, IReadOnlyList<Material> materials, IEnumerable<CrossSection> all,
                               List<FieldError> errors)
    {
        if (s.Parts.Count == 0) errors.Add(new("staged.parts", Loc.S("SectionErrNoParts")));
        var sections = all.ToList();
        for (int k = 0; k < s.Parts.Count; k++)
        {
            var part = s.Parts[k];
            string pp = $"staged.parts[{k}]";
            if (string.IsNullOrWhiteSpace(part.Tag)) errors.Add(new($"{pp}.tag", Loc.S("SectionErrTagEmpty")));
            if (part.SourceSectionId is int srcId)
            {
                var src = sections.FirstOrDefault(c => c.Id == srcId);
                if (src == null || src.Id == s.Id)
                    errors.Add(new($"{pp}.section", Loc.S("SectionErrPartSourceMissing")));
                else if (src is StagedSection)
                    errors.Add(new($"{pp}.section", string.Format(Loc.S("SectionErrPartSourceStaged"), src.Tag)));
            }
            else
                ValidateAreaSet(part.Areas, materials, $"{pp}.areas", errors);
        }
    }

    /// <summary>
    /// Проверяет и записывает область пула: номер, Id носителя, диаграммы; у группы стержней — круги пула
    /// (<see cref="SyncBarCircles"/>). Новая — добавляется в <see cref="DatabaseService.MaterialAreas"/>.
    /// </summary>
    public static IReadOnlyList<FieldError> SaveArea(DatabaseService db, MaterialArea a, CalcSettings calc)
    {
        var errors = ValidateArea(a, db.Materials);
        if (errors.Count > 0) return errors;
        WriteArea(db, a, calc);
        return [];
    }

    static void WriteArea(DatabaseService db, MaterialArea a, CalcSettings calc)
    {
        a.Material ??= db.Materials.FirstOrDefault(m => m.Id == a.MaterialId);
        if (a.Material != null)
        {
            a.MaterialId = a.Material.Id;
            if (a.Material.Type != MatType.Custom)
                a.DiagrammType = DiagrammCompatibility.Coerce(a.Material.Type, a.DiagrammType);
        }
        if (a.Category == AreaCategory.Stirrups)
        {
            a.HostArea = null;
            a.HostAreaId = null;
            foreach (var g in a.Stirrups) g.MaterialId = a.MaterialId;
        }
        else
            a.HostAreaId = a.HostArea?.Id is int hid and > 0 ? hid : a.HostArea == null ? null : a.HostAreaId;
        if (a.Num == 0) a.Num = db.MaterialAreas.Count > 0 ? db.MaterialAreas.Max(x => x.Num) + 1 : 1;
        if (a.Category == AreaCategory.Region) a.SetWKT();
        if (a.HostArea is { Diagramms.Count: 0 } host)
            host.ResolveAndBuildDiagramms(calc.Sp63DescEtaMin, db.Diagrams, calc.RebarDifferentialDiagram, calc.EkbDescEtaMin);
        a.ResolveAndBuildDiagramms(calc.Sp63DescEtaMin, db.Diagrams, calc.RebarDifferentialDiagram, calc.EkbDescEtaMin);
        db.SaveMaterialArea(a);   // новая — добавляется в db.MaterialAreas
        if (a.Category == AreaCategory.RebarGroup) SyncBarCircles(db, a);
    }

    /// <summary>
    /// Проверяет и записывает сечение с изменёнными областями <paramref name="areas"/> (сначала бетонные — Id носителя
    /// нужен группам стержней): номер, состав или части, диаграммы. Новое — в <see cref="DatabaseService.CrossSections"/>.
    /// </summary>
    public static IReadOnlyList<FieldError> Save(DatabaseService db, CrossSection s, CalcSettings calc,
                                                IEnumerable<MaterialArea>? areas = null)
    {
        var errors = Validate(s, db.Materials, db.CrossSections);
        if (errors.Count > 0) return errors;
        var dirty = (areas ?? []).Concat(AllOwnAreas(s).Where(a => a.Id == 0)).Distinct().ToList();
        foreach (var a in dirty.OrderBy(a => a.Category == AreaCategory.Region ? 0 : 1))
            WriteArea(db, a, calc);
        if (s.Num == 0) s.Num = db.CrossSections.Count > 0 ? db.CrossSections.Max(c => c.Num) + 1 : 1;
        if (s is StagedSection staged)
        {
            for (int i = 0; i < staged.Parts.Count; i++)
            {
                staged.Parts[i].Index = i;
                if (staged.Parts[i].SourceSectionId is int src)
                    staged.Parts[i].Areas = db.CrossSections.First(c => c.Id == src).Areas;
            }
            staged.SyncAreas();
        }
        db.SaveCrossSection(s);
        if (!db.CrossSections.Contains(s)) db.CrossSections.Add(s);
        s.ResolveAndBuildDiagramms(calc.Sp63DescEtaMin, db.Diagrams, calc.RebarDifferentialDiagram, calc.EkbDescEtaMin);
        return [];
    }

    /// <summary>Области сечения: у обычного — состав, у стадийного — свои области частей (без ссылочных).</summary>
    public static IEnumerable<MaterialArea> AllOwnAreas(CrossSection s) =>
        s is StagedSection st ? st.Parts.Where(p => p.SourceSectionId == null).SelectMany(p => p.Areas) : s.Areas;

    /// <summary>Кто использует область: сечения и части стадийных, группы стержней с этим носителем.</summary>
    public static List<string> AreaUsedBy(DatabaseService db, MaterialArea a)
    {
        var users = new List<string>();
        foreach (var s in db.CrossSections)
            if (AllOwnAreas(s).Contains(a))
                users.Add(string.Format(Loc.S("SectionUsedBySection"), s.Tag));
        if (a.Id > 0)
            foreach (var r in db.MaterialAreas.Where(r => r != a && (r.HostAreaId == a.Id || ReferenceEquals(r.HostArea, a))))
                users.Add(string.Format(Loc.S("SectionUsedByRebarHost"), r.Tag));
        return users.Distinct().ToList();
    }

    /// <summary>Удаляет область, если её никто не использует; иначе — false и список пользователей.</summary>
    public static bool TryDeleteArea(DatabaseService db, MaterialArea a, out List<string> usedBy)
    {
        usedBy = AreaUsedBy(db, a);
        if (usedBy.Count > 0) return false;
        db.DeleteMaterialArea(a);
        DeleteBarCircles(db, a);
        return true;
    }

    /// <summary>Кто использует сечение: стадийные сечения, задачи расчёта, сечения огнестойкости, КонЭ и КЭ схем.</summary>
    public static List<string> SectionUsedBy(DatabaseService db, CrossSection s)
    {
        var users = new List<string>();
        if (s.Id <= 0) return users;
        foreach (var st in db.SectionsUsingAsPart(s.Id))
            users.Add(string.Format(Loc.S("SectionUsedByStaged"), st.Tag));
        var (tasks, fire, members, elements) = db.CrossSectionReferences(s.Id);
        users.AddRange(tasks.Select(t => string.Format(Loc.S("SectionUsedByTask"), t)));
        users.AddRange(fire.Select(t => string.Format(Loc.S("SectionUsedByFire"), t)));
        if (members > 0) users.Add(string.Format(Loc.S("SectionUsedByFemMembers"), members));
        if (elements > 0) users.Add(string.Format(Loc.S("SectionUsedByFemElements"), elements));
        return users;
    }

    /// <summary>Удаляет сечение (области пула остаются), если его никто не использует.</summary>
    public static bool TryDeleteSection(DatabaseService db, CrossSection s, out List<string> usedBy)
    {
        usedBy = SectionUsedBy(db, s);
        if (usedBy.Count > 0) return false;
        if (s.Id > 0) db.DeleteCrossSection(s);
        else db.CrossSections.Remove(s);
        return true;
    }

    /// <summary>Набор кругов пула, в котором отражены стержни группы (узел «Геометрия / Окружности»).</summary>
    public static string BarCircleSet(MaterialArea a) => $"RebarGroup#{a.Id}";

    /// <summary>Служебный набор кругов группы стержней — круги производные, в документ не выгружаются.</summary>
    public static bool IsBarCircleSet(string? set) => set != null && set.StartsWith("RebarGroup#", StringComparison.Ordinal);

    /// <summary>Пересоздаёт круги пула со стержнями группы (набор <see cref="BarCircleSet"/>).</summary>
    public static void SyncBarCircles(DatabaseService db, MaterialArea a)
    {
        DeleteBarCircles(db, a);
        string set = BarCircleSet(a);
        int next = db.Circles.Count > 0 ? db.Circles.Max(c => c.Num) + 1 : 1;
        foreach (var f in a.Fibers.Where(f => f.TypeFiber == FiberType.point))
        {
            var c = new CircleP(f.X, f.Y, f.Diameter / 2) { Tag = a.Tag, GeometrySet = set, Num = next++ };
            db.SaveCircle(c);
            db.Circles.Add(c);
        }
    }

    static void DeleteBarCircles(DatabaseService db, MaterialArea a)
    {
        string set = BarCircleSet(a);
        foreach (var c in db.Circles.Where(c => c.GeometrySet == set).ToList())
            ContourAuthoring.DeleteCircle(db, c);
    }
}
