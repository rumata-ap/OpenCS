using CScore.Fem;
using CScore.Import;
using Xunit;

namespace CScore.Tests.Import;

/// <summary>Перенос ГУ из текстового файла ЛИРЫ в сеточный уровень схемы (срез 2 плана 4б ЛИРЫ + 4ж).</summary>
public sealed class LiraBoundaryTransferTests
{
    const double TonToKn = 9.80665;

    static readonly HashSet<string> Nodes = ["1", "2", "3", "4", "5", "6", "7"];

    static readonly Dictionary<string, string> Types = new() { ["1"] = "beam", ["2"] = "shell" };

    static LiraTextSchema Parse(string text) => LiraTextReader.Read(new StringReader(text));

    static void AssertClose(double[] expected, double[] actual)
    {
        Assert.Equal(expected.Length, actual.Length);
        for (int i = 0; i < expected.Length; i++) Assert.Equal(expected[i], actual[i], 1e-9 * Math.Max(1, Math.Abs(expected[i])));
    }

    /// <summary>
    /// КЭ: 1 — стержень, 2 — пластина, 3 и 4 — КЭ 57 на узле 5 (складываются), 5 — КЭ 51 на узле 6, 6 — КЭ 56,
    /// 7 — КЭ 57 на узле вне сетки, 8 — КЭ 51 с DOF 7 (не распознан), 9 — КЭ 251.
    /// </summary>
    const string Schema = """
        ( 0/ 1; synthetic/ 2; 5/
        33;M 1 CM 100 T 1 C 1 /
        )
        ( 1/
        10 1 1 2 /44 2 1 2 3 4 /57 3 5 /57 3 5 /51 4 6 /56 6 7 /57 3 99 /51 5 6 /251 6 7 /
        )
        ( 2/
        1 2 4 0/1 2 5 0/1 1 1 0/
        1 1 6 30/
        2 1 5 0/
        )
        ( 3/
        1 GEI 3.06e+06 0.2 0.25 RO 2.5 /
        2 GEI 3.06e+06 0.2 0.2 RO 2.5 /
        3 10 20 30 1 2 3 /
        4 3 2000 6 5 /
        5 7 1 /
        6 100 100 0 0 0 0 /
        )
        ( 4/
        0 0 0 /6 0 0 /6 6 0 /0 6 0 /0 0 -1 /6 0 -1 /6 6 -1 /
        )
        ( 5/
        1 1 2 3 /
        2 3 /2 6 /
        99 1 /
        3 1 2 3 /
        4 /
        )
        ( 14/
        1 0 0 0 0 0 -0.15 -0.15 /
        )
        ( 25/
        1 2 3 1 -1/
        4 5 -2/
        98 1 -1/
        7 99 -1/
        )
        """;

