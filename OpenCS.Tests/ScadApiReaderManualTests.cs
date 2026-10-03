using System.Diagnostics;
using System.IO;
using CScore.Import;
using OpenCS.Services.Scad;
using Xunit;
using Xunit.Abstractions;

namespace OpenCS.Tests;

/// <summary>
/// Ручной прогон чтения .SPR через SCADAPIX.dll на модели «Краеведческий музей_сваи» (срез 0, 01.10).
/// Переменные окружения: OPENCS_SCAD_SPR — путь к модели, OPENCS_SCAD_DIR — каталог DLL (необязательно,
/// иначе поиск установки), OPENCS_SCAD_WORK — рабочий каталог (необязательно). Без OPENCS_SCAD_SPR тест
/// сразу выходит.
/// </summary>
public class ScadApiReaderManualTests(ITestOutputHelper output)
{
    [Fact]
    public void ReadMuseumModel()
    {
        string? spr = Environment.GetEnvironmentVariable("OPENCS_SCAD_SPR");
        if (string.IsNullOrWhiteSpace(spr)) return;
        string dir = Environment.GetEnvironmentVariable("OPENCS_SCAD_DIR") is { Length: > 0 } d
            ? d
            : ScadInstallLocator.FindDllDirectory() ?? throw new InvalidOperationException("SCAD не найден");
        output.WriteLine($"DLL: {dir}");

        var native = ScadApiNative.Load(dir);
        var sw = Stopwatch.StartNew();
        ScadApiReadResult r;
        using (var s = new ScadApiSession(native))
        {
            s.Open(spr);
            output.WriteLine($"Открытие: {sw.ElapsedMilliseconds} мс");
            r = ScadApiReader.Read(s, new ScadReadOptions(), null, CancellationToken.None);
        }
        output.WriteLine($"Открытие + чтение: {sw.ElapsedMilliseconds} мс");
        var data = r.Data;

        output.WriteLine($"Узлов {data.Nodes.Count}, КЭ {data.Elements.Count}, удалено КЭ {r.DeletedElements}, " +
            $"пропущено: {string.Join(", ", r.SkippedByType.Select(kv => $"{kv.Key}×{kv.Value}"))}");
        output.WriteLine($"Жёсткостей {data.Stiffnesses.Count}, групп {data.Groups.Count}, блоков {data.Blocks.Count}, " +
            $"ЖБ {data.ConcreteGroups.Count}, углов {data.PlateAxisAngles.Count}, вырожденных осей {r.DegenerateAxisElements}");
        output.WriteLine($"Стержни без бруса: {string.Join(", ", r.BarStiffnessesWithoutShape)}");
        foreach (var g in data.ConcreteGroups)
            output.WriteLine($"ЖБ {g.Num} «{g.Name}»: a = {string.Join("/", g.RangeM)} м, {g.ConcreteClass}, " +
                $"{g.LongitudinalRebarClass}/{g.TransverseRebarClass}, КЭ {g.ElementIds.Length}");
        foreach (var st in data.Stiffnesses)
            output.WriteLine($"Жёсткость {st.Id} «{st.Name}» {st.Kind} h={st.ThicknessM} rect={st.BarRect}");
        var angles = data.PlateAxisAngles.Values.GroupBy(a => Math.Round(a, 1)).OrderByDescending(g => g.Count()).Take(8);
        output.WriteLine("Углы: " + string.Join(", ", angles.Select(g => $"{g.Key}°×{g.Count()}")));

        Assert.Equal(112525, data.Nodes.Count);
        Assert.Equal(0, r.DeletedNodes);
        Assert.Equal(8, r.DeletedElements);
        Assert.Equal(126639 - 8, data.Elements.Count + r.SkippedByType.Values.Sum());
        Assert.Equal(1.0, data.LengthUnitM);
        Assert.Equal(1.0, data.SectionUnitM);

        var node1 = data.Nodes.Single(nd => nd.Id == 1);
        Assert.Equal(14.072, node1.X, 3);
        Assert.Equal(35.882, node1.Y, 3);
        Assert.Equal(22.59, node1.Z, 3);

        // КЭ 55459: узлы SCAD (0;0) (0,46;0) (0;0,44) (0,46;0,44) сохраняются как есть; контур — n1→n2→n4→n3,
        // как его обходит 3D-вид (Fem3DVM.BuildShellEdges).
        var e = data.Elements.Single(x => x.Id == 55459);
        Assert.Equal([66373, 66958, 66385, 47706], e.NodeIds);
        var pts = new[] { 0, 1, 3, 2 }.Select(k => data.Nodes.Single(nd => nd.Id == e.NodeIds[k])).ToArray();
        double area = 0;
        for (int i = 0; i < 4; i++)
        {
            var p = pts[i]; var q = pts[(i + 1) % 4];
            area += (p.X - pts[0].X) * (q.Y - pts[0].Y) - (q.X - pts[0].X) * (p.Y - pts[0].Y);
        }
        // У «бабочки» площадь по Гауссу ≈ 0, у контура ≈ 0,46 × 0,44.
        Assert.InRange(Math.Abs(area / 2), 0.46 * 0.44 * 0.9, 0.46 * 0.44 * 1.1);

        Assert.Equal(new LiraBarRect(0.6, 0.6), data.Stiffnesses.Single(x => x.Id == 6).BarRect);
        Assert.Equal(16726, data.Groups.Single(g => g.Name == "Покрытие").ElementIds.Length);
        Assert.Equal(0.04, data.ConcreteGroups.Single(g => g.Name == "колонны").RangeM[0], 6);
        Assert.Equal(27, data.Stiffnesses.Count);
    }

