using System.Text.Json;
using System.Text.Json.Serialization;

namespace CScore.Fem.Fields;

/// <summary>Привязка столбца значений таблицы к компоненте величины и единице ввода.</summary>
public sealed record FemFieldColumn(string Header, string Component, string UnitLabel, double UnitFactor);

/// <summary>
/// Цели поля: группы, КонЭ, явные КЭ и узлы сетки. Всё пусто — вся схема (все пластины / все узлы с пружинами).
/// Цели — выбор, а не замороженный список: поле переживает повторный импорт и перестроение сетки.
/// </summary>
public sealed class FemFieldTargets
{
    public List<int> GroupIds { get; set; } = [];
    public List<string> MemberTags { get; set; } = [];
    public List<string> ElemTags { get; set; } = [];
    public List<string> NodeTags { get; set; } = [];
    /// <summary>Для пружин: из узлов КЭ целей (или всей схемы) брать только узлы, у которых уже есть пружина
    /// (сваи КЭ 56). Явно заданные <see cref="NodeTags"/> фильтр не трогает.</summary>
    public bool OnlyNodesWithSprings { get; set; } = true;

    [JsonIgnore]
    public bool IsEmpty => GroupIds.Count == 0 && MemberTags.Count == 0 && ElemTags.Count == 0 && NodeTags.Count == 0;
}

/// <summary>Описание поля, хранимое JSON: столбцы, совмещение, цели, способ и допуски.</summary>
public sealed class FemFieldDefinition
{
    static readonly JsonSerializerOptions Json = new() { Converters = { new JsonStringEnumConverter() } };

    public string XHeader { get; set; } = "X";
    public string YHeader { get; set; } = "Y";
    public string? ZHeader { get; set; }
    public List<FemFieldColumn> Columns { get; set; } = [];
    public FieldAlignment Alignment { get; set; } = new();
    public FemFieldTargets Targets { get; set; } = new();
    public FieldSettings Settings { get; set; } = new();

    public string ToJson() => JsonSerializer.Serialize(this, Json);
    public static FemFieldDefinition FromJson(string json) => JsonSerializer.Deserialize<FemFieldDefinition>(json, Json) ?? new();
}

/// <summary>
/// Поле из таблицы точек — объект МКЭ-схемы. Точки хранятся в единицах таблицы (до совмещения), значения — в единицах
/// ввода по <see cref="FemFieldDefinition.Columns"/>; пересчёт в СИ — при вычислении.
/// </summary>
public sealed class FemField
{
    public int Id { get; set; }
    public int SchemaId { get; set; }
    public string Name { get; set; } = "";
    public string Quantity { get; set; } = FemFieldQuantities.FoundationC1;
    public FemFieldDefinition Definition { get; set; } = new();
    public List<FieldPoint> Points { get; set; } = [];
    public bool HasZ { get; set; }
    public string? SourceFile { get; set; }
    public bool Enabled { get; set; } = true;
    /// <summary>Порядок среди полей схемы: при наложении последнее выигрывает.</summary>
    public int SortOrder { get; set; }
    /// <summary>Подпись целей, по которой посчитан кэш значений; null — не считано.</summary>
    public string? MeshSignature { get; set; }
}

/// <summary>Вычисленное значение поля по цели и компоненте, в СИ; null — значения нет.</summary>
public sealed record FemFieldValue(int FieldId, FemFieldTargetKind TargetKind, string TargetTag, string Component,
    double? Value, FieldHow How, double Distance);

/// <summary>Ручная правка величины цели (верхний слой, редактор — следующим шагом), в СИ.</summary>
public sealed record FemParamOverride(FemFieldTargetKind TargetKind, string TargetTag, string Quantity, string Component, double Value);
