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
/// геомнелин, seq — загружения стадиями по очереди, bis — дроблений, iter — итераций на шаг, calc — вид расчёта,
/// tens — растяжение бетона до трещины, slab — ЗН 1 постоянно, растёт только ЗН 2) и сравнение полей перемещений и трещин с первым вариантом.
/// <see cref="CreateQuarterDatabase"/> — база с четвертью схемы и условиями симметрии из базы полной схемы.
/// OPENCS_LIRA_SECANT_MOMENT_CUTS («опора=6;пролёт=3») — по шагам средние по ширине Mx в сечениях плиты x = const и
/// отношение первого к остальным. Журналы итераций — в OPENCS_CSFEA_OUT. Без переменных тесты выходят.
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
                // OPENCS_LIRA_SECANT_BAR_D / _BAR_A — диаметр стержней и привязка их центров, мм (по умолчанию 16 и 40).
                double d = (double.TryParse(Environment.GetEnvironmentVariable("OPENCS_LIRA_SECANT_BAR_D"), NumberStyles.Float, CultureInfo.InvariantCulture, out double dMm) ? dMm : 16) / 1000;
                double a = (double.TryParse(Environment.GetEnvironmentVariable("OPENCS_LIRA_SECANT_BAR_A"), NumberStyles.Float, CultureInfo.InvariantCulture, out double aMm) ? aMm : 40) / 1000;
                double x = profile.WidthM / 2 - a, y = profile.HeightM / 2 - a, area = Math.PI * d * d / 4;
                (double X, double Y)[] at = nBars == 8
                    ? [(-x, -y), (0, -y), (x, -y), (x, 0), (x, y), (0, y), (-x, y), (-x, 0)]
                    : [(-x, -y), (x, -y), (x, y), (-x, y)];
                var layout = new ImportedBarRebarLayout([.. at.Select(p => new LiraBarPoint(p.X, p.Y, area, d))], $"{at.Length}d{d * 1000:0}");
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

    /// <summary>
    /// Вариант прогона: множитель нагрузки, шаги, геомнелин, загружения по очереди, дробления, итерации, вид расчёта
    /// (calc=C|CL|N|NL), slab — растёт только ЗН 2: стадия 1 — ЗН 1 одним шагом, стадия 2 — ЗН 2 × k за steps шагов.
    /// </summary>
    sealed record Variant(string Name, double K, int Steps, bool Geom, bool Sequential, int Bisections, int Iterations,
        CalcType Calc, bool Slab)
    {
        /// <summary>Растяжение бетона до трещины (tens=0|1): по умолчанию C и CL — нет, N и NL — как в сечении (null).</summary>
        public bool? Tension { get; init; }

        /// <summary>
        /// Пропорциональное нагружение (col=доля): одна стадия — ЗН 2 × k и ЗН 1 × k·доля (сила на колонны растёт вместе с
        /// нагрузкой на плиту); λ — множитель ЗН 2. null — не задано.
        /// </summary>
        public double? ColumnShare { get; init; }

        public static Variant Parse(string spec)
        {
            var map = spec.Split(',', StringSplitOptions.RemoveEmptyEntries).Select(p => p.Split('='))
                .ToDictionary(p => p[0].Trim(), p => p.Length > 1 ? p[1].Trim() : "1");
            double D(string key, double def) => map.TryGetValue(key, out var s) ? double.Parse(s, CultureInfo.InvariantCulture) : def;
            var calc = map.TryGetValue("calc", out var c) ? Enum.Parse<CalcType>(c, ignoreCase: true) : CalcType.N;
            return new Variant(spec, D("k", 1), (int)D("steps", 1), D("geom", 0) != 0, D("seq", 0) != 0, (int)D("bis", 4), (int)D("iter", 50),
                calc, D("slab", 0) != 0)
            {
                Tension = map.ContainsKey("tens") ? D("tens", 1) != 0 : calc is CalcType.C or CalcType.CL ? false : null,
                ColumnShare = map.ContainsKey("col") ? D("col", 1) : null,
            };
        }
    }

    /// <summary>
    /// Четверть схемы «Плита_упругое» в копии базы: остаются узлы x ≤ <paramref name="half"/>, y ≤ <paramref name="half"/>
    /// и КЭ на них; на плоскостях симметрии x = half — ux, ry, rz, y = half — uy, rx, rz. Нагрузки: в ЗН 1 остаются
    /// узловые силы на колонны, нагрузка на плиту из ЗН 1 переходит в ЗН 2 (плита целиком в одном загружении), прочие
    /// загружения (боковая сила) пустеют. Id узлов и КЭ сохраняются — строки удаляются, а не перезаписываются.
    /// </summary>
    static string CutQuarter(string path, double half, bool elasticBars, bool elasticPlates)
    {
        using var c = new SqliteConnection($"Data Source={path}");
        c.Open();
        using var tx = c.BeginTransaction();
        List<object[]> Rows(string sql)
        {
            using var cmd = c.CreateCommand();
            cmd.CommandText = sql;
            var rows = new List<object[]>();
            using var r = cmd.ExecuteReader();
            while (r.Read()) { var row = new object[r.FieldCount]; r.GetValues(row); rows.Add(row); }
            return rows;
        }
        int Exec(string sql, params object[] args)
        {
            using var cmd = c.CreateCommand();
            cmd.CommandText = sql;
            for (int i = 0; i < args.Length; i++) cmd.Parameters.AddWithValue("@p" + i, args[i]);
            return cmd.ExecuteNonQuery();
        }
        long sid = (long)Rows("SELECT id FROM fem_schemas ORDER BY id LIMIT 1")[0][0];
        const double eps = 1e-6;
        var keep = new HashSet<string>(StringComparer.Ordinal);
        var symmetry = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var n in Rows($"SELECT node_tag, x, y FROM fem_mesh_nodes WHERE schema_id={sid}"))
        {
            string tag = (string)n[0];
            double x = Convert.ToDouble(n[1]), y = Convert.ToDouble(n[2]);
            if (x > half + eps || y > half + eps) { Exec("DELETE FROM fem_mesh_nodes WHERE schema_id=@p0 AND node_tag=@p1", sid, tag); continue; }
            keep.Add(tag);
            int mask = (Math.Abs(x - half) < eps ? 0b110001 : 0) | (Math.Abs(y - half) < eps ? 0b101010 : 0);
            if (mask != 0) symmetry[tag] = mask;
        }
        var elements = new HashSet<string>(StringComparer.Ordinal);
        foreach (var e in Rows($"SELECT id, elem_tag, node_ids_json FROM fem_elements WHERE schema_id={sid}"))
            if (System.Text.Json.JsonSerializer.Deserialize<int[]>((string)e[2])!.All(n => keep.Contains(n.ToString(CultureInfo.InvariantCulture))))
                elements.Add((string)e[1]);
            else Exec("DELETE FROM fem_elements WHERE id=@p0", e[0]);

        foreach (var s in Rows($"SELECT id, node_tag, mask FROM fem_mesh_node_supports WHERE schema_id={sid}"))
            if (!keep.Contains((string)s[1])) Exec("DELETE FROM fem_mesh_node_supports WHERE id=@p0", s[0]);
            else if (symmetry.Remove((string)s[1], out int mask)) Exec("UPDATE fem_mesh_node_supports SET mask=@p1 WHERE id=@p0", s[0], Convert.ToInt32(s[2]) | mask);
        foreach (var (tag, mask) in symmetry)
            Exec("INSERT INTO fem_mesh_node_supports (schema_id, node_tag, mask, origin) VALUES (@p0, @p1, @p2, @p3)", sid, tag, mask, LiraSchemaConverter.BoundaryOrigin);
        int bodies = 0;
        foreach (var b in Rows($"SELECT id, master_node_tag, slave_node_tags_json FROM fem_rigid_bodies WHERE schema_id={sid}"))
        {
            var slaves = System.Text.Json.JsonSerializer.Deserialize<string[]>((string)b[2])!;
            if (!keep.Contains((string)b[1])) Exec("DELETE FROM fem_rigid_bodies WHERE id=@p0", b[0]);
            else if (!slaves.All(keep.Contains)) throw new InvalidOperationException($"АЖТ узла {b[1]} пересекает плоскость симметрии.");
            else bodies++;
        }

        var cases = Rows($"SELECT id, source_load_num FROM fem_load_cases WHERE schema_id={sid}").ToDictionary(r => Convert.ToInt32(r[1]), r => (long)r[0]);
        Exec("DELETE FROM fem_mesh_node_loads WHERE schema_id=@p0 AND load_case_id<>@p1", sid, cases[1]);
        foreach (var l in Rows($"SELECT id, mesh_node_tag FROM fem_mesh_node_loads WHERE schema_id={sid}"))
            if (!keep.Contains((string)l[1])) Exec("DELETE FROM fem_mesh_node_loads WHERE id=@p0", l[0]);
        Exec("DELETE FROM fem_element_loads WHERE schema_id=@p0 AND load_case_id NOT IN (@p1, @p2)", sid, cases[1], cases[2]);
        Exec("UPDATE fem_element_loads SET load_case_id=@p2 WHERE schema_id=@p0 AND load_case_id=@p1", sid, cases[1], cases[2]);
        foreach (var l in Rows($"SELECT id, target_tags_json FROM fem_element_loads WHERE schema_id={sid}"))
            Exec("UPDATE fem_element_loads SET target_tags_json=@p1 WHERE id=@p0", l[0], System.Text.Json.JsonSerializer.Serialize(
                System.Text.Json.JsonSerializer.Deserialize<string[]>((string)l[1])!.Where(elements.Contains)));
        foreach (var g in Rows($"SELECT id, member_tags_json FROM fem_member_groups WHERE schema_id={sid}"))
            Exec("UPDATE fem_member_groups SET member_tags_json=@p1 WHERE id=@p0", g[0], System.Text.Json.JsonSerializer.Serialize(
                FemMemberGroup.ParseTags((string)g[1]).Where(elements.Contains)));
        int elastic = elasticBars ? Exec("UPDATE fem_elements SET cross_section_id=NULL WHERE schema_id=@p0 AND elem_type='beam'", sid) : 0;
        int elasticGroups = elasticPlates ? Exec("UPDATE fem_member_groups SET plate_section_id=NULL WHERE schema_id=@p0", sid) : 0;
        tx.Commit();
        return $"четверть: узлов {keep.Count}, КЭ {elements.Count}, АЖТ {bodies}, новых закреплений на плоскостях симметрии {symmetry.Count}, " +
               $"стержней без сечения (упругих) {elastic}, групп пластин без сечения (упругих) {elasticGroups}";
    }

    /// <summary>
    /// Сечения стержней в готовой базе OPENCS_LIRA_SECANT_ASSIGN_BARS (стержни без сечения, например после
    /// OPENCS_LIRA_SECANT_ELASTIC_BARS): «Брус» по жёсткости с армированием OPENCS_LIRA_SECANT_BARS / _BAR_D / _BAR_A,
    /// классы — как в <see cref="ManualSections"/>. Без запущенной ЛИРЫ.
    /// </summary>
    [Fact]
    public void AssignBarSections()
    {
        var path = Environment.GetEnvironmentVariable("OPENCS_LIRA_SECANT_ASSIGN_BARS");
        if (string.IsNullOrWhiteSpace(path)) return;
        try
        {
            using var db = new DatabaseService(path);
            db.LoadAll();
            var schema = db.FemSchemas.First();
            ManualSections(db, schema, FemCheckSchemaData.Load(db, schema.Id));
        }
        finally { SqliteConnection.ClearAllPools(); }
    }

    /// <summary>
    /// Усилия пластин из расчёта, открытого в ЛИРЕ (API результатов): сумма загружений по КЭ базы OPENCS_LIRA_SECANT_RUN
    /// в CSV OPENCS_LIRA_SECANT_LIRA_FORCES (id, mx, my, mxy, кН·м/м; qx, qy, кН/м) — для сверки упругой эпюры.
    /// Знаки и оси — как их отдаёт импорт усилий ЛИРЫ с настройками по умолчанию.
    /// </summary>
    [Fact]
    public void ExportLiraPlateForces()
    {
        var source = Environment.GetEnvironmentVariable("OPENCS_LIRA_SECANT_RUN");
        var csv = Environment.GetEnvironmentVariable("OPENCS_LIRA_SECANT_LIRA_FORCES");
        if (string.IsNullOrWhiteSpace(source) || string.IsNullOrWhiteSpace(csv)) return;
        FemSchema schema;
        List<int> ids;
        try
        {
            using var db = new DatabaseService(source);
            db.LoadAll();
            schema = db.FemSchemas.First();
            ids = [.. db.GetFemMeshElements(schema.Id).Where(e => e.ElemType == "shell").Select(e => int.Parse(e.ElemTag)).Order()];
        }
        finally { SqliteConnection.ClearAllPools(); }

        List<ForceSet>? sets = null;
        Exception? error = null;
        var thread = new Thread(() =>
        {
            try { sets = LiraApiForceImporter.ReadLoadCaseForces(schema, ids, new LiraImportSettings()); }
            catch (Exception ex) { error = ex; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (error != null) throw error;

        var sum = new Dictionary<int, double[]>();
        foreach (var set in sets!)
        {
            output.WriteLine($"«{set.Tag}»: строк пластин {set.ShellItems.Count}, сечений на КЭ до {set.ShellItems.GroupBy(i => i.SourceElementNum).Max(g => g.Count())}");
            foreach (var g in set.ShellItems.GroupBy(i => i.SourceElementNum!.Value))
            {
                if (!sum.TryGetValue(g.Key, out var a)) sum[g.Key] = a = new double[5];
                a[0] += g.Average(i => i.Mx); a[1] += g.Average(i => i.My); a[2] += g.Average(i => i.Mxy);
                a[3] += g.Average(i => i.Qx); a[4] += g.Average(i => i.Qy);
            }
        }
        File.WriteAllLines(csv, new[] { "id,mx,my,mxy,qx,qy" }.Concat(sum.OrderBy(kv => kv.Key).Select(kv =>
            kv.Key.ToString(CultureInfo.InvariantCulture) + "," + string.Join(',', kv.Value.Select(x => x.ToString("R", CultureInfo.InvariantCulture))))));
        output.WriteLine($"КЭ запрошено {ids.Count}, получено {sum.Count}; файл: {csv}");
    }

    /// <summary>
    /// База с четвертью схемы: копия OPENCS_LIRA_SECANT_QUARTER_FROM (база <see cref="CreateDatabase"/>) в
    /// OPENCS_LIRA_SECANT_QUARTER_DB, обрезанная <see cref="CutQuarter"/> по x, y ≤ OPENCS_LIRA_SECANT_QUARTER_HALF (по
    /// умолчанию 9 м); OPENCS_LIRA_SECANT_ELASTIC_BARS=1 — стержни без сечения CScore (упругие по жёсткости схемы),
    /// OPENCS_LIRA_SECANT_ELASTIC_PLATES=1 — то же для пластин (линейная схема, как в ЛИРЕ).
    /// </summary>
    [Fact]
    public void CreateQuarterDatabase()
    {
        var from = Environment.GetEnvironmentVariable("OPENCS_LIRA_SECANT_QUARTER_FROM");
        var path = Environment.GetEnvironmentVariable("OPENCS_LIRA_SECANT_QUARTER_DB");
        if (string.IsNullOrWhiteSpace(from) || string.IsNullOrWhiteSpace(path)) return;
        double half = double.TryParse(Environment.GetEnvironmentVariable("OPENCS_LIRA_SECANT_QUARTER_HALF"), NumberStyles.Float, CultureInfo.InvariantCulture, out double h) ? h : 9;
        File.Copy(from, path, overwrite: true);
        try
        {
            output.WriteLine(CutQuarter(path, half, Environment.GetEnvironmentVariable("OPENCS_LIRA_SECANT_ELASTIC_BARS") == "1",
                Environment.GetEnvironmentVariable("OPENCS_LIRA_SECANT_ELASTIC_PLATES") == "1"));
            using var db = new DatabaseService(path);
            db.LoadAll();
            var schema = db.FemSchemas.First();
            foreach (var lc in db.GetFemLoadCases(schema.Id).OrderBy(c => c.SourceLoadNum))
                output.WriteLine($"ЗН {lc.SourceLoadNum} «{lc.Tag}»: узловых сил {db.GetFemMeshNodeLoads(schema.Id, lc.Id).Count}, " +
                                 $"ΣFz = {db.GetFemMeshNodeLoads(schema.Id, lc.Id).Sum(l => l.Fz) / 1e3:0.###} кН; на КЭ: " +
                                 string.Join("; ", db.GetFemElementLoads(schema.Id, lc.Id).Select(l => $"{l.Values[0] / 1e3:0.###} кПа × {l.TargetTags.Count} КЭ")));
            output.WriteLine("закрепления, маска × узлов: " + string.Join(", ", db.GetFemMeshNodeSupports(schema.Id).GroupBy(s => s.Mask)
                .Select(g => $"{Convert.ToString(g.Key, 2)} × {g.Count()}")));
            output.WriteLine($"База: {path}");
        }
        finally { SqliteConnection.ClearAllPools(); }
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
                var stages = v.ColumnShare is double share
                    ? [new FemAnalysisStage { Tag = "ЗН 1 и 2", LoadExpressionJson = Expr((cases[0], v.K * share), (cases[1], v.K)), LoadFactorStep = 1.0 / v.Steps, MaxLoadFactor = 1 }]
                    : v.Slab
                    ? [new FemAnalysisStage { Tag = cases[0].Tag, LoadExpressionJson = Expr((cases[0], 1)), LoadFactorStep = 1, MaxLoadFactor = 1 },
                       new FemAnalysisStage { Tag = cases[1].Tag, LoadExpressionJson = Expr((cases[1], v.K)), LoadFactorStep = 1.0 / v.Steps, MaxLoadFactor = 1 }]
                    : v.Sequential
                    ? cases.Select(c => new FemAnalysisStage { Tag = c.Tag, LoadExpressionJson = Expr((c, v.K)), LoadFactorStep = 1.0 / v.Steps, MaxLoadFactor = 1 }).ToList()
                    : [new FemAnalysisStage { Tag = "все ЗН", LoadExpressionJson = Expr([.. cases.Select(c => (c, v.K))]), LoadFactorStep = 1.0 / v.Steps, MaxLoadFactor = 1 }];
                var csfea = new FemCsfeaParams
                {
                    PlateRebarSource = db.PlateSections.Any(s => s.RebarLayers.Count > 0) ? FemCheckRebarSource.Section : FemCheckRebarSource.Assigned, GeomNonlinear = v.Geom, TensionConcrete = v.Tension, MaxBisections = v.Bisections, MaxIterations = v.Iterations,
                };
                var analysis = new FemAnalysis
                {
                    SchemaId = schema.Id, Tag = v.Name, Kind = FemCsfeaRunner.AnalysisKind, LoadExpressionJson = stages[0].LoadExpressionJson,
                    ParamsJson = new FemAnalysisParams { CalcType = v.Calc, Stages = stages, Csfea = csfea }.ToJson(),
                };
                var prepared = await FemCsfeaRunner.PrepareAsync(ctx, schema, analysis, buildMesh: false, CancellationToken.None);
                output.WriteLine($"=== {v.Name}: проверка входа — ошибки {prepared.HasErrors}");
                if (outcomes.Count == 0 || prepared.HasErrors)
                    foreach (var line in prepared.Describe().Concat(prepared.Report).Take(40)) output.WriteLine("  " + line);
                if (prepared.HasErrors) continue;
                if (outcomes.Count == 0 && prepared.Input!.PlateSection is { } plateOf
                    && plateOf(prepared.Input.MeshElements.First(e => e.ElemType == "shell")) is { } first)
                    output.WriteLine($"  пластина: {System.Text.Json.JsonSerializer.Serialize(first.Section.Section)}");
                // Диаграммы материалов плиты варианта: точки ε → σ (единицы диаграммы).
                if (prepared.Input!.PlateSection?.Invoke(prepared.Input.MeshElements.First(e => e.ElemType == "shell"))?.Materials is { } mats)
                    foreach (var (name, d) in new[] { ("бетон", mats.ConcreteDiagram), ("арматура", mats.RebarDiagram) })
                        output.WriteLine($"  диаграмма «{name}» ({v.Calc}): " + string.Join("; ", d.Ic.X.Concat(d.It.X).Distinct().OrderBy(x => x)
                            .Select(x => string.Create(CultureInfo.InvariantCulture, $"{x:0.######} → {d.Sig(x, out _):0.###}"))));

                using var log = outDir == null ? null : new StreamWriter(Path.Combine(outDir, Regex.Replace(v.Name, @"[^\w=.,-]", "_") + ".txt"));
                var clock = Stopwatch.StartNew();
                if (log != null) log.AutoFlush = true;
                // OPENCS_LIRA_SECANT_MOMENT_CUTS = «опора=6;пролёт=3» — сечения плиты x = const: на каждом принятом шаге средний
                // по ширине Mx (кН·м/м) в КЭ, ближайших к сечению с обеих сторон; отношения — первого сечения к остальным.
                var cuts = (Environment.GetEnvironmentVariable("OPENCS_LIRA_SECANT_MOMENT_CUTS") ?? "").Split(';', StringSplitOptions.RemoveEmptyEntries)
                    .Select(p => p.Split('=')).Select(p => (Name: p[0].Trim(), X: double.Parse(p[1], CultureInfo.InvariantCulture))).ToList();
                var cutShells = new List<HashSet<int>>();
                if (cuts.Count > 0)
                {
                    var at = meshNodes.Where(n => int.TryParse(n.NodeTag, out _)).ToDictionary(n => int.Parse(n.NodeTag), n => n.X);
                    var centers = prepared.Input.MeshElements.Where(e => e.ElemType == "shell").ToDictionary(e => int.Parse(e.ElemTag),
                        e => System.Text.Json.JsonSerializer.Deserialize<int[]>(e.NodeIdsJson)!.Average(n => at[n]));
                    foreach (var (_, x) in cuts)
                    {
                        double nearest = centers.Values.Min(c => Math.Abs(c - x));
                        cutShells.Add([.. centers.Where(kv => Math.Abs(kv.Value - x) < nearest + 1e-6).Select(kv => kv.Key)]);
                    }
                }
                var cutMoments = new Dictionary<(int Stage, double Factor), double[]>();
                Action<CSfea.Core.SecantStepResult, Func<RcSecantStepFields>>? onStep = cuts.Count == 0 ? null : (s, fields) =>
                {
                    var f = fields();
                    var sum = new double[cuts.Count];
                    for (int e = 0; e < f.ShellIds.Length; e++)
                        for (int c = 0; c < cuts.Count; c++)
                            if (cutShells[c].Contains(f.ShellIds[e])) sum[c] += f.ShellForces[e * RcSecantStepFields.ShellForceComponents + 3];
                    cutMoments[(s.Stage, s.LoadFactor)] = [.. sum.Select((v, c) => v / cutShells[c].Count / 1e3)];
                };
                var options = FemCsfeaSetup.SecantOptions(csfea, -1, s => log?.WriteLine($"[{clock.Elapsed.TotalSeconds,7:0.0}] {s}"),
                    prepared.Adapted!.ShellForceAngles, onStep);
                var run = RcSecantAnalysis.Run(prepared.Adapted.Model, options, null, CancellationToken.None);
                clock.Stop();
                log?.Flush();

                var r = run.Result;
                var last = r.Steps.LastOrDefault(s => s.Converged);
                if (last == null) { output.WriteLine($"  ни один шаг не сошёлся: {r.Message}"); continue; }
                int stageCount = stages.Count;
                // λ шага: доля полной нагрузки × k; при slab — множитель нагрузки на плиту (ЗН 2), стадия 1 — нуль.
                double Lambda(int stage, double factor) => v.ColumnShare != null ? v.K * factor : v.Slab ?(stage == 0 ? 0 : v.K * factor) : v.K * (stage + factor) / stageCount;
                double lambda = Lambda(last.Stage, last.LoadFactor);
                var u = new Dictionary<int, double[]>();
                for (int i = 0; i < run.Build.NodeIds.Length; i++) u[run.Build.NodeIds[i]] = last.U[(6 * i)..(6 * i + 6)];
                var cracks = new Dictionary<int, int>();
                for (int e = 0; e < run.Build.ShellIds.Length; e++) cracks[run.Build.ShellIds[e]] = last.Shells[e].Cracks;
                var history = r.Steps.Where(s => s.Converged)
                    .Select(s => (Lambda(s.Stage, s.LoadFactor), s.U[run.Build.Dof(control[0].Node, 2)] * 1e3)).ToList();
                // Состояние по шагам: наибольшее горизонтальное смещение, текучесть и отказ сечений (оболочки + стержни).
                output.WriteLine("  λ: гориз., мм / текучесть об.+ст. / отказ об.+ст.: " + string.Join("; ", r.Steps.Where(s => s.Converged).Select(s =>
                {
                    double h = 0;
                    for (int i = 0; i < s.U.Length / 6; i++) h = Math.Max(h, Math.Sqrt(s.U[6 * i] * s.U[6 * i] + s.U[6 * i + 1] * s.U[6 * i + 1]));
                    return $"{Lambda(s.Stage, s.LoadFactor):0.####}: {h * 1e3:0.00} / {s.Shells.Count(x => x.Yielded)}+{s.Beams.Count(x => x.Yielded)} / {s.Shells.Count(x => x.Failed)}+{s.Beams.Count(x => x.Failed)}";
                })));
                if (cuts.Count > 0)
                {
                    output.WriteLine("  средний по ширине Mx, кН·м/м: λ | " + string.Join(" | ", cuts.Select(c => $"{c.Name} (x = {c.X:0.##})")) +
                                     string.Concat(cuts.Skip(1).Select(c => $" | {cuts[0].Name} / {c.Name}")));
                    foreach (var s in r.Steps.Where(s => s.Converged))
                        if (cutMoments.TryGetValue((s.Stage, s.LoadFactor), out var m))
                            output.WriteLine($"    {Lambda(s.Stage, s.LoadFactor):0.####} | " + string.Join(" | ", m.Select(x => x.ToString("0.00"))) +
                                             string.Concat(m.Skip(1).Select(x => $" | {(Math.Abs(x) > 1e-9 ? Math.Abs(m[0] / x).ToString("0.000") : "—")}")));
                }
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
                    // Ход нагружения: принятые шаги — λ, uz контрольных точек (мм), число оболочек с трещинами, текучестью, отказом.
                    File.WriteAllLines(stem + ".history.csv", new[] { "lambda," + string.Join(',', control.Select(c => $"uz_{c.Node}")) + ",cracked,yielded,failed,iterations" + string.Concat(cuts.Select(c => string.Create(inv, $",mx_{c.X}"))) }
                        .Concat(r.Steps.Where(s => s.Converged).Select(s => string.Create(inv, $"{Lambda(s.Stage, s.LoadFactor)},") +
                            string.Join(',', control.Select(c => (s.U[run.Build.Dof(c.Node, 2)] * 1e3).ToString("R", inv))) +
                            string.Create(inv, $",{s.Shells.Count(x => x.Cracked)},{s.Shells.Count(x => x.Yielded)},{s.Shells.Count(x => x.Failed)},{s.Iterations}") +
                            string.Concat(cuts.Select((_, c) => "," + (cutMoments.TryGetValue((s.Stage, s.LoadFactor), out var m) ? m[c].ToString("R", inv) : ""))))));
                    var mesh = run.Build.Mesh;
                    File.WriteAllLines(stem + ".shells.csv", new[] { "id,nodes,cracks,cracked,yielded,failed" }.Concat(Enumerable.Range(0, mesh.Shells.Count).Select(e =>
                        string.Create(inv, $"{run.Build.ShellIds[e]},{string.Join(' ', mesh.Shells[e].Nodes.Select(n => run.Build.NodeIds[n]))},{last.Shells[e].Cracks},{(last.Shells[e].Cracked ? 1 : 0)},{(last.Shells[e].Yielded ? 1 : 0)},{(last.Shells[e].Failed ? 1 : 0)}"))));
                    File.WriteAllLines(stem + ".beams.csv", new[] { "id,i,j,cracked,yielded,failed" }.Concat(Enumerable.Range(0, mesh.Beams.Count).Select(e =>
                        string.Create(inv, $"{run.Build.BeamIds[e]},{run.Build.NodeIds[mesh.Beams[e].I]},{run.Build.NodeIds[mesh.Beams[e].J]},{(last.Beams[e].Cracked ? 1 : 0)},{(last.Beams[e].Yielded ? 1 : 0)},{(last.Beams[e].Failed ? 1 : 0)}"))));
                    // Усилия последнего принятого шага: пластины — в осях выдачи (центр КЭ), стержни — местные силы концов i, j.
                    if (run.LastConvergedFields() is { } fields)
                    {
                        const int sc = RcSecantStepFields.ShellForceComponents, bc = RcSecantStepFields.BeamForceComponents;
                        File.WriteAllLines(stem + ".shellforces.csv", new[] { "id,nx,ny,nxy,mx,my,mxy,qx,qy" }.Concat(Enumerable.Range(0, fields.ShellIds.Length).Select(e =>
                            fields.ShellIds[e].ToString(inv) + "," + string.Join(',', fields.ShellForces.Skip(e * sc).Take(sc).Select(x => x.ToString("R", inv))))));
                        // Трещины по граням КЭ (низ и верх — по глобальной Z): доля треснувших слоёв половины сечения, главная
                        // растягивающая деформация грани и угол линии трещины к оси X, град (перпендикуляр к ε₁), по
                        // деформациям центра КЭ.
                        var plateOfElement = prepared.Input.PlateSection;
                        var byTag = prepared.Input.MeshElements.Where(e => e.ElemType == "shell").ToDictionary(e => int.Parse(e.ElemTag));
                        // Ширина раскрытия (только N, NL): усилия КЭ шага → упрощённая проверка Капра — Мори (пп. 8.2.15–8.2.18
                        // СП 63); длительная часть усилий — доля OPENCS_LIRA_SECANT_LONG_SHARE (по умолчанию 0,9) от полных;
                        // a_long = a(длит., φ1 = 1,4), a_short = a(полн., 1,0) − a(длит., 1,0) + a(длит., 1,4) (п. 8.2.7) — наибольшие
                        // по направлениям грани; угол — линия трещины к оси X сечения (перпендикуляр к направлению α).
                        bool widths = v.Calc is CalcType.N or CalcType.NL;
                        double longShare = double.TryParse(Environment.GetEnvironmentVariable("OPENCS_LIRA_SECANT_LONG_SHARE"), NumberStyles.Float, inv, out double ls) ? ls : 0.9;
                        var materialById = db.Materials.ToDictionary(m => m.Id);
                        // sig_ratio — наибольшее σs/σs,т растянутой арматуры КЭ, psi_min — наименьший ψs (поля состояния шага).
                        var crackRows = new List<string> { "id,crack_bot,crack_top,e1_bot,ang_bot,e1_top,ang_top,acrc_long_bot,acrc_short_bot,acrc_ang_bot,acrc_long_top,acrc_short_top,acrc_ang_top,sig_ratio,psi_min" };
                        for (int e = 0; e < mesh.Shells.Count; e++)
                        {
                            if (plateOfElement?.Invoke(byTag[run.Build.ShellIds[e]])?.Section.Section is not { } ps) continue;
                            var coords = mesh.ShellCoords(e);
                            var ue = CSfea.Core.StructuralMesh.NodeDofs(mesh.Shells[e].Nodes).Select(d => last.U[d]).ToArray();
                            var (eps, kappa, _) = CSfea.Core.ShellElementForces.CenterStrainsGlobal(coords, ue);
                            var frame = CSfea.Core.ShellGeometry.LocalFrame(coords);
                            bool up = frame[2, 2] > 0;
                            (double E1, double Angle) Face(double z)
                            {
                                double ex = eps[0] + kappa[0] * z, ey = eps[1] + kappa[1] * z, g = eps[2] + kappa[2] * z;
                                double theta = 0.5 * Math.Atan2(g, ex - ey);
                                double e1 = 0.5 * (ex + ey) + Math.Sqrt(0.25 * (ex - ey) * (ex - ey) + 0.25 * g * g);
                                double c = -Math.Sin(theta), s = Math.Cos(theta);   // линия трещины в местных осях КЭ
                                double angle = Math.Atan2(c * frame[0, 1] + s * frame[1, 1], c * frame[0, 0] + s * frame[1, 0]) * 180 / Math.PI;
                                return (e1, angle > 90 ? angle - 180 : angle <= -90 ? angle + 180 : angle);
                            }
                            var (bot, top) = (Face(up ? -ps.H / 2 : ps.H / 2), Face(up ? ps.H / 2 : -ps.H / 2));
                            int so = e * RcSecantStepFields.ShellStateComponents;
                            // [низ, верх] по оси z сечения: a_long, a_short, угол линии трещины.
                            var w = new (double Long, double Short, double Angle)[2];
                            if (widths && materialById.TryGetValue(ps.ConcreteMaterialId, out var concreteMat) && materialById.TryGetValue(ps.RebarMaterialId, out var rebarMat))
                            {
                                int fo = e * sc;
                                List<ShellSimplDirectionResult> Dirs(double k, double phi1) => ShellSimplSolver.Solve(new ShellSimplSolver.SolveParams(
                                    k * fields.ShellForces[fo] / 1e3, k * fields.ShellForces[fo + 1] / 1e3, k * fields.ShellForces[fo + 2] / 1e3,
                                    k * fields.ShellForces[fo + 3] / 1e3, k * fields.ShellForces[fo + 4] / 1e3, k * fields.ShellForces[fo + 5] / 1e3,
                                    "shell_simpl_capri_sls", 10.0, 0.3, phi1, 0.5), ps, concreteMat, rebarMat, CalcType.N).CapriDirs!;
                                var (full10, long10, long14) = (Dirs(1, 1.0), Dirs(longShare, 1.0), Dirs(longShare, 1.4));
                                for (int i = 0; i < full10.Count; i++)
                                {
                                    if (full10[i].Strip.NoRebar) continue;
                                    double aLong = long14[i].Strip.Acrc_mm, aShort = full10[i].Strip.Acrc_mm - long10[i].Strip.Acrc_mm + aLong;
                                    int face = full10[i].Top ? 1 : 0;
                                    if (aShort > w[face].Short)
                                    {
                                        double line = full10[i].Alpha_deg + 90;
                                        w[face] = (aLong, aShort, line > 90 ? line - 180 : line);
                                    }
                                }
                            }
                            var (wb, wt) = up ? (w[0], w[1]) : (w[1], w[0]);
                            crackRows.Add(string.Create(inv, $"{run.Build.ShellIds[e]},{fields.ShellStates[so + (up ? 0 : 1)]},{fields.ShellStates[so + (up ? 1 : 0)]},{bot.E1:R},{bot.Angle:0.##},{top.E1:R},{top.Angle:0.##},") +
                                          string.Create(inv, $"{wb.Long:0.####},{wb.Short:0.####},{wb.Angle:0.##},{wt.Long:0.####},{wt.Short:0.####},{wt.Angle:0.##},{fields.ShellStates[so + 3]:0.####},{fields.ShellStates[so + 2]:0.####}"));
                        }
                        File.WriteAllLines(stem + ".cracks.csv", crackRows);
                        File.WriteAllLines(stem + ".beamforces.csv", new[] { "id,ni,qyi,qzi,mxi,myi,mzi,nj,qyj,qzj,mxj,myj,mzj" }.Concat(Enumerable.Range(0, fields.BeamIds.Length).Select(e =>
                            fields.BeamIds[e].ToString(inv) + "," + string.Join(',', fields.BeamForces.Skip(e * bc).Take(bc).Select(x => x.ToString("R", inv))))));
                    }
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
