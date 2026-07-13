using System.Globalization;
using System.Text;

namespace CScore.Import;

/// <summary>Импорт стержневых и пластинчатых усилий из XLS-отчёта SCAD (как LIRA HTML: bar+shell в одном проходе).</summary>
public static class ScadXlsForceImporter
{
    public static ScadXlsImportResult ImportBarSheets(
        ScadXlsImportMode mode,
        ScadXlsImportOptions options,
        IReadOnlyDictionary<int, string> loadCaseNames,
        IReadOnlyList<ScadXlsSheetData> sheets,
        IProgress<ScadXlsProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var result = new ScadXlsImportResult();
        if (!options.ImportAllElements && (options.ElementIds == null || options.ElementIds.Count == 0))
        {
            result.Error = "Не задан фильтр элементов.";
            return result;
        }

        var sets = new Dictionary<string, ForceSet>(StringComparer.Ordinal);
        int sheetCount = sheets.Count;
        int forceSheets = 0;

        for (int si = 0; si < sheets.Count; si++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var sheet = sheets[si];
            progress?.Report(new ScadXlsProgress
            {
                Phase = "sheet",
                SheetIndex = si + 1,
                SheetCount = sheetCount,
                Fraction = sheetCount == 0 ? 1 : (double)(si + 1) / sheetCount * 0.9,
                Message = $"Лист {si + 1}/{sheetCount}: {sheet.Name}",
            });

            var layout = DetectLayout(sheet.Cells);
            if (layout == null || !MatchesMode(mode, layout.Kind))
                continue;

            forceSheets++;
            result.SheetsRead++;
            if (layout.IsShell)
            {
                if (mode == ScadXlsImportMode.Rsu)
                    ImportShellRsuSheet(sheet.Cells, layout, options, sets, result);
                else
                    ImportShellLoadCaseSheet(sheet.Cells, layout, options, loadCaseNames, sets, result);
            }
            else
            {
                if (mode == ScadXlsImportMode.Rsu)
                    ImportRsuSheet(sheet.Cells, layout, options, sets, result);
                else
                    ImportLoadCaseSheet(sheet.Cells, layout, options, loadCaseNames, sets, result);
            }
        }

        if (forceSheets == 0)
        {
            result.Error = "В файле не найдено листов с усилиями стержней/пластин.";
            return result;
        }

        if (sets.Count == 0)
        {
            result.Warning = "После фильтра не найдено ни одной строки усилий.";
            return result;
        }

        foreach (var fs in sets.Values
                     .OrderBy(f => f.Kind, StringComparer.Ordinal)
                     .ThenBy(f => f.Tag, StringComparer.Ordinal))
        {
            int num = 1;
            if (fs.Kind == "shell")
            {
                foreach (var item in fs.ShellItems)
                    item.Num = num++;
            }
            else
            {
                foreach (var item in fs.Items)
                    item.Num = num++;
            }
            result.ForceSets.Add(fs);
        }

        progress?.Report(new ScadXlsProgress
        {
            Phase = "done",
            Fraction = 0.95,
            Message = $"Собрано наборов: {result.ForceSets.Count}",
        });

        return result;
    }

