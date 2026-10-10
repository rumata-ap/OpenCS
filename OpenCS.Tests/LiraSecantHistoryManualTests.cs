using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using CScore;
using CScore.Fem;
using CScore.Fem.Import;
using CScore.Import;
using CScore.PlateRebar;
using CSfea.CScoreBridge.Structural;
using Microsoft.Data.Sqlite;
using OpenCS.Services;
using OpenCS.Tasks;
using OpenCS.Utilites;
using Xunit;
using Xunit.Abstractions;

namespace OpenCS.Tests;

/// <summary>
/// Ручная проверка секущего расчёта CSfea на схеме ЛИРЫ: зависит ли результат от числа шагов нагружения (истории
/// трещин). <see cref="CreateDatabase"/> — схема, открытая в ЛИРЕ (как «Импорт схемы из ЛИРЫ (API)»), плюс то, чего
/// импорт не переносит: нагрузки и АЖТ — из текстового файла той же схемы (OPENCS_LIRA_SECANT_TXT), ТЗА и подбор —
/// OPENCS_LIRA_SECANT_RBT / _ASP; сечения пластин и стержней — как команды создания сечений; база —
/// OPENCS_LIRA_SECANT_DB. <see cref="RunVariants"/> — прогоны базы OPENCS_LIRA_SECANT_RUN по списку
/// OPENCS_LIRA_SECANT_VARIANTS («k=1,steps=1|k=1,steps=10,geom=1»; k — множитель нагрузки, steps — шагов, geom —
/// геомнелин, seq — загружения стадиями по очереди, bis — дроблений, iter — итераций на шаг) и сравнение полей
/// перемещений и трещин с первым вариантом. Журналы итераций — в OPENCS_CSFEA_OUT. Без переменных тесты выходят.
/// </summary>
public sealed class LiraSecantHistoryManualTests(ITestOutputHelper output)
{
    const double TonToN = 9806.65;

