using CScore;
using CScore.ParametricSteel;
using CScore.Sp16;
using OpenCS.Utilites;
using OpenCS.ViewModels;
using Xunit;

namespace OpenCS.Tests;

/// <summary>Модель диалога параметрического МК-сечения: поля, сортамент, ориентация, материал.</summary>
public sealed class ParametricSteelSectionVMTests
{
    /// <summary>Сортамент из одной строки 60Б1 (прокатный двутавр h &gt; 500 мм).</summary>
    sealed class FakeSortament : ISteelSortament
    {
        public static readonly SteelCatalogSubtype Subtype = new(18, "Двутавры", "Двутавр по ГОСТ Р 57837-2017");
        public static readonly SteelCatalogEntry Entry = new("Двутавры", Subtype.Name, 18, 7, "60Б1",
            SteelProfileKind.IBeam, SteelFabrication.Rolled, 0.596, 0.199, 0.010, 0.015, 0.022, 0, 0, 120.5, 68720, 1980, 82.37);

        public IReadOnlyList<SteelCatalogSubtype> GetSteelCatalogSubtypes(SteelProfileKind kind, SteelFabrication fabrication) =>
            kind == SteelProfileKind.IBeam && fabrication == SteelFabrication.Rolled ? [Subtype] : [];
        public IReadOnlyList<SteelCatalogProfileItem> GetSteelCatalogProfiles(int subtypeId) =>
            subtypeId == Subtype.Id ? [new SteelCatalogProfileItem(Entry.Id, Entry.Name)] : [];
        public SteelCatalogEntry? GetSteelCatalogEntry(int subtypeId, int profileId) =>
            subtypeId == Subtype.Id && profileId == Entry.Id ? Entry : null;
    }

    static Material Mat(int id, MatType type) => new() { Id = id, Type = type, Tag = $"М{id}" };

    static ParametricSteelSectionVM WithCatalog() =>
        new([Mat(1, MatType.Custom), Mat(2, MatType.Steel)], new FakeSortament());

    [Fact]
    public void DefaultMaterialIsFirstSteelAndMissingMaterialsBlockSave()
    {
        Assert.Equal(2, WithCatalog().MaterialId);

        var empty = new ParametricSteelSectionVM([], null);
        Assert.True(empty.NoSteelMaterials);
        Assert.False(empty.CanSave);
        Assert.Contains("ParametricSteelMissingMaterial", empty.Diagnostics);
    }

    [Fact]
    public void FieldVisibilityFollowsKindAndFabrication()
    {
        var vm = WithCatalog();
        Assert.Equal(SteelProfileKind.IBeam, vm.Kind);
        Assert.Equal(SteelFabrication.Rolled, vm.Fabrication);
        Assert.True(vm.ShowR2);
        Assert.True(vm.ShowSlope);
        Assert.False(vm.ShowBottomFlange);
        Assert.True(vm.HasCatalog);

        vm.Fabrication = SteelFabrication.Welded;
        Assert.True(vm.ShowBottomFlange);
        Assert.False(vm.ShowR1);
        Assert.False(vm.HasCatalog);

        vm.Kind = SteelProfileKind.Channel;
        Assert.Equal(SteelFabrication.Rolled, vm.Fabrication);
        Assert.Equal([SteelFabrication.Rolled, SteelFabrication.Bent, SteelFabrication.Welded],
            vm.FabricationOptions.Select(o => o.Value));
        vm.Fabrication = SteelFabrication.Bent;
        Assert.False(vm.ShowTf);
        Assert.True(vm.ShowR1);
        Assert.Equal("ParametricSteelTMm", vm.TwLabel);
        var bent = vm.BuildDefinition();
        Assert.Equal(bent.Tw, bent.Tf1);
        Assert.Equal(0, bent.FlangeSlope);

        vm.Kind = SteelProfileKind.Pipe;
        Assert.False(vm.ShowB);
        Assert.Equal(0, vm.BuildDefinition().Bf1);
        Assert.True(vm.CanSave);
    }

