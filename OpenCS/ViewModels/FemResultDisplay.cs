using System.Windows.Media.Media3D;
using OpenCS.OpenSees.Structural;

namespace OpenCS.ViewModels;

/// <summary>Единица отображения линейных результатов, хранящихся в метрах.</summary>
public enum FemLengthUnit
{
    /// <summary>Миллиметры.</summary>
    Millimeters,
    /// <summary>Сантиметры.</summary>
    Centimeters,
    /// <summary>Метры.</summary>
    Meters
}

/// <summary>Коэффициент отображения углов поворота, хранящихся в радианах.</summary>
public enum FemRotationScale
{
    /// <summary>Радианы без дополнительного коэффициента.</summary>
    One = 1,
    /// <summary>Радианы, умноженные на 100.</summary>
    OneHundred = 100,
    /// <summary>Радианы, умноженные на 1000.</summary>
    OneThousand = 1000
}

/// <summary>Режим состава узловой таблицы результатов.</summary>
public enum FemDisplacementDisplayMode
{
    /// <summary>Показывать все доступные узловые результаты.</summary>
    AllNodes,
    /// <summary>Показывать только экстремальные узлы по стержням.</summary>
    ExtremesOnly
}

/// <summary>Чистые преобразования результатов FEM для отображения.</summary>
public static class FemResultDisplayConverter
{
    /// <summary>Переводит длину из метров в выбранную единицу.</summary>
    public static double ToLength(double meters, FemLengthUnit unit) => ConvertLength(meters, unit);

    /// <summary>Переводит длину из метров в выбранную единицу.</summary>
    public static double ConvertLength(double meters, FemLengthUnit unit) =>
        meters * (unit switch
        {
            FemLengthUnit.Millimeters => 1000.0,
            FemLengthUnit.Centimeters => 100.0,
            FemLengthUnit.Meters => 1.0,
            _ => throw new ArgumentOutOfRangeException(nameof(unit), unit, null)
        });

    /// <summary>Возвращает формат округления длины для 3D-подписи.</summary>
    public static string LengthValueFormat(FemLengthUnit unit) => unit switch
    {
        FemLengthUnit.Millimeters => "F1",
        FemLengthUnit.Centimeters => "F2",
        FemLengthUnit.Meters => "F4",
        _ => throw new ArgumentOutOfRangeException(nameof(unit), unit, null)
    };

    /// <summary>Переводит радианное значение поворота в выбранный масштаб.</summary>
    public static double ToRotation(double radians, FemRotationScale scale) => ConvertRotation(radians, scale);

    /// <summary>Переводит радианное значение поворота в выбранный масштаб.</summary>
    public static double ConvertRotation(double radians, FemRotationScale scale) =>
        radians * (int)scale;
}

/// <summary>Рассчитывает масштаб ленты одной ненулевой эпюры усилия.</summary>
public static class FemForceScaleCalculator
{
    /// <summary>
    /// Возвращает масштаб в метрах на кН для одной компоненты усилия.
    /// Нулевые и нечисловые значения не участвуют в расчёте.
    /// </summary>
    public static double Suggest(double geometryDiagonalM, IReadOnlyList<double> values)
    {
        if (!double.IsFinite(geometryDiagonalM) || geometryDiagonalM <= 0)
            return 1.0;

        double maxValue = 0.0;
        foreach (double value in values)
        {
            if (double.IsFinite(value))
                maxValue = Math.Max(maxValue, Math.Abs(value));
        }

        if (maxValue <= 1e-12)
            return 1.0;

        double maxValueKN = maxValue / 1000.0;
        double result = 0.1 * geometryDiagonalM / maxValueKN;
        if (!double.IsFinite(result) || result <= 0) return 1.0;
        double rounded = Math.Round(result, 2);
        return rounded > 0 ? rounded : result;
    }
}

/// <summary>Хранит ручные переопределения и автоматические масштабы по компонентам.</summary>
public sealed class FemForceScaleState
{
    readonly Dictionary<FemForceComponent, ScaleEntry> _values = [];

