using CScore;
using CScore.Fem;
using CScore.Import;
using OpenCS.Utilites;

namespace OpenCS.Services;

/// <summary>Что сделано при создании сечений пластин по данным ЛИРЫ.</summary>
public sealed class LiraPlateSectionsReport
{
    /// <summary>У схемы нет файла подбора (ASP) — взять толщину и классы неоткуда.</summary>
    public bool NoAsp { get; init; }
    /// <summary>Пользователь отказался вводить привязку — ничего не создано.</summary>
    public bool Cancelled { get; init; }
    /// <summary>Созданные материалы.</summary>
    public List<string> Materials { get; } = [];
    /// <summary>Созданные пластинчатые сечения.</summary>
    public List<string> Sections { get; } = [];
    /// <summary>Цели, получившие сечение.</summary>
    public List<(string Target, string Section)> Assigned { get; } = [];
    /// <summary>Цели, для которых сечение создать не удалось, и причина.</summary>
    public List<(string Target, string Reason)> Skipped { get; } = [];
    /// <summary>Цели с КЭ других сочетаний «толщина + классы» (перечень сочетаний).</summary>
    public List<(string Target, string Combos)> OtherCombos { get; } = [];
    /// <summary>Цели, в шаблон которых поставлена условная арматура (перечень граней).</summary>
    public List<(string Target, string Faces)> Nominal { get; } = [];
    /// <summary>Условная арматура, введённая пользователем.</summary>
    public LiraPlateNominalRebar? NominalRebar { get; set; }
}

/// <summary>
/// Создание сечений-шаблонов пластинчатых целей по данным ЛИРЫ: материалы — по классам из подбора (*.asp)
/// и справочника СП 63, толщина — из подбора, арматура — фоновые ТЗА. Одинаковые шаблоны разных целей
/// дают одно сечение; существующие материалы и сечения проекта используются повторно.
/// </summary>
public static class LiraPlateSectionCreator
{
    /// <summary>Создать и назначить сечения целям без пластинчатого сечения.</summary>
    /// <param name="targets">Группы и конструктивные элементы; цели с сечением и без пластин пропускаются.</param>
    /// <param name="askNominal">Запрос привязки и диаметра для граней без фонового ТЗА (аргумент — предлагаемые
    /// значения); null — отказ, ничего не создаётся.</param>
    /// <param name="catalogDirectory">Каталог справочника материалов; null — рядом с приложением.</param>
    public static LiraPlateSectionsReport Create(
        DatabaseService db, FemCheckSchemaData data, IEnumerable<IFemCheckable> targets,
        Func<LiraPlateNominalRebar, LiraPlateNominalRebar?> askNominal, string? catalogDirectory = null)
    {
        if (data.Asp == null) return new LiraPlateSectionsReport { NoAsp = true };

        // ── План: сначала все шаблоны, потом запись — отказ от ввода не оставляет половину работы ──
        var report = new LiraPlateSectionsReport();
        var plans = new List<(IFemCheckable Target, LiraPlateTemplate Template)>();
        LiraPlateNominalRebar? nominal = null;
        foreach (var target in targets)
        {
            if (PlateSectionId(target) != null) continue;
            var plates = data.Scope(target).Elements.Where(e => e.Element.ElemType == "shell").ToList();
            if (plates.Count == 0) continue;

            var result = LiraPlateSectionTemplates.Build(plates, data.Asp, data.Rbt, nominal);
            while (result.NeedsNominal)
            {
                // Значение неприемлемо для этой цели (привязка больше половины толщины) — спрашиваем заново.
                nominal = askNominal(result.Suggested);
                if (nominal == null) return new LiraPlateSectionsReport { Cancelled = true };
                result = LiraPlateSectionTemplates.Build(plates, data.Asp, data.Rbt, nominal);
            }
            if (result.Template == null) { report.Skipped.Add((target.Tag, result.Problem ?? "")); continue; }
            plans.Add((target, result.Template));
        }
        report.NominalRebar = nominal;

        // ── Запись ───────────────────────────────────────────────────────────────────────────────
        foreach (var (target, template) in plans)
        {
            var concrete = FindOrCreate(db, report, template.Combo.ConcreteClass, isConcrete: true, catalogDirectory);
            if (concrete == null)
            {
                report.Skipped.Add((target.Tag, $"класса бетона {template.Combo.ConcreteClass} нет в справочнике тяжёлого бетона"));
                continue;
            }
            var rebar = FindOrCreate(db, report, template.Combo.RebarClass, isConcrete: false, catalogDirectory);
            if (rebar == null)
            {
                report.Skipped.Add((target.Tag, $"класса арматуры {template.Combo.RebarClass} нет в справочнике арматуры"));
                continue;
            }

            var section = FindOrCreateSection(db, report, template, concrete, rebar);
            switch (target)
            {
                case FemMemberGroup group:
                    group.PlateSectionId = section.Id;
                    db.SaveFemMemberGroup(group);
                    break;
                case FemMember member:
                    member.PlateSectionId = section.Id;
                    db.SaveFemMember(member);
                    break;
            }
            report.Assigned.Add((target.Tag, section.Tag));
            if (template.OtherCombos.Count > 0)
                report.OtherCombos.Add((target.Tag,
                    string.Join("; ", template.OtherCombos.Select(c => $"{c.Combo.Label} — {c.Count} КЭ"))));
            if (template.NominalFaces.Count > 0)
                report.Nominal.Add((target.Tag, string.Join(", ", template.NominalFaces)));
        }
        return report;
    }

