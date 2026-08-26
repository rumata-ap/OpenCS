using CScore;
using OpenCS.Utilites;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Text.Json;

namespace OpenCS.ViewModels;

/// <summary>Строка сведений о температурных коэффициентах растянутой арматуры.</summary>
public sealed record FireThermalCurvatureRebarRowVM(
   string ClassText, string TemperatureText, string AreaText, string GammaText);

/// <summary>Представление результата задачи температурной кривизны.</summary>
public sealed class FireThermalCurvatureResultVM
{
   public string ErrorText { get; }
   public bool HasError => ErrorText.Length > 0;

   public string ChiText { get; }
   public string EpsText { get; }
   public string DText { get; }
   public string THotText { get; }
   public string TColdText { get; }
   public string TRebarText { get; }
   public string H0Text { get; }
   public string XtText { get; }
   public string XtMethodText { get; }
   public string XiRText { get; }
   public string ZText { get; }
   public string ZSimplifiedText { get; }
   public string Phi1Text { get; }
   public string HeightText { get; }
   public string AsText { get; }
   public string EstText { get; }
   public string ProfileQualityText { get; }
   public string AxisText { get; }

   public ObservableCollection<string> Warnings { get; } = [];
   public ObservableCollection<FireThermalCurvatureRebarRowVM> RebarRows { get; } = [];
   public FireLineChartVM? ProfileChart { get; }
   public bool HasWarnings => Warnings.Count > 0;
   public bool HasRebarRows => RebarRows.Count > 0;
   public bool HasProfileChart => ProfileChart is { Series.Count: > 0 };

   /// <summary>Разобрать JSON результата и подготовить локализованные строки для WPF.</summary>
   public FireThermalCurvatureResultVM(CalcResult result)
   {
      string noValue = Loc.S("FireCurvature_NoValue");
      ErrorText = "";
      ChiText = EpsText = DText = THotText = TColdText = TRebarText = H0Text =
         XtText = XtMethodText = XiRText = ZText = ZSimplifiedText = Phi1Text =
         HeightText = AsText = EstText = ProfileQualityText = AxisText = noValue;

      if (FireResultJson.TryGetError(result.DataJson, out string error))
      {
         ErrorText = error;
         return;
      }

      JsonElement root;
      try
      {
         root = FireResultJson.Root(result.DataJson);
      }
      catch
      {
         ErrorText = Loc.S("FireCurvature_CalculationError");
         return;
      }

      ChiText = Fmt(root, "chi_t", "F6");
      EpsText = Fmt(root, "eps_t", "F6");
      DText = FireResultJson.DblOrNull(root, "D") is double d
         ? string.Format(CultureInfo.InvariantCulture, "{0:F3} {1}", d, Loc.S("Unit_Nm2"))
         : Loc.S("FireCurvature_DUnavailable");
      THotText = FmtUnit(root, "t_hot_concrete", "F1", "Unit_Celsius");
      TColdText = FmtUnit(root, "t_cold_concrete", "F1", "Unit_Celsius");
      TRebarText = FmtUnit(root, "t_rebar", "F1", "Unit_Celsius");
      H0Text = FmtUnit(root, "h0", "F4", "Unit_m");
      XtText = FmtUnit(root, "x_t", "F4", "Unit_m");
      XtMethodText = MapXtMethod(FireResultJson.Str(root, "x_t_method", ""));
      XiRText = Fmt(root, "xi_r", "F4");
      ZText = FmtUnit(root, "z", "F4", "Unit_m");
      ZSimplifiedText = FmtUnit(root, "z_simplified", "F4", "Unit_m");
      Phi1Text = Fmt(root, "phi1", "F4");
      HeightText = FmtUnit(root, "h", "F4", "Unit_m");
      AsText = FmtUnit(root, "A_s", "F6", "Unit_m2");
      EstText = FmtUnit(root, "E_st", "F1", "Unit_MPa", 1e-6);
      ProfileQualityText = FmtUnit(root, "profile_quality", "F2", "Unit_Celsius");
      AxisText = string.Format(CultureInfo.InvariantCulture, Loc.S("FireCurvature_AxisFormat"),
         FireResultJson.Dbl(root, "axis_x"), FireResultJson.Dbl(root, "axis_y"));

      AddWarnings(root);
      ReadRebarDetails(root);
      ProfileChart = BuildProfileChart(root);
   }

