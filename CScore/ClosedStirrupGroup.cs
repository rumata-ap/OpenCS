namespace CScore;

/// <summary>Группа равномерно расположенных замкнутых хомутов одной марки стали и одного шага.</summary>
public sealed class ClosedStirrupGroup
{
    /// <summary>Идентификатор группы в хранилище.</summary>
    public int Id { get; set; }
    /// <summary>Идентификатор материала поперечной арматуры.</summary>
    public int MaterialId { get; set; }
    /// <summary>Шаг хомутов вдоль оси стержня, м.</summary>
    public double SpacingM { get; set; }
    /// <summary>Набор независимых замкнутых контуров хомутов в одном сечении.</summary>
    public List<ClosedStirrupLoop> Loops { get; set; } = [];

    /// <summary>Проверяет допустимость группы для заданной бетонной полигональной области.</summary>
    public void ValidateFor(MaterialArea area)
    {
        ArgumentNullException.ThrowIfNull(area);
        if (area.Category != AreaCategory.Region || area.Hull is null || string.IsNullOrWhiteSpace(area.WKT))
            throw new ArgumentException("Замкнутые хомуты допустимы только для полигональной области бетона.", nameof(area));
        if (MaterialId <= 0)
            throw new ArgumentOutOfRangeException(nameof(MaterialId), MaterialId, "Идентификатор материала должен быть положительным.");
        ValidatePositiveFinite(SpacingM, nameof(SpacingM));
        if (Loops.Count == 0)
            throw new ArgumentException("Группа хомутов должна содержать хотя бы один замкнутый контур.", nameof(Loops));
        foreach (var loop in Loops)
        {
            ArgumentNullException.ThrowIfNull(loop);
            loop.Validate();
        }
    }

    /// <summary>Создаёт глубокую копию группы.</summary>
    public ClosedStirrupGroup Clone(bool preserveId) => new()
    {
        Id = preserveId ? Id : 0,
        MaterialId = MaterialId,
        SpacingM = SpacingM,
        Loops = Loops.Select(loop => loop.Clone(preserveId)).ToList()
    };

    internal static void ValidatePositiveFinite(double value, string name)
    {
        if (!double.IsFinite(value) || value <= 0.0)
            throw new ArgumentOutOfRangeException(name, value, "Значение должно быть конечным и строго положительным.");
    }
}

/// <summary>Один явный центровой контур замкнутого хомута.</summary>
public sealed class ClosedStirrupLoop
{
    /// <summary>Идентификатор контура в хранилище.</summary>
    public int Id { get; set; }
    /// <summary>Замкнутый контур по оси стержня хомута.</summary>
    public Contour CenterlineContour { get; set; } = new();
    /// <summary>Площадь сечения одного стержня хомута, м².</summary>
    public double BarAreaM2 { get; set; }
    /// <summary>Номинальный диаметр стержня хомута, м.</summary>
    public double BarDiameterM { get; set; }

    /// <summary>Проверяет геометрию и характеристики одного контура хомута.</summary>
    public void Validate()
    {
        ArgumentNullException.ThrowIfNull(CenterlineContour);
        ClosedStirrupGroup.ValidatePositiveFinite(BarAreaM2, nameof(BarAreaM2));
        ClosedStirrupGroup.ValidatePositiveFinite(BarDiameterM, nameof(BarDiameterM));
        var x = CenterlineContour.X;
        var y = CenterlineContour.Y;
        if (x.Count != y.Count || x.Count < 4 ||
            Math.Abs(x[0] - x[^1]) >= Contour.CloseTolerance ||
            Math.Abs(y[0] - y[^1]) >= Contour.CloseTolerance)
            throw new ArgumentException("Центровой контур хомута должен быть замкнутым и содержать минимум три вершины.", nameof(CenterlineContour));
        int distinctVertices = x.Zip(y, (px, py) => (px, py)).Take(x.Count - 1).Distinct().Count();
        if (distinctVertices < 3)
            throw new ArgumentException("Центровой контур хомута должен содержать минимум три различные вершины.", nameof(CenterlineContour));
    }

    /// <summary>Создаёт глубокую копию контура хомута.</summary>
    public ClosedStirrupLoop Clone(bool preserveId) => new()
    {
        Id = preserveId ? Id : 0,
        CenterlineContour = CenterlineContour.CloneForCalc(),
        BarAreaM2 = BarAreaM2,
        BarDiameterM = BarDiameterM
    };
}
