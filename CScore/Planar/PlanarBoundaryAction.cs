using CScore.Fem;

namespace CScore.Planar;

/// <summary>Источник нормализованного действия на cut interface.</summary>
public enum PlanarBoundaryActionSourceMode
{
    Parent,
    Template,
    Combined
}

/// <summary>Физический тип действия на cut interface.</summary>
public enum PlanarBoundaryActionKind
{
    Force,
    Kinematic
}

/// <summary>Режим одной степени свободы на граничном интерфейсе.</summary>
public enum PlanarBoundaryDofMode
{
    None,
    Force,
    Kinematic,
    PreserveSupport,
    Free,
    Incomplete
}

/// <summary>Единицы первого boundary contract: СИ, Н, м.</summary>
public enum PlanarBoundaryUnitSystem
{
    Si
}

/// <summary>Правило интерполяции samples вдоль граничной линии.</summary>
public enum PlanarBoundaryInterpolationKind
{
    Uniform,
    Linear
}

/// <summary>Режимы шести степеней свободы cut interface.</summary>
public sealed record PlanarBoundaryModeByDof(
    PlanarBoundaryDofMode UX,
    PlanarBoundaryDofMode UY,
    PlanarBoundaryDofMode UZ,
    PlanarBoundaryDofMode RX,
    PlanarBoundaryDofMode RY,
    PlanarBoundaryDofMode RZ)
{
    /// <summary>Режим без назначенных степеней свободы.</summary>
    public static PlanarBoundaryModeByDof None { get; } = new(
        PlanarBoundaryDofMode.None,
        PlanarBoundaryDofMode.None,
        PlanarBoundaryDofMode.None,
        PlanarBoundaryDofMode.None,
        PlanarBoundaryDofMode.None,
        PlanarBoundaryDofMode.None);

    /// <summary>Назначает один режим всем шести степеням свободы.</summary>
    public static PlanarBoundaryModeByDof All(PlanarBoundaryDofMode mode) => new(mode, mode, mode, mode, mode, mode);

    /// <summary>Возвращает копию с назначенным режимом выбранных DOF.</summary>
    public PlanarBoundaryModeByDof With(PlanarDofMask mask, PlanarBoundaryDofMode mode)
    {
        PlanarBoundaryDofMode[] values = [UX, UY, UZ, RX, RY, RZ];
        for (int bit = 0; bit < values.Length; bit++)
        {
            var dof = (PlanarDofMask)(1 << bit);
            if ((mask & dof) == 0) continue;
            if (values[bit] is not (PlanarBoundaryDofMode.None or PlanarBoundaryDofMode.Incomplete) &&
                values[bit] != mode)
                throw new ArgumentException($"DOF {dof} уже имеет режим {values[bit]}.", nameof(mask));
            values[bit] = mode;
        }

        return new(values[0], values[1], values[2], values[3], values[4], values[5]);
    }

    /// <summary>Возвращает режим ровно одной выбранной степени свободы.</summary>
    public PlanarBoundaryDofMode Get(PlanarDofMask dof)
    {
        return dof switch
        {
            PlanarDofMask.UX => UX,
            PlanarDofMask.UY => UY,
            PlanarDofMask.UZ => UZ,
            PlanarDofMask.RX => RX,
            PlanarDofMask.RY => RY,
            PlanarDofMask.RZ => RZ,
            _ => throw new ArgumentException("Нужно указать ровно один DOF.", nameof(dof))
        };
    }

    /// <summary>Возвращает маску DOF, для которых задан любой явный режим.</summary>
    public PlanarDofMask CoveredDofs
    {
        get
        {
            PlanarDofMask result = PlanarDofMask.None;
            PlanarBoundaryDofMode[] values = [UX, UY, UZ, RX, RY, RZ];
            for (int bit = 0; bit < values.Length; bit++)
                if (values[bit] != PlanarBoundaryDofMode.None)
                    result |= (PlanarDofMask)(1 << bit);
            return result;
        }
    }

    /// <summary>Проверяет допустимость всех назначенных режимов.</summary>
    public IReadOnlyList<FemValidationDiagnostic> Validate()
    {
        var diagnostics = new List<FemValidationDiagnostic>();
        PlanarBoundaryDofMode[] values = [UX, UY, UZ, RX, RY, RZ];
        for (int bit = 0; bit < values.Length; bit++)
            if (!Enum.IsDefined(values[bit]))
                diagnostics.Add(new("planar_boundary_dof_mode_invalid", $"DOF {(PlanarDofMask)(1 << bit)} имеет неизвестный режим."));
        return diagnostics;
    }
}

