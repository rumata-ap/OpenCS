using System.Collections.ObjectModel;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Windows;
using System.Windows.Media;
using CScore;
using CScore.Sp63.Normal;
using OpenCS.Utilites;

namespace OpenCS.ViewModels;

/// <summary>Одна строка численной проверки нормального сечения.</summary>
public sealed class Sp63NormalCheckRow
{
   /// <summary>Обозначение формулы.</summary>
   public string Formula { get; init; } = "";
   /// <summary>Локализованное описание проверки.</summary>
   public string Description { get; init; } = "";
   /// <summary>Ссылка на пункт нормы.</summary>
   public string NormReference { get; init; } = "";
   /// <summary>Приложенная величина в текстовом виде.</summary>
   public string AppliedText { get; init; } = "";
   /// <summary>Допускаемая величина в текстовом виде.</summary>
   public string AllowableText { get; init; } = "";
   /// <summary>Коэффициент использования.</summary>
   public string RatioText { get; init; } = "";
   /// <summary>Текст результата строки.</summary>
   public string PassedText { get; init; } = "";
   /// <summary>Цвет результата строки.</summary>
   public Brush PassedBrush { get; init; } = Brushes.Gray;
   /// <summary>Текст трассировки переменных строки.</summary>
   public string VariablesText { get; init; } = "";
}

/// <summary>Одна строка причины неприменимости или справочной оговорки.</summary>
public sealed class Sp63NormalMessageRow
{
   /// <summary>Стабильный код сообщения.</summary>
   public string Code { get; init; } = "";
   /// <summary>Локализованный текст сообщения.</summary>
   public string Text { get; init; } = "";
   /// <summary>Ссылка на норму или пометка «справочно».</summary>
   public string NormReference { get; init; } = "";
}

/// <summary>Одна переменная, использованная в формульном расчёте.</summary>
public sealed class Sp63NormalVariableRow
{
   /// <summary>Имя переменной из результата расчёта.</summary>
   public string Name { get; init; } = "";
   /// <summary>Значение переменной.</summary>
   public string ValueText { get; init; } = "";
}

/// <summary>Представление результата упрощённой проверки нормального сечения СП 63.</summary>
public sealed class Sp63NormalResultVM
{
   static readonly JsonSerializerOptions JsonOptions = new()
   {
      PropertyNameCaseInsensitive = true,
      NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals
   };

   /// <summary>Модель результата, полученная из DataJson.</summary>
   public Sp63NormalResult Model { get; }
   /// <summary>Метка расчётной задачи.</summary>
   public string TaskTag { get; }
   /// <summary>Метка проверенного сечения.</summary>
   public string SectionTag { get; }
   /// <summary>Дата и время расчёта.</summary>
   public string CreatedText { get; }
   /// <summary>Исходные усилия.</summary>
   public string ForcesSummary { get; }
   /// <summary>Параметры формульной проверки.</summary>
   public string ContextSummary { get; }
   /// <summary>Выбранная ветвь расчёта.</summary>
   public string BranchText { get; }
   /// <summary>Итоговый текст вердикта.</summary>
   public string VerdictText { get; }
   /// <summary>Цвет текста вердикта.</summary>
   public Brush VerdictBrush { get; }
   /// <summary>Цвет фона вердикта.</summary>
   public Brush VerdictBackground { get; }
   /// <summary>Показывать ли блок численных проверок.</summary>
   public Visibility StrengthVisibility { get; }
   /// <summary>Показывать ли блок конструктивных требований раздела 10.</summary>
   public Visibility ConstructiveVisibility { get; }
   /// <summary>Показывать ли причины неприменимости.</summary>
   public Visibility ApplicabilityVisibility { get; }
   /// <summary>Показывать ли справочные сообщения.</summary>
   public Visibility InformationVisibility { get; }
   /// <summary>Показывать ли таблицу переменных.</summary>
   public Visibility VariablesVisibility { get; }
   /// <summary>Строки численных проверок.</summary>
   public ObservableCollection<Sp63NormalCheckRow> StrengthRows { get; } = [];
   /// <summary>Строки справочных проверок минимального армирования (п. 10.3.6).</summary>
   public ObservableCollection<Sp63NormalCheckRow> ConstructiveRows { get; } = [];
   /// <summary>Причины неприменимости.</summary>
   public ObservableCollection<Sp63NormalMessageRow> ApplicabilityRows { get; } = [];
   /// <summary>Справочные сообщения.</summary>
   public ObservableCollection<Sp63NormalMessageRow> InformationRows { get; } = [];
   /// <summary>Переменные результата.</summary>
   public ObservableCollection<Sp63NormalVariableRow> VariableRows { get; } = [];

   /// <summary>Создаёт VM только по JSON результата.</summary>
   public Sp63NormalResultVM(string dataJson)
      : this(dataJson, null, null, null)
   {
   }

