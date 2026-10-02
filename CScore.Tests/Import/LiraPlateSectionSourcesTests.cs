using System.Text.Json;
using CScore.Fem;
using CScore.Import;
using Xunit;

namespace CScore.Tests.Import;

/// <summary>Сечение пластинчатого КЭ из заданного (ТЗА) и подобранного (ASP) армирования ЛИРЫ.</summary>
public class LiraPlateSectionSourcesTests
{
    const double H = 0.2;
    const double Cover = 0.035;

    static MaterialChars ConcreteChars(CalcType ct, double rb, double rbt) => new(ct)
    {
        Type = MatType.Concrete, E = 30_000_000.0, Fc = -rb, Ft = rbt,
        Ec0 = -0.002, Ec1 = -0.6 * rb / 30_000_000.0, Ec2 = -0.0035, Ec1Red = -0.0015,
        Et0 = 0.0001, Et1 = 0.6 * rbt / 30_000_000.0, Et2 = 0.00015, Et1Red = 0.00008,
    };

    static MaterialChars RebarChars(CalcType ct, double rs) => new(ct)
    {
        Type = MatType.ReSteelF, E = 200_000_000.0, Fc = -rs, Ft = rs, Ec2 = -0.025, Et2 = 0.025,
    };

    static Material Concrete()
    {
        var m = new Material { Id = 1, Tag = "B25", Type = MatType.Concrete, E = 30_000_000.0 };
        m.C = ConcreteChars(CalcType.C, 14_500.0, 1_050.0);
        m.CL = ConcreteChars(CalcType.CL, 14_500.0, 1_050.0);
        m.N = ConcreteChars(CalcType.N, 18_500.0, 1_550.0);
        m.NL = ConcreteChars(CalcType.NL, 18_500.0, 1_550.0);
        return m;
    }

    static Material Rebar()
    {
        var m = new Material { Id = 2, Tag = "A500", Type = MatType.ReSteelF, E = 200_000_000.0 };
        m.C = RebarChars(CalcType.C, 435_000.0);
        m.CL = RebarChars(CalcType.CL, 435_000.0);
        m.N = RebarChars(CalcType.N, 500_000.0);
        m.NL = RebarChars(CalcType.NL, 500_000.0);
        return m;
    }

    /// <summary>Шаблон: ⌀12 шаг 200 у обеих граней, привязка 35 мм.</summary>
    static PlateSection Template() => new()
    {
        Tag = "Пл200", H = H, NLayers = 40, PlateModel = "layered", ConcreteDiagramType = DiagrammType.L3,
        ConcreteMaterialId = 1, RebarMaterialId = 2,
        RebarLayers = [TemplateLayer("низ", -(H / 2 - Cover)), TemplateLayer("верх", H / 2 - Cover)],
    };

    static PlateRebarLayer TemplateLayer(string name, double z) => new()
    {
        Name = name, InputMode = "direct", Asx = 565e-6, Asy = 565e-6, Zsx = z, Zsy = z,
        DiameterX = 0.012, DiameterY = 0.012,
    };

    static FemCheckScopeElement Elem(int num, string? typeIds = null, double? thickness = null) =>
        new(num, new FemElement { ElemTag = num.ToString(), ElemType = "shell", ReinforcementTypeIds = typeIds, ThicknessM = thickness }, null);

    static LiraPlateRebarLayer TzaLayer(LiraPlateRebarSlot slot, double diameterMm, double spacingMm, double aCm) => new()
    {
        Slot = slot, A = aCm,
        Terms = [new LiraPlateRebarTerm(1, diameterMm, Math.PI * diameterMm * diameterMm / 400.0, spacingMm)],
    };

    /// <summary>ТЗА 1 — фон ⌀12 шаг 200 у обеих граней; ТЗА 2 — усиление низа по X ⌀16 шаг 200; ТЗА 3 — только низ.</summary>
    static LiraRbtFile Rbt() => new()
    {
        PlateTypes = new Dictionary<int, LiraPlateReinforcementType>
        {
            [1] = new()
            {
                Id = 1, Kind = LiraRbtReader.KindPlateSimple, Binding = LiraRebarBinding.Centroid,
                Layers =
                [
                    TzaLayer(LiraPlateRebarSlot.XT, 12, 200, 3.5), TzaLayer(LiraPlateRebarSlot.XB, 12, 200, 3.5),
                    TzaLayer(LiraPlateRebarSlot.YT, 12, 200, 3.5), TzaLayer(LiraPlateRebarSlot.YB, 12, 200, 3.5),
                ],
            },
            [2] = new()
            {
                Id = 2, Kind = LiraRbtReader.KindPlateSimple, Binding = LiraRebarBinding.Centroid,
                Layers = [TzaLayer(LiraPlateRebarSlot.XB, 16, 200, 3.5)],
            },
            [3] = new()
            {
                Id = 3, Kind = LiraRbtReader.KindPlateSimple, Binding = LiraRebarBinding.Centroid,
                Layers = [TzaLayer(LiraPlateRebarSlot.XB, 16, 100, 3.5), TzaLayer(LiraPlateRebarSlot.YB, 16, 100, 3.5)],
            },
        },
    };

