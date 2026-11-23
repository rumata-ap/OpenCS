using System.Globalization;
using System.Text;
using System.Text.Json;
using CScore;
using CScore.Combinations;
using OpenCS.Utilites;

namespace OpenCS.Services;

/// <summary>
/// Проверка, сохранение, копия, удаление, CSV и сочетания СП 20 наборов усилий — одна для страниц наборов, диалога
/// сочетаний и консоли (<c>opencs apply</c>, <c>opencs combine sp20</c>). Единицы — базы: кН, кН·м (стержни);
/// кН/м, кН·м/м, σ — кПа (пластины). Пути ошибок — как в документе opencs-model (<c>forceSets[]</c>): <c>tag</c>,
/// <c>kind</c>, <c>rows</c>, <c>rows[3].Mx</c>, <c>gammaF</c>.
/// </summary>
public static class ForceSetAuthoring
{
    /// <summary>Виды наборов.</summary>
    public static readonly string[] Kinds = ["bar", "shell"];

    /// <summary>Столбцы строк стержня (кН, кН·м).</summary>
    public static readonly string[] BarColumns = ["N", "Mx", "My", "Vx", "Vy", "T"];

    /// <summary>Столбцы строк пластины (кН/м, кН·м/м; σ — кПа, могут быть пусты).</summary>
    public static readonly string[] ShellColumns = ["Nx", "Ny", "Nxy", "Mx", "My", "Mxy", "Qx", "Qy", "sigmaX", "sigmaY", "tauXY"];

    /// <summary>Столбцы напряжений пластины — пусто допустимо.</summary>
    public static readonly string[] StressColumns = ["sigmaX", "sigmaY", "tauXY"];

    // ── Значения строк по имени столбца ──────────────────────────────────────────────────────

    /// <summary>Значение столбца строки стержня.</summary>
    public static double BarValue(LoadItem i, string column) => column switch
    {
        "N" => i.N, "Mx" => i.Mx, "My" => i.My, "Vx" => i.Vx, "Vy" => i.Vy, "T" => i.T,
        _ => throw new ArgumentException(column, nameof(column))
    };

    /// <summary>Записывает значение столбца строки стержня.</summary>
    public static void SetBarValue(LoadItem i, string column, double v)
    {
        switch (column)
        {
            case "N": i.N = v; break;
            case "Mx": i.Mx = v; break;
            case "My": i.My = v; break;
            case "Vx": i.Vx = v; break;
            case "Vy": i.Vy = v; break;
            case "T": i.T = v; break;
            default: throw new ArgumentException(column, nameof(column));
        }
    }

    /// <summary>Значение столбца строки пластины; σ может быть null.</summary>
    public static double? ShellValue(ShellLoadItem i, string column) => column switch
    {
        "Nx" => i.Nx, "Ny" => i.Ny, "Nxy" => i.Nxy, "Mx" => i.Mx, "My" => i.My, "Mxy" => i.Mxy,
        "Qx" => i.Qx, "Qy" => i.Qy, "sigmaX" => i.SigmaX, "sigmaY" => i.SigmaY, "tauXY" => i.TauXY,
        _ => throw new ArgumentException(column, nameof(column))
    };

    /// <summary>Записывает значение столбца строки пластины (null допустим только у σ).</summary>
    public static void SetShellValue(ShellLoadItem i, string column, double? v)
    {
        switch (column)
        {
            case "Nx": i.Nx = v ?? 0; break;
            case "Ny": i.Ny = v ?? 0; break;
            case "Nxy": i.Nxy = v ?? 0; break;
            case "Mx": i.Mx = v ?? 0; break;
            case "My": i.My = v ?? 0; break;
            case "Mxy": i.Mxy = v ?? 0; break;
            case "Qx": i.Qx = v ?? 0; break;
            case "Qy": i.Qy = v ?? 0; break;
            case "sigmaX": i.SigmaX = v; break;
            case "sigmaY": i.SigmaY = v; break;
            case "tauXY": i.TauXY = v; break;
            default: throw new ArgumentException(column, nameof(column));
        }
    }

    // ── Проверка ─────────────────────────────────────────────────────────────────────────────

