using System.Text.Json;
using CScore;
using CScore.PlateRebar;
using OpenCS.Utilites;

namespace OpenCS.Services;

/// <summary>
/// Проверка, сохранение и удаление сечений пластин — одна для страницы сечения пластины и консоли
/// (<c>opencs apply</c>). Единицы — базы (м, м²/м). Пути ошибок — как в документе opencs-model (<c>plateSections[]</c>):
/// <c>tag</c>, <c>h</c>, <c>nLayers</c>, <c>concrete</c>, <c>rebar</c>, <c>diagram</c>, <c>rebarLayers[1].x.step</c>,
/// <c>rebarLayers[0].y.z</c>, <c>rebarLayers[2].material</c>.
/// </summary>
public static class PlateSectionAuthoring
{
    /// <summary>Способы задания слоя: Ø + шаг, Ø + число на метр, площадь напрямую.</summary>
    public static readonly string[] InputModes = ["diameter_spacing", "diameter_count", "direct"];

    /// <summary>Модели интегрирования по толщине.</summary>
    public static readonly string[] PlateModels = ["layered", "char1d_principal", "char1d_axial"];

    /// <summary>Модели снижения прочности бетона: без снижения, Веккьо — Коллинз.</summary>
    public static readonly string[] SofteningModels = ["", "vecchio_collins"];

    /// <summary>Материал арматуры пластины: стержневая арматура.</summary>
    public static bool IsRebarMaterial(MatType t) => t is MatType.ReSteelF or MatType.ReSteelU;

    /// <summary>Проверка сечения перед записью.</summary>
    public static IReadOnlyList<FieldError> Validate(PlateSection ps, IReadOnlyList<Material> materials)
    {
        var errors = new List<FieldError>();
        if (string.IsNullOrWhiteSpace(ps.Tag)) errors.Add(new("tag", Loc.S("SectionErrTagEmpty")));
        if (!double.IsFinite(ps.H) || ps.H <= 0) errors.Add(new("h", Loc.S("SectionErrPositive")));
        if (ps.NLayers < 1) errors.Add(new("nLayers", Loc.S("PlateErrNLayers")));
        if (!PlateModels.Contains(string.IsNullOrEmpty(ps.PlateModel) ? "layered" : ps.PlateModel))
            errors.Add(new("model", Loc.S("PlateErrModel")));
        if (!SofteningModels.Contains(ps.SofteningModel ?? ""))
            errors.Add(new("softening", Loc.S("PlateErrModel")));
        if (!double.IsFinite(ps.SofteningEpsC2) || ps.SofteningEpsC2 <= 0)
            errors.Add(new("epsC2", Loc.S("SectionErrPositive")));

        var concrete = materials.FirstOrDefault(m => m.Id == ps.ConcreteMaterialId && ps.ConcreteMaterialId != 0);
        if (concrete == null) errors.Add(new("concrete", Loc.S("SectionErrNoMaterial")));
        else if (concrete.Type != MatType.Concrete)
            errors.Add(new("concrete", string.Format(Loc.S("PlateErrConcreteMaterial"), concrete.Tag)));
        if (!DiagrammCompatibility.Allowed(MatType.Concrete).Contains(ps.ConcreteDiagramType))
            errors.Add(new("diagram", Loc.S("PlateErrDiagram")));

        var rebar = materials.FirstOrDefault(m => m.Id == ps.RebarMaterialId && ps.RebarMaterialId != 0);
        if (rebar == null) errors.Add(new("rebar", Loc.S("SectionErrNoMaterial")));
        else if (!IsRebarMaterial(rebar.Type))
            errors.Add(new("rebar", string.Format(Loc.S("SectionErrRebarMaterial"), rebar.Tag)));

        for (int i = 0; i < ps.RebarLayers.Count; i++)
            ValidateLayer(ps.RebarLayers[i], ps.H, materials, $"rebarLayers[{i}]", i + 1, errors);
        return errors;
    }

