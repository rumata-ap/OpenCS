using CScore;
using CScore.Fem;
using CScore.Import;
using OpenCS.OpenSees.CScore;
using OpenCS.OpenSees.Model;

namespace OpenCS.Services.Scad;

/// <summary>Материалы расчёта плиты по схеме SCAD.</summary>
internal enum ScadShellMaterialMode
{
    /// <summary>
    /// Фактические характеристики натурного опыта (Дорфман, 1975): бетон Rb = 26,0 МПа (призменная), Eb = 26 500 МПа,
    /// Rbt = 2,0 МПа (не измерялась); арматура плит по ЖБ-группам с A400 → A300 (A-II), колонны — класс группы.
    /// </summary>
    Experiment,

    /// <summary>Бетон B22,5 нормативный (0,9 · B25: Rb,n = 16,65, Rbt,n = 1,395 МПа), Eb = 28 750 МПа; арматура — как в Experiment.</summary>
    NormativeB225,

    /// <summary>Классы ЖБ-групп SCAD по справочнику СП 63 (нормативные).</summary>
    Groups,
}

/// <summary>
/// Параметры сценария: упругий расчёт (линейная сверка) или нелинейный, материалы, стадии; работа бетона на растяжение
/// (false — колонны без растяжения, у пластин ft = <see cref="ScadShellScenario.MinTensionKPa"/>: модель
/// PlasticDamageConcretePlaneStress требует ft &gt; 0).
/// </summary>
internal sealed record ScadShellScenarioOptions(bool Elastic, ScadShellMaterialMode Materials,
    IReadOnlyList<ScadShellStage> Stages, string? CatalogDirectory = null, bool ConcreteTension = true);

/// <summary>
/// Вход сборщика <see cref="ScadShellModelAssembler"/> по прочитанной схеме SCAD: материалы, сечения пластин из шаблонов
/// ЖБ-групп и заданного армирования (<see cref="ScadAssignedPlateSectionSource"/>), fiber-сечения колонн из
/// заданного армирования стержней (<see cref="ScadAssignedBarSectionSource"/>).
/// </summary>
internal static class ScadShellScenario
{
    const int ConcreteId = 1, A240Id = 2, A300Id = 3, A400Id = 4;

    /// <summary>Прочность бетона пластин на растяжение при отключённой работе на растяжение, кПа (0,05 МПа).</summary>
    public const double MinTensionKPa = 50;

    public static ScadShellModelInput Build(ScadSchemaData data, ScadShellScenarioOptions o, List<string> report)
    {
        var materials = Materials(o);
        var byId = materials.ToDictionary(m => m.Id);
        var groups = new ScadConcreteGroupIndex(data.ConcreteGroups);
        var stiff = data.Stiffnesses.ToDictionary(s => s.Id);
        var elems = data.Elements.ToDictionary(e => e.Id);

        FemCheckScopeElement Scope(int id) => new(id, new FemElement
        {
            ElemTag = id.ToString(), StiffnessNum = elems[id].StiffnessId,
            ThicknessM = stiff.GetValueOrDefault(elems[id].StiffnessId)?.ThicknessM,
        }, null);

        int RebarId(string cls, bool plate) => MaterialCatalog.ClassKey(cls) switch
        {
            "A240" => A240Id,
            "A300" => A300Id,
            "A400" when plate && o.Materials != ScadShellMaterialMode.Groups => A300Id,   // A-II опыта
            _ => A400Id,
        };

        // Пластины: шаблон и источник на ЖБ-группу.
        var sources = new Dictionary<int, (ScadAssignedPlateSectionSource Source, PlateSection Template)>();
        ScadShellElementSection? PlateSection(int id)
        {
            double h = stiff.GetValueOrDefault(elems[id].StiffnessId)?.ThicknessM ?? 0.2;
            var g = groups.Find(id);
            int gNum = g?.Num ?? 0;
            if (!sources.TryGetValue(gNum, out var src))
            {
                var template = new PlateSection
                {
                    Tag = g?.Name ?? "без группы", H = h, ConcreteMaterialId = ConcreteId,
                    RebarMaterialId = RebarId(g?.LongitudinalRebarClass ?? "A400", plate: true), TensionConcrete = true,
                };
                src = (data.AssignedRebar != null ? new ScadAssignedPlateSectionSource(template, data.AssignedRebar, groups) : null!, template);
                sources[gNum] = src;
            }
            if (o.Elastic) return new ScadShellElementSection(src.Template, $"elastic|{h}");
            if (src.Source == null) { report.Add($"Пластина {id}: в схеме нет заданного армирования."); return null; }
            var r = src.Source.Resolve(Scope(id));
            if (r.Section == null) { report.Add($"Пластина {id}: {r.Reason}"); return null; }
            return new ScadShellElementSection(r.Section, $"{gNum}|{r.RebarKey}");
        }

        // Колонны: fiber-сечения по заданному армированию стержней.
        Func<int, (CrossSection, string)?>? beamSection = null;
        if (!o.Elastic && data.AssignedRebar != null)
        {
            var lira = ScadSchemaConverter.ToSchemaStiffnesses(data).ToDictionary(s => s.Id);
            var context = new ScadBarSectionContext(lira, null, groups,
                _ => byId[ConcreteId], cls => byId[RebarId(cls, plate: false)]);
            var bars = new ScadAssignedBarSectionSource(context, data.AssignedRebar);
            beamSection = id =>
            {
                var r = bars.Resolve(Scope(id), null);
                if (r.Section == null) { report.Add($"Стержень {id}: {r.Reason} — упругий."); return null; }
                // Fiber-сечению OpenSees нужна сетка фибр бетона: прямоугольная 20 × 20 (сечение кэшируется источником).
                foreach (var area in r.Section.Areas.Where(a => a.Category == AreaCategory.Region &&
                                                                 a.Fibers.All(f => f.TypeFiber == FiberType.point)))
                    area.SliceXY(20, 20);
                return (r.Section, r.Section.Tag);
            };
        }

        var (eGe, nu) = o.Elastic ? ElasticPlate(data) : (0.0, 0.2);

        var concrete = byId[ConcreteId].GetChars(CalcType.N)!;
        double gj = Math.Abs(concrete.E) * 1000 / 2.4 * 0.141 * Math.Pow(0.3, 4);
        return new ScadShellModelInput
        {
            Data = data,
            PlateSection = PlateSection,
            Resolver = o.Elastic
                ? new ElasticPlateMaterialResolver(eGe, nu)
                : new PlateSectionShellMaterialResolver(id => byId.GetValueOrDefault(id), CalcType.N, SteelModelKind.Steel01, 1e-4),
            BeamSection = beamSection,
            BeamMaterials = byId,
            BeamCalc = CalcType.N,
            BeamOptions = new CrossSectionToOpenSeesAdapter.Options { GJ = gj, ConsiderConcreteTension = o.ConcreteTension },
            Stages = o.Stages,
        };
    }