    public static ScadXlsImportResult ImportFile(
        string path,
        ScadXlsImportMode mode,
        ScadXlsImportOptions options,
        IProgress<ScadXlsProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var result = new ScadXlsImportResult();
        try
        {
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
            progress?.Report(new ScadXlsProgress
            {
                Phase = "open",
                Fraction = 0.02,
                Message = "Открытие файла…",
            });

            using var stream = File.Open(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            using var reader = ExcelDataReader.ExcelReaderFactory.CreateReader(stream);

            var loadCaseNames = new Dictionary<int, string>();
            var combinationNames = new Dictionary<int, string>();
            var sheets = new List<ScadXlsSheetData>();
            var elemToStiff = new Dictionary<int, int>();
            var stiffToThickness = new Dictionary<int, double>();
            int sheetIndex = 0;
            do
            {
                cancellationToken.ThrowIfCancellationRequested();
                sheetIndex++;
                string name = reader.Name ?? $"Sheet{sheetIndex}";
                progress?.Report(new ScadXlsProgress
                {
                    Phase = "read",
                    SheetIndex = sheetIndex,
                    Fraction = Math.Min(0.05 + sheetIndex * 0.005, 0.5),
                    Message = $"Чтение: {name}",
                });

                var cells = ReadSheetCells(reader, maxProbeRows: 30);
                if (cells.Count == 0)
                    continue;

                string title = Cell(cells, 0, 0);
                if (IsLoadCaseNamesSheet(title, name))
                {
                    var full = ReadRemainingAsNewList(cells, reader);
                    ParseLoadCaseNames(full, loadCaseNames);
                    continue;
                }

                if (IsCombinationNamesSheet(title, name))
                {
                    var full = ReadRemainingAsNewList(cells, reader);
                    ParseLoadCaseNames(full, combinationNames);
                    continue;
                }

                if (IsElementsSheet(title, cells))
                {
                    var full = ReadRemainingAsNewList(cells, reader);
                    ParseElementStiffnessIds(full, elemToStiff);
                    continue;
                }

                if (LooksLikeStiffnessThicknessSheet(title, name, cells))
                {
                    var full = ReadRemainingAsNewList(cells, reader);
                    ParseStiffnessThicknesses(full, stiffToThickness);
                    continue;
                }

                var layout = DetectLayout(cells);
                if (layout == null || !MatchesMode(mode, layout.Kind))
                    continue;

                var fullSheet = ReadRemainingAsNewList(cells, reader);
                sheets.Add(new ScadXlsSheetData { Name = name, Cells = fullSheet });
            }
            while (reader.NextResult());

            var mergedThickness = MergeThicknessMaps(
                topology: options.ElementThicknessM,
                xlsElemToStiff: elemToStiff,
                xlsStiffToH: stiffToThickness);
            var opts = new ScadXlsImportOptions
            {
                TonToKnFactor = options.TonToKnFactor,
                InvertBarBendingMoments = options.InvertBarBendingMoments,
                ElementIds = options.ElementIds,
                ImportAllElements = options.ImportAllElements,
                DefaultThicknessM = options.DefaultThicknessM,
                ElementThicknessM = mergedThickness,
            };

            var namesForMode = mode == ScadXlsImportMode.Combinations
                ? combinationNames
                : loadCaseNames;
            return ImportBarSheets(mode, opts, namesForMode, sheets, progress, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            result.Error = "Импорт отменён.";
            result.ForceSets.Clear();
            return result;
        }
        catch (Exception ex)
        {
            result.Error = ex.Message;
            return result;
        }
    }

    enum SheetKind
    {
        BarLoadCases, BarRsu, ShellLoadCases, ShellRsu,
        BarCombinations, ShellCombinations,
    }

    sealed class Layout
    {
        public SheetKind Kind;
        public bool IsShell => Kind is SheetKind.ShellLoadCases or SheetKind.ShellRsu
            or SheetKind.ShellCombinations;
        public bool IsCombination => Kind is SheetKind.BarCombinations or SheetKind.ShellCombinations;
        public int DataStart;
        public int ColElem, ColSec, ColLoad, ColForm;
        public int ColCrit, ColType;
        // bar
        public int ColN, ColMk, ColMy, ColQz, ColMz, ColQy;
        // shell
        public int ColSx, ColSy, ColTxy, ColSMx, ColSMy, ColMxy, ColSQx, ColSQy;
    }

    static bool MatchesMode(ScadXlsImportMode mode, SheetKind kind) => mode switch
    {
        ScadXlsImportMode.LoadCases => kind is SheetKind.BarLoadCases or SheetKind.ShellLoadCases,
        ScadXlsImportMode.Rsu => kind is SheetKind.BarRsu or SheetKind.ShellRsu,
        ScadXlsImportMode.Combinations => kind is SheetKind.BarCombinations or SheetKind.ShellCombinations,
        _ => false,
    };

    static Layout? DetectLayout(IReadOnlyList<IReadOnlyList<string>> cells)
    {
        for (int r = 0; r < cells.Count - 1; r++)
        {
            var row = cells[r];
            var next = cells[r + 1];

            int crit = FindCol(row, "Крит");
            int type = FindCol(row, "Тип");
            int elem = FindCol(row, "Элем");
            if (elem < 0) elem = FindCol(row, "Элемент");
            int sec = FindCol(row, "Сеч");
            if (sec < 0) sec = FindCol(row, "Сечение");
            int load = FindCol(row, "Загруж");
            int comb = FindCol(row, "Комбинация");
            int form = FindCol(row, "форм");

            if (TryFindBarComponents(next, out var bar))
            {
                if (crit >= 0 && type >= 0 && elem >= 0)
                {
                    return new Layout
                    {
                        Kind = SheetKind.BarRsu,
                        DataStart = r + 2,
                        ColElem = elem,
                        ColSec = sec >= 0 ? sec : elem + 1,
                        ColCrit = crit,
                        ColType = type,
                        ColN = bar.n, ColMk = bar.mk, ColMy = bar.my,
                        ColQz = bar.qz, ColMz = bar.mz, ColQy = bar.qy,
                    };
                }
                if (elem >= 0 && sec >= 0 && comb >= 0)
                {
                    return new Layout
                    {
                        Kind = SheetKind.BarCombinations,
                        DataStart = r + 2,
                        ColElem = elem,
                        ColSec = sec,
                        ColLoad = comb,
                        ColForm = form,
                        ColN = bar.n, ColMk = bar.mk, ColMy = bar.my,
                        ColQz = bar.qz, ColMz = bar.mz, ColQy = bar.qy,
                    };
                }
                if (elem >= 0 && load >= 0 && sec >= 0)
                {
                    return new Layout
                    {
                        Kind = SheetKind.BarLoadCases,
                        DataStart = r + 2,
                        ColElem = elem,
                        ColSec = sec,
                        ColLoad = load,
                        // Нет колонки «форм» (статика без динамики) → -1 = пустая форма, не брать N/значения.
                        ColForm = form,
                        ColN = bar.n, ColMk = bar.mk, ColMy = bar.my,
                        ColQz = bar.qz, ColMz = bar.mz, ColQy = bar.qy,
                    };
                }
            }

            if (TryFindShellComponents(next, out var shell))
            {
                if (crit >= 0 && type >= 0 && elem >= 0)
                {
                    return new Layout
                    {
                        Kind = SheetKind.ShellRsu,
                        DataStart = r + 2,
                        ColElem = elem,
                        ColSec = sec >= 0 ? sec : elem + 1,
                        ColCrit = crit,
                        ColType = type,
                        ColSx = shell.sx, ColSy = shell.sy, ColTxy = shell.txy,
                        ColSMx = shell.mx, ColSMy = shell.my, ColMxy = shell.mxy,
                        ColSQx = shell.qx, ColSQy = shell.qy,
                    };
                }
                if (elem >= 0 && sec >= 0 && comb >= 0)
                {
                    return new Layout
                    {
                        Kind = SheetKind.ShellCombinations,
                        DataStart = r + 2,
                        ColElem = elem,
                        ColSec = sec,
                        ColLoad = comb,
                        ColForm = form,
                        ColSx = shell.sx, ColSy = shell.sy, ColTxy = shell.txy,
                        ColSMx = shell.mx, ColSMy = shell.my, ColMxy = shell.mxy,
                        ColSQx = shell.qx, ColSQy = shell.qy,
                    };
                }
                if (elem >= 0 && load >= 0 && sec >= 0)
                {
                    return new Layout
                    {
                        Kind = SheetKind.ShellLoadCases,
                        DataStart = r + 2,
                        ColElem = elem,
                        ColSec = sec,
                        ColLoad = load,
                        ColForm = form,
                        ColSx = shell.sx, ColSy = shell.sy, ColTxy = shell.txy,
                        ColSMx = shell.mx, ColSMy = shell.my, ColMxy = shell.mxy,
                        ColSQx = shell.qx, ColSQy = shell.qy,
                    };
                }
            }
        }
        return null;
    }

    static bool TryFindBarComponents(IReadOnlyList<string> row, out (int n, int mk, int my, int qz, int mz, int qy) cols)
    {
        cols = default;
        int n = IndexOfExact(row, "N");
        int mk = IndexOfExact(row, "Mk");
        int my = IndexOfExact(row, "My");
        int qz = IndexOfExact(row, "Qz");
        int mz = IndexOfExact(row, "Mz");
        int qy = IndexOfExact(row, "Qy");
        if (n < 0 || mk < 0 || my < 0 || qz < 0 || mz < 0 || qy < 0)
            return false;
        if (IndexOfExact(row, "sX") >= 0 || IndexOfExact(row, "Rx") >= 0)
            return false;
        cols = (n, mk, my, qz, mz, qy);
        return true;
    }

    static bool TryFindShellComponents(
        IReadOnlyList<string> row,
        out (int sx, int sy, int txy, int mx, int my, int mxy, int qx, int qy) cols)
    {
        cols = default;
        int sx = IndexOfExact(row, "sX");
        int sy = IndexOfExact(row, "sY");
        int txy = IndexOfExact(row, "txy");
        int mx = IndexOfExact(row, "Mx");
        int my = IndexOfExact(row, "My");
        int mxy = IndexOfExact(row, "Mxy");
        if (sx < 0 || sy < 0 || txy < 0 || mx < 0 || my < 0 || mxy < 0)
            return false;
        cols = (sx, sy, txy, mx, my, mxy, IndexOfExact(row, "Qx"), IndexOfExact(row, "Qy"));
        return true;
    }

    static int IndexOfExact(IReadOnlyList<string> row, string name)
    {
        for (int i = 0; i < row.Count; i++)
        {
            if (row[i].Trim().Equals(name, StringComparison.OrdinalIgnoreCase))
                return i;
        }
        return -1;
    }

    static int FindCol(IReadOnlyList<string> row, string contains)
    {
        for (int i = 0; i < row.Count; i++)
        {
            if (row[i].Contains(contains, StringComparison.OrdinalIgnoreCase))
                return i;
        }
        return -1;
    }

    static void ImportLoadCaseSheet(
        IReadOnlyList<IReadOnlyList<string>> cells,
        Layout layout,
        ScadXlsImportOptions options,
        IReadOnlyDictionary<int, string> loadCaseNames,
        Dictionary<string, ForceSet> sets,
        ScadXlsImportResult result)
    {
        for (int r = layout.DataStart; r < cells.Count; r++)
        {
            var row = cells[r];
            if (!TryParseInt(Cell(row, layout.ColElem), out int elem))
                continue;
            if (!AcceptsElement(options, elem))
                continue;
            if (!TryParseInt(Cell(row, layout.ColSec), out int sec))
                continue;
            if (!TryParseInt(Cell(row, layout.ColLoad), out int lc))
                continue;
            string form = Cell(row, layout.ColForm);
            if (!ScadXlsForceMapper.IsAcceptedForm(form))
                continue;
            if (!TryReadBarForces(row, layout, out var forces))
                continue;

            string key = $"bar:{lc}";
            string tag = layout.IsCombination
                ? CombinationTag(lc, loadCaseNames)
                : LoadCaseTag(lc, loadCaseNames);
            var fs = GetOrCreateSet(sets, key, "bar", tag);
            var item = ScadXlsForceMapper.MapBar(
                forces.n, forces.mk, forces.my, forces.qz, forces.mz, forces.qy, options);
            item.Label = FormatBarLabel(elem, sec.ToString(CultureInfo.InvariantCulture), form, crit: null);
            fs.Items.Add(item);
            result.RowsMatched++;
        }
    }

    static void ImportShellLoadCaseSheet(
        IReadOnlyList<IReadOnlyList<string>> cells,
        Layout layout,
        ScadXlsImportOptions options,
        IReadOnlyDictionary<int, string> loadCaseNames,
        Dictionary<string, ForceSet> sets,
        ScadXlsImportResult result)
    {
        for (int r = layout.DataStart; r < cells.Count; r++)
        {
            var row = cells[r];
            if (!TryParseInt(Cell(row, layout.ColElem), out int elem))
                continue;
            if (!AcceptsElement(options, elem))
                continue;
            string secText = Cell(row, layout.ColSec).Trim();
            if (string.IsNullOrEmpty(secText))
                continue;
            if (!TryParseInt(Cell(row, layout.ColLoad), out int lc))
                continue;
            string form = Cell(row, layout.ColForm);
            if (!ScadXlsForceMapper.IsAcceptedForm(form))
                continue;
            if (!TryReadShellForces(row, layout, out var forces))
                continue;

            double h = options.ResolveThicknessM(elem);
            if (h <= 0)
            {
                result.Error ??= "Не задана толщина пластины (поле диалога или таблица жёсткостей).";
                continue;
            }

            string key = $"shell:{lc}";
            string tag = layout.IsCombination
                ? CombinationTag(lc, loadCaseNames)
                : LoadCaseTag(lc, loadCaseNames);
            var fs = GetOrCreateSet(sets, key, "shell", tag);
            var item = ScadXlsForceMapper.MapShell(
                forces.sx, forces.sy, forces.txy, forces.mx, forces.my, forces.mxy,
                forces.qx, forces.qy, h, options);
            item.Label = FormatShellLabel(elem, secText, form, crit: null);
            fs.ShellItems.Add(item);
            result.RowsMatched++;
        }
    }

    static void ImportRsuSheet(
        IReadOnlyList<IReadOnlyList<string>> cells,
        Layout layout,
        ScadXlsImportOptions options,
        Dictionary<string, ForceSet> sets,
        ScadXlsImportResult result)
    {
        int lastElem = -1;
        int lastSec = -1;
        for (int r = layout.DataStart; r < cells.Count; r++)
        {
            var row = cells[r];
            if (TryParseInt(Cell(row, layout.ColElem), out int elem))
                lastElem = elem;
            else if (lastElem < 0)
                continue;

            if (TryParseInt(Cell(row, layout.ColSec), out int sec))
                lastSec = sec;
            else if (lastSec < 0)
                continue;

            if (!AcceptsElement(options, lastElem))
                continue;
            if (!TryParseInt(Cell(row, layout.ColCrit), out int crit))
                continue;
            string type = Cell(row, layout.ColType).Trim();
            if (string.IsNullOrEmpty(type))
                continue;
            if (!TryReadBarForces(row, layout, out var forces))
                continue;

            string key = $"bar:{type}";
            var fs = GetOrCreateSet(sets, key, "bar", $"РСУ_{type}");
            var item = ScadXlsForceMapper.MapBar(
                forces.n, forces.mk, forces.my, forces.qz, forces.mz, forces.qy, options);
            item.Label = FormatBarLabel(lastElem, lastSec.ToString(CultureInfo.InvariantCulture), form: null, crit);
            fs.Items.Add(item);
            result.RowsMatched++;
        }
    }

    static void ImportShellRsuSheet(
        IReadOnlyList<IReadOnlyList<string>> cells,
        Layout layout,
        ScadXlsImportOptions options,
        Dictionary<string, ForceSet> sets,
        ScadXlsImportResult result)
    {
        int lastElem = -1;
        string lastSec = "";
        for (int r = layout.DataStart; r < cells.Count; r++)
        {
            var row = cells[r];
            if (TryParseInt(Cell(row, layout.ColElem), out int elem))
                lastElem = elem;
            else if (lastElem < 0)
                continue;

            string secText = Cell(row, layout.ColSec).Trim();
            if (!string.IsNullOrEmpty(secText))
                lastSec = secText;
            else if (string.IsNullOrEmpty(lastSec))
                continue;

            if (!AcceptsElement(options, lastElem))
                continue;
            if (!TryParseInt(Cell(row, layout.ColCrit), out int crit))
                continue;
            string type = Cell(row, layout.ColType).Trim();
            if (string.IsNullOrEmpty(type))
                continue;
            if (!TryReadShellForces(row, layout, out var forces))
                continue;

            double h = options.ResolveThicknessM(lastElem);
            if (h <= 0)
            {
                result.Error ??= "Не задана толщина пластины (поле диалога или таблица жёсткостей).";
                continue;
            }

            string key = $"shell:{type}";
            var fs = GetOrCreateSet(sets, key, "shell", $"РСУ_{type}");
            var item = ScadXlsForceMapper.MapShell(
                forces.sx, forces.sy, forces.txy, forces.mx, forces.my, forces.mxy,
                forces.qx, forces.qy, h, options);
            item.Label = FormatShellLabel(lastElem, lastSec, form: null, crit);
            fs.ShellItems.Add(item);
            result.RowsMatched++;
        }
    }

    static ForceSet GetOrCreateSet(Dictionary<string, ForceSet> sets, string key, string kind, string tag)
    {
        if (!sets.TryGetValue(key, out var fs))
        {
            fs = new ForceSet
            {
                Kind = kind,
                Tag = tag,
                SourceType = "scad",
            };
            sets[key] = fs;
        }
        return fs;
    }

    static string LoadCaseTag(int lc, IReadOnlyDictionary<int, string> loadCaseNames)
        => loadCaseNames.TryGetValue(lc, out var name) && !string.IsNullOrWhiteSpace(name)
            ? name
            : $"Загружение {lc}";

    static string CombinationTag(int n, IReadOnlyDictionary<int, string> names)
        => names.TryGetValue(n, out var name) && !string.IsNullOrWhiteSpace(name)
            ? name
            : $"РСН {n}";

    static string FormatBarLabel(int elem, string sec, string? form, int? crit)
    {
        string label = crit is int c
            ? $"{elem}_С{sec}_К{c}"
            : $"{elem}_С{sec}";
        if (!string.IsNullOrWhiteSpace(form))
            label += " LS+SD";
        return label;
    }

    static string FormatShellLabel(int elem, string sec, string? form, int? crit)
    {
        string secPart = TryParseInt(sec, out int sn) ? $"С{sn}" : sec;
        string label = crit is int c
            ? $"{elem}_{secPart}_К{c}"
            : $"{elem}_{secPart}";
        if (!string.IsNullOrWhiteSpace(form))
            label += " LS+SD";
        return label;
    }

    static bool AcceptsElement(ScadXlsImportOptions options, int elem)
        => options.ImportAllElements || options.ElementIds.Contains(elem);

    static bool TryReadBarForces(
        IReadOnlyList<string> row, Layout layout,
        out (double n, double mk, double my, double qz, double mz, double qy) f)
    {
        f = default;
        return TryParseDouble(Cell(row, layout.ColN), out f.n)
            && TryParseDouble(Cell(row, layout.ColMk), out f.mk)
            && TryParseDouble(Cell(row, layout.ColMy), out f.my)
            && TryParseDouble(Cell(row, layout.ColQz), out f.qz)
            && TryParseDouble(Cell(row, layout.ColMz), out f.mz)
            && TryParseDouble(Cell(row, layout.ColQy), out f.qy);
    }

    static bool TryReadShellForces(
        IReadOnlyList<string> row, Layout layout,
        out (double sx, double sy, double txy, double mx, double my, double mxy, double qx, double qy) f)
    {
        f = default;
        if (!TryParseDouble(Cell(row, layout.ColSx), out f.sx)
            || !TryParseDouble(Cell(row, layout.ColSy), out f.sy)
            || !TryParseDouble(Cell(row, layout.ColTxy), out f.txy)
            || !TryParseDouble(Cell(row, layout.ColSMx), out f.mx)
            || !TryParseDouble(Cell(row, layout.ColSMy), out f.my)
            || !TryParseDouble(Cell(row, layout.ColMxy), out f.mxy))
            return false;
        f.qx = layout.ColSQx >= 0 && TryParseDouble(Cell(row, layout.ColSQx), out var qx) ? qx : 0;
        f.qy = layout.ColSQy >= 0 && TryParseDouble(Cell(row, layout.ColSQy), out var qy) ? qy : 0;
        return true;
    }

    static string Cell(IReadOnlyList<IReadOnlyList<string>> cells, int r, int c)
        => r < cells.Count ? Cell(cells[r], c) : "";

    static string Cell(IReadOnlyList<string> row, int c)
        => c >= 0 && c < row.Count ? row[c] ?? "" : "";

    static bool TryParseInt(string s, out int v)
    {
        s = s.Trim();
        return int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out v);
    }

    static bool TryParseDouble(string s, out double v)
    {
        s = (s ?? "").Trim().Replace(',', '.');
        if (string.IsNullOrEmpty(s))
        {
            v = 0;
            return true;
        }
        return double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out v);
    }