   void AddWarnings(JsonElement root)
   {
      if (FireResultJson.Bool(root, "uniform_heating"))
         Warnings.Add(Loc.S("FireCurvature_Uniform"));
      if (FireResultJson.Bool(root, "axis_from_inertia"))
         Warnings.Add(Loc.S("FireCurvature_AxisFromInertia"));
      if (FireResultJson.Bool(root, "rebar_both_faces"))
         Warnings.Add(Loc.S("FireCurvature_RebarBothFaces"));
      if (FireResultJson.Bool(root, "xi_capped"))
         Warnings.Add(Loc.S("FireCurvature_XiCapped"));
      if (FireResultJson.Bool(root, "x_t_method_fallback"))
         Warnings.Add(Loc.S("FireCurvature_XtFallback"));
      if (FireResultJson.Bool(root, "eps_b2_out_of_range"))
         Warnings.Add(Loc.S("FireCurvature_EpsB2OutOfRange"));
      if (FireResultJson.Bool(root, "aggregate_not_silicate"))
         Warnings.Add(Loc.S("FireCurvature_AggregateNotSilicate"));

      string reasonKey = FireResultJson.Str(root, "d_unsupported", "");
      if (reasonKey.Length > 0)
         Warnings.Add(Loc.S(reasonKey));
   }

   void ReadRebarDetails(JsonElement root)
   {
      if (!root.TryGetProperty("rebar_details", out JsonElement details)
          || details.ValueKind != JsonValueKind.Array)
         return;

      foreach (JsonElement detail in details.EnumerateArray())
      {
         string group = FireResultJson.Str(detail, "class_group", "");
         string source = FireResultJson.Str(detail, "class_source", "");
         string classText = Loc.S($"Material_FireRebarClass_{group}");
         if (classText == $"Material_FireRebarClass_{group}")
            classText = group;
         if (source.Length > 0)
            classText = string.Format(Loc.S("FireCurvature_RebarClassFormat"), classText, MapSource(source));

         RebarRows.Add(new FireThermalCurvatureRebarRowVM(
            classText,
            FmtUnit(detail, "temperature_c", "F1", "Unit_Celsius"),
            FmtUnit(detail, "area_m2", "F6", "Unit_m2"),
            string.Format(CultureInfo.InvariantCulture, "{0:F4} / {1:F4}",
               FireResultJson.Dbl(detail, "gamma_st"),
               FireResultJson.Dbl(detail, "gamma_st_e"))));
      }
   }

   static FireLineChartVM? BuildProfileChart(JsonElement root)
   {
      if (!root.TryGetProperty("profile", out JsonElement profile)
          || profile.ValueKind != JsonValueKind.Array)
         return null;

      var s = new List<double>();
      var actual = new List<double>();
      var linear = new List<double>();
      foreach (JsonElement row in profile.EnumerateArray())
      {
         s.Add(FireResultJson.Dbl(row, "s"));
         actual.Add(FireResultJson.Dbl(row, "t_actual"));
         linear.Add(FireResultJson.Dbl(row, "t_linear"));
      }

      if (s.Count < 2)
         return null;

      return new FireLineChartVM(
         Loc.S("FireCurvature_ChartTitle"),
         Loc.S("FireCurvature_ChartAxisX"),
         Loc.S("FireCurvature_ChartAxisY"),
         [
            new FireLineSeries(Loc.S("FireCurvature_ProfileActual"), s.ToArray(), actual.ToArray(), "#DC2626"),
            new FireLineSeries(Loc.S("FireCurvature_ProfileLinear"), s.ToArray(), linear.ToArray(), "#2563EB")
         ]);
   }

   static string Fmt(JsonElement root, string name, string format)
      => FireResultJson.DblOrNull(root, name) is double value
         ? value.ToString(format, CultureInfo.InvariantCulture) : Loc.S("FireCurvature_NoValue");

   static string FmtUnit(JsonElement root, string name, string format, string unitKey, double scale = 1.0)
      => FireResultJson.DblOrNull(root, name) is double value
         ? string.Format(CultureInfo.InvariantCulture, "{0} {1}",
            (value * scale).ToString(format, CultureInfo.InvariantCulture), Loc.S(unitKey))
         : Loc.S("FireCurvature_NoValue");

   static string MapXtMethod(string method) => method switch
   {
      "sp468_8_11" => Loc.S("FireCurvature_XtMethodFormula"),
      "fiber_equilibrium" => Loc.S("FireCurvature_XtMethodFiber"),
      _ => method.Length == 0 ? Loc.S("FireCurvature_NoValue") : method
   };

   static string MapSource(string source) => source switch
   {
      "explicit" => Loc.S("FireCurvature_SourceExplicit"),
      "class" => Loc.S("FireCurvature_SourceClass"),
      "tag" => Loc.S("FireCurvature_SourceTag"),
      "fallback" => Loc.S("FireCurvature_SourceFallback"),
      _ => source
   };
}
