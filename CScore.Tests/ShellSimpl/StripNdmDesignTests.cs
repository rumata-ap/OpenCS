using System.Diagnostics;
using CScore.ShellSimpl;
using Xunit;
using Xunit.Abstractions;
using static CScore.Tests.ShellSimpl.StripNdmFixture;

namespace CScore.Tests.ShellSimpl;

/// <summary>Подбор площадей полосы по НДМ: <see cref="StripNdmDesign"/>.</summary>
public sealed class StripNdmDesignTests(ITestOutputHelper output)
{
    const double Rs = 435_000.0;
    static readonly StripNdmLimits Lim = Limits();

    static StripNdmDesignResult Uls(double n, double m) =>
        StripNdmDesign.RequiredUls(n, m, H, Z, -Z, ConcreteDiagram(CalcType.C), RebarDiagram(CalcType.C), Lim);

    static double Eta(StripNdmDesignResult r, double n, double m, double at, double ac) =>
        StripNdm.UltimateFactor(Strip(CalcType.C, asTop: r.TensionTop ? at : ac, asBot: r.TensionTop ? ac : at), n, m, Lim).Eta;

    [Theory]
    [InlineData(0.0, 30.0)]
    [InlineData(0.0, -20.0)]
    [InlineData(-200.0, 40.0)]
    [InlineData(80.0, 25.0)]
    public void TensionOnly_FoundAreaIsMinimal(double n, double m)
    {
        var r = Uls(n, m);
        output.WriteLine($"N={n}, M={m}: {r.Status}, As_t = {r.AsTension * 1e4:F3} см²/м, η = {r.Value:F4}");
        Assert.Equal(StripNdmDesignStatus.TensionOnly, r.Status);
        Assert.Equal(m >= 0, r.TensionTop);
        Assert.InRange(r.Value, 0.99, 1.0);
        Assert.True(Eta(r, n, m, 0.99 * r.AsTension, 0.0) > 1.0);
    }

    [Fact]
    public void NoReinforcementNeeded_WhenConcreteCarriesCompression()
    {
        var r = Uls(-1000.0, 5.0);
        Assert.Equal(StripNdmDesignStatus.NotRequired, r.Status);
        Assert.Equal(0.0, r.AsTension);
        Assert.True(r.Value <= 1.0);
    }

    // Сквозное растяжение с малым эксцентриситетом: бетон не работает, ряды на Rs из равновесия
    // F_t + F_c = N, (F_t − F_c)·z = M — точный ответ минимума As_t + As_c = N/Rs.
    [Fact]
    public void ThroughTension_BothRowsAtYield()
    {
        double n = 300.0, m = 5.0;
        var r = Uls(n, m);
        double ft = (n + m / Z) / 2.0, fc = n - ft;
        output.WriteLine($"As_t = {r.AsTension * 1e4:F3} (точно {ft / Rs * 1e4:F3}), As_c = {r.AsCompression * 1e4:F3} (точно {fc / Rs * 1e4:F3}) см²/м");
        Assert.Equal(StripNdmDesignStatus.WithCompression, r.Status);
        Assert.Equal(n / Rs, r.AsTension + r.AsCompression, 0.01 * n / Rs);
        Assert.Equal(ft / Rs, r.AsTension, 0.03 * ft / Rs);
        Assert.Equal(fc / Rs, r.AsCompression, 0.03 * fc / Rs);
        Assert.True(r.Value <= 1.0);
    }

    // Изгиб сверх предела сжатой зоны одиночного армирования: нужна сжатая арматура.
    [Fact]
    public void LargeMoment_NeedsCompressionReinforcement()
    {
        var r = Uls(0.0, 200.0);
        output.WriteLine($"{r.Status}: As_t = {r.AsTension * 1e4:F2}, As_c = {r.AsCompression * 1e4:F2} см²/м, η = {r.Value:F4}");
        Assert.Equal(StripNdmDesignStatus.WithCompression, r.Status);
        Assert.True(r.AsCompression > 0.0);
        Assert.True(r.Value <= 1.0);
        Assert.True(Eta(r, 0.0, 200.0, r.AsTension, 0.0) > 1.0);
    }

    [Fact]
    public void Impossible_WhenThicknessIsInsufficient()
    {
        var r = Uls(-20_000.0, 0.0);
        Assert.Equal(StripNdmDesignStatus.Impossible, r.Status);
        Assert.False(string.IsNullOrWhiteSpace(r.Reason));
    }

    // Замер, не утверждение. 18.10.2026, Debug: ≈ 2,5 с на 1000 вызовов (≈ 2,5 мс на полосу).
    [Fact]
    public void Timing_1000Calls()
    {
        var sw = Stopwatch.StartNew();
        for (int i = 0; i < 1000; i++) Uls(-50.0 + 0.1 * i, 10.0 + 0.02 * i);
        output.WriteLine($"1000 × RequiredUls: {sw.ElapsedMilliseconds} мс");
    }

    // ── ПС2 ──

    static StripNdmDesignResult Sls(double n, double m, double asC, double lim) =>
        StripNdmDesign.RequiredSls(n, m, H, Z, -Z, asC, lim, ConcreteDiagram(CalcType.N), RebarDiagram(CalcType.N),
            Ds, Ds, 200_000_000.0, 1.0, 0.5);

    static double Acrc(StripNdmDesignResult r, double n, double m, double at, double ac) =>
        StripNdm.CrackWidth(Strip(CalcType.N, asTop: r.TensionTop ? at : ac, asBot: r.TensionTop ? ac : at),
            n, m, [Ds, Ds], 200_000_000.0, 1.0, 0.5).AcrcMm;

    [Theory]
    [InlineData(0.0, 30.0, 0.3)]
    [InlineData(0.0, 30.0, 0.1)]
    [InlineData(-100.0, -32.0, 0.2)]
    public void Sls_FoundAreaIsMinimal(double n, double m, double lim)
    {
        var r = Sls(n, m, AsPerMeter, lim);
        output.WriteLine($"N={n}, M={m}, a_crc,ult={lim}: {r.Status}, As_t = {r.AsTension * 1e4:F3} см²/м, a_crc = {r.Value:F4}");
        Assert.Equal(StripNdmDesignStatus.TensionOnly, r.Status);
        Assert.True(r.Value <= lim);
        Assert.True(Acrc(r, n, m, 0.99 * r.AsTension, r.AsCompression) > lim);
    }

    [Fact]
    public void Sls_NoCrack_NotRequired()
    {
        var r = Sls(0.0, 5.0, 0.0, 0.3);
        Assert.Equal(StripNdmDesignStatus.NotRequired, r.Status);
        Assert.Equal(0.0, r.AsTension);
    }

    [Fact]
    public void Sls_ThroughTension_GrowsBothRows()
    {
        var r = Sls(300.0, 5.0, 0.0, 0.2);
        output.WriteLine($"{r.Status}: As_t = {r.AsTension * 1e4:F3}, As_c = {r.AsCompression * 1e4:F3} см²/м, a_crc = {r.Value:F4}");
        Assert.Equal(StripNdmDesignStatus.WithCompression, r.Status);
        Assert.True(r.AsCompression > 0.0 && r.AsCompression < r.AsTension);
        Assert.True(r.Value <= 0.2);
    }
}