    /// <summary>Проверка набора перед записью.</summary>
    public static IReadOnlyList<FieldError> Validate(ForceSet fs)
    {
        var errors = new List<FieldError>();
        if (string.IsNullOrWhiteSpace(fs.Tag)) errors.Add(new("tag", Loc.S("ForceSetErrTagEmpty")));
        if (Sp20Name.TryParse(fs.Tag, out var name) && name.GammaF is double g && !(g > 0))
            errors.Add(new("gammaF", Loc.S("ForceSetErrGamma")));
        if (!Kinds.Contains(fs.Kind))
        {
            errors.Add(new("kind", Loc.S("ForceSetErrKind")));
            return errors;
        }
        bool shell = fs.Kind == "shell";
        int count = shell ? fs.ShellItems.Count : fs.Items.Count;
        if (count == 0) errors.Add(new("rows", Loc.S("ForceSetErrNoRows")));
        if (shell)
        {
            for (int r = 0; r < fs.ShellItems.Count; r++)
                foreach (var c in ShellColumns)
                    if (ShellValue(fs.ShellItems[r], c) is double v && !double.IsFinite(v))
                        errors.Add(new($"rows[{r}].{c}", Loc.S("ContourErrNotFinite")));
        }
        else
        {
            for (int r = 0; r < fs.Items.Count; r++)
                foreach (var c in BarColumns)
                    if (!double.IsFinite(BarValue(fs.Items[r], c)))
                        errors.Add(new($"rows[{r}].{c}", Loc.S("ContourErrNotFinite")));
        }
        return errors;
    }

    // ── Черновик ─────────────────────────────────────────────────────────────────────────────

    static LoadItem Copy(LoadItem i) => new()
    {
        Id = i.Id, Num = i.Num, Label = i.Label, N = i.N, Mx = i.Mx, My = i.My, Vx = i.Vx, Vy = i.Vy, T = i.T,
        SourceElementNum = i.SourceElementNum, SourceSectionNum = i.SourceSectionNum
    };

    static ShellLoadItem Copy(ShellLoadItem i) => new()
    {
        Id = i.Id, Num = i.Num, Label = i.Label, Nx = i.Nx, Ny = i.Ny, Nxy = i.Nxy, Mx = i.Mx, My = i.My, Mxy = i.Mxy,
        Qx = i.Qx, Qy = i.Qy, SigmaX = i.SigmaX, SigmaY = i.SigmaY, TauXY = i.TauXY,
        SourceElementNum = i.SourceElementNum, SourceSectionNum = i.SourceSectionNum
    };

    /// <summary>
    /// Переносит правимые поля <paramref name="src"/> в <paramref name="dst"/>: имя, описание, вид, признаки второго
    /// порядка и несовершенств, строки (копиями, с их Id). Id, номер, источник и ссылки на схему не трогает.
    /// </summary>
    public static void CopyInto(ForceSet src, ForceSet dst)
    {
        dst.Tag = src.Tag;
        dst.Description = src.Description;
        dst.Kind = src.Kind;
        dst.SecondOrder = src.SecondOrder;
        dst.Imperfections = src.Imperfections;
        dst.Items = src.Items.ConvertAll(Copy);
        dst.ShellItems = src.ShellItems.ConvertAll(Copy);
    }

    /// <summary>Копия для правки на странице: те же Id, номер, источник и ссылки, строки — копиями.</summary>
    public static ForceSet Draft(ForceSet src)
    {
        var d = new ForceSet
        {
            Id = src.Id, Num = src.Num, SourceType = src.SourceType, SourceSchemaId = src.SourceSchemaId,
            SourceElementTag = src.SourceElementTag, SourceMemberId = src.SourceMemberId, SourceElementId = src.SourceElementId,
        };
        CopyInto(src, d);
        return d;
    }

    /// <summary>Отпечаток записываемых полей (заголовок и строки) — для «*» и тестов.</summary>
    public static string Fingerprint(ForceSet fs) => JsonSerializer.Serialize(new
    {
        fs.Tag, Description = fs.Description ?? "", fs.Kind, fs.SecondOrder, Imperfections = fs.Imperfections ?? "",
        Bar = fs.Items.Select(i => new object?[] { i.Label, i.N, i.Mx, i.My, i.Vx, i.Vy, i.T, i.SourceElementNum, i.SourceSectionNum }),
        Shell = fs.ShellItems.Select(i => new object?[]
        {
            i.Label, i.Nx, i.Ny, i.Nxy, i.Mx, i.My, i.Mxy, i.Qx, i.Qy, i.SigmaX, i.SigmaY, i.TauXY, i.SourceElementNum, i.SourceSectionNum
        })
    });

