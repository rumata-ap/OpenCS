using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace CScore.Import;

/// <summary>Разбиение записей текстового файла ЛИРЫ на поля и разбор чисел.</summary>
internal static class LiraTextTokens
{
    static readonly char[] Separators = [' ', '\t', '\r', '\n'];

    public static string[] Split(string text) => text.Split(Separators, StringSplitOptions.RemoveEmptyEntries);

    public static bool TryInt(string s, out int v) =>
        int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out v);

    public static bool TryDouble(string s, out double v) =>
        double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out v);

    /// <summary>Все поля строки — числа; иначе null (и для пустой строки).</summary>
    public static double[]? TryNumbers(string text)
    {
        string[] t = Split(text);
        if (t.Length == 0) return null;
        var v = new double[t.Length];
        for (int i = 0; i < t.Length; i++)
            if (!TryDouble(t[i], out v[i])) return null;
        return v;
    }
}

/// <summary>
/// Потоковый разбор текстового файла схемы ЛИРЫ (<c>Data\&lt;Title&gt;.txt</c>, ЛИРА-САПФИР 2024/2025) в
/// <see cref="LiraTextSchema"/>. Файл — документы <c>( N/ запись/ запись/ … )</c> (и <c>{N/ … }</c> — описания
/// сечений, пропускаются). Разбираются документы 0 (единицы, загружения), 1 (КЭ), 2 (шарниры), 3 (жёсткости),
/// 4 (координаты), 5 (связи и объединения), 6 (нагрузки), 7 (значения нагрузок), 17 (углы — только записи
/// «КЭ угол»), 25 (АЖТ); остальные — счётчиками
/// в <see cref="LiraTextSchema.SkippedDocuments"/>.
/// <para>Правила записи (описание «ЛИРА-Windows», раздел 3): запись продолжается записью с ведущими нулями (док. 1 —
/// «0 0 узлы…», док. 3 и 7 — «0 …»); в документах 1–7 действует повторитель <c>R N1 N2 [N3]/ шаги…/</c> (п. 3.15) —
/// N1 строк (с N3-й развёрнутого документа или последние) повторяются N2 раз, к k-му повтору у каждой графы прибавлен k·шаг.</para>
/// </summary>
public static class LiraTextReader
{
    static LiraTextReader()
    {
        // Windows-1251 (старые файлы без BOM) не зарегистрирована в .NET Core/5+ по умолчанию.
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
    }

    /// <summary>Файл схемы: UTF-8 (с BOM, как пишет ЛИРА), UTF-16 по BOM или Windows-1251, если не UTF-8.</summary>
    public static LiraTextSchema Read(string path)
    {
        Encoding encoding = DetectEncoding(path);
        using var reader = new StreamReader(path, encoding, detectEncodingFromByteOrderMarks: true, bufferSize: 1 << 16);
        return Read(reader);
    }

    /// <summary>Текст схемы из открытого читателя.</summary>
    public static LiraTextSchema Read(TextReader reader)
    {
        var schema = new LiraTextSchema();
        DocumentParser? doc = null;
        foreach (var e in Scan(reader))
        {
            switch (e.Kind)
            {
                case ScanKind.Begin:
                    doc = e.Braced ? new SkippedDocument(schema, e.Document, true) : Create(schema, e.Document);
                    break;
                case ScanKind.Record:
                    doc!.Record(e.Text);
                    break;
                case ScanKind.End:
                    doc!.End();
                    doc = null;
                    break;
            }
        }
        if (doc != null)
        {
            schema.Warn($"Документ {doc.Number} не закрыт до конца файла.");
            doc.End();
        }
        return schema;
    }

