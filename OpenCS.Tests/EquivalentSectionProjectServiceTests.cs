using CScore;
using CScore.Fem;
using CScore.Planar;
using CScore.PlateRebar;
using CScore.PlateStrip;
using OpenCS.Services;
using OpenCS.Utilites;
using Xunit;

namespace OpenCS.Tests;

public sealed class EquivalentSectionProjectServiceTests
{
    [Fact]
    public void BuildAndSave_RegionNotFound_ReportsBlockingDiagnostic()
    {
        using var fixture = Fixture();
        var result = fixture.Service.BuildAndSave(
            Strip(), fixture.SchemaId, 999, CalcType.C, ReductionPolicy.ConstitutiveIntegration, 2);

        Assert.False(result.IsCalculable);
        Assert.Contains(result.Diagnostics, d => d.Code == "equivalent_section_region_not_found");
    }

    [Fact]
    public void BuildAndSave_BackgroundMemberNotFound_ReportsBlockingDiagnostic()
    {
        using var fixture = Fixture();
        int regionId = fixture.SaveRegion();

        var result = fixture.Service.BuildAndSave(
            Strip(), fixture.SchemaId, regionId, CalcType.C, ReductionPolicy.ConstitutiveIntegration, 2);

        Assert.False(result.IsCalculable);
        Assert.Contains(result.Diagnostics, d => d.Code == "equivalent_section_background_section_not_found");
    }

    [Fact]
    public void BuildAndSave_DuplicateBackgroundMember_PicksMinimalIdAndWarns()
    {
        using var fixture = Fixture();
        int regionId = fixture.SaveRegion();
        int plateA = fixture.SavePlateSection();
        int plateB = fixture.SavePlateSection();
        fixture.SaveMember(regionId, plateA);
        fixture.SaveMember(regionId, plateB);

        var result = fixture.Service.BuildAndSave(
            Strip(), fixture.SchemaId, regionId, CalcType.C, ReductionPolicy.ConstitutiveIntegration, 2);

        Assert.True(result.IsCalculable, string.Join("; ", result.Diagnostics.Select(d => d.Message)));
        Assert.Contains(result.Diagnostics, d => d.Code == "equivalent_section_duplicate_background_member" && !d.IsError);
        Assert.Equal(plateA, result.Section!.SourcePlateSectionId);
    }

    [Fact]
    public void BuildAndSave_HappyPath_SetsProvenanceAndUniformStiffness()
    {
        using var fixture = Fixture();
        int regionId = fixture.SaveRegion();
        int plateId = fixture.SavePlateSection();
        fixture.SaveMember(regionId, plateId);

        var result = fixture.Service.BuildAndSave(
            Strip(), fixture.SchemaId, regionId, CalcType.C, ReductionPolicy.ConstitutiveIntegration, 2, 0.5);

        Assert.True(result.IsCalculable, string.Join("; ", result.Diagnostics.Select(d => d.Message)));
        var section = result.Section!;
        Assert.Equal(fixture.SchemaId, section.SourceSchemaId);
        Assert.Equal(regionId, section.SourceRegionId);
        Assert.Equal(plateId, section.SourcePlateSectionId);
        Assert.Equal(0.5, section.SpanStationFraction, 12);
        Assert.NotEqual("", section.SourceRegionFingerprint);
        Assert.NotEqual("", section.InputFingerprint);
        Assert.True(section.EA > 0.0);
    }

    [Fact]
    public void BuildAndSave_ZoneCoveringHalfWidth_ProducesDifferentStiffnessThanUniform()
    {
        using var fixtureUniform = Fixture();
        int regionUniform = fixtureUniform.SaveRegion();
        int plateUniform = fixtureUniform.SavePlateSection();
        fixtureUniform.SaveMember(regionUniform, plateUniform);
        var uniform = fixtureUniform.Service.BuildAndSave(
            Strip(), fixtureUniform.SchemaId, regionUniform, CalcType.C,
            ReductionPolicy.ConstitutiveIntegration, 2);

        using var fixtureZoned = Fixture();
        int regionZoned = fixtureZoned.SaveRegion(withHeavyZoneCoveringLeftHalf: true);
        int plateZoned = fixtureZoned.SavePlateSection();
        fixtureZoned.SaveMember(regionZoned, plateZoned);
        var zoned = fixtureZoned.Service.BuildAndSave(
            Strip(), fixtureZoned.SchemaId, regionZoned, CalcType.C,
            ReductionPolicy.ConstitutiveIntegration, 2);

        Assert.True(uniform.IsCalculable, string.Join("; ", uniform.Diagnostics.Select(d => d.Message)));
        Assert.True(zoned.IsCalculable, string.Join("; ", zoned.Diagnostics.Select(d => d.Message)));
        Assert.NotEqual(uniform.Section!.EIz, zoned.Section!.EIz, 6);
    }

