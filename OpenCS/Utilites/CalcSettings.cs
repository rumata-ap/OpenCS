using System.Text.Json.Serialization;

namespace OpenCS.Utilites
{
   /// <summary>
   /// Глобальные настройки численного расчёта и отображения результатов. Сериализуются в JSON.
   /// </summary>
   public class CalcSettings
   {
      /// <summary>Густота постпроцессорной сетки для бессеточных областей (шаг = max(w,h)/GridDensity).</summary>
      [JsonPropertyName("gridDensity")]
      public int GridDensity { get; set; } = 40;

      /// <summary>Допуск сходимости итераций Ньютона, кН (норма невязки).</summary>
      [JsonPropertyName("newtonTol")]
      public double NewtonTolerance { get; set; } = 0.1;

      /// <summary>Максимальное число итераций Ньютона.</summary>
      [JsonPropertyName("newtonMaxIter")]
      public int NewtonMaxIter { get; set; } = 25;

      /// <summary>Шаг приращения при вычислении численных производных Якобиана.</summary>
      [JsonPropertyName("newtonH")]
      public double NewtonDeltaH { get; set; } = 1e-7;

      // ── Стили линий ──────────────────────────────────────────────────
      [JsonPropertyName("hullColor")]
      public string HullColor { get; set; } = "#000000";
      [JsonPropertyName("hullThickness")]
      public double HullThickness { get; set; } = 1.5;

      [JsonPropertyName("holeColor")]
      public string HoleColor { get; set; } = "#606060";
      [JsonPropertyName("holeThickness")]
      public double HoleThickness { get; set; } = 1.0;

      [JsonPropertyName("neutralAxisColor")]
      public string NeutralAxisColor { get; set; } = "#000000";
      [JsonPropertyName("neutralAxisThickness")]
      public double NeutralAxisThickness { get; set; } = 2.0;

      [JsonPropertyName("centroidNdsColor")]
      public string CentroidNdsColor { get; set; } = "#CC0000";
      [JsonPropertyName("centroidNdsSize")]
      public double CentroidNdsSize { get; set; } = 8.0;

      /// <summary>Размер шрифта подписей σ/ε на канвасе сечения (пт).</summary>
      [JsonPropertyName("fiberLabelFontSize")]
      public double FiberLabelFontSize { get; set; } = 9.0;

      /// <summary>
      /// Нижняя граница нисходящей ветви криволинейной диаграммы бетона по Прил. Г СП 63.13330
      /// (уровень напряжений η = σ/Rb). По норме ≥ 0.85 (п. Г.1).
      /// </summary>
      [JsonPropertyName("sp63DescEtaMin")]
      public double Sp63DescEtaMin { get; set; } = 0.85;

      /// <summary>
      /// Параллельное выполнение пакетных задач прочности/жёсткости (Parallel.For).
      /// Каждый поток работает с клоном сечения. Огнестойкостные задачи не затрагиваются.
      /// </summary>
      [JsonPropertyName("batchParallel")]
      public bool BatchParallel { get; set; } = false;

      /// <summary>Плавная (градиентная) цветовая карта напряжений/деформаций по умолчанию.</summary>
      [JsonPropertyName("smoothColormap")]
      public bool SmoothColormap { get; set; } = false;

      public static CalcSettings Default => new();

      public CalcSettings Clone() => new()
      {
         GridDensity           = GridDensity,
         NewtonTolerance       = NewtonTolerance,
         NewtonMaxIter         = NewtonMaxIter,
         NewtonDeltaH          = NewtonDeltaH,
         HullColor             = HullColor,
         HullThickness         = HullThickness,
         HoleColor             = HoleColor,
         HoleThickness         = HoleThickness,
         NeutralAxisColor      = NeutralAxisColor,
         NeutralAxisThickness  = NeutralAxisThickness,
         CentroidNdsColor      = CentroidNdsColor,
         CentroidNdsSize       = CentroidNdsSize,
         FiberLabelFontSize    = FiberLabelFontSize,
         Sp63DescEtaMin        = Sp63DescEtaMin,
         BatchParallel         = BatchParallel,
         SmoothColormap        = SmoothColormap,
      };
   }
}