   /// <summary>Создаёт VM с контекстом задачи и сечения для шапки результата.</summary>
   public Sp63NormalResultVM(string dataJson, CalcResult? result,
      CalcTask? task, OpenCS.AppViewModel? app)
   {
      Model = Deserialize(dataJson);
      Model.StrengthDetails ??= [];
      Model.ConstructiveChecks ??= [];
      Model.ApplicabilityMessages ??= [];
      Model.InformationalMessages ??= [];
      Model.Variables ??= [];

      TaskTag = result?.TaskTag ?? task?.Tag ?? Loc.S("Sp63NormalNoTask");
      CreatedText = result?.Created ?? "";
      SectionTag = task != null && app != null
         ? app.CrossSections.FirstOrDefault(s => s.Id == task.SectionId)?.Tag
           ?? Loc.S("Sp63NormalNoSection")
         : Loc.S("Sp63NormalNoSection");
      ForcesSummary = BuildForcesSummary(task, app);
      ContextSummary = BuildContextSummary(task);
      BranchText = LocalizeBranch(Model.Branch);

      VerdictText = Model.Status switch
      {
         Sp63NormalStatus.Calculated when Model.StrengthPassed == true
            => Loc.S("Sp63NormalVerdictPassed"),
         Sp63NormalStatus.Calculated
            => Loc.S("Sp63NormalVerdictNotPassed"),
         Sp63NormalStatus.NotApplicable
            => Loc.S("Sp63NormalVerdictUnavailable"),
         _ => Loc.S("Sp63NormalVerdictInvalidInput")
      };
      (VerdictBrush, VerdictBackground) = Model.Status switch
      {
         Sp63NormalStatus.Calculated when Model.StrengthPassed == true
            => (Brushes.Green, NewBackground(0, 128, 0)),
         Sp63NormalStatus.Calculated
            => (Brushes.Red, NewBackground(220, 0, 0)),
         _ => (Brushes.DarkOrange, NewBackground(220, 130, 0))
      };

      foreach (var detail in Model.StrengthDetails)
         StrengthRows.Add(ToCheckRow(detail));
      foreach (var detail in Model.ConstructiveChecks)
         ConstructiveRows.Add(ToCheckRow(detail));
      foreach (var message in Model.ApplicabilityMessages)
         ApplicabilityRows.Add(ToMessageRow(message));
      foreach (var message in Model.InformationalMessages)
         InformationRows.Add(ToMessageRow(message));
      foreach (var variable in Model.Variables.OrderBy(item => item.Key))
         VariableRows.Add(new Sp63NormalVariableRow
         {
            Name = variable.Key,
            ValueText = FormatNumber(variable.Value)
         });

      StrengthVisibility = Model.Status == Sp63NormalStatus.Calculated
         ? Visibility.Visible : Visibility.Collapsed;
      ConstructiveVisibility = ConstructiveRows.Count > 0
         ? Visibility.Visible : Visibility.Collapsed;
      ApplicabilityVisibility = ApplicabilityRows.Count > 0
         ? Visibility.Visible : Visibility.Collapsed;
      InformationVisibility = InformationRows.Count > 0
         ? Visibility.Visible : Visibility.Collapsed;
      VariablesVisibility = VariableRows.Count > 0
         ? Visibility.Visible : Visibility.Collapsed;
   }

   static Sp63NormalResult Deserialize(string dataJson)
   {
      try
      {
         if (!string.IsNullOrWhiteSpace(dataJson))
         {
            var model = JsonSerializer.Deserialize<Sp63NormalResult>(dataJson, JsonOptions);
            if (model != null) return model;
         }
      }
      catch (JsonException)
      {
         // Повреждённый результат отображается как ошибка исходных данных.
      }

      return new Sp63NormalResult
      {
         Status = Sp63NormalStatus.InvalidInput,
         Branch = "invalid_input",
         ApplicabilityMessages =
         [new Sp63NormalMessage(
            "invalid_result_json",
            Sp63NormalMessageKind.Applicability,
            "8.1",
            "Sp63Normal_InvalidResultJson")]
      };
   }

   static Sp63NormalCheckRow ToCheckRow(CheckDetail detail)
   {
      bool passed = detail.Passed;
      return new Sp63NormalCheckRow
      {
         Formula = detail.Formula,
         Description = Loc.S(detail.Description),
         NormReference = detail.NormReference,
         AppliedText = FormatNumber(detail.Applied),
         AllowableText = FormatNumber(detail.Allowable),
         RatioText = FormatNumber(detail.Ratio),
         PassedText = Loc.S(passed ? "Sp63NormalCheckPassed" : "Sp63NormalCheckNotPassed"),
         PassedBrush = passed ? Brushes.Green : Brushes.Red,
         VariablesText = FormatVariables(detail.Variables)
      };
   }

