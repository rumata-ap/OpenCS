using System.Text.Json;
using System.Text.Json.Serialization;
using CScore.Sp63Punching;

namespace OpenCS.Tasks;

/// <summary>Грузовая площадь: форма и размеры, м.</summary>
public sealed record PunchingAreaParams
{
    /// <summary>"rectangle" | "circle" | "ring" | "polygon".</summary>
    public string Shape { get; init; } = "rectangle";
    /// <summary>Размер вдоль X, м.</summary>
    public double Bx { get; init; } = 0.4;
    /// <summary>Размер вдоль Y, м.</summary>
    public double By { get; init; } = 0.4;
    /// <summary>Диаметр круга / наружный диаметр кольца, м.</summary>
    public double D { get; init; } = 0.4;
    /// <summary>Толщина стенки кольца, м.</summary>
    public double T { get; init; } = 0.1;
    /// <summary>Вершины многоугольника [[x, y], …], м.</summary>
    public List<double[]> Points { get; init; } = [];
}

/// <summary>Плита: толщина, привязки арматуры к растянутой грани, бетон.</summary>
public sealed record PunchingSlabParams
{
    /// <summary>Толщина плиты, м.</summary>
    public double H { get; init; } = 0.2;
    /// <summary>Привязка центра арматуры вдоль X к растянутой грани, м.</summary>
    public double Ax { get; init; } = 0.035;
    /// <summary>Привязка центра арматуры вдоль Y к растянутой грани, м.</summary>
    public double Ay { get; init; } = 0.045;
    /// <summary>Id бетона проекта; 0 — не выбран.</summary>
    public int ConcreteMaterialId { get; init; }
    /// <summary>Ручное Rbt, кПа (с коэффициентами условий работы); null — из материала.</summary>
    public double? RbtOverride { get; init; }
}

/// <summary>Свободный край плиты относительно грузовой площади.</summary>
public sealed record PunchingEdgeParams
{
    /// <summary>Сторона площади, у которой край: "+x" | "-x" | "+y" | "-y".</summary>
    public string Side { get; init; } = "+x";
    /// <summary>Наименьшее расстояние от площади до края, м.</summary>
    public double C { get; init; }
    /// <summary>Угол скоса края к грани площади, град; 0 — параллельно.</summary>
    public double AngleDeg { get; init; }
}

/// <summary>Отверстие: центр относительно центра площади и размеры, м.</summary>
public sealed record PunchingHoleParams
{
    /// <summary>"rectangle" | "circle".</summary>
    public string Shape { get; init; } = "rectangle";
    public double Cx { get; init; }
    public double Cy { get; init; }
    public double Bx { get; init; } = 0.2;
    public double By { get; init; } = 0.2;
    public double D { get; init; } = 0.2;
}

/// <summary>Равномерная поперечная арматура вдоль контура.</summary>
public sealed record PunchingReinforcementParams
{
    public bool Enabled { get; init; }
    /// <summary>Id арматуры проекта; 0 — не выбрана.</summary>
    public int MaterialId { get; init; }
    /// <summary>Диаметр стержня, м.</summary>
    public double Diameter { get; init; } = 0.008;
    /// <summary>Число стержней в ряду на длине шага вдоль контура.</summary>
    public int Count { get; init; } = 2;
    /// <summary>Шаг вдоль контура sw, м.</summary>
    public double Step { get; init; } = 0.06;
    /// <summary>От грани площади до внешнего ряда, м; null — контур за зоной не проверять.</summary>
    public double? ZoneExtent { get; init; }
    /// <summary>Ручное Rsw, кПа; null — из материала по табл. 6.15.</summary>
    public double? RswOverride { get; init; }

    /// <summary>Asw = n·πd²/4, м².</summary>
    [JsonIgnore] public double Asw => Count * Math.PI * Diameter * Diameter / 4;
}

/// <summary>Усилия продавливания.</summary>
public sealed record PunchingForceParams
{
    /// <summary>Продавливающая сила, кН (модуль).</summary>
    public double F { get; init; }
    public double Mx { get; init; }
    public double My { get; init; }
    /// <summary>Моменты — полные Mloc узла, в расчёт идёт половина (п. 8.1.46).</summary>
    public bool MomentsAreMloc { get; init; }
    /// <summary>"from_below" — колонна под плитой | "from_above" — нагрузка сверху.</summary>
    public string LoadSide { get; init; } = "from_below";
    /// <summary>Нагрузка против F в основании пирамиды, кПа.</summary>
    public double CounterPressure { get; init; }
}

