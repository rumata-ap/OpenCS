using CScore;
using CScore.Fire;
using CScore.Fire.Entities;
using OpenCS.Tasks;
using OpenCS.Utilites;

namespace OpenCS.Services;

/// <summary>
/// Проверка, номер, имя по умолчанию, запись, копия, удаление и смена сечения огневых сечений — одна для окна
/// создания, страницы огневого сечения, дерева и вида <c>fireSections</c> документа <c>opencs-model</c>.
/// Пути ошибок — как в документе: <c>tag</c>, <c>section</c>, <c>fire.durationMin</c>, <c>fire.curve</c>,
/// <c>fire.aggregate</c>, <c>mesh.stepMm</c>, <c>mesh.algorithm</c>, <c>mesh.smoothing</c>, <c>time.stepS</c>,
/// <c>time.snapshotStepMin</c>, <c>time.theta</c>, <c>time.picardTol</c>, <c>time.picardMaxIter</c>,
/// <c>bc.preset</c>, <c>bc.holes</c>, <c>bc.edges[i].…</c>.
/// </summary>
public static class FireSectionAuthoring
{
    /// <summary>Огневые кривые.</summary>
    public static readonly string[] Curves = ["iso834", "hydrocarbon", "slow"];
    /// <summary>Шаблоны граничных условий внешнего контура.</summary>
    public static readonly string[] BcPresets = ["manual", "1-sided", "2-sided", "3-sided", "all-sided"];
    /// <summary>Граничные условия отверстий.</summary>
    public static readonly string[] HolePresets = ["ambient", "adiabatic"];
    /// <summary>Алгоритмы сетки.</summary>
    public static readonly string[] Algorithms = ["ruppert", "advancing_front"];
    /// <summary>Типы заполнителя бетона (пусто — из бетона сечения).</summary>
    public static readonly string[] Aggregates = ["silicate", "carbonate", "lightweight"];
    /// <summary>Типы граничного условия ребра.</summary>
    public static readonly string[] EdgeTypes = ["fire", "ambient", "adiabatic"];

    /// <summary>Наибольшая длительность пожара, мин.</summary>
    public const double MaxDurationMin = 600.0;
    /// <summary>Наибольший шаг по времени, с.</summary>
    public const double MaxTimeStepS = 600.0;
    /// <summary>Наибольшее число итераций сглаживания сетки.</summary>
    public const int MaxSmoothing = 50;

    // ── Проверка ──────────────────────────────────────────────────────────────────────────────