    /// <summary>Пластинчатое сечение цели; null — не назначено или цель не группа и не элемент.</summary>
    public static int? PlateSectionId(IFemCheckable target) => target switch
    {
        FemMemberGroup group => group.PlateSectionId,
        FemMember member     => member.PlateSectionId,
        _                    => null,
    };

    static Material? FindOrCreate(
        DatabaseService db, LiraPlateSectionsReport report, string materialClass, bool isConcrete, string? directory)
    {
        string key = MaterialCatalog.ClassKey(materialClass);
        if (key.Length == 0) return null;
        var existing = db.Materials.FirstOrDefault(m =>
            (isConcrete ? m.Type == MatType.Concrete : m.Type is MatType.ReSteelF or MatType.ReSteelU)
            && MaterialCatalog.ClassKey(m.Tag) == key);
        if (existing != null) return existing;

        var created = isConcrete ? MaterialCatalog.CreateHeavyConcrete(materialClass, directory)
                                 : MaterialCatalog.CreateRebar(materialClass, directory);
        if (created == null) return null;
        db.AddMaterial(created);
        report.Materials.Add(created.Tag);
        return created;
    }

    static PlateSection FindOrCreateSection(
        DatabaseService db, LiraPlateSectionsReport report, LiraPlateTemplate template, Material concrete, Material rebar)
    {
        string key = PlateElementSectionFactory.Key(template.Combo.ThicknessM, template.Layers);
        bool Same(PlateSection s) =>
            s.ConcreteMaterialId == concrete.Id && s.RebarMaterialId == rebar.Id
            && PlateElementSectionFactory.Key(s.H, s.RebarLayers) == key;

        // Имя занято сечением с другим содержимым (его правили вручную) — новое получает номер.
        string tag = template.Tag;
        for (int n = 2; ; n++)
        {
            var named = db.PlateSections.Where(s => s.Tag == tag).ToList();
            if (named.FirstOrDefault(Same) is { } existing) return existing;
            if (named.Count == 0) break;
            tag = $"{template.Tag} ({n})";
        }

        var section = new PlateSection
        {
            Num = db.PlateSections.Count > 0 ? db.PlateSections.Max(s => s.Num) + 1 : 1,
            Tag = tag,
            H = template.Combo.ThicknessM,
            // Подбор ЛИРЫ близок к минимальному армированию: сжатая зона — несколько миллиметров,
            // и десяти слоёв по толщине слоистой модели на неё не хватает.
            NLayers = ShellLayeredCheck.RefinedLayers,
            ConcreteMaterialId = concrete.Id,
            RebarMaterialId = rebar.Id,
            RebarLayers = [.. template.Layers.Select(l => l.Clone())],
        };
        db.SavePlateSection(section);
        report.Sections.Add(section.Tag);
        return section;
    }
}
