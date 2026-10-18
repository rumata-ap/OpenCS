using System.Text.Json;
using CScore;
using CScore.ShellSimpl;
using OpenCS.Tasks;
using OpenCS.Utilites;
using Xunit;

namespace OpenCS.Tests;

/// <summary>Задачи подбора сеток Капри-Мори: <see cref="CapraMauryDesignUlsHandler"/> и пакет.</summary>
public sealed class CapraMauryDesignHandlerTests
{
    static string TempPath() => Path.Combine(Path.GetTempPath(), $"capri_design_{Guid.NewGuid():N}.db");
    static void TryDelete(string path) { try { File.Delete(path); } catch { } }

    static (Material concrete, Material rebar) MakeMaterials()
    {
        MaterialChars Concrete(CalcType calc) => new(calc)
        {
            Type = MatType.Concrete, E = 30_000_000.0, Fc = -14_500.0, Ft = 1_050.0,
            Ec0 = -0.002, Ec1 = -0.6 * 14_500.0 / 30_000_000.0, Ec2 = -0.0035,
            Ec1Red = -0.0015, Et0 = 0.0001, Et1 = 0.6 * 1_050.0 / 30_000_000.0, Et2 = 0.00015, Et1Red = 0.00008,
        };
        MaterialChars Rebar(CalcType calc) => new(calc)
        {
            Type = MatType.ReSteelF, E = 200_000_000.0, Fc = -435_000.0, Ft = 435_000.0, Ec2 = -0.025, Et2 = 0.025,
        };
        var concrete = new Material { Id = 1, Num = 1, Type = MatType.Concrete, E = 30_000_000.0 };
        concrete.MaterialChars = [Concrete(CalcType.C), Concrete(CalcType.CL), Concrete(CalcType.N), Concrete(CalcType.NL)];
        var rebar = new Material { Id = 2, Num = 2, Type = MatType.ReSteelF, E = 200_000_000.0 };
        rebar.MaterialChars = [Rebar(CalcType.C), Rebar(CalcType.CL), Rebar(CalcType.N), Rebar(CalcType.NL)];
        return (concrete, rebar);
    }

    static DatabaseService Seed(string path)
    {
        var database = new DatabaseService(path);
        var (concrete, rebar) = MakeMaterials();
        database.Materials.Add(concrete);
        database.Materials.Add(rebar);
        var section = new PlateSection { Id = 7, H = 0.2, ConcreteMaterialId = 1, RebarMaterialId = 2, ConcreteDiagramType = DiagrammType.L3 };
        // Площади сечения в подборе не используются — только привязки рядов.
        section.RebarLayers.Add(new PlateRebarLayer { Name = "низ", Asx = 0.0, Asy = 0.0, Zsx = -0.07, Zsy = -0.07, InputMode = "direct" });
        section.RebarLayers.Add(new PlateRebarLayer { Name = "верх", Asx = 0.0, Asy = 0.0, Zsx = 0.07, Zsy = 0.07, InputMode = "direct" });
        database.PlateSections.Add(section);
        database.ForceSets.Add(new ForceSet
        {
            Id = 9, Kind = "shell", Tag = "РСУ",
            ShellItems =
            [
                new ShellLoadItem { Num = 1, Label = "т.1", Mx = 30.0, My = 10.0, Mxy = 8.0 },
                new ShellLoadItem { Num = 2, Label = "т.2", Mx = -25.0, My = 15.0, Mxy = -12.0 },
                new ShellLoadItem { Num = 3, Label = "т.3", Mx = 5.0, My = 35.0 },
            ],
        });
        return database;
    }