    [Fact]
    public void BuildAndSave_InvalidStationFraction_ReportsBlockingDiagnosticWithoutThrowing()
    {
        using var fixture = Fixture();
        int regionId = fixture.SaveRegion();
        int plateId = fixture.SavePlateSection();
        fixture.SaveMember(regionId, plateId);

        var result = fixture.Service.BuildAndSave(
            Strip(), fixture.SchemaId, regionId, CalcType.C,
            ReductionPolicy.ConstitutiveIntegration, 2, spanStationFraction: 1.5);

        Assert.False(result.IsCalculable);
        Assert.Contains(result.Diagnostics, d => d.Code == "equivalent_section_invalid_station_fraction");
    }

    [Fact]
    public void BuildAndSave_ZonePriorityConflict_IsNotBlocking()
    {
        using var fixture = Fixture();
        int regionId = fixture.SaveRegion(withConflictingPriorityZones: true);
        int plateId = fixture.SavePlateSection();
        fixture.SaveMember(regionId, plateId);

        var result = fixture.Service.BuildAndSave(
            Strip(), fixture.SchemaId, regionId, CalcType.C, ReductionPolicy.ConstitutiveIntegration, 2);

        Assert.True(result.IsCalculable, string.Join("; ", result.Diagnostics.Select(d => d.Message)));
        Assert.Contains(result.Diagnostics, d => d.Code == "plate_rebar_zone_priority_conflict" && !d.IsError);
    }

    [Fact]
    public void RefreshStale_CorruptedStripGeometry_ReportsBlockingDiagnosticWithoutThrowing()
    {
        using var fixture = Fixture();
        int regionId = fixture.SaveRegion();
        int plateId = fixture.SavePlateSection();
        fixture.SaveMember(regionId, plateId);
        var strip = Strip();
        strip.Geometry.LeftBoundary = [];
        var equivalent = new EquivalentSection
        {
            SourceSchemaId = fixture.SchemaId,
            SourceRegionId = regionId,
            Strip = strip,
            ReductionPolicy = ReductionPolicy.ConstitutiveIntegration,
            WidthIntegrationPoints = 2,
            InputFingerprint = "old-input",
            IsCalculable = true,
            BeamTangent = new double[3, 3]
        };

        bool changed = fixture.Service.RefreshStale(equivalent, CalcType.C);

        Assert.True(changed);
        Assert.False(equivalent.IsCalculable);
        Assert.True(equivalent.IsStale);
        Assert.Contains(equivalent.Diagnostics, d => d.Code == "equivalent_section_invalid_strip");
    }

    [Fact]
    public void RefreshStale_MissingRegion_MarksNotCalculableAndStale()
    {
        using var fixture = Fixture();
        var equivalent = new EquivalentSection
        {
            SourceSchemaId = fixture.SchemaId,
            SourceRegionId = 999,
            InputFingerprint = "old-input",
            IsCalculable = true,
            BeamTangent = new double[3, 3]
        };

        bool changed = fixture.Service.RefreshStale(equivalent, CalcType.C);

        Assert.True(changed);
        Assert.False(equivalent.IsCalculable);
        Assert.True(equivalent.IsStale);
        Assert.Contains(equivalent.Diagnostics, d => d.Code == "equivalent_section_region_not_found");
    }