    /// <summary>Проверка полей без обращения к проекту: имя, списки, диапазоны, рёбра (без индексов по контуру).</summary>
    public static List<FieldError> ValidateFields(FireSectionDef d)
    {
        var e = new List<FieldError>();
        if (string.IsNullOrWhiteSpace(d.Tag)) e.Add(new("tag", Loc.S("FireErrTagEmpty")));

        if (!(d.FireDurationMin > 0 && d.FireDurationMin <= MaxDurationMin))
            e.Add(new("fire.durationMin", string.Format(Loc.S("FireErrRange"), Loc.S("FireSection_Duration"), "0", Num(MaxDurationMin))));
        if (!Curves.Contains(d.FireCurve)) e.Add(new("fire.curve", Loc.S("FireErrCurve")));
        if (!string.IsNullOrEmpty(d.AggregateType) && !Aggregates.Contains(d.AggregateType))
            e.Add(new("fire.aggregate", Loc.S("FireErrAggregate")));

        if (!(d.MeshStepM > 0 && double.IsFinite(d.MeshStepM)))
            e.Add(new("mesh.stepMm", string.Format(Loc.S("FireErrPositive"), Loc.S("FireSection_MeshStep"))));
        if (!Algorithms.Contains(d.Algorithm)) e.Add(new("mesh.algorithm", Loc.S("FireErrAlgorithm")));
        if (d.SmoothIterTri < 0 || d.SmoothIterTri > MaxSmoothing)
            e.Add(new("mesh.smoothing", string.Format(Loc.S("FireErrRange"), Loc.S("FireSection_Smoothing"), "0", MaxSmoothing.ToString())));
        if (d.MeshElementType is not ("linear" or "quadratic"))
            e.Add(new("mesh.elementType", Loc.S("FireErrElementType")));

        if (!(d.TimeStepS > 0 && d.TimeStepS <= MaxTimeStepS))
            e.Add(new("time.stepS", string.Format(Loc.S("FireErrRange"), Loc.S("FireSection_TimeStep"), "0", Num(MaxTimeStepS))));
        if (!(d.SnapshotStepMin > 0 && double.IsFinite(d.SnapshotStepMin)))
            e.Add(new("time.snapshotStepMin", string.Format(Loc.S("FireErrPositive"), Loc.S("FireSection_SnapshotStep"))));
        else if (d.FireDurationMin > 0 && d.SnapshotStepMin > d.FireDurationMin)
            e.Add(new("time.snapshotStepMin", Loc.S("FireErrSnapshotStep")));
        if (!(d.Theta >= 0 && d.Theta <= 1))
            e.Add(new("time.theta", string.Format(Loc.S("FireErrRange"), "θ", "0", "1")));
        if (!(d.PicardTolCelsius > 0 && double.IsFinite(d.PicardTolCelsius)))
            e.Add(new("time.picardTol", string.Format(Loc.S("FireErrPositive"), Loc.S("FireSection_PicardTol"))));
        if (d.PicardMaxIter < 1)
            e.Add(new("time.picardMaxIter", string.Format(Loc.S("FireErrPositive"), Loc.S("FireSection_PicardMaxIter"))));

        if (!BcPresets.Contains(d.BcPreset)) e.Add(new("bc.preset", Loc.S("FireErrBcPreset")));
        if (!HolePresets.Contains(d.HoleBcPreset)) e.Add(new("bc.holes", Loc.S("FireErrHolePreset")));

        var seen = new HashSet<(string, int?, int)>();
        for (int i = 0; i < d.Edges.Count; i++)
        {
            var g = d.Edges[i];
            string p = $"bc.edges[{i}]";
            bool hole = g.ContourType == "hole";
            if (g.ContourType is not ("outer" or "hole")) e.Add(new($"{p}.contour", Loc.S("FireErrEdgeContour")));
            if (hole && g.HoleIndex is null or < 0) e.Add(new($"{p}.hole", Loc.S("FireErrEdgeHole")));
            if (g.EdgeIndex < 0) e.Add(new($"{p}.edge", Loc.S("FireErrEdgeIndex")));
            if (!EdgeTypes.Contains(g.BcType)) e.Add(new($"{p}.type", Loc.S("FireErrEdgeType")));
            if (!(g.AlphaConv >= 0 && double.IsFinite(g.AlphaConv))) e.Add(new($"{p}.alpha", Loc.S("FireErrEdgeAlpha")));
            if (!(g.Emissivity >= 0 && g.Emissivity <= 1)) e.Add(new($"{p}.emissivity", Loc.S("FireErrEdgeEmissivity")));
            if (!double.IsFinite(g.TAmbientCelsius) || g.TAmbientCelsius < -273.15)
                e.Add(new($"{p}.tAmbient", Loc.S("FireErrEdgeTemperature")));
            if (!seen.Add((g.ContourType, hole ? g.HoleIndex : null, g.EdgeIndex)))
                e.Add(new($"{p}.edge", Loc.S("FireErrEdgeDuplicate")));
        }
        return e;
    }

