using System.Text.Json;
using CScore.En1992;
using CScore.Fem;
using CScore.Fem.Imperfections;
using CScore.Sp63;

using Xunit;

namespace CScore.Tests;

/// <summary>
/// Несовершенства ЖБ (срез 6): θᵢ по EN 1992-1-1 (5.1), eᵢ = θᵢ·l₀/2 (5.2) и l₀/400 (п. 5.2(7)a, (9)) — по тексту
/// ТКП EN 1992-1-1-2009 (с. 52–54 PDF); случайный эксцентриситет п. 8.1.7 СП 63; привязка режимов к сетке резолвером.
/// </summary>
public sealed class En1992ImperfectionsTests
{
    // Ручной расчёт (5.1): α_h = 2/√l в пределах [2/3; 1], α_m = √(0,5·(1 + 1/m)), θ = α_h·α_m/200.
    //  l = 4 м, m = 1:  α_h = 1, α_m = 1 → θ = 1/200;
    //  l = 16 м, m = 4: 2/4 = 0,5 → 2/3, α_m = √0,625 = 0,790569 → θ = 1/379,47;
    //  l = 9 м: 2/3 (граница); l = 6,25 м: 0,8 → θ = 1/250.
    [Theory]
    [InlineData(4.0, 1, 1.0, 1.0, 200.0)]
    [InlineData(16.0, 4, 2.0 / 3, 0.7905694150420949, 379.4733192202055)]
    [InlineData(9.0, 1, 2.0 / 3, 1.0, 300.0)]
    [InlineData(6.25, 1, 0.8, 1.0, 250.0)]
    public void Theta_Formula51(double l, int m, double alphaH, double alphaM, double inv)
    {
        var r = En1992Imperfections.Theta(l, m);
        Assert.Equal(alphaH, r.AlphaH, 12);
        Assert.Equal(alphaM, r.AlphaM, 12);
        Assert.Equal(1 / inv, r.Theta, 12);
        Assert.Contains("θᵢ = θ₀·α_h·α_m", r.Describe());
    }

