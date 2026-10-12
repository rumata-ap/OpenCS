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

        // Пружины: т/м → Н/м и т·м/рад → Н·м/рад (длины в метрах); КЭ 56 — как КЭ 57.
        double t = TonToKn * 1e3;
        Assert.Equal(3, r.Springs.Count);
        var s5 = r.Springs.Single(x => x.NodeTag == "5");
        Assert.Equal("3,4", s5.SourceElemTag);
        AssertClose([20 * t, 40 * t, 60 * t, 2 * t, 4 * t, 6 * t], s5.Stiffnesses);
        var s6 = r.Springs.Single(x => x.NodeTag == "6");
        Assert.Equal("5", s6.SourceElemTag);
        AssertClose([0, 0, 2000 * t, 0, 0, 5 * t], s6.Stiffnesses);
        Assert.Equal(FemSpringTargetKinds.MeshNode, s6.TargetKind);
        var s7 = r.Springs.Single(x => x.NodeTag == "7");
        Assert.Equal("6", s7.SourceElemTag);
        AssertClose([100 * t, 100 * t, 0, 0, 0, 0], s7.Stiffnesses);

        // АЖТ: номер — порядковый в документе 25, ведущий в ведомых не повторяется; −2 и тело без ведомых — пропущены.
        var body = Assert.Single(r.RigidBodies, b => !b.IsCoupled);
        Assert.Equal(("1", "1", FemBoundaryDofs.All), (body.MasterNodeTag, body.SourceElemTag, body.Mask));
        Assert.Equal(FemRigidBodyKinds.Rigid, body.Kind);
        Assert.Equal(["2", "3"], body.SlaveNodeTags);

        // Объединение «3 1 2 3 / 4 /»: ведущий — первый узел группы, равны X Y Z, номер — порядковый номер группы.
        var coupling = Assert.Single(r.RigidBodies, b => b.IsCoupled);
        Assert.Equal(("3", "1", 0b000111, "import:lira"),
            (coupling.MasterNodeTag, coupling.SourceElemTag, coupling.Mask, coupling.Origin));
        Assert.Equal(["4"], coupling.SlaveNodeTags);

        // Шарниры: конец 2 — DOF 4, 5 = T, My (биты 3, 4), конец 1 — N; упругий шарнир (поле 30) и пластина не переносятся.
        Assert.Equal(new FemElementBoundaryProps(0b000001, 0b011000, null), Assert.Single(r.ElementProps).Value);
        Assert.Equal("1", r.ElementProps.Keys.Single());

        var rows = r.Summary.ToDictionary(x => x.Kind);
        Assert.Equal((2, 1), (rows["Связи"].Transferred, rows["Связи"].NotTransferred));
        Assert.Equal((4, 2), (rows["Упругие связи (КЭ 51, 56, 57)"].Transferred, rows["Упругие связи (КЭ 51, 56, 57)"].NotTransferred));
        Assert.Equal((1, 3), (rows["Абсолютно жёсткие тела"].Transferred, rows["Абсолютно жёсткие тела"].NotTransferred));
        Assert.Equal((1, 1), (rows["Шарниры стержней"].Transferred, rows["Шарниры стержней"].NotTransferred));
        Assert.Equal((1, 0, "узлов: 2"), (rows["Объединения перемещений"].Transferred, rows["Объединения перемещений"].NotTransferred,
            rows["Объединения перемещений"].Note));
        Assert.Equal(1, rows["Упругие шарниры"].NotTransferred);
        Assert.False(rows.ContainsKey("КЭ 56 (связь)"));
        Assert.Equal(1, rows["КЭ 251 (физически нелинейная связь)"].NotTransferred);
        Assert.Equal(1, rows["Жёсткие вставки (документ 14)"].NotTransferred);
        Assert.False(rows.ContainsKey("Упругое основание пластин (C1)"));

        Assert.StartsWith("Перенесено из ЛИРЫ граничных условий: связей 2, пружин 3, жёстких тел 1, объединений перемещений 1, " +
            "стержней с шарнирами 1 (концов 2).", r.Report[0]);
        Assert.DoesNotContain(r.Report, l => l.Contains("объединения перемещений", StringComparison.OrdinalIgnoreCase)
            && l.StartsWith("Не перенесено"));
        Assert.DoesNotContain(r.Report, l => l.StartsWith("Не перенесено: КЭ 56"));
    }

    [Fact]
    public void Couplings_NodesOutsideMeshDropped_GroupsWithOneNodeSkipped()
    {
        // Группы: 5 (X) + 6, 99 → 5–6; 7 (Z UX) + 98 → один узел в сетке, пропуск; 99 (Y) + 1, 2 → ведущий — 1.
        string text = Schema.Replace("""
            3 1 2 3 /
            4 /
            """, """
            5 1 /
            6 /99 /
            7 3 4 /
            98 /
            99 2 /
            1 /2 /
            """);
        var s = Parse(text);
        Assert.Equal(3, s.Couplings.Count);

        var r = LiraBoundaryTransfer.Transfer(s, Nodes, Types, TonToKn);

        var couplings = r.RigidBodies.Where(b => b.IsCoupled).ToList();
        Assert.Equal([("5", "1", 0b000001), ("1", "3", 0b000010)],
            couplings.Select(c => (c.MasterNodeTag, c.SourceElemTag, c.Mask)));
        Assert.Equal(["6"], couplings[0].SlaveNodeTags);
        Assert.Equal(["2"], couplings[1].SlaveNodeTags);
        var row = r.Summary.Single(x => x.Kind == "Объединения перемещений");
        Assert.Equal((2, 1), (row.Transferred, row.NotTransferred));
        Assert.Contains(r.Report, l => l == "Объединения перемещений: узлов нет в сетке — 3, исключены из групп.");
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
    public void FoundationC1_FromDocument19_OverridesPassed_C2AndBarsReported()
    {
        // Документ 19: пластина 2 — C1 = 2393,08 т/м³ и C2; стержень 1 (ширина, C1) — не переносится.
        var s = Parse(Schema + "( 19/ 2 2393.08 153.7 153.7 0 0/ 1 0.6 500 0 0 0/ )");
        var passed = new Dictionary<string, double> { ["2"] = 1 };

        var r = LiraBoundaryTransfer.Transfer(s, Nodes, Types, TonToKn, passed);

        Assert.Equal(2393.08 * TonToKn * 1e3, r.ElementProps["2"].FoundationC1!.Value, 3);
        var row = r.Summary.Single(x => x.Kind == "Упругое основание пластин (C1)");
        Assert.Equal((1, 1), (row.Transferred, row.NotTransferred));
        Assert.Contains(r.Report, l => l.Contains("C2 упругого основания (документ 19), пластин — 1"));
        Assert.Contains(r.Report, l => l.Contains("упругое основание не пластин (документ 19), записей — 1"));
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
    [InlineData(56, new[] { 100.0, 100, 0, 0, 0, 0 }, new[] { 100.0, 100, 0, 0, 0, 0 })]
    public void SpringStiffness_Parsed(int type, double[] values, double[] expected) =>
        Assert.Equal(expected, LiraBoundaryTransfer.SpringStiffness(type, values));

    [Theory]
    [InlineData(57, new[] { 1.0, 2, 3 })]
    [InlineData(51, new[] { 3.0 })]
    [InlineData(51, new[] { 0.0, 1 })]
    [InlineData(51, new[] { 2.5, 1 })]
    [InlineData(56, new[] { 1.0, 2, 3 })]
    [InlineData(55, new[] { 1.0, 2, 3, 4, 5, 6 })]
    public void SpringStiffness_Rejected(int type, double[] values) =>
        Assert.Null(LiraBoundaryTransfer.SpringStiffness(type, values));
}
