using CScore.Sp16;

namespace CScore.ParametricSteel;

/// <summary>Результат материализации параметрического стального сечения.</summary>
public sealed record ParametricSteelGenerationResult(
    CrossSection Section, SteelProfile? Profile, IReadOnlyList<string> Diagnostics)
{
    /// <summary>Области, созданные генератором и предназначенные для junction.</summary>
    public IReadOnlyList<MaterialArea> GeneratedAreas { get; init; } = [];
}

/// <summary>Строит стальное сечение (одна область, контур и отверстия) и дескриптор профиля СП 16.</summary>
public static class ParametricSteelSectionGenerator
{
    /// <summary>Версия алгоритма построения контура (входит в отпечаток).</summary>
    public const int GeneratorVersion = 1;

    /// <summary>Материализует параметры; ошибочный ввод не создаёт частичную геометрию.</summary>
    public static ParametricSteelGenerationResult Generate(ParametricSteelSectionDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(definition);
        var errors = Validate(definition);
        var shape = errors.Count == 0 ? SteelContourBuilder.Build(definition) : null;
        if (errors.Count == 0 && shape == null)
            errors.Add("Радиусы закруглений не помещаются в размеры профиля.");
        if (errors.Count != 0)
            return new(new CrossSection { Tag = definition.Tag }, null, errors);

        var (outer, holes) = Orient(definition, shape!.Outer, shape.Holes);
        var area = new MaterialArea
        {
            Tag = "Сталь", Category = AreaCategory.Region, MaterialId = definition.MaterialId
        };
        area.Hull = ClosedContour(outer, "контур", ContourType.Hull);
        foreach (var hole in holes)
            area.Contours.Add(ClosedContour(hole, "отверстие", ContourType.Hole));
        area.SetWKT();
        var section = new CrossSection { Tag = definition.Tag, Areas = [area] };
        return new(section, ToSteelProfile(definition), []) { GeneratedAreas = [area] };
    }

    /// <summary>Дескриптор профиля СП 16 для определения (радиусы — в трактовке <see cref="Sp16Section"/>).</summary>
    public static SteelProfile ToSteelProfile(ParametricSteelSectionDefinition d)
    {
        bool welded = d.Fabrication == SteelFabrication.Welded;
        var p = new SteelProfile
        {
            Kind = d.Kind, Fabrication = d.Fabrication,
            Rotated90 = d.Rotated90 && ParametricSteelSectionDefinition.CanRotate(d.Kind),
            Flipped = d.Flipped && ParametricSteelSectionDefinition.CanFlip(d.Kind),
            ItReference = d.Kind is SteelProfileKind.IBeam or SteelProfileKind.Channel && d.Catalog is { It: > 0 } c ? c.It : 0,
        };
        return d.Kind switch
        {
            SteelProfileKind.IBeam => p with
            {
                H = d.H, Bf1 = d.Bf1, Tf1 = d.Tf1, Tw = d.Tw, R = welded ? 0 : d.R1,
                Bf2 = welded ? d.BfBottom : 0, Tf2 = welded ? d.TfBottom : 0,
            },
            // Гнутый швеллер: hef = hw − 2R, bef = b − tw − R — R внутренний (Sp16Section.Hef, OverhangOf).
            SteelProfileKind.Channel => p with
            {
                H = d.H, Bf1 = d.Bf1, Tw = d.Tw, R = welded ? 0 : d.R1,
                Tf1 = d.Fabrication == SteelFabrication.Bent ? d.Tw : d.Tf1,
            },
            SteelProfileKind.Tee => p with { H = d.H, Bf1 = d.Bf1, Tf1 = d.Tf1, Tw = d.Tw },
            SteelProfileKind.Angle => p with { H = d.H, Bf1 = d.Bf1, Tf1 = d.Tw, Tw = d.Tw, R = d.R1 },
            // Гнутый короб: hef = H − 2R, bef,1 = b − 2R — R наружный.
            SteelProfileKind.Box => d.Fabrication == SteelFabrication.Bent
                ? p with { H = d.H, Bf1 = d.Bf1, Tf1 = d.Tw, Tw = d.Tw, R = d.R1 + d.Tw }
                : p with { H = d.H, Bf1 = d.Bf1, Tf1 = d.Tf1, Tw = d.Tw },
            SteelProfileKind.Pipe => p with { H = d.H, Tw = d.Tw },
            SteelProfileKind.Rect => p with { H = d.H, Bf1 = d.Bf1 },
            SteelProfileKind.Round => p with { H = d.H },
            _ => p,
        };
    }

