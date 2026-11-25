using CScore.Fem;
using CScore.Fem.Editing;
using OpenCS.Utilites;

namespace OpenCS.Services;

/// <summary>Что удалится вместе со схемой (для вопроса пользователю) и почему удалить нельзя.</summary>
/// <param name="Analyses">Постановки расчёта.</param>
/// <param name="Results">Сохранённые результаты постановок.</param>
/// <param name="Checks">Проверки по КЭ.</param>
/// <param name="Fields">Поля из таблиц точек.</param>
/// <param name="Imperfections">Элементы несовершенств.</param>
/// <param name="Fragments">Имена схем-фрагментов этой схемы (остаются, теряют связь с родителем).</param>
/// <param name="Submodels">Имена субмоделей, извлечённых из схемы: пока они есть, удалить нельзя.</param>
public sealed record FemSchemaDeleteImpact(int Analyses, int Results, int Checks, int Fields, int Imperfections,
    IReadOnlyList<string> Fragments, IReadOnlyList<string> Submodels)
{
    /// <summary>Удаление запрещено.</summary>
    public bool Blocked => Submodels.Count > 0;
}

/// <summary>
/// Имя, проверка, запись, копия и удаление МКЭ-схем — одно для дерева, страницы схемы и вида <c>femSchemas</c>
/// документа <c>opencs-model</c>. Проверка состава — топология, канон нагрузок и определения комбинаций
/// (<see cref="FemTopologyValidator"/>, <see cref="FemCanonicalValidator"/>, <see cref="FemLoadDefinitionValidator"/>).
/// </summary>
public static class FemSchemaAuthoring
{
    /// <summary>Родная схема (создана в OpenCS, а не импортом, фрагментом или субмоделью).</summary>
    public static bool IsNative(FemSchema s) => s.SourceType == "internal" && s.ParentSchemaId == null;

    // ── Имя ──────────────────────────────────────────────────────────────────────────────────

    /// <summary>Ошибка имени (пусто или уже есть у другой схемы) — <c>null</c>, если имя годится.</summary>
    public static string? TagError(DatabaseService db, string? tag, int selfId = 0)
    {
        string t = (tag ?? "").Trim();
        if (t.Length == 0) return Loc.S("FemSchemaErrTagEmpty");
        return db.FemSchemas.Any(s => s.Id != selfId && string.Equals(s.Tag.Trim(), t, StringComparison.CurrentCultureIgnoreCase))
            ? string.Format(Loc.S("FemSchemaErrTagDuplicate"), t)
            : null;
    }

    /// <summary>Первое свободное имя «Схема N».</summary>
    public static string DefaultTag(DatabaseService db) => FreeTag(db, n => string.Format(Loc.S("FemSchemaDefaultTag"), n));

    /// <summary>Свободное имя копии: «Имя (копия)», «Имя (копия 2)»…</summary>
    public static string CopyTag(DatabaseService db, string source)
    {
        string first = string.Format(Loc.S("FemSchemaCopyTag"), source);
        return TagError(db, first) == null ? first : FreeTag(db, n => string.Format(Loc.S("FemSchemaCopyTagN"), source, n), 2);
    }

    static string FreeTag(DatabaseService db, Func<int, string> name, int start = 1)
    {
        for (int n = start; ; n++)
            if (TagError(db, name(n)) == null) return name(n);
    }

    /// <summary>Новая пустая родная схема с именем <paramref name="tag"/> (или свободным «Схема N»).</summary>
    public static FemSchema CreateNew(DatabaseService db, string? tag = null)
    {
        string t = string.IsNullOrWhiteSpace(tag) ? DefaultTag(db) : tag.Trim();
        if (TagError(db, t) is { } err) throw new InvalidOperationException(err);
        var schema = new FemSchema { Tag = t, SourceType = "internal" };
        db.SaveFemSchema(schema);
        return schema;
    }

    /// <summary>Переименование: ошибка — схема не меняется.</summary>
    public static string? Rename(DatabaseService db, FemSchema schema, string tag)
    {
        if (TagError(db, tag, schema.Id) is { } err) return err;
        schema.Tag = tag.Trim();
        db.SaveFemSchema(schema);
        return null;
    }

    /// <summary>Копия схемы под свободным именем: вход, сетка и её ГУ; постановки, проверки и результаты — нет.</summary>
    public static FemSchema Duplicate(DatabaseService db, FemSchema source) => db.DuplicateFemSchema(source.Id, CopyTag(db, source.Tag));

    // ── Удаление ─────────────────────────────────────────────────────────────────────────────

    /// <summary>Что удалится со схемой и есть ли запрет (субмодели).</summary>
    public static FemSchemaDeleteImpact DeleteImpact(DatabaseService db, FemSchema schema)
    {
        var submodelIds = db.FemSubmodelSchemaIds(schema.Id);
        return new FemSchemaDeleteImpact(
            Analyses: db.GetFemAnalyses(schema.Id).Count,
            Results: db.FemSchemaResultCount(schema.Id),
            Checks: db.FemChecks.Count(c => c.SchemaId == schema.Id),
            Fields: db.GetFemFields(schema.Id).Count,
            Imperfections: db.GetFemImperfections(schema.Id).Count,
            Fragments: db.FemSchemas.Where(s => s.ParentSchemaId == schema.Id).Select(s => s.Tag).ToList(),
            Submodels: db.FemSchemas.Where(s => submodelIds.Contains(s.Id)).Select(s => s.Tag).ToList());
    }

