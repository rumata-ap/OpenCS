using System.ComponentModel;
using System.Globalization;
using System.Windows.Media.Media3D;
using OpenCS.Utilites;

namespace OpenCS.ViewModels;

public enum FemTransformKind { Move, Copy }

public enum FemTransformStage { BasePoint, TargetPoint, ArrayCount, ArrayTarget }

/// <summary>Разобранный ввод командной строки.</summary>
public abstract record FemPointInput
{
    /// <summary>«x;y;z» (или «x;y» при z = 0) — абсолютные координаты.</summary>
    public sealed record Absolute(Point3D Point) : FemPointInput;
    /// <summary>«@dx;dy;dz» (или «@dx;dy») — смещение от базовой точки.</summary>
    public sealed record Relative(Vector3D Offset) : FemPointInput;
    /// <summary>Одно число — расстояние вдоль направления на курсор (прямой ввод расстояния).</summary>
    public sealed record Distance(double Value) : FemPointInput;
    /// <summary>Буква «М» — опция «Массив».</summary>
    public sealed record ArrayOption : FemPointInput;
    public sealed record Empty : FemPointInput;
    public sealed record Invalid : FemPointInput;

    /// <summary>Разбирает ввод: разделитель координат «;», десятичный разделитель — точка или запятая.</summary>
    public static FemPointInput Parse(string? text)
    {
        var s = (text ?? "").Trim();
        if (s.Length == 0) return new Empty();
        if (s is "М" or "м" or "M" or "m") return new ArrayOption();
        bool relative = s.StartsWith('@');
        if (relative) s = s[1..].Trim();
        var parts = s.Split(';', StringSplitOptions.TrimEntries);
        var values = new double[parts.Length];
        for (int i = 0; i < parts.Length; i++)
            if (!Pars.ParseAny(parts[i], out values[i])) return new Invalid();
        return (relative, values.Length) switch
        {
            (true, 2) => new Relative(new Vector3D(values[0], values[1], 0)),
            (true, 3) => new Relative(new Vector3D(values[0], values[1], values[2])),
            (false, 1) => new Distance(values[0]),
            (false, 2) => new Absolute(new Point3D(values[0], values[1], 0)),
            (false, 3) => new Absolute(new Point3D(values[0], values[1], values[2])),
            _ => new Invalid(),
        };
    }
}

