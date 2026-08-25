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
      /// Нижняя граница нисходящей ветви диаграммы ЕКБ (уровень напряжений η = σ/Rb).
      /// Кривая MC90 строится до σ = η·Rb; переход с ур. (2.1-18) на (2.1-20)
      /// происходит при η = 0.5, поэтому при η ≥ 0.5 участок (2.1-20) не строится.
      /// </summary>
      [JsonPropertyName("ekbDescEtaMin")]
      public double EkbDescEtaMin { get; set; } = 0.05;

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

      /// <summary>
      /// Учитывать работу бетона на растяжение при расчётах по 1-й ГПС (CalcType C/CL):
      /// поиск предельных усилий и плоскости деформаций для стержневых и пластинчатых
      /// сечений. На 2-ю ГПС (N/NL) не влияет — там растяжение бетона учитывается как раньше.
      /// </summary>
      [JsonPropertyName("considerConcreteTensionUls")]
      public bool ConsiderConcreteTensionUls { get; set; } = false;

      /// <summary>
      /// Разрешить ли учёт растяжения бетона для стержневого сечения при данном виде
      /// расчёта: для C/CL — по значению <see cref="ConsiderConcreteTensionUls"/>,
      /// для N/NL — всегда true (без изменений).
      /// </summary>
      public bool ResolveConcreteTension(CScore.CalcType calc)
         => calc is CScore.CalcType.C or CScore.CalcType.CL ? ConsiderConcreteTensionUls : true;

      /// <summary>
      /// Метод коэффициента ψs (неравномерность деформаций растянутой арматуры между
      /// трещинами) в задачах ширины раскрытия трещин: "stress8138" (п. 8.2.18, по
      /// отношению напряжений, по умолчанию) | "strain8232" (п. 8.2.32, по отношению
      /// деформаций).
      /// </summary>
      [JsonPropertyName("crackWidthPsiSMethod")]
      public string CrackWidthPsiSMethod { get; set; } = "stress8138";

      /// <summary>Разобранное значение <see cref="CrackWidthPsiSMethod"/> для передачи в CrackWidthSolver.</summary>
      public CScore.PsiSMethod ResolvePsiSMethod()
         => CrackWidthPsiSMethod == "strain8232" ? CScore.PsiSMethod.Strain8232 : CScore.PsiSMethod.Stress8138;

      // ── Наклонные сечения по СП 63.13330, пп. 8.1.32–8.1.35 ─────────────
      // Численные параметры перебора и два нормативных умолчания: они одинаковы для всех
      // задач и потому вынесены сюда, а не дублируются в параметрах каждой постановки.
      // Сохранённые ранее задачи хранят свои значения в ParamsJson и продолжают считаться
      // по ним (см. ShearInclinedParams — поля nullable).

      /// <summary>Шаг стоянок вдоль элемента в расчёте наклонных сечений, м; 0 — авто.</summary>
      [JsonPropertyName("shearStationStep")]
      public double ShearStationStep { get; set; }

      /// <summary>Шаг перебора проекции наклонного сечения C, м; 0 — авто (h0/100).</summary>
      [JsonPropertyName("shearProjectionStep")]
      public double ShearProjectionStep { get; set; }

      /// <summary>Длина приопорной зоны проверки момента по (8.63), м; 0 — 2·h0 по норме.</summary>
      [JsonPropertyName("shearMomentZoneLength")]
      public double ShearMomentZoneLength { get; set; }

      /// <summary>
      /// Коэффициент включения продольной арматуры k в наклонном сечении.
      /// Анкеровка по 10.3.21–10.3.28 расчётом не проверяется, значение задаёт пользователь.
      /// </summary>
      [JsonPropertyName("shearAnchorageFactor")]
      public double ShearAnchorageFactor { get; set; } = 1.0;

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

      // ── OpenSees (расчёт схемы: линейный и нелинейный) ──────────────────
      // Solver-настройки нелинейного FEM-расчёта — по аналогии с NewtonTolerance/NewtonMaxIter
      // выше (глобальные, не дублируются в каждой постановке FemAnalysis).

      /// <summary>Безопасное значение крутильной жёсткости GJ по умолчанию, кН·м².</summary>
      public const double DefaultOpenSeesGjKnm2 = 1e7;

      /// <summary>Резервное значение GJ для новых стержней, кН·м².</summary>
      [JsonPropertyName("openSeesDefaultGjKnm2")]
      public double OpenSeesDefaultGjKnm2 { get; set; } = DefaultOpenSeesGjKnm2;

      /// <summary>Оценивать ли GJ нового стержня по назначенному поперечному сечению.</summary>
      [JsonPropertyName("openSeesAutoGjFromSection")]
      public bool OpenSeesAutoGjFromSection { get; set; } = true;

      /// <summary>Путь к OpenSees.exe. Пусто — автоопределение (%OPENSEES_HOME%\bin, затем рядом с OpenCS.exe).</summary>
      [JsonPropertyName("openSeesExecutablePath")]
      public string? OpenSeesExecutablePath { get; set; }

      /// <summary>Таймаут запуска OpenSees.exe, с.</summary>
      [JsonPropertyName("openSeesTimeoutSeconds")]
      public int OpenSeesTimeoutSeconds { get; set; } = 120;

      /// <summary>Каталог артефактов запусков OpenSees (сгенерированные .tcl, логи, результаты).
      /// Пусто — каталог "OpenSeesArtifacts" рядом с исполняемым файлом OpenCS.</summary>
      [JsonPropertyName("openSeesArtifactsPath")]
      public string? OpenSeesArtifactsPath { get; set; }

      /// <summary>Разрешённый путь к каталогу артефактов OpenSees: заданный пользователем
      /// или каталог "OpenSeesArtifacts" рядом с исполняемым файлом OpenCS по умолчанию.</summary>
      public string ResolveOpenSeesArtifactsPath()
         => string.IsNullOrWhiteSpace(OpenSeesArtifactsPath)
            ? System.IO.Path.Combine(System.AppContext.BaseDirectory, "OpenSeesArtifacts")
            : OpenSeesArtifactsPath;

      /// <summary>Количество частей, на которое делится неудавшийся шаг нагрузки на каждом уровне
      /// дробления (нелинейный расчёт).</summary>
      [JsonPropertyName("openSeesRefinementDivisions")]
      public int OpenSeesRefinementDivisions { get; set; } = 10;

      /// <summary>Максимальная глубина рекурсивного дробления неудавшегося шага: на каждом уровне
      /// интервал делится на OpenSeesRefinementDivisions частей; если и они не сходятся —
      /// дробятся рекурсивно дальше, вплоть до этой глубины (нелинейный расчёт).</summary>
      [JsonPropertyName("openSeesMaxRefinementDepth")]
      public int OpenSeesMaxRefinementDepth { get; set; } = 4;

      /// <summary>Допуск критерия сходимости Ньютона (нелинейный расчёт).</summary>
      [JsonPropertyName("openSeesTolerance")]
      public double OpenSeesTolerance { get; set; } = 1e-6;

      /// <summary>Максимальное число итераций Ньютона на шаг (нелинейный расчёт).</summary>
      [JsonPropertyName("openSeesMaxIterations")]
      public int OpenSeesMaxIterations { get; set; } = 50;

      /// <summary>Формулировка geomTransf: "Linear" | "PDelta" | "Corotational".</summary>
      [JsonPropertyName("openSeesGeomTransfKind")]
      public string OpenSeesGeomTransfKind { get; set; } = "Linear";

      /// <summary>Критерий сходимости Ньютона: "EnergyIncr" | "NormUnbalance" | "NormDispIncr".</summary>
      [JsonPropertyName("openSeesConvergenceTest")]
      public string OpenSeesConvergenceTest { get; set; } = "EnergyIncr";

      /// <summary>Алгоритм решателя: "Newton" | "NewtonLineSearch" (line search внутри итерации —
      /// масштабирует шаг Ньютона, если он "перелетает" через резкий разрыв диаграммы материала,
      /// вместо провала итерации целиком; на реальном кинематическом сценарии с обрывом растяжения
      /// бетона в строгий ноль довёл расчёт до конца там, где обычный Newton не успевал пройти и
      /// трети пути за то же время — см. заметку в памяти проекта).</summary>
      [JsonPropertyName("openSeesAlgorithm")]
      public string OpenSeesAlgorithm { get; set; } = "NewtonLineSearch";

      /// <summary>Число точек интегрирования forceBeamColumn (нелинейный расчёт).</summary>
      [JsonPropertyName("openSeesIntegrationPoints")]
      public int OpenSeesIntegrationPoints { get; set; } = 5;

      /// <summary>Записывать ли состояния (σ, ε) отдельных волокон сечений в nonlinear_fiber_states.out
      /// (нелинейный расчёт схемы). При крупных моделях и большом числе шагов/под-шагов дробления файл
      /// может достигать сотен МБ (элементы × точки интегрирования × волокна × шаги); выключение
      /// экономит место и время расчёта, но лишает результатную вкладку детальной картины по волокнам.</summary>
      [JsonPropertyName("openSeesRecordFiberStates")]
      public bool OpenSeesRecordFiberStates { get; set; } = true;

      /// <summary>Ограничение записи волоконных состояний конкретными точками интегрирования вдоль
      /// длины КАЖДОГО элемента (1-based номера через запятую, напр. "1,3,5"). Пусто/некорректно —
      /// писать все точки интегрирования элемента (как раньше). Игнорируется при
      /// OpenSeesRecordFiberStates=false.</summary>
      [JsonPropertyName("openSeesFiberStatesIntegrationPoints")]
      public string? OpenSeesFiberStatesIntegrationPoints { get; set; }

      /// <summary>Разбирает <see cref="OpenSeesFiberStatesIntegrationPoints"/> в множество 1-based
      /// номеров точек интегрирования; null — ограничения нет (писать все точки элемента).</summary>
      public IReadOnlySet<int>? ResolveOpenSeesFiberStatesIntegrationPoints()
      {
         if (string.IsNullOrWhiteSpace(OpenSeesFiberStatesIntegrationPoints)) return null;
         var points = new HashSet<int>();
         foreach (var token in OpenSeesFiberStatesIntegrationPoints.Split(',', System.StringSplitOptions.RemoveEmptyEntries | System.StringSplitOptions.TrimEntries))
            if (int.TryParse(token, out var v) && v > 0)
               points.Add(v);
         return points.Count > 0 ? points : null;
      }

      // Настройки материалов (учёт растяжения бетона, источник/модель) специфичны для конкретной
      // постановки — хранятся в FemAnalysisParams, а не здесь (задаются в диалоге постановки).

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
         EkbDescEtaMin         = EkbDescEtaMin,
         BatchParallel         = BatchParallel,
         ShellWarmStart        = ShellWarmStart,
         ShellNewtonTolRes     = ShellNewtonTolRes,
         SmoothColormap        = SmoothColormap,
         RebarDifferentialDiagram = RebarDifferentialDiagram,
         ConsiderConcreteTensionUls = ConsiderConcreteTensionUls,
         CrackWidthPsiSMethod  = CrackWidthPsiSMethod,
         ShearStationStep      = ShearStationStep,
         ShearProjectionStep   = ShearProjectionStep,
         ShearMomentZoneLength = ShearMomentZoneLength,
         ShearAnchorageFactor  = ShearAnchorageFactor,
         Sp20GammaFPermanent      = Sp20GammaFPermanent,
         Sp20GammaFPermanentFav   = Sp20GammaFPermanentFav,
         Sp20GammaFLongTerm       = Sp20GammaFLongTerm,
         Sp20GammaFShortTerm      = Sp20GammaFShortTerm,
         Sp20GammaFAccidental     = Sp20GammaFAccidental,
         OpenSeesDefaultGjKnm2    = OpenSeesDefaultGjKnm2,
         OpenSeesAutoGjFromSection = OpenSeesAutoGjFromSection,
         OpenSeesExecutablePath   = OpenSeesExecutablePath,
         OpenSeesTimeoutSeconds   = OpenSeesTimeoutSeconds,
         OpenSeesArtifactsPath    = OpenSeesArtifactsPath,
         OpenSeesRefinementDivisions = OpenSeesRefinementDivisions,
         OpenSeesMaxRefinementDepth = OpenSeesMaxRefinementDepth,
         OpenSeesTolerance        = OpenSeesTolerance,
         OpenSeesMaxIterations    = OpenSeesMaxIterations,
         OpenSeesGeomTransfKind   = OpenSeesGeomTransfKind,
         OpenSeesConvergenceTest  = OpenSeesConvergenceTest,
         OpenSeesAlgorithm        = OpenSeesAlgorithm,
         OpenSeesIntegrationPoints = OpenSeesIntegrationPoints,
         OpenSeesRecordFiberStates = OpenSeesRecordFiberStates,
         OpenSeesFiberStatesIntegrationPoints = OpenSeesFiberStatesIntegrationPoints,
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
