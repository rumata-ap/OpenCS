using System.Text.Json;
using CScore;
using OpenCS.Tasks;
using OpenCS.Utilites;
using Xunit;

namespace OpenCS.Tests;

public sealed class ShellLayeredSlsHandlerTests
{
    static string TempPath() => Path.Combine(Path.GetTempPath(), $"shell_layered_sls_{Guid.NewGuid():N}.db");
    static void TryDelete(string path) { try { File.Delete(path); } catch { } }

    static (Material concrete, Material rebar) MakeMaterials()
    {
        MaterialChars Concrete(CalcType calc) => new(calc)
        {
            Type = MatType.Concrete, E = 32_500_000.0, Fc = -22_000.0, Ft = 1_750.0,
            Ec0 = -0.002, Ec1 = -0.6 * 22_000.0 / 32_500_000.0, Ec2 = -0.0035,
            Ec1Red = -0.0015, Et0 = 1_750.0 / 32_500_000.0,
            Et1 = 0.6 * 1_750.0 / 32_500_000.0, Et2 = 0.00015, Et1Red = 0.00008,
        };
        MaterialChars Rebar(CalcType calc) => new(calc)
        {
            Type = MatType.ReSteelF, E = 200_000_000.0, Fc = -500_000.0,
            Ft = 500_000.0, Ry = 500_000.0, Ru = 500_000.0,
            Ec2 = -0.025, Et2 = 0.025, Ec1Red = -0.0025, Et1Red = 0.0025,
        };
        var concrete = new Material { Id = 1, Num = 1, Type = MatType.Concrete };
        concrete.MaterialChars =
        [
            Concrete(CalcType.C), Concrete(CalcType.CL), Concrete(CalcType.N), Concrete(CalcType.NL),
        ];
        var rebar = new Material { Id = 2, Num = 2, Type = MatType.ReSteelF };
        rebar.MaterialChars =
        [
            Rebar(CalcType.C), Rebar(CalcType.CL), Rebar(CalcType.N), Rebar(CalcType.NL),
        ];
        return (concrete, rebar);
    }

    static PlateSection MakeSection(int concreteMatId, int rebarMatId)
    {
        var section = new PlateSection
        {
            Id = 7, H = 0.200,
            ConcreteMaterialId = concreteMatId, RebarMaterialId = rebarMatId,
        };
        section.RebarLayers.Add(new PlateRebarLayer
        {
            Name = "верх", Asx = 0.001, Zsx = 0.0875, Asy = 0.001, Zsy = 0.0875,
            InputMode = "direct",
        });
        return section;
    }

    [Fact]
    public void Run_WithoutDatabase_ReturnsError()
    {
        var handler = new ShellLayeredSlsHandler();
        var task = new CalcTask { Id = 1, Kind = "shell_layered_sls", CalcType = CalcType.C };

        var result = handler.Run(task, null!, new LoadItem(), CalcSettings.Default);

        Assert.Equal("error", result.Status);
    }

    [Fact]
    public void Run_ProducesFullStripPicture()
    {
        string path = TempPath();
        try
        {
            using var database = new DatabaseService(path);
            var (concrete, rebar) = MakeMaterials();
            database.Materials.Add(concrete);
            database.Materials.Add(rebar);
            var section = MakeSection(concrete.Id, rebar.Id);
            database.PlateSections.Add(section);

            var task = new CalcTask
            {
                Id = 1, Kind = "shell_layered_sls", CalcType = CalcType.C, SectionId = section.Id,
                ParamsJson = new ShellLayeredSlsParams { Mx = 50.0, My = 30.0, Phi1 = 1.0, Phi2 = 0.5 }.ToJson(),
            };

            var result = new ShellLayeredSlsHandler().Run(
                task, null!, new LoadItem(), CalcSettings.Default,
                new TaskRunContext { Database = database });

            Assert.True(result.Status is "ok" or "not_passed", $"status={result.Status}, data={result.DataJson}");
            using var doc = JsonDocument.Parse(result.DataJson);
            var strips = doc.RootElement.GetProperty("Strips").EnumerateArray().ToList();
            Assert.Equal(2, strips.Count);
            Assert.Contains(strips, s => s.GetProperty("Direction").GetString() == "x");
            Assert.Contains(strips, s => s.GetProperty("Direction").GetString() == "y");
            Assert.True(doc.RootElement.GetProperty("AcrcMaxMm").GetDouble() >= 0.0);
            Assert.True(doc.RootElement.GetProperty("Variables").GetProperty("phi1").GetDouble() > 0.0);
        }
        finally { TryDelete(path); }
    }

    // П. 8.2.8/8.2.14: M_crc слоистой модели ищется по деформационной модели, а Wpl = γ·Wred
    // остаётся лишь допускаемым упрощением. Признак того, что решатель подключён и к задаче
    // (а не только к FemCheckRunner): выбор γ на результат не влияет.
    [Fact]
    public void Run_TakesCrackingMomentFromDeformationModel_IgnoringWplGamma()
    {
        double McrcWith(string gamma)
        {
            string path = TempPath();
            try
            {
                using var database = new DatabaseService(path);
                var (concrete, rebar) = MakeMaterials();
                database.Materials.Add(concrete);
                database.Materials.Add(rebar);
                var section = MakeSection(concrete.Id, rebar.Id);
                database.PlateSections.Add(section);

                var task = new CalcTask
                {
                    Id = 1, Kind = "shell_layered_sls", CalcType = CalcType.C, SectionId = section.Id,
                    ParamsJson = new ShellLayeredSlsParams
                    {
                        Mx = 50.0, My = 30.0, Phi1 = 1.0, Phi2 = 0.5, WplGammaMethod = gamma,
                    }.ToJson(),
                };

                var result = new ShellLayeredSlsHandler().Run(
                    task, null!, new LoadItem(), CalcSettings.Default,
                    new TaskRunContext { Database = database });

                Assert.True(result.Status is "ok" or "not_passed", $"status={result.Status}");
                using var doc = JsonDocument.Parse(result.DataJson);
                return doc.RootElement.GetProperty("Strips").EnumerateArray()
                    .First(s => s.GetProperty("Direction").GetString() == "x")
                    .GetProperty("Mcrc").GetDouble();
            }
            finally { TryDelete(path); }
        }

        Assert.Equal(McrcWith("sp63"), McrcWith("snip"), 6);
    }
}
