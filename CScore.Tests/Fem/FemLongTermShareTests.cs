using System;
using System.Collections.Generic;
using System.Linq;
using CScore;
using CScore.Fem;
using Xunit;

namespace CScore.Tests.Fem;

/// <summary>
/// Доля длительной части усилий схемы по парам наборов «полный — длительный» (<see cref="FemLongTermShare"/>): РСН
/// (строка на КЭ), РСУ с разным числом строк, метрика, осреднение.
/// </summary>
public class FemLongTermShareTests
{
    const double H = 0.2;

    static ShellLoadItem Shell(int elem, double nx, double ny, double nxy, double mx, double my, double mxy, int? sec = 1) => new()
    {
        Label = $"э.{elem} с{sec}", Nx = nx, Ny = ny, Nxy = nxy, Mx = mx, My = my, Mxy = mxy,
        SourceElementNum = elem, SourceSectionNum = sec,
    };

    static ShellLoadItem Scale(ShellLoadItem r, double k) =>
        Shell(r.SourceElementNum!.Value, k * r.Nx, k * r.Ny, k * r.Nxy, k * r.Mx, k * r.My, k * r.Mxy, r.SourceSectionNum);

    static FemLongTermShareResult? Shells(IReadOnlyList<ShellLoadItem> total, IReadOnlyList<ShellLoadItem> lng,
        Func<int, double>? weight = null, Func<int, double?>? thickness = null) =>
        FemLongTermShare.Shells([(total, lng)], thickness ?? (_ => H), weight);

    static double Energy(ShellLoadItem r) => FemLongTermShare.Vector(r, H).Sum(v => v * v);

    static double Dot(ShellLoadItem a, ShellLoadItem b) =>
        FemLongTermShare.Vector(a, H).Zip(FemLongTermShare.Vector(b, H), (x, y) => x * y).Sum();

    /// <summary>РСН: по строке на КЭ в каждом наборе, длительная часть пропорциональна полной.</summary>
    [Fact]
    public void ProportionalPairs_GiveExactShare()
    {
        var total = new[] { Shell(1, -50, -30, 15, 18, 8, 10), Shell(2, 20, 5, -3, -12, 25, 4), Shell(3, 0, 0, 0, 40, -14, 7) };
        var lng = total.Select(r => Scale(r, 0.6)).ToArray();

        var r = Shells(total, lng)!;

        Assert.Equal(0.6, r.Share, 12);
        Assert.Equal(3, r.Groups);
        Assert.Equal(0.6, r.MinGroupShare, 12);
        Assert.Equal(0.6, r.MaxGroupShare, 12);
    }

    /// <summary>Метрика — скаляр тензоров усилий: поворот осей выдачи в плоскости КЭ долю не меняет.</summary>
    [Fact]
    public void Share_IsInvariantToInPlaneRotation()
    {
        var total = new[] { Shell(1, -50, -30, 15, 18, 8, 10), Shell(1, 12, 40, -6, -5, 22, 3) };
        var lng = new[] { Shell(1, -35, -10, 9, 11, 2, 8), Shell(1, 4, 21, -1, -3, 13, -2), Shell(1, -20, 6, 2, 9, 9, 1) };

        var plain = Shells(total, lng)!;
        var rotated = Shells(total.Select(r => Keep(ShellForceTransform.Rotate(r, 37), r)).ToArray(),
            lng.Select(r => Keep(ShellForceTransform.Rotate(r, 37), r)).ToArray())!;

        Assert.Equal(plain.Share, rotated.Share, 10);

        static ShellLoadItem Keep(ShellLoadItem rotated, ShellLoadItem source)
        {
            rotated.SourceElementNum = source.SourceElementNum;
            rotated.SourceSectionNum = source.SourceSectionNum;
            return rotated;
        }
    }