    /// <summary>
    /// Заданное армирование (срез 4): печать групп пластин и стержней модели OPENCS_SCAD_ARM_SPR — для сверки
    /// с окном SCAD. Без переменной тест ничего не делает.
    /// </summary>
    [Fact]
    public void ReadAssignedRebar()
    {
        string? spr = Environment.GetEnvironmentVariable("OPENCS_SCAD_ARM_SPR");
        if (string.IsNullOrWhiteSpace(spr)) return;
        string dir = Environment.GetEnvironmentVariable("OPENCS_SCAD_DIR") ?? ScadInstallLocator.FindDllDirectory()!;
        using var s = new ScadApiSession(ScadApiNative.Load(dir));
        s.Open(spr);

        var file = ScadApiReader.ReadAssignedRebar(s);
        foreach (var g in file.Plates)
            output.WriteLine($"Пластины {g.Num} «{g.Name}», КЭ {g.ElementIds.Length} ({string.Join(",", g.ElementIds.Take(5))}…): " +
                $"⌀ {string.Join("/", g.DiametersMm)} шаг {string.Join("/", g.StepsM)}; поперечная ⌀{g.TransverseDiameterMm} " +
                $"{g.TransverseStepXM}×{g.TransverseStepYM}; площади {string.Join("/", Enumerable.Range(0, 4).Select(i => g.Area(i).ToString("0.###")))} см²/м");
        foreach (var g in file.Rods)
        {
            output.WriteLine($"Стержни {g.Num} «{g.Name}», КЭ {g.ElementIds.Length} ({string.Join(",", g.ElementIds.Take(5))}…), участков {g.Parts.Length}");
            foreach (var p in g.Parts)
                output.WriteLine($"  уч.{p.PartNo} {p.LengthPercent}%: S1 {p.S1}; S2 {p.S2}; S3 {p.S3}; S4 {p.S4}; " +
                    $"Z {p.StirrupsZ}; Y {p.StirrupsY}; Σ {p.LongitudinalSum:0.###} см²");
        }
        foreach (var raw in ScadApiReader.ReadArmPlateRecords(s))
            output.WriteLine("ApiArmElemPlate[0..80]: " + Convert.ToHexString(raw, 0, 80));
        output.WriteLine($"КЭ в нескольких группах: {file.MultiGroupElements}");
        Assert.False(file.IsEmpty);
    }

    /// <summary>
    /// Стальные группы (срез 3 сечений стержней): модель OPENCS_SCAD_STEEL_SPR = 111.SPR — группы «Связи» и
    /// «Балки» (пробник 03.10). Без переменной тест ничего не делает.
    /// </summary>
    [Fact]
    public void ReadSteelGroups()
    {
        string? spr = Environment.GetEnvironmentVariable("OPENCS_SCAD_STEEL_SPR");
        if (string.IsNullOrWhiteSpace(spr)) return;
        string dir = Environment.GetEnvironmentVariable("OPENCS_SCAD_DIR") ?? ScadInstallLocator.FindDllDirectory()!;
        using var s = new ScadApiSession(ScadApiNative.Load(dir));
        s.Open(spr);

        var groups = ScadApiReader.ReadSteelGroups(s);
        foreach (var g in groups)
            output.WriteLine($"Сталь {g.Num} «{g.Name}», КЭ {g.ElementIds.Length}: {g.SteelMark}, γc {g.GammaC}, γn {g.GammaN}, " +
                $"μ {g.MuXoZ}/{g.MuYoZ}, l {g.LengthXoZ}/{g.LengthYoZ}, λu {g.CompressionLimit}−{g.CompressionLimitAlpha}α/{g.TensionLimit}, " +
                $"шаг {g.StepOutPlane?.ToString() ?? $"{g.StepOutPlaneRatio}·l"}, конструктивный элемент {g.IsMember}, тип {g.ConstructionType}");

        Assert.Equal(2, groups.Count);
        var ties = groups.Single(g => g.Name == "Связи");
        var beams = groups.Single(g => g.Name == "Балки");
        Assert.Equal(31, ties.ElementIds.Length);
        Assert.Equal(33, beams.ElementIds.Length);
        Assert.Equal("C255", beams.SteelMark);
        Assert.Equal(0.8, beams.GammaC, 9);
        Assert.Equal(2, beams.MuXoZ, 9);
        Assert.Equal(0.5, beams.StepOutPlane!.Value, 9);
        Assert.Null(ties.StepOutPlane);
        Assert.Equal(1, ties.StepOutPlaneRatio, 9);
        Assert.Equal(180, ties.CompressionLimit, 9);
        Assert.Equal(60, ties.CompressionLimitAlpha, 9);
        Assert.Equal(400, ties.TensionLimit, 9);
    }

    /// <summary>
    /// Усилия, комбинации и РСУ (срез 2): КЭ 814 (стержень) и 55459 (пластина) — числа пробника p9–p11,
    /// затем время чтения группы «Покрытие».
    /// </summary>
    [Fact]
    public void ReadForcesMuseumModel()
    {
        string? spr = Environment.GetEnvironmentVariable("OPENCS_SCAD_SPR");
        if (string.IsNullOrWhiteSpace(spr)) return;
        string dir = Environment.GetEnvironmentVariable("OPENCS_SCAD_DIR") ?? ScadInstallLocator.FindDllDirectory()!;
        string? work = Environment.GetEnvironmentVariable("OPENCS_SCAD_WORK") ?? ScadInstallLocator.FindWorkDirectory();
        var native = ScadApiNative.Load(dir);
        using var s = new ScadApiSession(native);
        s.Open(spr);

        var two = new Dictionary<int, ScadElementKind> { [814] = ScadElementKind.Beam, [55459] = ScadElementKind.Shell };
        var lc = ScadApiForceReader.Read(s, work, two, ScadForceReadKind.LoadCases, null, CancellationToken.None);
        output.WriteLine("Загружения: " + string.Join(" | ", lc.Catalog.LoadNames));
        Assert.Equal(2, lc.Forces.Count);
        var bar = lc.Forces.Single(f => f.ElemId == 814);
        output.WriteLine($"814: типы {string.Join(",", bar.Types)}, точек {bar.Points}, загр. {bar.LoadCount}, " +
            $"с1 з1: {string.Join("; ", bar.LoadCase(0, 0).ToArray().Select(v => v.ToString("0.###")))}");
        Assert.Equal(-24.31, bar.LoadCase(0, 0)[bar.Types.AsSpan().IndexOf(ScadApiForceMapper.BarN)], 2);
        var plate = lc.Forces.Single(f => f.ElemId == 55459);
        output.WriteLine($"55459: типы {string.Join(",", plate.Types)}, точек {plate.Points}, " +
            $"з1: {string.Join("; ", plate.LoadCase(0, 0).ToArray().Select(v => v.ToString("0.###")))}");
        Assert.Equal(lc.Catalog.LoadNames.Count, bar.LoadCount);
        Assert.Empty(bar.Combinations);

        var comb = ScadApiForceReader.Read(s, work, two, ScadForceReadKind.Combinations, null, CancellationToken.None);
        output.WriteLine("Комбинации: " + string.Join(" | ", comb.Catalog.CombinationNames));
        var barC = comb.Forces.Single(f => f.ElemId == 814);
        Assert.True(barC.CombinationCount > 0);
        Assert.Equal(barC.CombinationCount, comb.Catalog.CombinationNames.Count);
        Assert.StartsWith("C1", comb.Catalog.CombinationNames[0]);

        var rsu = ScadApiForceReader.Read(s, work, two, ScadForceReadKind.Rsu, null, CancellationToken.None);
        var barR = rsu.Rsu.Single(r => r.ElemId == 814);
        var first = barR.Rows[0];
        output.WriteLine($"РСУ 814: строк {barR.Rows.Count}, первая: т{first.Point} гр{first.Group} кр{first.Criterion} " +
            string.Join("; ", first.Us.Select(v => v.ToString("0.###"))));
        Assert.Equal(0, first.Group);
        Assert.Equal(-35.487, first.Us[barR.Types.AsSpan().IndexOf(ScadApiForceMapper.BarN)], 2);

        // Чужой КЭ (другого вида) и несуществующий — в счётчики; больше половины — «другой проект».
        var wrong = new Dictionary<int, ScadElementKind> { [814] = ScadElementKind.Shell, [55459] = ScadElementKind.Shell, [9_999_999] = ScadElementKind.Shell };
        var ex = Assert.Throws<ScadApiException>(() =>
            ScadApiForceReader.Read(s, work, wrong, ScadForceReadKind.LoadCases, null, CancellationToken.None));
        Assert.Equal("ScadApiForcesOtherProject", ex.ResourceKey);

        // Время на группу «Покрытие».
        var schema = ScadApiReader.Read(s, new ScadReadOptions(OutputAxes: false, ConcreteGroups: false), null, CancellationToken.None);
        var kinds = schema.Data.Elements.ToDictionary(e => e.Id, e => ScadElementKinds.Classify(e.TypeCode, e.NodeIds.Length));
        var roof = schema.Data.Groups.Single(g => g.Name == "Покрытие").ElementIds
            .Where(kinds.ContainsKey).ToDictionary(id => id, id => kinds[id]);
        foreach (var kind in new[] { ScadForceReadKind.LoadCases, ScadForceReadKind.Combinations, ScadForceReadKind.Rsu })
        {
            var sw = Stopwatch.StartNew();
            var r = ScadApiForceReader.Read(s, work, roof, kind, null, CancellationToken.None);
            long readMs = sw.ElapsedMilliseconds;
            output.WriteLine($"  чтение {readMs} мс, строк РСУ {r.Rsu.Sum(x => x.Rows.Count)}");
            var sets = kind switch
            {
                ScadForceReadKind.LoadCases => ScadForceSetBuilder.LoadCases(r.Forces, r.Catalog, 1, "Покрытие", new ScadXlsImportOptions()),
                ScadForceReadKind.Combinations => ScadForceSetBuilder.Combinations(r.Forces, r.Catalog, 1, "Покрытие", new ScadXlsImportOptions()),
                _ => ScadForceSetBuilder.Rsu(r.Rsu, 1, "Покрытие", new ScadXlsImportOptions()),
            };
            output.WriteLine($"Покрытие {kind}: КЭ {roof.Count}, без результатов {r.NoResultElements}, нет {r.MissingElements}, " +
                $"другой вид {r.WrongKindElements}; наборов {sets.Count} ({string.Join(", ", sets.Select(x => $"«{x.Tag}» {x.Items.Count + x.ShellItems.Count}"))}), {sw.ElapsedMilliseconds} мс");
            Assert.NotEmpty(sets);
        }
    }

    [Fact]
    public void SummaryAndRepeatedSessions()
    {
        string? spr = Environment.GetEnvironmentVariable("OPENCS_SCAD_SPR");
        if (string.IsNullOrWhiteSpace(spr)) return;
        string dir = Environment.GetEnvironmentVariable("OPENCS_SCAD_DIR") ?? ScadInstallLocator.FindDllDirectory()!;
        string? work = Environment.GetEnvironmentVariable("OPENCS_SCAD_WORK") ?? ScadInstallLocator.FindWorkDirectory();
        output.WriteLine($"DLL: {dir}, рабочий каталог: {work}");

        var native = ScadApiNative.Load(dir);
        Assert.Same(native, ScadApiNative.Load(dir));
        for (int k = 0; k < 2; k++)
        {
            using var s = new ScadApiSession(native);
            s.Open(spr);
            var sum = ScadApiReader.ReadSummary(s, work);
            output.WriteLine(sum.ToString());
            Assert.Equal(112525, sum.Nodes);
            Assert.Equal(27, sum.Stiffnesses);
            Assert.Equal(2, sum.Groups);
            Assert.Equal(2, sum.ConcreteGroups);
            Assert.Equal(2, sum.AxisSystems);
        }

        var missing = Assert.Throws<ScadApiException>(() =>
        {
            using var s = new ScadApiSession(native);
            s.Open(Path.Combine(Path.GetTempPath(), "нет такого.SPR"));
        });
        Assert.Equal("ScadApiProjectNotFound", missing.ResourceKey);
    }
}
