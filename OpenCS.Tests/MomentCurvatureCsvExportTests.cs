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
                Ky = -0.002, Kz = 0.003, Converged = true, PsiActive = false, NonPhysical = false
            },
            new MomentCurvatureBiaxialPointRow
            {
                Segment = 2, N = -11.5, Mx = -21.25, My = 4.75, E0 = 0.0002,
                Ky = -0.004, Kz = 0.005, Converged = false, PsiActive = true, NonPhysical = true
            }
        };
        using var writer = new StringWriter(CultureInfo.InvariantCulture);
        string[] headers = ["Участок", "N", "Mx", "My", "e0", "κy", "κz", "Сошлось", "ψs активно", "Превышено"];

        MomentCurvatureCsvExporter.Write(writer, rows, settings, headers);

        string[] lines = writer.ToString().Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal("Участок,N,Mx,My,e0,κy,κz,Сошлось,ψs активно,Превышено", lines[0]);
        Assert.Equal(3, lines.Length);
        Assert.Contains("1,-10.5,-20.25,3.75,0.0001,-0.002,0.003,True,False,False", lines[1]);
        Assert.Contains("2,-11.5,-21.25,4.75,0.0002,-0.004,0.005,False,True,True", lines[2]);
    }
}