    /// <summary>Возвращает масштаб компоненты, вычисляя его при первом обращении.</summary>
    public double Get(FemForceComponent component, Func<double> automaticFactory)
    {
        if (!_values.TryGetValue(component, out ScaleEntry entry))
        {
            entry = new ScaleEntry(Normalize(automaticFactory()), false);
            _values[component] = entry;
        }

        return entry.Value;
    }

    /// <summary>Устанавливает ручной масштаб, который не заменяется автообновлением.</summary>
    public void SetManual(FemForceComponent component, double value) =>
        _values[component] = new ScaleEntry(Normalize(value), true);

    /// <summary>Записывает автоматический масштаб, не снимая ручной override.</summary>
    public void SetAutomatic(FemForceComponent component, double value)
    {
        if (_values.TryGetValue(component, out ScaleEntry entry) && entry.IsManual) return;
        _values[component] = new ScaleEntry(Normalize(value), false);
    }

    /// <summary>Сбрасывает ручное значение и записывает новый автоматический масштаб.</summary>
    public void Reset(FemForceComponent component, Func<double> automaticFactory) =>
        _values[component] = new ScaleEntry(Normalize(automaticFactory()), false);

    /// <summary>Обновляет только компоненты без ручного переопределения.</summary>
    public void RefreshAutomatic(params (FemForceComponent Component, double Value)[] values)
    {
        foreach ((FemForceComponent component, double value) in values)
            SetAutomatic(component, value);
    }

    /// <summary>Показывает, задан ли для компоненты ручной масштаб.</summary>
    public bool IsManual(FemForceComponent component) =>
        _values.TryGetValue(component, out ScaleEntry entry) && entry.IsManual;

    static double Normalize(double value) =>
        double.IsFinite(value) && value > 0 ? value : 1.0;

    readonly record struct ScaleEntry(double Value, bool IsManual);
}

/// <summary>Конечный mesh-элемент стержня с координатами его концов.</summary>
public sealed record FemMemberMeshElement(
    int ElementTag,
    int NodeITag,
    int NodeJTag,
    Point3D PointI,
    Point3D PointJ);

/// <summary>Геометрический контекст одного конструктивного стержня для построения эпюр.</summary>
public sealed record FemMemberGeometryContext(
    string MemberTag,
    Point3D Origin,
    Vector3D Direction,
    IReadOnlyList<FemMemberMeshElement> Elements);

/// <summary>Линейный сегмент эпюры в дуговых координатах стержня.</summary>
public sealed record FemDiagramSegment(
    int ElementTag,
    double S0,
    double S1,
    double Value0,
    double Value1);

/// <summary>Экстремальное значение эпюры с указанием mesh-элемента и положения.</summary>
public sealed record FemDiagramExtremum(
    int ElementTag,
    double Position,
    double Value,
    bool IsMaximum);

/// <summary>Набор сегментов одной эпюры и её глобальные экстремумы.</summary>
public sealed class FemDiagramSeries
{
    /// <summary>Сегменты эпюры, отсортированные по дуговой координате.</summary>
    public IReadOnlyList<FemDiagramSegment> Segments { get; }

    /// <summary>Минимум и максимум по значениям концов всех сегментов.</summary>
    public IReadOnlyList<FemDiagramExtremum> Extrema { get; }

    /// <summary>Пустая эпюра без сегментов и экстремумов.</summary>
    public static FemDiagramSeries Empty { get; } = new([]);

    /// <summary>Создаёт ряд и вычисляет его экстремумы по сырым значениям.</summary>
    public FemDiagramSeries(IReadOnlyList<FemDiagramSegment> segments)
    {
        Segments = segments;
        Extrema = BuildExtrema(segments);
    }

    static IReadOnlyList<FemDiagramExtremum> BuildExtrema(IReadOnlyList<FemDiagramSegment> segments)
    {
        var samples = segments
            .SelectMany(segment => new[]
            {
                new Sample(segment.ElementTag, segment.S0, segment.Value0),
                new Sample(segment.ElementTag, segment.S1, segment.Value1)
            })
            .Where(sample => double.IsFinite(sample.Value))
            .ToList();
        if (samples.Count == 0) return [];

        Sample min = samples
            .OrderBy(sample => sample.Value)
            .ThenBy(sample => sample.ElementTag)
            .ThenBy(sample => sample.Position)
            .First();
        Sample max = samples
            .OrderByDescending(sample => sample.Value)
            .ThenBy(sample => sample.ElementTag)
            .ThenBy(sample => sample.Position)
            .First();
        return
        [
            new FemDiagramExtremum(min.ElementTag, min.Position, min.Value, false),
            new FemDiagramExtremum(max.ElementTag, max.Position, max.Value, true)
        ];
    }

