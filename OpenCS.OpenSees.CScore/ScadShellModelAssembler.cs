using System.Globalization;
using System.Text.RegularExpressions;
using CScore;
using CScore.Import;
using OpenCS.OpenSees.Model;
using OpenCS.OpenSees.Structural;

namespace OpenCS.OpenSees.CScore;

/// <summary>Стадия нагружения: сумма загружений SCAD с коэффициентами, приращение λ и максимальное λ.</summary>
public sealed record ScadShellStage(string Tag, IReadOnlyList<(int LoadCase, double Factor)> Loads,
    double LoadFactorStep, double MaxLoadFactor = 1);

/// <summary>Сечение пластинчатого КЭ: <see cref="PlateSection"/> и ключ дедупа (одинаков у одинаковых сечений).</summary>
public sealed record ScadShellElementSection(PlateSection Section, string Key);

/// <summary>Вход сборки модели OpenSees из расчётной модели SCAD.</summary>
public sealed class ScadShellModelInput
{
    /// <summary>Схема SCAD: узлы, КЭ, жёсткости, оси (<see cref="ScadSchemaData.PlateAxisAngles"/>), расчётная модель.</summary>
    public required ScadSchemaData Data { get; init; }

    /// <summary>Сечение пластинчатого КЭ по номеру; null — КЭ пропускается (в отчёт).</summary>
    public required Func<int, ScadShellElementSection?> PlateSection { get; init; }

    /// <summary>Материалы слоёв пластин (нелинейные или упругие — <see cref="ElasticPlateMaterialResolver"/>).</summary>
    public required IPlateSectionShellMaterialResolver Resolver { get; init; }

    /// <summary>
    /// Fiber-сечение стержня по номеру КЭ (нелинейный стержень); null у всех — упругие стержни по жёсткости SCAD.
    /// Сечения с одинаковым ключом строятся один раз.
    /// </summary>
    public Func<int, (CrossSection Section, string Key)?>? BeamSection { get; init; }

    /// <summary>Материалы fiber-сечений стержней по id.</summary>
    public IReadOnlyDictionary<int, Material> BeamMaterials { get; init; } = new Dictionary<int, Material>();

    /// <summary>Вид расчёта для диаграмм fiber-сечений.</summary>
    public CalcType BeamCalc { get; init; } = CalcType.N;

    /// <summary>Опции fiber-сечений (FirstMaterialTag задаёт сборщик).</summary>
    public CrossSectionToOpenSeesAdapter.Options BeamOptions { get; init; } = new();

    /// <summary>Число точек интегрирования нелинейного стержня.</summary>
    public int BeamIntegrationPoints { get; init; } = 5;

    /// <summary>Стадии нагружения (история).</summary>
    public required IReadOnlyList<ScadShellStage> Stages { get; init; }

    /// <summary>Политика Newton-анализа; null — по умолчанию модели.</summary>
    public NonlinearAnalysisPolicy? Policy { get; init; }
}

/// <summary>Итог сборки: модель, суммарная вертикальная нагрузка стадий (Н, вниз — плюс), отчёт.</summary>
public sealed record ScadShellModelResult(ShellOpenSeesModel Model, IReadOnlyList<double> StageTotalDownN,
    IReadOnlyList<string> Report);

/// <summary>
/// Сборка <see cref="ShellOpenSeesModel"/> из импортированной схемы SCAD (спека «Плита SCAD → нелинейный OpenSees»,
/// срез 2): узлы с закреплениями, оболочки Q4/T3 (порядок SCAD «1 2 4 3» → обход контура) со слоистыми сечениями,
/// стержни (fiber или упругие), абсолютно жёсткие тела → rigidLink, нагрузки загружений → узловые силы стадий.
/// Теги узлов и КЭ — номера SCAD.
/// </summary>
public static class ScadShellModelAssembler
{
    static readonly Regex RoToken = new(@"\bRO\s+([-+0-9.eE]+)", RegexOptions.Compiled);
    static readonly Regex NuToken = new(@"\bNU\s+([-+0-9.eE]+)", RegexOptions.Compiled);