    static List<string> Validate(ParametricSteelSectionDefinition d)
    {
        var errors = new List<string>();
        if (!ParametricSteelSectionDefinition.AllowedFabrications(d.Kind).Contains(d.Fabrication))
        {
            errors.Add("Способ изготовления недопустим для выбранного вида сечения.");
            return errors;
        }
        if (d.Rotated90 && !ParametricSteelSectionDefinition.CanRotate(d.Kind))
            errors.Add("Поворот на 90° недоступен для выбранного вида сечения.");
        if (d.Flipped && !ParametricSteelSectionDefinition.CanFlip(d.Kind))
            errors.Add("Зеркальное положение недоступно для выбранного вида сечения.");

        static bool Pos(double v) => double.IsFinite(v) && v > 0;
        static bool NonNeg(double v) => double.IsFinite(v) && v >= 0;
        bool rolled = d.Fabrication == SteelFabrication.Rolled, bent = d.Fabrication == SteelFabrication.Bent;
        switch (d.Kind)
        {
            case SteelProfileKind.IBeam:
                if (!Pos(d.H) || !Pos(d.Bf1) || !Pos(d.Tf1) || !Pos(d.Tw) || !NonNeg(d.Bf2) || !NonNeg(d.Tf2))
                { errors.Add("Размеры двутавра должны быть положительными."); break; }
                if (d.Tw >= Math.Min(d.Bf1, d.BfBottom)) errors.Add("Толщина стенки должна быть меньше ширины поясов.");
                if (d.Tf1 + d.TfBottom >= d.H) errors.Add("Сумма толщин поясов должна быть меньше высоты.");
                if (rolled) ValidateRolledFlanges(d, (d.Bf1 - d.Tw) / 2, errors);
                break;
            case SteelProfileKind.Channel:
                if (!Pos(d.H) || !Pos(d.Bf1) || !Pos(d.Tw) || (!bent && !Pos(d.Tf1)))
                { errors.Add("Размеры швеллера должны быть положительными."); break; }
                if (d.Tw >= d.Bf1) errors.Add("Толщина стенки должна быть меньше ширины полки.");
                if (2 * (bent ? d.Tw : d.Tf1) >= d.H) errors.Add("Удвоенная толщина полки должна быть меньше высоты.");
                if (rolled) ValidateRolledFlanges(d, d.Bf1 - d.Tw, errors);
                if (!NonNeg(d.R1)) errors.Add("Радиус гиба не может быть отрицательным.");
                break;
            case SteelProfileKind.Tee:
                if (!Pos(d.H) || !Pos(d.Bf1) || !Pos(d.Tf1) || !Pos(d.Tw))
                { errors.Add("Размеры тавра должны быть положительными."); break; }
                if (d.Tw >= d.Bf1) errors.Add("Толщина стенки должна быть меньше ширины полки.");
                if (d.Tf1 >= d.H) errors.Add("Толщина полки должна быть меньше высоты.");
                break;
            case SteelProfileKind.Angle:
                if (!Pos(d.H) || !Pos(d.Bf1) || !Pos(d.Tw))
                { errors.Add("Размеры уголка должны быть положительными."); break; }
                if (d.Tw >= Math.Min(d.H, d.Bf1)) errors.Add("Толщина уголка должна быть меньше ширины полок.");
                if (!NonNeg(d.R1) || !NonNeg(d.R2)) errors.Add("Радиусы закруглений не могут быть отрицательными.");
                break;
            case SteelProfileKind.Box:
                if (!Pos(d.H) || !Pos(d.Bf1) || !Pos(d.Tw) || (!bent && !Pos(d.Tf1)))
                { errors.Add("Размеры короба должны быть положительными."); break; }
                if (2 * d.Tw >= d.Bf1) errors.Add("Удвоенная толщина стенки должна быть меньше ширины короба.");
                if (2 * (bent ? d.Tw : d.Tf1) >= d.H) errors.Add("Удвоенная толщина пояса должна быть меньше высоты короба.");
                if (bent && (!NonNeg(d.R1) || d.R1 + d.Tw > Math.Min(d.Bf1, d.H) / 2))
                    errors.Add("Наружный радиус гиба R + t должен быть не больше половины меньшего размера короба.");
                break;
            case SteelProfileKind.Pipe:
                if (!Pos(d.H) || !Pos(d.Tw)) { errors.Add("Диаметр и толщина стенки трубы должны быть положительными."); break; }
                if (d.Tw >= d.H / 2) errors.Add("Толщина стенки трубы должна быть меньше радиуса.");
                break;
            case SteelProfileKind.Rect:
                if (!Pos(d.H) || !Pos(d.Bf1)) errors.Add("Размеры листа должны быть положительными.");
                break;
            case SteelProfileKind.Round:
                if (!Pos(d.H)) errors.Add("Диаметр круга должен быть положительным.");
                break;
            default:
                errors.Add("Вид сечения не поддерживается параметрическим построением.");
                break;
        }
        return errors;
    }