    [Fact]
    public void Theta_RejectsInvalid()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => En1992Imperfections.Theta(0, 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => En1992Imperfections.Theta(4, 0));
    }

    // eᵢ = θᵢ·l₀/2: L = 4 м (θ = 1/200), μ = 1 → 10 мм; μ = 0,7 → 7 мм (θ — по фактической длине); L = 6,25 м → θ = 1/250,
    // μ = 1 → 12,5 мм. l₀/400: L = 3 м → 7,5 мм.
    [Theory]
    [InlineData(4.0, 1.0, 0.010)]
    [InlineData(4.0, 0.7, 0.007)]
    [InlineData(6.25, 1.0, 0.0125)]
    public void BowEi_Formula52(double length, double mu, double expected)
    {
        Assert.Equal(expected, En1992Imperfections.BowEi(length, mu, out string describe), 12);
        Assert.Contains("(5.2)", describe);
    }

    [Fact]
    public void BowSimplified_L0Over400()
    {
        Assert.Equal(0.0075, En1992Imperfections.BowSimplified(3.0, 1.0, out string describe), 12);
        Assert.Contains("l₀/400", describe);
    }

    // e_a = max(l/600, h/30, 10 мм): 3 м и 0,4 м → max(5; 13,33; 10) = 13,33 мм; 9 м и 0,2 м → 15 мм; 3 м и 0,2 м → 10 мм.
    [Theory]
    [InlineData(3.0, 0.4, 0.4 / 30)]
    [InlineData(9.0, 0.2, 0.015)]
    [InlineData(3.0, 0.2, 0.010)]
    public void Sp63_AccidentalEccentricity(double l, double h, double expected)
    {
        Assert.Equal(expected, Sp63AccidentalEccentricity.Ea(l, h, out string describe), 12);
        Assert.Contains("п. 8.1.7", describe);
    }

    // ---------------- резолвер ----------------

    const double L = 6.0;
    const int N = 4;

    static (List<FemMeshNode> Nodes, List<FemElement> Elements) Column()
    {
        var nodes = Enumerable.Range(0, N + 1).Select(i => new FemMeshNode { NodeTag = (i + 1).ToString(), Z = L * i / N }).ToList();
        var elements = Enumerable.Range(1, N).Select(i => new FemElement
        {
            ElemTag = i.ToString(), ElemType = "beam", NodeIdsJson = JsonSerializer.Serialize(new[] { i, i + 1 }), SourceMemberTag = "C",
        }).ToList();
        return (nodes, elements);
    }

    // Прямоугольник b = 0,3 (X сечения) × h = 0,5 (Y сечения), м.
    static CrossSection Rect()
    {
        var area = new MaterialArea
        {
            Tag = "бетон", Category = AreaCategory.Region, Material = new Material { Tag = "B25", Type = MatType.Concrete },
            Hull = new Contour([-0.15, 0.15, 0.15, -0.15, -0.15], [-0.25, -0.25, 0.25, 0.25, -0.25], "hull"),
        };
        return new CrossSection { Tag = "300×500", Areas = [area] };
    }

    static FemImperfection Bow(FemBowParams p) => new()
    {
        Id = 3, LoadCaseId = 9, Kind = FemImperfectionKinds.Bow, Tag = "Погибь ЖБ", ParamsJson = p.ToJson(),
    };

    static FemImperfectionResolution Resolve(FemImperfection imp, bool withSection = true)
    {
        var (nodes, elements) = Column();
        var section = Rect();
        var context = withSection ? new FemImperfectionContext { BeamCross = _ => section } : null;
        return FemImperfectionResolver.Resolve([(imp, 1.0)], elements, nodes, [new FemMember { ElemTag = "C" }], context);
    }

    [Fact]
    public void Resolver_Sp63_SectionHeightByPlane()
    {
        // Плоскость y (своя схема: Y сечения по местной y) — h = 0,5: max(10; 16,67; 10) = 16,67 мм.
        var y = Resolve(Bow(new FemBowParams { MemberTags = ["C"], Plane = "y", E0Mode = FemBowParams.E0Sp63, Sign = "plus" }));
        Assert.False(y.HasErrors);
        var by = (FemResolvedBow)y.Items.Single();
        Assert.Equal(0.5 / 30, by.E0, 12);
        Assert.True(by.Concrete);
        Assert.False(by.ActivityEn1993);
        Assert.Contains("п. 8.1.7", by.E0Source);
        // Плоскость z — h = 0,3: max(10; 10; 10) = 10 мм.
        var bz = (FemResolvedBow)Resolve(Bow(new FemBowParams { MemberTags = ["C"], Plane = "z", E0Mode = FemBowParams.E0Sp63, Sign = "plus" })).Items.Single();
        Assert.Equal(0.010, bz.E0, 12);
        // h вручную 900 мм → 30 мм.
        var bm = (FemResolvedBow)Resolve(Bow(new FemBowParams
        {
            MemberTags = ["C"], E0Mode = FemBowParams.E0Sp63, SectionHeightMm = 900, Sign = "plus",
        }), withSection: false).Items.Single();
        Assert.Equal(0.030, bm.E0, 12);
        // Без сечения и без h — ошибка.
        var none = Resolve(Bow(new FemBowParams { MemberTags = ["C"], E0Mode = FemBowParams.E0Sp63 }), withSection: false);
        Assert.Contains(none.Diagnostics, d => d.IsError && d.Code == "imperfection_section_height");
    }

    [Fact]
    public void Resolver_En1992_BowAndActivity()
    {
        // L = 6 м: α_h = 2/√6 = 0,8165, θ = 1/244,9; eᵢ = θ·μL/2, μ = 0,7.
        var r = Resolve(Bow(new FemBowParams { MemberTags = ["C"], E0Mode = FemBowParams.E0En1992, Mu = 0.7, Sign = "minus" }));
        var b = (FemResolvedBow)r.Items.Single();
        Assert.Equal(-(2 / Math.Sqrt(6)) / 200 * 0.7 * 6 / 2, b.E0, 12);
        Assert.DoesNotContain(r.Diagnostics, d => d.Code == "imperfection_bow_unbraced");
        // Упрощённо l₀/400 при μ = 1 → 15 мм; в отчёте — «всегда» по EN 1992-1-1 5.2(1)P.
        var s = Resolve(Bow(new FemBowParams { MemberTags = ["C"], E0Mode = FemBowParams.E0En1992Simplified, Sign = "plus" }));
        var bs = (FemResolvedBow)s.Items.Single();
        Assert.Equal(0.015, bs.E0, 12);
        var decided = FemImperfectionResolver.Decide(s.Items, null, Column().Elements, Column().Nodes, "нет расчёта");
        Assert.Contains("5.2(1)P", decided.Report.Bows.Single().ActiveReason);
        Assert.DoesNotContain(decided.Diagnostics, d => d.Code == "imperfection_activity");
        // μ ≥ 2 — предупреждение; μ ≤ 0 — ошибка.
        Assert.Contains(Resolve(Bow(new FemBowParams { MemberTags = ["C"], E0Mode = FemBowParams.E0En1992, Mu = 2 })).Diagnostics,
            d => !d.IsError && d.Code == "imperfection_bow_unbraced");
        Assert.Contains(Resolve(Bow(new FemBowParams { MemberTags = ["C"], E0Mode = FemBowParams.E0En1992, Mu = 0 })).Diagnostics,
            d => d.IsError && d.Code == "imperfection_mu_invalid");
    }

    [Fact]
    public void Resolver_SwayEn1992()
    {
        var imp = new FemImperfection
        {
            Id = 1, LoadCaseId = 9, Kind = FemImperfectionKinds.Sway, Tag = "Крен ЖБ",
            ParamsJson = new FemSwayParams { PhiMode = FemSwayParams.PhiEn1992, HeightM = 16, M = 4 }.ToJson(),
        };
        var r = Resolve(imp);
        var s = (FemResolvedSway)r.Items.Single();
        Assert.Equal(1 / 379.4733192202055, s.Phi, 12);
        Assert.NotNull(s.Theta);
        Assert.Null(s.Norm);
        var decided = FemImperfectionResolver.Decide(r.Items, null, Column().Elements, Column().Nodes);
        var row = decided.Report.Sways.Single();
        Assert.True(row.En1992);
        Assert.Contains("EN 1992-1-1 (5.1)", row.Source);
        Assert.Equal(16, row.H);
        // l по умолчанию — размах z охвата (6 м).
        var span = (FemResolvedSway)Resolve(new FemImperfection
        {
            Kind = FemImperfectionKinds.Sway, ParamsJson = new FemSwayParams { PhiMode = FemSwayParams.PhiEn1992 }.ToJson(),
        }).Items.Single();
        Assert.Equal(2 / Math.Sqrt(6) / 200, span.Phi, 12);
    }

    [Fact]
    public void WallBowParams_RoundTrip()
    {
        var p = new FemWallBowParams { MemberTags = ["W1"], E0Mode = FemBowParams.E0Sp63, ThicknessMm = 200, Mu = 0.8, Sign = "minus" };
        var back = FemWallBowParams.Parse(p.ToJson());
        Assert.Equal(p.MemberTags, back.MemberTags);
        Assert.Equal(p.E0Mode, back.E0Mode);
        Assert.Equal(200, back.ThicknessMm);
        Assert.Equal(0.8, back.Mu);
        Assert.Equal("minus", back.Sign);
        Assert.Equal(FemBowParams.E0En1992Simplified, FemWallBowParams.Parse("{}").E0Mode);
        Assert.True(FemBowParams.IsConcreteMode(FemBowParams.E0Sp63));
        Assert.False(FemBowParams.IsConcreteMode(FemBowParams.E0Table));
    }
}
