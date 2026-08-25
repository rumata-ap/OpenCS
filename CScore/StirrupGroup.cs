using System.Text.Json.Serialization;

namespace CScore;

/// <summary>Группа элементов поперечного армирования одной марки стали и одного шага: замкнутые хомуты и открытые срезы-стержни.</summary>
public sealed class StirrupGroup
{
    /// <summary>Идентификатор группы в хранилище.</summary>
    public int Id { get; set; }
    /// <summary>Идентификатор материала поперечной арматуры.</summary>
    public int MaterialId { get; set; }
    /// <summary>Шаг хомутов вдоль оси стержня, м.</summary>
    public double SpacingM { get; set; }
    /// <summary>Отступ по умолчанию для вновь создаваемых элементов группы, м.</summary>
    public double? OffsetM { get; set; }
    /// <summary>Набор независимых элементов поперечного армирования в одном сечении.</summary>
    public List<StirrupElement> Elements { get; set; } = [];

    /// <summary>Проверяет допустимость группы для заданной области.</summary>
    public void ValidateFor(MaterialArea area)
    {
        ArgumentNullException.ThrowIfNull(area);
        if (area.Category == AreaCategory.Stirrups)
        {
            if (area.HostAreaId != null)
                throw new ArgumentException(
                    "У области поперечного армирования HostAreaId должен быть null: иначе она получит разностную диаграмму продольной арматуры.",
                    nameof(area));
            if (area.MaterialId > 0 && MaterialId != area.MaterialId)
                throw new ArgumentException(
                    "Материал группы хомутов должен совпадать с материалом области.", nameof(area));
        }
        else if (area.Category != AreaCategory.Region || area.Hull is null || string.IsNullOrWhiteSpace(area.WKT))
        {
            throw new ArgumentException(
                "Группа хомутов допустима для области поперечного армирования либо для полигональной области бетона.",
                nameof(area));
        }
        if (MaterialId <= 0)
            throw new ArgumentOutOfRangeException(nameof(MaterialId), MaterialId, "Идентификатор материала должен быть положительным.");
        ValidatePositiveFinite(SpacingM, nameof(SpacingM));
        if (Elements.Count == 0)
            throw new ArgumentException("Группа хомутов должна содержать хотя бы один элемент.", nameof(Elements));
        foreach (var element in Elements)
        {
            ArgumentNullException.ThrowIfNull(element);
            element.Validate();
        }
    }

    /// <summary>Создаёт глубокую копию группы.</summary>
    public StirrupGroup Clone(bool preserveId) => new()
    {
        Id = preserveId ? Id : 0,
        MaterialId = MaterialId,
        SpacingM = SpacingM,
        OffsetM = OffsetM,
        Elements = Elements.Select(element => element.Clone(preserveId)).ToList()
    };

    internal static void ValidatePositiveFinite(double value, string name)
    {
        if (!double.IsFinite(value) || value <= 0.0)
            throw new ArgumentOutOfRangeException(name, value, "Значение должно быть конечным и строго положительным.");
    }
}

/// <summary>Один центровой элемент поперечного армирования: замкнутый хомут или открытый срез-стержень.</summary>
public sealed class StirrupElement
{
    /// <summary>Идентификатор элемента в хранилище.</summary>
    public int Id { get; set; }
    /// <summary>Центровая линия стержня поперечного армирования.</summary>
    public Contour CenterlineContour { get; set; } = new();
    /// <summary>Площадь сечения одного стержня хомута, м².</summary>
    public double BarAreaM2 { get; set; }
    /// <summary>Номинальный диаметр стержня хомута, м.</summary>
    public double BarDiameterM { get; set; }
    /// <summary>Параметры построения элемента; null означает ручную геометрию.</summary>
    public StirrupElementSource? Source { get; set; }

    /// <summary>Признак замкнутого хомута, а не открытого среза-стержня.</summary>
    [JsonIgnore]
    public bool IsClosed => CenterlineContour.IsClosed;

    /// <summary>Проверяет геометрию и характеристики одного элемента.</summary>
    public void Validate()
    {
        ArgumentNullException.ThrowIfNull(CenterlineContour);
        StirrupGroup.ValidatePositiveFinite(BarAreaM2, nameof(BarAreaM2));
        StirrupGroup.ValidatePositiveFinite(BarDiameterM, nameof(BarDiameterM));
        var x = CenterlineContour.X;
        var y = CenterlineContour.Y;
        if (x.Count != y.Count)
            throw new ArgumentException("Центровая линия хомута должна иметь согласованные координаты.", nameof(CenterlineContour));
        if (!CenterlineContour.IsPolyline && !IsClosed)
            throw new ArgumentException("Центровая линия должна быть замкнутым контуром или открытой полилинией.", nameof(CenterlineContour));

        if (x.Count < 2 || PolylineLength(x, y) <= 1e-9)
            throw new ArgumentException("Центровая линия должна содержать минимум две вершины и иметь ненулевую длину.", nameof(CenterlineContour));

        if (IsClosed)
        {
            int distinctVertices = x.Zip(y, (px, py) => (px, py)).Take(x.Count - 1).Distinct().Count();
            if (distinctVertices < 3)
                throw new ArgumentException("Замкнутый хомут должен содержать минимум три различные вершины.", nameof(CenterlineContour));
        }
    }

    static double PolylineLength(IList<double> x, IList<double> y)
    {
        double length = 0.0;
        for (int i = 1; i < x.Count; i++)
            length += Math.Sqrt(Math.Pow(x[i] - x[i - 1], 2) + Math.Pow(y[i] - y[i - 1], 2));
        return length;
    }

    /// <summary>Создаёт глубокую копию элемента и его геометрии.</summary>
    public StirrupElement Clone(bool preserveId) => new()
    {
        Id = preserveId ? Id : 0,
        CenterlineContour = CenterlineContour.CloneForCalc(),
        BarAreaM2 = BarAreaM2,
        BarDiameterM = BarDiameterM,
        Source = Source?.Clone()
    };
}