    readonly record struct Sample(int ElementTag, double Position, double Value);
}

/// <summary>Компонента узлового результата в глобальной системе координат.</summary>
public enum FemNodalComponent
{
    /// <summary>Перемещение по глобальной оси X.</summary>
    Ux,
    /// <summary>Перемещение по глобальной оси Y.</summary>
    Uy,
    /// <summary>Перемещение по глобальной оси Z.</summary>
    Uz,
    /// <summary>Поворот вокруг глобальной оси X.</summary>
    Rx,
    /// <summary>Поворот вокруг глобальной оси Y.</summary>
    Ry,
    /// <summary>Поворот вокруг глобальной оси Z.</summary>
    Rz
}

/// <summary>Группа результата, определяющая правило перевода значений для графика.</summary>
public enum FemResultGroup
{
    /// <summary>Силы и моменты: Н и Н·м переводятся в кН и кН·м.</summary>
    Forces,
    /// <summary>Глобальные линейные перемещения.</summary>
    Displacements,
    /// <summary>Глобальные углы поворота.</summary>
    Rotations
}

/// <summary>Строит общие ряды силовых и узловых результатов одного стержня.</summary>
public static class FemMemberResultSeriesBuilder
{
    /// <summary>Строит силовую эпюру по концевым результатам mesh-элементов.</summary>
    public static FemDiagramSeries BuildForces(
        FemMemberGeometryContext context,
        IReadOnlyDictionary<int, FemElementEndForces> values,
        FemForceComponent component)
    {
        var segments = new List<FemDiagramSegment>();
        foreach (FemMemberMeshElement element in context.Elements)
        {
            if (!values.TryGetValue(element.ElementTag, out FemElementEndForces? force))
                continue;

            FemForceEndpointPair pair = FemForceEndpointConverter.Convert(
                force, FemForceEndpointSignPolicy.OpenSeesDefault);
            double value0 = FemForceEndpointConverter.ReadComponent(pair.Start, component);
            double value1 = FemForceEndpointConverter.ReadComponent(pair.End, component);
            AddSegment(segments, context, element, value0, value1);
        }

        return new FemDiagramSeries(SortSegments(segments));
    }

    /// <summary>Строит эпюру глобальной узловой компоненты по результатам узлов.</summary>
    public static FemDiagramSeries BuildNodal(
        FemMemberGeometryContext context,
        IReadOnlyDictionary<int, FemNodeDisplacement> values,
        FemNodalComponent component)
    {
        var segments = new List<FemDiagramSegment>();
        foreach (FemMemberMeshElement element in context.Elements)
        {
            if (!values.TryGetValue(element.NodeITag, out FemNodeDisplacement? value0) ||
                !values.TryGetValue(element.NodeJTag, out FemNodeDisplacement? value1))
                continue;

            AddSegment(segments, context, element, ReadNodal(value0, component), ReadNodal(value1, component));
        }

        return new FemDiagramSeries(SortSegments(segments));
    }

    static void AddSegment(
        ICollection<FemDiagramSegment> segments,
        FemMemberGeometryContext context,
        FemMemberMeshElement element,
        double value0,
        double value1)
    {
        if (!double.IsFinite(value0) || !double.IsFinite(value1)) return;
        segments.Add(new FemDiagramSegment(
            element.ElementTag,
            ArcCoordinate(context, element.PointI),
            ArcCoordinate(context, element.PointJ),
            value0,
            value1));
    }

    static IReadOnlyList<FemDiagramSegment> SortSegments(IEnumerable<FemDiagramSegment> segments) =>
        segments
            .OrderBy(segment => Math.Min(segment.S0, segment.S1))
            .ThenBy(segment => Math.Max(segment.S0, segment.S1))
            .ThenBy(segment => segment.ElementTag)
            .ToList();

