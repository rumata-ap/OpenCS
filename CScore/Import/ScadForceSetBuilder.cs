namespace CScore.Import;

/// <summary>
/// Наборы усилий из результатов SCAD (SCADAPIX.dll) в том же виде, что у импорта ЛИРЫ через API: метки строк
/// «э.{КЭ} с{сеч}», SourceElementNum/SourceSectionNum, SourceType = "fea" — мозаики, эпюры и проверки по КЭ
/// работают без изменений. Тип расчёта РСУ — суффиксом «(C)/(CL)/(N)/(NL)» в имени набора
/// (<c>FemCheckRunner.ExtractCalcType</c>).
/// </summary>
public static class ScadForceSetBuilder
{
    /// <summary>Суффиксы групп РСУ SCAD (GroupRsu 0–3).</summary>
    static readonly string[] RsuSuffixes = ["(C)", "(CL)", "(N)", "(NL)"];

    /// <summary>Набор на каждое загружение.</summary>
    public static List<ForceSet> LoadCases(IReadOnlyList<ScadElementForces> elements, ScadResultCatalog catalog,
        int schemaId, string targetTag, ScadXlsImportOptions options)
    {
        int count = elements.Count > 0 ? elements.Max(e => e.LoadCount) : 0;
        var result = new List<ForceSet>();
        for (int lc = 0; lc < count; lc++)
        {
            string name = Name(catalog.LoadNames, lc, $"Загружение {lc + 1}");
            var fs = NewSet(Tag(targetTag, name), schemaId);
            int load = lc;
            Fill(fs, elements.Where(e => load < e.LoadCount), (e, p) => e.LoadCase(p, load), options);
            if (fs.Items.Count > 0 || fs.ShellItems.Count > 0) result.Add(fs);
        }
        return result;
    }

    /// <summary>Набор на каждую комбинацию загружений («РСН» SCAD). Предельного состояния у комбинации нет —
    /// без суффикса (тип расчёта C, задаётся в проверке).</summary>
    public static List<ForceSet> Combinations(IReadOnlyList<ScadElementForces> elements, ScadResultCatalog catalog,
        int schemaId, string targetTag, ScadXlsImportOptions options)
    {
        int count = elements.Count > 0 ? elements.Max(e => e.CombinationCount) : 0;
        var result = new List<ForceSet>();
        for (int c = 0; c < count; c++)
        {
            string name = Name(catalog.CombinationNames, c, $"РСН {c + 1}");
            var fs = NewSet(Tag(targetTag, name), schemaId);
            int comb = c;
            Fill(fs, elements.Where(e => comb < e.CombinationCount), (e, p) => e.Combination(p, comb), options);
            if (fs.Items.Count > 0 || fs.ShellItems.Count > 0) result.Add(fs);
        }
        return result;
    }

    /// <summary>
    /// Четыре набора РСУ по группам SCAD: «РСУ (C)», «(CL)», «(N)», «(NL)». Одинаковые строки одного КЭ,
    /// точки и группы (разные критерии дали одно сочетание) — один раз.
    /// </summary>
    public static List<ForceSet> Rsu(IReadOnlyList<ScadRsuElement> elements, int schemaId, string targetTag,
        ScadXlsImportOptions options)
    {
        var sets = RsuSuffixes.Select(s => NewSet(Tag(targetTag, $"РСУ {s}"), schemaId)).ToArray();
        var nums = new int[sets.Length];
        foreach (var e in elements)
        {
            var seen = new HashSet<ScadRsuRow>(RsuRowValueComparer.Instance);
            foreach (var row in e.Rows.OrderBy(r => r.Point).ThenBy(r => r.Group).ThenBy(r => r.Criterion))
            {
                if (row.Group < 0 || row.Group >= sets.Length) continue;
                if (!seen.Add(row)) continue;
                var fs = sets[row.Group];
                string label = $"э.{e.ElemId} с{row.Point} к{row.Criterion}";
                if (e.Kind == ScadElementKind.Shell)
                {
                    var item = ScadApiForceMapper.MapShell(e.Types, row.Us, options);
                    Stamp(item, ++nums[row.Group], label, e.ElemId, row.Point);
                    fs.ShellItems.Add(item);
                }
                else
                {
                    var item = ScadApiForceMapper.MapBar(e.Types, row.Us, options);
                    Stamp(item, ++nums[row.Group], label, e.ElemId, row.Point);
                    fs.Items.Add(item);
                }
            }
        }
        foreach (var fs in sets) fs.Kind = fs.ShellItems.Count > 0 ? "shell" : "bar";
        return sets.Where(fs => fs.Items.Count > 0 || fs.ShellItems.Count > 0).ToList();
    }