    /// <summary>Текст вопроса об удалении: имя и непустые позиции состава.</summary>
    public static string DeleteQuestion(FemSchema schema, FemSchemaDeleteImpact impact)
    {
        var lines = new List<string>();
        void Add(int n, string key) { if (n > 0) lines.Add(string.Format(Loc.S(key), n)); }
        Add(impact.Analyses, "FemSchemaDeleteAnalyses");
        Add(impact.Results, "FemSchemaDeleteResults");
        Add(impact.Checks, "FemSchemaDeleteChecks");
        Add(impact.Fields, "FemSchemaDeleteFields");
        Add(impact.Imperfections, "FemSchemaDeleteImperfections");
        string text = string.Format(Loc.S("FemSchemaDeleteConfirm"), schema.Tag);
        if (lines.Count > 0) text += "\n\n" + Loc.S("FemSchemaDeleteAlso") + "\n" + string.Join("\n", lines.Select(l => "• " + l));
        if (impact.Fragments.Count > 0)
            text += "\n\n" + string.Format(Loc.S("FemSchemaDeleteFragments"), NameList(impact.Fragments));
        return text;
    }

    /// <summary>Текст отказа (субмодели), если удалить нельзя.</summary>
    public static string BlockedText(FemSchema schema, FemSchemaDeleteImpact impact) =>
        string.Format(Loc.S("FemSchemaDeleteHasSubmodels"), schema.Tag, NameList(impact.Submodels));

    /// <summary>Удаляет схему; при запрете — <c>false</c> и текст отказа.</summary>
    public static bool TryDelete(DatabaseService db, FemSchema schema, out string? refusal)
    {
        var impact = DeleteImpact(db, schema);
        if (impact.Blocked)
        {
            refusal = BlockedText(schema, impact);
            return false;
        }
        db.DeleteFemSchema(schema);
        refusal = null;
        return true;
    }

    static string NameList(IReadOnlyList<string> names) =>
        string.Join(", ", names.Take(5).Select(n => $"«{n}»")) + (names.Count > 5 ? " " + string.Format(Loc.S("UsedByMore"), names.Count - 5) : "");

    // ── Сессия правки ────────────────────────────────────────────────────────────────────────

    /// <summary>Заполняет сессию составом схемы из базы (узлы, КонЭ, нагрузки, линейные связи, пружины узлов КонЭ) и
    /// схемы (группы, загружения, определения); история — чистая.</summary>
    public static void LoadSession(DatabaseService db, FemSchemaEditSession session)
    {
        var schema = session.Schema;
        session.Nodes.Clear(); session.Members.Clear(); session.MemberGroups.Clear(); session.LoadCases.Clear();
        session.NodeLoads.Clear(); session.MemberLoads.Clear(); session.KinematicLoads.Clear(); session.ElementLoads.Clear();
        session.MeshNodeLoads.Clear(); session.LineSupports.Clear(); session.NodeSprings.Clear(); session.LoadDefinitions.Clear();
        session.Nodes.AddRange(db.GetFemNodes(schema.Id));
        session.Members.AddRange(db.GetFemMembers(schema.Id));
        session.MemberGroups.AddRange(schema.MemberGroups);
        session.LoadCases.AddRange(schema.LoadCases);
        session.NodeLoads.AddRange(db.GetFemNodeLoads(schema.Id));
        session.MemberLoads.AddRange(db.GetFemMemberLoads(schema.Id));
        session.KinematicLoads.AddRange(db.GetFemKinematicLoads(schema.Id));
        session.ElementLoads.AddRange(db.GetFemElementLoads(schema.Id));
        session.MeshNodeLoads.AddRange(db.GetFemMeshNodeLoads(schema.Id));
        session.LineSupports.AddRange(db.GetFemLineSupports(schema.Id));
        session.NodeSprings.AddRange(db.GetFemSprings(schema.Id).Where(s => s.TargetKind == FemSpringTargetKinds.Node));
        session.LoadDefinitions.AddRange(schema.LoadDefinitions);
        session.MarkSaved();
    }

    /// <summary>Сессия схемы из базы.</summary>
    public static FemSchemaEditSession OpenSession(DatabaseService db, FemSchema schema)
    {
        var session = new FemSchemaEditSession(schema);
        LoadSession(db, session);
        return session;
    }