   static Sp63NormalMessageRow ToMessageRow(Sp63NormalMessage message) => new()
   {
      Code = message.Code,
      Text = LocalizeMessage(message),
      NormReference = message.NormReference
   };

   static string LocalizeMessage(Sp63NormalMessage message)
   {
      string text = message.Text ?? "";
      if (text.StartsWith("Sp63Normal_", StringComparison.Ordinal))
      {
         string localized = Loc.S(text);
         return localized == text ? message.Code : localized;
      }
      return text.Length > 0 ? text : message.Code;
   }

   static string LocalizeBranch(string branch) => branch switch
   {
      "compression" => Loc.S("Sp63NormalBranchCompression"),
      "bending" => Loc.S("Sp63NormalBranchBending"),
      "central_tension" => Loc.S("Sp63NormalBranchCentralTension"),
      "eccentric_tension_between" => Loc.S("Sp63NormalBranchEccentricTensionBetween"),
      "eccentric_tension_outside" => Loc.S("Sp63NormalBranchEccentricTensionOutside"),
      "not_applicable" => Loc.S("Sp63NormalBranchNotApplicable"),
      "invalid_input" => Loc.S("Sp63NormalBranchInvalidInput"),
      _ when !string.IsNullOrWhiteSpace(branch) => branch,
      _ => Loc.S("Sp63NormalBranchUnknown")
   };

   static string BuildForcesSummary(CalcTask? task, OpenCS.AppViewModel? app)
   {
      if (task == null || app == null)
         return Loc.S("Sp63NormalNoForceData");

      var parameters = Sp63NormalTaskParams.Parse(task.ParamsJson);
      LoadItem? load = parameters.UseManualForces
         ? parameters.ToLoadItem()
         : app.BarForceSets.FirstOrDefault(set => set.Id == task.ForceSetId)
            ?.Items.FirstOrDefault(item => item.Id == task.ForceItemId);
      if (load == null)
         return Loc.S("Sp63NormalNoForceData");

      return string.Format(CultureInfo.CurrentCulture, Loc.S("Sp63NormalForcesFormat"),
         FormatNumber(load.N), FormatNumber(load.Mx), FormatNumber(load.My));
   }

   static string BuildContextSummary(CalcTask? task)
   {
      if (task == null)
         return Loc.S("Sp63NormalNoTaskContext");

      var p = Sp63NormalTaskParams.Parse(task.ParamsJson);
      string shape = p.ShapeKind == "rectangular"
         ? Loc.S("Sp63NormalShapeRectangular") : p.ShapeKind;
      string axis = p.Axis == "My"
         ? Loc.S("Sp63NormalAxisMy") : Loc.S("Sp63NormalAxisMx");
      string scheme = p.StructuralScheme == "statically_determinate"
         ? Loc.S("Sp63NormalStaticallyDeterminate")
         : Loc.S("Sp63NormalStaticallyIndeterminate");
      string stability = p.StabilityMode == "section_only_explicit"
         ? Loc.S("Sp63NormalSectionOnlyExplicit") : Loc.S("Sp63NormalMemberMode");
      string length = p.ElementLengthOrRestraintDistance.HasValue
         ? FormatNumber(p.ElementLengthOrRestraintDistance.Value)
         : Loc.S("Sp63NormalNotSpecified");
      string l0 = p.EffectiveLengthL0.HasValue
         ? FormatNumber(p.EffectiveLengthL0.Value)
         : Loc.S("Sp63NormalNotSpecified");

      return string.Format(CultureInfo.CurrentCulture, Loc.S("Sp63NormalContextFormat"),
         shape, axis, scheme, length, l0, stability, FormatNumber(p.Psi),
         FormatNumber(p.SlendernessThreshold));
   }

   static string FormatVariables(IReadOnlyDictionary<string, double> variables) =>
      variables.Count == 0
         ? ""
         : string.Join(Loc.S("Sp63NormalVariableSeparator"), variables.OrderBy(item => item.Key)
            .Select(item => string.Format(CultureInfo.CurrentCulture,
               Loc.S("Sp63NormalVariableFormat"), item.Key, FormatNumber(item.Value))));

   static string FormatNumber(double value)
   {
      if (double.IsNaN(value)) return Loc.S("Sp63NormalNotANumber");
      if (double.IsPositiveInfinity(value)) return Loc.S("Sp63NormalPositiveInfinity");
      if (double.IsNegativeInfinity(value)) return Loc.S("Sp63NormalNegativeInfinity");
      return value.ToString("G6", CultureInfo.CurrentCulture);
   }

   static Brush NewBackground(byte r, byte g, byte b) =>
      new SolidColorBrush(Color.FromArgb(40, r, g, b));
}
