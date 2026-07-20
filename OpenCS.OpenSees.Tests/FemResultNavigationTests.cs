using CScore;
using OpenCS.OpenSees.CScore;
using OpenCS.Utilites;

namespace OpenCS.OpenSees.Tests;

public sealed class FemResultNavigationTests
{
    [Fact]
    public void Result_identity_prefers_source_member_tag()
    {
        Assert.Equal("member-7", FemResultIdentity.ResolveMemberTag("member-7", 101));
        Assert.Equal("101", FemResultIdentity.ResolveMemberTag(null, 101));
    }

    [Theory]
    [InlineData(1.0, true)]
    [InlineData(0.0, false)]
    [InlineData(-1.0, false)]
    [InlineData(double.NaN, false)]
    [InlineData(double.PositiveInfinity, false)]
    public void Scale_input_accepts_only_finite_positive_values(double value, bool expected)
    {
        Assert.Equal(expected, OpenCS.ViewModels.FemScaleInput.IsValid(value));
    }

    [Fact]
    public void Database_loads_calc_result_by_id()
    {
        string path = Path.Combine(Path.GetTempPath(), "opencs-result-" + Guid.NewGuid().ToString("N") + ".db");
        try
        {
            using (var db = new DatabaseService(path))
            {
                var expected = new CalcResult
                {
                    TaskId = 0,
                    TaskKind = "fem_linear",
                    TaskTag = "test",
                    Created = "2026-07-20 00:00:00",
                    Status = "ok",
                    DataJson = "{}"
                };

                db.SaveCalcResult(expected);
                var actual = db.GetCalcResultById(expected.Id);

                Assert.NotNull(actual);
                Assert.Equal(expected.Id, actual!.Id);
                Assert.Equal(expected.DataJson, actual.DataJson);
            }
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }
}