/// <summary>Настройки расчёта.</summary>
public sealed record PunchingOptionParams
{
    /// <summary>"auto" | "loading" | "unloading" | "ignore".</summary>
    public string EccentricityX { get; init; } = "loading";
    public string EccentricityY { get; init; } = "loading";
    /// <summary>"minimum" | "stress_side".</summary>
    public string Wb { get; init; } = "minimum";
    /// <summary>"auto" | "off".</summary>
    public string Pylon { get; init; } = "auto";
    public double HoleInfluence { get; init; } = 6.0;
    /// <summary>Справочный вариант по Галяутдинову (2022).</summary>
    public bool PylonReduced { get; init; }
    /// <summary>μ продольного армирования, доля; null — множитель 1.</summary>
    public double? Mu { get; init; }
}

/// <summary>Контур пользователя.</summary>
public sealed record PunchingCustomContourParams
{
    public string Name { get; init; } = "";
    public List<double[]> Points { get; init; } = [];
    public List<int> InactiveEdges { get; init; } = [];
}

/// <summary>
/// Параметры задачи «Продавливание плиты (СП 63)», пп. 8.1.46–8.1.52. Хранятся в
/// <see cref="CScore.CalcTask.ParamsJson"/>; единицы — м, кН, кПа (как в CScore).
/// </summary>
public sealed record PunchingTaskParams
{
    public PunchingAreaParams Area { get; init; } = new();
    public PunchingSlabParams Slab { get; init; } = new();
    public List<PunchingEdgeParams> Edges { get; init; } = [];
    public List<PunchingHoleParams> Holes { get; init; } = [];
    public PunchingReinforcementParams Reinforcement { get; init; } = new();
    public PunchingForceParams Forces { get; init; } = new();
    public PunchingOptionParams Options { get; init; } = new();
    public List<PunchingCustomContourParams> CustomContours { get; init; } = [];

    /// <summary>Наибольший допустимый угол скоса края, град.</summary>
    public const double MaxEdgeAngleDeg = 60;

    static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    /// <summary>JSON параметров.</summary>
    public string ToJson() => JsonSerializer.Serialize(this, JsonOpts);

    /// <summary>Разбор JSON; пустой или «{}» — значения по умолчанию.</summary>
    public static PunchingTaskParams Parse(string? json)
    {
        if (string.IsNullOrWhiteSpace(json) || json.Trim() == "{}") return new PunchingTaskParams();
        var p = JsonSerializer.Deserialize<PunchingTaskParams>(json, JsonOpts) ?? new PunchingTaskParams();
        return p with
        {
            Area = p.Area ?? new(), Slab = p.Slab ?? new(), Edges = p.Edges ?? [], Holes = p.Holes ?? [],
            Reinforcement = p.Reinforcement ?? new(), Forces = p.Forces ?? new(), Options = p.Options ?? new(),
            CustomContours = p.CustomContours ?? []
        };
    }

    /// <summary>h0x = h − ax, м.</summary>
    [JsonIgnore] public double H0x => Slab.H - Slab.Ax;
    /// <summary>h0y = h − ay, м.</summary>
    [JsonIgnore] public double H0y => Slab.H - Slab.Ay;
    /// <summary>Приведённая рабочая высота h0 = 0,5(h0x + h0y), м (п. 8.1.46).</summary>
    [JsonIgnore] public double H0 => 0.5 * (H0x + H0y);

    /// <summary>Грузовая площадь ядра.</summary>
    public PunchingLoadArea BuildArea() => Area.Shape switch
    {
        "circle" => new PunchingLoadArea.Circle(Area.D),
        "ring" => new PunchingLoadArea.Ring(Area.D, Area.T),
        "polygon" => new PunchingLoadArea.Polygon(Area.Points.Select(p => new PlanPoint(p[0], p[1])).ToList()),
        _ => new PunchingLoadArea.Rectangle(Area.Bx, Area.By)
    };

    /// <summary>Единичный вектор стороны площади, на которой лежит край ("+x" → (1, 0)).</summary>
    public static PlanPoint SideVector(string side) => side switch
    {
        "-x" => new PlanPoint(-1, 0),
        "+y" => new PlanPoint(0, 1),
        "-y" => new PlanPoint(0, -1),
        _ => new PlanPoint(1, 0)
    };

