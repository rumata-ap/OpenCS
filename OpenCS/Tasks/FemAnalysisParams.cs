using System.Text.Json;
using System.Text.Json.Serialization;
using CScore;

namespace OpenCS.Tasks;

/// <summary>Параметры запуска FEM-расчёта (линейного и нелинейного), хранимые в FemAnalysis.ParamsJson.
/// Поля CalcType/ConsiderConcreteTension/MaterialSource/MainMaterialModel/SteelModel/
/// SteelHardeningRatioOverride используются только при Kind="nonlinear" и специфичны для
/// конкретной постановки. Solver-механика (исполняемый файл, таймаут, сходимость, geomTransf,
/// точки интегрирования) — глобальная, см. <see cref="OpenCS.Utilites.CalcSettings"/> (вкладка
/// «OpenSees» в диалоге настроек), а не хранится в каждой постановке.</summary>
/// <summary>Одна стадия нагружения нелинейной постановки: имя + выражение выбора загружения
/// (сериализованный FemLoadExpression), резолвится независимо от остальных стадий.</summary>
/// <summary>DTO настроек управления траекторией одной стадии, на NodeId схемы. Один и тот
/// же тип используется и для прямого режима (FemAnalysisStage.PathControl), и для
/// continuation (FemAnalysisStage.ContinueWith) — единый контракт вместо плоского набора
/// ContinueWith*-полей.</summary>
public sealed class FemAnalysisPathControl
{
    public string Mode { get; set; } = "LoadControl"; // LoadControl|DisplacementControl|ArcLength
    public int? ControlNodeId { get; set; }
    public int? ControlDof { get; set; }
    public double? InitialIncrement { get; set; }
    public double? MinIncrement { get; set; }
    public double? MaxIncrement { get; set; }
    public double? TargetDisplacement { get; set; }
    public int? MaxSteps { get; set; }
    public double? ArcLengthS { get; set; }
    public double? ArcLengthAlpha { get; set; }
    public double? ArcLengthMinS { get; set; }
    public int? MonitorNodeId { get; set; }
    public int? MonitorDof { get; set; }
}

public sealed class FemAnalysisStage
{
    public string Tag { get; set; } = "";
    public string LoadExpressionJson { get; set; } = "{}";
    /// <summary>Шаг коэффициента нагрузки λ этой стадии. null — легаси-стадия, сохранённая до
    /// появления per-stage настройки; см. FemAnalysisParams.Parse (мигрирует из старого
    /// глобального поля LoadFactorStep).</summary>
    public double? LoadFactorStep { get; set; }
    /// <summary>Максимальный коэффициент нагрузки λ этой стадии. null — см. LoadFactorStep.</summary>
    public double? MaxLoadFactor { get; set; }
    /// <summary>Способ управления траекторией; null → LoadControl без continuation (legacy JSON).</summary>
    public FemAnalysisPathControl? PathControl { get; set; }
    /// <summary>Настройки продолжения; осмысленны только при PathControl?.Mode == "LoadControl"/null.</summary>
    public FemAnalysisPathControl? ContinueWith { get; set; }
}