    /// <summary>Что потеряет пластина при удалении: линейные связи граней, нагрузки на КЭ с целью «этот КонЭ», группы.</summary>
    public static (int LineSupports, int Loads, int Groups) PlanarDeleteImpact(FemSchemaEditSession session, string tag) => (
        session.LineSupports.Count(s => s.MemberTag == tag),
        session.ElementLoads.Count(l => l.TargetKind == FemLoadTargetKinds.Members && l.TargetTags.Contains(tag))
            + session.MemberLoads.Count(l => session.Members.Any(m => m.Id == l.MemberId && m.ElemTag == tag)),
        session.MemberGroups.Count(g => g.Kind == FemMemberGroup.KindMembers && g.Tags.Contains(tag)));

    /// <summary>Вопрос об удалении пластины: имя и что она потеряет.</summary>
    public static string PlanarDeleteQuestion(FemSchemaEditSession session, string tag)
    {
        var (supports, loads, groups) = PlanarDeleteImpact(session, tag);
        string text = string.Format(Loc.S("FemPlanarDeleteConfirm"), tag);
        var lines = new List<string>();
        if (supports > 0) lines.Add(string.Format(Loc.S("FemPlanarDeleteSupports"), supports));
        if (loads > 0) lines.Add(string.Format(Loc.S("FemPlanarDeleteLoads"), loads));
        if (groups > 0) lines.Add(string.Format(Loc.S("FemPlanarDeleteGroups"), groups));
        return lines.Count == 0 ? text : text + "\n\n" + string.Join("\n", lines.Select(l => "• " + l));
    }

    /// <summary>
    /// Удаляет пластинчатый КонЭ вне редактора схемы (дерево, таблица «Пластины»): та же команда, что в редакторе
    /// (линейные связи, нагрузки, состав групп), затем запись и удаление осиротевшей области.
    /// </summary>
    public static void DeletePlanarMember(DatabaseService db, FemSchema schema, string tag)
    {
        var session = OpenSession(db, schema);
        if (session.Members.FirstOrDefault(m => m.ElemTag == tag) is not { } member) return;
        session.Execute(new DeleteMemberCommand(member));
        Write(db, session);
    }

    /// <summary>Области, на которые сейчас (в базе) ссылаются КонЭ схемы.</summary>
    static HashSet<int> ReferencedRegions(IEnumerable<FemMember> members) =>
        members.Where(m => m.PlanarRegionId != null).Select(m => m.PlanarRegionId!.Value).ToHashSet();

    /// <summary>Удаляет области, на которые до записи ссылался КонЭ, а после — ни один (пластина удалена в сессии).
    /// Области без КонЭ по иным причинам не трогаются.</summary>
    static int PruneOrphanRegions(DatabaseService db, HashSet<int> before, IEnumerable<FemMember> after)
    {
        var used = ReferencedRegions(after);
        int n = 0;
        foreach (int id in before.Where(id => !used.Contains(id)))
        {
            db.DeletePlanarRegion(id);
            n++;
        }
        return n;
    }

    /// <summary>Запись состава сессии (полная замена) и удаление областей удалённых пластин.</summary>
    static void Write(DatabaseService db, FemSchemaEditSession session)
    {
        var before = ReferencedRegions(db.GetFemMembers(session.Schema.Id));
        db.SaveFemSchemaEdit(session.Schema.Id, session.Nodes, session.Members, session.MemberGroups,
            session.LoadCases, session.NodeLoads, session.MemberLoads, session.KinematicLoads, session.LoadDefinitions,
            session.ElementLoads, session.MeshNodeLoads, session.LineSupports, session.NodeSprings);
        PruneOrphanRegions(db, before, session.Members);
    }

    // ── Проверка и запись состава ───────────────────────────────────────────────────────────

    /// <summary>Проверка состава сессии перед записью (ошибки и предупреждения).</summary>
    public static List<FemValidationDiagnostic> Validate(DatabaseService db, FemSchemaEditSession session)
    {
        var schema = session.Schema;
        bool imported = schema.Id > 0 && db.HasImportedMesh(schema.Id);
        var importedNodes = imported
            ? db.GetFemMeshNodes(schema.Id).Where(n => n.Origin == FemMember.MeshSourceImported).ToList()
            : null;
        var importedElementTags = imported
            ? db.GetFemMeshElements(schema.Id).Where(e => e.Origin == FemMember.MeshSourceImported)
                .Select(e => e.ElemTag).ToHashSet(StringComparer.Ordinal)
            : null;
        return FemTopologyValidator.Validate(schema, session.Nodes, session.Members, session.MemberGroups,
                importedNodes, importedElementTags)
            .Concat(FemCanonicalValidator.Validate(schema, session.LoadCases, session.Nodes,
                session.NodeLoads, session.Members, session.MemberLoads, session.KinematicLoads))
            .Concat(FemLoadDefinitionValidator.Validate(schema, session.LoadDefinitions, session.LoadCases))
            .ToList();
    }

    /// <summary>Проверяет и записывает состав сессии; при ошибках база не меняется. История правок сбрасывается.</summary>
    public static bool Save(DatabaseService db, FemSchemaEditSession session, out List<FemValidationDiagnostic> diagnostics)
    {
        diagnostics = Validate(db, session);
        if (diagnostics.Any(d => d.IsError)) return false;
        Write(db, session);
        session.MarkSaved();
        return true;
    }
}