    public static ScadShellModelResult Assemble(ScadShellModelInput input)
    {
        ArgumentNullException.ThrowIfNull(input);
        var data = input.Data;
        var model = data.AnalysisModel ?? throw new CScoreMappingException(
            "У схемы SCAD нет расчётной модели (опоры, жёсткие тела, нагрузки) — перечитайте схему из .SPR.");
        var report = new List<string>();
        var stiff = data.Stiffnesses.ToDictionary(s => s.Id);
        double fu = model.ForceUnitN, lu = model.LengthUnitM;

        // Узлы.
        var nodes = data.Nodes.ToDictionary(n => n.Id);
        var used = new HashSet<int>();

        // Оболочки.
        var shellRequests = new List<(PlateSection Section, ShellFrame Frame, int SectionTag)>();
        var sectionTagByKey = new Dictionary<string, int>(StringComparer.Ordinal);
        var shellDraft = new List<(ScadElementRecord Element, int[] Contour, ShellElementKind Kind, ShellFrame Frame, int SectionTag)>();
        int noSection = 0;
        foreach (var e in data.Elements)
        {
            if (ScadElementKinds.Classify(e.TypeCode, e.NodeIds.Length) != ScadElementKind.Shell) continue;
            int[] contour = e.NodeIds.Length == 4 ? [e.NodeIds[0], e.NodeIds[1], e.NodeIds[3], e.NodeIds[2]] : e.NodeIds;
            if (input.PlateSection(e.Id) is not { } sec) { noSection++; continue; }
            var frame = Frame(contour.Select(id => nodes[id]).ToArray(), data.PlateAxisAngles.GetValueOrDefault(e.Id));
            string key = sec.Key + "|" + FrameKey(frame);
            if (!sectionTagByKey.TryGetValue(key, out int tag))
            {
                sectionTagByKey[key] = tag = sectionTagByKey.Count + 1;
                shellRequests.Add((sec.Section, frame, tag));
            }
            shellDraft.Add((e, contour, e.NodeIds.Length == 4 ? ShellElementKind.ASDShellQ4 : ShellElementKind.ASDShellT3, frame, tag));
            foreach (int id in contour) used.Add(id);
        }
        if (noSection > 0) report.Add($"Пропущено {noSection} пластин без сечения.");
        if (shellRequests.Count == 0) throw new CScoreMappingException("Нет пластин с сечениями.");

        var mapped = PlateSectionOpenSeesMapper.MapMany(shellRequests, input.Resolver);
        report.AddRange(mapped.Diagnostics);
        var sectionByTag = mapped.Sections.ToDictionary(s => s.Tag);
        var shells = shellDraft.Select(s => new NormalizedShellElement(s.Element.Id, s.Kind, s.Contour, s.SectionTag,
            sectionByTag[s.SectionTag].Fingerprint, s.Frame,
            ShellIntegrationPolicy.Full, $"scad:{s.Element.Id}")).ToList();

        // Стержни.
        int nextMaterialTag = mapped.Materials.Count == 0 ? 1 : mapped.Materials.Max(m => m.Tag) + 1;
        int nextSectionTag = sectionTagByKey.Count + 1;
        var beamSections = new Dictionary<int, OpenSeesSectionModel>();
        var beamSectionByKey = new Dictionary<string, int>(StringComparer.Ordinal);
        var nonlinearBeams = new List<FemNonlinearElement>();
        var linearBeams = new List<FemLinearElement>();
        foreach (var e in data.Elements)
        {
            if (ScadElementKinds.Classify(e.TypeCode, e.NodeIds.Length) != ScadElementKind.Beam) continue;
            var (ni, nj) = (nodes[e.NodeIds[0]], nodes[e.NodeIds[1]]);
            var vecxz = Vecxz(ni, nj);
            if (input.BeamSection?.Invoke(e.Id) is { } bs)
            {
                if (!beamSectionByKey.TryGetValue(bs.Key, out int sTag))
                {
                    var options = CopyOptions(input.BeamOptions, nextMaterialTag);
                    var built = CrossSectionToOpenSeesAdapter.Build(bs.Section, input.BeamCalc, input.BeamMaterials, null, options);
                    nextMaterialTag += built.Materials.Count;
                    beamSectionByKey[bs.Key] = sTag = nextSectionTag++;
                    beamSections[sTag] = built;
                }
                nonlinearBeams.Add(new FemNonlinearElement(e.Id, ni.Id, nj.Id, sTag, input.BeamIntegrationPoints, vecxz));
            }
            else if (ElasticBeam(e, stiff, fu, lu, vecxz, ni, nj) is { } lin) linearBeams.Add(lin);
            else { report.Add($"Стержень {e.Id}: нет сечения (жёсткость {e.StiffnessId} — не брус S0)."); continue; }
            used.Add(ni.Id);
            used.Add(nj.Id);
        }

        // Жёсткие тела.
        var links = new List<ShellRigidLinkConstraint>();
        foreach (var b in model.RigidBodies)
        {
            var type = b.Mask switch
            {
                0x3F => ShellRigidLinkType.Beam,
                0x07 => ShellRigidLinkType.Bar,
                _ => (ShellRigidLinkType?)null,
            };
            if (type == null) { report.Add($"Жёсткое тело {b.ElemId}: маска 0x{b.Mask:X} не поддерживается — пропущено."); continue; }
            foreach (int s in b.SlaveNodes) links.Add(new ShellRigidLinkConstraint(b.MasterNode, s, type.Value));
            used.Add(b.MasterNode);
            foreach (int s in b.SlaveNodes) used.Add(s);
        }

        var shellNodes = data.Nodes.Where(n => used.Contains(n.Id)).Select(n => new NormalizedShellNode(n.Id, n.X, n.Y, n.Z,
            Fixed(model.Bounds.GetValueOrDefault(n.Id)), $"scad:{n.Id}")).ToList();

        // Нагрузки.
        var loadByCase = new Dictionary<int, Dictionary<int, double>>();   // загружение → узел → Fz вниз, Н
        foreach (var lc in model.LoadCases)
            loadByCase[lc.Num] = NodalLoads(lc, data, stiff, nodes, fu, lu, report);
        var stages = new List<ShellNonlinearStage>();
        var totals = new List<double>();
        foreach (var st in input.Stages)
        {
            var sum = new Dictionary<int, double>();
            foreach (var (lc, factor) in st.Loads)
            {
                if (!loadByCase.TryGetValue(lc, out var f)) throw new CScoreMappingException($"Нет загружения {lc}.");
                foreach (var (node, fz) in f) sum[node] = sum.GetValueOrDefault(node) + factor * fz;
            }
            stages.Add(new ShellNonlinearStage
            {
                Tag = st.Tag, LoadFactorStep = st.LoadFactorStep, MaxLoadFactor = st.MaxLoadFactor,
                Loads = sum.Where(kv => used.Contains(kv.Key)).OrderBy(kv => kv.Key)
                    .Select(kv => new ShellNodalLoad(kv.Key, 0, 0, -kv.Value, 0, 0, 0)).ToList(),
            });
            totals.Add(sum.Where(kv => used.Contains(kv.Key)).Sum(kv => kv.Value));
        }

        var result = new ShellOpenSeesModel
        {
            Nodes = shellNodes,
            Materials = mapped.Materials,
            Sections = mapped.Sections,
            Elements = shells,
            NonlinearBeamSections = beamSections,
            NonlinearBeamElements = nonlinearBeams,
            BeamElements = linearBeams,
            RigidLinks = links,
            Stages = stages,
            Policy = input.Policy ?? new ShellOpenSeesModel().Policy,
        };
        result.Validate();
        return new ScadShellModelResult(result, totals, report);
    }