    /// <summary>
    /// РСУ: у КЭ три строки полного набора и две — длительного. Доля строки — наибольшая проекция строк длительного
    /// набора на неё; доля КЭ — среднее с весом по энергии строк.
    /// </summary>
    [Fact]
    public void DifferentRowCounts_UseLargestProjectionPerRow()
    {
        var f = new[] { Shell(7, 0, 0, 0, 30, 5, 0), Shell(7, 0, 0, 0, 5, 30, 0), Shell(7, -100, -20, 0, 10, 10, 2) };
        var g = new[] { Shell(7, 0, 0, 0, 20, 4, 0), Shell(7, -60, -10, 0, 3, 18, 1) };

        var r = Shells(f, g)!;

        double[] s = f.Select(fi => Math.Clamp(g.Max(gj => Dot(fi, gj)) / Energy(fi), 0, 1)).ToArray();
        double expected = f.Select((fi, i) => Energy(fi) * s[i]).Sum() / f.Sum(Energy);
        Assert.Equal(expected, r.Share, 12);
        Assert.Equal(3, r.Rows);
        Assert.Equal(1, r.Groups);
        // Строка «Mx» ближе к первой строке длительного набора, строка «My» — ко второй.
        Assert.Equal(Dot(f[0], g[0]) / Energy(f[0]), s[0], 12);
        Assert.Equal(Dot(f[1], g[1]) / Energy(f[1]), s[1], 12);
    }

    [Fact]
    public void Share_IsClampedToZeroOne()
    {
        var total = new[] { Shell(1, 0, 0, 0, 10, 10, 0), Shell(2, 0, 0, 0, 10, 10, 0) };
        var lng = new[] { Shell(1, 0, 0, 0, 25, 25, 0), Shell(2, 0, 0, 0, -8, -8, 0) };

        var r = Shells(total, lng)!;

        Assert.Equal(1.0, r.MaxGroupShare, 12);   // длительная часть больше полной
        Assert.Equal(0.0, r.MinGroupShare, 12);   // длительная часть противоположного знака
        Assert.Equal(0.5, r.Share, 12);
    }

    /// <summary>Одно число на схему — среднее долей КЭ с весом «вес КЭ × энергия усилий».</summary>
    [Fact]
    public void SchemaShare_IsEnergyWeightedMean()
    {
        var a = Shell(1, 0, 0, 0, 30, 0, 0);
        var b = Shell(2, 0, 0, 0, 10, 0, 0);
        var total = new[] { a, b };
        var lng = new[] { Scale(a, 0.8), Scale(b, 0.2) };

        var plain = Shells(total, lng)!;
        Assert.Equal((900 * 0.8 + 100 * 0.2) / 1000, plain.Share, 12);

        // Вес КЭ (площадь/толщина): у второго КЭ в 9 раз больше — вклады равны.
        var weighted = Shells(total, lng, e => e == 2 ? 9 : 1)!;
        Assert.Equal(0.5, weighted.Share, 12);
    }

    /// <summary>Число строк РСУ у КЭ не добавляет ему веса: повтор строк полного набора долю не меняет.</summary>
    [Fact]
    public void RepeatedRows_DoNotInflateElementWeight()
    {
        var a = Shell(1, 0, 0, 0, 20, 0, 0);
        var b = Shell(2, 0, 0, 0, 20, 0, 0);
        var lng = new[] { Scale(a, 0.9), Scale(b, 0.3) };

        var once = Shells([a, b], lng)!;
        var repeated = Shells([a, a, a, a, b], lng)!;

        Assert.Equal(0.6, once.Share, 12);
        Assert.Equal(once.Share, repeated.Share, 12);
    }

    [Fact]
    public void ElementsWithoutLongRowsOrThickness_AreCountedAndExcluded()
    {
        var total = new[] { Shell(1, 0, 0, 0, 20, 0, 0), Shell(2, 0, 0, 0, 50, 0, 0), Shell(3, 0, 0, 0, 50, 0, 0), Shell(4, 0, 0, 0, 0, 0, 0) };
        var lng = new[] { Shell(1, 0, 0, 0, 14, 0, 0), Shell(3, 0, 0, 0, 5, 0, 0), Shell(4, 0, 0, 0, 0, 0, 0) };

        var r = Shells(total, lng, thickness: e => e == 3 ? null : H)!;

        Assert.Equal(0.7, r.Share, 12);
        Assert.Equal(1, r.Groups);
        Assert.Equal(1, r.GroupsWithoutLong);   // КЭ 2
        Assert.Equal(2, r.GroupsSkipped);       // КЭ 3 без толщины, КЭ 4 с нулевыми усилиями
    }