    static LiraAspFile Asp(params LiraAspPlate[] plates) => new() { Plates = plates.ToDictionary(p => p.ElementId) };

    static LiraAspPlate AspPlate(int id, double as1, double as2, double as3, double as4, double h = H,
        string rebar = "A500", string concrete = "B25") => new(id, as1, as2, as3, as4, 0, h, rebar, concrete);

    // ── Заданное армирование (ТЗА) ───────────────────────────────────────────────────────────

    [Fact]
    public void Assigned_SumsTypesAndKeepsBinding()
    {
        var source = new LiraAssignedPlateSectionSource(Template(), Rbt());

        var r = source.Resolve(Elem(10, "1 2", thickness: 0.3));

        Assert.Equal("ТЗА 1 2", r.Label);
        var s = Assert.IsType<PlateSection>(r.Section);
        Assert.Equal(0.3, s.H);
        Assert.Equal(40, s.NLayers);                      // модель и бетон — от шаблона
        var bottom = s.RebarLayers.Single(l => l.Zsx < 0);
        var top = s.RebarLayers.Single(l => l.Zsx > 0);
        double a12 = Math.PI * 0.012 * 0.012 / 4 / 0.2, a16 = Math.PI * 0.016 * 0.016 / 4 / 0.2;   // шаг 200
        Assert.Equal(a12 + a16, bottom.Asx, 9);
        Assert.Equal(a12, bottom.Asy, 9);
        Assert.Equal(a12, top.Asx, 9);
        Assert.Equal(-(0.15 - 0.035), bottom.Zsx, 9);     // привязка от грани КЭ толщиной 0,3
        // Эквивалентный диаметр по п. 8.2.17: (12² + 16²) / (12 + 16)
        Assert.Equal((144.0 + 256.0) / 28.0 / 1000.0, bottom.DiameterX, 9);
        Assert.Equal(0.012, top.DiameterX, 9);
    }

    [Fact]
    public void Assigned_SameTypesAndThickness_ShareSection()
    {
        var source = new LiraAssignedPlateSectionSource(Template(), Rbt());

        var a = source.Resolve(Elem(10, "1 2"));
        var b = source.Resolve(Elem(11, "1 2"));
        var c = source.Resolve(Elem(12, "1"));

        Assert.Same(a.Section, b.Section);
        Assert.Equal(a.RebarKey, b.RebarKey);
        Assert.NotSame(a.Section, c.Section);
        Assert.NotEqual(a.RebarKey, c.RebarKey);
    }

    [Theory]
    [InlineData(null, "не назначены")]
    [InlineData("1 7", "ТЗА 7 нет")]
    [InlineData("1 x", "Некорректный номер")]
    public void Assigned_MissingData_GivesReason(string? typeIds, string expected)
    {
        var r = new LiraAssignedPlateSectionSource(Template(), Rbt()).Resolve(Elem(10, typeIds));

        Assert.Null(r.Section);
        Assert.Contains(expected, r.Reason);
    }

    // ── Подобранное армирование (ASP) ────────────────────────────────────────────────────────

    [Fact]
    public void Selected_TakesAreasFromAspAndCoverFromTemplate()
    {
        var source = new LiraSelectedPlateSectionSource(Template(), Asp(AspPlate(10, 8.0, 3.0, 6.0, 2.0, h: 0.25)));

        var r = source.Resolve(Elem(10));

        Assert.Equal("ASP", r.Label);
        var s = Assert.IsType<PlateSection>(r.Section);
        Assert.Equal(0.25, s.H);
        var bottom = s.RebarLayers.Single(l => l.Zsx < 0);
        var top = s.RebarLayers.Single(l => l.Zsx > 0);
        Assert.Equal(8.0e-4, bottom.Asx, 12);   // AS1 — нижняя по X
        Assert.Equal(6.0e-4, bottom.Asy, 12);   // AS3 — нижняя по Y
        Assert.Equal(3.0e-4, top.Asx, 12);      // AS2 — верхняя по X
        Assert.Equal(2.0e-4, top.Asy, 12);      // AS4 — верхняя по Y
        Assert.Equal(-(0.125 - Cover), bottom.Zsx, 9);
        Assert.Equal(0.125 - Cover, top.Zsy, 9);
        Assert.Equal(0.012, bottom.DiameterX);
    }