    static void ValidateLayer(PlateRebarLayer l, double h, IReadOnlyList<Material> materials, string path, int n,
                              List<FieldError> errors)
    {
        if (!InputModes.Contains(l.InputMode))
        {
            errors.Add(new($"{path}.mode", Loc.S("PlateErrLayerMode")));
            return;
        }
        bool x = ValidateDirection(l.InputMode, l.DiameterX, l.SpacingX, l.CountPerMeterX, l.Asx, $"{path}.x", n, errors);
        bool y = ValidateDirection(l.InputMode, l.DiameterY, l.SpacingY, l.CountPerMeterY, l.Asy, $"{path}.y", n, errors);
        if (!x && !y && !errors.Any(e => e.Path.StartsWith(path + ".", StringComparison.Ordinal)))
            errors.Add(new(path, string.Format(Loc.S("PlateErrLayerEmpty"), n)));
        // Положение — только у направления с арматурой: у пустого Zs не используется.
        if (x) ValidateZ(l.Zsx, h, $"{path}.x.z", n, errors);
        if (y) ValidateZ(l.Zsy, h, $"{path}.y.z", n, errors);
        if (l.MaterialId != 0)
        {
            var m = materials.FirstOrDefault(m => m.Id == l.MaterialId);
            if (m == null) errors.Add(new($"{path}.material", Loc.S("SectionErrNoMaterial")));
            else if (!IsRebarMaterial(m.Type))
                errors.Add(new($"{path}.material", string.Format(Loc.S("SectionErrRebarMaterial"), m.Tag)));
        }
        if (!double.IsFinite(l.Angle)) errors.Add(new($"{path}.angle", Loc.S("ContourErrNotFinite")));
    }

    /// <summary>Проверка одного направления слоя; true — у направления есть арматура.</summary>
    static bool ValidateDirection(string mode, double d, double spacing, double count, double area, string path, int n,
                                  List<FieldError> errors)
    {
        if (mode == "direct")
        {
            if (!double.IsFinite(area) || area < 0)
            {
                errors.Add(new($"{path}.area", Loc.S("SectionErrNonNegative")));
                return false;
            }
            return area > 0;
        }
        string second = mode == "diameter_spacing" ? "step" : "count";
        double other = mode == "diameter_spacing" ? spacing : count;
        // Ø и шаг (число) нули оба — направление без арматуры; одно без другого — ошибка.
        if (d == 0 && other == 0) return false;
        bool ok = true;
        if (!double.IsFinite(d) || d <= 0)
        {
            errors.Add(new($"{path}.d", string.Format(Loc.S("PlateErrLayerDiameter"), n)));
            ok = false;
        }
        if (!double.IsFinite(other) || other <= 0)
        {
            errors.Add(new($"{path}.{second}", string.Format(Loc.S(mode == "diameter_spacing" ? "PlateErrLayerStep" : "PlateErrLayerCount"), n)));
            ok = false;
        }
        return ok;
    }

    static void ValidateZ(double z, double h, string path, int n, List<FieldError> errors)
    {
        if (!double.IsFinite(z))
            errors.Add(new(path, Loc.S("ContourErrNotFinite")));
        else if (double.IsFinite(h) && h > 0 && Math.Abs(z) >= h / 2)
            errors.Add(new(path, string.Format(Loc.S("PlateErrLayerZ"), n, Math.Round(h / 2 * 1000, 3))));
    }

    /// <summary>
    /// Переносит расчётные поля <paramref name="src"/> в <paramref name="dst"/> (слои — копиями); Id, номер и признак
    /// сечения области схемы не трогает.
    /// </summary>
    public static void CopyInto(PlateSection src, PlateSection dst)
    {
        dst.Tag = src.Tag;
        dst.H = src.H;
        dst.NLayers = src.NLayers;
        dst.ConcreteMaterialId = src.ConcreteMaterialId;
        dst.RebarMaterialId = src.RebarMaterialId;
        dst.TensionConcrete = src.TensionConcrete;
        dst.SofteningModel = src.SofteningModel;
        dst.SofteningEpsC2 = src.SofteningEpsC2;
        dst.PlateModel = src.PlateModel;
        dst.ConcreteDiagramType = src.ConcreteDiagramType;
        dst.RebarLayers = src.RebarLayers.Select(l => l.Clone()).ToList();
    }