    static double ArcCoordinate(FemMemberGeometryContext context, Point3D point)
    {
        Vector3D direction = context.Direction;
        if (direction.Length <= 1e-12) direction = new Vector3D(1, 0, 0);
        else direction.Normalize();
        return Vector3D.DotProduct(point - context.Origin, direction);
    }

    static double ReadNodal(FemNodeDisplacement value, FemNodalComponent component) => component switch
    {
        FemNodalComponent.Ux => value.Ux,
        FemNodalComponent.Uy => value.Uy,
        FemNodalComponent.Uz => value.Uz,
        FemNodalComponent.Rx => value.Rx,
        FemNodalComponent.Ry => value.Ry,
        FemNodalComponent.Rz => value.Rz,
        _ => 0.0
    };
}

/// <summary>Переводит готовый ряд эпюры в единицы выбранной группы результата.</summary>
public static class FemDiagramValueScaler
{
    /// <summary>Возвращает новый ряд, не изменяя исходные сырые значения.</summary>
    public static FemDiagramSeries Scale(
        FemDiagramSeries series,
        FemResultGroup group,
        FemLengthUnit lengthUnit,
        FemRotationScale rotationScale)
    {
        double ScaleValue(double value) => group switch
        {
            FemResultGroup.Forces => value / 1000.0,
            FemResultGroup.Displacements => FemResultDisplayConverter.ToLength(value, lengthUnit),
            FemResultGroup.Rotations => FemResultDisplayConverter.ToRotation(value, rotationScale),
            _ => value
        };

        var segments = series.Segments
            .Select(segment => new FemDiagramSegment(
                segment.ElementTag,
                segment.S0,
                segment.S1,
                ScaleValue(segment.Value0),
                ScaleValue(segment.Value1)))
            .ToList();
        return new FemDiagramSeries(segments);
    }
}

/// <summary>Строка отображаемой таблицы глобальных перемещений узла.</summary>
public sealed record FemNodeDisplacementRow(
    string? MemberTag,
    int NodeTag,
    double Ux,
    double Uy,
    double Uz,
    double Rx,
    double Ry,
    double Rz,
    IReadOnlyList<FemNodalComponent> ExtremeComponents);

/// <summary>Данные одной 3D-подписи выбранной узловой компоненты.</summary>
public sealed record FemNodeResultLabelData(
    int NodeTag,
    FemNodeDisplacementRow Row,
    FemNodalComponent Component,
    double Value);

/// <summary>Подготавливает уникальные подписи для узлового слоя 3D-вида.</summary>
public static class FemNodeResultLabelDataBuilder
{
    /// <summary>
    /// Оставляет одну строку на узел. В режиме экстремумов один общий узел может
    /// присутствовать в таблице несколько раз — для каждого конструктивного стержня.
    /// </summary>
    public static IReadOnlyList<FemNodeResultLabelData> Build(
        IReadOnlyList<FemNodeDisplacementRow> rows,
        FemNodalComponent component,
        FemDisplacementDisplayMode mode)
    {
        IEnumerable<FemNodeDisplacementRow> candidates = mode == FemDisplacementDisplayMode.ExtremesOnly
            ? rows.Where(row => row.ExtremeComponents.Contains(component))
            : rows;

        return candidates
        .GroupBy(row => row.NodeTag)
        .Select(group => group
            .OrderBy(row => row.MemberTag ?? string.Empty, StringComparer.Ordinal)
            .ThenBy(row => row.NodeTag)
            .First())
        .OrderBy(label => label.NodeTag)
        .Select(row => new FemNodeResultLabelData(row.NodeTag, row, component, Read(row, component)))
        .ToList();
    }

    static double Read(FemNodeDisplacementRow row, FemNodalComponent component) => component switch
    {
        FemNodalComponent.Ux => row.Ux,
        FemNodalComponent.Uy => row.Uy,
        FemNodalComponent.Uz => row.Uz,
        FemNodalComponent.Rx => row.Rx,
        FemNodalComponent.Ry => row.Ry,
        FemNodalComponent.Rz => row.Rz,
        _ => 0.0
    };
}

