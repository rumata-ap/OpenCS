using CScore.Fem;
using CScore.Import;
using Xunit;

namespace CScore.Tests.Import;

/// <summary>Сечение-шаблон пластинчатой цели по данным ЛИРЫ: подбор (ASP) + фоновые ТЗА (RBT).</summary>
public class LiraPlateSectionTemplatesTests
{
    static FemCheckScopeElement Elem(int num, string? typeIds = null, double? thickness = null) =>
        new(num, new FemElement { ElemTag = num.ToString(), ElemType = "shell", ReinforcementTypeIds = typeIds, ThicknessM = thickness }, null);

    static LiraPlateRebarLayer TzaLayer(LiraPlateRebarSlot slot, double diameterMm, double spacingMm, double aCm) => new()
    {
        Slot = slot, A = aCm,
        Terms = [new LiraPlateRebarTerm(1, diameterMm, Math.PI * diameterMm * diameterMm / 400.0, spacingMm)],
    };

    static LiraPlateReinforcementType Tza(int id, params LiraPlateRebarLayer[] layers) => new()
    {
        Id = id, Kind = LiraRbtReader.KindPlateSimple, Binding = LiraRebarBinding.Centroid, Layers = layers,
    };

    /// <summary>ТЗА 1 — фон ⌀10 шаг 300 у обеих граней, привязка 4 см; ТЗА 2 — усиление низа по X; ТЗА 3 — только низ ⌀16.</summary>
    static LiraRbtFile Rbt() => new()
    {
        PlateTypes = new Dictionary<int, LiraPlateReinforcementType>
        {
            [1] = Tza(1, TzaLayer(LiraPlateRebarSlot.XT, 10, 300, 4), TzaLayer(LiraPlateRebarSlot.XB, 10, 300, 4),
                         TzaLayer(LiraPlateRebarSlot.YT, 10, 300, 4), TzaLayer(LiraPlateRebarSlot.YB, 10, 300, 4)),
            [2] = Tza(2, TzaLayer(LiraPlateRebarSlot.XB, 10, 300, 4)),
            [3] = Tza(3, TzaLayer(LiraPlateRebarSlot.XB, 16, 200, 3.5), TzaLayer(LiraPlateRebarSlot.YB, 16, 200, 3.5)),
        },
    };

    static LiraAspFile Asp(params LiraAspPlate[] plates) => new() { Plates = plates.ToDictionary(p => p.ElementId) };

    // Толщина в файле — float32.
    static LiraAspPlate AspPlate(int id, float h = 0.2f, string rebar = "A500", string concrete = "B25") =>
        new(id, 1, 1, 1, 1, 0, h, rebar, concrete);

    [Fact]
    public void Build_TakesPrevailingComboAndTypesCommonToAllElements()
    {
        FemCheckScopeElement[] plates = [Elem(1, "1"), Elem(2, "1 2"), Elem(3, "1 2"), Elem(4, "1"), Elem(5)];
        var asp = Asp(AspPlate(1), AspPlate(2), AspPlate(3), AspPlate(4, h: 0.18f), AspPlate(5));

        var r = LiraPlateSectionTemplates.Build(plates, asp, Rbt(), nominal: null);

        var t = Assert.IsType<LiraPlateTemplate>(r.Template);
        Assert.Equal(new LiraPlateCombo(0.2, "B25", "A500"), t.Combo);
        Assert.Equal("ЛИРА h200 B25 A500 · ТЗА 1", t.Tag);
        Assert.Equal([1], t.BackgroundTypeIds);          // ТЗА 2 есть не у всех; КЭ без ТЗА фон не отменяет
        Assert.Empty(t.NominalFaces);
        Assert.Equal(5, t.Elements);
        Assert.Equal([(new LiraPlateCombo(0.18, "B25", "A500"), 1)], t.OtherCombos);

        double a10 = Math.PI * 0.010 * 0.010 / 4 / 0.3;
        var top = t.Layers.Single(l => l.Zsx > 0);
        var bottom = t.Layers.Single(l => l.Zsx < 0);
        Assert.Equal(a10, top.Asx, 9);
        Assert.Equal(a10, bottom.Asy, 9);
        Assert.Equal(0.1 - 0.04, top.Zsx, 9);
        Assert.Equal(-(0.1 - 0.04), bottom.Zsy, 9);
        Assert.Equal(0.010, bottom.DiameterX, 9);
    }

