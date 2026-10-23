using System.Globalization;
using CScore.Fem;

namespace CScore.Import;

/// <summary>
/// Перенос нагрузок из текстового файла ЛИРЫ (<see cref="LiraTextSchema"/>, документы 6 и 7) в нагрузки сеточного
/// уровня схемы. Узлы и КЭ сопоставляются по номерам (теги сетки импорта ЛИРЫ — номера схемы), загружения — по
/// номерам документа 0 (строка 39 — имена). Собственный вес ЛИРА записывает обычной нагрузкой (вид 16) в своё
/// загружение — он переносится как есть, коэффициент собственного веса загружения не ставится (иначе двойной учёт).
/// <para>Виды (описание «ЛИРА-Windows», раздел 3, док. 6, проверено на образцах ЛИРЫ 2013–2025):</para>
/// <list type="bullet">
/// <item>0 — узловая в общих осях, направления 1–3 силы, 4–6 моменты → <see cref="FemMeshNodeLoad"/>;</item>
/// <item>6/16 — равномерная (местные/общие оси) → <see cref="FemElementLoadKinds.Uniform"/>;</item>
/// <item>7/17 — трапециевидная: на пластине — значения в узлах КЭ в порядке документа 1 (= порядок хранения, у Q4
/// 1-2-4-3; на «Тест_стена» гидростатика линейна по z узлов) → <see cref="FemElementLoadKinds.Nodal"/>, у треугольника
/// — первые три числа; на стержне <c>q1 a1 q2 a2</c> → <see cref="FemElementLoadKinds.Trapezoid"/>;</item>
/// <item>5/15 — сосредоточенная: стержень <c>P A</c>, пластина <c>P A B</c> — привязки от узла 1 по X1 (узел 1 → 2) и
/// Y1 = Z1 × X1 (1 241 518 из 1 241 531 точек образцов внутри КЭ, прочие — за краем на доли мм) →
/// <see cref="FemElementLoadKinds.Point"/>;</item>
/// <item>19 — пластины, <c>i j q1 q2</c>: погонная по ребру между узлами i, j (номера в порядке хранения: в образцах
/// рёбра Q4 1-3 и 2-4 встречаются тысячами — при геометрической нумерации это диагонали) →
/// <see cref="FemElementLoadKinds.Edge"/>.</item>
/// </list>
/// Знак ЛИРЫ: положительные силы — против осей, моменты — по часовой стрелке с конца оси, поэтому силы и моменты
/// переносятся с обратным знаком. Единицы — документ 0 (строка 33). Местные оси стержня — оси сетки: правило ЛИРЫ по
/// умолчанию с углом разворота документа 17, заданные при импорте схемы (<see cref="LiraSchemaConverter.BarRotationDeg"/>;
/// углы пластин на привязки не влияют — 32 526 точек образцов на пластинах с углом внутри КЭ в неповёрнутых осях).
/// Не переносятся (в журнал): температура, заданные смещения,
/// синусоидальные, динамические параметры узловых, моменты на КЭ, виды 4 и 60 (в описании формата их нет), записи с
/// ненулевыми дополнительными числами строки значений, смысл которых не установлен. Повторный перенос заменяет всё
/// перенесённое раньше (происхождение «import:lira»); заданное вручную не трогается.
/// </summary>
public static class LiraLoadTransfer
{
    public static readonly string Origin = LiraSchemaConverter.BoundaryOrigin;

    const int Node = 0, PointLocal = 5, EvenlyLocal = 6, TrapezLocal = 7, PointGlobal = 15, EvenlyGlobal = 16,
        TrapezGlobal = 17, EdgeGlobal = 19;