    static Encoding DetectEncoding(string path)
    {
        var buf = new byte[1 << 16];
        int n;
        using (var fs = File.OpenRead(path)) n = fs.Read(buf, 0, buf.Length);
        if (n >= 3 && buf[0] == 0xEF && buf[1] == 0xBB && buf[2] == 0xBF) return Encoding.UTF8;
        if (n >= 2 && (buf[0] == 0xFF && buf[1] == 0xFE || buf[0] == 0xFE && buf[1] == 0xFF)) return Encoding.UTF8;  // StreamReader распознает BOM сам
        // Без BOM: строгая проверка начала файла как UTF-8 (без flush обрезанный на границе буфера символ не в счёт).
        try
        {
            new UTF8Encoding(false, throwOnInvalidBytes: true).GetDecoder().GetCharCount(buf, 0, n, flush: false);
            return new UTF8Encoding(false);
        }
        catch (DecoderFallbackException)
        {
            return Encoding.GetEncoding(1251);
        }
    }

    // ── Сканер документов и записей ──────────────────────────────────────────────────────────────

    enum ScanKind { Begin, Record, End }

    readonly record struct ScanEvent(ScanKind Kind, int Document, bool Braced, string Text);

    /// <summary>
    /// Документы и записи потоком. Закрывающая «)» завершает документ, только если за ней (через пробелы) идёт
    /// «(», «{» или конец файла — скобки в именах загружений не рвут документ 0.
    /// </summary>
    static IEnumerable<ScanEvent> Scan(TextReader r)
    {
        var sb = new StringBuilder();
        while (true)
        {
            // Вне документа: ищем «(N/» или «{N/».
            int c = r.Read();
            if (c < 0) yield break;
            if (c != '(' && c != '{') continue;
            bool braced = c == '{';
            char close = braced ? '}' : ')';
            while (r.Peek() is ' ' or '\t' or '\r' or '\n') r.Read();
            int number = 0, digits = 0;
            while (r.Peek() is >= '0' and <= '9')
            {
                number = number * 10 + (r.Read() - '0');
                digits++;
            }
            while (r.Peek() is ' ' or '\t' or '\r' or '\n') r.Read();
            if (digits == 0 || r.Peek() != '/') continue;
            r.Read();

            yield return new ScanEvent(ScanKind.Begin, number, braced, "");
            sb.Clear();
            while (true)
            {
                c = r.Read();
                if (c < 0)
                {
                    if (Flush(sb) is { } last) yield return new ScanEvent(ScanKind.Record, number, braced, last);
                    yield break;  // документ не закрыт — читатель предупредит
                }
                if (c == '/')
                {
                    if (Flush(sb) is { } rec) yield return new ScanEvent(ScanKind.Record, number, braced, rec);
                    continue;
                }
                if (c == close)
                {
                    if (braced) break;
                    var ws = new StringBuilder();
                    while (r.Peek() is ' ' or '\t' or '\r' or '\n') ws.Append((char)r.Read());
                    int next = r.Peek();
                    if (next is < 0 or '(' or '{') break;
                    sb.Append(')').Append(ws);
                    continue;
                }
                sb.Append((char)c);
            }
            if (Flush(sb) is { } tail) yield return new ScanEvent(ScanKind.Record, number, braced, tail);
            yield return new ScanEvent(ScanKind.End, number, braced, "");
        }
    }

    static string? Flush(StringBuilder sb)
    {
        string s = sb.ToString().Trim();
        sb.Clear();
        return s.Length == 0 ? null : s;
    }

    // ── Документы ────────────────────────────────────────────────────────────────────────────────

    static DocumentParser Create(LiraTextSchema s, int number) => number switch
    {
        0 => new HeaderDocument(s),
        1 => new ElementsDocument(s),
        2 => new HingesDocument(s),
        3 => new StiffnessDocument(s),
        4 => new CoordinatesDocument(s),
        5 => new SupportsDocument(s),
        6 => new LoadsDocument(s),
        7 => new LoadValuesDocument(s),
        17 => new AnglesDocument(s),
        25 => new RigidBodiesDocument(s),
        _ => new SkippedDocument(s, number, false),
    };

    abstract class DocumentParser(LiraTextSchema schema, int number)
    {
        protected readonly LiraTextSchema Schema = schema;
        public int Number { get; } = number;