    static bool IsLoadCaseNamesSheet(string title, string name)
        => title.Contains("Имена загружений", StringComparison.OrdinalIgnoreCase)
           || name.Contains("Имена загружений", StringComparison.OrdinalIgnoreCase);

    static bool IsCombinationNamesSheet(string title, string name)
        => title.Contains("Имена комбинаций", StringComparison.OrdinalIgnoreCase)
           || name.Contains("Имена комбинаций", StringComparison.OrdinalIgnoreCase);

    static bool IsElementsSheet(string title, IReadOnlyList<IReadOnlyList<string>> probe)
    {
        if (!title.Contains("Элемент", StringComparison.OrdinalIgnoreCase))
            return false;
        for (int r = 0; r < Math.Min(8, probe.Count); r++)
        {
            if (FindCol(probe[r], "жестк") >= 0)
                return true;
        }
        return false;
    }

    static bool LooksLikeStiffnessThicknessSheet(string title, string name, IReadOnlyList<IReadOnlyList<string>> probe)
    {
        string blob = title + " " + name;
        bool nameHint = blob.Contains("Жестк", StringComparison.OrdinalIgnoreCase)
                        || blob.Contains("жестк", StringComparison.OrdinalIgnoreCase);
        for (int r = 0; r < Math.Min(12, probe.Count); r++)
        {
            int thk = FindCol(probe[r], "толщ");
            int id = FindCol(probe[r], "номер");
            if (id < 0) id = FindCol(probe[r], "№");
            if (id < 0) id = FindCol(probe[r], "Тип");
            if (thk >= 0 && (nameHint || id >= 0))
                return true;
        }
        return false;
    }