/// <summary>Строит полную или экстремальную таблицу узловых результатов.</summary>
public static class FemDisplacementTableBuilder
{
    /// <summary>
    /// Возвращает отображаемые строки. Экстремумы выбираются по сырым значениям,
    /// после чего все шесть компонент переводятся в выбранные единицы.
    /// </summary>
    public static IReadOnlyList<FemNodeDisplacementRow> Build(
        IReadOnlyList<FemNodeDisplacement> values,
        IReadOnlyDictionary<string, IReadOnlyList<int>> memberNodes,
        FemDisplacementDisplayMode mode,
        FemLengthUnit lengthUnit,
        FemRotationScale rotationScale)
    {
        var rawByTag = values
            .GroupBy(value => value.NodeTag)
            .ToDictionary(group => group.Key, group => group.Last());

        if (mode == FemDisplacementDisplayMode.AllNodes)
            return values.Select(value => ToRow(null, value, [], lengthUnit, rotationScale)).ToList();

        var reasons = new Dictionary<(string MemberTag, int NodeTag), HashSet<FemNodalComponent>>();
        foreach ((string memberTag, IReadOnlyList<int> nodeTags) in memberNodes.OrderBy(item => item.Key, StringComparer.Ordinal))
        {
            var candidates = nodeTags
                .Distinct()
                .Where(rawByTag.ContainsKey)
                .Select(nodeTag => rawByTag[nodeTag])
                .ToList();
            foreach (FemNodalComponent component in Enum.GetValues<FemNodalComponent>())
            {
                var finite = candidates
                    .Where(value => double.IsFinite(Read(value, component)))
                    .ToList();
                if (finite.Count == 0) continue;

                FemNodeDisplacement min = finite
                    .OrderBy(value => Read(value, component))
                    .ThenBy(value => value.NodeTag)
                    .First();
                FemNodeDisplacement max = finite
                    .OrderByDescending(value => Read(value, component))
                    .ThenBy(value => value.NodeTag)
                    .First();
                AddReason(reasons, memberTag, min.NodeTag, component);
                AddReason(reasons, memberTag, max.NodeTag, component);
            }
        }

        return reasons
            .OrderBy(item => item.Key.MemberTag, StringComparer.Ordinal)
            .ThenBy(item => item.Key.NodeTag)
            .Select(item => ToRow(
                item.Key.MemberTag,
                rawByTag[item.Key.NodeTag],
                item.Value.OrderBy(component => component).ToArray(),
                lengthUnit,
                rotationScale))
            .ToList();
    }

    static void AddReason(
        IDictionary<(string MemberTag, int NodeTag), HashSet<FemNodalComponent>> reasons,
        string memberTag,
        int nodeTag,
        FemNodalComponent component)
    {
        var key = (memberTag, nodeTag);
        if (!reasons.TryGetValue(key, out HashSet<FemNodalComponent>? components))
            reasons[key] = components = [];
        components.Add(component);
    }

    static FemNodeDisplacementRow ToRow(
        string? memberTag,
        FemNodeDisplacement value,
        IReadOnlyList<FemNodalComponent> extremeComponents,
        FemLengthUnit lengthUnit,
        FemRotationScale rotationScale) =>
        new(
            memberTag,
            value.NodeTag,
            FemResultDisplayConverter.ToLength(value.Ux, lengthUnit),
            FemResultDisplayConverter.ToLength(value.Uy, lengthUnit),
            FemResultDisplayConverter.ToLength(value.Uz, lengthUnit),
            FemResultDisplayConverter.ToRotation(value.Rx, rotationScale),
            FemResultDisplayConverter.ToRotation(value.Ry, rotationScale),
            FemResultDisplayConverter.ToRotation(value.Rz, rotationScale),
            extremeComponents);

    static double Read(FemNodeDisplacement value, FemNodalComponent component) => component switch
    {
        FemNodalComponent.Ux => value.Ux,
        FemNodalComponent.Uy => value.Uy,
        FemNodalComponent.Uz => value.Uz,
        FemNodalComponent.Rx => value.Rx,
        FemNodalComponent.Ry => value.Ry,
        FemNodalComponent.Rz => value.Rz,
        _ => 0.0
    };
}