        public abstract void Record(string text);
        public virtual void End() { }

        protected void Bad(string text, string why) =>
            Schema.Warn($"Документ {Number}: запись «{Shorten(text)}» не разобрана — {why}.");

        static string Shorten(string s)
        {
            s = s.ReplaceLineEndings(" ");
            return s.Length <= 60 ? s : s[..60] + "…";
        }
    }

    sealed class SkippedDocument(LiraTextSchema s, int number, bool braced) : DocumentParser(s, number)
    {
        int _records;
        public override void Record(string text) => _records++;
        public override void End() => Schema.SkippedDocuments.Add(new LiraTextSkippedDocument(Number, braced, _records));
    }

    /// <summary>Документ 0: строки <c>N; текст</c>.</summary>
    sealed class HeaderDocument(LiraTextSchema s) : DocumentParser(s, 0)
    {
        static readonly Regex RowSplit = new(@"/(?=\s*\d+\s*;)", RegexOptions.Compiled);
        static readonly Regex Row = new(@"^\s*(\d+)\s*;(.*)$", RegexOptions.Compiled | RegexOptions.Singleline);
        static readonly Regex CaseName = new(@"(\d+)\s*:\s*(.*?)\s*;", RegexOptions.Compiled | RegexOptions.Singleline);

        readonly List<string> _records = [];

        public override void Record(string text) => _records.Add(text);

        public override void End()
        {
            // Записи склеиваются обратно: «/» внутри имени не должна рвать строку.
            foreach (string part in RowSplit.Split(string.Join("/", _records)))
            {
                var m = Row.Match(part);
                if (!m.Success)
                {
                    Bad(part, "нет номера строки «N;»");
                    continue;
                }
                int row = int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture);
                string body = m.Groups[2].Value.Trim();
                Schema.HeaderRows[row] = body;
                switch (row)
                {
                    case 1: Schema.Title = body; break;
                    case 2: if (LiraTextTokens.TryInt(body, out int kind)) Schema.SchemaKind = kind; break;
                    case 33: Schema.Units = ParseUnits(body); break;
                    case 39:
                        foreach (Match c in CaseName.Matches(body))
                            Schema.LoadCaseNames[int.Parse(c.Groups[1].Value, CultureInfo.InvariantCulture)] = c.Groups[2].Value;
                        break;
                }
            }
        }