    /// <summary>
    /// Узловые силы загружения (Н, вниз — плюс): собственный вес (Qw 96: ρ·h·A пластин, ρ·A·l стержней) и давление на
    /// пластины (Qw 16, Qn 3 — по глобальной Z; положительное значение — вниз, как сумма нагрузок протокола SCAD).
    /// Пластины Q4 — согласованные силы билинейной интерполяции (Гаусс 2 × 2), T3 — A/3 на узел.
    /// </summary>
    public static Dictionary<int, double> NodalLoads(ScadLoadCase lc, ScadSchemaData data,
        IReadOnlyDictionary<int, ScadStiffnessRecord> stiff, IReadOnlyDictionary<int, ScadNodeRecord> nodes,
        double fu, double lu, List<string> report)
    {
        var f = new Dictionary<int, double>();
        var elems = data.Elements.ToDictionary(e => e.Id);
        var unsupported = new HashSet<string>();
        foreach (var r in lc.NodeLoads) unsupported.Add($"узловые Qw {r.Qw}");
        foreach (var r in lc.AreaLoads) unsupported.Add($"площадные Qw {r.Qw}");
        foreach (var r in lc.ElementLoads)
        {
            bool selfWeight = r.Qw == ScadAnalysisModel.SelfWeightQw;
            bool pressure = r.Qw == ScadAnalysisModel.PlatePressureQw && r.Qn == 3;
            if (!selfWeight && !pressure || r.Data.Length == 0) { unsupported.Add($"Qw {r.Qw} Qn {r.Qn}"); continue; }
            foreach (int id in r.Ids)
            {
                if (!elems.TryGetValue(id, out var e)) continue;
                var kind = ScadElementKinds.Classify(e.TypeCode, e.NodeIds.Length);
                var s = stiff.GetValueOrDefault(e.StiffnessId);
                if (kind == ScadElementKind.Shell)
                {
                    double q = pressure
                        ? r.Data[0] * fu / (lu * lu)
                        : r.Data[0] * Density(s, fu, lu) * (s?.ThicknessM ?? 0);
                    int[] contour = e.NodeIds.Length == 4 ? [e.NodeIds[0], e.NodeIds[1], e.NodeIds[3], e.NodeIds[2]] : e.NodeIds;
                    var w = AreaWeights(contour.Select(n => nodes[n]).ToArray());
                    for (int k = 0; k < contour.Length; k++) f[contour[k]] = f.GetValueOrDefault(contour[k]) + q * w[k];
                }
                else if (kind == ScadElementKind.Beam && selfWeight && s?.BarRect is { } rect)
                {
                    var (a, b) = (nodes[e.NodeIds[0]], nodes[e.NodeIds[1]]);
                    double l = Math.Sqrt(Math.Pow(b.X - a.X, 2) + Math.Pow(b.Y - a.Y, 2) + Math.Pow(b.Z - a.Z, 2));
                    double half = 0.5 * r.Data[0] * Density(s, fu, lu) * rect.WidthM * rect.HeightM * l;
                    f[a.Id] = f.GetValueOrDefault(a.Id) + half;
                    f[b.Id] = f.GetValueOrDefault(b.Id) + half;
                }
                else unsupported.Add($"Qw {r.Qw} на КЭ {kind}");
            }
        }
        foreach (string u in unsupported) report.Add($"Загружение {lc.Num}: нагрузка не поддерживается — {u}.");
        return f;
    }

