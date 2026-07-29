using System.Text.Json;
using System.Text.Json.Serialization;
using CScore;

namespace OpenCS.Tasks;

/// <summary>Параметры запуска FEM-расчёта (линейного и нелинейного), хранимые в FemAnalysis.ParamsJson.
/// Поля CalcType/ConsiderConcreteTension/MaterialSource/ConcreteModel/SteelModel/
/// SteelHardeningRatioOverride используются только при Kind="nonlinear" и специфичны для
/// конкретной постановки. Solver-механика (исполняемый файл, таймаут, сходимость, geomTransf,
/// точки интегрирования) — глобальная, см. <see cref="OpenCS.Utilites.CalcSettings"/> (вкладка
/// «OpenSees» в диалоге настроек), а не хранится в каждой постановке.</summary>
/// <summary>Одна стадия нагружения нелинейной постановки: имя + выражение выбора загружения
/// (сериализованный FemLoadExpression), резолвится независимо от остальных стадий.</summary>
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
    /// трещинообразования/текучести) независимо от MaterialSource/ConcreteModel/SteelModel —
    /// геометрическая нелинейность (geomTransf) при этом продолжает действовать как задано.
    /// Полезно, чтобы изолировать эффекты геометрической нелинейности (P-Δ/большие перемещения) от
    /// материальных при отладке/верификации расчёта.</summary>
    public bool ConsiderPhysicalNonlinearity { get; set; } = true;
    /// <summary>Источник диаграммы материала: "Translated" (перевод диаграммы CScore, по
    /// умолчанию) | "Native" (собственные параметрические материалы OpenSees).</summary>
    public string MaterialSource { get; set; } = "Translated";
    /// <summary>Модель бетона при MaterialSource="Native": "Concrete0102" (Kent-Scott-Park,
    /// Concrete02/01) | "Concrete04" (Popovics, по умолчанию).</summary>
    public string ConcreteModel { get; set; } = "Concrete04";
    /// <summary>Модель стали/арматуры при MaterialSource="Native": "Steel01" | "Steel02".</summary>
    public string SteelModel { get; set; } = "Steel02";
    /// <summary>Переопределение отношения модуля упрочнения стали/арматуры к E0 при
    /// MaterialSource="Native". null — вычисляется автоматически из характеристик материала.</summary>
    public double? SteelHardeningRatioOverride { get; set; }
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
