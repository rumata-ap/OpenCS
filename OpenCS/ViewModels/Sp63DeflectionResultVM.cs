using System.Collections.ObjectModel;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Windows;
using System.Windows.Media;
using CScore;
using CScore.Sp63.Deflection;
using OpenCS.Utilites;

namespace OpenCS.ViewModels;

/// <summary>Строка составляющей формульной кривизны.</summary>
public sealed class Sp63DeflectionCurvatureRow
{
    public string Name { get; init; } = "";
    public string Duration { get; init; } = "";
    public string Moment { get; init; } = "";
    public string AxialForce { get; init; } = "";
    public string Modulus { get; init; } = "";
    public string Stiffness { get; init; } = "";
    public string Curvature { get; init; } = "";
}

/// <summary>Строка сообщения результата прогиба.</summary>
public sealed class Sp63DeflectionMessageRow
{
    public string Text { get; init; } = "";
    public string NormReference { get; init; } = "";
}

/// <summary>Представляет результат отдельной формульной задачи прогиба СП 63.</summary>
public sealed class Sp63DeflectionResultVM
{
    static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals
    };

    public Sp63DeflectionResult Model { get; }
    public string TaskTag { get; }
    public string SectionTag { get; }
    public string CreatedText { get; }
    public string ForcesSummary { get; }
    public string ContextSummary { get; }
    public string SchemeText { get; }
    public string CoefficientText { get; }
    public string SpanText { get; }
    public string CurvatureText { get; }
    public string DeflectionText { get; }
    public string LimitText { get; }
    public string UtilizationText { get; }
    public string VerdictText { get; }
    public Brush VerdictBrush { get; }
    public Brush VerdictBackground { get; }
    public bool HasDeflection { get; }
    public bool HasVerdict { get; }
    public Visibility CurvatureVisibility => CurvatureRows.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
    public Visibility ApplicabilityVisibility => ApplicabilityRows.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
    public Visibility InformationVisibility => InformationRows.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
    public ObservableCollection<Sp63DeflectionCurvatureRow> CurvatureRows { get; } = [];
    public ObservableCollection<Sp63DeflectionMessageRow> ApplicabilityRows { get; } = [];
    public ObservableCollection<Sp63DeflectionMessageRow> InformationRows { get; } = [];

    /// <summary>Создаёт VM из JSON с необязательным контекстом задачи.</summary>
    public Sp63DeflectionResultVM(string dataJson, CalcResult? result = null,
        CalcTask? task = null, OpenCS.AppViewModel? app = null)
    {
        Model = Deserialize(dataJson);
        Model.ApplicabilityMessages ??= [];
        Model.InformationalMessages ??= [];
        Model.Variables ??= [];
        TaskTag = result?.TaskTag ?? task?.Tag ?? Loc.S("Sp63NormalNoTask");
        CreatedText = result?.Created ?? "";
        SectionTag = task != null && app != null
            ? app.CrossSections.FirstOrDefault(section => section.Id == task.SectionId)?.Tag ?? Loc.S("Sp63NormalNoSection")
            : Loc.S("Sp63NormalNoSection");
        ForcesSummary = BuildForcesSummary(task, app);
        ContextSummary = BuildContextSummary(Model);
        SchemeText = Loc.S(SchemeResource(Model.Scheme));
        CoefficientText = Format(Model.CoefficientS);
        SpanText = Format(Model.SpanM);
        CurvatureText = Model.Curvature is { } curvature ? Format(curvature.Total) : "—";
        DeflectionText = Format(Model.DeflectionMm);
        LimitText = Format(Model.DeflectionLimitMm);
        UtilizationText = Format(Model.Utilization);
        HasDeflection = Model.Status == Sp63DeflectionStatus.Calculated && Model.Curvature != null;
        HasVerdict = Model.Status == Sp63DeflectionStatus.Calculated && Model.DeflectionPassed.HasValue;
        VerdictText = Model.Status switch
        {
            Sp63DeflectionStatus.Calculated when Model.DeflectionPassed == true => Loc.S("Sp63Deflection_VerdictPassed"),
            Sp63DeflectionStatus.Calculated when Model.DeflectionPassed == false => Loc.S("Sp63Deflection_VerdictFailed"),
            Sp63DeflectionStatus.NotApplicable => Loc.S("Sp63Deflection_StatusNotApplicable"),
            _ => Loc.S("Sp63Deflection_StatusInvalidInput")
        };
        (VerdictBrush, VerdictBackground) = Model.Status == Sp63DeflectionStatus.Calculated
            ? Model.DeflectionPassed == true
                ? (Brushes.Green, Background(0, 128, 0))
                : (Brushes.Red, Background(220, 0, 0))
            : (Brushes.DarkOrange, Background(220, 130, 0));

        if (Model.Curvature is { } resultCurvature)
            foreach (var term in resultCurvature.Terms)
                CurvatureRows.Add(new Sp63DeflectionCurvatureRow
                {
                    Name = $"(1/r){term.Index}",
                    Duration = Loc.S(term.LongTerm ? "Sp63CurvatureLongTerm" : "Sp63CurvatureShortTerm"),
                    Moment = Format(term.M),
                    AxialForce = Format(term.N),
                    Modulus = Format(term.Eb1 / 1000.0),
                    Stiffness = Format(term.D),
                    Curvature = Format(term.Curvature)
                });
        foreach (var message in Model.ApplicabilityMessages)
            ApplicabilityRows.Add(new Sp63DeflectionMessageRow
            { Text = Localize(message.Text, message.Code), NormReference = message.NormReference });
        foreach (var message in Model.InformationalMessages)
            InformationRows.Add(new Sp63DeflectionMessageRow
            { Text = Localize(message.Text, message.Code), NormReference = message.NormReference });
    }

    static Sp63DeflectionResult Deserialize(string json)
    {
        try
        {
            if (!string.IsNullOrWhiteSpace(json) && JsonSerializer.Deserialize<Sp63DeflectionResult>(json, JsonOptions) is { } model)
                return model;
        }
        catch (JsonException) { }
        return new Sp63DeflectionResult
        {
            Status = Sp63DeflectionStatus.InvalidInput,
            Branch = "invalid_input",
            ApplicabilityMessages = [new("invalid_result_json", Sp63DeflectionMessageKind.Applicability,
                "8.2.21", "Sp63Deflection_ResultJsonInvalid")]
        };
    }

    static string BuildForcesSummary(CalcTask? task, OpenCS.AppViewModel? app)
    {
        if (task == null || app == null) return Loc.S("Sp63NormalNoForceData");
        var p = Sp63DeflectionTaskParams.Parse(task.ParamsJson);
        LoadItem? load = p.UseManualForces
            ? new LoadItem { N = p.N ?? 0, Mx = p.Mx ?? 0, My = p.My ?? 0 }
            : app.BarForceSets.FirstOrDefault(set => set.Id == task.ForceSetId)?.Items.FirstOrDefault(item => item.Id == task.ForceItemId);
        if (load == null) return Loc.S("Sp63NormalNoForceData");
        return string.Format(CultureInfo.CurrentCulture, Loc.S("Sp63NormalForcesFormat"),
            Format(load.N), Format(load.Mx), Format(load.My));
    }

    static string SchemeResource(Sp63DeflectionStaticScheme scheme) => scheme switch
    {
        Sp63DeflectionStaticScheme.SimplySupportedUniform => "Sp63Deflection_SchemeUniform",
        Sp63DeflectionStaticScheme.SimplySupportedMidpoint => "Sp63Deflection_SchemeMidpoint",
        Sp63DeflectionStaticScheme.CantileverTip => "Sp63Deflection_SchemeCantilever",
        _ => "Sp63Deflection_StatusInvalidInput"
    };

    static string BuildContextSummary(Sp63DeflectionResult result)
    {
        string axis = result.Variables.GetValueOrDefault("axis") == 1
            ? Loc.S("Sp63NormalAxisMy") : Loc.S("Sp63NormalAxisMx");
        string nLong = result.Variables.TryGetValue("Nl", out var n) ? Format(n) : "—";
        string mLong = result.Variables.TryGetValue("Ml", out var m) ? Format(m) : "—";
        return string.Format(CultureInfo.CurrentCulture, Loc.S("Sp63Deflection_ResultContextFormat"),
            axis, nLong, mLong);
    }

    static string Localize(string text, string fallback) =>
        text.StartsWith("Sp63Deflection_", StringComparison.Ordinal) ||
        text.StartsWith("Sp63Normal_", StringComparison.Ordinal)
            ? (Loc.S(text) == text ? fallback : Loc.S(text))
            : string.IsNullOrWhiteSpace(text) ? fallback : text;

    static string Format(double value)
    {
        if (double.IsNaN(value)) return Loc.S("Sp63NormalNotANumber");
        if (double.IsPositiveInfinity(value)) return Loc.S("Sp63NormalPositiveInfinity");
        if (double.IsNegativeInfinity(value)) return Loc.S("Sp63NormalNegativeInfinity");
        return value.ToString("G6", CultureInfo.CurrentCulture);
    }

    static Brush Background(byte r, byte g, byte b) => new SolidColorBrush(Color.FromArgb(40, r, g, b));
}