    static void ParseElementStiffnessIds(
        IReadOnlyList<IReadOnlyList<string>> cells, Dictionary<int, int> elemToStiff)
    {
        int header = -1, colElem = -1, colStiff = -1;
        for (int r = 0; r < Math.Min(10, cells.Count); r++)
        {
            int e = FindCol(cells[r], "Номер элемента");
            if (e < 0) e = IndexOfExact(cells[r], "Номер элемента");
            if (e < 0 && Cell(cells[r], 0).Contains("Номер", StringComparison.OrdinalIgnoreCase))
                e = 0;
            int s = FindCol(cells[r], "жестк");
            if (e >= 0 && s >= 0)
            {
                header = r; colElem = e; colStiff = s;
                break;
            }
        }
        if (header < 0) return;
        for (int r = header + 1; r < cells.Count; r++)
        {
            if (!TryParseInt(Cell(cells[r], colElem), out int elem))
                continue;
            if (!TryParseInt(Cell(cells[r], colStiff), out int stiff))
                continue;
            elemToStiff[elem] = stiff;
        }
    }

    static void ParseStiffnessThicknesses(
        IReadOnlyList<IReadOnlyList<string>> cells, Dictionary<int, double> stiffToH)
    {
        int header = -1, colId = -1, colH = -1;
        for (int r = 0; r < Math.Min(15, cells.Count); r++)
        {
            int h = FindCol(cells[r], "толщ");
            if (h < 0) continue;
            int id = FindCol(cells[r], "номер");
            if (id < 0) id = FindCol(cells[r], "№");
            if (id < 0) id = FindCol(cells[r], "Тип");
            if (id < 0) id = 0;
            header = r; colId = id; colH = h;
            break;
        }
        if (header < 0) return;

        // единицы: если в шапке «мм» — делим на 1000
        bool mm = false;
        for (int r = 0; r <= header; r++)
        {
            for (int c = 0; c < cells[r].Count; c++)
            {
                if (Cell(cells[r], c).Contains("мм", StringComparison.OrdinalIgnoreCase))
                    mm = true;
            }
        }

        for (int r = header + 1; r < cells.Count; r++)
        {
            if (!TryParseInt(Cell(cells[r], colId), out int id))
                continue;
            if (!TryParseDouble(Cell(cells[r], colH), out double h) || h <= 0)
                continue;
            if (mm) h /= 1000.0;
            stiffToH[id] = h;
        }
    }