    [Fact]
    public void CatalogSelectionFillsFieldsAndEditingRemovesReference()
    {
        var vm = WithCatalog();
        vm.SelectedCatalogProfile = vm.CatalogProfiles.Single();

        Assert.Equal(596, vm.HMm, 9);
        Assert.Equal(22, vm.R1Mm, 9);
        Assert.Equal(new ParametricSteelCatalogRef("Двутавры", FakeSortament.Subtype.Name, "60Б1", 82.37e-8), vm.Catalog);
        Assert.True(vm.ShowIt);
        Assert.Equal(82.37, vm.ItCm4!.Value, 9);
        Assert.True(vm.Sp16!.ItFromCatalog);
        Assert.Contains("60Б1", vm.Tag);
        Assert.StartsWith("120", vm.CatalogAreaText);
        Assert.Equal(vm.Catalog, vm.BuildDefinition().Catalog);

        vm.TwMm = 11;

        Assert.Null(vm.Catalog);
        Assert.Null(vm.SelectedCatalogProfile);
        Assert.Equal("", vm.CatalogAreaText);
        Assert.Null(vm.BuildDefinition().Catalog);
        Assert.Equal(0.011, vm.BuildDefinition().Tw, 12);
    }

    [Fact]
    public void LoadDefinitionRestoresCatalogSelectionAndCustomTag()
    {
        var definition = FakeSortament.Entry.ToDefinition() with { MaterialId = 1, Tag = "Балка Б-1", Rotated90 = true };
        var vm = WithCatalog();

        vm.LoadDefinition(definition);

        Assert.Equal(FakeSortament.Entry.Name, vm.SelectedCatalogProfile?.Name);
        Assert.Equal("Балка Б-1", vm.Tag);
        Assert.Equal(1, vm.MaterialId);
        Assert.True(vm.Rotated90);
        var rebuilt = vm.BuildDefinition();
        Assert.Equal(definition.Catalog, rebuilt.Catalog);
        Assert.Equal(definition.H, rebuilt.H, 12);
        Assert.Equal(definition.Tf1, rebuilt.Tf1, 12);
        Assert.Equal(definition.R1, rebuilt.R1, 12);
        Assert.True(rebuilt.Rotated90);
    }

    [Fact]
    public void LoadDefinitionAddsCatalogTorsionConstantToLegacyReference()
    {
        var legacy = FakeSortament.Entry.ToDefinition() with
        {
            MaterialId = 1, Catalog = new ParametricSteelCatalogRef("Двутавры", FakeSortament.Subtype.Name, "60Б1"),
        };
        var vm = WithCatalog();
        vm.LoadDefinition(legacy);
        Assert.Equal(82.37e-8, vm.BuildDefinition().Catalog!.It, 15);
        Assert.Equal(82.37e-8, ParametricSteelSectionGenerator.ToSteelProfile(vm.BuildDefinition()).ItReference, 15);
    }

    [Fact]
    public void DimensionsInMillimetresHaveNoBinaryTails()
    {
        // Сортамент хранит мм и переводит в м: 9 · 0,001 · 1000 = 9,000000000000002 в двоичной арифметике.
        const double mm = 0.001;
        Assert.NotEqual(9.0, 9 * mm * 1000);
        var vm = WithCatalog();
        vm.LoadDefinition(ParametricSteelSectionDefinition.RolledIBeam(300 * mm, 201 * mm, 9 * mm, 15 * mm, 18 * mm) with { MaterialId = 1 });
        Assert.Equal(9.0, vm.TwMm);
        Assert.Equal(18.0, vm.R1Mm);
        Assert.Equal(201.0, vm.BMm);
    }

    [Fact]
    public void DimensionEditDropsCatalogTorsionConstant()
    {
        var vm = WithCatalog();
        vm.SelectedCatalogProfile = vm.CatalogProfiles.Single();
        vm.TfMm = 16;
        Assert.False(vm.Sp16!.ItFromCatalog);
        Assert.Equal(vm.Sp16.ItFormula * 1e8, vm.ItCm4!.Value, 9);
    }

