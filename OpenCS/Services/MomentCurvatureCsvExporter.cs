using System.Globalization;
using System.IO;
using System.Text;
using CsvHelper;
using CsvHelper.Configuration;
using OpenCS.Utilites;
using OpenCS.ViewModels;

namespace OpenCS.Services;

/// <summary>Записывает точки траектории «кривизна–момент» в CSV.</summary>
public static class MomentCurvatureCsvExporter
{
    /// <summary>Возвращает кодировку CSV, включая историческую Windows-1251.</summary>
    public static Encoding ResolveEncoding(string name)
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        return Encoding.GetEncoding(name);
    }

    /// <summary>Записывает все точки в исходном порядке с настройками CSV приложения.</summary>
    public static void Write(TextWriter writer, IEnumerable<MomentCurvatureBiaxialPointRow> rows,
        CsvExportSettings settings, IReadOnlyList<string> headers)
    {
        ArgumentNullException.ThrowIfNull(writer);
        ArgumentNullException.ThrowIfNull(rows);
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(headers);
        if (headers.Count != 10) throw new ArgumentException(null, nameof(headers));

        var config = new CsvConfiguration(CultureInfo.InvariantCulture)
        {
            Delimiter = settings.Delimiter
        };
        using var csv = new CsvWriter(writer, config);

        foreach (string header in headers)
            csv.WriteField(header);
        csv.NextRecord();

        foreach (var row in rows)
        {
            csv.WriteField(row.Segment);
            csv.WriteField(row.N);
            csv.WriteField(row.Mx);
            csv.WriteField(row.My);
            csv.WriteField(row.E0);
            csv.WriteField(row.Ky);
            csv.WriteField(row.Kz);
            csv.WriteField(row.Converged);
            csv.WriteField(row.PsiActive);
            csv.WriteField(row.NonPhysical);
            csv.NextRecord();
        }
    }
}
