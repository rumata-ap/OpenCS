using System.IO;
using CScore;
using CScore.Sp63.Deflection;
using OpenCS.Services;
using OpenCS.Utilites;

namespace OpenCS.Tests;

/// <summary>
/// Маленький файл проекта на диске для тестов без UI (контекст проекта, консоль): бетон B25 и арматура А500С из каталога,
/// прямоугольное сечение 500×300 с двумя стержнями и задача прогиба СП 63 с ручными усилиями.
/// </summary>
static class TestProjectDb
{
    /// <summary>Создаёт проект в <paramref name="path"/>; возвращает id сечения и задачи.</summary>
    public static (int SectionId, int TaskId) Create(string path)
    {
        using var db = new DatabaseService(path);
        var concrete = MaterialCatalog.CreateHeavyConcrete("B25")!;
        var rebar = MaterialCatalog.CreateRebar("А500С")!;
        db.SaveMaterial(concrete);
        db.SaveMaterial(rebar);

        var section = new CrossSection { Num = 1, Tag = "Балка" };
        var region = new MaterialArea { Category = AreaCategory.Region, Material = concrete, MaterialId = concrete.Id };
        region.Contours.Add(new Contour([-0.25, 0.25, 0.25, -0.25, -0.25],
            [-0.15, -0.15, 0.15, 0.15, -0.15], "hull") { Type = ContourType.Hull });
        region.SetWKT();
        section.Areas.Add(region);
        foreach (double y in new[] { -0.11, 0.11 })
        {
            var group = new MaterialArea { Category = AreaCategory.RebarGroup, Material = rebar, MaterialId = rebar.Id };
            group.Fibers.Add(new Fiber { TypeFiber = FiberType.point, X = 0, Y = y, Area = 20e-4, Diameter = 0.02 });
            section.Areas.Add(group);
        }
        foreach (var area in section.Areas) db.SaveMaterialArea(area);
        db.SaveCrossSection(section);

        var task = new CalcTask
        {
            Num = 1, Tag = "Прогиб", Kind = "sp63_deflection", CalcType = CalcType.N, SectionId = section.Id,
            ParamsJson = new Sp63DeflectionTaskParams
            {
                UseManualForces = true, N = 10, Mx = 80, My = 0,
                ForcesMode = "manual", NLongManual = 4, MxLongManual = 20,
                MyLongManual = 0, Scheme = "simply_supported_uniform", SpanM = 6,
                DeflectionLimitMm = 30
            }.ToJson()
        };
        db.SaveCalcTask(task);
        return (section.Id, task.Id);
    }

    /// <summary>Удаляет файл проекта после теста (пулы соединений SQLite держат его открытым).</summary>
    public static void Delete(string path)
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try { File.Delete(path); } catch (IOException) { }
    }
}
