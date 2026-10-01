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