    [Fact]
    public void CreateDatabase()
    {
        var path = Environment.GetEnvironmentVariable("OPENCS_LIRA_SECANT_DB");
        var txt = Environment.GetEnvironmentVariable("OPENCS_LIRA_SECANT_TXT");
        if (string.IsNullOrWhiteSpace(path) || string.IsNullOrWhiteSpace(txt)) return;
        string text = File.ReadAllText(txt, Encoding.UTF8);

        LiraSchemaData? raw = null;
        string? title = null;
        Exception? error = null;
        var thread = new Thread(() =>
        {
            try { raw = LiraApiSchemaReader.Read(out _, out title); }
            catch (Exception ex) { error = ex; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (error != null) throw error;
        output.WriteLine($"ЛИРА «{title}»: узлов {raw!.Nodes.Count}, КЭ {raw.Elements.Count}, жёсткостей {raw.Stiffnesses.Count}, " +
                         $"КЭ с ТЗА {raw.ElementReinforcementTypes.Count}");

        if (File.Exists(path)) File.Delete(path);
        try
        {
            using var db = new DatabaseService(path);
            var schema = new FemSchema { Tag = title ?? "Схема ЛИРА-САПР (API)", SourceType = "lira" };
            db.SaveFemSchema(schema);
            // OPENCS_LIRA_SECANT_SPLIT — на сколько КЭ делить каждый стержень (P-δ в пределах колонны); 1 — как в ЛИРЕ.
            if (int.TryParse(Environment.GetEnvironmentVariable("OPENCS_LIRA_SECANT_SPLIT"), out int split) && split > 1)
                output.WriteLine($"стержни: каждый из {SplitBars(raw, split)} разделён на {split} КЭ");
            var meshNodes = LiraSchemaConverter.ToFemMeshNodes(raw, schema.Id);
            var meshElements = LiraSchemaConverter.ToFemMeshBarElements(raw, schema.Id)
                .Concat(LiraSchemaConverter.ToFemMeshShellElements(raw, schema.Id)).ToArray();
            var groups = LiraSchemaConverter.ToFemMemberGroupsByStiffness(raw, schema.Id)
                .Concat(LiraSchemaConverter.ToFemMemberGroupsByPlateStiffness(raw, schema.Id))
                .Concat(LiraSchemaConverter.ToFemMemberGroupsByReinforcementTypes(raw, schema.Id)).ToArray();
            var import = FemImportResult.MeshOnly(meshNodes, meshElements, groups);
            import.PruneMissingGroupTags();
            foreach (var w in db.SaveFemImport(schema.Id, import).Take(10)) output.WriteLine("импорт: " + w.Message);
            db.SaveFemSchemaStiffnesses(schema.Id, raw.Stiffnesses);
            if (raw.Units != null)
                db.SaveFemSchemaSourceFile(schema.Id, FemSchemaSourceFileKind.LiraUnits, "", Encoding.UTF8.GetBytes(raw.Units.ToJson()));

            // ── Закрепления: COM (таблица 4) против документа 5 текстового файла; расходятся — берётся файл ──
            var nodeTags = meshNodes.Select(n => n.NodeTag).ToHashSet(StringComparer.Ordinal);
            var fromCom = LiraSchemaConverter.ToFemMeshNodeSupports(raw).ToDictionary(s => s.NodeTag, s => s.Mask);
            var fromTxt = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (var rec in Records(text, 5))
            {
                int mask = 0;
                foreach (var dof in rec.Skip(1)) mask |= 1 << (int.Parse(dof, CultureInfo.InvariantCulture) - 1);
                fromTxt[rec[0]] = fromTxt.GetValueOrDefault(rec[0]) | (mask & FemBoundaryDofs.All);
            }
            bool same = fromCom.Count == fromTxt.Count && fromTxt.All(kv => fromCom.GetValueOrDefault(kv.Key) == kv.Value);
            output.WriteLine($"закрепления: COM {fromCom.Count}, файл {fromTxt.Count}, совпадают: {same}; маски: " +
                             string.Join(", ", fromTxt.GroupBy(kv => kv.Value).Select(g => $"{Convert.ToString(g.Key, 2)} × {g.Count()}")));
            var supports = fromTxt.Where(kv => nodeTags.Contains(kv.Key))
                .Select(kv => new FemMeshNodeSupport { NodeTag = kv.Key, Mask = kv.Value }).ToList();

            // ── АЖТ: документ 25 — ведущий узел, ведомые, −1 ──
            var bodies = new List<FemRigidBody>();
            foreach (var rec in Records(text, 25))
            {
                var body = new FemRigidBody { MasterNodeTag = rec[0], SourceElemTag = (bodies.Count + 1).ToString() };
                body.SetSlaveNodeTags(rec.Skip(1).Where(t => t != "-1"));
                bodies.Add(body);
            }
            output.WriteLine($"АЖТ: {bodies.Count}, ведомых узлов {bodies.Sum(b => b.SlaveNodeTags.Count)}");
            var props = LiraSchemaConverter.ToFemElementBoundaryProps(raw);
            db.SaveFemBoundary(schema.Id, LiraSchemaConverter.BoundaryOrigin, supports, [], bodies, props.Count > 0 ? props : null);

            // ── Нагрузки: документ 6 (номер, вид, направление, строка документа 7, загружение); т и м; «+» — против оси ──
            var values = Records(text, 7).ToDictionary(r => r[0], r => double.Parse(r[1], CultureInfo.InvariantCulture));
            var caseNames = Regex.Matches(text[..text.IndexOf(')')], @"^\s*(\d+):\s*(.+?)\s*;", RegexOptions.Multiline)
                .ToDictionary(m => int.Parse(m.Groups[1].Value), m => m.Groups[2].Value);
            var cases = new Dictionary<int, FemLoadCase>();
            FemLoadCase Case(int num)
            {
                if (!cases.TryGetValue(num, out var lc))
                    cases[num] = lc = new FemLoadCase
                    {
                        Id = -num, Tag = caseNames.GetValueOrDefault(num, $"ЗН {num}"), SourceLoadNum = num,
                        Origin = LiraSchemaConverter.BoundaryOrigin,
                    };
                return lc;
            }
            var nodeLoads = new List<FemMeshNodeLoad>();
            var uniform = new Dictionary<(int Case, int Dir, string Value), List<string>>();
            string[] axes = ["x", "y", "z"];
            foreach (var rec in Records(text, 6))
            {
                int kind = int.Parse(rec[1]), dir = int.Parse(rec[2]), lcNum = int.Parse(rec[4]);
                if (dir is < 1 or > 3) throw new NotSupportedException($"Нагрузка {string.Join(' ', rec)}: направление {dir}.");
                if (kind == 0)
                {
                    double f = -values[rec[3]] * TonToN;
                    nodeLoads.Add(new FemMeshNodeLoad
                    {
                        LoadCaseId = Case(lcNum).Id, MeshNodeTag = rec[0], Origin = LiraSchemaConverter.BoundaryOrigin,
                        Fx = dir == 1 ? f : 0, Fy = dir == 2 ? f : 0, Fz = dir == 3 ? f : 0,
                    });
                }
                else if (kind == 16)
                {
                    Case(lcNum);
                    if (!uniform.TryGetValue((lcNum, dir, rec[3]), out var list)) uniform[(lcNum, dir, rec[3])] = list = [];
                    list.Add(rec[0]);
                }
                else throw new NotSupportedException($"Нагрузка {string.Join(' ', rec)}: вид {kind} не разобран.");
            }
            var elementLoads = new List<FemElementLoad>();
            foreach (var ((lcNum, dir, value), tags) in uniform)
            {
                var load = new FemElementLoad
                {
                    LoadCaseId = Case(lcNum).Id, TargetKind = FemLoadTargetKinds.Elements, LoadKind = FemElementLoadKinds.Uniform,
                    Axis = axes[dir - 1], Origin = LiraSchemaConverter.BoundaryOrigin,
                };
                load.SetTargetTags(tags);
                load.SetValues([-values[value] * TonToN]);
                elementLoads.Add(load);
                output.WriteLine($"ЗН {lcNum}: равномерная {axes[dir - 1]} {-values[value] * TonToN / 1e3:0.###} кПа на {tags.Count} КЭ");
            }
            // OPENCS_LIRA_SECANT_H — горизонтальная сила +X, кН, в узле каждой колонны на уровне плиты (ведущие узлы АЖТ) —
            // отдельным загружением; в схеме ЛИРЫ боковых сил нет.
            if (double.TryParse(Environment.GetEnvironmentVariable("OPENCS_LIRA_SECANT_H"), NumberStyles.Float, CultureInfo.InvariantCulture, out double hKn) && hKn != 0)
            {
                int hNum = cases.Keys.Max() + 1;
                caseNames[hNum] = "ГОРИЗОНТАЛЬНАЯ НАГРУЗКА";
                foreach (var body in bodies)
                    nodeLoads.Add(new FemMeshNodeLoad
                    {
                        LoadCaseId = Case(hNum).Id, MeshNodeTag = body.MasterNodeTag, Origin = LiraSchemaConverter.BoundaryOrigin, Fx = hKn * 1e3,
                    });
            }
            foreach (var g in nodeLoads.GroupBy(l => l.LoadCaseId))
                output.WriteLine($"ЗН {-g.Key}: узловых сил {g.Count()}, ΣFz = {g.Sum(l => l.Fz) / 1e3:0.###} кН, ΣFx = {g.Sum(l => l.Fx) / 1e3:0.###} кН");
            var caseList = cases.OrderBy(kv => kv.Key).Select(kv => kv.Value).ToList();
            var tempIds = caseList.ToDictionary(c => c.Id, c => c);
            db.SaveFemLoadCasesAndMeshLoads(schema.Id, caseList, [], []);
            foreach (var l in elementLoads) l.LoadCaseId = tempIds[l.LoadCaseId].Id;
            foreach (var l in nodeLoads) l.LoadCaseId = tempIds[l.LoadCaseId].Id;
            db.SaveFemLoadCasesAndMeshLoads(schema.Id, caseList, elementLoads, nodeLoads);

            // ── Армирование и сечения ──
            if (Environment.GetEnvironmentVariable("OPENCS_LIRA_SECANT_RBT") is { Length: > 0 } rbt)
                db.SaveFemSchemaReinforcementFile(schema.Id, Path.GetFileName(rbt), File.ReadAllBytes(rbt));
            if (Environment.GetEnvironmentVariable("OPENCS_LIRA_SECANT_ASP") is { Length: > 0 } asp)
                db.SaveFemSchemaSelectedReinforcementFile(schema.Id, Path.GetFileName(asp), File.ReadAllBytes(asp));
            db.LoadAll();
            schema = db.FemSchemas.Single(x => x.Id == schema.Id);
            var data = FemCheckSchemaData.Load(db, schema.Id);
            foreach (var e in data.Errors) output.WriteLine("файлы армирования: " + e);
            output.WriteLine($"RBT: пластин {data.Rbt?.PlateTypes.Count}, стержней {data.Rbt?.BarTypes.Count}; ASP: {(data.Asp != null ? "есть" : "нет")}");

            var plates = LiraPlateSectionCreator.Create(db, data, schema.MemberGroups, n => n, CatalogDirectory());
            output.WriteLine($"пластины: нет ASP {plates.NoAsp}, материалы [{string.Join(", ", plates.Materials)}], сечения [{string.Join(", ", plates.Sections)}]");
            foreach (var (target, section) in plates.Assigned) output.WriteLine($"  «{target}» → «{section}»");
            foreach (var (target, reason) in plates.Skipped) output.WriteLine($"  пропущено «{target}»: {reason}");
            foreach (var (target, faces) in plates.Nominal) output.WriteLine($"  условная арматура «{target}»: {faces}");
            foreach (var s in db.PlateSections)
                output.WriteLine($"  сечение «{s.Tag}»: h = {s.H}, слоёв {s.NLayers}, арматура: " + string.Join("; ",
                    s.RebarLayers.Select(l => $"{l.Name} Asx {l.Asx * 1e4:0.###} Asy {l.Asy * 1e4:0.###} см²/м, z {l.Zsx:0.####}/{l.Zsy:0.####}")));

            var bars = ImportedBarSectionCreator.Create(db, FemCheckSchemaData.Load(db, schema.Id), catalogDirectory: CatalogDirectory(),
                chooseRebar: modes => new ImportedBarRebarChoice(modes.Contains(ImportedBarRebarMode.Assigned) ? ImportedBarRebarMode.Assigned
                    : modes.Contains(ImportedBarRebarMode.Selected) ? ImportedBarRebarMode.Selected : ImportedBarRebarMode.None));
            output.WriteLine($"стержни: нет данных {bars.NoMaterialData}, армирование {bars.RebarMode}, материалы [{string.Join(", ", bars.Materials)}], " +
                             $"назначено {bars.AssignedElements}");
            foreach (var (section, count) in bars.Assigned) output.WriteLine($"  «{section}»: КЭ {count}");
            foreach (var (reason, elements) in bars.Skipped) output.WriteLine($"  пропущено {elements.Count}: {reason}");
            foreach (var (reason, elements) in bars.WithoutRebar) output.WriteLine($"  без арматуры {elements.Count}: {reason}");
            if (plates.NoAsp || bars.NoMaterialData) ManualSections(db, schema, data);
            foreach (var m in db.Materials) output.WriteLine($"  материал «{m.Tag}» ({m.Type}), E = {m.E}");
            output.WriteLine($"База: {path}");
        }
        finally { SqliteConnection.ClearAllPools(); }
    }

    /// <summary>
    /// Сечения без файла подбора (ASP не читается): классы OPENCS_LIRA_SECANT_CONCRETE / _REBAR (по умолчанию B25 и
    /// A500 — как в ASP схемы «Плита_упругое»). Пластины — шаблон на группу жёсткости, слои арматуры даёт ТЗА (.RBT);
    /// стержни — «Брус» по жёсткости с OPENCS_LIRA_SECANT_BARS стержнями ⌀16 по периметру на привязке 4 см (по
    /// умолчанию 8 — ТЗА «AU AS 8d16 c4.0/4.0»).
    /// </summary>
    void ManualSections(DatabaseService db, FemSchema schema, FemCheckSchemaData data)
    {
        string concreteClass = Environment.GetEnvironmentVariable("OPENCS_LIRA_SECANT_CONCRETE") ?? "B25";
        string rebarClass = Environment.GetEnvironmentVariable("OPENCS_LIRA_SECANT_REBAR") ?? "A500";
        var concrete = MaterialCatalog.CreateHeavyConcrete(concreteClass, CatalogDirectory())
                       ?? throw new InvalidOperationException($"Класса бетона {concreteClass} нет в справочнике.");
        var rebar = MaterialCatalog.CreateRebar(rebarClass, CatalogDirectory()) ?? MaterialCatalog.CreateRebar("А500С", CatalogDirectory())
                    ?? throw new InvalidOperationException($"Класса арматуры {rebarClass} нет в справочнике.");
        db.AddMaterial(concrete);
        db.AddMaterial(rebar);

        var shells = data.Mesh.Where(e => e.ElemType == "shell").ToDictionary(e => e.ElemTag, StringComparer.Ordinal);
        foreach (var group in schema.MemberGroups.Where(g => g.PlateSectionId == null))
        {
            var tags = group.Tags.Where(shells.ContainsKey).ToList();
            if (tags.Count == 0) continue;
            double h = shells[tags[0]].ThicknessM ?? (data.Stiffnesses.GetValueOrDefault(shells[tags[0]].StiffnessNum ?? 0) is { } st ? LiraStiffnessParams.PlateThicknessM(st) : null)
                       ?? throw new InvalidOperationException($"Группа «{group.Tag}»: толщина пластин неизвестна.");
            var section = db.PlateSections.FirstOrDefault(s => Math.Abs(s.H - h) < 1e-9);
            if (section == null)
            {
                section = new PlateSection
                {
                    Num = db.PlateSections.Count + 1, Tag = $"Плита {h * 1000:0} {concreteClass} {rebarClass}", H = h,
                    NLayers = ShellLayeredCheck.RefinedLayers, ConcreteMaterialId = concrete.Id, RebarMaterialId = rebar.Id,
                    TensionConcrete = true, RebarLayers = PlateLayers(h),
                };
                db.SavePlateSection(section);
            }
            group.PlateSectionId = section.Id;
            db.SaveFemMemberGroup(group);
            output.WriteLine($"  вручную: группа «{group.Tag}» ({tags.Count} пластин, h = {h}) → «{section.Tag}», слоёв {section.NLayers}");
        }

        // OPENCS_LIRA_SECANT_PLATE_REBAR = «диаметр,шаг,привязка» в мм (по умолчанию 12,200,30) — сетки у обеих граней в обе
        // стороны, источник армирования «Сечение»; «rbt» — слоёв нет, армирование по ТЗА («Заданное»).
        List<PlateRebarLayer> PlateLayers(double h)
        {
            string spec = Environment.GetEnvironmentVariable("OPENCS_LIRA_SECANT_PLATE_REBAR") ?? "12,200,30";
            if (spec == "rbt") return [];
            var p = spec.Split(',').Select(s => double.Parse(s, CultureInfo.InvariantCulture) / 1000).ToArray();
            double As = Math.PI * p[0] * p[0] / 4 / p[1], z = h / 2 - p[2];
            return
            [
                new() { Name = "низ", InputMode = "direct", Asx = As, Asy = As, Zsx = -z, Zsy = -z, Face = RebarFace.MinusN },
                new() { Name = "верх", InputMode = "direct", Asx = As, Asy = As, Zsx = z, Zsy = z, Face = RebarFace.PlusN },
            ];
        }

        int nBars = int.TryParse(Environment.GetEnvironmentVariable("OPENCS_LIRA_SECANT_BARS"), out int nb) ? nb : 8;
        var service = new ParametricRcSectionProjectService(db);
        var byStiffness = new Dictionary<int, CrossSection>();
        var assignments = new List<(FemElement, int?)>();
        foreach (var e in data.Mesh.Where(e => e.ElemType == "beam" && e.CrossSectionId == null))
        {
            int num = e.StiffnessNum ?? throw new InvalidOperationException($"Стержень {e.ElemTag}: нет номера жёсткости.");
            if (!byStiffness.TryGetValue(num, out var section))
            {
                var (profile, reason) = ImportedBarProfiles.Resolve(data.Stiffnesses, num, scad: false);
                if (profile == null) throw new InvalidOperationException($"Стержень {e.ElemTag}: {reason}");
                const double d = 0.016, a = 0.04;
                double x = profile.WidthM / 2 - a, y = profile.HeightM / 2 - a, area = Math.PI * d * d / 4;
                (double X, double Y)[] at = nBars == 8
                    ? [(-x, -y), (0, -y), (x, -y), (x, 0), (x, y), (0, y), (-x, y), (-x, 0)]
                    : [(-x, -y), (x, -y), (x, y), (-x, y)];
                var layout = new ImportedBarRebarLayout([.. at.Select(p => new LiraBarPoint(p.X, p.Y, area, d))], $"{at.Length}d16");
                var (definition, why) = RcSectionBuilder.Build(profile, concrete.Id, rebar.Id, $"{concreteClass} {rebarClass}", layout);
                if (definition == null) throw new InvalidOperationException($"Стержень {e.ElemTag}: {why}");
                section = new CrossSection { Num = db.CrossSections.Count + 1, Tag = definition.Tag };
                var result = service.GenerateAndSave(section, definition);
                if (result.Diagnostics.Count != 0) throw new InvalidOperationException(string.Join("; ", result.Diagnostics));
                byStiffness[num] = section;
                output.WriteLine($"  вручную: жёсткость {num} «{profile.SourceLabel}» → «{section.Tag}»");
            }
            assignments.Add((e, section.Id));
        }
        db.SetFemElementCrossSections(assignments);
        output.WriteLine($"  вручную: стержней с сечением {assignments.Count}");
    }

    /// <summary>Вариант прогона: множитель нагрузки, шаги, геомнелин, загружения по очереди, дробления, итерации.</summary>
    sealed record Variant(string Name, double K, int Steps, bool Geom, bool Sequential, int Bisections, int Iterations)
    {
        public static Variant Parse(string spec)
        {
            var map = spec.Split(',', StringSplitOptions.RemoveEmptyEntries).Select(p => p.Split('='))
                .ToDictionary(p => p[0].Trim(), p => p.Length > 1 ? p[1].Trim() : "1");
            double D(string key, double def) => map.TryGetValue(key, out var s) ? double.Parse(s, CultureInfo.InvariantCulture) : def;
            return new Variant(spec, D("k", 1), (int)D("steps", 1), D("geom", 0) != 0, D("seq", 0) != 0, (int)D("bis", 4), (int)D("iter", 50));
        }
    }

    sealed record Outcome(Variant Variant, bool Completed, double Lambda, int Iterations, double Seconds,
        Dictionary<int, double[]> U, Dictionary<int, int> ShellCracks, int Cracked, int Yielded, int Failed, int BeamsCracked,
        int BeamsYielded, List<(double Lambda, double Uz)> History);

    [Fact]
    public async Task RunVariants()
    {
        var source = Environment.GetEnvironmentVariable("OPENCS_LIRA_SECANT_RUN");
        if (string.IsNullOrWhiteSpace(source)) return;
        var variants = (Environment.GetEnvironmentVariable("OPENCS_LIRA_SECANT_VARIANTS") ?? "k=1,steps=1|k=1,steps=10")
            .Split('|', StringSplitOptions.RemoveEmptyEntries).Select(Variant.Parse).ToList();
        string? outDir = Environment.GetEnvironmentVariable("OPENCS_CSFEA_OUT");
        if (outDir != null) Directory.CreateDirectory(outDir);
        var root = Path.Combine(Path.GetTempPath(), $"opencs-lira-secant-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        var path = Path.Combine(root, "schema.db");
        File.Copy(source, path);
        try
        {
            using var db = new DatabaseService(path);
            db.LoadAll();
            var schema = db.FemSchemas.First();
            var cases = db.GetFemLoadCases(schema.Id).OrderBy(c => c.SourceLoadNum).ToList();
            var gmsh = new GmshSettings { ExecutablePath = @"C:\Tools\gmsh-4.15.2-Windows64\gmsh.exe", ArtifactsPath = Path.Combine(root, "gmsh") };
            var ctx = new FemCsfeaRunContext(db, gmsh, new OutputLog(output));

            // Контрольные точки плиты: центры средней, угловой и крайней ячеек, середина пролёта по оси колонн.
            (string Name, double X, double Y)[] points = [("центр", 9, 9), ("угл. ячейка", 3, 3), ("крайняя ячейка", 9, 3), ("ось колонн", 9, 6),
                ("колонна ср.", 6, 6), ("колонна угл.", 0, 0), ("край, пролёт", 0, 3)];
            var meshNodes = db.GetFemMeshNodes(schema.Id);
            double zSlab = meshNodes.GroupBy(n => Math.Round(n.Z, 3)).MaxBy(g => g.Count())!.Key;
            var control = points.Select(p => (p.Name, Node: int.Parse(meshNodes.Where(n => Math.Abs(n.Z - zSlab) < 1e-6)
                .MinBy(n => Math.Pow(n.X - p.X, 2) + Math.Pow(n.Y - p.Y, 2))!.NodeTag))).ToList();

            var outcomes = new List<Outcome>();
            foreach (var v in variants)
            {
                string Expr(params (FemLoadCase Case, double K)[] terms) => new FemLoadExpression
                {
                    Mode = FemLoadExpressionMode.Sum,
                    Terms = [.. terms.Select(t => new FemLoadTerm { LoadCaseId = t.Case.Id, Coefficient = t.K })],
                }.ToJson();
                var stages = v.Sequential
                    ? cases.Select(c => new FemAnalysisStage { Tag = c.Tag, LoadExpressionJson = Expr((c, v.K)), LoadFactorStep = 1.0 / v.Steps, MaxLoadFactor = 1 }).ToList()
                    : [new FemAnalysisStage { Tag = "все ЗН", LoadExpressionJson = Expr([.. cases.Select(c => (c, v.K))]), LoadFactorStep = 1.0 / v.Steps, MaxLoadFactor = 1 }];
                var csfea = new FemCsfeaParams
                {
                    PlateRebarSource = db.PlateSections.Any(s => s.RebarLayers.Count > 0) ? FemCheckRebarSource.Section : FemCheckRebarSource.Assigned, GeomNonlinear = v.Geom, MaxBisections = v.Bisections, MaxIterations = v.Iterations,
                };
                var analysis = new FemAnalysis
                {
                    SchemaId = schema.Id, Tag = v.Name, Kind = FemCsfeaRunner.AnalysisKind, LoadExpressionJson = stages[0].LoadExpressionJson,
                    ParamsJson = new FemAnalysisParams { CalcType = CalcType.N, Stages = stages, Csfea = csfea }.ToJson(),
                };
                var prepared = await FemCsfeaRunner.PrepareAsync(ctx, schema, analysis, buildMesh: false, CancellationToken.None);
                output.WriteLine($"=== {v.Name}: проверка входа — ошибки {prepared.HasErrors}");
                if (outcomes.Count == 0 || prepared.HasErrors)
                    foreach (var line in prepared.Describe().Concat(prepared.Report).Take(40)) output.WriteLine("  " + line);
                if (prepared.HasErrors) continue;
                if (outcomes.Count == 0 && prepared.Input!.PlateSection is { } plateOf
                    && plateOf(prepared.Input.MeshElements.First(e => e.ElemType == "shell")) is { } first)
                    output.WriteLine($"  пластина: {System.Text.Json.JsonSerializer.Serialize(first.Section.Section)}");

                using var log = outDir == null ? null : new StreamWriter(Path.Combine(outDir, Regex.Replace(v.Name, @"[^\w=.,-]", "_") + ".txt"));
                var clock = Stopwatch.StartNew();
                if (log != null) log.AutoFlush = true;
                var options = FemCsfeaSetup.SecantOptions(csfea, -1, s => log?.WriteLine($"[{clock.Elapsed.TotalSeconds,7:0.0}] {s}"),
                    prepared.Adapted!.ShellForceAngles);
                var run = RcSecantAnalysis.Run(prepared.Adapted.Model, options, null, CancellationToken.None);
                clock.Stop();
                log?.Flush();

                var r = run.Result;
                var last = r.Steps.LastOrDefault(s => s.Converged);
                if (last == null) { output.WriteLine($"  ни один шаг не сошёлся: {r.Message}"); continue; }
                int stageCount = stages.Count;
                double lambda = v.K * (last.Stage + last.LoadFactor) / stageCount;
                var u = new Dictionary<int, double[]>();
                for (int i = 0; i < run.Build.NodeIds.Length; i++) u[run.Build.NodeIds[i]] = last.U[(6 * i)..(6 * i + 6)];
                var cracks = new Dictionary<int, int>();
                for (int e = 0; e < run.Build.ShellIds.Length; e++) cracks[run.Build.ShellIds[e]] = last.Shells[e].Cracks;
                var history = r.Steps.Where(s => s.Converged)
                    .Select(s => (v.K * (s.Stage + s.LoadFactor) / stageCount, s.U[run.Build.Dof(control[0].Node, 2)] * 1e3)).ToList();
                // Состояние по шагам: наибольшее горизонтальное смещение, текучесть и отказ сечений (оболочки + стержни).
                output.WriteLine("  λ: гориз., мм / текучесть об.+ст. / отказ об.+ст.: " + string.Join("; ", r.Steps.Where(s => s.Converged).Select(s =>
                {
                    double h = 0;
                    for (int i = 0; i < s.U.Length / 6; i++) h = Math.Max(h, Math.Sqrt(s.U[6 * i] * s.U[6 * i] + s.U[6 * i + 1] * s.U[6 * i + 1]));
                    return $"{v.K * (s.Stage + s.LoadFactor) / stageCount:0.####}: {h * 1e3:0.00} / {s.Shells.Count(x => x.Yielded)}+{s.Beams.Count(x => x.Yielded)} / {s.Shells.Count(x => x.Failed)}+{s.Beams.Count(x => x.Failed)}";
                })));
                var o = new Outcome(v, r.Completed, lambda, r.Steps.Sum(s => s.Iterations), clock.Elapsed.TotalSeconds, u, cracks,
                    last.Shells.Count(s => s.Cracked), last.Shells.Count(s => s.Yielded), last.Shells.Count(s => s.Failed),
                    last.Beams.Count(s => s.Cracked), last.Beams.Count(s => s.Yielded), history);
                outcomes.Add(o);
                // OPENCS_LIRA_SECANT_EXPORT — каталог: состояние последнего принятого шага в CSV (узлы с перемещениями, КЭ с
                // признаками трещин, текучести и отказа) — для иллюстраций.
                if (Environment.GetEnvironmentVariable("OPENCS_LIRA_SECANT_EXPORT") is { Length: > 0 } exportDir)
                {
                    Directory.CreateDirectory(exportDir);
                    string stem = Path.Combine(exportDir, Regex.Replace(v.Name, @"[^\w=.,-]", "_"));
                    var inv = CultureInfo.InvariantCulture;
                    var xyz = meshNodes.Where(n => int.TryParse(n.NodeTag, out _)).ToDictionary(n => int.Parse(n.NodeTag), n => (n.X, n.Y, n.Z));
                    File.WriteAllLines(stem + ".nodes.csv", new[] { "id,x,y,z,ux,uy,uz" }.Concat(u.Where(kv => xyz.ContainsKey(kv.Key)).Select(kv =>
                        string.Create(inv, $"{kv.Key},{xyz[kv.Key].X},{xyz[kv.Key].Y},{xyz[kv.Key].Z},{kv.Value[0]},{kv.Value[1]},{kv.Value[2]}"))));
                    var mesh = run.Build.Mesh;
                    File.WriteAllLines(stem + ".shells.csv", new[] { "id,nodes,cracks,cracked,yielded,failed" }.Concat(Enumerable.Range(0, mesh.Shells.Count).Select(e =>
                        string.Create(inv, $"{run.Build.ShellIds[e]},{string.Join(' ', mesh.Shells[e].Nodes.Select(n => run.Build.NodeIds[n]))},{last.Shells[e].Cracks},{(last.Shells[e].Cracked ? 1 : 0)},{(last.Shells[e].Yielded ? 1 : 0)},{(last.Shells[e].Failed ? 1 : 0)}"))));
                    File.WriteAllLines(stem + ".beams.csv", new[] { "id,i,j,cracked,yielded,failed" }.Concat(Enumerable.Range(0, mesh.Beams.Count).Select(e =>
                        string.Create(inv, $"{run.Build.BeamIds[e]},{run.Build.NodeIds[mesh.Beams[e].I]},{run.Build.NodeIds[mesh.Beams[e].J]},{(last.Beams[e].Cracked ? 1 : 0)},{(last.Beams[e].Yielded ? 1 : 0)},{(last.Beams[e].Failed ? 1 : 0)}"))));
                }

                output.WriteLine($"  {(r.Completed ? "пройдено" : "ОСТАНОВЛЕНО: " + r.Message)}; λ = {lambda:0.####} (доля полной нагрузки × k), " +
                                 $"шагов {r.Steps.Count(s => s.Converged)}, итераций {o.Iterations}, {o.Seconds:0} с, невязка {last.TrueResidual:e2}");
                output.WriteLine("  uz, мм: " + string.Join("; ", control.Select(c => $"{c.Name} (узел {c.Node}) {u[c.Node][2] * 1e3:0.000}")) +
                                 $"; max|uz| {u.Values.Max(x => Math.Abs(x[2])) * 1e3:0.000}; max|uxy| {u.Values.Max(x => Math.Sqrt(x[0] * x[0] + x[1] * x[1])) * 1e3:0.000}");
                output.WriteLine($"  оболочки: с трещинами {o.Cracked}, текучесть {o.Yielded}, отказ {o.Failed}, слоёв с трещинами {cracks.Values.Sum()}; " +
                                 $"стержни: с трещинами {o.BeamsCracked}, текучесть {o.BeamsYielded}");
                if (history.Count > 1)
                    output.WriteLine("  λ → uz центра, мм: " + string.Join("; ", history.Select(h => $"{h.Item1:0.###} → {h.Item2:0.00}")));
            }

            // ── Сравнение с первым вариантом той же достигнутой нагрузки ──
            if (outcomes.Count < 2) return;
            var a = outcomes[0];
            output.WriteLine($"=== Сравнение с «{a.Variant.Name}» (λ = {a.Lambda:0.####})");
            foreach (var b in outcomes.Skip(1))
            {
                if (Math.Abs(b.Lambda - a.Lambda) > 1e-9)
                {
                    output.WriteLine($"  «{b.Variant.Name}»: λ = {b.Lambda:0.####} — нагрузка другая, поля не сравниваются");
                    continue;
                }
                double maxU = a.U.Values.Max(x => Math.Sqrt(x[0] * x[0] + x[1] * x[1] + x[2] * x[2]));
                double maxD = 0, sumD2 = 0, sumU2 = 0;
                int worst = 0;
                foreach (var (id, ua) in a.U)
                {
                    var ub = b.U[id];
                    double d = Math.Sqrt(Math.Pow(ua[0] - ub[0], 2) + Math.Pow(ua[1] - ub[1], 2) + Math.Pow(ua[2] - ub[2], 2));
                    if (d > maxD) { maxD = d; worst = id; }
                    sumD2 += d * d;
                    sumU2 += ua[0] * ua[0] + ua[1] * ua[1] + ua[2] * ua[2];
                }
                int differ = a.ShellCracks.Count(kv => (kv.Value > 0) != (b.ShellCracks[kv.Key] > 0));
                int layers = a.ShellCracks.Sum(kv => Math.Abs(kv.Value - b.ShellCracks[kv.Key]));
                output.WriteLine($"  «{b.Variant.Name}»: max|Δu| = {maxD * 1e3:0.0000} мм ({maxD / maxU:P3} от max|u| = {maxU * 1e3:0.000} мм, узел {worst}), " +
                                 $"‖Δu‖/‖u‖ = {Math.Sqrt(sumD2 / sumU2):P3}; оболочек с разным признаком трещины {differ} из {a.ShellCracks.Count}, " +
                                 $"разница числа треснувших слоёв {layers} (всего {a.ShellCracks.Values.Sum()} / {b.ShellCracks.Values.Sum()})");
            }
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            try { Directory.Delete(root, true); } catch (IOException) { }
        }
    }

    /// <summary>
    /// Делит каждый стержень на <paramref name="parts"/> равных КЭ: первый сохраняет номер, остальные и промежуточные
    /// узлы получают номера после наибольших; жёсткость и ТЗА — как у исходного. Возвращает число разделённых стержней.
    /// </summary>
    static int SplitBars(LiraSchemaData raw, int parts)
    {
        var nodes = raw.Nodes.ToDictionary(n => n.Id);
        int nextNode = raw.Nodes.Max(n => n.Id) + 1, nextElem = raw.Elements.Max(e => e.Id) + 1, count = 0;
        foreach (var e in raw.Elements.Where(e => e.NodeIds.Length == 2).ToList())
        {
            var (a, b) = (nodes[e.NodeIds[0]], nodes[e.NodeIds[1]]);
            int prev = a.Id;
            for (int i = 1; i <= parts; i++)
            {
                int next = b.Id;
                if (i < parts)
                {
                    double s = (double)i / parts;
                    next = nextNode++;
                    raw.Nodes.Add(new LiraNodeRecord(next, a.X + s * (b.X - a.X), a.Y + s * (b.Y - a.Y), a.Z + s * (b.Z - a.Z), 0));
                }
                if (i == 1) raw.Elements[raw.Elements.IndexOf(e)] = e with { NodeIds = [prev, next] };
                else
                {
                    int id = nextElem++;
                    raw.Elements.Add(e with { Id = id, NodeIds = [prev, next] });
                    if (raw.ElementReinforcementTypes.TryGetValue(e.Id, out var tza)) raw.ElementReinforcementTypes[id] = tza;
                }
                prev = next;
            }
            count++;
        }
        return count;
    }

    /// <summary>Записи документа <paramref name="number"/> текстового файла ЛИРЫ: «( N/ запись / запись / )» — поля по пробелам.</summary>
    static List<string[]> Records(string text, int number)
    {
        var m = Regex.Match(text, $@"\(\s*{number}\s*/(.*?)\n\s*\)", RegexOptions.Singleline);
        if (!m.Success) return [];
        return m.Groups[1].Value.Split('/').Select(r => r.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))
            .Where(r => r.Length > 0).ToList();
    }

    sealed class OutputLog(ITestOutputHelper output) : ILogService
    {
        public System.Collections.ObjectModel.ObservableCollection<LogEntry> LogEntries { get; } = [];
        public void Info(string message) => output.WriteLine("журнал: " + message);
        public void Warning(string message) => output.WriteLine("журнал (!): " + message);
        public void Error(string message) => output.WriteLine("журнал (ошибка): " + message);
    }

    static string CatalogDirectory()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "OpenCS.sln"))) dir = dir.Parent;
        return Path.Combine(dir!.FullName, "OpenCS", "DataSource");
    }
}