    static void ValidateRolledFlanges(ParametricSteelSectionDefinition d, double outstand, List<string> errors)
    {
        if (!double.IsFinite(d.R1) || !double.IsFinite(d.R2) || d.R1 < 0 || d.R2 < 0)
            errors.Add("Радиусы закруглений не могут быть отрицательными.");
        if (!double.IsFinite(d.FlangeSlope) || d.FlangeSlope < 0 || d.FlangeSlope > 0.2)
            errors.Add("Уклон внутренних граней полок должен быть в пределах 0…20 %.");
        else if (d.Tf1 - d.FlangeSlope * outstand / 2 <= 0)
            errors.Add("При заданном уклоне толщина полки у кромки получается неположительной.");
    }

    /// <summary>
    /// Перенос в центр тяжести, зеркало, затем перестановка осей (обратная <see cref="PolygonSection.SwapAxes"/>);
    /// обход: контур CCW, отверстия CW.
    /// </summary>
    static (List<(double X, double Y)> Outer, List<List<(double X, double Y)>> Holes) Orient(
        ParametricSteelSectionDefinition d, List<(double X, double Y)> outer, List<List<(double X, double Y)>> holes)
    {
        var poly = new PolygonSection(outer, holes);
        bool flipX = d.Flipped && d.Kind == SteelProfileKind.Channel;
        bool flipY = d.Flipped && d.Kind is SteelProfileKind.Tee or SteelProfileKind.Angle;
        bool swap = d.Rotated90 && ParametricSteelSectionDefinition.CanRotate(d.Kind);
        List<(double X, double Y)> Map(List<(double X, double Y)> ring, bool ccw)
        {
            var r = ring.Select(p =>
            {
                double x = p.X - poly.Xc, y = p.Y - poly.Yc;
                if (flipX) x = -x;
                if (flipY) y = -y;
                return swap ? (y, x) : (x, y);
            }).ToList();
            if (SignedArea(r) > 0 != ccw) r.Reverse();
            return r;
        }
        return (Map(outer, true), holes.Select(h => Map(h, false)).ToList());
    }

    static double SignedArea(List<(double X, double Y)> r)
    {
        double a = 0;
        for (int i = 0; i < r.Count; i++)
        {
            var (x0, y0) = r[i];
            var (x1, y1) = r[(i + 1) % r.Count];
            a += x0 * y1 - x1 * y0;
        }
        return a / 2;
    }

    static Contour ClosedContour(List<(double X, double Y)> source, string tag, ContourType type)
    {
        var points = source.ToList();
        points.Add(points[0]);
        return new Contour(points.Select(p => p.X), points.Select(p => p.Y), tag) { Type = type };
    }
}