    /// <summary>Полная проверка: <see cref="ValidateFields"/>, сечение (есть, с бетонным контуром), индексы рёбер по контуру.</summary>
    public static List<FieldError> Validate(DatabaseService db, FireSectionDef d)
    {
        var e = ValidateFields(d);
        var section = db.CrossSections.FirstOrDefault(s => s.Id == d.SectionId);
        if (section == null)
        {
            e.Add(new("section", Loc.S("FireSection_NeedCrossSection")));
            return e;
        }
        var counts = HasConcreteContour(section, db.Materials) ? FireMeshBuilder.ContourEdgeCounts(section) : null;
        if (counts is not var (outer, holes))
        {
            e.Add(new("section", string.Format(Loc.S("FireErrSectionNoConcrete"), section.Tag)));
            return e;
        }
        for (int i = 0; i < d.Edges.Count; i++)
        {
            var g = d.Edges[i];
            int limit = g.ContourType == "hole"
                ? (g.HoleIndex is int h && h >= 0 && h < holes.Length ? holes[h] : -1)
                : outer;
            if (g.ContourType == "hole" && limit < 0)
                e.Add(new($"bc.edges[{i}].hole", string.Format(Loc.S("FireErrEdgeHoleRange"), holes.Length)));
            else if (g.EdgeIndex >= limit)
                e.Add(new($"bc.edges[{i}].edge", string.Format(Loc.S("FireErrEdgeRange"), limit)));
        }
        return e;
    }

    /// <summary>Есть область с контуром из бетона (по объекту материала или Id в пуле) — тепловой расчёт возможен.</summary>
    public static bool HasConcreteContour(CrossSection s, IEnumerable<Material>? pool = null) =>
        s.Areas.Any(a => a.Hull != null
            && (a.Material ?? pool?.FirstOrDefault(m => m.Id == a.MaterialId))?.Type == MatType.Concrete);

    static string Num(double v) => v.ToString("G", System.Globalization.CultureInfo.CurrentCulture);

    // ── Номер, имя, копия, запись ─────────────────────────────────────────────────────────────

    /// <summary>Номер нового огневого сечения: следующий за наибольшим.</summary>
    public static int NextNum(DatabaseService db) => db.FireSections.Count > 0 ? db.FireSections.Max(f => f.Num) + 1 : 1;

    /// <summary>Имя по умолчанию по номеру («Огневое сечение 3»).</summary>
    public static string DefaultTag(int num) => string.Format(Loc.S("FireSection_DefaultTag"), num);

    /// <summary>Новое огневое сечение с умолчаниями для сечения <paramref name="sectionId"/>.</summary>
    public static FireSectionDef CreateDefault(DatabaseService db, int sectionId)
    {
        int num = NextNum(db);
        return new FireSectionDef
        {
            Num = num, Tag = DefaultTag(num), SectionId = sectionId,
            Algorithm = "ruppert", BcPreset = "manual", HoleBcPreset = "ambient",
        };
    }

    /// <summary>Глубокая копия со всеми полями, включая Id и номер (черновик страницы).</summary>
    public static FireSectionDef Clone(FireSectionDef s)
    {
        var c = new FireSectionDef { Id = s.Id, Num = s.Num };
        CopyInto(s, c);
        return c;
    }

    /// <summary>Переносит имя и входные данные (без Id и номера); рёбра копируются.</summary>
    public static void CopyInto(FireSectionDef src, FireSectionDef dst)
    {
        dst.Tag = src.Tag;
        dst.SectionId = src.SectionId;
        dst.FireDurationMin = src.FireDurationMin;
        dst.FireCurve = src.FireCurve;
        dst.MeshStepM = src.MeshStepM;
        dst.TimeStepS = src.TimeStepS;
        dst.Theta = src.Theta;
        dst.PicardTolCelsius = src.PicardTolCelsius;
        dst.PicardMaxIter = src.PicardMaxIter;
        dst.SnapshotStepMin = src.SnapshotStepMin;
        dst.BcPreset = src.BcPreset;
        dst.HoleBcPreset = src.HoleBcPreset;
        dst.Algorithm = src.Algorithm;
        dst.SmoothIterTri = src.SmoothIterTri;
        dst.MeshElementType = src.MeshElementType;
        dst.AggregateType = src.AggregateType;
        dst.Edges = src.Edges.Select(CloneEdge).ToList();
    }

