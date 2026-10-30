using System.Diagnostics;
using CScore;
using CScore.Fem;
using CScore.Planar;
using OpenCS.Services;
using OpenCS.Utilites;
using Xunit.Abstractions;

namespace OpenCS.Gmsh.Tests;

/// <summary>
/// Синтетическое N-этажное здание для сборки сетки схемы волнами: план 18×12 м, этаж 3 м; на этаже — наружные стены
/// с окнами, внутренние x = 6 и x = 12 с проёмами, короткая y = 6 (Т-примыкания), плита перекрытия с лестничным
/// проёмом. Размер КЭ у всех областей один — стены, стоящие друг на друге, не должны выстраиваться в цепочку волн.
/// </summary>
public sealed class StoreyBuildingTests(ITestOutputHelper output)
{
    const string Gmsh = @"C:\Tools\gmsh-4.15.2-Windows64\gmsh.exe";
    const double StoreyH = 3.0;

    /// <summary>Области здания: тег КонЭ и область (Id не назначены).</summary>
    public static List<(string Tag, PlanarRegion Region)> Building(int storeys)
    {
        static Contour Rect(double u0, double v0, double u1, double v1, ContourType type = ContourType.None) =>
            new() { X = [u0, u1, u1, u0], Y = [v0, v0, v1, v1], Type = type };
        static Contour[] Windows(params double[] us) =>
            us.Select(u => Rect(u, 0.9, u + 1.5, 2.1, ContourType.Hole)).ToArray();
        var result = new List<(string, PlanarRegion)>();
        for (int k = 0; k < storeys; k++)
        {
            double z = k * StoreyH;
            Frame3D WallY(double y) => new(new PlanarVector3(0, y, z), new PlanarVector3(1, 0, 0), new PlanarVector3(0, 0, 1), new PlanarVector3(0, -1, 0));
            Frame3D WallX(double x) => new(new PlanarVector3(x, 0, z), new PlanarVector3(0, 1, 0), new PlanarVector3(0, 0, 1), new PlanarVector3(1, 0, 0));
            void Add(string tag, Contour hull, Contour[] holes, Frame3D frame) =>
                result.Add(($"{tag}-{k + 1}", PlanarRegion.CreateFromContour(hull, holes, frame, $"{tag}-{k + 1}")));
            Add("A", Rect(0, 0, 18, StoreyH), Windows(2, 8, 14), WallY(0));
            Add("B", Rect(0, 0, 18, StoreyH), Windows(2, 8, 14), WallY(12));
            Add("1", Rect(0, 0, 12, StoreyH), Windows(8), WallX(0));
            Add("4", Rect(0, 0, 12, StoreyH), Windows(5), WallX(18));
            Add("2", Rect(0, 0, 12, StoreyH), [Rect(3, 0.3, 4, 2.1, ContourType.Hole)], WallX(6));
            Add("3", Rect(0, 0, 12, StoreyH), [Rect(8, 0.3, 9, 2.1, ContourType.Hole)], WallX(12));
            Add("Б", Rect(0, 0, 6, StoreyH), [], WallY(6));
            Add("П", Rect(0, 0, 18, 12), [Rect(8, 4, 10, 8, ContourType.Hole)],
                new Frame3D(new PlanarVector3(0, 0, z + StoreyH), new PlanarVector3(1, 0, 0), new PlanarVector3(0, 1, 0), new PlanarVector3(0, 0, 1)));
        }
        return result;
    }

    /// <summary>Число волн не растёт с этажностью: плиты разных этажей не касаются, стены этажей чередуются.</summary>
    [Fact]
    public void Waves_DoNotGrowWithStoreys()
    {
        int Count(int storeys)
        {
            var building = Building(storeys);
            var members = new List<FemMember>();
            for (int i = 0; i < building.Count; i++)
            {
                building[i].Region.Id = i + 1;
                building[i].Region.MeshMaxElementSizeM = 0.5;
                members.Add(new FemMember { Id = i + 1, ElemTag = building[i].Tag, ElemType = "shell", PlanarRegionId = i + 1, NodeIdsJson = "[]" });
            }
            var ordered = FemPlanarMeshPlan.OrderedRegions(members, building.Select(b => b.Region).ToList());
            var plan = FemPlanarMeshPlan.Waves(ordered);
            foreach (var wave in plan.Waves)
                foreach (var i in wave)
                    Assert.DoesNotContain(plan.Neighbours[i], j => plan.WaveOf[j] == plan.WaveOf[i]);
            return plan.Waves.Count;
        }

        int two = Count(2), eight = Count(8);
        Assert.Equal(two, eight);
        Assert.True(eight <= 6, $"волн {eight}");
    }

