using CScore.Fem;
using CScore.Import;
using Xunit;

namespace CScore.Tests;

/// <summary>Свойства КЭ своей схемы по сечениям проекта и составной источник.</summary>
public sealed class ProjectElementStiffnessSourceTests
{
    const double Ec = 32_500_000, Es = 200_000_000; // кПа, как Material.E в БД

    static (ProjectElementStiffnessSource Source, CrossSection Section) Build(params FemMember[] extra)
    {
        var section = SectionCutFixtures.BuildReinforcedRectangle(0.3, 0.5, 41, 41);
        section.Id = 5;
        var concrete = SectionCutFixtures.BuildConcreteMaterial();
        var plate = new PlateSection { Id = 7, H = 0.2, ConcreteMaterialId = concrete.Id };
        FemMember[] members =
        [
            new() { ElemTag = "B1", ElemType = "beam", CrossSectionId = 5, GjStrategy = "manual", GjManualValue = 1e7 },
            new() { ElemTag = "P1", ElemType = "shell", PlateSectionId = 7 },
            .. extra,
        ];
        return (new ProjectElementStiffnessSource(members, [section], [plate], [concrete, SectionCutFixtures.BuildSteelMaterial()]),
            section);
    }

    static FemElement Beam(string member, int? section = null) =>
        new() { ElemTag = "1", ElemType = "beam", SourceMemberTag = member, CrossSectionId = section };

    [Fact]
    public void Bar_TransformedPropertiesAboutCentroid_AxesAsOpenSees()
    {
        var (src, _) = Build();
        var bar = src.Bar(Beam("B1"))!;
        double asBar = Math.PI * 0.025 * 0.025 / 4, rx = 0.1, ry = 0.2;
        double ea = (Ec * 0.15 + Es * 4 * asBar) * 1e3;
        Assert.Equal(1, bar.E * bar.A / ea, 3);
        // ось X сечения → местная z: изгиб вокруг местной y — по ширине 0,3 (x²)
        Assert.InRange(bar.E * bar.Iy / ((Ec * 0.5 * Math.Pow(0.3, 3) / 12 + Es * 4 * asBar * rx * rx) * 1e3), 0.995, 1.0);
        Assert.InRange(bar.E * bar.Iz / ((Ec * 0.3 * Math.Pow(0.5, 3) / 12 + Es * 4 * asBar * ry * ry) * 1e3), 0.995, 1.0);
        Assert.Equal(bar.E / 2.4, bar.G, 3); // бетон преобладает — ν 0,2
        Assert.Equal(1e7, bar.G * bar.J, 3);  // ручной GJ конструктивного элемента
        double expectedWeight = (0.15 * 25e3 + 4 * asBar * 78.5e3) / (0.15 + 4 * asBar);
        Assert.Equal(expectedWeight, src.UnitWeight(Beam("B1"))!.Value, 6);
        Assert.Equal(0.15 + 4 * asBar, src.BarArea(Beam("B1"))!.Value, 4);
    }

    [Fact]
    public void Bar_WithoutGj_PolarMoment_ElementSectionFirst()
    {
        var (src, _) = Build(new FemMember { ElemTag = "B2", ElemType = "beam", CrossSectionId = 99 });
        Assert.Null(src.Bar(Beam("B2")));
        var bar = src.Bar(Beam("B2", section: 5))!; // сечение КЭ важнее сечения КонЭ; стратегия КЭ manual без значения
        Assert.Equal(bar.Iy + bar.Iz, bar.J, 12);
    }

    [Fact]
    public void Bar_SaintVenant_FromTorsionTask()
    {
        var section = SectionCutFixtures.BuildReinforcedRectangle(0.3, 0.5);
        section.Id = 5;
        var member = new FemMember { ElemTag = "B3", CrossSectionId = 5, GjStrategy = "saint_venant", GjTorsionTaskId = 42 };
        var src = new ProjectElementStiffnessSource([member], [section], [], [], task => task == 42 ? 2e7 : null);
        var bar = src.Bar(Beam("B3"))!;
        Assert.Equal(2e7, bar.G * bar.J, 3);
    }

    [Fact]
    public void Shell_PlateSectionOfMember()
    {
        var (src, _) = Build();
        var e = new FemElement { ElemTag = "2", ElemType = "shell", SourceMemberTag = "P1" };
        var s = src.Shell(e)!;
        Assert.Equal(Ec * 1e3, s.E, 3);
        Assert.Equal(0.2, s.Nu, 12);
        Assert.Equal(0.2, s.H, 12);
        Assert.Equal(25e3, src.UnitWeight(e)!.Value, 9);
        Assert.Null(src.Shell(new FemElement { ElemTag = "3", ElemType = "shell", SourceMemberTag = "B1" }));
        Assert.Null(src.Bar(e));
    }

    [Fact]
    public void Composite_ImportedFirst_ThenProject_AllAnswersFromOneSource()
    {
        var (project, _) = Build();
        var lira = new LiraElementStiffnessSource(new Dictionary<int, LiraStiffnessRecord>
        {
            [3] = new(3, 0, "", "Ro:2.5 E:3e6 B:20 H:40 BAR_END Mu:0.2 STD_END", 0.01),
            [4] = new(4, 0, "", "Ro:2.5 E:0 B:20 H:40 BAR_END STD_END", 0.01),
        }, new LiraUnits(1000, 1));
        var src = new FemCompositeStiffnessSource(lira, project);

        var imported = new FemElement { ElemTag = "1", ElemType = "beam", StiffnessNum = 3, SourceMemberTag = "B1" };
        Assert.Equal(0.08, src.BarArea(imported)!.Value, 12);
        Assert.Equal(2.5e3, src.UnitWeight(imported)!.Value, 9);

        // жёсткость ЛИРЫ без свойств — отвечает сечение проекта, вес и площадь тоже его
        var fallback = new FemElement { ElemTag = "2", ElemType = "beam", StiffnessNum = 4, SourceMemberTag = "B1" };
        Assert.Equal(project.Bar(fallback), src.Bar(fallback));
        Assert.Equal(project.UnitWeight(fallback), src.UnitWeight(fallback));
        Assert.Equal(project.BarArea(fallback), src.BarArea(fallback));
    }
}
