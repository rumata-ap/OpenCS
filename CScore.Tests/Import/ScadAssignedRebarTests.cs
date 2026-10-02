using CScore.Fem;
using CScore.Import;
using CScore.PlateRebar;
using Xunit;

namespace CScore.Tests.Import;

/// <summary>Заданное армирование SCAD: площади, участки стержня, хранение.</summary>
public class ScadAssignedRebarTests
{
    static ScadAssignedPlate Plate(int num, int[] ids, int d = 12, double step = 0.2) =>
        new(num, "плита", ids, [d, d, d, d], [step, step, step, step], 8, 0.4, 0.4);

    static ScadAssignedRodPart Part(int no, double percent, int d1, int n1 = 3) =>
        new(no, percent,
            new ScadRodFace(new ScadBarSet(n1, d1), null, null, 0),
            new ScadRodFace(new ScadBarSet(n1, d1), null, null, 0),
            null, null, null, null);

    [Fact]
    public void Plate_AreaPerMeter_FromDiameterAndStep()
    {
        var p = Plate(1, [1]);
        // ⌀12 шаг 200: 1,131 см² × 5 = 5,655 см²/м.
        Assert.Equal(5.655, p.Area(0), 3);
        // ⌀8 сетка 400×400: 0,503 / 0,16 = 3,142 см²/м².
        Assert.Equal(3.142, p.TransverseArea, 3);
        Assert.False(p.IsEmpty);
    }

    [Fact]
    public void Plate_ZeroDiameterOrStep_NoRebar()
    {
        var p = new ScadAssignedPlate(1, "", [1], [12, 0, 12, 12], [0.2, 0.2, 0, 0.2], 0, 0, 0);
        Assert.Equal(0, p.Area(1));
        Assert.Equal(0, p.Area(2));
        Assert.Equal(0, p.TransverseArea);
        Assert.True(new ScadAssignedPlate(1, "", [1], [0, 0, 0, 0], [0, 0, 0, 0], 0, 0, 0).IsEmpty);
    }

    [Fact]
    public void RodFace_SumsDiametersAndSecondRow()
    {
        // 2⌀20 + 1⌀16 в первом ряду, 2⌀12 во втором.
        var face = new ScadRodFace(new ScadBarSet(2, 20), new ScadBarSet(1, 16), new ScadBarSet(2, 12), 0.05);
        Assert.Equal(2 * 3.1416 + 2.0106 + 2 * 1.1310, face.AreaCm2, 3);
    }

    [Fact]
    public void Stirrups_AreaPerMeter()
    {
        // 2 среза ⌀8 шаг 150: 2 × 0,503 / 0,15 = 6,702 см²/м.
        Assert.Equal(6.702, new ScadRodStirrups(8, 2, 0.15).AreaPerMeter, 3);
        Assert.Equal(0, new ScadRodStirrups(8, 2, 0).AreaPerMeter);
    }

    [Fact]
    public void PartAt_ThreeSections_TwoParts()
    {
        // Участки 30 % (⌀25) и 70 % (⌀16); сечения в 0, 50, 100 %.
        var rod = new ScadAssignedRod(1, "", [1], [Part(1, 30, 25), Part(2, 70, 16)]);
        Assert.Equal(1, rod.PartAt(1, 3)!.PartNo);
        Assert.Equal(2, rod.PartAt(2, 3)!.PartNo);
        Assert.Equal(2, rod.PartAt(3, 3)!.PartNo);
        Assert.Equal(2, rod.Weakest!.PartNo);
    }

    [Fact]
    public void PartAt_OnBoundary_TakesWeakerPart()
    {
        // Граница 50 % совпадает со вторым из трёх сечений.
        var rod = new ScadAssignedRod(1, "", [1], [Part(1, 50, 16), Part(2, 50, 25)]);
        Assert.Equal(1, rod.PartAt(2, 3)!.PartNo);
        Assert.Equal(2, rod.PartAt(3, 3)!.PartNo);
    }

    [Fact]
    public void PartAt_OnePart_ConstantAlongLength()
    {
        var rod = new ScadAssignedRod(1, "", [1], [Part(1, 100, 16)]);
        Assert.Same(rod.Parts[0], rod.PartAt(1, 3));
        Assert.Same(rod.Parts[0], rod.PartAt(5, 0));
    }

    [Fact]
    public void File_FindsGroupsAndCountsElementsInSeveral()
    {
        var file = new ScadAssignedRebarFile(
            [Plate(2, [10, 11]), Plate(1, [11, 12])],
            [new ScadAssignedRod(1, "", [20], [Part(1, 100, 16)])]);
        Assert.Equal(1, file.Plate(11)!.Num);
        Assert.Equal(2, file.Plate(10)!.Num);
        Assert.Null(file.Plate(20));
        Assert.Equal(1, file.Rod(20)!.Num);
        Assert.Equal(1, file.MultiGroupElements);
    }