    // Спека дословно описывает «удаление региона → восстановление ТОГО ЖЕ региона», но
    // AddPlanarRegion использует AUTOINCREMENT — повторное SaveRegion() даёт новый Id, а не тот
    // же самый. Вместо этого сценарий моделирует «фон удалён (FemMember убран) → фон
    // восстановлен (FemMember пересоздан)», регион не трогается — та же архитектурная суть F-2
    // (снятие блокирующей причины восстанавливает IsCalculable без ручного вмешательства), через
    // симметричный путь equivalent_section_background_section_not_found.
    [Fact]
    public void RefreshStale_BackgroundRestoredAfterDeletion_RecoversIsCalculableWithoutManualIntervention()
    {
        using var fixture = Fixture();
        int regionId = fixture.SaveRegion();
        int plateId = fixture.SavePlateSection();
        int memberId = fixture.SaveMember(regionId, plateId);
        var built = fixture.Service.BuildAndSave(
            Strip(), fixture.SchemaId, regionId, CalcType.C, ReductionPolicy.ConstitutiveIntegration, 2);
        Assert.True(built.IsCalculable, string.Join("; ", built.Diagnostics.Select(d => d.Message)));
        var equivalent = built.Section!;

        fixture.DeleteMember(memberId);
        bool changedAfterDelete = fixture.Service.RefreshStale(equivalent, CalcType.C);
        Assert.True(changedAfterDelete);
        Assert.False(equivalent.IsCalculable);

        fixture.SaveMember(regionId, plateId);
        bool changedAfterRestore = fixture.Service.RefreshStale(equivalent, CalcType.C);

        Assert.True(changedAfterRestore);
        Assert.True(equivalent.IsCalculable);
    }

    [Fact]
    public void BuildAndSave_MeshSource_UsesElementRebarAndPersistsKey()
    {
        using var fixture = Fixture();
        int regionId = fixture.SaveRegion();
        int plateId = fixture.SavePlateSection(withRebar: true);
        var member = fixture.SaveMemberWithMesh(regionId, plateId);

        var layout = fixture.Service.BuildAndSave(
            Strip(), fixture.SchemaId, regionId, CalcType.C, ReductionPolicy.ConstitutiveIntegration, 2);
        var mesh = fixture.Service.BuildAndSave(
            Strip(), fixture.SchemaId, regionId, CalcType.C, ReductionPolicy.ConstitutiveIntegration, 2,
            rebarSourceKey: FemCheckRebarSource.Section);

        Assert.True(layout.IsCalculable, string.Join("; ", layout.Diagnostics.Select(d => d.Message)));
        Assert.True(mesh.IsCalculable, string.Join("; ", mesh.Diagnostics.Select(d => d.Message)));
        Assert.Equal("", layout.Section!.RebarSourceKey);
        Assert.Equal(FemCheckRebarSource.Section, mesh.Section!.RebarSourceKey);
        // Источник входит в отпечаток входов: смена источника помечает сечение устаревшим.
        Assert.NotEqual(layout.Section.InputFingerprint, mesh.Section.InputFingerprint);
        // Ключ переживает сохранение, а пересчёт по нему не считает сечение устаревшим.
        var reloaded = fixture.Db.EquivalentSections.Single(e => e.Id == mesh.Section.Id);
        Assert.Equal(FemCheckRebarSource.Section, reloaded.RebarSourceKey);
        fixture.Service.RefreshStale(reloaded, CalcType.C);
        Assert.False(reloaded.IsStale);
        Assert.True(reloaded.IsCalculable);
        Assert.False(string.IsNullOrEmpty(member));
    }

    [Fact]
    public void BuildAndSave_StripAcrossHole_TakesStiffnessOfMaterialWidth()
    {
        using var solidFixture = Fixture();
        int solidRegion = solidFixture.SaveRegion();
        solidFixture.SaveMember(solidRegion, solidFixture.SavePlateSection());
        var solid = solidFixture.Service.BuildAndSave(
            Strip(), solidFixture.SchemaId, solidRegion, CalcType.C, ReductionPolicy.ConstitutiveIntegration, 4);

        using var holedFixture = Fixture();
        // Отверстие 2×1 м посередине полосы шириной 2 м: на станции сечения (0,5) материала — половина ширины.
        int holedRegion = holedFixture.SaveRegion(withHole: true);
        holedFixture.SaveMember(holedRegion, holedFixture.SavePlateSection());
        var holed = holedFixture.Service.BuildAndSave(
            Strip(), holedFixture.SchemaId, holedRegion, CalcType.C, ReductionPolicy.ConstitutiveIntegration, 4);

        Assert.True(solid.IsCalculable, string.Join("; ", solid.Diagnostics.Select(d => d.Message)));
        Assert.True(holed.IsCalculable, string.Join("; ", holed.Diagnostics.Select(d => d.Message)));
        Assert.Equal(0.5, holed.Section!.EA / solid.Section!.EA, 6);
        Assert.Equal(0.5, holed.Section.EIy / solid.Section.EIy, 6);
        Assert.NotEqual(solid.Section.InputFingerprint, holed.Section.InputFingerprint);
    }