    /// <summary>Приоритет: толщина из XLS (A) перекрывает топологию (B).</summary>
    static Dictionary<int, double> MergeThicknessMaps(
        IReadOnlyDictionary<int, double> topology,
        IReadOnlyDictionary<int, int> xlsElemToStiff,
        IReadOnlyDictionary<int, double> xlsStiffToH)
    {
        var map = new Dictionary<int, double>();
        foreach (var kv in topology)
        {
            if (kv.Value > 0)
                map[kv.Key] = kv.Value;
        }
        foreach (var (elem, stiffId) in xlsElemToStiff)
        {
            if (xlsStiffToH.TryGetValue(stiffId, out double h) && h > 0)
                map[elem] = h;
        }
        return map;
    }

    static void ParseLoadCaseNames(IReadOnlyList<IReadOnlyList<string>> cells, Dictionary<int, string> map)
    {
        for (int r = 0; r < cells.Count; r++)
        {
            if (!TryParseInt(Cell(cells, r, 0), out int num))
                continue;
            string name = Cell(cells, r, 1).Trim();
            if (!string.IsNullOrEmpty(name))
                map[num] = name;
        }
    }

    static List<List<string>> ReadSheetCells(ExcelDataReader.IExcelDataReader reader, int maxProbeRows)
    {
        var rows = new List<List<string>>();
        int n = 0;
        while (reader.Read() && n < maxProbeRows)
        {
            rows.Add(ReadCurrentRow(reader));
            n++;
        }
        return rows;
    }

    static List<List<string>> ReadRemainingAsNewList(List<List<string>> already, ExcelDataReader.IExcelDataReader reader)
    {
        var all = new List<List<string>>(already);
        while (reader.Read())
            all.Add(ReadCurrentRow(reader));
        return all;
    }

    static List<string> ReadCurrentRow(ExcelDataReader.IExcelDataReader reader)
    {
        int fc = Math.Max(reader.FieldCount, 0);
        var row = new List<string>(fc);
        for (int i = 0; i < fc; i++)
        {
            var v = reader.GetValue(i);
            row.Add(v switch
            {
                null => "",
                double d => d.ToString(CultureInfo.InvariantCulture),
                float f => f.ToString(CultureInfo.InvariantCulture),
                decimal m => m.ToString(CultureInfo.InvariantCulture),
                DateTime dt => dt.ToString(CultureInfo.InvariantCulture),
                _ => Convert.ToString(v, CultureInfo.InvariantCulture) ?? "",
            });
        }
        return row;
    }
}
