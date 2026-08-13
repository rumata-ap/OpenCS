namespace OpenCS.OpenSees.CScore;

/// <summary>Настройки нелинейного расчёта, задаваемые на постановке (FemAnalysisParams):
/// формулировка geomTransf, уточнение, критерий сходимости, точки интегрирования. Шаг/предел
/// коэффициента нагрузки λ — per-stage, см. FemNonlinearStageInput/FemNonlinearStage.</summary>
public sealed record FemNonlinearAnalysisOptions(
    string GeomTransfKind,
    int RefinementDivisions,
    double Tolerance,
    int MaxIterations,
    int IntegrationPoints,
    string ConvergenceTest = "EnergyIncr",
    // Учитывать ли работу бетона на растяжение (см. CrossSectionToOpenSeesAdapter.Options.ConsiderConcreteTension).
    bool ConsiderConcreteTension = true,
    // Источник диаграммы материала: перевод CScore (по умолчанию) либо нативные материалы OpenSees.
    MaterialSource MaterialSource = MaterialSource.Translated,
    // Модель основной области при MaterialSource.Native.
    MainMaterialModelKind MainMaterialModel = MainMaterialModelKind.Concrete04,
    // Модель стали/арматуры при MaterialSource.Native.
    SteelModelKind SteelModel = SteelModelKind.Steel02,
    // Переопределение отношения модуля упрочнения стали/арматуры; null — автоматически.
    double? SteelHardeningRatioOverride = null,
    // Максимальная глубина рекурсивного дробления неудавшегося шага (см. FemNonlinearModel.MaxRefinementDepth).
    int MaxRefinementDepth = 4,
    // Формулировка стержневого элемента: "forceBeamColumn" (по умолчанию) | "dispBeamColumn"
    // (см. FemNonlinearModel.ElementFormulation про устойчивость при строго нулевом хвосте
    // растяжения бетона).
    string ElementFormulation = "forceBeamColumn",
    // Алгоритм решателя Ньютона: "Newton" | "NewtonLineSearch" (по умолчанию — см.
    // FemNonlinearModel.Algorithm про эмпирическое ускорение сходимости на кинематических
    // нагрузках).
    string Algorithm = "NewtonLineSearch",
    // Записывать ли состояния (σ, ε) отдельных волокон в nonlinear_fiber_states.out (см.
    // CalcSettings.OpenSeesRecordFiberStates — при крупных моделях файл может достигать сотен МБ).
    bool RecordFiberStates = true,
    // Ограничение записи волоконных состояний конкретными точками интегрирования вдоль длины
    // КАЖДОГО элемента; null — писать все точки (см. CalcSettings.OpenSeesFiberStatesIntegrationPoints).
    IReadOnlySet<int>? FiberStatesIntegrationPoints = null,
    // Учитывать ли физическую (материальную) нелинейность fiber-сечений. При false модель вообще не
    // содержит fiber-сечений: в точках интегрирования используется линейно-упругая section Elastic
    // (см. CrossSectionToOpenSeesAdapter.Options.ConsiderPhysicalNonlinearity) с приведёнными
    // (transformed) EA/EIy/EIz исходного контурного/фиброво заданного сечения. Геометрическая
    // нелинейность (GeomTransfKind) при этом не отключается.
    bool ConsiderPhysicalNonlinearity = true,
    // Минимальное отношение напряжения к пиковому на ниспадающей ветви бетона СП 63.13330.
    double Sp63EtaMin = 0.85,
    // Абсолютный модуль упрочнения арматуры в МПа; null — legacy-постановка без нового поля.
    double? SteelHardeningModulusMpa = 0);