    /// <summary>Копия для правки на странице: те же Id, номер и признак области, слои — копиями.</summary>
    public static PlateSection Draft(PlateSection src)
    {
        var d = new PlateSection { Id = src.Id, Num = src.Num, GeneratedForRegionId = src.GeneratedForRegionId };
        CopyInto(src, d);
        return d;
    }

    /// <summary>Отпечаток записываемых полей — для «*» страницы.</summary>
    public static string Fingerprint(PlateSection ps) => JsonSerializer.Serialize(new
    {
        ps.Tag, ps.H, ps.NLayers, ps.ConcreteMaterialId, ps.RebarMaterialId, ps.TensionConcrete,
        Softening = ps.SofteningModel ?? "", ps.SofteningEpsC2,
        Model = string.IsNullOrEmpty(ps.PlateModel) ? "layered" : ps.PlateModel,
        Diagram = ps.ConcreteDiagramType.ToString(),
        Layers = ps.RebarLayers.Select(l => new
        {
            l.Name, l.InputMode, l.Asx, l.Asy, l.Zsx, l.Zsy, l.DiameterX, l.DiameterY, l.CountPerMeterX,
            l.CountPerMeterY, l.SpacingX, l.SpacingY, l.MaterialId, Face = l.Face.ToString(), l.Angle
        })
    });

    /// <summary>Проверяет и записывает сечение: площади слоёв — по способу задания, номер; новое — в пул проекта.</summary>
    public static IReadOnlyList<FieldError> Save(DatabaseService db, PlateSection ps)
    {
        foreach (var l in ps.RebarLayers) l.RecalcArea();
        var errors = Validate(ps, db.Materials);
        if (errors.Count > 0) return errors;
        if (string.IsNullOrEmpty(ps.PlateModel)) ps.PlateModel = "layered";
        if (ps.Num == 0) ps.Num = db.PlateSections.Count > 0 ? db.PlateSections.Max(s => s.Num) + 1 : 1;
        db.SavePlateSection(ps);   // новое — добавляется в db.PlateSections
        return [];
    }

    /// <summary>Сечение ссылается на материал (бетон, общая арматура, материал слоя).</summary>
    public static bool UsesMaterial(PlateSection ps, int materialId) =>
        materialId != 0 && (ps.ConcreteMaterialId == materialId || ps.RebarMaterialId == materialId
                            || ps.RebarLayers.Any(l => l.MaterialId == materialId));

    /// <summary>Кто использует сечение: задачи пластин, КонЭ и группы КЭ схем, эквивалентные сечения полос.</summary>
    public static List<string> UsedBy(DatabaseService db, PlateSection ps)
    {
        var users = new List<string>();
        if (ps.Id <= 0) return users;
        var (tasks, members, groups, equivalents) = db.PlateSectionReferences(ps.Id);
        users.AddRange(tasks.Select(t => string.Format(Loc.S("SectionUsedByTask"), t)));
        if (members > 0) users.Add(string.Format(Loc.S("SectionUsedByFemMembers"), members));
        users.AddRange(groups.Select(t => string.Format(Loc.S("PlateUsedByFemGroup"), t)));
        users.AddRange(equivalents.Select(t => string.Format(Loc.S("PlateUsedByEquivalent"), t)));
        return users;
    }

    /// <summary>Удаляет сечение, если его никто не использует; иначе — false и список пользователей.</summary>
    public static bool TryDelete(DatabaseService db, PlateSection ps, out List<string> usedBy)
    {
        usedBy = UsedBy(db, ps);
        if (usedBy.Count > 0) return false;
        db.DeletePlateSection(ps);
        return true;
    }
}
