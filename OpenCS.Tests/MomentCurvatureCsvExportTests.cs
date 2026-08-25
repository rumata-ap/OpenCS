using System.Globalization;
using System.Text;
using OpenCS.Services;
using OpenCS.Utilites;
using OpenCS.ViewModels;
using Xunit;

namespace OpenCS.Tests;

/// <summary>Проверки табличного экспорта траектории «кривизна–момент».</summary>
public sealed class MomentCurvatureCsvExportTests
{
    [Fact]
    public void ResolveEncoding_SupportsDefaultWindows1251Setting()
    {
        Encoding encoding = MomentCurvatureCsvExporter.ResolveEncoding("windows-1251");

        Assert.Equal(1251, encoding.CodePage);
    }

    [Fact]
    public void Write_UsesConfiguredDelimiterAndKeepsUnconvergedPoints()
    {
        var settings = new CsvExportSettings { Delimiter = ",", Encoding = "utf-8" };
        var rows = new[]
        {
            new MomentCurvatureBiaxialPointRow
            {
                Segment = 1, N = -10.5, Mx = -20.25, My = 3.75, E0 = 0.0001,
                Ky = -0.002, Kz = 0.003, NStiffnessRatio = 0.75, MxStiffnessRatio = 0.5,
                MyStiffnessRatio = 0.25, Converged = true, PsiActive = false, NonPhysical = false
            },
            new MomentCurvatureBiaxialPointRow
            {
                Segment = 2, N = -11.5, Mx = -21.25, My = 4.75, E0 = 0.0002,
                Ky = -0.004, Kz = 0.005, NStiffnessRatio = null, MxStiffnessRatio = 0.25,
                MyStiffnessRatio = 0.125, Converged = false, PsiActive = true, NonPhysical = true
            }
        };
        using var writer = new StringWriter(CultureInfo.InvariantCulture);
        string[] headers = ["Участок", "N", "Mx", "My", "e0", "κy", "κz", "EA/EA₀", "B/B₀x", "B/B₀y", "Сошлось", "ψs активно", "Превышено"];

        MomentCurvatureCsvExporter.Write(writer, rows, settings, headers);

        string[] lines = writer.ToString().Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal("Участок,N,Mx,My,e0,κy,κz,EA/EA₀,B/B₀x,B/B₀y,Сошлось,ψs активно,Превышено", lines[0]);
        Assert.Equal(3, lines.Length);
        Assert.Contains("1,-10.5,-20.25,3.75,0.0001,-0.002,0.003,0.75,0.5,0.25,True,False,False", lines[1]);
        Assert.Contains("2,-11.5,-21.25,4.75,0.0002,-0.004,0.005,,0.25,0.125,False,True,True", lines[2]);
    }

    [Fact]
    public void Write_UsesFixedPointNotationForSmallValues()
    {
        var settings = new CsvExportSettings { Delimiter = ";", Encoding = "windows-1251" };
        var rows = new[]
        {
            new MomentCurvatureBiaxialPointRow
            {
                Segment = 1, N = -100.0, Mx = 0.5074, My = 0.0, E0 = -2.07634e-05,
                Ky = -3.754e-05, Kz = 1.977e-05, NStiffnessRatio = 1.0,
                MxStiffnessRatio = 1.1, MyStiffnessRatio = 1.2,
                Converged = true, PsiActive = false, NonPhysical = false
            }
        };
        using var writer = new StringWriter(CultureInfo.InvariantCulture);
        string[] headers = ["Участок", "N", "Mx", "My", "e0", "κy", "κz", "EA/EA0", "B/B0x", "B/B0y", "Сошлось", "Psi_s активно", "Превышено"];

        MomentCurvatureCsvExporter.Write(writer, rows, settings, headers);

        string csv = writer.ToString();
        Assert.DoesNotContain("E-", csv, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("-0.0000207634", csv);
        Assert.Contains("-0.00003754", csv);
        Assert.Contains("0.00001977", csv);
    }

    [Fact]
    public void Write_UsesWriterCultureToAvoidExcelDateDetection()
    {
        var settings = new CsvExportSettings { Delimiter = ";", Encoding = "windows-1251" };
        var rows = new[]
        {
            new MomentCurvatureBiaxialPointRow
            {
                Segment = 1, N = -100.0, Mx = -3.5733, My = 0.7149, E0 = -2.09539e-05,
                Ky = -3.754e-05, Kz = 1.977e-05, NStiffnessRatio = 1.06052917224107,
                MxStiffnessRatio = 1.15949564908542, MyStiffnessRatio = 1.07143258584837,
                Converged = true, PsiActive = false, NonPhysical = false
            }
        };
        using var writer = new StringWriter(CultureInfo.GetCultureInfo("ru-RU"));
        string[] headers = ["Участок", "N", "Mx", "My", "e0", "Ky", "Kz", "EA/EA0", "B/B0x", "B/B0y", "Сошлось", "Psi_s активно", "Превышено"];

        MomentCurvatureCsvExporter.Write(writer, rows, settings, headers);

        string csv = writer.ToString();
        Assert.Contains("1,06052917224107", csv);
        Assert.Contains("1,15949564908542", csv);
        Assert.DoesNotContain("1.06052917224107", csv);
        Assert.DoesNotContain("E-", csv, StringComparison.OrdinalIgnoreCase);
    }
}
