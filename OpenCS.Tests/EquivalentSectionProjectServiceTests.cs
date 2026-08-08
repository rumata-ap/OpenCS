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
            bool withConflictingPriorityZones = false)
        {
            var region = PlanarRegion.CreateFromContour(new Contour { X = [0, 10, 10, 0], Y = [0, 0, 10, 10] });
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

        public int SavePlateSection()
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
