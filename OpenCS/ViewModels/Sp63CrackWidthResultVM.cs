using System.Collections.ObjectModel;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Windows;
using System.Windows.Media;
using CScore;
using CScore.Sp63.CrackWidth;
using OpenCS.Utilites;

namespace OpenCS.ViewModels;

/// <summary>Одна строка численной проверки ширины раскрытия трещин.</summary>
public sealed class Sp63CrackWidthCheckRow
{
   /// <summary>Обозначение формулы.</summary>
   public string Formula { get; init; } = "";
   /// <summary>Локализованное описание проверки.</summary>
   public string Description { get; init; } = "";
   /// <summary>Ссылка на пункт нормы.</summary>
   public string NormReference { get; init; } = "";
   /// <summary>Приложенная величина в текстовом виде (acrc, мм).</summary>
   public string AppliedText { get; init; } = "";
   /// <summary>Допускаемая величина в текстовом виде (acrc,lim, мм).</summary>
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
public sealed class Sp63CrackWidthMessageRow
{
   /// <summary>Стабильный код сообщения.</summary>
   public string Code { get; init; } = "";
   /// <summary>Локализованный текст сообщения.</summary>
   public string Text { get; init; } = "";
   /// <summary>Ссылка на норму или пометка «справочно».</summary>
   public string NormReference { get; init; } = "";
}

/// <summary>Одна составляющая кривизны (1/r)i по п. 8.2.24.</summary>
public sealed class Sp63CurvatureRow
{
   /// <summary>Обозначение составляющей.</summary>
   public string Name { get; init; } = "";
   /// <summary>Продолжительность действия нагрузки.</summary>
   public string DurationText { get; init; } = "";
   /// <summary>Момент M, кН·м.</summary>
   public string MText { get; init; } = "";
   /// <summary>Модуль деформации Eb1, МПа.</summary>
   public string Eb1Text { get; init; } = "";
   /// <summary>ψs.</summary>
   public string PsiSText { get; init; } = "";
   /// <summary>Высота сжатой зоны xm, мм.</summary>
   public string XmText { get; init; } = "";
   /// <summary>Жёсткость D, кН·м².</summary>
   public string DText { get; init; } = "";
   /// <summary>Кривизна 1/r, 1/м.</summary>
   public string CurvatureText { get; init; } = "";
}

/// <summary>Одна переменная, использованная в формульном расчёте.</summary>
public sealed class Sp63CrackWidthVariableRow
{
   /// <summary>Имя переменной из результата расчёта.</summary>
   public string Name { get; init; } = "";
   /// <summary>Значение переменной.</summary>
   public string ValueText { get; init; } = "";
}

/// <summary>Представление результата упрощённой проверки ширины раскрытия трещин СП 63.</summary>
public sealed class Sp63CrackWidthResultVM
{
   static readonly JsonSerializerOptions JsonOptions = new()
   {
      PropertyNameCaseInsensitive = true,
      NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals
   };

   /// <summary>Модель результата, полученная из DataJson.</summary>
   public Sp63CrackWidthResult Model { get; }
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
   /// <summary>Показывать ли причины неприменимости.</summary>
   public Visibility ApplicabilityVisibility { get; }
   /// <summary>Показывать ли справочные сообщения.</summary>
   public Visibility InformationVisibility { get; }
   /// <summary>Показывать ли таблицу переменных.</summary>
   public Visibility VariablesVisibility { get; }
   /// <summary>Строки численных проверок.</summary>
   public ObservableCollection<Sp63CrackWidthCheckRow> StrengthRows { get; } = [];
   /// <summary>Причины неприменимости.</summary>
   public ObservableCollection<Sp63CrackWidthMessageRow> ApplicabilityRows { get; } = [];
   /// <summary>Справочные сообщения.</summary>
   public ObservableCollection<Sp63CrackWidthMessageRow> InformationRows { get; } = [];
   /// <summary>Переменные результата.</summary>
   public ObservableCollection<Sp63CrackWidthVariableRow> VariableRows { get; } = [];
   /// <summary>Составляющие кривизны.</summary>
   public ObservableCollection<Sp63CurvatureRow> CurvatureRows { get; } = [];
   /// <summary>Итог по кривизне: формула, полная кривизна, φb,cr, εb1,red.</summary>
   public string CurvatureSummary { get; } = "";
   /// <summary>Показывать ли блок кривизны.</summary>
   public Visibility CurvatureVisibility { get; }