    /// <param name="schema">Разобранный текстовый файл схемы.</param>
    /// <param name="meshNodeTags">Теги узлов сетки схемы (номера узлов ЛИРЫ).</param>
    /// <param name="elementTypes">Тег КЭ сетки (номер КЭ ЛИРЫ) → тип («beam» | «shell»).</param>
    /// <param name="tonToKn">Сколько кН в тонне-силе (настройка импорта): нагрузки — в единицах документа 0.</param>
    /// <param name="loadCases">Загружения схемы.</param>
    /// <param name="elementLoads">Нагрузки на КЭ схемы.</param>
    /// <param name="meshNodeLoads">Узловые нагрузки на узлы сетки схемы.</param>
    /// <param name="allocateLoadCaseId">Временный Id нового загружения (отрицательный, до сохранения).</param>
    public static FemLoadTransferResult Transfer(LiraTextSchema schema, IReadOnlySet<string> meshNodeTags,
        IReadOnlyDictionary<string, string> elementTypes, double tonToKn,
        IReadOnlyList<FemLoadCase> loadCases, IReadOnlyList<FemElementLoad> elementLoads,
        IReadOnlyList<FemMeshNodeLoad> meshNodeLoads, Func<int> allocateLoadCaseId)
    {
        double fu = schema.Units.ForceUnitN(tonToKn), lu = schema.Units.LengthUnitM;
        static string T(int id) => id.ToString(CultureInfo.InvariantCulture);
        var report = new List<string>();
        var cases = loadCases.ToList();
        var bySource = cases.Where(c => c.Origin == Origin && c.SourceLoadNum != null)
            .GroupBy(c => c.SourceLoadNum!.Value).ToDictionary(g => g.Key, g => g.First());
        var newElementLoads = new List<FemElementLoad>();
        var newNodeLoads = new List<FemMeshNodeLoad>();
        int created = 0, skippedRecords = 0, localBarTransverse = 0;

        var caseNums = schema.LoadCaseNames.Keys.Concat(schema.Loads.Select(l => l.LoadCase)).Distinct().Order().ToList();
        var states = new Dictionary<int, CaseState>();
        foreach (int num in caseNums)
        {
            if (!bySource.TryGetValue(num, out var target))
            {
                string name = schema.LoadCaseNames.GetValueOrDefault(num)?.Trim() ?? "";
                target = new FemLoadCase
                {
                    Id = allocateLoadCaseId(), Origin = Origin, SourceLoadNum = num,
                    Tag = UniqueTag(name.Length > 0 ? name : $"L{num}", cases),
                };
                cases.Add(target);
                created++;
            }
            states[num] = new CaseState(target);
        }

        foreach (var r in schema.Loads)
        {
            var st = states[r.LoadCase];
            void Skip(string what) { st.Skipped[what] = st.Skipped.GetValueOrDefault(what) + 1; skippedRecords++; }

            if (!schema.LoadValues.TryGetValue(r.ValueRow, out var v) || v.Length == 0) { Skip("нет строки значений документа 7"); continue; }

            if (r.Kind == Node)
            {
                if (r.Direction is < 1 or > 6) { Skip($"узловые с направлением {r.Direction}"); continue; }
                if (HasExtra(v, 1)) { Skip("узловые с параметрами динамики (лишние числа строки значений)"); continue; }
                string tag = T(r.Target);
                if (!meshNodeTags.Contains(tag)) { Skip("узловые на узлы, которых нет в сетке"); continue; }
                if (!st.Nodes.TryGetValue(tag, out var load))
                {
                    st.Nodes[tag] = load = new FemMeshNodeLoad { LoadCaseId = st.Target.Id, Origin = Origin, MeshNodeTag = tag };
                    newNodeLoads.Add(load);
                }
                double value = -v[0] * fu * (r.Direction > 3 ? lu : 1);
                switch (r.Direction)
                {
                    case 1: load.Fx += value; break;
                    case 2: load.Fy += value; break;
                    case 3: load.Fz += value; break;
                    case 4: load.Mx += value; break;
                    case 5: load.My += value; break;
                    default: load.Mz += value; break;
                }
                continue;
            }

            if (r.Kind is not (PointLocal or EvenlyLocal or TrapezLocal or PointGlobal or EvenlyGlobal or TrapezGlobal or EdgeGlobal))
            {
                Skip(Describe(r.Kind));
                continue;
            }
            if (r.Direction is < 1 or > 3) { Skip($"моменты на КЭ (вид {r.Kind}, направление {r.Direction})"); continue; }
            string elemTag = T(r.Target);
            string? type = elementTypes.GetValueOrDefault(elemTag);
            bool shell = type == "shell", bar = type == "beam";
            if (!shell && !bar) { Skip($"вид {r.Kind} на КЭ, которых нет в сетке среди стержней и пластин"); continue; }
            bool local = r.Kind is PointLocal or EvenlyLocal or TrapezLocal;
            string cs = local ? "local" : "global";
            string axis = r.Direction switch { 1 => "x", 2 => "y", _ => "z" };
            if (bar && local && r.Direction > 1) localBarTransverse++;
            double perArea = fu / (lu * lu), perLength = fu / lu;

            switch (r.Kind)
            {
                case EvenlyLocal or EvenlyGlobal:
                    if (HasExtra(v, 1)) { Skip($"равномерная (вид {r.Kind}) с дополнительным числом, смысл не установлен"); break; }
                    st.Add(FemElementLoadKinds.Uniform, cs, axis, [-v[0] * (shell ? perArea : perLength)], elemTag, newElementLoads);
                    break;
                case TrapezLocal or TrapezGlobal when shell:
                {
                    int n = NodeCount(schema, r.Target);
                    if (v.Length < n) { Skip($"трапециевидная (вид {r.Kind}) на пластине: значений меньше, чем узлов"); break; }
                    st.Add(FemElementLoadKinds.Nodal, cs, axis, v.Take(n).Select(q => -q * perArea).ToArray(), elemTag, newElementLoads);
                    break;
                }
                case TrapezLocal or TrapezGlobal:
                    if (v.Length < 4) { Skip($"трапециевидная (вид {r.Kind}) на стержне: ожидается q1 a1 q2 a2"); break; }
                    st.Add(FemElementLoadKinds.Trapezoid, cs, axis,
                        [-v[0] * perLength, v[1] * lu, -v[2] * perLength, v[3] * lu], elemTag, newElementLoads);
                    break;
                case PointLocal or PointGlobal when shell:
                    if (v.Length < 3 || HasExtra(v, 3)) { Skip($"сосредоточенная (вид {r.Kind}) на пластине: ожидается P A B"); break; }
                    st.Add(FemElementLoadKinds.Point, cs, axis, [-v[0] * fu, v[1] * lu, v[2] * lu], elemTag, newElementLoads);
                    break;
                case PointLocal or PointGlobal:
                    if (v.Length < 2 || HasExtra(v, 2)) { Skip($"сосредоточенная (вид {r.Kind}) на стержне: ожидается P A"); break; }
                    st.Add(FemElementLoadKinds.Point, cs, axis, [-v[0] * fu, v[1] * lu], elemTag, newElementLoads);
                    break;
                case EdgeGlobal when shell:
                    if (v.Length < 4 || !IsEdge(v[0], v[1], NodeCount(schema, r.Target)))
                    {
                        Skip("нагрузка на ребро пластины (вид 19): узлы i, j — не ребро");
                        break;
                    }
                    st.Add(FemElementLoadKinds.Edge, cs, axis, [v[0], v[1], -v[2] * perLength, -v[3] * perLength], elemTag, newElementLoads);
                    break;
                default:
                    Skip($"вид {r.Kind} на стержнях");
                    break;
            }
        }

        foreach (var (num, st) in states)
        {
            st.Flush();
            foreach (var (what, count) in st.Skipped)
                report.Add($"Загружение {num} «{st.Target.Tag}»: не перенесено — {what}, записей {count}.");
        }
        if (localBarTransverse > 0)
            report.Add($"Местные нагрузки по Y1/Z1 стержней ({localBarTransverse}) приложены в местных осях сетки — оси ЛИРЫ " +
                "с углом разворота стержня, прочитанным при импорте схемы.");
        foreach (var orphan in cases.Where(c => c.Origin == Origin && c.SourceLoadNum is { } n && !states.ContainsKey(n)))
            report.Add($"Загружение «{orphan.Tag}» (ЛИРА {orphan.SourceLoadNum}) в файле отсутствует — перенесённые в него нагрузки удалены.");

        report.Insert(0, $"Перенесено из ЛИРЫ: загружений {states.Count} (новых {created}), нагрузок на КЭ " +
            $"{newElementLoads.Count}, узловых {newNodeLoads.Count}; не перенесено записей {skippedRecords}.");
        return new FemLoadTransferResult(cases,
            elementLoads.Where(l => l.Origin != Origin).Concat(newElementLoads).ToList(),
            meshNodeLoads.Where(l => l.Origin != Origin).Concat(newNodeLoads).ToList(),
            report, created, newElementLoads.Count, newNodeLoads.Count, skippedRecords);
    }