    /// <summary>
    /// Края ядра. Внутренняя нормаль — от края к площади, повёрнутая на угол скоса; край проходит так,
    /// что наименьшее расстояние от площади до него равно c.
    /// </summary>
    public IReadOnlyList<PunchingSlabEdge> BuildEdges()
    {
        var area = BuildArea();
        var list = new List<PunchingSlabEdge>();
        foreach (var e in Edges)
        {
            var s = SideVector(e.Side);
            double a = e.AngleDeg * Math.PI / 180;
            var n = new PlanPoint(-(s.X * Math.Cos(a) - s.Y * Math.Sin(a)), -(s.X * Math.Sin(a) + s.Y * Math.Cos(a)));
            double hmin = Support(area, n);
            list.Add(new PunchingSlabEdge((hmin - e.C) * n, n));
        }
        return list;
    }

    /// <summary>min по площади (p·n) — опорная функция в направлении n.</summary>
    static double Support(PunchingLoadArea area, PlanPoint n) => area switch
    {
        PunchingLoadArea.Circle c => -c.D / 2,
        PunchingLoadArea.Ring r => -r.D / 2,
        _ => PunchingContourBuilder.AreaPolygon(area).Min(p => p.Dot(n))
    };

    /// <summary>Отверстия ядра; круг — 32-угольник.</summary>
    public IReadOnlyList<PunchingHole> BuildHoles() => Holes.Select(HolePolygon).Select(p => new PunchingHole(p)).ToList();

    static List<PlanPoint> HolePolygon(PunchingHoleParams h)
    {
        if (h.Shape == "circle")
            return Enumerable.Range(0, 32)
                .Select(i => new PlanPoint(h.Cx + h.D / 2 * Math.Cos(2 * Math.PI * i / 32), h.Cy + h.D / 2 * Math.Sin(2 * Math.PI * i / 32)))
                .ToList();
        return
        [
            new(h.Cx - h.Bx / 2, h.Cy - h.By / 2), new(h.Cx + h.Bx / 2, h.Cy - h.By / 2),
            new(h.Cx + h.Bx / 2, h.Cy + h.By / 2), new(h.Cx - h.Bx / 2, h.Cy + h.By / 2)
        ];
    }

    /// <summary>
    /// Проверка ввода без материалов. Возвращает коды ошибок (ключ локализации — «PunchingErr_» + код);
    /// пустой список — данные годятся для ядра.
    /// </summary>
    public List<string> Validate()
    {
        var errors = new List<string>();
        if (!(Slab.H > 0)) errors.Add("h_nonpositive");
        else if (!(H0x > 0) || !(H0y > 0) || Slab.Ax < 0 || Slab.Ay < 0) errors.Add("h0_nonpositive");
        if (Slab.RbtOverride is double rbt && !(rbt > 0)) errors.Add("rbt_nonpositive");

        bool areaOk = Area.Shape switch
        {
            "circle" => Area.D > 0,
            "ring" => Area.D > 0 && Area.T > 0 && 2 * Area.T <= Area.D,
            "polygon" => Area.Points.Count >= 3 && Area.Points.All(p => p.Length == 2 && p.All(double.IsFinite)) && PolygonArea() > 1e-12,
            _ => Area.Bx > 0 && Area.By > 0
        };
        if (!areaOk) errors.Add(Area.Shape == "polygon" ? "polygon_points" : "area_size");

        if (Edges.Count > 2) errors.Add("edges_too_many");
        if (Edges.Select(e => e.Side).Distinct().Count() < Edges.Count) errors.Add("edges_same_side");
        if (Edges.Count == 2 && SideVector(Edges[0].Side).Dot(SideVector(Edges[1].Side)) < -0.5) errors.Add("edges_opposite");
        if (Edges.Any(e => e.C < 0 || !double.IsFinite(e.C))) errors.Add("edge_c_negative");
        if (Edges.Any(e => Math.Abs(e.AngleDeg) > MaxEdgeAngleDeg)) errors.Add("edge_angle");
        if (Edges.Any(e => e.Side is not ("+x" or "-x" or "+y" or "-y"))) errors.Add("edge_side");

        if (Holes.Any(h => h.Shape == "circle" ? !(h.D > 0) : !(h.Bx > 0 && h.By > 0))) errors.Add("hole_size");
        else if (areaOk && Holes.Any(h => ConvexOverlap(PunchingContourBuilder.AreaPolygon(BuildArea()), HolePolygon(h))))
            errors.Add("hole_overlaps_area");

        if (Reinforcement.Enabled)
        {
            if (!(Reinforcement.Diameter > 0) || Reinforcement.Count < 1 || !(Reinforcement.Step > 0)) errors.Add("sw_values");
            if (Reinforcement.ZoneExtent is double z && !(z > 0)) errors.Add("sw_zone");
            if (Reinforcement.RswOverride is double rsw && !(rsw > 0)) errors.Add("rsw_nonpositive");
        }

        if (Forces.F < 0 || !double.IsFinite(Forces.F)) errors.Add("force_negative");
        if (!double.IsFinite(Forces.Mx) || !double.IsFinite(Forces.My) || !double.IsFinite(Forces.CounterPressure)) errors.Add("force_invalid");
        if (!(Options.HoleInfluence >= 0)) errors.Add("hole_influence");
        if (Options.Mu is double mu && !(mu >= 0 && mu < 0.2)) errors.Add("mu_range");
        return errors;
    }