    [Fact]
    public void Single_DesignsBothMeshes()
    {
        string path = TempPath();
        try
        {
            using var database = Seed(path);
            var task = new CalcTask
            {
                Id = 1, Kind = "shell_simpl_capri_design_uls", CalcType = CalcType.C, SectionId = 7,
                ParamsJson = JsonSerializer.Serialize(new ShellSimplParams { Mx = 30.0, My = 10.0, Mxy = 8.0, StripModel = "ndm" }),
            };
            var result = new CapraMauryDesignUlsHandler().Run(task, null!, new LoadItem(), CalcSettings.Default,
                new TaskRunContext { Database = database });

            Assert.True(result.Status == "ok", $"status={result.Status}, data={result.DataJson}");
            using var doc = JsonDocument.Parse(result.DataJson);
            var design = doc.RootElement.GetProperty("design");
            Assert.True(design.GetProperty("Top").GetProperty("Asx").GetDouble() > 0.0);
            Assert.True(design.GetProperty("Top").GetProperty("Asy").GetDouble() > 0.0);
            Assert.Equal(18, design.GetProperty("Facets").GetArrayLength());
            Assert.Equal(0.07, doc.RootElement.GetProperty("z_top").GetDouble(), 9);
        }
        finally { TryDelete(path); }
    }

    [Fact]
    public void Batch_RowsAndEnvelope()
    {
        string path = TempPath();
        try
        {
            using var database = Seed(path);
            var task = new CalcTask
            {
                Id = 2, Kind = "shell_simpl_capri_design_uls_batch", CalcType = CalcType.C, SectionId = 7, ForceSetId = 9,
                ParamsJson = JsonSerializer.Serialize(new ShellSimplParams { StripModel = "ndm" }),
            };
            var result = new CapraMauryDesignUlsBatchHandler().Run(task, null!, new LoadItem(), CalcSettings.Default,
                new TaskRunContext { Database = database });

            Assert.True(result.Status == "ok", $"status={result.Status}, data={result.DataJson}");
            var data = JsonSerializer.Deserialize<CapraMauryDesignBatchData>(result.DataJson)!;
            Assert.Equal(3, data.Total);
            Assert.Equal(3, data.Feasible);
            Assert.Equal(4, data.Envelope.Count);

            // Огибающая — максимум каждой компоненты по строкам, с управляющей строкой.
            var topY = data.Envelope.Single(e => e.Component == "top_y");
            Assert.Equal(data.Rows.Max(r => r.TopAsy), topY.As);
            Assert.Equal(data.Rows.MaxBy(r => r.TopAsy)!.Num, topY.Num);
            Assert.Equal(3, topY.Num);
            var botX = data.Envelope.Single(e => e.Component == "bot_x");
            Assert.Equal(2, botX.Num);

            // Строка пакета совпадает с одиночным подбором тех же усилий.
            var ctx = CapraMauryDesignSolver.CreateUls(database.PlateSections.Single(), database.Materials.First(m => m.Id == 1), database.Materials.First(m => m.Id == 2), CalcType.C);
            var single = CapraMauryDesignSolver.DesignUls(ctx, 0, 0, 0, -25.0, 15.0, -12.0);
            Assert.Equal(single.Bottom.Asx, data.Rows[1].BotAsx, 12);
        }
        finally { TryDelete(path); }
    }

    [Fact]
    public void Run_WithoutDatabase_ReturnsError()
    {
        var task = new CalcTask { Id = 1, Kind = "shell_simpl_capri_design_uls", CalcType = CalcType.C };
        Assert.Equal("error", new CapraMauryDesignUlsHandler().Run(task, null!, new LoadItem(), CalcSettings.Default).Status);
        Assert.Equal("error", new CapraMauryDesignUlsBatchHandler().Run(task, null!, new LoadItem(), CalcSettings.Default).Status);
    }

    [Fact]
    public void KindsAreRegistered()
    {
        Assert.Contains("shell_simpl_capri_design_uls", TaskRunner.KindList);
        Assert.Contains("shell_simpl_capri_design_uls_batch", TaskRunner.KindList);
        Assert.Equal(OpenCS.ViewModels.CalcTaskGroups.Uls, OpenCS.ViewModels.CalcTaskGroups.Classify("shell_simpl_capri_design_uls_batch"));
        Assert.True(CalcTaskForceHelper.UsesDummyForceItem(new CalcTask { Kind = "shell_simpl_capri_design_uls" }));
    }
}
