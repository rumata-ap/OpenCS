using CScore;
using CScore.Fem;
using CScore.Planar;
using CScore.PlateRebar;
using CScore.PlateStrip;
using OpenCS.Services;
using OpenCS.ViewModels;
using Xunit;

namespace OpenCS.Tests;

/// <summary>
/// Диалог плоского КонЭ: привязка точек, рисование зоны армирования мышью и вкладка «Полоса» —
/// указание оси мышью и построение эквивалентного сечения по КЭ сетки схемы.
/// </summary>
public sealed class PlanarRegionMemberVmStripTests
{
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

    /// <summary>Схема с плитой 10×10 м (КонЭ с сечением и одним КЭ на весь регион) и открытым VM диалога.</summary>
    static void WithVm(Action<PlanarRegionMemberVM, AppViewModel> test)
    {
        string path = Path.Combine(Path.GetTempPath(), $"opencs-region-strip-vm-{Guid.NewGuid():N}.db");
        var app = new AppViewModel(new LogService(), new NullFileDialogService(), path);
        try
        {
            app.Materials.Add(LinearMaterial(1));
            app.Materials.Add(LinearMaterial(2));
            var schema = new FemSchema { Tag = "Схема SCAD", SourceType = "scad" };
            app.db.SaveFemSchema(schema);

            var region = PlanarRegion.CreateFromContour(new Contour { X = [0, 10, 10, 0], Y = [0, 0, 10, 10] });
            app.db.AddPlanarRegion(region, schema.Id);
            var section = new PlateSection
            {
                H = 0.3, NLayers = 20, TensionConcrete = true, PlateModel = "layered",
                ConcreteMaterialId = 1, RebarMaterialId = 2, ConcreteDiagramType = DiagrammType.L2,
                RebarLayers =
                [
                    new PlateRebarLayer { InputMode = "direct", Asx = 0.002, Asy = 0.002, Zsx = -0.12, Zsy = -0.11, Face = RebarFace.MinusN }
                ]
            };
            app.db.SavePlateSection(section);

            var member = new FemMember
            {
                SchemaId = schema.Id, ElemTag = "Плита 1", ElemType = "shell", NodeIdsJson = "[]",
                PlanarRegionId = region.Id, PlateSectionId = section.Id, Kind = "plate"
            };
            app.db.SaveFemMember(member);
            app.db.SaveFemMeshSnapshot(schema.Id,
                [new FemMeshNode { NodeTag = "1", X = 0, Y = 0 }, new FemMeshNode { NodeTag = "2", X = 10, Y = 0 },
                 new FemMeshNode { NodeTag = "3", X = 10, Y = 10 }, new FemMeshNode { NodeTag = "4", X = 0, Y = 10 }],
                [new FemElement
                {
                    ElemTag = "1", ElemType = "shell", NodeIdsJson = "[1,2,4,3]", ThicknessM = 0.3,
                    SourceMemberTag = member.ElemTag, Origin = FemMember.MeshSourceImported
                }]);

            var saved = app.db.GetPlanarRegions(schema.Id).Single();
            var vm = new PlanarRegionMemberVM(app, schema, saved.Frame, member, saved);
            // «Бетон» теста — линейный материал типа арматуры (как в EquivalentSectionProjectServiceTests): список бетонов
            // проекта его не содержит, и VM сечения при открытии обнулил бы ссылку на материал.
            section.ConcreteMaterialId = 1;
            test(vm, app);
        }
        finally
        {
            app.db.Dispose();
            foreach (var suffix in new[] { "", "-wal", "-shm" })
                try { File.Delete(path + suffix); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        }
    }

    [Fact]
    public void ZoneDrawing_SnapsToHullCornersAndCreatesZone() => WithVm((vm, _) =>
    {
        vm.BeginZoneDrawing();
        // Пиксель 0,05 м: допуск привязки 0,5 м — точки рядом с углами контура встают точно в углы.
        vm.AddDrawingPoint(0.2, 0.1, 0.05);
        vm.AddDrawingPoint(9.8, 0.2, 0.05);
        vm.AddDrawingPoint(9.9, 9.9, 0.05);
        Assert.Equal(3, vm.DrawingPointCount);

        Assert.True(vm.FinishZoneDrawing());

        Assert.False(vm.IsDrawingZone);
        var zone = Assert.Single(vm.RebarZones).Model;
        Assert.Equal([(0.0, 0.0), (10.0, 0.0), (10.0, 10.0)], zone.Polygon.Select(p => (p.U, p.V)).ToArray());
        Assert.Equal(vm.ActiveRebarFace, zone.Face);
        Assert.True(vm.RebarLayoutDirty);
    });

    [Fact]
    public void ZoneDrawing_LessThanThreePoints_IsCancelled() => WithVm((vm, _) =>
    {
        vm.BeginZoneDrawing();
        vm.AddDrawingPoint(2, 2, 0.01);
        vm.AddDrawingPoint(5, 2, 0.01);

        Assert.False(vm.FinishZoneDrawing());

        Assert.False(vm.IsDrawingZone);
        Assert.Empty(vm.RebarZones);
    });

    [Fact]
    public void ZoneDrawing_UndoRemovesLastPoint() => WithVm((vm, _) =>
    {
        vm.SnapEnabled = false;
        vm.BeginZoneDrawing();
        vm.AddDrawingPoint(1, 1, 0.01);
        vm.AddDrawingPoint(4, 1, 0.01);
        vm.UndoDrawingPoint();

        Assert.Equal(1, vm.DrawingPointCount);
        vm.CancelZoneDrawing();
        Assert.False(vm.IsDrawingZone);
    });

    [Fact]
    public void StripAxis_PickedWithMouse_SnapsOrthogonallyAndBuildsSection() => WithVm((vm, app) =>
    {
        vm.EnsureStripInitialized();
        Assert.True(vm.CanBuildStrip);
        // У схемы SCAD без файлов армирования — только раскладка региона и сечение-шаблон.
        Assert.Contains(vm.StripRebarSources, o => o.Key == "");

        vm.BeginStripAxisPicking();
        Assert.True(vm.IsPickingStripAxis);
        vm.OnStripCanvasClick(2.0, 5.0, 0.001);
        // Конец почти на той же отметке V: ортогональная привязка выравнивает V по началу.
        vm.OnStripCanvasClick(8.0, 5.01, 0.001);

        Assert.False(vm.IsPickingStripAxis);
        Assert.Equal((2.0, 5.0), (vm.StripStartU!.Value, vm.StripStartV!.Value));
        Assert.Equal((8.0, 5.0), (vm.StripEndU!.Value, vm.StripEndV!.Value));
        Assert.Equal(6.0, vm.StripLengthM, 9);

        vm.StripName = "Полоса X";
        vm.StripWidthM = 2.0;
        vm.StripRebarSourceKey = FemCheckRebarSource.Section;
        vm.BuildStrip();

        Assert.DoesNotContain(vm.StripDiagnostics, d => d.IsError);
        var built = Assert.Single(app.db.EquivalentSections);
        Assert.Equal("Полоса X", built.Tag);
        Assert.True(built.IsCalculable);
        Assert.Equal(FemCheckRebarSource.Section, built.RebarSourceKey);
        Assert.Equal(6.0, built.Strip.Geometry.LengthM, 9);
        Assert.Equal(2.0, built.Strip.ExplicitWidthM, 9);
        Assert.Same(built, vm.SelectedEquivalent);
        Assert.Single(vm.RegionEquivalentSections);
    });

    [Fact]
    public void BuildStrip_WithoutAxis_ReportsDiagnostic() => WithVm((vm, app) =>
    {
        vm.EnsureStripInitialized();

        vm.BuildStrip();

        Assert.Contains(vm.StripDiagnostics, d => d.Code == "strip_axis_not_set");
        Assert.Empty(app.db.EquivalentSections);
    });

    [Fact]
    public void SelectingExistingSection_LoadsItsParameters_AndRebuildUpdatesInPlace() => WithVm((vm, app) =>
    {
        vm.EnsureStripInitialized();
        vm.BeginStripAxisPicking();
        vm.OnStripCanvasClick(1.0, 4.0, 0.001);
        vm.OnStripCanvasClick(9.0, 4.0, 0.001);
        vm.StripName = "S1";
        vm.BuildStrip();
        var first = Assert.Single(app.db.EquivalentSections);

        vm.SelectedEquivalent = null;
        vm.SelectedEquivalent = first;

        Assert.Equal("S1", vm.StripName);
        Assert.Equal(8.0, vm.StripLengthM, 9);

        vm.StripWidthM = 1.5;
        vm.BuildStrip();

        var only = Assert.Single(app.db.EquivalentSections);
        Assert.Equal(first.Id, only.Id);
        Assert.Equal(1.5, only.Strip.ExplicitWidthM, 9);
    });
}