    // ── Запись ───────────────────────────────────────────────────────────────────────────────

    /// <summary>Следующий номер набора — один ряд для стержней и пластин.</summary>
    public static int NextNum(DatabaseService db) => db.ForceSets.Count > 0 ? db.ForceSets.Max(s => s.Num) + 1 : 1;

    /// <summary>Проверяет и записывает набор; новый — с номером и в пул проекта.</summary>
    public static IReadOnlyList<FieldError> Save(DatabaseService db, ForceSet fs)
    {
        var errors = Validate(fs);
        if (errors.Count > 0) return errors;
        if (fs.Num == 0) fs.Num = NextNum(db);
        db.SaveForceSet(fs);
        if (!db.ForceSets.Contains(fs)) db.ForceSets.Add(fs);
        return [];
    }

    /// <summary>Других наборов проекта с тем же именем (для предупреждения: имя — ключ сочетаний и консоли).</summary>
    public static int OthersWithTag(DatabaseService db, ForceSet fs) =>
        db.ForceSets.Count(s => !ReferenceEquals(s, fs) && (fs.Id == 0 || s.Id != fs.Id) && s.Tag.Trim() == fs.Tag.Trim());

    /// <summary>
    /// Копия набора со всеми полями (второй порядок, несовершенства, источник, номера КЭ строк), без ссылок на схему,
    /// с суффиксом имени; записывается и добавляется в пул.
    /// </summary>
    public static ForceSet Duplicate(DatabaseService db, ForceSet src)
    {
        var copy = new ForceSet { SourceType = src.SourceType };
        CopyInto(src, copy);
        copy.Tag = string.Format(Loc.S("ForceSetCopyName"), src.Tag);
        foreach (var i in copy.Items) i.Id = 0;
        foreach (var i in copy.ShellItems) i.Id = 0;
        copy.Num = NextNum(db);
        db.SaveForceSet(copy);
        db.ForceSets.Add(copy);
        return copy;
    }

    // ── Удаление ─────────────────────────────────────────────────────────────────────────────

    /// <summary>Кто использует набор: задачи, КонЭ и группы КЭ схем, проверки МКЭ.</summary>
    public static List<string> UsedBy(DatabaseService db, ForceSet fs)
    {
        var users = new List<string>();
        if (fs.Id <= 0) return users;
        var u = db.ForceSetReferences(fs.Id);
        users.AddRange(u.Tasks.Select(t => string.Format(Loc.S("SectionUsedByTask"), t)));
        if (u.FemMembers > 0) users.Add(string.Format(Loc.S("SectionUsedByFemMembers"), u.FemMembers));
        users.AddRange(u.FemGroups.Select(t => string.Format(Loc.S("PlateUsedByFemGroup"), t)));
        users.AddRange(u.FemChecks.Select(t => string.Format(Loc.S("ForceSetUsedByFemCheck"), t)));
        return users;
    }

    /// <summary>Удаляет набор, если его никто не использует; иначе — false и список пользователей.</summary>
    public static bool TryDelete(DatabaseService db, ForceSet fs, out List<string> usedBy)
    {
        usedBy = UsedBy(db, fs);
        if (usedBy.Count > 0) return false;
        if (fs.Id == 0) db.ForceSets.Remove(fs);
        else db.DeleteForceSet(fs);
        return true;
    }