    /// <summary>Объёмный вес из строки жёсткости (RO), Н/м³; нет — 0.</summary>
    static double Density(ScadStiffnessRecord? s, double fu, double lu) =>
        s?.Text is { } t && RoToken.Match(t) is { Success: true } m && Num(m.Groups[1].Value) is double ro
            ? ro * fu / (lu * lu * lu) : 0;

    /// <summary>Доли площади на узлы: Q4 — ∫Nᵢ dA (Гаусс 2 × 2), T3 — A/3.</summary>
    internal static double[] AreaWeights(ScadNodeRecord[] p)
    {
        ShellVector3 V(ScadNodeRecord n) => new(n.X, n.Y, n.Z);
        if (p.Length == 3)
        {
            double a = 0.5 * (V(p[1]) - V(p[0])).Cross(V(p[2]) - V(p[0])).Length;
            return [a / 3, a / 3, a / 3];
        }
        var w = new double[4];
        double g = 1 / Math.Sqrt(3);
        double[] xi = [-1, 1, 1, -1], eta = [-1, -1, 1, 1];
        foreach (double gx in (double[])[-g, g])
            foreach (double gy in (double[])[-g, g])
            {
                var dx = ShellVector3.Zero;
                var dy = ShellVector3.Zero;
                var n = new double[4];
                for (int k = 0; k < 4; k++)
                {
                    n[k] = 0.25 * (1 + xi[k] * gx) * (1 + eta[k] * gy);
                    dx += V(p[k]) * (0.25 * xi[k] * (1 + eta[k] * gy));
                    dy += V(p[k]) * (0.25 * eta[k] * (1 + xi[k] * gx));
                }
                double j = dx.Cross(dy).Length;
                for (int k = 0; k < 4; k++) w[k] += n[k] * j;
            }
        return w;
    }

    /// <summary>
    /// Frame КЭ: Ex — ось X1 (узел 1 → узел 2), повёрнутая на угол осей выдачи SCAD вокруг нормали; нормаль — по обходу.
    /// </summary>
    static ShellFrame Frame(ScadNodeRecord[] p, double angleDeg)
    {
        ShellVector3 V(ScadNodeRecord n) => new(n.X, n.Y, n.Z);
        var normal = (V(p[1]) - V(p[0])).Cross(V(p[^1]) - V(p[0])).Normalize();
        var x1 = (V(p[1]) - V(p[0])).Normalize();
        var y1 = normal.Cross(x1).Normalize();
        double a = angleDeg * Math.PI / 180;
        var ex = (x1 * Math.Cos(a) + y1 * Math.Sin(a)).Normalize();
        return new ShellFrame(Round(ex), Round(normal.Cross(ex).Normalize()), Round(normal));
    }