   /// <summary>Создаёт VM только по JSON результата.</summary>
   public Sp63CrackWidthResultVM(string dataJson)
      : this(dataJson, null, null, null)
   {
   }

   /// <summary>Создаёт VM с контекстом задачи и сечения для шапки результата.</summary>
   public Sp63CrackWidthResultVM(string dataJson, CalcResult? result,
      CalcTask? task, OpenCS.AppViewModel? app)
   {
      Model = Deserialize(dataJson);
      Model.Details ??= [];
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
         Sp63CrackWidthStatus.Calculated when Model.LimitPassed == true
            => Loc.S("Sp63CrackWidthVerdictPassed"),
         Sp63CrackWidthStatus.Calculated
            => Loc.S("Sp63CrackWidthVerdictNotPassed"),
         Sp63CrackWidthStatus.NotApplicable
            => Loc.S("Sp63NormalVerdictUnavailable"),
         _ => Loc.S("Sp63NormalVerdictInvalidInput")
      };
      (VerdictBrush, VerdictBackground) = Model.Status switch
      {
         Sp63CrackWidthStatus.Calculated when Model.LimitPassed == true
            => (Brushes.Green, NewBackground(0, 128, 0)),
         Sp63CrackWidthStatus.Calculated
            => (Brushes.Red, NewBackground(220, 0, 0)),
         _ => (Brushes.DarkOrange, NewBackground(220, 130, 0))
      };

      foreach (var detail in Model.Details)
         StrengthRows.Add(ToCheckRow(detail));
      foreach (var message in Model.ApplicabilityMessages)
         ApplicabilityRows.Add(ToMessageRow(message));
      foreach (var message in Model.InformationalMessages)
         InformationRows.Add(ToMessageRow(message));
      foreach (var variable in Model.Variables.OrderBy(item => item.Key))
         VariableRows.Add(new Sp63CrackWidthVariableRow
         {
            Name = variable.Key,
            ValueText = FormatNumber(variable.Value)
         });

      StrengthVisibility = Model.Status == Sp63CrackWidthStatus.Calculated
         ? Visibility.Visible : Visibility.Collapsed;
      ApplicabilityVisibility = ApplicabilityRows.Count > 0
         ? Visibility.Visible : Visibility.Collapsed;
      InformationVisibility = InformationRows.Count > 0
         ? Visibility.Visible : Visibility.Collapsed;
      VariablesVisibility = VariableRows.Count > 0
         ? Visibility.Visible : Visibility.Collapsed;

      if (Model.Curvature is { } curvature)
      {
         foreach (var term in curvature.Terms)
            CurvatureRows.Add(new Sp63CurvatureRow
            {
               Name = $"(1/r){term.Index}",
               DurationText = Loc.S(term.LongTerm ? "Sp63CurvatureLongTerm" : "Sp63CurvatureShortTerm"),
               MText = FormatNumber(term.M),
               Eb1Text = FormatNumber(term.Eb1 / 1000.0),
               PsiSText = double.IsNaN(term.PsiS) ? "—" : FormatNumber(term.PsiS),
               XmText = double.IsNaN(term.Xm) ? "—" : FormatNumber(term.Xm * 1000.0),
               DText = FormatNumber(term.D) + (term.LimitedByUncracked ? " *" : ""),
               CurvatureText = FormatNumber(term.Curvature)
            });
         CurvatureSummary = string.Format(CultureInfo.CurrentCulture,
            Loc.S(curvature.Cracked ? "Sp63CurvatureSummaryCracked" : "Sp63CurvatureSummaryUncracked"),
            FormatNumber(curvature.Total), FormatNumber(curvature.PhiBCr),
            FormatNumber(curvature.EpsB1RedLong));
      }
      CurvatureVisibility = CurvatureRows.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
   }