    // ── CSV ──────────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Пишет строки набора в CSV: разделитель «;», числа с точкой; столбцы — <c>Label</c>, усилия вида, <c>Elem</c>,
    /// <c>Sect</c>.
    /// </summary>
    public static void WriteCsv(string path, ForceSet fs)
    {
        bool shell = fs.Kind == "shell";
        var cols = shell ? ShellColumns : BarColumns;
        var sb = new StringBuilder();
        sb.AppendLine(string.Join(";", new[] { "Label" }.Concat(cols.Select(CsvName)).Concat(["Elem", "Sect"])));
        static string Num(double? v) => v?.ToString("R", CultureInfo.InvariantCulture) ?? "";
        static string Int(int? v) => v?.ToString(CultureInfo.InvariantCulture) ?? "";
        static string Text(string s) => s.IndexOfAny([';', '"', '\n', '\r']) >= 0 ? "\"" + s.Replace("\"", "\"\"") + "\"" : s;
        if (shell)
            foreach (var i in fs.ShellItems)
                sb.AppendLine(string.Join(";", new[] { Text(i.Label) }.Concat(cols.Select(c => Num(ShellValue(i, c))))
                    .Concat([Int(i.SourceElementNum), Int(i.SourceSectionNum)])));
        else
            foreach (var i in fs.Items)
                sb.AppendLine(string.Join(";", new[] { Text(i.Label) }.Concat(cols.Select(c => Num(BarValue(i, c))))
                    .Concat([Int(i.SourceElementNum), Int(i.SourceSectionNum)])));
        File.WriteAllText(path, sb.ToString(), new UTF8Encoding(true));
    }

    /// <summary>Имя столбца в CSV: как в прежних файлах OpenCS (<c>SigmaX</c>, <c>TauXY</c>).</summary>
    static string CsvName(string column) => column switch { "sigmaX" => "SigmaX", "sigmaY" => "SigmaY", "tauXY" => "TauXY", _ => column };

    /// <summary>
    /// Читает строки набора вида <paramref name="kind"/> из CSV. Разделитель — «;», табуляция или «,» (по строке
    /// заголовка); числа — с точкой или запятой (при разделителе «,» — только с точкой). Заголовок обязателен, регистр
    /// не важен; неизвестный столбец, не число, пустой файл — ошибки с номером строки файла. Пустая ячейка — 0
    /// (у σ — нет напряжения).
    /// </summary>
    public static (List<LoadItem> Bar, List<ShellLoadItem> Shell, List<string> Errors) ReadCsv(string path, string kind)
    {
        var lines = File.ReadAllLines(path, Encoding.UTF8);
        return ParseCsv(lines, kind);
    }