    double PolygonArea()
    {
        double a = 0;
        for (int i = 0; i < Area.Points.Count; i++)
        {
            var p = Area.Points[i]; var q = Area.Points[(i + 1) % Area.Points.Count];
            a += p[0] * q[1] - q[0] * p[1];
        }
        return Math.Abs(a) / 2;
    }

    /// <summary>Пересекаются ли выпуклые многоугольники (теорема о разделяющей оси, касание — не пересечение).</summary>
    static bool ConvexOverlap(IReadOnlyList<PlanPoint> a, IReadOnlyList<PlanPoint> b)
    {
        foreach (var poly in new[] { a, b })
            for (int i = 0; i < poly.Count; i++)
            {
                var e = poly[(i + 1) % poly.Count] - poly[i];
                var axis = new PlanPoint(-e.Y, e.X);
                double amin = a.Min(p => p.Dot(axis)), amax = a.Max(p => p.Dot(axis));
                double bmin = b.Min(p => p.Dot(axis)), bmax = b.Max(p => p.Dot(axis));
                double tol = 1e-12 * Math.Max(1, axis.Length);
                if (amax <= bmin + tol || bmax <= amin + tol) return false;
            }
        return true;
    }

    /// <summary>Вход ядра при известных Rbt и Rsw (кПа). Вызывать после <see cref="Validate"/>.</summary>
    public PunchingInput ToInput(double rbt, double rsw)
    {
        var r = Reinforcement;
        var reinforcement = r.Enabled ? new PunchingShearReinforcement(rsw, r.Asw, r.Step, r.ZoneExtent) : null;
        var custom = CustomContours
            .Where(c => c.Points.Count >= 3)
            .Select(c => new PunchingCustomContour(c.Points.Select(p => new PlanPoint(p[0], p[1])).ToList(),
                c.InactiveEdges, string.IsNullOrWhiteSpace(c.Name) ? null : c.Name))
            .ToList();
        return new PunchingInput(
            BuildArea(), H0, rbt,
            new PunchingForces(Forces.F, Forces.Mx, Forces.My, Forces.MomentsAreMloc,
                Forces.LoadSide == "from_above" ? PunchingLoadSide.FromAbove : PunchingLoadSide.FromBelow,
                Forces.CounterPressure),
            reinforcement, BuildEdges(), BuildHoles(), custom.Count > 0 ? custom : null,
            new PunchingOptions(
                Eccentricity(Options.EccentricityX), Eccentricity(Options.EccentricityY),
                Options.Pylon == "off" ? PylonMode.Off : PylonMode.Auto,
                Options.HoleInfluence,
                Options.Wb == "stress_side" ? SectionModulusMode.StressSide : SectionModulusMode.Minimum,
                Options.PylonReduced, false, Options.Mu));
    }

    static EccentricityMode Eccentricity(string mode) => mode switch
    {
        "auto" => EccentricityMode.Auto,
        "unloading" => EccentricityMode.Unloading,
        "ignore" => EccentricityMode.Ignore,
        _ => EccentricityMode.Loading
    };
}