    /// <summary>Копия ребра.</summary>
    public static FireBoundaryEdgeDef CloneEdge(FireBoundaryEdgeDef g) => new()
    {
        EdgeIndex = g.EdgeIndex, ContourType = g.ContourType, HoleIndex = g.HoleIndex, BcType = g.BcType,
        AlphaConv = g.AlphaConv, Emissivity = g.Emissivity, TAmbientCelsius = g.TAmbientCelsius,
    };

    /// <summary>Совпадают ли имя и все входные данные (для «*» страницы).</summary>
    public static bool SameContent(FireSectionDef a, FireSectionDef b) =>
        a.Tag == b.Tag && !InputsChanged(a, b);

    /// <summary>Изменились ли входные данные (всё, кроме имени и номера).</summary>
    public static bool InputsChanged(FireSectionDef a, FireSectionDef b) =>
        a.SectionId != b.SectionId || a.FireDurationMin != b.FireDurationMin || a.FireCurve != b.FireCurve
        || a.MeshStepM != b.MeshStepM || a.TimeStepS != b.TimeStepS || a.Theta != b.Theta
        || a.PicardTolCelsius != b.PicardTolCelsius || a.PicardMaxIter != b.PicardMaxIter
        || a.SnapshotStepMin != b.SnapshotStepMin || a.BcPreset != b.BcPreset || a.HoleBcPreset != b.HoleBcPreset
        || a.Algorithm != b.Algorithm || a.SmoothIterTri != b.SmoothIterTri || a.MeshElementType != b.MeshElementType
        || (a.AggregateType ?? "") != (b.AggregateType ?? "") || !SameEdges(a.Edges, b.Edges);

    static bool SameEdges(List<FireBoundaryEdgeDef> a, List<FireBoundaryEdgeDef> b)
    {
        static string K(FireBoundaryEdgeDef g) => FormattableString.Invariant(
            $"{g.ContourType}|{g.HoleIndex}|{g.EdgeIndex}|{g.BcType}|{g.AlphaConv:R}|{g.Emissivity:R}|{g.TAmbientCelsius:R}");
        return a.Select(K).OrderBy(x => x, StringComparer.Ordinal)
            .SequenceEqual(b.Select(K).OrderBy(x => x, StringComparer.Ordinal));
    }

    /// <summary>Проверяет и записывает новое огневое сечение (номер — если не задан). Ошибки — без записи.</summary>
    public static List<FieldError> Save(DatabaseService db, FireSectionDef d)
    {
        var errors = Validate(db, d);
        if (errors.Count > 0) return errors;
        if (d.Id == 0 && d.Num == 0) d.Num = NextNum(db);
        db.SaveFireSection(d);
        return errors;
    }

    /// <summary>Последствия смены сечения у <paramref name="target"/>: задачи на огневом сечении и число их результатов.</summary>
    public static (List<CalcTask> Tasks, int Results) SectionChangeImpact(DatabaseService db, FireSectionDef target)
    {
        var tasks = TasksOf(db, target);
        var ids = tasks.Select(t => t.Id).ToHashSet();
        return (tasks, db.CalcResults.Count(r => ids.Contains(r.TaskId)));
    }

    /// <summary>
    /// Записывает правку <paramref name="edited"/> в <paramref name="target"/>. При смене сечения задачи огневого
    /// сечения переводятся на новое сечение, их результаты удаляются (они получены на прежнем). Тепловой расчёт не
    /// удаляется: при изменённых входных данных он показывается устаревшим. Ошибки — без записи.
    /// </summary>
    public static List<FieldError> Update(DatabaseService db, FireSectionDef target, FireSectionDef edited)
    {
        var probe = new FireSectionDef { Id = target.Id, Num = target.Num };
        CopyInto(edited, probe);
        var errors = Validate(db, probe);
        if (errors.Count > 0) return errors;
        bool sectionChanged = target.SectionId != probe.SectionId;
        var tasks = sectionChanged ? TasksOf(db, target) : [];
        CopyInto(probe, target);
        db.SaveFireSection(target);
        foreach (var t in tasks)
        {
            t.SectionId = target.SectionId;
            db.SaveCalcTask(t);
            db.DeleteCalcResultsByTaskId(t.Id);
        }
        return errors;
    }