    [Fact]
    public void Synthetic_MapsAllKinds()
    {
        var s = Parse(Schema);
        Assert.Empty(s.Warnings);

        var r = LiraBoundaryTransfer.Transfer(s, Nodes, Types, TonToKn);

        // Связи: записи узла 2 объединены, узел 99 — вне сетки.
        Assert.Equal([("1", 0b000111), ("2", 0b100100)], r.Supports.Select(x => (x.NodeTag, x.Mask)));
        Assert.All(r.Supports, x => Assert.Equal("import:lira", x.Origin));

        // Пружины: т/м → Н/м и т·м/рад → Н·м/рад (длины в метрах).
        double t = TonToKn * 1e3;
        Assert.Equal(2, r.Springs.Count);
        var s5 = r.Springs.Single(x => x.NodeTag == "5");
        Assert.Equal("3,4", s5.SourceElemTag);
        AssertClose([20 * t, 40 * t, 60 * t, 2 * t, 4 * t, 6 * t], s5.Stiffnesses);
        var s6 = r.Springs.Single(x => x.NodeTag == "6");
        Assert.Equal("5", s6.SourceElemTag);
        AssertClose([0, 0, 2000 * t, 0, 0, 5 * t], s6.Stiffnesses);
        Assert.Equal(FemSpringTargetKinds.MeshNode, s6.TargetKind);

        // АЖТ: номер — порядковый в документе 25, ведущий в ведомых не повторяется; −2 и тело без ведомых — пропущены.
        var body = Assert.Single(r.RigidBodies);
        Assert.Equal(("1", "1", FemBoundaryDofs.All), (body.MasterNodeTag, body.SourceElemTag, body.Mask));
        Assert.Equal(["2", "3"], body.SlaveNodeTags);

        // Шарниры: конец 2 — DOF 4, 5 = T, My (биты 3, 4), конец 1 — N; упругий шарнир (поле 30) и пластина не переносятся.
        Assert.Equal(new FemElementBoundaryProps(0b000001, 0b011000, null), Assert.Single(r.ElementProps).Value);
        Assert.Equal("1", r.ElementProps.Keys.Single());

        var rows = r.Summary.ToDictionary(x => x.Kind);
        Assert.Equal((2, 1), (rows["Связи"].Transferred, rows["Связи"].NotTransferred));
        Assert.Equal((3, 2), (rows["Упругие связи (КЭ 51, 57)"].Transferred, rows["Упругие связи (КЭ 51, 57)"].NotTransferred));
        Assert.Equal((1, 3), (rows["Абсолютно жёсткие тела"].Transferred, rows["Абсолютно жёсткие тела"].NotTransferred));
        Assert.Equal((1, 1), (rows["Шарниры стержней"].Transferred, rows["Шарниры стержней"].NotTransferred));
        Assert.Equal(1, rows["Упругие шарниры"].NotTransferred);
        Assert.Equal(1, rows["Объединения перемещений"].NotTransferred);
        Assert.Equal(1, rows["КЭ 56 (упругая связь)"].NotTransferred);
        Assert.Equal(1, rows["КЭ 251 (физически нелинейная связь)"].NotTransferred);
        Assert.Equal(1, rows["Жёсткие вставки (документ 14)"].NotTransferred);
        Assert.False(rows.ContainsKey("Упругое основание пластин (C1)"));

        Assert.StartsWith("Перенесено из ЛИРЫ граничных условий: связей 2, пружин 2, жёстких тел 1, стержней с шарнирами 1 (концов 2).",
            r.Report[0]);
        Assert.Contains(r.Report, l => l.StartsWith("Не перенесено: объединения перемещений — групп 1 (узлов 2)"));
    }

    [Fact]
    public void FoundationC1_KeptForShellsAndMergedWithReleases()
    {
        var s = Parse(Schema);
        var c1 = new Dictionary<string, double> { ["2"] = 5e7, ["1"] = 1e7, ["404"] = 1e7, ["3"] = 0 };

        var r = LiraBoundaryTransfer.Transfer(s, Nodes, Types, TonToKn, c1);

        Assert.Equal(new FemElementBoundaryProps(null, null, 5e7), r.ElementProps["2"]);
        // C1 на стержне не ставится, освобождения стержня остаются.
        Assert.Equal(new FemElementBoundaryProps(0b000001, 0b011000, null), r.ElementProps["1"]);
        var row = r.Summary.Single(x => x.Kind == "Упругое основание пластин (C1)");
        Assert.Equal(1, row.Transferred);
    }

    [Fact]
    public void Units_ForceAndLengthFromDocument0()
    {
        // Силы в кН (точно 1000 Н), длины в см: т/м → кН/см = 1e5 Н/м, кН·см = 10 Н·м.
        string text = Schema.Replace("33;M 1 CM 100 T 1 C 1 /", "33;CM 100 MM 1000 KN 9.81 C 1 /");
        var s = Parse(text);

        var r = LiraBoundaryTransfer.Transfer(s, Nodes, Types, TonToKn);

        var s5 = r.Springs.Single(x => x.NodeTag == "5");
        Assert.Equal(20 * 1e5, s5.Kx, 6);
        Assert.Equal(2 * 10, s5.Kux, 9);
    }

    [Theory]
    [InlineData(57, new[] { 1.0, 2, 3, 4, 5, 6, 7 }, new[] { 1.0, 2, 3, 4, 5, 6 })]
    [InlineData(51, new[] { 3.0, 2000, 3, 1 }, new[] { 0.0, 0, 2001, 0, 0, 0 })]
    [InlineData(51, new[] { 1.0, 10 }, new[] { 10.0, 0, 0, 0, 0, 0 })]
    public void SpringStiffness_Parsed(int type, double[] values, double[] expected) =>
        Assert.Equal(expected, LiraBoundaryTransfer.SpringStiffness(type, values));

    [Theory]
    [InlineData(57, new[] { 1.0, 2, 3 })]
    [InlineData(51, new[] { 3.0 })]
    [InlineData(51, new[] { 0.0, 1 })]
    [InlineData(51, new[] { 2.5, 1 })]
    [InlineData(56, new[] { 1.0, 2, 3, 4, 5, 6 })]
    public void SpringStiffness_Rejected(int type, double[] values) =>
        Assert.Null(LiraBoundaryTransfer.SpringStiffness(type, values));
}
