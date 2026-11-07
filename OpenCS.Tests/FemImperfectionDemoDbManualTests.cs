using CScore;
using CScore.Fem;
using CScore.Fem.Imperfections;
using CScore.ParametricRc;
using CScore.ParametricSteel;
using CScore.Planar;
using Microsoft.Data.Sqlite;
using OpenCS.Services;
using OpenCS.Tasks;
using OpenCS.Utilites;
using Xunit;

namespace OpenCS.Tests;

/// <summary>
/// Тестовый проект для ручной проверки начальных несовершенств (срезы 0–7) и снимков экрана сайта. Запуск:
/// <c>OPENCS_IMPERFECTION_DEMO_DB=путь\к\файлу.db dotnet test OpenCS.Tests -p:AllowUnsafeBlocks=true
/// --filter FullyQualifiedName~FemImperfectionDemoDbManualTests</c>; без переменной тест ничего не делает.
/// Схемы: 1 — консоль с креном (TEST-IMP-01), 2 — шарнирный стержень с погибью (TEST-IMP-02), 3 — колонна с формой
/// по (5.9) (TEST-IMP-03), 4 — рама (крен по (5.5) с m, погиби «авто» и критерий 5.3.2(6), формы, пакет «±»),
/// стойки и ригель — гнутые профили ГОСТ 30245-2012 из сортамента; 5 — ЖБ-колонна (e_a п. 8.1.7 СП 63 и eᵢ EN 1992), 6 — стена по ярусам с перекрытием. Сетки и расчёты не выполнены —
/// их выполняет проверяющий.
/// </summary>
public sealed class FemImperfectionDemoDbManualTests
{
    const int Fixed = 63, PinnedBase = 0b100111, PinnedTop = 0b100011, HoldY = 0b000010;

    [Fact]
    public void CreateDemoDatabase()
    {
        var path = Environment.GetEnvironmentVariable("OPENCS_IMPERFECTION_DEMO_DB");
        if (string.IsNullOrWhiteSpace(path)) return;
        if (File.Exists(path)) File.Delete(path);
        try
        {
            using var db = new DatabaseService(path);
            var catalog = CatalogDirectory();

            // Сталь тестов TEST-IMP-01/02: E = 210 ГПа (как в статьях); TEST-IMP-03 и рама — С355 по СП 16 (E = 206 ГПа).
            var s355 = MaterialCatalog.CreateStructuralSteel("С355", 0.010, shaped: false, catalog) ?? throw new InvalidOperationException("С355");
            db.AddMaterial(s355);
            var s355e210 = MaterialCatalog.CreateStructuralSteel("С355", 0.008, shaped: false, catalog) ?? throw new InvalidOperationException("С355");
            s355e210.Tag += ", E = 210 ГПа (примеры EN)";
            s355e210.E = 210_000_000;
            foreach (var c in s355e210.MaterialChars) c.E = 210_000_000;
            db.AddMaterial(s355e210);
            var concrete = MaterialCatalog.CreateHeavyConcrete("B25", catalog) ?? throw new InvalidOperationException("B25");
            var rebar = MaterialCatalog.CreateRebar("A500", catalog) ?? throw new InvalidOperationException("A500");
            db.AddMaterial(concrete);
            db.AddMaterial(rebar);

            var tube200 = SteelSection(db, "Труба □200×8 (E = 210 ГПа)", ParametricSteelSectionDefinition.WeldedBox(0.2, 0.2, 0.008, 0.008), s355e210);
            var tube300 = SteelSection(db, "Труба □300×10", ParametricSteelSectionDefinition.WeldedBox(0.3, 0.3, 0.010, 0.010), s355);
            // Рама — гнутые замкнутые квадратные профили по ГОСТ 30245-2012 из сортамента (скругления углов, кривая по табл. 6.2).
            var sortament = new ProfileDB(Path.Combine(catalog, "Sortamenty.db3"));
            var tube200s = CatalogSection(db, sortament, "□200×8 ГОСТ 30245-2012", Gost30245Square, "200x8", s355);
            var tube300s = CatalogSection(db, sortament, "□300×10 ГОСТ 30245-2012", Gost30245Square, "300x10", s355);
            var rc400 = RcSection(db, "К 400×400, 6Ø20", 0.4, 0.4, concrete, rebar);
            var slab = Plate(db, "П 200", 0.2, concrete, rebar);
            var wall = Plate(db, "С 200", 0.2, concrete, rebar);

            Cantilever(db, tube200);
            PinnedBow(db, tube200);
            ModeColumn(db, tube300);
            Frame(db, tube200s, tube300s);
            RcColumn(db, rc400);
            WallStoreys(db, rc400, slab, wall);
        }
        finally { SqliteConnection.ClearAllPools(); }
    }