    /// <summary>Копия: все входные данные и ручные ГУ, имя «… (копия)», новый номер; тепловой расчёт не копируется.</summary>
    public static FireSectionDef Duplicate(DatabaseService db, FireSectionDef src)
    {
        var copy = new FireSectionDef { Num = NextNum(db) };
        CopyInto(src, copy);
        copy.Tag = string.Format(Loc.S("CalcTaskCopyTag"), src.Tag);
        db.SaveFireSection(copy);
        return copy;
    }

    // ── Использование и удаление ──────────────────────────────────────────────────────────────

    /// <summary>Огневое сечение огневой задачи (из параметров); 0 — не огневая или не задано.</summary>
    public static int FireSectionIdOf(CalcTask t)
    {
        if (!FireTaskParamsBuilder.IsFireKind(t.Kind)) return 0;
        try
        {
            return t.Kind == "fire_thermal_curvature"
                ? FireThermalCurvatureParams.Parse(t.ParamsJson).FireSectionId
                : FireRCheckParams.Parse(t.ParamsJson).FireSectionId;
        }
        catch (System.Text.Json.JsonException) { return 0; }
    }

    /// <summary>Огневые задачи, ссылающиеся на огневое сечение.</summary>
    public static List<CalcTask> TasksOf(DatabaseService db, FireSectionDef d) =>
        d.Id <= 0 ? [] : db.CalcTasks.Where(t => FireSectionIdOf(t) == d.Id).OrderBy(t => t.Num).ToList();

    /// <summary>Кто использует огневое сечение (задачи), строками для сообщения.</summary>
    public static List<string> UsedBy(DatabaseService db, FireSectionDef d) =>
        TasksOf(db, d).Select(t => string.Format(Loc.S("SectionUsedByTask"), t.Tag)).ToList();

    /// <summary>Удаляет огневое сечение (с рёбрами и тепловым расчётом), если на него не ссылаются задачи.</summary>
    public static bool TryDelete(DatabaseService db, FireSectionDef d, out List<string> usedBy)
    {
        usedBy = UsedBy(db, d);
        if (usedBy.Count > 0) return false;
        if (d.Id > 0) db.DeleteFireSection(d.Id);
        else db.FireSections.Remove(d);
        return true;
    }

    /// <summary>Сообщение «используется»: до пяти имён и «… ещё N».</summary>
    public static string UsedByText(List<string> usedBy)
    {
        var shown = usedBy.Take(5).ToList();
        if (usedBy.Count > 5) shown.Add(string.Format(Loc.S("UsedByMore"), usedBy.Count - 5));
        return string.Join("\n", shown);
    }

    // ── Заполнитель ───────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Эффективный тип заполнителя: заданный у огневого сечения, иначе — у бетона сечения (по объекту материала
    /// области или по Id в пуле материалов), иначе силикатный. Одно место для страницы, расчёта и проверки актуальности.
    /// </summary>
    public static string EffectiveAggregate(FireSectionDef d, CrossSection section, IEnumerable<Material>? pool = null)
    {
        if (!string.IsNullOrWhiteSpace(d.AggregateType))
            return d.AggregateType.Trim().ToLowerInvariant();
        return SectionAggregate(section, pool);
    }

    /// <summary>Заполнитель бетона сечения (силикатный, если не задан).</summary>
    public static string SectionAggregate(CrossSection section, IEnumerable<Material>? pool = null)
    {
        foreach (var area in section.Areas)
        {
            var mat = area.Material ?? pool?.FirstOrDefault(m => m.Id == area.MaterialId);
            if (mat?.Type == MatType.Concrete)
                return string.IsNullOrWhiteSpace(mat.AggregateType) ? "silicate" : mat.AggregateType.Trim().ToLowerInvariant();
        }
        return "silicate";
    }
}