/// <summary>Ссылка на исходный вклад boundary action.</summary>
public sealed record PlanarBoundarySourceReference(
    string SourceKind,
    string SourceId,
    int? MemberId = null,
    int? ElementId = null,
    int? NodeId = null,
    string? ResultId = null,
    string? FieldId = null);

/// <summary>Сэмпл линейной силы и момента вдоль интерфейса.</summary>
public sealed record PlanarBoundaryForceSample(
    double S,
    PlanarVector3 ForcePerLength,
    PlanarVector3 MomentPerLength);

/// <summary>Сэмпл перемещения и поворота вдоль интерфейса.</summary>
public sealed record PlanarBoundaryKinematicSample(
    double S,
    PlanarVector3 Displacement,
    PlanarVector3 Rotation);

/// <summary>Силовое действие на одном cut interface.</summary>
public sealed class PlanarBoundaryForceAction
{
    public string InterfaceId { get; init; } = "";
    public PlanarDofMask DofMask { get; init; }
    public Frame3D Frame { get; init; } = Frame3D.Identity;
    public PlanarBoundaryUnitSystem UnitSystem { get; init; } = PlanarBoundaryUnitSystem.Si;
    public PlanarBoundaryInterpolationKind Interpolation { get; init; } = PlanarBoundaryInterpolationKind.Linear;
    public PlanarVector3 ReferencePoint { get; init; } = PlanarVector3.Zero;
    public IReadOnlyList<PlanarBoundaryForceSample> Samples { get; init; } = [];
    public IReadOnlyList<PlanarBoundarySourceReference> SourceReferences { get; init; } = [];

    /// <summary>Проверяет форму и числовую корректность force action.</summary>
    public IReadOnlyList<FemValidationDiagnostic> Validate() => PlanarBoundaryActionValidation.Validate(this);
}

/// <summary>Кинематическое действие на одном cut interface.</summary>
public sealed class PlanarBoundaryKinematicAction
{
    public string InterfaceId { get; init; } = "";
    public PlanarDofMask DofMask { get; init; }
    public Frame3D Frame { get; init; } = Frame3D.Identity;
    public PlanarBoundaryUnitSystem UnitSystem { get; init; } = PlanarBoundaryUnitSystem.Si;
    public PlanarBoundaryInterpolationKind Interpolation { get; init; } = PlanarBoundaryInterpolationKind.Linear;
    public IReadOnlyList<PlanarBoundaryKinematicSample> Samples { get; init; } = [];
    public IReadOnlyList<PlanarBoundarySourceReference> SourceReferences { get; init; } = [];

    /// <summary>Проверяет форму и числовую корректность kinematic action.</summary>
    public IReadOnlyList<FemValidationDiagnostic> Validate() => PlanarBoundaryActionValidation.Validate(this);
}

/// <summary>Нормализованный набор действий одного source mode.</summary>
public sealed class PlanarBoundaryActionSet
{
    public PlanarBoundaryActionSourceMode SourceMode { get; init; }
    public IReadOnlyList<PlanarBoundaryForceAction> ForceActions { get; init; } = [];
    public IReadOnlyList<PlanarBoundaryKinematicAction> KinematicActions { get; init; } = [];
    public IReadOnlyList<PlanarBoundarySourceReference> SourceReferences { get; init; } = [];
    public IReadOnlyList<FemValidationDiagnostic> Diagnostics { get; init; } = [];
    public PlanarDofMask CoveredDofs => ForceActions
        .Select(action => action.DofMask)
        .Concat(KinematicActions.Select(action => action.DofMask))
        .Aggregate(PlanarDofMask.None, (mask, item) => mask | item);
    public bool IsCalculable => !Diagnostics.Any(diagnostic => diagnostic.IsError);
}