    /// <summary>
    /// Строки набора: стержень без результатов (все сечения нулевые) пропускается целиком, нулевое сечение
    /// нагруженного стержня остаётся (иначе сбивается счёт сечений в эпюрах); пластина с нулями — пропуск.
    /// </summary>
    static void Fill(ForceSet fs, IEnumerable<ScadElementForces> elements,
        Func<ScadElementForces, int, ReadOnlySpan<double>> values, ScadXlsImportOptions options)
    {
        int num = 0;
        foreach (var e in elements)
        {
            if (e.Kind == ScadElementKind.Shell)
            {
                for (int p = 0; p < e.Points; p++)
                {
                    var item = ScadApiForceMapper.MapShell(e.Types, values(e, p), options);
                    if (ScadApiForceMapper.IsZero(item)) continue;
                    Stamp(item, ++num, $"э.{e.ElemId} с{p + 1}", e.ElemId, p + 1);
                    fs.ShellItems.Add(item);
                }
            }
            else
            {
                var rows = new List<LoadItem>(e.Points);
                for (int p = 0; p < e.Points; p++)
                    rows.Add(ScadApiForceMapper.MapBar(e.Types, values(e, p), options));
                if (rows.All(ScadApiForceMapper.IsZero)) continue;
                for (int p = 0; p < rows.Count; p++)
                {
                    Stamp(rows[p], ++num, $"э.{e.ElemId} с{p + 1}", e.ElemId, p + 1);
                    fs.Items.Add(rows[p]);
                }
            }
        }
        fs.Kind = fs.ShellItems.Count > 0 ? "shell" : "bar";
    }

    static void Stamp(LoadItem i, int num, string label, int elem, int sec)
    {
        i.Num = num; i.Label = label; i.SourceElementNum = elem; i.SourceSectionNum = sec;
    }

    static void Stamp(ShellLoadItem i, int num, string label, int elem, int sec)
    {
        i.Num = num; i.Label = label; i.SourceElementNum = elem; i.SourceSectionNum = sec;
    }

    static ForceSet NewSet(string tag, int schemaId) => new()
    {
        Tag = tag,
        SourceType = "fea",
        SourceSchemaId = schemaId,
    };

    static string Tag(string targetTag, string name) =>
        string.IsNullOrEmpty(targetTag) ? name : $"{targetTag} — {name}";

    static string Name(IReadOnlyList<string> names, int index, string fallback) =>
        index < names.Count && !string.IsNullOrWhiteSpace(names[index]) ? names[index].Trim() : fallback;

    /// <summary>Строки РСУ равны, если совпадают точка, группа и усилия (побитово); критерий не важен.</summary>
    sealed class RsuRowValueComparer : IEqualityComparer<ScadRsuRow>
    {
        public static readonly RsuRowValueComparer Instance = new();

        public bool Equals(ScadRsuRow? a, ScadRsuRow? b) =>
            ReferenceEquals(a, b) || a != null && b != null && a.Point == b.Point && a.Group == b.Group
                && a.Us.AsSpan().SequenceEqual(b.Us);

        public int GetHashCode(ScadRsuRow r)
        {
            var h = new HashCode();
            h.Add(r.Point);
            h.Add(r.Group);
            foreach (double v in r.Us) h.Add(v);
            return h.ToHashCode();
        }
    }
}