public sealed class FemAnalysisParams
{
    /// <summary>Тип расчёта для выбора диаграмм материалов fiber-сечений (нелинейный расчёт).</summary>
    public CalcType? CalcType { get; set; }
    /// <summary>Шаг коэффициента пропорциональной нагрузки λ.</summary>
    public double LoadFactorStep { get; set; } = 0.1;
    /// <summary>Максимальный коэффициент пропорциональной нагрузки λ.</summary>
    public double MaxLoadFactor { get; set; } = 10.0;
    /// <summary>Старое число шагов; читается только из legacy JSON и не записывается в новый JSON.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? LoadSteps { get; set; }
    /// <summary>Учитывать ли работу бетона на растяжение в fiber-сечениях (на арматуру/сталь не
    /// влияет). При отключении огибающая растяжения бетона заменяется строго горизонтальным
    /// нулевым напряжением — сечение считается уже полностью растрескавшимся с самого начала.
    /// Приоритет отдан корректному распределению напряжений над устойчивостью солвера — см.
    /// <see cref="ElementFormulation"/> про dispBeamColumn как более устойчивый выбор именно для
    /// этого случая.</summary>
    public bool ConsiderConcreteTension { get; set; } = true;
    /// <summary>Учитывать ли физическую (материальную) нелинейность fiber-сечений. При отключении
    /// все материалы работают линейно-упруго (модуль E из характеристик материала, без
    /// трещинообразования/текучести) независимо от MaterialSource/MainMaterialModel/SteelModel —
    /// геометрическая нелинейность (geomTransf) при этом продолжает действовать как задано.
    /// Полезно, чтобы изолировать эффекты геометрической нелинейности (P-Δ/большие перемещения) от
    /// материальных при отладке/верификации расчёта.</summary>
    public bool ConsiderPhysicalNonlinearity { get; set; } = true;
    /// <summary>Источник диаграммы материала: "Translated" (перевод диаграммы CScore, по
    /// умолчанию) | "Native" (собственные параметрические материалы OpenSees).</summary>
    public string MaterialSource { get; set; } = "Translated";
    /// <summary>Нативная модель основной области при MaterialSource="Native":
    /// "Concrete0102" (Kent-Scott-Park), "Concrete04" (Popovics), "Steel01" или "Steel02".</summary>
    public string MainMaterialModel { get; set; } = "Concrete04";
    /// <summary>Модель стали/арматуры при MaterialSource="Native": "Steel01" | "Steel02".</summary>
    public string SteelModel { get; set; } = "Steel02";
    /// <summary>Переопределение отношения модуля упрочнения стали/арматуры к E0 при
    /// MaterialSource="Native". null — вычисляется автоматически из характеристик материала.</summary>
    public double? SteelHardeningRatioOverride { get; set; }
    /// <summary>Абсолютный модуль упрочнения арматуры после текучести в МПа для нелинейного
    /// OpenSees-расчёта. Ноль задаёт горизонтальный хвост диаграммы; null означает, что поле
    /// отсутствовало в legacy JSON и постановка ещё не пересохранялась с этой настройкой.</summary>
    public double? SteelHardeningModulusMpa { get; set; } = 0;
    /// <summary>Формулировка стержневого элемента: "forceBeamColumn" (по умолчанию, force-based) |
    /// "dispBeamColumn" (displacement-based, устойчивее к вырожденной матрице гибкости, когда все
    /// фибры сечения одновременно попадают на строго горизонтальный (нулевой) хвост диаграммы —
    /// характерно для кинематических нагрузок с полностью отключённым/оборванным растяжением
    /// бетона). Эмпирически подтверждено: на реальном кинематическом сценарии dispBeamColumn
    /// сошёлся дальше forceBeamColumn и без единой ошибки обращения матрицы.</summary>
    public string ElementFormulation { get; set; } = "forceBeamColumn";
    /// <summary>Стадии нагружения нелинейной постановки в порядке приложения. Используется только
    /// при Kind="nonlinear". Пусто для легаси-постановок, сохранённых до появления многостадийного
    /// нагружения — см. ResolveStages.</summary>
    public List<FemAnalysisStage> Stages { get; set; } = [];
    /// <summary>Параметры секущего расчёта CSfea (Kind = "csfea_secant"); null — по умолчанию.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public FemCsfeaParams? Csfea { get; set; }

    /// <summary>Возвращает стадии постановки; если Stages пуст (легаси-постановка), синтезирует
    /// одну стадию из единственного LoadExpressionJson постановки (историческое поведение до
    /// появления многостадийного нагружения).</summary>
    public IReadOnlyList<FemAnalysisStage> ResolveStages(CScore.Fem.FemAnalysis analysis) =>
        Stages.Count > 0 ? Stages : [new FemAnalysisStage
        {
            Tag = analysis.Tag, LoadExpressionJson = analysis.LoadExpressionJson,
            LoadFactorStep = LoadFactorStep, MaxLoadFactor = MaxLoadFactor
        }];

    public string ToJson() => JsonSerializer.Serialize(this);
    public static FemAnalysisParams Parse(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return new();
        var result = JsonSerializer.Deserialize<FemAnalysisParams>(json) ?? new();
        using var doc = JsonDocument.Parse(json);
        if (!doc.RootElement.TryGetProperty(nameof(SteelHardeningModulusMpa), out _))
            result.SteelHardeningModulusMpa = null;
        if (!doc.RootElement.TryGetProperty("MainMaterialModel", out _) &&
            doc.RootElement.TryGetProperty("ConcreteModel", out var legacyConcreteModel) &&
            legacyConcreteModel.ValueKind == JsonValueKind.String)
        {
            result.MainMaterialModel = legacyConcreteModel.GetString() ?? result.MainMaterialModel;
        }
        if (!doc.RootElement.TryGetProperty("LoadFactorStep", out _) && result.LoadSteps is > 0)
            result.LoadFactorStep = 1.0 / result.LoadSteps.Value;
        result.LoadSteps = null;
        foreach (var stage in result.Stages)
        {
            stage.LoadFactorStep ??= result.LoadFactorStep;
            stage.MaxLoadFactor ??= result.MaxLoadFactor;
        }
        return result;
    }
}