    static ShellVector3 Round(ShellVector3 v)
    {
        var r = new ShellVector3(Math.Round(v.X, 12), Math.Round(v.Y, 12), Math.Round(v.Z, 12));
        return r.Normalize();
    }

    static string FrameKey(ShellFrame f) => string.Join(",",
        new[] { f.Ex.X, f.Ex.Y, f.Ex.Z, f.Normal.X, f.Normal.Y, f.Normal.Z }.Select(v => v.ToString("F9", CultureInfo.InvariantCulture)));

    static (double, double, double) Vecxz(ScadNodeRecord a, ScadNodeRecord b)
    {
        var axis = new ShellVector3(b.X - a.X, b.Y - a.Y, b.Z - a.Z).Normalize();
        return Math.Abs(axis.X) > 0.9 ? (0, 1, 0) : (1, 0, 0);
    }

    static FemLinearElement? ElasticBeam(ScadElementRecord e, IReadOnlyDictionary<int, ScadStiffnessRecord> stiff,
        double fu, double lu, (double, double, double) vecxz, ScadNodeRecord a, ScadNodeRecord b)
    {
        if (stiff.GetValueOrDefault(e.StiffnessId) is not { BarRect: { } rect, Text: { } text }) return null;
        var parts = text.Split([' ', '\t'], StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 2 || Num(parts[1]) is not double e0) return null;
        double nu = NuToken.Match(text) is { Success: true } m && Num(m.Groups[1].Value) is double v ? v : 0.2;
        double E = e0 * fu / (lu * lu), w = rect.WidthM, h = rect.HeightM;
        double lo = Math.Min(w, h), hi = Math.Max(w, h);
        double j = lo * lo * lo * hi * (1.0 / 3 - 0.21 * lo / hi * (1 - Math.Pow(lo / hi, 4) / 12));
        return new FemLinearElement(e.Id, a.Id, b.Id, w * h, E, E / (2 * (1 + nu)), j, w * h * h * h / 12, h * w * w * w / 12, vecxz);
    }

    static CrossSectionToOpenSeesAdapter.Options CopyOptions(CrossSectionToOpenSeesAdapter.Options o, int firstTag) => new()
    {
        GJ = o.GJ, Convention = o.Convention, FirstMaterialTag = firstTag, ConsiderConcreteTension = o.ConsiderConcreteTension,
        MaterialSource = o.MaterialSource, MainMaterialModel = o.MainMaterialModel, SteelModel = o.SteelModel,
        SteelHardeningRatioOverride = o.SteelHardeningRatioOverride, SteelHardeningModulusPa = o.SteelHardeningModulusPa,
        ConsiderPhysicalNonlinearity = o.ConsiderPhysicalNonlinearity, Sp63EtaMin = o.Sp63EtaMin, EkbEtaMin = o.EkbEtaMin,
    };

    static bool[] Fixed(int mask) => Enumerable.Range(0, 6).Select(i => (mask & (1 << i)) != 0).ToArray();

    static double? Num(string s) =>
        double.TryParse(s.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out double v) ? v : null;
}

/// <summary>
/// Упругие слои пластин для линейной сверки: бетон — ElasticIsotropic (E, ν), арматура — упругая (Es) в PlateRebar.
/// </summary>
public sealed class ElasticPlateMaterialResolver(double e, double nu, double es = 2e11) : IPlateSectionShellMaterialResolver
{
    /// <inheritdoc />
    public IReadOnlyList<NativeShellMaterialDefinition> ResolveConcrete(int sourceMaterialId) =>
        [new NativeShellMaterialDefinition(1, $"elastic-concrete:{sourceMaterialId}", new ElasticIsotropicShellMaterialSpec(e, nu))];

    /// <inheritdoc />
    public IReadOnlyList<NativeShellMaterialDefinition> ResolveRebar(int sourceMaterialId) =>
    [
        new NativeShellMaterialDefinition(1, $"elastic-rebar:{sourceMaterialId}:uniaxial", new ElasticUniaxialShellMaterialSpec(es)),
        new NativeShellMaterialDefinition(2, $"elastic-rebar:{sourceMaterialId}:plate", new PlateRebarShellMaterialSpec(1, 0)),
    ];
}