    /// <summary>
    /// Упругие E (Па) и ν пластин для линейной сверки — из строки жёсткости первой пластины SCAD («GE E ν h …»);
    /// нет данных — 3·10¹⁰ Па и 0,2.
    /// </summary>
    public static (double E, double Nu) ElasticPlate(ScadSchemaData data)
    {
        var stiff = data.Stiffnesses.ToDictionary(s => s.Id);
        var ge = data.Elements.Where(e => ScadElementKinds.Classify(e.TypeCode, e.NodeIds.Length) == ScadElementKind.Shell)
            .Select(e => stiff.GetValueOrDefault(e.StiffnessId)).FirstOrDefault(s => s?.Text != null);
        var parts = ge?.Text!.Split([' ', '\t'], StringSplitOptions.RemoveEmptyEntries) ?? [];
        double e = parts.Length > 2 && double.TryParse(parts[1], System.Globalization.NumberStyles.Float,
            System.Globalization.CultureInfo.InvariantCulture, out double e0) ? e0 * (data.AnalysisModel?.ForceUnitN ?? 1) : 3e10;
        double nu = parts.Length > 2 && double.TryParse(parts[2], System.Globalization.NumberStyles.Float,
            System.Globalization.CultureInfo.InvariantCulture, out double v) ? v : 0.2;
        return (e, nu);
    }

    /// <summary>Материалы сценария: бетон (id 1), A240 (2), A300 (3), A400 (4).</summary>
    public static List<Material> Materials(ScadShellScenarioOptions o)
    {
        var concrete = MaterialCatalog.CreateHeavyConcrete("B25", o.CatalogDirectory)
            ?? throw new InvalidOperationException("В справочнике нет бетона B25.");
        switch (o.Materials)
        {
            case ScadShellMaterialMode.Experiment: SetConcrete(concrete, "Бетон опыта (Rпр 26,0 МПа)", -26000, 2000, 26_500_000); break;
            case ScadShellMaterialMode.NormativeB225: SetConcrete(concrete, "B22,5 (0,9·B25)", -16650, 1395, 28_750_000); break;
        }
        if (!o.ConcreteTension)
            foreach (var c in Chars(concrete))
            {
                c.Ft = MinTensionKPa;
                c.Et1 = 0.6 * MinTensionKPa / c.E;
            }
        concrete.Id = ConcreteId;
        var a240 = MaterialCatalog.CreateRebar("A240", o.CatalogDirectory) ?? throw new InvalidOperationException("Нет A240.");
        a240.Id = A240Id;
        var a300 = MaterialCatalog.CreateRebar("A240", o.CatalogDirectory)!;
        foreach (var c in Chars(a300))
        {
            c.Fc = -300000; c.Rsc = 300000; c.Ft = 300000;
            c.Ec0 = -300000 / c.E; c.Et0 = 300000 / c.E;
        }
        a300.Id = A300Id;
        a300.Tag = "A300 (A-II)";
        var a400 = MaterialCatalog.CreateRebar("A400", o.CatalogDirectory) ?? throw new InvalidOperationException("Нет A400.");
        a400.Id = A400Id;
        return [concrete, a240, a300, a400];
    }

    static void SetConcrete(Material m, string tag, double fc, double ft, double e)
    {
        foreach (var c in Chars(m))
        {
            c.Fc = fc; c.Ft = ft; c.E = e;
            c.Ec1 = 0.6 * fc / e; c.Et1 = 0.6 * ft / e;
        }
        m.Tag = tag;
        m.E = e;
    }

    static IEnumerable<MaterialChars> Chars(Material m) =>
        new[] { m.C, m.CL, m.N, m.NL }.Where(c => c != null).Cast<MaterialChars>();
}
