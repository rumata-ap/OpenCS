using CScore.Import;
using Xunit;

namespace CScore.Tests.Import;

/// <summary>Маппер усилий SCADAPIX.dll по кодам TypeUs и построитель наборов.</summary>
public class ScadForceSetBuilderTests
{
    static readonly ScadXlsImportOptions Opt = new() { TonToKnFactor = 10 };

    // Коды стержня модели музея (КЭ 814): N MK MY QZ MZ QY rx ry rz.
    static readonly byte[] BarTypes = [0, 1, 4, 5, 6, 7, 23, 59, 60];
    // Коды пластины (КЭ 55459): NX NY TXY MX MY MXY QX QY RZ RX RY.
    static readonly byte[] ShellTypes = [8, 9, 11, 14, 15, 16, 17, 18, 20, 21, 22];

    [Fact]
    public void MapBar_ByCodesNotPositions()
    {
        // Порядок кодов переставлен, MK отсутствует.
        byte[] types = [6, 0, 7, 4, 5];
        double[] v = [3, -2, 4, 1, 5];   // Mz=3, N=-2, Qy=4, My=1, Qz=5

        var i = ScadApiForceMapper.MapBar(types, v, Opt);

        Assert.Equal(-20, i.N);
        Assert.Equal(0, i.T);
        Assert.Equal(-10, i.Mx);  // Mx ← My, инверсия
        Assert.Equal(-30, i.My);  // My ← Mz, инверсия
        Assert.Equal(50, i.Vy);   // Vy ← Qz
        Assert.Equal(40, i.Vx);   // Vx ← Qy
    }

    [Fact]
    public void MapShell_StressesAndMoments()
    {
        double[] v = [1.4, 1.1, 1.2, -0.09, -0.07, -0.15, 0.06, -0.04, 9, 9, 9];

        var i = ScadApiForceMapper.MapShell(ShellTypes, v, Opt);

        Assert.Equal(14, i.SigmaX!.Value, 9);
        Assert.Equal(11, i.SigmaY!.Value, 9);
        Assert.Equal(12, i.TauXY!.Value, 9);
        Assert.Equal(0.9, i.Mx, 9);   // плюс SCAD — растяжение низа → инверсия
        Assert.Equal(0.7, i.My, 9);
        Assert.Equal(1.5, i.Mxy, 9);
        Assert.Equal(0.6, i.Qx, 9);
        Assert.Equal(-0.4, i.Qy, 9);
    }

    [Fact]
    public void LoadCases_LayoutPointLoadForce_NamesAndLabels()
    {
        // Стержень: 3 сечения × 2 загружения × 9 усилий; N = 100·сечение + загружение.
        var us = new double[3 * 2 * BarTypes.Length];
        for (int p = 0; p < 3; p++)
            for (int l = 0; l < 2; l++)
                us[(p * 2 + l) * BarTypes.Length] = 100 * (p + 1) + (l + 1);
        var bar = new ScadElementForces(814, ScadElementKind.Beam, BarTypes, 3, us, 2, [], 0);
        var catalog = new ScadResultCatalog(["СОБСТВЕННЫЙ ВЕС ", ""], []);

        var sets = ScadForceSetBuilder.LoadCases([bar], catalog, schemaId: 5, "Колонны", Opt);

        Assert.Equal(["Колонны — СОБСТВЕННЫЙ ВЕС", "Колонны — Загружение 2"], sets.Select(s => s.Tag));
        Assert.All(sets, s => { Assert.Equal("fea", s.SourceType); Assert.Equal(5, s.SourceSchemaId); Assert.Equal("bar", s.Kind); });
        var second = sets[1].Items;
        Assert.Equal([1020, 2020, 3020], second.Select(i => i.N));
        Assert.Equal(["э.814 с1", "э.814 с2", "э.814 с3"], second.Select(i => i.Label));
        Assert.Equal([1, 2, 3], second.Select(i => i.SourceSectionNum!.Value));
        Assert.All(second, i => Assert.Equal(814, i.SourceElementNum));
        Assert.Equal([1, 2, 3], second.Select(i => i.Num));
    }