    [Fact]
    public void OrientationIsIgnoredWhereNotAllowedAndApplicabilityUsesSectionAxes()
    {
        var vm = WithCatalog();
        vm.SelectedCatalogProfile = vm.CatalogProfiles.Single();
        // Прокатный двутавр h > 500 мм: в плоскости стенки тип a, из плоскости — c (табл. 7).
        Assert.Equal("a", vm.CurveXText);
        Assert.Equal("c", vm.CurveYText);
        Assert.Equal("1", vm.TableE1Text); // тип 1 табл. Е.1 (в тестах Loc.S возвращает ключ — формат не применяется)

        vm.Rotated90 = true;
        Assert.Equal("c", vm.CurveXText);
        Assert.Equal("a", vm.CurveYText);
        Assert.True(vm.IyCm4 > vm.IxCm4);

        vm.Kind = SteelProfileKind.Pipe;
        Assert.False(vm.CanRotate);
        Assert.False(vm.Rotated90);
        vm.Rotated90 = true;
        Assert.False(vm.BuildDefinition().Rotated90);
    }

    [Fact]
    public void DialogXamlLoadsOnStaThread()
    {
        Exception? error = null;
        var thread = new Thread(() =>
        {
            try
            {
                var dialog = new OpenCS.Views.Dialogs.ParametricSteelSectionDialog(WithCatalog());
                Assert.True(dialog.ViewModel.CanSave);
                dialog.Close();
            }
            catch (Exception ex) { error = ex; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        Assert.Null(error);
    }

    [Fact]
    public void GeneratorErrorsBlockSave()
    {
        var vm = WithCatalog();
        vm.TfMm = 400;
        Assert.False(vm.CanSave);
        Assert.Null(vm.AreaCm2);
    }
}

/// <summary>Сортамент для параметрических МК: соответствие подтипов, уклон, радиусы труб.</summary>
public sealed class ProfileDbSteelCatalogTests
{
    static ProfileDB Db()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "OpenCS.sln"))) dir = dir.Parent;
        return new ProfileDB(Path.Combine(dir!.FullName, "OpenCS", "DataSource", "Sortamenty.db3"));
    }

    static SteelCatalogEntry Find(ProfileDB db, int subtype, string name) =>
        db.GetSteelCatalogEntry(subtype, db.GetSteelCatalogProfiles(subtype).First(p => p.Name == name).Id)!;

    [Fact]
    public void SubtypesAreFilteredByKindAndFabrication()
    {
        var db = Db();
        Assert.Equal([11, 12, 13, 14, 15, 16, 17, 18, 19, 20, 21],
            db.GetSteelCatalogSubtypes(SteelProfileKind.IBeam, SteelFabrication.Rolled).Select(s => s.Id));
        Assert.Equal([28, 29], db.GetSteelCatalogSubtypes(SteelProfileKind.Channel, SteelFabrication.Bent).Select(s => s.Id));
        Assert.Equal([3, 4, 5, 6], db.GetSteelCatalogSubtypes(SteelProfileKind.Angle, SteelFabrication.Bent).Select(s => s.Id));
        Assert.Equal([46], db.GetSteelCatalogSubtypes(SteelProfileKind.Pipe, SteelFabrication.Rolled).Select(s => s.Id));
        Assert.Empty(db.GetSteelCatalogSubtypes(SteelProfileKind.Box, SteelFabrication.Welded));
        Assert.Empty(db.GetSteelCatalogSubtypes(SteelProfileKind.Tee, SteelFabrication.Welded));
    }

    [Fact]
    public void SlopedChannelUsesTenPercentAndRectTubeRadiusIsInner()
    {
        var db = Db();
        var channel = Find(db, 25, "20У");
        Assert.Equal(0.10, channel.FlangeSlope, 12);
        Assert.Equal(0.2, channel.H, 12);

        var ibeam = Find(db, 17, "20");
        Assert.Equal(0.12, ibeam.FlangeSlope, 12);

        var tube = db.GetSteelCatalogProfiles(37).Select(p => db.GetSteelCatalogEntry(37, p.Id)!)
            .First(e => Math.Abs(e.H - 0.2) < 1e-9 && Math.Abs(e.B - 0.1) < 1e-9 && Math.Abs(e.Tw - 0.006) < 1e-9);
        Assert.Equal(SteelProfileKind.Box, tube.Kind);
        Assert.Equal(SteelFabrication.Bent, tube.Fabrication);
        Assert.Equal(0.006, tube.R1, 12);
    }

    [Fact]
    public void CatalogSortReordersProfilesAndKeepsSelection()
    {
        var vm = new ParametricSteelSectionVM([new Material { Id = 1, Type = MatType.Steel, Tag = "С245" }], Db());
        try
        {
            vm.CatalogSort = SteelCatalogSort.Standard;
            vm.SelectedSubtype = vm.CatalogSubtypes.Single(s => s.Id == 18);
            var standard = vm.CatalogProfiles.Select(p => p.Name).ToList();
            Assert.All(vm.CatalogProfiles, p => Assert.Equal("", p.Hint));
            vm.SelectedCatalogProfile = vm.CatalogProfiles.Single(p => p.Name == "30Б1");
            var definition = vm.BuildDefinition();

            vm.CatalogSort = SteelCatalogSort.Wx;
            var wx = vm.CatalogProfiles.Select(p => p.Item.WxCm3 ?? double.MaxValue).ToList();
            Assert.Equal(wx.OrderBy(v => v), wx);
            Assert.All(vm.CatalogProfiles, p => Assert.NotEqual("", p.Hint));
            Assert.Equal("30Б1", vm.SelectedCatalogProfile?.Name);
            Assert.Equal(definition, vm.BuildDefinition());                                     // сортировка не снимает ссылку

            vm.CatalogSort = SteelCatalogSort.Standard;
            Assert.Equal(standard, vm.CatalogProfiles.Select(p => p.Name));
        }
        finally { vm.CatalogSort = SteelCatalogSort.Standard; }
    }

    [Fact]
    public void CatalogProfilesCarrySortValues()
    {
        var p = Db().GetSteelCatalogProfiles(18).Single(x => x.Name == "30Б1");
        Assert.Equal(298, p.HMm!.Value, 6);
        Assert.Equal(6318, p.IxCm4!.Value, 0);
        Assert.True(p.ACm2 > 0 && p.WxCm3 > 0 && p.WyCm3 > 0 && p.IyCm4 > 0);
        var pipe = Db().GetSteelCatalogProfiles(46).First();
        Assert.Equal(pipe.IxCm4, pipe.IyCm4);                                                   // у труб I и W на обе оси
        Assert.NotNull(pipe.WxCm3);
    }

    [Fact]
    public void TorsionConstantIsReadFromCatalog()
    {
        var db = Db();
        Assert.Equal(8.597, Find(db, 18, "30Б1").ItCm4!.Value, 3);
        Assert.Equal(8.597e-8, Find(db, 18, "30Б1").ToCatalogRef().It, 15);
        Assert.True(Find(db, 1, "100 x 100 x 8").ItCm4 is null or > 0);                       // у уголков колонка It, а не J
    }

    [Theory]
    [InlineData(18, "30Б1")]
    [InlineData(17, "20")]
    [InlineData(25, "20У")]
    [InlineData(28, "200 x 80 x 6")]
    [InlineData(1, "100 x 100 x 8")]
    [InlineData(2, "100 x 63 x 8")]
    public void GeneratedAreaMatchesCatalog(int subtype, string name)
    {
        var entry = Find(Db(), subtype, name);
        var result = ParametricSteelSectionGenerator.Generate(entry.ToDefinition() with { MaterialId = 1 });
        Assert.Empty(result.Diagnostics);
        var hull = result.Section.Areas.Single().Hull!;
        var poly = new PolygonSection(hull.Points.Select(p => (p.X, p.Y)), []);
        Assert.InRange(poly.A * 1e4 / entry.ACm2!.Value, 0.99, 1.01);
    }
}