    /// <summary>Разбор CSV (строки файла) — см. <see cref="ReadCsv"/>.</summary>
    public static (List<LoadItem> Bar, List<ShellLoadItem> Shell, List<string> Errors) ParseCsv(IReadOnlyList<string> lines, string kind)
    {
        var bar = new List<LoadItem>();
        var shell = new List<ShellLoadItem>();
        var errors = new List<string>();
        bool isShell = kind == "shell";
        var cols = isShell ? ShellColumns : BarColumns;

        int headerLine = 0;
        while (headerLine < lines.Count && string.IsNullOrWhiteSpace(lines[headerLine])) headerLine++;
        if (headerLine >= lines.Count)
        {
            errors.Add(Loc.S("ForceSetCsvErrEmpty"));
            return (bar, shell, errors);
        }
        string header = lines[headerLine].TrimStart('﻿');
        char sep = header.Contains(';') ? ';' : header.Contains('\t') ? '\t' : ',';
        var names = SplitCsv(header, sep);
        var map = new List<(int Index, string Column)>();
        int labelIndex = -1, elemIndex = -1, sectIndex = -1;
        for (int k = 0; k < names.Count; k++)
        {
            string n = names[k].Trim();
            if (n.Length == 0) continue;
            if (n.Equals("Label", StringComparison.OrdinalIgnoreCase)) { labelIndex = k; continue; }
            if (n.Equals("Elem", StringComparison.OrdinalIgnoreCase)) { elemIndex = k; continue; }
            if (n.Equals("Sect", StringComparison.OrdinalIgnoreCase)) { sectIndex = k; continue; }
            string? col = cols.FirstOrDefault(c => c.Equals(n, StringComparison.OrdinalIgnoreCase));
            if (col == null) errors.Add(string.Format(Loc.S("ForceSetCsvErrColumn"), headerLine + 1, n));
            else map.Add((k, col));
        }
        if (errors.Count > 0) return (bar, shell, errors);

        for (int ln = headerLine + 1; ln < lines.Count; ln++)
        {
            if (string.IsNullOrWhiteSpace(lines[ln])) continue;
            var cells = SplitCsv(lines[ln], sep);
            string Cell(int k) => k >= 0 && k < cells.Count ? cells[k].Trim() : "";
            int before = errors.Count;
            var values = new Dictionary<string, double?>();
            foreach (var (k, c) in map)
            {
                string text = Cell(k);
                if (text.Length == 0) { values[c] = null; continue; }
                if (TryParseNumber(text, sep == ',', out double v)) values[c] = v;
                else errors.Add(string.Format(Loc.S("ForceSetCsvErrNumber"), ln + 1, names[k].Trim(), text));
            }
            int? elem = null, sect = null;
            foreach (var (k, set) in new[] { (elemIndex, (Action<int>)(x => elem = x)), (sectIndex, x => sect = x) })
            {
                string text = Cell(k);
                if (text.Length == 0) continue;
                if (int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out int x)) set(x);
                else errors.Add(string.Format(Loc.S("ForceSetCsvErrNumber"), ln + 1, names[k].Trim(), text));
            }
            if (errors.Count > before) continue;
            string label = labelIndex >= 0 ? Cell(labelIndex) : "";
            if (isShell)
            {
                var item = new ShellLoadItem { Label = label, SourceElementNum = elem, SourceSectionNum = sect };
                foreach (var c in cols)
                    SetShellValue(item, c, values.GetValueOrDefault(c) ?? (StressColumns.Contains(c) ? null : 0));
                shell.Add(item);
            }
            else
            {
                var item = new LoadItem { Label = label, SourceElementNum = elem, SourceSectionNum = sect };
                foreach (var c in cols) SetBarValue(item, c, values.GetValueOrDefault(c) ?? 0);
                bar.Add(item);
            }
        }
        if (errors.Count == 0 && bar.Count + shell.Count == 0) errors.Add(Loc.S("ForceSetCsvErrEmpty"));
        for (int k = 0; k < bar.Count; k++) bar[k].Num = k + 1;
        for (int k = 0; k < shell.Count; k++) shell[k].Num = k + 1;
        return (bar, shell, errors);
    }

    /// <summary>Число с точкой или запятой (обе сразу — не число); <paramref name="dotOnly"/> — только с точкой.</summary>
    public static bool TryParseNumber(string text, bool dotOnly, out double value)
    {
        value = 0;
        string s = text.Trim().Replace('−', '-');
        if (s.Contains(',') && (dotOnly || s.Contains('.'))) return false;
        s = s.Replace(',', '.');
        return double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out value) && double.IsFinite(value);
    }

    /// <summary>Разбор строки CSV с кавычками.</summary>
    static List<string> SplitCsv(string line, char sep)
    {
        var cells = new List<string>();
        var sb = new StringBuilder();
        bool quoted = false;
        for (int i = 0; i < line.Length; i++)
        {
            char ch = line[i];
            if (quoted)
            {
                if (ch == '"' && i + 1 < line.Length && line[i + 1] == '"') { sb.Append('"'); i++; }
                else if (ch == '"') quoted = false;
                else sb.Append(ch);
            }
            else if (ch == '"') quoted = true;
            else if (ch == sep) { cells.Add(sb.ToString()); sb.Clear(); }
            else sb.Append(ch);
        }
        cells.Add(sb.ToString());
        return cells;
    }

    // ── Сочетания СП 20 ──────────────────────────────────────────────────────────────────────

    /// <summary>Вид сочетания: основное, особое или оба.</summary>
    public enum Sp20Mode { Fundamental, Accidental, Both }

    /// <summary>Запрос сочетаний СП 20.</summary>
    /// <param name="Group">Группа основного сочетания: C, CL, N, NL.</param>
    /// <param name="Name">Базовое имя результатов; пусто — «СП20 — имена наборов».</param>
    public sealed record Sp20Request(IReadOnlyList<ForceSet> Sets, Sp20Mode Mode, string Group, bool Envelope, bool Cases,
                                     string? Name, Sp20LoadValueKind ValueKind, Sp20GammaDefaults Gamma);

    /// <summary>Группы основного сочетания.</summary>
    public static readonly string[] Sp20Groups = ["C", "CL", "N", "NL"];

    /// <summary>Базовое имя по умолчанию: «СП20 — имена наборов».</summary>
    public static string Sp20DefaultName(IEnumerable<ForceSet> sets) =>
        string.Format(Loc.S("Sp20DefaultName"), string.Join(", ", sets.Select(s => s.Tag)));

    /// <summary>
    /// Сочетания СП 20: сначала считаются все выбранные виды (основное, особое), затем созданные наборы записываются
    /// одной транзакцией — ошибка не оставляет части результата. Ошибки — текстом (без записи); иначе созданные наборы
    /// (уже в пуле проекта) и предупреждения разбора имён.
    /// </summary>
    public static (List<ForceSet> Created, List<string> Warnings, List<string> Errors) CombineSp20(DatabaseService db, Sp20Request r)
    {
        var errors = new List<string>();
        if (r.Sets.Count == 0) errors.Add(Loc.S("Sp20ErrNoSets"));
        if (!r.Envelope && !r.Cases) errors.Add(Loc.S("Sp20ErrNoOutput"));
        if (r.Mode != Sp20Mode.Accidental && !Sp20Groups.Contains(r.Group))
            errors.Add(string.Format(Loc.S("Sp20ErrGroup"), r.Group));
        if (errors.Count > 0) return ([], [], errors);

        var sets = r.Sets.ToList();
        string baseName = string.IsNullOrWhiteSpace(r.Name) ? Sp20DefaultName(sets) : r.Name.Trim();
        var rowLabels = SP20Combinations.RowLabels(sets[0]).ToList();
        string kind = sets[0].Kind;
        bool secondOrder = SP20Combinations.SecondOrderOf(sets, out _);
        string? imperfections = SP20Combinations.ImperfectionsOf(sets, out _);
        var created = new List<ForceSet>();
        var warnings = new List<string>();
        try
        {
            void Run(string group, string suffix, string labelPrefix)
            {
                var (env, cases, _, w) = SP20Combinations.SP20EnvelopeAndCasesForGroup(sets, group, r.ValueKind, r.Gamma);
                foreach (var x in w) if (!warnings.Contains(x)) warnings.Add(x);
                if (r.Envelope)
                    created.Add(SP20Combinations.EnvelopeToForceSet(env, kind, $"{baseName} ({suffix})", labelPrefix, rowLabels));
                if (r.Cases)
                    created.Add(SP20Combinations.CasesToForceSet(cases, kind,
                        $"{baseName} ({string.Format(Loc.S("Sp20SuffixList"), suffix)})", rowLabels));
            }
            if (r.Mode is Sp20Mode.Fundamental or Sp20Mode.Both)
                Run(r.Group, string.Format(Loc.S("Sp20SuffixFundamental"), r.Group), "Cm");
            if (r.Mode is Sp20Mode.Accidental or Sp20Mode.Both)
                Run("A", Loc.S("Sp20SuffixAccidental"), "Cs");
        }
        catch (Exception ex) when (ex is InvalidOperationException or ArgumentException)
        {
            return ([], warnings, [ex.Message]);
        }

        int num = NextNum(db);
        foreach (var fs in created)
        {
            fs.SecondOrder = secondOrder;
            fs.Imperfections = imperfections;
            fs.Num = num++;
        }
        db.SaveNewForceSets(created);
        foreach (var fs in created) db.ForceSets.Add(fs);
        return (created, warnings, []);
    }
}