    [Fact]
    public void LoadCases_ZeroBarSkippedButZeroSectionKept_ZeroShellSkipped()
    {
        var zeroBar = new ScadElementForces(1, ScadElementKind.Beam, BarTypes, 2, new double[2 * BarTypes.Length], 1, [], 0);
        var cantilever = new double[2 * BarTypes.Length];
        cantilever[0] = 5; // сечение 1 нагружено, сечение 2 — нули
        var bar = new ScadElementForces(2, ScadElementKind.Beam, BarTypes, 2, cantilever, 1, [], 0);
        var zeroShell = new ScadElementForces(3, ScadElementKind.Shell, ShellTypes, 1, new double[ShellTypes.Length], 1, [], 0);
        var shell = new ScadElementForces(4, ScadElementKind.Shell, ShellTypes, 1,
            [1, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0], 1, [], 0);

        var fs = Assert.Single(ScadForceSetBuilder.LoadCases([zeroBar, bar, zeroShell, shell],
            new ScadResultCatalog([], []), 1, "", Opt));

        Assert.Equal("Загружение 1", fs.Tag);
        Assert.Equal(["э.2 с1", "э.2 с2"], fs.Items.Select(i => i.Label));
        Assert.Equal("э.4 с1", Assert.Single(fs.ShellItems).Label);
        Assert.Equal("shell", fs.Kind);
    }

    [Fact]
    public void Combinations_NamesWithoutLimitStateSuffix()
    {
        var comb = new double[ShellTypes.Length * 2];
        comb[0] = 1; comb[ShellTypes.Length] = 2;
        var shell = new ScadElementForces(55459, ScadElementKind.Shell, ShellTypes, 1,
            new double[ShellTypes.Length], 1, comb, 2);
        var catalog = new ScadResultCatalog([], ["C1 - \"L1+L2\"", ""]);

        var sets = ScadForceSetBuilder.Combinations([shell], catalog, 1, "Плита", Opt);

        Assert.Equal(["Плита — C1 - \"L1+L2\"", "Плита — РСН 2"], sets.Select(s => s.Tag));
        Assert.Equal(20, sets[1].ShellItems.Single().SigmaX!.Value, 9);
        Assert.DoesNotContain(sets, s => s.Tag.Contains("(C)"));
    }

    [Fact]
    public void Rsu_FourGroupsDuplicatesRemoved()
    {
        double[] a = [-35.487, 0.038, 1.436, -0.688, 5.93, 3.262, 0, 0, 0];
        double[] b = [-30.948, 0.042, 0.624, -0.321, 2.658, 1.691, 0, 0, 0];
        var bar = new ScadRsuElement(814, ScadElementKind.Beam, BarTypes,
        [
            new(1, 0, 1, a), new(1, 1, 1, a), new(1, 2, 1, a), new(1, 3, 1, a),
            new(1, 0, 2, b), new(1, 0, 3, a), // критерий 3 дал то же сочетание, что 1 — дубль
            new(2, 0, 1, a),                  // другое сечение — не дубль
        ]);

        var sets = ScadForceSetBuilder.Rsu([bar], 1, "Колонна", Opt);

        Assert.Equal(["Колонна — РСУ (C)", "Колонна — РСУ (CL)", "Колонна — РСУ (N)", "Колонна — РСУ (NL)"],
            sets.Select(s => s.Tag));
        var c = sets[0].Items;
        Assert.Equal(["э.814 с1 к1", "э.814 с1 к2", "э.814 с2 к1"], c.Select(i => i.Label));
        Assert.Equal(-354.87, c[0].N, 6);
        Assert.Equal([1, 1, 2], c.Select(i => i.SourceSectionNum!.Value));
        Assert.Single(sets[1].Items);
        Assert.Equal(CScore.CalcType.CL, CScore.Fem.FemCheckRunner.ExtractCalcType(sets[1].Tag, null));
        Assert.Equal(CScore.CalcType.NL, CScore.Fem.FemCheckRunner.ExtractCalcType(sets[3].Tag, null));
    }

    [Fact]
    public void Rsu_EmptyElementsAndShellKind()
    {
        var none = new ScadRsuElement(4892, ScadElementKind.Shell, ShellTypes, []);
        var shell = new ScadRsuElement(55459, ScadElementKind.Shell, ShellTypes,
            [new(1, 2, 0, [16.076, 12.382, 13.687, -0.135, -0.105, -0.228, 0.103, -0.06, 0, 0, 0])]);

        var fs = Assert.Single(ScadForceSetBuilder.Rsu([none, shell], 1, "", Opt));

        Assert.Equal("РСУ (N)", fs.Tag);
        Assert.Equal("shell", fs.Kind);
        Assert.Equal("э.55459 с1 к0", Assert.Single(fs.ShellItems).Label);
    }
}