    /// <summary>Нагрузки одного загружения: слияние одинаковых нагрузок на КЭ в одну запись, узловые — по узлу.</summary>
    sealed class CaseState(FemLoadCase target)
    {
        public FemLoadCase Target { get; } = target;
        public Dictionary<string, FemMeshNodeLoad> Nodes { get; } = new(StringComparer.Ordinal);
        public SortedDictionary<string, int> Skipped { get; } = new(StringComparer.Ordinal);
        // Ключ «вид|оси|ось|значения» → цепочка записей: повтор той же нагрузки на тот же КЭ (две записи файла) должен
        // складываться, а цель записи — без повторов, поэтому повтор уходит в следующую запись цепочки.
        readonly Dictionary<Key, List<Entry>> _merged = [];

        /// <summary>Ключ слияния: вид, оси, ось и до четырёх значений (без строк — 0,6 млн записей ФР19).</summary>
        readonly record struct Key(string Kind, string Cs, string Axis, int Count, double V0, double V1, double V2, double V3);

        sealed class Entry(FemElementLoad load)
        {
            public FemElementLoad Load { get; } = load;
            public List<string> Tags { get; } = [];
            HashSet<string>? _seen; // со второго тега: у большинства записей (сосредоточенные) тег один

            public bool Has(string tag) => _seen?.Contains(tag) ?? (Tags.Count == 1 && Tags[0] == tag);