    [Fact]
    public void Build_WithoutRbt_AsksForNominalRebarAndPutsItOnBothFaces()
    {
        FemCheckScopeElement[] plates = [Elem(1, "1"), Elem(2, "1")];
        var asp = Asp(AspPlate(1), AspPlate(2));

        var asked = LiraPlateSectionTemplates.Build(plates, asp, rbt: null, nominal: null);
        Assert.Null(asked.Template);
        Assert.True(asked.NeedsNominal);
        Assert.Equal(LiraPlateSectionTemplates.DefaultNominal, asked.Suggested);

        var r = LiraPlateSectionTemplates.Build(plates, asp, rbt: null, new LiraPlateNominalRebar(0.035, 0.012));
        var t = Assert.IsType<LiraPlateTemplate>(r.Template);
        Assert.Equal("ЛИРА h200 B25 A500 · d12 a35", t.Tag);
        Assert.Equal(["Z+", "Z−"], t.NominalFaces);
        Assert.Empty(t.BackgroundTypeIds);
        Assert.Equal(2, t.Layers.Count);
        Assert.All(t.Layers, l =>
        {
            Assert.Equal(Math.PI * 0.012 * 0.012 / 4 / 0.2, l.Asx, 9);
            Assert.Equal(0.1 - 0.035, Math.Abs(l.Zsx), 9);
        });
    }

    [Fact]
    public void Build_BackgroundAtOneFace_SuggestsItsCoverForTheOther()
    {
        FemCheckScopeElement[] plates = [Elem(1, "3"), Elem(2, "3 2")];
        var asp = Asp(AspPlate(1), AspPlate(2));

        var asked = LiraPlateSectionTemplates.Build(plates, asp, Rbt(), nominal: null);
        Assert.True(asked.NeedsNominal);
        Assert.Equal(new LiraPlateNominalRebar(0.035, 0.016), asked.Suggested);

        var t = LiraPlateSectionTemplates.Build(plates, asp, Rbt(), asked.Suggested).Template!;
        Assert.Equal("ЛИРА h200 B25 A500 · ТЗА 3 + d16 a35", t.Tag);
        Assert.Equal(["Z+"], t.NominalFaces);
        Assert.Equal(Math.PI * 0.016 * 0.016 / 4 / 0.2, t.Layers.Single(l => l.Zsx < 0).Asx, 9);   // низ — из ТЗА 3
        Assert.Equal(0.1 - 0.035, t.Layers.Single(l => l.Zsx > 0).Zsx, 9);
    }

    [Fact]
    public void Build_RejectsNominalCoverBeyondHalfThickness()
    {
        FemCheckScopeElement[] plates = [Elem(1)];

        var r = LiraPlateSectionTemplates.Build(plates, Asp(AspPlate(1)), rbt: null, new LiraPlateNominalRebar(0.12, 0.01));

        Assert.Null(r.Template);
        Assert.True(r.NeedsNominal);
        Assert.NotNull(r.Problem);
    }

    [Fact]
    public void Build_ElementsMissingInAsp_GivesProblem()
    {
        var r = LiraPlateSectionTemplates.Build([Elem(7, "1")], Asp(AspPlate(1)), Rbt(), nominal: null);

        Assert.Null(r.Template);
        Assert.False(r.NeedsNominal);
        Assert.Contains("ASP", r.Problem);
    }

    /// <summary>У импорта через API толщина КЭ в схеме пуста — источник ТЗА берёт её из подбора, а не от шаблона.</summary>
    [Fact]
    public void AssignedSource_TakesThicknessFromAspWhenElementHasNone()
    {
        var template = new PlateSection { Tag = "Шаблон", H = 0.2 };
        var asp = Asp(AspPlate(1, h: 0.18f), AspPlate(2, h: 0.18f));
        var source = new LiraAssignedPlateSectionSource(template, Rbt(), asp);

        Assert.Equal(0.18, source.Resolve(Elem(1, "1")).Section!.H);
        Assert.Equal(0.25, source.Resolve(Elem(2, "1", thickness: 0.25)).Section!.H);   // своя толщина КЭ главнее
        Assert.Equal(0.2, source.Resolve(Elem(3, "1")).Section!.H);                     // КЭ нет в ASP — от шаблона
        Assert.Equal(-(0.09 - 0.04), source.Resolve(Elem(1, "1")).Section!.RebarLayers.Single(l => l.Zsx < 0).Zsx, 9);
    }
}
