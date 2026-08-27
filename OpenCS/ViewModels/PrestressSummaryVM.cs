using System;
using System.Collections.ObjectModel;
using System.Text.Json;
using OpenCS.Utilites;

namespace OpenCS.ViewModels;

/// <summary>ViewModel блока сведений о действиях предварительного напряжения.</summary>
public sealed class PrestressSummaryVM : ViewModelBase
{
    public bool HasPrestress { get; private set; }
    public string PrestressReferenceText { get; private set; } = "—";
    public string PrestressNominalNText { get; private set; } = "—";
    public string PrestressNominalMxText { get; private set; } = "—";
    public string PrestressNominalMyText { get; private set; } = "—";
    public string PrestressEffectiveNText { get; private set; } = "—";
    public string PrestressEffectiveMxText { get; private set; } = "—";
    public string PrestressEffectiveMyText { get; private set; } = "—";
    public string PrestressActualNText { get; private set; } = "—";
    public string PrestressActualMxText { get; private set; } = "—";
    public string PrestressActualMyText { get; private set; } = "—";
    public bool HasPrestressAboveStrength { get; private set; }
    public string PrestressAboveStrengthText { get; private set; } = "";
    public ObservableCollection<PrestressRow> PrestressRows { get; } = [];

    /// <summary>Строка с результатами по одной группе предварительно напряжённой арматуры.</summary>
    public record PrestressRow(string Tag, string Area, string Sigma, string Nominal, string Effective,
                               string Actual, string SigmaActual);

    /// <summary>Создаёт модель блока из JSON результата расчёта.</summary>
    public static PrestressSummaryVM Parse(JsonElement root)
    {
        var viewModel = new PrestressSummaryVM();
        viewModel.Read(root);
        return viewModel;
    }

    void Read(JsonElement root)
    {
        if (!root.TryGetProperty("prestress", out var prestress) ||
            !prestress.TryGetProperty("groups", out var groups) ||
            groups.ValueKind != JsonValueKind.Array || groups.GetArrayLength() == 0)
            return;

        HasPrestress = true;

        if (prestress.TryGetProperty("reference", out var reference))
        {
            double x = ReadDouble(reference, "x_m");
            double y = ReadDouble(reference, "y_m");
            PrestressReferenceText = $"x = {x:+0.000;-0.000}; y = {y:+0.000;-0.000}  м";
        }

        if (prestress.TryGetProperty("nominal", out var nominal))
        {
            PrestressNominalNText = FormatForce(nominal, "N_kN", "кН");
            PrestressNominalMxText = FormatForce(nominal, "Mx_kNm", "кН·м");
            PrestressNominalMyText = FormatForce(nominal, "My_kNm", "кН·м");
        }

        if (prestress.TryGetProperty("effective", out var effective))
        {
            PrestressEffectiveNText = FormatForce(effective, "N_kN", "кН");
            PrestressEffectiveMxText = FormatForce(effective, "Mx_kNm", "кН·м");
            PrestressEffectiveMyText = FormatForce(effective, "My_kNm", "кН·м");
        }

        if (prestress.TryGetProperty("actual", out var actual))
        {
            PrestressActualNText = FormatForce(actual, "N_kN", "кН");
            PrestressActualMxText = FormatForce(actual, "Mx_kNm", "кН·м");
            PrestressActualMyText = FormatForce(actual, "My_kNm", "кН·м");
        }

        HasPrestressAboveStrength =
            prestress.TryGetProperty("hasGroupsAboveStrength", out var aboveStrength) &&
            aboveStrength.ValueKind == JsonValueKind.True;

        foreach (var group in groups.EnumerateArray())
        {
            string tag = group.TryGetProperty("tag", out var tagEl) ? tagEl.GetString() ?? "" : "";
            double area = ReadDouble(group, "area_m2");
            double sigma = ReadDouble(group, "sigSp_MPa");
            group.TryGetProperty("nominal", out var groupNominal);
            group.TryGetProperty("effective", out var groupEffective);
            group.TryGetProperty("actual", out var groupActual);
            double sigmaActual = ReadDouble(group, "sigActual_MPa");
            double sigmaLimit = ReadDouble(group, "sigLimit_MPa");
            bool exceeds = group.TryGetProperty("exceedsStrength", out var exceedsEl) &&
                           exceedsEl.ValueKind == JsonValueKind.True;
            if (exceeds && PrestressAboveStrengthText.Length == 0)
                PrestressAboveStrengthText = string.Format(
                    Loc.S("ResultPrestressAboveStrength"),
                    Math.Abs(sigma * ReadFiniteDouble(group, "gammaSp", 1.0)),
                    sigmaLimit,
                    sigmaActual);

            PrestressRows.Add(new PrestressRow(
                tag,
                $"{area:0.000000} м²",
                $"{sigma:+0.0;-0.0} МПа",
                FormatVector(groupNominal),
                FormatVector(groupEffective),
                FormatVector(groupActual),
                $"{sigmaActual:0.0} МПа"));
        }
    }

    static double ReadDouble(JsonElement parent, string name)
        => parent.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Number
            ? value.GetDouble()
            : 0.0;

    static double ReadFiniteDouble(JsonElement parent, string name, double fallback)
    {
        if (!parent.TryGetProperty(name, out var value) || value.ValueKind != JsonValueKind.Number)
            return fallback;
        double number = value.GetDouble();
        return double.IsFinite(number) ? number : fallback;
    }

    static string FormatForce(JsonElement vector, string name, string unit)
        => $"{ReadDouble(vector, name):+0.000;-0.000}  {unit}";

    static string FormatVector(JsonElement vector)
    {
        double n = ReadDouble(vector, "N_kN");
        double mx = ReadDouble(vector, "Mx_kNm");
        double my = ReadDouble(vector, "My_kNm");
        return $"N={n:+0.0;-0.0}; Mx={mx:+0.0;-0.0}; My={my:+0.0;-0.0}";
    }
}