    [Fact]
    public void ResolveLiveSources_StripAcrossHole_ScalesSourcesOfElementsInHole()
    {
        using var fixture = Fixture();
        int regionId = fixture.SaveRegion(withHole: true);
        fixture.SaveMember(regionId, fixture.SavePlateSection());
        var built = fixture.Service.BuildAndSave(
            Strip(), fixture.SchemaId, regionId, CalcType.C, ReductionPolicy.ConstitutiveIntegration, 4);
        Assert.True(built.IsCalculable, string.Join("; ", built.Diagnostics.Select(d => d.Message)));

        // 6 элементов по 1 м вдоль u ∈ [2,8]: отверстие u ∈ [4,6] — это 3-й и 4-й элементы (индексы 2 и 3).
        double[] stations = [0, 1.0 / 6, 2.0 / 6, 3.0 / 6, 4.0 / 6, 5.0 / 6, 1];
        var live = fixture.Service.ResolveLiveSources(built.Section!, CalcType.C, stations);

        Assert.True(live.IsCalculable, string.Join("; ", live.Diagnostics.Select(d => d.Message)));
        double Total(int element) => Enumerable.Range(0, live.Grid![element].Count)
            .Sum(i => live.Grid[element][i] is ScaledPlateSectionResponse s ? s.Factor : 1.0);
        Assert.Equal(4.0, Total(1), 9);
        Assert.True(Total(2) < 4.0);
        Assert.True(Total(3) < 4.0);
        Assert.Equal(4.0, Total(4), 9);
    }

    [Fact]
    public void BuildAndSave_MeshSourceWithoutMesh_IsBlockedWithDiagnostic()
    {
        using var fixture = Fixture();
        int regionId = fixture.SaveRegion();
        int plateId = fixture.SavePlateSection(withRebar: true);
        fixture.SaveMember(regionId, plateId);

        var result = fixture.Service.BuildAndSave(
            Strip(), fixture.SchemaId, regionId, CalcType.C, ReductionPolicy.ConstitutiveIntegration, 2,
            rebarSourceKey: FemCheckRebarSource.Section);

        Assert.False(result.IsCalculable);
        Assert.Contains(result.Diagnostics, d => d.Code == "strip_rebar_source_no_elements" && d.IsError);
    }

    static PlateStripBeamAnalogy Strip() => new()
    {
        Id = "strip-service",
        SourceRegionId = 0,
        ExplicitWidthM = 2.0,
        Fingerprint = "strip-fp",
        Geometry = new PlateStripGeometry
        {
            CenterLine = [new PlanarPoint2D(2, 5), new PlanarPoint2D(8, 5)],
            LeftBoundary = [new PlanarPoint2D(2, 6), new PlanarPoint2D(8, 6)],
            RightBoundary = [new PlanarPoint2D(2, 4), new PlanarPoint2D(8, 4)],
            Polygon = [new PlanarPoint2D(2, 4), new PlanarPoint2D(8, 4), new PlanarPoint2D(8, 6), new PlanarPoint2D(2, 6)],
            LengthM = 6.0
        }
    };

    static TestFixture Fixture() => new();

    sealed class TestFixture : IDisposable
    {
        readonly string _path;
        public DatabaseService Db { get; }
        public EquivalentSectionProjectService Service { get; }
        public int SchemaId => 4;

        public TestFixture()
        {
            _path = Path.Combine(Path.GetTempPath(), $"opencs-equivalent-resolver-{Guid.NewGuid():N}.db");
            Db = new DatabaseService(_path);
            var concrete = LinearMaterial(id: 1);
            var rebar = LinearMaterial(id: 2);
            var materials = new Dictionary<int, Material> { [1] = concrete, [2] = rebar };
            Service = new EquivalentSectionProjectService(Db, materials);
        }

