namespace CScore.Sp16;

/// <summary>
/// Стальной элемент для проверок: сечение в каноническом положении профиля, параметры,
/// приведённые к каноническим осям, и производные величины устойчивости (λ, λ̄, φ).
/// Если контур повёрнут на 90° (<see cref="SteelProfile.Rotated90"/>), расчётные длины,
/// типы сечений и усилия переставляются, а в отчёте оси подписываются по контуру.
/// </summary>
public sealed class Sp16Member
{
    /// <summary>Сечение (канонические оси).</summary>
    public Sp16Section S { get; }
    /// <summary>Параметры в канонических осях.</summary>
    public SteelDesignParams P { get; }
    /// <summary>Оси контура переставлены относительно канонических.</summary>
    public bool Rotated { get; }
    /// <summary>Общие примечания (распознавание, миграция параметров).</summary>
    public List<string> Notes { get; } = [];

    Sp16Member(Sp16Section s, SteelDesignParams p, bool rotated)
    {
        S = s; P = p; Rotated = rotated;
    }

    /// <summary>Создаёт элемент по контуру сечения (оси контура), материалу и параметрам (оси контура).</summary>
    public static Sp16Member Create(PolygonSection contour, SteelMaterialProps mat, SteelDesignParams p)
    {
        bool recognized = p.Profile == null;
        var s = Sp16Section.FromContour(contour, p.Profile, mat);
        bool rot = s.Profile.Rotated90;
        var pc = rot ? p with { LefX = p.LefY, LefY = p.LefX, CurveX = p.CurveY, CurveY = p.CurveX } : p;
        var m = new Sp16Member(s, pc, rot);
        if (recognized)
            m.Notes.Add(s.Kind == SteelProfileKind.Generic
                ? "Профиль по контуру не распознан — произвольное сечение: выполняются только проверки, не требующие типа сечения; задайте профиль вручную"
                : $"Профиль распознан по контуру: {s.Profile.Describe()}{(rot ? " (контур повёрнут на 90°)" : "")}");
        if (p.MigratedFromLegacy)
            m.Notes.Add("Параметры задачи в старом формате: lef = l0·μ; «γM» и «βm» не применяются (γm уже учтён в Ry, коэффициента βm в СП 16 нет); γc принят по параметрам");
        return m;
    }

    /// <summary>Тот же элемент с другими параметрами (в канонических осях) — для вспомогательных расчётов.</summary>
    internal Sp16Member WithParams(SteelDesignParams canonicalParams) => new(S, canonicalParams, Rotated);

    /// <summary>Усилия из осей контура в канонические.</summary>
    public SteelForces ToCanonical(SteelForces f) => Rotated ? f.SwapAxes() : f;

    /// <summary>Имя оси контура для канонической оси x (true) или y.</summary>
    public string Axis(bool canonicalX) => Rotated ^ canonicalX ? "x" : "y";

    // ── Гибкости и φ (7.1.3) ──

    /// <summary>Гибкость λ = lef/i относительно оси x (true) или y.</summary>
    public double Lambda(bool aboutX) => (aboutX ? P.LefX : P.LefY) / (aboutX ? S.ix : S.iy);

    /// <summary>Гибкость уголка относительно оси минимальной жёсткости (по большей из расчётных длин).</summary>
    public double LambdaMinAxis => Math.Max(P.LefX, P.LefY) / S.iMin;

    /// <summary>Условная гибкость λ̄ = λ√(Ry/E).</summary>
    public double LambdaBar(bool aboutX) => Sp16Stability.LambdaBar(Lambda(aboutX), S.Mat.Ry, S.Mat.E);

    /// <summary>Тип сечения по табл. 7 (с учётом переопределения в параметрах).</summary>
    public SectionCurve? Curve(bool aboutX) => (aboutX ? P.CurveX : P.CurveY) ?? S.CurveFor(aboutX);

    /// <summary>φ относительно оси x или y (null — тип сечения не определён).</summary>
    public double? Phi(bool aboutX) => Curve(aboutX) is { } c ? Sp16Stability.Phi(LambdaBar(aboutX), c) : null;

    /// <summary>Одиночный уголок: устойчивость проверяется относительно оси минимальной жёсткости.</summary>
    public bool IsAngle => S.Kind == SteelProfileKind.Angle;

    /// <summary>λ̄ уголка относительно оси минимальной жёсткости.</summary>
    public double LambdaBarMinAxis => Sp16Stability.LambdaBar(LambdaMinAxis, S.Mat.Ry, S.Mat.E);

    /// <summary>
    /// Наименьший φ при центральном сжатии и соответствующая условная гибкость (для табл. 9, 10, 32).
    /// null — тип сечения не определён.
    /// </summary>
    public (double Phi, double LambdaBar)? GoverningPhi()
    {
        if (IsAngle) return (Sp16Stability.Phi(LambdaBarMinAxis, Curve(true) ?? SectionCurve.b), LambdaBarMinAxis);
        var px = Phi(true); var py = Phi(false);
        if (px == null || py == null) return null;
        return px <= py ? (px.Value, LambdaBar(true)) : (py.Value, LambdaBar(false));
    }
}