            public void Add(string tag)
            {
                if (Tags.Count == 1) _seen = new HashSet<string>(Tags, StringComparer.Ordinal);
                _seen?.Add(tag);
                Tags.Add(tag);
            }
        }

        public void Add(string kind, string cs, string axis, double[] values, string tag, List<FemElementLoad> sink)
        {
            if (values.Length > 4) throw new ArgumentException("Не более четырёх значений нагрузки.", nameof(values));
            double V(int k) => k < values.Length ? values[k] : 0;
            var key = new Key(kind, cs, axis, values.Length, V(0), V(1), V(2), V(3));
            if (!_merged.TryGetValue(key, out var chain)) _merged[key] = chain = [];
            var entry = chain.FirstOrDefault(e => !e.Has(tag));
            if (entry == null)
            {
                var load = new FemElementLoad
                {
                    LoadCaseId = Target.Id, Origin = Origin, TargetKind = FemLoadTargetKinds.Elements,
                    LoadKind = kind, CoordinateSystem = cs, Axis = axis,
                };
                load.SetValues(values);
                chain.Add(entry = new Entry(load));
                sink.Add(load);
            }
            entry.Add(tag);
        }

        /// <summary>Назначает цели записей (один раз — сериализация списка тегов дорогая).</summary>
        public void Flush()
        {
            foreach (var entry in _merged.Values.SelectMany(c => c)) entry.Load.SetTargetTags(entry.Tags);
            _merged.Clear();
        }
    }

    /// <summary>Числа строки значений после первых <paramref name="used"/> не нулевые.</summary>
    static bool HasExtra(double[] v, int used)
    {
        for (int i = used; i < v.Length; i++)
            if (v[i] != 0) return true;
        return false;
    }

    static int NodeCount(LiraTextSchema schema, int element) =>
        element >= 1 && element <= schema.Elements.Count ? schema.Elements[element - 1].Nodes.Length : 0;

    /// <summary>Номера узлов <paramref name="i"/>, <paramref name="j"/> (с 1, порядок хранения) — ребро пластины из
    /// <paramref name="n"/> узлов; у Q4 контур 1-2-4-3.</summary>
    static bool IsEdge(double i, double j, int n)
    {
        if (i != Math.Floor(i) || j != Math.Floor(j) || i < 1 || j < 1 || i > n || j > n || i == j) return false;
        if (n == 3) return true;
        int[] contour = [1, 2, 4, 3];
        int d = Math.Abs(Array.IndexOf(contour, (int)i) - Array.IndexOf(contour, (int)j));
        return n == 4 && (d == 1 || d == 3);
    }

    static string UniqueTag(string tag, IReadOnlyList<FemLoadCase> cases)
    {
        if (cases.All(c => c.Tag != tag)) return tag;
        for (int k = 2; ; k++)
            if (cases.All(c => c.Tag != $"{tag} ({k})")) return $"{tag} ({k})";
    }

    static string Describe(int kind) => kind switch
    {
        1 or 11 => $"заданные смещения (вид {kind})",
        8 or 18 or 28 or 38 or 48 or 88 => $"температура (вид {kind})",
        26 or 36 or 46 or 56 => $"синусоидальная (вид {kind})",
        9 => "равномерная, вид 9 (отличие от вида 6 не установлено)",
        10 or 20 => $"трапециевидная, вид {kind} (отличие от видов 7/17 не установлено)",
        25 or 35 => $"сосредоточенная, вид {kind} (отличие от вида 15 не установлено)",
        _ => $"вид {kind} (в описании формата нет)",
    };
}