        public int SaveRegion(
            bool withHeavyZoneCoveringLeftHalf = false,
            bool withConflictingPriorityZones = false,
            bool withHole = false)
        {
            var region = PlanarRegion.CreateFromContour(
                new Contour { X = [0, 10, 10, 0], Y = [0, 0, 10, 10] },
                withHole ? [new Contour { X = [4, 6, 6, 4], Y = [4.5, 4.5, 5.5, 5.5] }] : null);
            if (withHeavyZoneCoveringLeftHalf)
            {
                region.RebarZones.Add(new RebarZone
                {
                    Name = "left-half",
                    Face = RebarFace.PlusN,
                    Priority = 1,
                    Operation = RebarZoneOperation.Replace,
                    Polygon =
                    [
                        new RebarZonePoint { U = 0, V = 0 }, new RebarZonePoint { U = 6, V = 0 },
                        new RebarZonePoint { U = 6, V = 10 }, new RebarZonePoint { U = 0, V = 10 }
                    ],
                    Layout = new PlateRebarLayer { Face = RebarFace.PlusN, Asx = 0.01, Asy = 0.01, MaterialId = 2 }
                });
            }
            if (withConflictingPriorityZones)
            {
                for (int i = 0; i < 2; i++)
                    region.RebarZones.Add(new RebarZone
                    {
                        Name = $"conflict-{i}",
                        Face = RebarFace.PlusN,
                        Priority = 1,
                        Operation = RebarZoneOperation.Add,
                        Polygon =
                        [
                            new RebarZonePoint { U = 0, V = 0 }, new RebarZonePoint { U = 10, V = 0 },
                            new RebarZonePoint { U = 10, V = 10 }, new RebarZonePoint { U = 0, V = 10 }
                        ],
                        Layout = new PlateRebarLayer { Face = RebarFace.PlusN, Asx = 0.001 * (i + 1), MaterialId = 2 }
                    });
            }
            return Db.AddPlanarRegion(region, SchemaId);
        }

        public int SavePlateSection(bool withRebar = false)
        {
            // ConcreteDiagramType=L2 обязателен: LinearMaterial ниже использует MatType.ReSteelF
            // и для бетона, и для арматуры (по образцу PlateModelTests.LinearConcrete), а
            // MaterialChars.D3L() (запрашивается дефолтным ConcreteDiagramType=L3) для ReSteelF
            // бросает ArgumentException("Диаграмма и материал не совместимы").
            // GetDiagramms(L2) для ReSteelF — уже проверенный рабочий путь.
            var section = new PlateSection
            {
                H = 0.3, NLayers = 20, TensionConcrete = true, PlateModel = "layered",
                ConcreteMaterialId = 1, RebarMaterialId = 2,
                ConcreteDiagramType = DiagrammType.L2
            };
            if (withRebar)
                section.RebarLayers =
                [
                    new PlateRebarLayer
                    {
                        InputMode = "direct", Asx = 0.002, Asy = 0.002, Zsx = -0.12, Zsy = -0.11, Face = RebarFace.MinusN
                    }
                ];
            Db.SavePlateSection(section);
            return section.Id;
        }

        public int SaveMember(int regionId, int plateSectionId)
        {
            var member = new CScore.Fem.FemMember
            {
                SchemaId = SchemaId,
                ElemTag = $"plate-{regionId}",
                ElemType = "shell",
                PlanarRegionId = regionId,
                PlateSectionId = plateSectionId
            };
            Db.SaveFemMember(member);
            return member.Id;
        }

        /// <summary>Конструктивный элемент региона и один КЭ-пластина 10×10 м, покрывающий регион; возвращает тег КЭ.</summary>
        public string SaveMemberWithMesh(int regionId, int plateSectionId)
        {
            SaveMember(regionId, plateSectionId);
            static FemMeshNode N(string tag, double x, double y) => new() { NodeTag = tag, X = x, Y = y, Z = 0 };
            var element = new FemElement
            {
                ElemTag = "1", ElemType = "shell", NodeIdsJson = "[1,2,4,3]", ThicknessM = 0.3,
                SourceMemberTag = $"plate-{regionId}", Origin = FemMember.MeshSourceImported
            };
            Db.SaveFemMeshSnapshot(SchemaId, [N("1", 0, 0), N("2", 10, 0), N("3", 10, 10), N("4", 0, 10)], [element]);
            return element.ElemTag;
        }

        public void DeleteMember(int id) => Db.DeleteFemMember(new CScore.Fem.FemMember { Id = id });

        static Material LinearMaterial(int id)
        {
            MaterialChars Ch(CalcType ct) => new(ct)
            {
                E = 30_000, Ry = 600, Ru = 600, Ft = 600, Fc = -600,
                Ec2 = -0.05, Et2 = 0.05, Type = MatType.ReSteelF,
            };
            var m = new Material { Id = id, E = 30_000, Type = MatType.ReSteelF, Tag = $"lin-{id}" };
            m.MaterialChars = [Ch(CalcType.C), Ch(CalcType.CL), Ch(CalcType.N), Ch(CalcType.NL)];
            return m;
        }

        public void Dispose()
        {
            Db.Dispose();
            try { if (File.Exists(_path)) File.Delete(_path); } catch { }
        }
    }
}