    /// <summary>Два этажа на реальном Gmsh: стыки без висящих узлов, общие узлы стен и плит.</summary>
    [Fact]
    public async Task TwoStoreys_BuildWithoutErrors()
    {
        var (result, _) = await BuildAsync(2, 0.6);
        Assert.False(result.HasErrors, string.Join("\n", result.Diagnostics));
        Assert.Equal(16, result.RegionCount);
        Assert.True(result.Mesh!.SharedNodeCount > 100, $"общих узлов {result.Mesh.SharedNodeCount}");
    }

    /// <summary>Замер на 1/5/10… этажах (по переменной OPENCS_BUILDING_STOREYS, например «1,5,10»): полная сборка и
    /// повтор без изменений.</summary>
    [Fact]
    public async Task Scaling_Measure()
    {
        var spec = Environment.GetEnvironmentVariable("OPENCS_BUILDING_STOREYS");
        if (string.IsNullOrEmpty(spec)) return;
        foreach (var storeys in spec.Split(',').Select(int.Parse))
        {
            var (result, times) = await BuildAsync(storeys, 0.5);
            output.WriteLine($"этажей {storeys}: областей {result.RegionCount}, узлов {result.Mesh?.Nodes.Count}, КЭ {result.Mesh?.Elements.Count}, " +
                $"ошибок {result.Diagnostics.Count(d => d.IsError)}; полная {times.Full:F1} с, повтор {times.Reuse:F1} с");
            Assert.False(result.HasErrors, string.Join("\n", result.Diagnostics.Take(10)));
        }
    }

    async Task<(FemSchemaMeshBuildResult Result, (double Full, double Reuse) Times)> BuildAsync(int storeys, double size)
    {
        var root = Path.Combine(Path.GetTempPath(), $"opencs-building-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        try
        {
            using var db = new DatabaseService(Path.Combine(root, "test.db"));
            var schema = new FemSchema { Tag = $"building-{storeys}" };
            db.SaveFemSchema(schema);
            var section = new PlateSection { H = 0.2, Tag = "П200" };
            db.SavePlateSection(section);
            db.SaveFemSchemaEdit(schema.Id, [new FemNode { SchemaId = schema.Id, NodeTag = "1", X = -1, Y = -1, Z = 0 }], [], [], [], []);
            foreach (var (tag, region) in Building(storeys))
            {
                region.MeshMaxElementSizeM = size;
                db.AddPlanarRegion(region, schema.Id);
                db.SaveFemMember(new FemMember
                {
                    SchemaId = schema.Id, ElemTag = tag, ElemType = "shell", NodeIdsJson = "[]", PlanarRegionId = region.Id,
                    PlateSectionId = section.Id,
                });
            }
            var service = new FemSchemaMeshService(db, new GmshSettings
            {
                ExecutablePath = Gmsh, ArtifactsPath = Path.Combine(root, "gmsh"), KeepArtifacts = false,
                ElementMode = PlanarMeshElementMode.Quads, TimeoutSeconds = 300,
            });
            var sw = Stopwatch.StartNew();
            var result = await service.BuildAsync(schema.Id, db.GetFemNodes(schema.Id), db.GetFemMembers(schema.Id), null, null,
                CancellationToken.None);
            var full = sw.Elapsed.TotalSeconds;
            sw.Restart();
            var reuse = await service.BuildAsync(schema.Id, db.GetFemNodes(schema.Id), db.GetFemMembers(schema.Id), null, null,
                CancellationToken.None);
            Assert.Equal(0, reuse.RebuiltRegionCount);
            return (result, (full, sw.Elapsed.TotalSeconds));
        }
        finally
        {
            try { if (Directory.Exists(root)) Directory.Delete(root, recursive: true); }
            catch (IOException) { }
        }
    }
}