    /// <summary>
    /// Сетки и расчёт постановок базы (OPENCS_IMPERFECTION_DEMO_CHECK — путь к базе; результаты пишутся в неё):
    /// устойчивость, затем секущие расчёты; пакет — только «Проверить вход». Сводки и отчёт несовершенств — в вывод.
    /// </summary>
    [Fact]
    public async Task RunDemoDatabase()
    {
        var path = Environment.GetEnvironmentVariable("OPENCS_IMPERFECTION_DEMO_CHECK");
        if (string.IsNullOrWhiteSpace(path)) return;
        var root = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(path))!, "gmsh");
        try
        {
            using var db = new DatabaseService(path);
            db.LoadAll();
            var log = new OutputLog(output);
            var gmsh = new GmshSettings { ExecutablePath = @"C:\Tools\gmsh-4.15.2-Windows64\gmsh.exe", ArtifactsPath = root };
            var ctx = new FemCsfeaRunContext(db, gmsh, log);
            foreach (var schema in db.FemSchemas.ToList())
            {
                output.WriteLine($"=== {schema.Tag}");
                foreach (var analysis in schema.Analyses.OrderBy(a => a.Kind == FemBucklingRunner.AnalysisKind ? 0 : 1).ToList())
                {
                    bool batch = FemAnalysisParams.Parse(analysis.ParamsJson).Batch != null;
                    if (batch)
                    {
                        var prepared = await FemCsfeaRunner.PrepareAsync(ctx, schema, analysis, buildMesh: true, CancellationToken.None);
                        output.WriteLine($"«{analysis.Tag}» (пакет) — проверка входа: ошибки {prepared.HasErrors}");
                        foreach (var line in prepared.Describe().Concat(prepared.Report)) output.WriteLine("  " + line);
                        continue;
                    }
                    var result = analysis.Kind == FemBucklingRunner.AnalysisKind
                        ? await FemBucklingRunner.RunAsync(ctx, schema, analysis, CancellationToken.None)
                        : await FemCsfeaRunner.RunAsync(ctx, schema, analysis, null, CancellationToken.None);
                    db.SaveCalcResult(result);
                    analysis.ResultId = result.Id;
                    analysis.Status = result.Status;
                    db.SaveFemAnalysis(analysis);
                    output.WriteLine($"«{analysis.Tag}» — {result.Status}");
                    if (analysis.Kind == FemBucklingRunner.AnalysisKind)
                    {
                        var b = FemBucklingSummary.Parse(result.DataJson);
                        output.WriteLine($"  λ: {string.Join("; ", b?.Cases.SelectMany(c => c.Lambda).Select(l => l.ToString("0.###")) ?? [])}");
                        continue;
                    }
                    var summary = FemCsfeaResultSummary.Parse(result.DataJson);
                    if (summary == null) { output.WriteLine("  " + result.DataJson); continue; }
                    foreach (var line in summary.Describe()) output.WriteLine("  " + line);
                    foreach (var line in summary.Imperfections?.Lines() ?? []) output.WriteLine("  несовершенства: " + line);
                    var last = summary.Steps.LastOrDefault();
                    if (last != null) output.WriteLine($"  последний шаг: λ = {last.LoadFactor:0.###}, сошёлся {last.Converged}, контроль = {last.Control * 1e3:0.###} мм");
                }
            }
        }
        finally { SqliteConnection.ClearAllPools(); }
    }

    readonly Xunit.Abstractions.ITestOutputHelper output;

    public FemImperfectionDemoDbManualTests(Xunit.Abstractions.ITestOutputHelper output) => this.output = output;

    sealed class OutputLog(Xunit.Abstractions.ITestOutputHelper output) : ILogService
    {
        public System.Collections.ObjectModel.ObservableCollection<LogEntry> LogEntries { get; } = [];
        public void Info(string message) => output.WriteLine("журнал: " + message);
        public void Warning(string message) => output.WriteLine("журнал (!): " + message);
        public void Error(string message) => output.WriteLine("журнал (ошибка): " + message);
    }

    // ── 1. TEST-IMP-01 ───────────────────────────────────────────────────────────────────────────────────

    static void Cantilever(DatabaseService db, CrossSection tube)
    {
        var b = new SchemaBuilder(db, "TEST-IMP-01 Консоль с креном");
        var n1 = b.Node(0, 0, 0, Fixed);
        var n2 = b.Node(0, 0, 4);
        b.Bar("К1", n1, n2, tube, 0.5);
        var p = b.Case("P (0,5·P_cr)", FemSp20Types.Permanent);
        b.Load(p, n2, fz: -612.3);
        var imp = b.Case("Крен", FemSp20Types.Imperfection);
        var comb = b.Combination("P + крен", "C", (p, 1), (imp, 1));
        b.Save();
        b.Imperfection(imp, FemImperfectionKinds.Sway, "Крен X (5.5)", new FemSwayParams
        {
            PhiMode = FemSwayParams.PhiEn1993, HeightM = 4, M = 1, AngleDeg = 0,
        }.ToJson(), "φ = 1/200: h = 4 м, m = 1");
        b.Secant("Второй порядок, упруго", comb, physical: false, control: (n2, 0));
    }

    // ── 2. TEST-IMP-02 ───────────────────────────────────────────────────────────────────────────────────

    static void PinnedBow(DatabaseService db, CrossSection tube)
    {
        var b = new SchemaBuilder(db, "TEST-IMP-02 Шарнирный стержень с погибью");
        var n1 = b.Node(0, 0, 0, PinnedBase);
        var n2 = b.Node(0, 0, 6, PinnedTop);
        b.Bar("К1", n1, n2, tube, 0.75);
        var p = b.Case("P (0,5·P_cr)", FemSp20Types.Permanent);
        b.Load(p, n2, fz: -1088.5);
        var imp = b.Case("Погибь", FemSp20Types.Imperfection);
        var comb = b.Combination("P + погибь", "C", (p, 1), (imp, 1));
        b.Save();
        b.Imperfection(imp, FemImperfectionKinds.Bow, "Погибь К1 (табл. 5.1)", new FemBowParams
        {
            MemberTags = ["К1"], Plane = FemBowParams.PlaneY, E0Mode = FemBowParams.E0Table, Curve = "a",
            Sign = FemBowParams.SignAuto, Activity = FemBowParams.ActivityAlways,
        }.ToJson(), "кривая a, упругий расчёт: e₀ = L/300 = 20 мм; концы шарнирные — «всегда»");
        b.Secant("Второй порядок, упруго", comb, physical: false, control: null);
    }

    // ── 3. TEST-IMP-03 ───────────────────────────────────────────────────────────────────────────────────

    static void ModeColumn(DatabaseService db, CrossSection tube)
    {
        var b = new SchemaBuilder(db, "TEST-IMP-03 Колонна, форма по (5.9)");
        var nodes = new List<FemNode>();
        for (int i = 0; i <= 4; i++) nodes.Add(b.Node(0, 0, 1.5 * i, i == 0 ? PinnedBase : i == 4 ? PinnedTop : HoldY));
        for (int i = 0; i < 4; i++) b.Bar($"К1.{i + 1}", nodes[i], nodes[i + 1], tube, 1.5);
        var p = b.Case("P = 1000 кН (устойчивость)", FemSp20Types.Permanent);
        b.Load(p, nodes[4], fz: -1000);
        var pChi = b.Case("P = χ·N_Rk", FemSp20Types.Permanent);
        b.Load(pChi, nodes[4], fz: -3062.2);
        var imp = b.Case("Форма 1 по (5.9)", FemSp20Types.Imperfection);
        var combP = b.Combination("P", "C", (p, 1));
        var comb = b.Combination("χ·N_Rk + форма", "C", (pChi, 1), (imp, 1));
        b.Save();
        int buckling = b.Buckling("Устойчивость", combP);
        b.Imperfection(imp, FemImperfectionKinds.Modes, "Форма 1, (5.9)", new FemModesParams
        {
            AnalysisId = buckling, Curve = "c", GammaM1 = 1.0,
            Modes = [new FemModeAmplitude { Mode = 1, Amplitude = FemModeAmplitude.AmplitudeEn1993 }],
        }.ToJson(), "кривая c, γ_M1 = 1: η_init = e₀ = 21,51 мм");
        b.Secant("Второй порядок при χ·N_Rk", comb, physical: false, control: (nodes[2], 0));
    }

    // ── 4. Рама ──────────────────────────────────────────────────────────────────────────────────────────

    static void Frame(DatabaseService db, CrossSection column, CrossSection beam)
    {
        var b = new SchemaBuilder(db, "Рама: крен (5.5), погиби, формы, пакет ±");
        var n1 = b.Node(0, 0, 0, Fixed);
        var n2 = b.Node(6, 0, 0, Fixed);
        var n3 = b.Node(0, 0, 4);
        var n4 = b.Node(6, 0, 4);
        b.Bar("К1", n1, n3, column, 0.5);
        b.Bar("К2", n2, n4, column, 0.5);
        b.Bar("Р1", n3, n4, beam, 0.5);
        var g = b.Case("G", FemSp20Types.Permanent);
        b.Load(g, n3, fz: -1300);
        b.Load(g, n4, fz: -200);
        var w = b.Case("W", FemSp20Types.ShortTerm);
        b.Load(w, n3, fx: 20);
        var imp = b.Case("Крен и погиби", FemSp20Types.Imperfection);
        var modes = b.Case("Форма рамы", FemSp20Types.Imperfection);
        var cg = b.Combination("G", "C", (g, 1));
        var cgw = b.Combination("G + W", "C", (g, 1), (w, 1));
        var cImp = b.Combination("G + W + несовершенства", "C", (g, 1), (w, 1), (imp, 1));
        var cMode = b.Combination("G + W + форма", "C", (g, 1), (w, 1), (modes, 1));
        b.Save();
        b.Imperfection(imp, FemImperfectionKinds.Sway, "Крен X", new FemSwayParams
        {
            PhiMode = FemSwayParams.PhiEn1993, M = 2, AngleDeg = 0,
        }.ToJson(), "h — размах z (4 м), m = 2 (подсказка после расчёта)");
        b.Imperfection(imp, FemImperfectionKinds.Bow, "Погиби стоек", new FemBowParams
        {
            MemberTags = ["К1", "К2"], Plane = FemBowParams.PlaneY, E0Mode = FemBowParams.E0Table, Curve = FemBowParams.CurveAuto,
            Sign = FemBowParams.SignAuto, Activity = FemBowParams.ActivityEn1993,
        }.ToJson(), "К1 (1300 кН > 0,25·N_cr = 1132 кН) — учтена по 5.3.2(6), К2 (200 кН) — нет");
        int buckling = b.Buckling("Устойчивость G", cg);
        b.Imperfection(modes, FemImperfectionKinds.Modes, "Форма 1 рамы", new FemModesParams
        {
            AnalysisId = buckling, Curve = null, Modes = [new FemModeAmplitude { Mode = 1, Amplitude = FemModeAmplitude.AmplitudeEn1993 }],
        }.ToJson(), null);
        b.Secant("С креном и погибями", cImp, physical: false, control: (n3, 0));
        b.Secant("С формой по (5.9)", cMode, physical: false, control: (n3, 0));
        b.Batch("Пакет: несовершенства ±", [cg, cgw], imp);
    }

    // ── 5. ЖБ-колонна ───────────────────────────────────────────────────────────────────────────────────

    static void RcColumn(DatabaseService db, CrossSection rc)
    {
        var b = new SchemaBuilder(db, "ЖБ-колонна: e_a СП 63 и eᵢ EN 1992");
        var n1 = b.Node(0, 0, 0, PinnedBase);
        var n2 = b.Node(0, 0, 3.6, PinnedTop);
        b.Bar("К1", n1, n2, rc, 0.45);
        var n = b.Case("N", FemSp20Types.Permanent);
        b.Load(n, n2, fz: -2500);
        var ea = b.Case("e_a СП 63", FemSp20Types.Imperfection);
        var ei = b.Case("eᵢ EN 1992", FemSp20Types.Imperfection);
        var cEa = b.Combination("N + e_a", "C", (n, 1), (ea, 1));
        var cEi = b.Combination("N + eᵢ", "C", (n, 1), (ei, 1));
        b.Save();
        b.Imperfection(ea, FemImperfectionKinds.Bow, "e_a К1", new FemBowParams
        {
            MemberTags = ["К1"], Plane = FemBowParams.PlaneY, E0Mode = FemBowParams.E0Sp63, Sign = FemBowParams.SignAuto,
            Activity = FemBowParams.ActivityAlways,
        }.ToJson(), "max(3600/600, 400/30, 10) = 13,3 мм");
        b.Imperfection(ei, FemImperfectionKinds.Bow, "eᵢ К1", new FemBowParams
        {
            MemberTags = ["К1"], Plane = FemBowParams.PlaneY, E0Mode = FemBowParams.E0En1992, Mu = 1, Sign = FemBowParams.SignAuto,
            Activity = FemBowParams.ActivityAlways,
        }.ToJson(), "θᵢ·l₀/2, l₀ = 3,6 м");
        b.Secant("Физнелин, второй порядок, e_a", cEa, physical: true, control: null);
        b.Secant("Физнелин, второй порядок, eᵢ", cEi, physical: true, control: null);
    }

    // ── 6. Стена по ярусам ───────────────────────────────────────────────────────────────────────────────

    static void WallStoreys(DatabaseService db, CrossSection column, PlateSection slab, PlateSection wall)
    {
        var b = new SchemaBuilder(db, "Стена 4×6 м с перекрытием: погибь по ярусам");
        b.Node(0, 0, 0, Fixed);
        b.Node(4, 0, 0, Fixed);
        var t1 = b.Node(0, 0, 6);
        var t2 = b.Node(4, 0, 6);
        var c1 = b.Node(0, 3, 0, Fixed);
        var c2 = b.Node(4, 3, 0, Fixed);
        var c3 = b.Node(0, 3, 3);
        var c4 = b.Node(4, 3, 3);
        b.Bar("К1", c1, c3, column, 0.5);
        b.Bar("К2", c2, c4, column, 0.5);
        var n = b.Case("N", FemSp20Types.Permanent);
        b.Load(n, t1, fz: -800);
        b.Load(n, t2, fz: -800);
        var imp = b.Case("Погибь стены", FemSp20Types.Imperfection);
        var comb = b.Combination("N + погибь", "C", (n, 1), (imp, 1));
        b.Save();
        b.Region("С1", new Frame3D(new PlanarVector3(0, 0, 0), new PlanarVector3(1, 0, 0), new PlanarVector3(0, 0, 1),
            new PlanarVector3(0, -1, 0)), 4, 6, 0.4, wall, "wall");
        b.Region("П1", new Frame3D(new PlanarVector3(0, 0, 3), new PlanarVector3(1, 0, 0), new PlanarVector3(0, 1, 0),
            new PlanarVector3(0, 0, 1)), 4, 3, 0.5, slab, "plate");
        b.Imperfection(imp, FemImperfectionKinds.WallBow, "Погибь С1", new FemWallBowParams
        {
            MemberTags = ["С1"], E0Mode = FemBowParams.E0En1992Simplified, Mu = 1, Sign = FemBowParams.SignAuto,
        }.ToJson(), "два яруса по 3 м (перекрытие на отметке 3): eᵢ = l₀/400 = 7,5 мм");
        b.Secant("Второй порядок, упруго", comb, physical: false, control: (t1, 1));
    }

    // ── построитель схемы ────────────────────────────────────────────────────────────────────────────────

    sealed class SchemaBuilder
    {
        readonly DatabaseService _db;
        readonly FemSchema _schema;
        readonly List<FemNode> _nodes = [];
        readonly List<FemMember> _members = [];
        readonly List<FemLoadCase> _cases = [];
        readonly List<FemNodeLoad> _loads = [];
        readonly List<FemLoadDefinition> _definitions = [];
        int _next = -1;

        public SchemaBuilder(DatabaseService db, string tag)
        {
            _db = db;
            _schema = new FemSchema { Tag = tag };
            db.SaveFemSchema(_schema);
        }

        public FemNode Node(double x, double y, double z, int mask = 0)
        {
            var n = new FemNode { Id = _next--, SchemaId = _schema.Id, NodeTag = (_nodes.Count + 1).ToString(), X = x, Y = y, Z = z, DofMask = mask };
            _nodes.Add(n);
            return n;
        }

        public void Bar(string tag, FemNode a, FemNode b, CrossSection section, double meshLength) => _members.Add(new FemMember
        {
            SchemaId = _schema.Id, ElemTag = tag, ElemType = "beam", NodeIdsJson = $"[{a.NodeTag},{b.NodeTag}]",
            CrossSectionId = section.Id, TargetMeshLengthM = meshLength,
        });

        public FemLoadCase Case(string tag, string type)
        {
            var c = new FemLoadCase { Id = _next--, SchemaId = _schema.Id, Tag = tag, Sp20Type = type };
            _cases.Add(c);
            return c;
        }

        /// <summary>Узловая нагрузка, кН (в базе — Н).</summary>
        public void Load(FemLoadCase c, FemNode n, double fx = 0, double fz = 0) =>
            _loads.Add(new FemNodeLoad { SchemaId = _schema.Id, LoadCaseId = c.Id, NodeId = n.Id, Fx = fx * 1e3, Fz = fz * 1e3 });   // кН → Н

        public FemLoadDefinition Combination(string tag, string type, params (FemLoadCase Case, double K)[] terms)
        {
            var d = new FemLoadDefinition { SchemaId = _schema.Id, Tag = tag, CombinationType = type };
            d.SetExpression(new FemLoadExpression
            {
                Mode = FemLoadExpressionMode.Sum, CombinationType = type,
                Terms = terms.Select(t => new FemLoadTerm { LoadCaseId = t.Case.Id, Coefficient = t.K, Mode = FemLoadTerm.ManualMode }).ToList(),
            });
            _definitions.Add(d);
            return d;
        }

        /// <summary>Сохраняет узлы, КонЭ, загружения, нагрузки и комбинации; Id загружений и узлов — из базы.</summary>
        public void Save()
        {
            var oldCaseTags = _cases.ToDictionary(c => c.Id, c => c.Tag);
            _db.SaveFemSchemaEdit(_schema.Id, _nodes, _members, [], _cases, _loads, _definitions);
            var saved = _db.GetFemLoadCases(_schema.Id).ToDictionary(c => c.Tag);
            foreach (var c in _cases) c.Id = saved[c.Tag].Id;
        }

        public void Imperfection(FemLoadCase c, string kind, string tag, string json, string? comment) =>
            _db.SaveFemImperfection(new FemImperfection
            {
                SchemaId = _schema.Id, LoadCaseId = c.Id, Kind = kind, Tag = tag, ParamsJson = json, Comment = comment,
            });

        public void Region(string tag, Frame3D frame, double width, double height, double meshSize, PlateSection section, string kind)
        {
            var region = PlanarRegion.CreateFromContour(
                new Contour { X = [0, width, width, 0], Y = [0, 0, height, height] }, frame: frame, tag: tag);
            region.MeshMaxElementSizeM = meshSize;
            _db.AddPlanarRegion(region, _schema.Id);
            _db.SaveFemMember(new FemMember
            {
                SchemaId = _schema.Id, ElemTag = tag, ElemType = "shell", NodeIdsJson = "[]", PlanarRegionId = region.Id,
                Kind = kind, KindSource = "manual", PlateSectionId = section.Id,
            });
        }

        static FemAnalysisStage Stage(FemLoadDefinition d, double step = 0.2) => new()
        {
            Tag = d.Tag, LoadExpressionJson = d.ExpressionJson, LoadFactorStep = step, MaxLoadFactor = 1,
        };

        public void Secant(string tag, FemLoadDefinition d, bool physical, (FemNode Node, int Dof)? control)
        {
            var pars = new FemAnalysisParams
            {
                CalcType = CalcType.C, Stages = [Stage(d)],
                Csfea = new FemCsfeaParams
                {
                    PhysicalNonlinearity = physical, GeomNonlinear = true, ControlNodeTag = control?.Node.NodeTag ?? "", ControlDof = control?.Dof ?? 2,
                },
            };
            _db.SaveFemAnalysis(new FemAnalysis
            {
                SchemaId = _schema.Id, Tag = tag, Kind = FemCsfeaRunner.AnalysisKind, LoadExpressionJson = d.ExpressionJson,
                ParamsJson = pars.ToJson(),
            });
        }

        public int Buckling(string tag, FemLoadDefinition d)
        {
            var pars = new FemAnalysisParams { Stages = [Stage(d, 1)], Buckling = new FemBucklingParams { Modes = 4 } };
            var a = new FemAnalysis
            {
                SchemaId = _schema.Id, Tag = tag, Kind = FemBucklingRunner.AnalysisKind, LoadExpressionJson = d.ExpressionJson,
                ParamsJson = pars.ToJson(),
            };
            _db.SaveFemAnalysis(a);
            return a.Id;
        }

        public void Batch(string tag, IReadOnlyList<FemLoadDefinition> items, FemLoadCase imperfection)
        {
            var pars = new FemAnalysisParams
            {
                CalcType = CalcType.C,
                Csfea = new FemCsfeaParams { PhysicalNonlinearity = false, GeomNonlinear = true },
                Batch = new FemCsfeaBatch
                {
                    Items = items.Select(d => new FemCsfeaBatchItem { Tag = d.Tag, LoadExpressionJson = d.ExpressionJson, Type = "C" }).ToList(),
                    Imperfections = [new FemCsfeaBatchImperfection { LoadCaseId = imperfection.Id, BothSigns = true }],
                    LoadFactorStep = 0.2, MaxLoadFactor = 1,
                },
            };
            _db.SaveFemAnalysis(new FemAnalysis
            {
                SchemaId = _schema.Id, Tag = tag, Kind = FemCsfeaRunner.AnalysisKind, LoadExpressionJson = items[0].ExpressionJson,
                ParamsJson = pars.ToJson(),
            });
        }
    }

    // ── сечения ──────────────────────────────────────────────────────────────────────────────────────────

    static CrossSection SteelSection(DatabaseService db, string tag, ParametricSteelSectionDefinition definition, Material steel)
    {
        var section = new CrossSection { Num = db.CrossSections.Count + 1, Tag = tag };
        var result = new ParametricSteelSectionProjectService(db).GenerateAndSave(section, definition with { Tag = tag, MaterialId = steel.Id });
        Assert.Empty(result.Diagnostics);
        return section;
    }

    /// <summary>Подтип сортамента «Стальные гнутые замкнутые сварные квадратные профили по ГОСТ 30245-2012».</summary>
    const int Gost30245Square = 83;

    /// <summary>Параметрическое МК-сечение по строке сортамента — как выбор профиля в диалоге сечения.</summary>
    static CrossSection CatalogSection(DatabaseService db, ProfileDB sortament, string tag, int subtype, string name, Material steel)
    {
        var item = sortament.GetSteelCatalogProfiles(subtype).Single(p => p.Name == name);
        var entry = sortament.GetSteelCatalogEntry(subtype, item.Id) ?? throw new InvalidOperationException(name);
        return SteelSection(db, tag, entry.ToDefinition(), steel);
    }

    static CrossSection RcSection(DatabaseService db, string tag, double b, double h, Material concrete, Material rebar)
    {
        var definition = ParametricRcSectionDefinition.Rectangle(b, h) with
        {
            Tag = tag, ConcreteMaterialId = concrete.Id, LongitudinalMaterialId = rebar.Id,
            UpperRebar = ParametricLongitudinalLayer.Physical(3, 0.020, h / 2 - 0.05),
            LowerRebar = ParametricLongitudinalLayer.Physical(3, 0.020, -(h / 2 - 0.05)),
        };
        var section = new CrossSection { Num = db.CrossSections.Count + 1, Tag = tag };
        var result = new ParametricRcSectionProjectService(db).GenerateAndSave(section, definition);
        Assert.Empty(result.Diagnostics);
        return section;
    }

    /// <summary>Сечение пластины h с сетками Ø12/200 у обеих граней (защитный слой до оси 35 мм).</summary>
    static PlateSection Plate(DatabaseService db, string tag, double h, Material concrete, Material rebar)
    {
        const double area = 5.65e-4;
        double z = h / 2 - 0.035;
        var section = new PlateSection
        {
            Num = db.PlateSections.Count + 1, Tag = tag, H = h, NLayers = 20,
            ConcreteMaterialId = concrete.Id, RebarMaterialId = rebar.Id,
            RebarLayers =
            [
                new PlateRebarLayer { Name = "низ", Asx = area, Asy = area, Zsx = -z, Zsy = -z, InputMode = "direct" },
                new PlateRebarLayer { Name = "верх", Asx = area, Asy = area, Zsx = z, Zsy = z, InputMode = "direct" },
            ],
        };
        db.SavePlateSection(section);
        return section;
    }

    static string CatalogDirectory()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "OpenCS.sln"))) dir = dir.Parent;
        return Path.Combine(dir!.FullName, "OpenCS.App", "DataSource");
    }
}
