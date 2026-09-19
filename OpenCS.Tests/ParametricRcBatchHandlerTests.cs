using System.Text.Json;
using CScore;
using CScore.ParametricRc;
using OpenCS.Tasks;
using OpenCS.Utilites;
using Xunit;

namespace OpenCS.Tests;

public sealed class ParametricRcBatchHandlerTests
{
    [Fact]
    public void RunMarksOnlyIncompatibleRowsNotApplicable()
    {
        string path = Path.Combine(Path.GetTempPath(), $"opencs-parametric-batch-{Guid.NewGuid():N}.db");
        try
        {
            using var database = new DatabaseService(path);
            database.ForceSets.Add(new ForceSet
            {
                Id = 7,
                Kind = "bar",
                Items =
                [
                    new LoadItem { Num = 1, Label = "Mx", Mx = 10 },
                    new LoadItem { Num = 2, Label = "biaxial", Mx = 10, My = 1 }
                ]
            });
            var task = new CalcTask
            {
                Id = 1, Kind = "strain_state_batch", ForceSetId = 7, CalcType = CalcType.C
            };
            var section = ParametricRcSectionGenerator.Generate(
                ParametricRcSectionDefinition.Rectangle(0.30, 0.50) with
                {
                    LowerRebar = ParametricLongitudinalLayer.Idealized(
                        0.0012, 0.020, -0.21, IdealizedRebarAxis.Mx)
                }).Section;

            var result = new StrainStateBatchHandler().Run(task, section,
                new LoadItem(), CalcSettings.Default,
                new TaskRunContext { Database = database });

            using var doc = JsonDocument.Parse(result.DataJson);
            Assert.Equal("partial", result.Status);
            Assert.Equal(1, doc.RootElement.GetProperty("not_applicable_count").GetInt32());
            Assert.Equal("not_applicable", doc.RootElement.GetProperty("rows")[1].GetProperty("status").GetString());
        }
        finally
        {
            try { if (File.Exists(path)) File.Delete(path); } catch { }
        }
    }
}