        LiraTextUnits ParseUnits(string body)
        {
            var d = LiraTextUnits.Default;
            string[] t = LiraTextTokens.Split(body);
            var names = new[] { d.Length, d.Section, d.Force, d.Temperature };
            var factors = new[] { d.LengthPerMeter, d.SectionPerMeter, d.ForcePerTonne, d.TemperatureFactor };
            for (int i = 0; i < 4 && 2 * i + 1 < t.Length; i++)
            {
                if (!LiraTextTokens.TryDouble(t[2 * i + 1], out double f) || f <= 0)
                {
                    Bad("33;" + body, "коэффициент единицы не число");
                    return d;
                }
                names[i] = t[2 * i];
                factors[i] = f;
            }
            return new LiraTextUnits(names[0], factors[0], names[1], factors[1], names[2], factors[2], names[3], factors[3]);
        }
    }

    /// <summary>
    /// Документы 1–7: повторитель R и продолжение записи строкой с ведущими нулями (док. 1 — два: «0 0 узлы…»,
    /// док. 3 и 7 — один; <paramref name="continuationZeros"/> = 0 — продолжений нет). Развёрнутые строки
    /// документа хранятся до его конца — на них ссылается N3 повторителя.
    /// </summary>
    abstract class LinesDocument(LiraTextSchema s, int number, int continuationZeros) : DocumentParser(s, number)
    {
        readonly List<string> _lines = [];
        (int Count, int Times, int? From)? _repeat;
        List<string[]>? _pending;

        public sealed override void Record(string text)
        {
            if (_repeat is { } rep)
            {
                _repeat = null;
                Expand(rep.Count, rep.Times, rep.From, LiraTextTokens.Split(text));
                return;
            }
            string[] t = LiraTextTokens.Split(text);
            if (t.Length > 0 && t[0] is "R" or "r")
            {
                if (t.Length is 3 or 4 && LiraTextTokens.TryInt(t[1], out int count) && LiraTextTokens.TryInt(t[2], out int times)
                    && count > 0 && times >= 0)
                {
                    int? from = null;
                    if (t.Length == 4)
                    {
                        if (!LiraTextTokens.TryInt(t[3], out int f) || f < 1)
                        {
                            Bad(text, "неверный номер строки повторителя");
                            return;
                        }
                        from = f;
                    }
                    _repeat = (count, times, from);
                }
                else Bad(text, "неверный повторитель");
                return;
            }
            _lines.Add(text);
            Line(text, t);
        }

        void Expand(int count, int times, int? from, string[] steps)
        {
            int start = from.HasValue ? from.Value - 1 : _lines.Count - count;
            if (start < 0 || start + count > _lines.Count)
            {
                Bad($"R {count} {times}{(from.HasValue ? " " + from : "")}", $"в документе только {_lines.Count} строк");
                return;
            }
            var block = _lines.GetRange(start, count).Select(LiraTextTokens.Split).ToList();
            for (int k = 1; k <= times; k++)
                foreach (string[] src in block)
                {
                    string[] t = Shift(src, steps, k);
                    string text = string.Join(" ", t);
                    _lines.Add(text);
                    Line(text, t);
                }
        }

        /// <summary>
        /// Графы строки плюс k·шаг; недостающие графы при ненулевом шаге — нули («0/ R 1 13/ 1/»). Ведущие нули
        /// строки продолжения — заполнители, не сдвигаются (п. 3.15: <c>31 5 1 2 18 19/ 0 0 44 45 61 62/ R 2 8/
        /// 0 1 1 1 1 1/</c> — меняется жёсткость, но не графы продолжения).
        /// </summary>
        string[] Shift(string[] src, string[] steps, int k)
        {
            int n = src.Length;
            for (int i = src.Length; i < steps.Length; i++)
                if (LiraTextTokens.TryDouble(steps[i], out double st) && st != 0) n = i + 1;
            int fixedColumns = continuationZeros > 0 && src[0] == "0" ? continuationZeros : 0;
            var r = new string[n];
            for (int i = 0; i < n; i++)
            {
                string v = i < src.Length ? src[i] : "0";
                string step = i < steps.Length && i >= fixedColumns ? steps[i] : "0";
                if (long.TryParse(v, NumberStyles.Integer, CultureInfo.InvariantCulture, out long lv)
                    && long.TryParse(step, NumberStyles.Integer, CultureInfo.InvariantCulture, out long ls))
                    r[i] = (lv + k * ls).ToString(CultureInfo.InvariantCulture);
                else if (LiraTextTokens.TryDouble(step, out double ds) && ds == 0)
                    r[i] = v;
                else if (LiraTextTokens.TryDouble(v, out double dv))
                    r[i] = (dv + k * ds).ToString("R", CultureInfo.InvariantCulture);
                else
                {
                    Bad(string.Join(" ", src), $"ненулевой шаг повторителя у нечисловой графы «{v}»");
                    r[i] = v;
                }
            }
            return r;
        }

        void Line(string text, string[] t)
        {
            if (continuationZeros == 0)
            {
                Entry([t], text);
                return;
            }
            if (t[0] == "0")
            {
                if (_pending == null) Bad(text, "продолжение без начальной записи");
                else if (t.Length < continuationZeros || t[..continuationZeros].Any(x => x != "0"))
                    Bad(text, $"продолжение должно начинаться с {continuationZeros} нулей");
                else _pending.Add(t[continuationZeros..]);
                return;
            }
            FlushPending();
            _pending = [t];
        }

        void FlushPending()
        {
            if (_pending == null) return;
            var p = _pending;
            _pending = null;
            Entry(p, string.Join(" / ", p.Select(x => string.Join(" ", x))));
        }

        public sealed override void End()
        {
            if (_repeat != null) Bad("R", "нет строки шагов повторителя");
            FlushPending();
            Finish();
        }

        /// <summary>Запись: первая строка и строки продолжения (без ведущих нулей).</summary>
        protected abstract void Entry(List<string[]> lines, string text);

        protected virtual void Finish() { }

        protected static int[]? Ints(IEnumerable<string> tokens)
        {
            var r = new List<int>();
            foreach (string s in tokens)
            {
                if (!LiraTextTokens.TryInt(s, out int v)) return null;
                r.Add(v);
            }
            return [.. r];
        }
    }

    sealed class ElementsDocument(LiraTextSchema s) : LinesDocument(s, 1, 2)
    {
        protected override void Entry(List<string[]> lines, string text)
        {
            int number = Schema.Elements.Count + 1;
            int[]? v = Ints(lines.SelectMany(x => x));
            if (v == null || v.Length < 2)
            {
                // КЭ всё равно занимает номер — иначе разойдётся нумерация последующих.
                Bad(text, $"КЭ {number}: ожидаются тип, жёсткость и узлы");
                Schema.Elements.Add(new LiraTextElement(number, 0, 0, []));
                return;
            }
            Schema.Elements.Add(new LiraTextElement(number, v[0], v[1], v[2..]));
        }
    }

    sealed class HingesDocument(LiraTextSchema s) : LinesDocument(s, 2, 0)
    {
        protected override void Entry(List<string[]> lines, string text)
        {
            string[] t = lines[0];
            int[]? head = t.Length >= 3 ? Ints(t[..^(t.Length == 3 ? 0 : 1)]) : null;
            double value = 0;
            if (head == null || head[1] is not (1 or 2) || t.Length > 3 && !LiraTextTokens.TryDouble(t[^1], out value))
            {
                Bad(text, "ожидается «КЭ конец(1/2) DOF… 0»");
                return;
            }
            Schema.Hinges.Add(new LiraTextHinge(head[0], head[1], head[2..], value));
        }
    }

    sealed class StiffnessDocument(LiraTextSchema s) : LinesDocument(s, 3, 1)
    {
        protected override void Entry(List<string[]> lines, string text)
        {
            if (!LiraTextTokens.TryInt(lines[0][0], out int number))
            {
                Bad(text, "нет номера жёсткости");
                return;
            }
            var texts = new List<string>(lines.Count) { string.Join(" ", lines[0][1..]) };
            texts.AddRange(lines.Skip(1).Select(x => string.Join(" ", x)));
            Schema.Stiffnesses.Add(new LiraTextStiffness(number, texts));
        }
    }

    sealed class CoordinatesDocument(LiraTextSchema s) : LinesDocument(s, 4, 0)
    {
        protected override void Entry(List<string[]> lines, string text)
        {
            string[] t = lines[0];
            var xyz = new double[3];
            for (int i = 0; i < t.Length; i++)
                if (i >= 3 || !LiraTextTokens.TryDouble(t[i], out xyz[i]))
                {
                    // Узел всё равно занимает номер.
                    Bad(text, $"узел {Schema.Nodes.Count + 1}: ожидается «x [y [z]]»");
                    break;
                }
            Schema.Nodes.Add(new LiraTextPoint(xyz[0], xyz[1], xyz[2]));
        }
    }

    /// <summary>
    /// Документ 5: запись <c>узел DOF…</c> — связь, если за ней не идут записи из одного номера узла; иначе —
    /// голова объединения перемещений, а эти записи — его узлы.
    /// </summary>
    sealed class SupportsDocument(LiraTextSchema s) : LinesDocument(s, 5, 0)
    {
        int _head;
        int[]? _dofs;
        List<int>? _members;

        protected override void Entry(List<string[]> lines, string text)
        {
            int[]? v = Ints(lines[0]);
            if (v == null || v.Any(x => x <= 0))
            {
                Close();
                Bad(text, "ожидаются положительные номера узла и DOF");
                return;
            }
            if (v.Length == 1)
            {
                if (_dofs == null) Bad(text, "узел объединения без строки «узел DOF…»");
                else (_members ??= []).Add(v[0]);
                return;
            }
            Close();
            if (v[1..].Any(d => d > 6))
            {
                Bad(text, "DOF вне 1–6");
                return;
            }
            _head = v[0];
            _dofs = v[1..];
        }

        void Close()
        {
            if (_dofs == null) return;
            if (_members == null) Schema.Supports.Add(new LiraTextSupport(_head, _dofs));
            else Schema.Couplings.Add(new LiraTextCoupling(_head, _dofs, [.. _members]));
            _dofs = null;
            _members = null;
        }

        protected override void Finish() => Close();
    }

    sealed class LoadsDocument(LiraTextSchema s) : LinesDocument(s, 6, 0)
    {
        int? _case;

        protected override void Entry(List<string[]> lines, string text)
        {
            int[]? v = Ints(lines[0]);
            if (v == null || v.Length is not (4 or 5))
            {
                Bad(text, "ожидается «объект вид направление №строки [загружение]»");
                return;
            }
            if (v.Length == 5) _case = v[4];
            else if (_case == null)
            {
                Bad(text, "нет загружения ни у записи, ни у предыдущих");
                return;
            }
            Schema.Loads.Add(new LiraTextLoad(v[0], v[1], v[2], v[3], _case.Value));
        }
    }

    sealed class LoadValuesDocument(LiraTextSchema s) : LinesDocument(s, 7, 1)
    {
        protected override void Entry(List<string[]> lines, string text)
        {
            var values = new List<double>();
            bool ok = LiraTextTokens.TryInt(lines[0][0], out int number);
            foreach (string tok in lines.SelectMany(x => x).Skip(1))
            {
                if (!ok) break;
                ok = LiraTextTokens.TryDouble(tok, out double d);
                values.Add(d);
            }
            if (!ok)
            {
                Bad(text, "ожидается «№ v1 v2 …»");
                return;
            }
            if (!Schema.LoadValues.TryAdd(number, [.. values])) Bad(text, $"строка {number} повторяется");
        }
    }

    /// <summary>
    /// Документ 17: записи «КЭ угол» (у стержней — угол разворота местных осей, = таблица 17 API) — в
    /// <see cref="LiraTextSchema.ElementAngles"/>; записи другой формы (у пластин бывают по 5–6 чисел, смысл не
    /// установлен) — счётчиком неразобранного документа.
    /// </summary>
    sealed class AnglesDocument(LiraTextSchema s) : DocumentParser(s, 17)
    {
        int _other;

        public override void Record(string text)
        {
            var t = LiraTextTokens.Split(text);
            if (t.Length == 2 && LiraTextTokens.TryInt(t[0], out int element) && LiraTextTokens.TryDouble(t[1], out double angle))
                Schema.ElementAngles[element] = angle;
            else _other++;
        }

        public override void End()
        {
            if (_other > 0) Schema.SkippedDocuments.Add(new LiraTextSkippedDocument(Number, false, _other));
        }
    }

    sealed class RigidBodiesDocument(LiraTextSchema s) : DocumentParser(s, 25)
    {
        public override void Record(string text)
        {
            var t = LiraTextTokens.Split(text);
            var v = new int[t.Length];
            for (int i = 0; i < t.Length; i++)
                if (!LiraTextTokens.TryInt(t[i], out v[i]))
                {
                    Bad(text, "ожидаются номера узлов");
                    return;
                }
            int? code = v[^1] < 0 ? v[^1] : null;
            int[] nodes = code.HasValue ? v[..^1] : v;
            if (nodes.Length < 2 || nodes.Any(x => x <= 0))
            {
                Bad(text, "ожидаются ведущий и ведомые узлы");
                return;
            }
            Schema.RigidBodies.Add(new LiraTextRigidBody(nodes[0], nodes[1..], code));
        }
    }
}