    [Fact]
    public void NoUsableRows_GiveNull()
    {
        Assert.Null(Shells([Shell(1, 0, 0, 0, 20, 0, 0)], []));
        Assert.Null(Shells([], [Shell(1, 0, 0, 0, 20, 0, 0)]));
    }

    /// <summary>Сечения одного КЭ — разные группы: строка сечения 1 не сопоставляется со строками сечения 2.</summary>
    [Fact]
    public void Sections_AreSeparateGroups()
    {
        var total = new[] { Shell(1, 0, 0, 0, 20, 0, 0, sec: 1), Shell(1, 0, 0, 0, 20, 0, 0, sec: 2) };
        var lng = new[] { Shell(1, 0, 0, 0, 16, 0, 0, sec: 1), Shell(1, 0, 0, 0, 4, 0, 0, sec: 2) };

        var r = Shells(total, lng)!;

        Assert.Equal(2, r.Groups);
        Assert.Equal(0.5, r.Share, 12);
        Assert.Equal(0.2, r.MinGroupShare, 12);
        Assert.Equal(0.8, r.MaxGroupShare, 12);
    }

    /// <summary>Мембранные усилия и моменты в одной метрике: N и M = N·h/√12 дают равную энергию.</summary>
    [Fact]
    public void Metric_BalancesMembraneAndBending()
    {
        double m = 100 * H / Math.Sqrt(12);
        Assert.Equal(Energy(Shell(1, 100, 0, 0, 0, 0, 0)), Energy(Shell(1, 0, 0, 0, m, 0, 0)), 9);
        // Чистый сдвиг ±45° — те же главные усилия: N:N одинаково.
        Assert.Equal(Energy(Shell(1, 100, -100, 0, 0, 0, 0)), Energy(Shell(1, 0, 0, 100, 0, 0, 0)), 9);
    }

    [Fact]
    public void Bars_UseSectionRadii()
    {
        LoadItem Bar(int elem, int sec, double n, double mx, double my) =>
            new() { Label = $"э.{elem} с{sec}", N = n, Mx = mx, My = my, SourceElementNum = elem, SourceSectionNum = sec };
        var total = new[] { Bar(1, 1, -500, 60, 0), Bar(1, 1, -300, 90, 10), Bar(1, 2, -500, -40, 0) };
        var lng = new[] { Bar(1, 1, -400, 30, 0), Bar(1, 2, -400, -20, 0) };
        const double ix = 0.4 / 3.4641016151377544, iy = 0.3 / 3.4641016151377544;

        var r = FemLongTermShare.Bars([(total, lng)], _ => (ix, iy))!;

        double E(LoadItem a, LoadItem b) => a.N * b.N + a.Mx * b.Mx / (ix * ix) + a.My * b.My / (iy * iy);
        double s11 = E(total[0], lng[0]) / E(total[0], total[0]), s12 = E(total[1], lng[0]) / E(total[1], total[1]);
        double s2 = E(total[2], lng[1]) / E(total[2], total[2]);
        double w1 = 0.5, w2 = 1.0;   // вес группы делится на число её строк
        double expected = (w1 * (E(total[0], total[0]) * s11 + E(total[1], total[1]) * s12) + w2 * E(total[2], total[2]) * s2)
                          / (w1 * (E(total[0], total[0]) + E(total[1], total[1])) + w2 * E(total[2], total[2]));
        Assert.Equal(expected, r.Share, 12);
        Assert.Equal(2, r.Groups);
        Assert.Null(FemLongTermShare.Bars([(total, lng)], _ => null));
    }
}