   static Sp63CrackWidthResult Deserialize(string dataJson)
   {
      try
      {
         if (!string.IsNullOrWhiteSpace(dataJson))
         {
            var model = JsonSerializer.Deserialize<Sp63CrackWidthResult>(dataJson, JsonOptions);
            if (model != null) return model;
         }
      }
      catch (JsonException)
      {
         // Повреждённый результат отображается как ошибка исходных данных.
      }

      return new Sp63CrackWidthResult
      {
         Status = Sp63CrackWidthStatus.InvalidInput,
         Branch = "invalid_input",
         ApplicabilityMessages =
         [new Sp63CrackWidthMessage(
            "invalid_result_json",
            Sp63CrackWidthMessageKind.Applicability,
            "8.2",
            "Sp63CrackWidth_InvalidResultJson")]
      };
   }

   static Sp63CrackWidthCheckRow ToCheckRow(CheckDetail detail)
   {
      bool passed = detail.Passed;
      return new Sp63CrackWidthCheckRow
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

   static Sp63CrackWidthMessageRow ToMessageRow(Sp63CrackWidthMessage message) => new()
   {
      Code = message.Code,
      Text = LocalizeMessage(message),
      NormReference = message.NormReference
   };

   static string LocalizeMessage(Sp63CrackWidthMessage message)
   {
      string text = message.Text ?? "";
      // Причины неприменимости геометрии/раскладки арматуры используют общие ключи
      // "Sp63Normal_*" — они распознаются тем же Sp63RebarLayoutAnalyzer, что и нормальное
      // сечение (см. Sp63CrackWidthMessage).
      if (text.StartsWith("Sp63Normal_", StringComparison.Ordinal) ||
          text.StartsWith("Sp63CrackWidth_", StringComparison.Ordinal))
      {
         string localized = Loc.S(text);
         return localized == text ? message.Code : localized;
      }
      return text.Length > 0 ? text : message.Code;
   }

   static string LocalizeBranch(string branch) => branch switch
   {
      "cracked" => Loc.S("Sp63CrackWidthBranchCracked"),
      "not_cracked" => Loc.S("Sp63CrackWidthBranchNotCracked"),
      "not_applicable" => Loc.S("Sp63NormalBranchNotApplicable"),
      "invalid_input" => Loc.S("Sp63NormalBranchInvalidInput"),
      _ when !string.IsNullOrWhiteSpace(branch) => branch,
      _ => Loc.S("Sp63NormalBranchUnknown")
   };

   static string BuildForcesSummary(CalcTask? task, OpenCS.AppViewModel? app)
   {
      if (task == null || app == null)
         return Loc.S("Sp63NormalNoForceData");

      var parameters = Sp63CrackWidthTaskParams.Parse(task.ParamsJson);
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

      var p = Sp63CrackWidthTaskParams.Parse(task.ParamsJson);
      string axis = p.Axis == "My"
         ? Loc.S("Sp63NormalAxisMy") : Loc.S("Sp63NormalAxisMx");

      if (Sp63CrackWidthTaskParams.TryParseMode(p.Mode, out var mode)
          && mode == Sp63CrackWidthMode.LongAndShort)
         return string.Format(CultureInfo.CurrentCulture, Loc.S("Sp63CrackWidthContextLongShortFormat"),
            axis, FormatNumber(p.LongTermShare), FormatNumber(p.Phi2),
            FormatNumber(p.AcrcLimMm), FormatNumber(p.AcrcLimShortMm));

      return string.Format(CultureInfo.CurrentCulture, Loc.S("Sp63CrackWidthContextFormat"),
         axis, FormatNumber(p.Phi1), FormatNumber(p.Phi2), FormatNumber(p.AcrcLimMm));
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