    [Fact]
    public void File_JsonRoundTrip()
    {
        var rodPart = new ScadAssignedRodPart(1, 100,
            new ScadRodFace(new ScadBarSet(2, 20), new ScadBarSet(1, 16), new ScadBarSet(2, 12), 0.05),
            new ScadRodFace(new ScadBarSet(3, 16), null, null, 0),
            new ScadBarSet(1, 12), null, new ScadRodStirrups(8, 2, 0.15), null);
        var file = new ScadAssignedRebarFile([Plate(1, [1, 2])], [new ScadAssignedRod(3, "колонны", [5], [rodPart])]);

        var back = ScadAssignedRebarFile.FromJson(file.ToJson());

        Assert.Equal(file.Plates[0].Area(0), back.Plate(2)!.Area(0));
        var p = back.Rod(5)!.Parts[0];
        Assert.Equal(rodPart.LongitudinalSum, p.LongitudinalSum, 9);
        Assert.Equal(0.05, p.S1.Row2DeltaM);
        Assert.Null(p.S4);
        Assert.Equal("колонны", back.Rods[0].Name);
    }

    [Fact]
    public void PlateSource_ComponentsAndMissing()
    {
        var file = new ScadAssignedRebarFile([new ScadAssignedPlate(1, "", [5], [12, 0, 10, 0], [0.2, 0.2, 0.2, 0.2], 0, 0, 0)], []);
        var src = new ScadAssignedPlateRebarSource(file);
        Assert.Equal(5.655, src.Get("5", PlateRebarMosaicComponent.BottomX).Value!.Value, 3);
        Assert.Equal(0, src.Get("5", PlateRebarMosaicComponent.TopX).Value);
        Assert.Equal(0, src.Get("5", PlateRebarMosaicComponent.Transverse).Value);
        Assert.Null(src.Get("6", PlateRebarMosaicComponent.BottomX).Value);
    }

    [Fact]
    public void BarSource_SectionsFollowParts_EnvelopeIsMinimum()
    {
        var rod = new ScadAssignedRod(1, "", [7], [Part(1, 30, 25), Part(2, 70, 16)]);
        var src = new ScadAssignedBarRebarSource(new ScadAssignedRebarFile([], [rod]), _ => 3);

        var s = src.GetSections("7", BarRebarComponent.As1).Select(v => v.Value!.Value).ToList();

        Assert.Equal([3 * 4.909, 3 * 2.011, 3 * 2.011], s.Select(v => Math.Round(v, 3)).ToList(), new ToleranceComparer(0.01));
        Assert.Equal(3 * 2.011, src.Get("7", BarRebarComponent.As1).Value!.Value, 2);
        Assert.Empty(src.GetSections("8", BarRebarComponent.As1));
    }

    [Fact]
    public void BarSource_UnknownSectionCount_OnePartGivesOneValue()
    {
        var file = new ScadAssignedRebarFile([], [new ScadAssignedRod(1, "", [7], [Part(1, 100, 16)])]);
        Assert.Single(new ScadAssignedBarRebarSource(file).GetSections("7", BarRebarComponent.LongitudinalSum));
    }

    [Fact]
    public void Difference_BySection_WhenCountsMatch()
    {
        var rod = new ScadAssignedRod(1, "", [7], [Part(1, 50, 25), Part(2, 50, 16)]);
        var assigned = new ScadAssignedBarRebarSource(new ScadAssignedRebarFile([], [rod]), _ => 2);
        var required = new FixedBarSource([10.0, 10.0]);

        var d = new BarRebarDifferenceSource(assigned, required).GetSections("7", BarRebarComponent.As1);

        Assert.Equal(3 * 4.909 - 10, d[0].Value!.Value, 2);
        Assert.Equal(3 * 2.011 - 10, d[1].Value!.Value, 2);
    }

    sealed class FixedBarSource(double[] sections) : IBarRebarFieldSource
    {
        public bool Supports(BarRebarComponent component) => true;
        public PlateRebarValue Get(string elemTag, BarRebarComponent component) => PlateRebarValue.Of(sections.Max());
        public IReadOnlyList<PlateRebarValue> GetSections(string elemTag, BarRebarComponent component) =>
            sections.Select(PlateRebarValue.Of).ToList();
    }

    sealed class ToleranceComparer(double tol) : IEqualityComparer<double>
    {
        public bool Equals(double x, double y) => Math.Abs(x - y) <= tol;
        public int GetHashCode(double obj) => 0;
    }

    [Fact]
    public void File_BrokenJson_Throws() =>
        Assert.Throws<InvalidDataException>(() => ScadAssignedRebarFile.FromJson("{oops"));
}