/// <summary>
/// Имя набора в формате СП 20: <c>G|L|Q|A(γf=…): название [группа]</c>. Разбор строгий: имя без префикса — не в формате
/// (не превращается в «Q: …»).
/// </summary>
public sealed record Sp20Name(char Prefix, double? GammaF, string Title, string? Group)
{
    /// <summary>Разбирает имя; false — имя не в формате СП 20.</summary>
    public static bool TryParse(string? tag, out Sp20Name name)
    {
        name = new Sp20Name('Q', null, tag ?? "", null);
        var (type, title, group, gamma, warning) = SP20Combinations.ParseForceSetName(tag ?? "");
        if (warning != null) return false;
        char prefix = type switch
        {
            NormLoadType.Permanent => 'G', NormLoadType.LongTerm => 'L', NormLoadType.Accidental => 'A', _ => 'Q'
        };
        name = new Sp20Name(prefix, gamma, title, group);
        return true;
    }

    /// <summary>Имя набора: γf — в культуре интерфейса (разбор понимает и точку, и запятую).</summary>
    public string Compose()
    {
        string gamma = GammaF is double g ? $"(γf={g.ToString("G", CultureInfo.CurrentCulture)})" : "";
        string grp = (Group ?? "").Trim();
        string name = $"{Prefix}{gamma}: {Title.Trim()}";
        return grp.Length > 0 ? $"{name} [{grp}]" : name;
    }
}