    [Fact]
    public void Selected_FailureCodeAndMissingElement_GiveReason()
    {
        var source = new LiraSelectedPlateSectionSource(Template(), Asp(AspPlate(10, 8.0, -274, 6.0, 2.0)));

        Assert.Contains("код 274", source.Resolve(Elem(10)).Reason);
        Assert.Contains("нет в файле ASP", source.Resolve(Elem(11)).Reason);
    }

    [Fact]
    public void Selected_TemplateWithoutFaceLayer_IsNotAvailable()
    {
        var template = Template();
        template.RebarLayers.RemoveAll(l => l.Zsx > 0);

        var r = new LiraSelectedPlateSectionSource(template, Asp(AspPlate(10, 8, 3, 6, 2))).Resolve(Elem(10));

        Assert.Null(r.Section);
        Assert.Contains("Z+", r.Reason);
    }

    [Fact]
    public void Selected_WarnsAboutMaterialMismatch()
    {
        var source = new LiraSelectedPlateSectionSource(Template(),
            Asp(AspPlate(10, 8, 3, 6, 2), AspPlate(11, 8, 3, 6, 2, concrete: "B30")));
        var elements = new[] { Elem(10), Elem(11) };

        // Кириллическая «В» в имени материала не считается расхождением.
        var cyrillic = new Material { Tag = "Бетон В25" };
        var warnings = source.Warnings(elements, cyrillic, Rebar());

        var w = Assert.Single(warnings);
        Assert.Contains("B30", w);
        Assert.DoesNotContain("B25,", w);
    }

    // ── Проверка по КЭ целиком ───────────────────────────────────────────────────────────────

    static readonly FemCheck LayeredUls = new()
    {
        NormCode = "rc_plate_check", Tag = "плита",
        ParamsJson = new PlateCheckParams { Kind = "shell_layered", CheckGroup = "uls" }.ToJson(),
    };

    static CalcResult Run(IReadOnlyList<IPlateElementSectionSource> sources, FemCheckScopeElement[] elements, ForceSet fs) =>
        FemCheckRunner.RunPerElement(LayeredUls, new FemMemberGroup { Tag = "Плита" },
            new FemCheckScope([], elements, RefersToMeshElements: true), [fs],
            new FemPerElementInputs { PlateTemplate = Template(), PlateSources = sources, ConcreteMat = Concrete(), RebarMat = Rebar() },
            (_, _, _) => throw new InvalidOperationException("стержневой исполнитель не нужен"));

    /// <summary>
    /// Проверка порциями по КЭ: строка явного NL-набора (длительная часть, п. 8.2.7) ищется по метке — в порции
    /// среди строк тех же КЭ; результат тот же, что у расчёта целиком.
    /// </summary>
    [Fact]
    public void Chunks_FindNlRowsOfTheirElements()
    {
        var sls = new FemCheck
        {
            NormCode = "rc_plate_check", Tag = "плита",
            ParamsJson = new PlateCheckParams { Kind = "shell_layered", CheckGroup = "sls", NlForceSetId = 2 }.ToJson(),
        };
        var n = new ForceSet
        {
            Id = 1, Kind = "shell", Tag = "Плита (N)",
            ShellItems = [Shell(7, "э.7 с1", -30), Shell(8, "э.8 с1", -30), Shell(9, "э.9 с1", -30)],
        };
        var nl = new ForceSet
        {
            Id = 2, Kind = "shell", Tag = "Плита (NL)",
            ShellItems = [Shell(9, "э.9 с1", -25), Shell(8, "э.8 с1", -5), Shell(7, "э.7 с1", -25)],
        };
        FemCheckScopeElement[] elements = [Elem(7), Elem(8), Elem(9)];

        CalcResult RunChunks(int chunkRows) => FemCheckRunner.RunPerElement(sls, new FemMemberGroup { Tag = "Плита" },
            new FemCheckScope([], elements, RefersToMeshElements: true), [n],
            new FemPerElementInputs
            {
                PlateTemplate = Template(), ConcreteMat = Concrete(), RebarMat = Rebar(), LookupForceSets = [n, nl],
            },
            (_, _, _) => throw new InvalidOperationException("стержневой исполнитель не нужен"), chunkRows: chunkRows);

        var whole = RunChunks(FemCheckRunner.DefaultChunkRows);
        var chunked = RunChunks(1);

        double Util(CalcResult r, string label) => r.FemCheckRows!.Single(x => x.Label == label).Utilization;
        foreach (var label in new[] { "э.7 с1", "э.8 с1", "э.9 с1" })
            Assert.Equal(Util(whole, label), Util(chunked, label), 9);
        Assert.Equal(whole.DataJson, chunked.DataJson);
        // NL-строка своего КЭ участвует: при равных полных усилиях у КЭ 8 длительная часть меньше.
        Assert.Equal(Util(chunked, "э.7 с1"), Util(chunked, "э.9 с1"), 9);
        Assert.NotEqual(Util(chunked, "э.7 с1"), Util(chunked, "э.8 с1"), 6);
    }