/// <summary>Какие шаги секущего расчёта CSfea записывают полные поля (сводка шагов пишется всегда).</summary>
public static class FemCsfeaRecording
{
    /// <summary>Только конечный результат.</summary>
    public const string Final = "Final";
    /// <summary>Выбранные шаги (<see cref="FemCsfeaParams.RecordSteps"/>) и, по флажку, концы стадий.</summary>
    public const string Selected = "Selected";
    /// <summary>Все шаги подряд.</summary>
    public const string All = "All";
}

/// <summary>
/// Параметры секущего расчёта CSfea в <see cref="FemAnalysisParams.Csfea"/>: физика сечений, итерации Пикара, запись
/// результатов, контрольный узел. Значения по умолчанию — умолчания ядра (<c>RcSecantOptions</c>,
/// <c>SecantPicardOptions</c>). Вид расчёта — общий <see cref="FemAnalysisParams.CalcType"/>, стадии —
/// <see cref="FemAnalysisParams.Stages"/> (path control не используется).
/// </summary>
public sealed class FemCsfeaParams
{
    /// <summary>Источник армирования пластин (<see cref="CScore.Fem.FemCheckRebarSource"/>), как в проверке по КЭ.</summary>
    public string PlateRebarSource { get; set; } = CScore.Fem.FemCheckRebarSource.Section;
    /// <summary>Работа бетона на растяжение до трещины: null — как в сечении (пластины), стержни — да.</summary>
    public bool? TensionConcrete { get; set; }
    /// <summary>ψs арматуры у трещин (п. 8.2.32 СП 63).</summary>
    public bool Psi { get; set; } = true;
    /// <summary>Правило выключения растянутого бетона пластин трещиной.</summary>
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public CSfea.CScoreBridge.Structural.PlateCrackRule PlateCrackRule { get; set; } = CSfea.CScoreBridge.Structural.PlateCrackRule.Layer;
    /// <summary>Сдвиговые деформации стержней по хомутам (КЭ Тимошенко).</summary>
    public bool BeamShear { get; set; } = true;
    /// <summary>ν бетона до трещины; null — как в сечении.</summary>
    public double? PoissonUncracked { get; set; }
    /// <summary>Геометрическая нелинейность (оболочки — фон Карман, стержни — CR).</summary>
    public bool GeomNonlinear { get; set; }

    /// <summary>Наибольшее число итераций Пикара на шаге.</summary>
    public int MaxIterations { get; set; } = 50;
    /// <summary>Допуск ‖Δu‖/‖u‖.</summary>
    public double TolDisplacement { get; set; } = 1e-4;
    /// <summary>Допуск изменения секущих жёсткостей (мера по работе).</summary>
    public double TolStiffness { get; set; } = 1e-3;
    /// <summary>Дроблений шага пополам при несходимости.</summary>
    public int MaxBisections { get; set; } = 4;
    /// <summary>Начальный коэффициент релаксации жёсткостей.</summary>
    public double Omega0 { get; set; } = 0.7;

    /// <summary>Запись полей шагов: <see cref="FemCsfeaRecording"/>.</summary>
    public string ResultRecording { get; set; } = FemCsfeaRecording.Final;
    /// <summary>Сквозные номера шагов для <see cref="FemCsfeaRecording.Selected"/>: «5, 10, 15».</summary>
    public string RecordSteps { get; set; } = "";
    /// <summary>При <see cref="FemCsfeaRecording.Selected"/> — записывать и концы стадий.</summary>
    public bool RecordStageEnds { get; set; } = true;

    /// <summary>Контрольный узел графика «λ — перемещение»: тег узла сетки; пусто — не задан.</summary>
    public string ControlNodeTag { get; set; } = "";
    /// <summary>DOF контрольного узла: 0–2 — ux, uy, uz; 3–5 — повороты.</summary>
    public int ControlDof { get; set; } = 2;

    /// <summary>
    /// Номера шагов из <see cref="RecordSteps"/> (разделители — запятая, точка с запятой, пробел), по возрастанию без
    /// повторов; null и текст ошибки — если есть не целое положительное число.
    /// </summary>
    public static IReadOnlyList<int>? ParseRecordSteps(string? text, out string? error)
    {
        error = null;
        var result = new SortedSet<int>();
        foreach (var part in (text ?? "").Split([',', ';', ' ', '\t'], StringSplitOptions.RemoveEmptyEntries))
        {
            if (!int.TryParse(part, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture,
                    out int n) || n < 1)
            {
                error = $"Номер шага «{part}» — не целое положительное число.";
                return null;
            }
            result.Add(n);
        }
        return result.ToList();
    }
}
