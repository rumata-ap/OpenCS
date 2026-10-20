using System.IO;
using System.Text.Json;
using CScore;
using OpenCS.Tasks;
using OpenCS.Utilites;
using Xunit;

namespace OpenCS.Tests;

/// <summary>
/// Предельное нагружение без масштабируемой составляющей (N = 0 в задаче по N, Mx = My = 0 в задаче по M):
/// если фиксированная часть сечению не по силам ни при каком k, коэффициент использования бесконечен —
/// результат пишется с пустым Кисп, а не падает целиком на сериализации ∞.
/// </summary>
public sealed class LimitForceNonFiniteTests : IDisposable
{
    readonly string _path = Path.Combine(Path.GetTempPath(), $"opencs_limitinf_{Guid.NewGuid():N}.db");
    readonly DatabaseService _db;

    public LimitForceNonFiniteTests()
    {
        _db = new DatabaseService(_path);
        _db.ForceSets.Add(new ForceSet
        {
            Id = 1, Kind = "bar", Tag = "РСУ",
            Items =
            [
                new LoadItem { Id = 1, Num = 1, Label = "посильная", N = -100, Mx = -60 },
                // Момент, которого балка не держит ни при каком N: в задаче по N без N — Кисп = ∞.
                new LoadItem { Id = 2, Num = 2, Label = "N = 0, M сверх", N = 0, Mx = -5000 },
                // Сжатие сверх несущей способности без моментов: в задаче по M — Кисп = ∞.
                new LoadItem { Id = 3, Num = 3, Label = "M = 0, N сверх", N = -1e6 },
            ],
        });
    }

    public void Dispose()
    {
        _db.Dispose();
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try { File.Delete(_path); } catch (IOException) { }
    }

    [Theory]
    [InlineData("limit_axial_batch", "bisection", 2)]
    [InlineData("limit_axial_batch", "fast", 2)]
    [InlineData("limit_moment_batch", "bisection", 3)]
    [InlineData("limit_moment_batch", "fast", 3)]
    public void Batch_InfeasibleFixedPart_RowWithoutUtilization(string kind, string solver, int infeasibleNum)
    {
        var task = new CalcTask
        {
            Id = 1, Kind = kind, CalcType = CalcType.C, ForceSetId = 1,
            ParamsJson = $$"""{"solver":"{{solver}}"}""",
        };

        var result = TaskRunner.Run(task, ReportFixtures.BuildBeam(), new LoadItem(), CalcSettings.Default,
            new TaskRunContext { Database = _db });

        Assert.NotEqual("error", result.Status);
        using var doc = JsonDocument.Parse(result.DataJson);
        var rows = doc.RootElement.GetProperty("rows").EnumerateArray().ToList();
        var bad = rows.Single(r => r.GetProperty("num").GetInt32() == infeasibleNum);
        Assert.Equal(JsonValueKind.Null, bad.GetProperty("utilization").ValueKind);
        var ok = rows.Single(r => r.GetProperty("num").GetInt32() == 1);
        Assert.True(ok.GetProperty("utilization").GetDouble() > 0);
    }

    [Theory]
    [InlineData("limit_axial", 2)]
    [InlineData("limit_moment", 3)]
    public void Single_InfeasibleFixedPart_ResultWithoutUtilization(string kind, int num)
    {
        var item = _db.ForceSets[0].Items.Single(i => i.Num == num);
        var task = new CalcTask { Id = 2, Kind = kind, CalcType = CalcType.C, ParamsJson = """{"solver":"bisection"}""" };

        var result = TaskRunner.Run(task, ReportFixtures.BuildBeam(), item, CalcSettings.Default,
            new TaskRunContext { Database = _db });

        Assert.NotEqual("error", result.Status);
        using var doc = JsonDocument.Parse(result.DataJson);
        Assert.Equal(JsonValueKind.Null, doc.RootElement.GetProperty("utilization").ValueKind);
    }
}