    static ShellLoadItem Shell(int elem, string label, double mx) =>
        new() { Label = label, Mx = mx, SourceElementNum = elem };

    /// <summary>
    /// Вопрос 10.2 спеки: момент ЛИРЫ, растягивающий низ, после инверсии знака при импорте отрицателен
    /// и должен нагружать нижнюю арматуру (XB/YB, AS1/AS3 → слой у грани Z−). Армирование только снизу:
    /// отрицательный Mx воспринимается, положительный (растянута голая верхняя грань) — нет.
    /// </summary>
    [Theory]
    [InlineData("assigned")]
    [InlineData("selected")]
    public void BottomReinforcement_ResistsNegativeMomentOnly(string sourceKey)
    {
        IPlateElementSectionSource source = sourceKey == "assigned"
            ? new LiraAssignedPlateSectionSource(Template(), Rbt())
            : new LiraSelectedPlateSectionSource(Template(), Asp(AspPlate(7, 20.1, 0, 20.1, 0)));
        var fs = new ForceSet
        {
            Id = 1, Kind = "shell", Tag = "РСН (C)",
            ShellItems = [Shell(7, "низ растянут", -60), Shell(7, "верх растянут", +60)],
        };

        var result = Run([source], [Elem(7, "3")], fs);

        using var doc = JsonDocument.Parse(result.DataJson);
        var rows = result.FemCheckRows!.ToDictionary(r => r.Label);
        Assert.True(rows["низ растянут"].Passed, result.DataJson);
        Assert.False(rows["верх растянут"].Passed, result.DataJson);
        Assert.Equal(sourceKey, rows["низ растянут"].RebarSource);
        Assert.Equal("not_passed", result.Status);
    }

    [Fact]
    public void SeveralSources_GiveAggregatePerSource()
    {
        // КЭ 7: ТЗА 1 (фон) и подбор ASP с вдвое большей нижней арматурой; КЭ 8 — без ТЗА и без подбора.
        var template = Template();
        IPlateElementSectionSource[] sources =
        [
            new TemplatePlateSectionSource(template),
            new LiraAssignedPlateSectionSource(template, Rbt()),
            new LiraSelectedPlateSectionSource(template, Asp(AspPlate(7, 11.3, 5.65, 11.3, 5.65))),
        ];
        var fs = new ForceSet
        {
            Id = 1, Kind = "shell", Tag = "РСН (C)",
            ShellItems = [Shell(7, "э.7", -20), Shell(8, "э.8", -20), Shell(99, "э.99", -20)],
        };

        var result = Run(sources, [Elem(7, "1"), Elem(8)], fs);

        Assert.Equal("incomplete", result.Status);
        using var doc = JsonDocument.Parse(result.DataJson);
        var root = doc.RootElement;
        Assert.Equal(["section", "assigned", "selected"], root.GetProperty("rebarSources").EnumerateArray().Select(s => s.GetString()));
        Assert.Equal(1, root.GetProperty("skippedRows").GetInt32());
        Assert.Equal(6, root.GetProperty("totalRows").GetInt32());   // 2 строки × 3 источника

        JsonElement El(int num, string source) => root.GetProperty("elements").EnumerateArray().Single(e =>
            e.GetProperty("elemNum").GetInt32() == num && e.GetProperty("rebarSource").GetString() == source);

        Assert.Equal("ok", El(7, "section").GetProperty("status").GetString());
        Assert.Equal("ok", El(7, "assigned").GetProperty("status").GetString());
        Assert.Equal("ТЗА 1", El(7, "assigned").GetProperty("sectionLabel").GetString());
        Assert.Equal("ok", El(8, "section").GetProperty("status").GetString());
        Assert.Equal("no_rebar", El(8, "assigned").GetProperty("status").GetString());
        Assert.Equal("no_rebar", El(8, "selected").GetProperty("status").GetString());
        // Фон ТЗА 1 совпадает с шаблоном — тот же коэффициент; у подбора арматуры больше — коэффициент меньше.
        double uSection = El(7, "section").GetProperty("utilMax").GetDouble();
        Assert.Equal(uSection, El(7, "assigned").GetProperty("utilMax").GetDouble(), 3);
        Assert.True(El(7, "selected").GetProperty("utilMax").GetDouble() < uSection);
    }
}