/// <summary>
/// Команды «Переместить» и «Копировать» в духе AutoCAD: базовая точка → вторая точка (щелчок с привязкой к узлу,
/// абсолютные «x;y;z», относительные «@dx;dy;dz» или расстояние вдоль курсора), Орто (F8) — смещение вдоль одной
/// глобальной оси. Копирование повторяется до Enter/Esc; «М» — массив из n копий с одним шагом.
/// Вид передаёт точки и ввод, команда отвечает событиями <see cref="MoveRequested"/> / <see cref="CopyRequested"/>.
/// </summary>
public sealed class FemTransformCommand(FemTransformKind kind, IReadOnlyCollection<string> nodeTags,
    IReadOnlyCollection<string> memberTags) : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;
    /// <summary>Перемещение на вектор; после него команда завершается.</summary>
    public event Action<Vector3D>? MoveRequested;
    /// <summary>Копии: шаг и число копий.</summary>
    public event Action<Vector3D, int>? CopyRequested;
    public event Action? Finished;

    public FemTransformKind Kind { get; } = kind;
    public IReadOnlyCollection<string> NodeTags { get; } = nodeTags;
    public IReadOnlyCollection<string> MemberTags { get; } = memberTags;

    FemTransformStage _stage = FemTransformStage.BasePoint;
    public FemTransformStage Stage { get => _stage; private set { _stage = value; Notify(nameof(Stage)); Notify(nameof(Prompt)); } }

    public Point3D? BasePoint { get; private set; }
    public bool IsFinished { get; private set; }

    bool _ortho;
    /// <summary>Орто: смещение мышью — только вдоль глобальной оси с наибольшей составляющей.</summary>
    public bool Ortho { get => _ortho; set { _ortho = value; Notify(nameof(Ortho)); } }

    int _arrayCount;

    string _message = "";
    /// <summary>Сообщение о неверном вводе (пусто — нет).</summary>
    public string Message { get => _message; private set { _message = value; Notify(nameof(Message)); } }

    public string Title => Loc.S(Kind == FemTransformKind.Move ? "FemFragmentMoveTitle" : "FemFragmentCopyTitle");

    public string Prompt => Stage switch
    {
        FemTransformStage.BasePoint => Loc.S("FemTransformPromptBase"),
        FemTransformStage.TargetPoint => Loc.S(Kind == FemTransformKind.Move ? "FemTransformPromptTargetMove" : "FemTransformPromptTargetCopy"),
        FemTransformStage.ArrayCount => Loc.S("FemTransformPromptArrayCount"),
        _ => string.Format(Loc.S("FemTransformPromptArrayTarget"), _arrayCount),
    };

    /// <summary>Смещение для точки курсора с учётом Орто; null — базовой точки ещё нет.</summary>
    public Vector3D? PreviewOffset(Point3D cursor) =>
        BasePoint is { } b ? (Ortho ? OrthoOf(cursor - b) : cursor - b) : null;

    /// <summary>Щелчок в 3D (точка уже с привязкой к узлу).</summary>
    public void PickPoint(Point3D point)
    {
        if (IsFinished) return;
        Message = "";
        switch (Stage)
        {
            case FemTransformStage.BasePoint:
                BasePoint = point;
                Notify(nameof(BasePoint));
                Stage = FemTransformStage.TargetPoint;
                break;
            case FemTransformStage.TargetPoint:
            case FemTransformStage.ArrayTarget:
                Apply(PreviewOffset(point)!.Value);
                break;
        }
    }

    /// <summary>Ввод из командной строки. <paramref name="cursor"/> — точка курсора для прямого ввода расстояния.</summary>
    public void Submit(string? text, Point3D? cursor)
    {
        if (IsFinished) return;
        Message = "";
        var input = FemPointInput.Parse(text);
        switch (Stage, input)
        {
            case (_, FemPointInput.Empty):
                Finish();
                break;
            case (FemTransformStage.ArrayCount, FemPointInput.Distance { Value: var n }):
                if (n >= 1 && n == Math.Floor(n) && n <= 1000)
                {
                    _arrayCount = (int)n;
                    Stage = FemTransformStage.ArrayTarget;
                }
                else Message = Loc.S("FemTransformArrayCountInvalid");
                break;
            case (FemTransformStage.ArrayCount, _):
                Message = Loc.S("FemTransformArrayCountInvalid");
                break;
            case (FemTransformStage.BasePoint, FemPointInput.Absolute { Point: var p }):
                PickPoint(p);
                break;
            case (FemTransformStage.BasePoint, FemPointInput.Relative { Offset: var v }):
                // Как «Перемещение» AutoCAD: вектор без базовой точки — одно действие, и команда завершается.
                BasePoint = new Point3D();
                Apply(v);
                if (Message.Length == 0) Finish();
                break;
            case (FemTransformStage.TargetPoint or FemTransformStage.ArrayTarget, FemPointInput.Absolute { Point: var p }):
                Apply(p - BasePoint!.Value);
                break;
            case (FemTransformStage.TargetPoint or FemTransformStage.ArrayTarget, FemPointInput.Relative { Offset: var v }):
                Apply(v);
                break;
            case (FemTransformStage.TargetPoint or FemTransformStage.ArrayTarget, FemPointInput.Distance { Value: var d }):
                if (cursor is { } c && PreviewOffset(c) is { Length: > 1e-12 } dir)
                {
                    dir.Normalize();
                    Apply(dir * d);
                }
                else Message = Loc.S("FemTransformDistanceNeedsDirection");
                break;
            case (FemTransformStage.TargetPoint, FemPointInput.ArrayOption) when Kind == FemTransformKind.Copy:
                Stage = FemTransformStage.ArrayCount;
                break;
            default:
                Message = string.Format(Loc.S("FemTransformInvalidInput"), text);
                break;
        }
    }

    /// <summary>Enter без ввода, Esc или ПКМ.</summary>
    public void Finish()
    {
        if (IsFinished) return;
        IsFinished = true;
        Notify(nameof(IsFinished));
        Finished?.Invoke();
    }

    void Apply(Vector3D offset)
    {
        if (offset.Length < 1e-12) { Message = Loc.S("FemTransformZeroOffset"); return; }
        if (Kind == FemTransformKind.Move)
        {
            MoveRequested?.Invoke(offset);
            Finish();
            return;
        }
        int copies = Stage == FemTransformStage.ArrayTarget ? _arrayCount : 1;
        CopyRequested?.Invoke(offset, copies);
        if (Stage == FemTransformStage.ArrayTarget) Stage = FemTransformStage.TargetPoint;
    }

    /// <summary>Составляющая вектора вдоль глобальной оси с наибольшим модулем.</summary>
    public static Vector3D OrthoOf(Vector3D v)
    {
        double ax = Math.Abs(v.X), ay = Math.Abs(v.Y), az = Math.Abs(v.Z);
        return ax >= ay && ax >= az ? new Vector3D(v.X, 0, 0)
            : ay >= az ? new Vector3D(0, v.Y, 0)
            : new Vector3D(0, 0, v.Z);
    }

    /// <summary>Смещение для показа в строке: «dX; dY; dZ, L».</summary>
    public static string Describe(Vector3D v) => string.Format(CultureInfo.CurrentCulture,
        "Δ {0:0.###}; {1:0.###}; {2:0.###}  L = {3:0.###}", v.X, v.Y, v.Z, v.Length);

    void Notify(string name) => PropertyChanged?.Invoke(this, new(name));
}
