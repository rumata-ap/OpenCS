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
    const string NumberFormat = "0.###############################";

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
        if (headers.Count != 13) throw new ArgumentException(null, nameof(headers));

        var config = new CsvConfiguration(CultureInfo.InvariantCulture)
        {
            Delimiter = settings.Delimiter
        };
        using var csv = new CsvWriter(writer, config);
        // Для StreamWriter это культура процесса: в ru-RU десятичная запятая
        // не позволяет Excel принять значения вида 1.06 за даты.
        var numberFormatProvider = writer.FormatProvider;

        foreach (string header in headers)
            csv.WriteField(header);
        csv.NextRecord();

        foreach (var row in rows)
        {
            csv.WriteField(row.Segment);
            csv.WriteField(FormatNumber(row.N, numberFormatProvider));
            csv.WriteField(FormatNumber(row.Mx, numberFormatProvider));
            csv.WriteField(FormatNumber(row.My, numberFormatProvider));
            csv.WriteField(FormatNumber(row.E0, numberFormatProvider));
            csv.WriteField(FormatNumber(row.Ky, numberFormatProvider));
            csv.WriteField(FormatNumber(row.Kz, numberFormatProvider));
            csv.WriteField(FormatNumber(row.NStiffnessRatio, numberFormatProvider));
            csv.WriteField(FormatNumber(row.MxStiffnessRatio, numberFormatProvider));
            csv.WriteField(FormatNumber(row.MyStiffnessRatio, numberFormatProvider));
            csv.WriteField(row.Converged);
            csv.WriteField(row.PsiActive);
            csv.WriteField(row.NonPhysical);
            csv.NextRecord();
        }
    }

    static string FormatNumber(double value, IFormatProvider formatProvider) =>
        value.ToString(NumberFormat, formatProvider);

    static string FormatNumber(double? value, IFormatProvider formatProvider) => value.HasValue
        ? value.Value.ToString(NumberFormat, formatProvider)
        : string.Empty;
}
