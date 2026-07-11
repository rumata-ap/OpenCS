using System.Text.Json.Serialization;
using CScore.Combinations;

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

      /// <summary>Схема численного якобиана Ньютона: "forward" | "central".</summary>
      [JsonPropertyName("newtonJacobian")]
      public string NewtonJacobian { get; set; } = "forward";

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
      public string NeutralAxisColor { get; set; } = "#808080";
      [JsonPropertyName("neutralAxisThickness")]
      public double NeutralAxisThickness { get; set; } = 2.0;

      [JsonPropertyName("centroidNdsColor")]
      public string CentroidNdsColor { get; set; } = "#CC0000";
      [JsonPropertyName("centroidNdsSize")]
      public double CentroidNdsSize { get; set; } = 8.0;

      /// <summary>Размер шрифта подписей σ/ε на канвасе сечения (пт).</summary>
      [JsonPropertyName("fiberLabelFontSize")]
      public double FiberLabelFontSize { get; set; } = 9.0;

      /// <summary>Размер и положение окна эпюры разреза сечения.</summary>
      [JsonPropertyName("sectionCutWindow")]
      public SectionCutWindowSettings SectionCutWindow { get; set; } = new();

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

      /// <summary>
      /// Тёплый старт в пакетном расчёте пластин: результат предыдущей строки используется
      /// как начальное приближение для следующей (SolveMany). При выключении каждая строка
      /// стартует независимо от упругого приближения.
      /// </summary>
      [JsonPropertyName("shellWarmStart")]
      public bool ShellWarmStart { get; set; } = false;

      /// <summary>
      /// Относительный допуск сходимости метода Ньютона для пластинчатых сечений
      /// (норма невязки усилий / (1 + норма целевых усилий)). Отличается от NewtonTolerance
      /// (абсолютный допуск для стержней в кН): для пластин подходящий порядок — 1e-3.
      /// </summary>
      [JsonPropertyName("shellNewtonTolRes")]
      public double ShellNewtonTolRes { get; set; } = 1e-3;

      /// <summary>Плавная (градиентная) цветовая карта напряжений/деформаций по умолчанию.</summary>
      [JsonPropertyName("smoothColormap")]
      public bool SmoothColormap { get; set; } = false;

      /// <summary>
      /// Учёт уменьшения площади бетона, замещённой площадью арматуры:
      /// разностная диаграмма σ_st − σ_bc для стержней в бетоне.
      /// При false — чистая диаграмма стали, бетонная сетка включает площадь под арматурой.
      /// </summary>
      [JsonPropertyName("rebarDifferentialDiagram")]
      public bool RebarDifferentialDiagram { get; set; } = true;

      /// <summary>γf по умолчанию для постоянной нагрузки (G), неблагоприятно.</summary>
      [JsonPropertyName("sp20GammaFG")]
      public double Sp20GammaFPermanent { get; set; } = 1.1;

      /// <summary>γf по умолчанию для постоянной нагрузки (G), благоприятно.</summary>
      [JsonPropertyName("sp20GammaFGFav")]
      public double Sp20GammaFPermanentFav { get; set; } = 0.9;

      /// <summary>γf по умолчанию для длительной переменной нагрузки (L).</summary>
      [JsonPropertyName("sp20GammaFL")]
      public double Sp20GammaFLongTerm { get; set; } = 1.2;

      /// <summary>γf по умолчанию для кратковременной переменной нагрузки (Q).</summary>
      [JsonPropertyName("sp20GammaFQ")]
      public double Sp20GammaFShortTerm { get; set; } = 1.4;

      /// <summary>γf по умолчанию для особой нагрузки (A).</summary>
      [JsonPropertyName("sp20GammaFA")]
      public double Sp20GammaFAccidental { get; set; } = 1.0;

      public static CalcSettings Default => new();

      public CalcSettings Clone() => new()
      {
         GridDensity           = GridDensity,
         NewtonTolerance       = NewtonTolerance,
         NewtonMaxIter         = NewtonMaxIter,
         NewtonDeltaH          = NewtonDeltaH,
         NewtonJacobian        = NewtonJacobian,
         HullColor             = HullColor,
         HullThickness         = HullThickness,
         HoleColor             = HoleColor,
         HoleThickness         = HoleThickness,
         NeutralAxisColor      = NeutralAxisColor,
         NeutralAxisThickness  = NeutralAxisThickness,
         CentroidNdsColor      = CentroidNdsColor,
         CentroidNdsSize       = CentroidNdsSize,
         FiberLabelFontSize    = FiberLabelFontSize,
         SectionCutWindow      = SectionCutWindow.Clone(),
         Sp63DescEtaMin        = Sp63DescEtaMin,
         BatchParallel         = BatchParallel,
         ShellWarmStart        = ShellWarmStart,
         ShellNewtonTolRes     = ShellNewtonTolRes,
         SmoothColormap        = SmoothColormap,
         RebarDifferentialDiagram = RebarDifferentialDiagram,
         Sp20GammaFPermanent      = Sp20GammaFPermanent,
         Sp20GammaFPermanentFav   = Sp20GammaFPermanentFav,
         Sp20GammaFLongTerm       = Sp20GammaFLongTerm,
         Sp20GammaFShortTerm      = Sp20GammaFShortTerm,
         Sp20GammaFAccidental     = Sp20GammaFAccidental,
      };

      /// <summary>Коэффициенты γf по умолчанию для комбинаторики СП 20.</summary>
      public Sp20GammaDefaults ToSp20GammaDefaults() => new()
      {
         PermanentUnfav  = Sp20GammaFPermanent,
         PermanentFav    = Sp20GammaFPermanentFav,
         LongTermUnfav   = Sp20GammaFLongTerm,
         ShortTermUnfav  = Sp20GammaFShortTerm,
         AccidentalUnfav = Sp20GammaFAccidental,
      };
   }
}
