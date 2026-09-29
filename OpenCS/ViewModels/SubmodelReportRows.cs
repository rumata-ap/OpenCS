using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;
using CScore.Submodel;
using OpenCS.Utilites;

namespace OpenCS.ViewModels;

/// <summary>Строка таблицы сверки: максимальное расхождение одного вида величин в единицах экрана.</summary>
public sealed record DeviationRow(string Kind, string Value, string Unit, string Scale, string Relative, string Location, string Dof);

/// <summary>Выбор режима граничного DOF в таблице сценария: «Авто» — режим по правилам сценария.</summary>
public enum DofOverrideChoice { Auto, Fixed, Force, Kinematic }

/// <summary>Элемент списка выбора режима DOF.</summary>
public sealed record DofOverrideOption(DofOverrideChoice Value, string Label);

/// <summary>Сводка по концу цепочки: узлы и итог контрольной проверки граничного вектора.</summary>
public sealed record ScenarioEndRow(string End, string ChildNodeTag, string ParentNodeTag, string Control);

/// <summary>
/// Строка DOF граничного сценария. Всё, кроме <see cref="Choice"/>, только для чтения; <see cref="Choice"/>
/// изменяется пользователем в таблице и уходит в перестроение сценария как переопределение.
/// </summary>
public sealed class ScenarioDofRow(bool atStart, string end, int dof, string dofLabel, DofMode mode,
    DofSource source, string modeText, string sourceText, string value, DofOverrideChoice choice) : INotifyPropertyChanged
{
    public bool AtStart { get; } = atStart;
    public string End { get; } = end;
    public int DofIndex { get; } = dof;
    public string Dof { get; } = dofLabel;
    public DofMode Mode { get; } = mode;
    public DofSource Source { get; } = source;
    public string ModeText { get; } = modeText;
    public string SourceText { get; } = sourceText;
    public string Value { get; } = value;

    DofOverrideChoice _choice = choice;
    public DofOverrideChoice Choice
    {
        get => _choice;
        set { if (_choice == value) return; _choice = value; OnPropertyChanged(); }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    void OnPropertyChanged([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new(name));
}

/// <summary>
/// Чистые построители строк таблиц вкладки «Субмодель» из доменных отчётов. Внутри домена — м, рад, Н, Н·м;
/// на экране — мм, рад, кН, кН·м. Подписи — через <see cref="Loc"/>.
/// </summary>
public static class SubmodelReportRows
{
    public const string Dash = "—";
    static readonly string[] Displacements = ["Ux", "Uy", "Uz", "Rx", "Ry", "Rz"];
    static readonly string[] Forces = ["Fx", "Fy", "Fz", "Mx", "My", "Mz"];

    /// <summary>Число для таблиц: 4 значащие цифры, культура интерфейса.</summary>
    public static string Number(double value) => value.ToString("G4", CultureInfo.CurrentCulture);

    /// <summary>Шесть строк отчёта сверки в порядке: перемещения, повороты, концевые силы и моменты, реакции.</summary>
    public static IReadOnlyList<DeviationRow> FromReport(SubmodelVerificationReport report)
    {
        ArgumentNullException.ThrowIfNull(report);
        return
        [
            Row("SubmodelKindTranslation", report.Translation, 1000, "SubmodelUnitMm", Displacements),
            Row("SubmodelKindRotation", report.Rotation, 1, "SubmodelUnitRad", Displacements),
            Row("SubmodelKindEndForce", report.EndForce, 1e-3, "SubmodelUnitKn", Forces),
            Row("SubmodelKindEndMoment", report.EndMoment, 1e-3, "SubmodelUnitKnm", Forces),
            Row("SubmodelKindReactionForce", report.ReactionForce, 1e-3, "SubmodelUnitKn", Forces),
            Row("SubmodelKindReactionMoment", report.ReactionMoment, 1e-3, "SubmodelUnitKnm", Forces),
        ];
    }

    static DeviationRow Row(string kindKey, MaxDeviation d, double factor, string unitKey, string[] dofLabels)
    {
        string kind = Loc.S(kindKey);
        if (d.Dof < 0)
        {
            string none = Loc.S("SubmodelNoData");
            return new(kind, none, Loc.S(unitKey), none, none, none, none);
        }
        return new(kind, Number(d.Value * factor), Loc.S(unitKey), Number(d.Scale * factor),
            d.Scale > 0 ? Number(d.Value / d.Scale) : Dash, d.Tag, d.Dof < dofLabels.Length ? dofLabels[d.Dof] : d.Dof.ToString());
    }

    /// <summary>По 6 строк на каждый конец сценария; выбор инициализируется из сохранённых переопределений.</summary>
    public static IReadOnlyList<ScenarioDofRow> FromScenario(BoundaryScenario scenario)
    {
        ArgumentNullException.ThrowIfNull(scenario);
        var rows = new List<ScenarioDofRow>();
        foreach (var end in scenario.Ends)
        {
            string endText = EndText(end.AtStart);
            for (int dof = 0; dof < end.Dofs.Count; dof++)
            {
                var a = end.Dofs[dof];
                rows.Add(new ScenarioDofRow(end.AtStart, endText, dof, Displacements[dof], a.Mode, a.Source,
                    ModeText(a.Mode), Loc.S(a.Source == DofSource.Override ? "SubmodelDofSourceOverride" : "SubmodelDofSourceAuto"),
                    DofValue(a, dof), a.Source == DofSource.Override ? ToChoice(a.Mode) : DofOverrideChoice.Auto));
            }
        }
        return rows;
    }

    /// <summary>Сводка по концам: узлы и контрольная проверка граничного вектора.</summary>
    public static IReadOnlyList<ScenarioEndRow> Ends(BoundaryScenario scenario) =>
        scenario.Ends.Select(e => new ScenarioEndRow(EndText(e.AtStart), e.ChildNodeTag, e.ParentNodeTag, ControlText(e.Control))).ToList();

    public static string ControlText(ControlCheck control)
    {
        if (!control.Available) return Loc.S("SubmodelControlUnavailable");
        return string.Format(Loc.S(control.Passed ? "SubmodelControlPassed" : "SubmodelControlFailed"),
            Number(control.ForceMismatch * 1e-3), Number(control.MomentMismatch * 1e-3));
    }

    /// <summary>Значение DOF с единицами экрана: сила кН, момент кН·м, перемещение мм, поворот рад; закрепление — «—».</summary>
    public static string DofValue(DofAssignment assignment, int dof)
    {
        if (assignment.Mode == DofMode.Fixed || assignment.Value is not { } v) return Dash;
        bool translational = dof < 3;
        return assignment.Mode == DofMode.Force
            ? $"{Number(v * 1e-3)} {Loc.S(translational ? "SubmodelUnitKn" : "SubmodelUnitKnm")}"
            : $"{Number(translational ? v * 1000 : v)} {Loc.S(translational ? "SubmodelUnitMm" : "SubmodelUnitRad")}";
    }

    /// <summary>Gauge-DOF материализации: «начало, Uz (узел 2)».</summary>
    public static IReadOnlyList<string> GaugeDofs(SubmodelMaterializationSummary summary) =>
        summary.GaugeDofs.Select(g => string.Format(Loc.S("SubmodelGaugeDofItem"), EndText(g.AtStart),
            g.Dof is >= 0 and < 6 ? Displacements[g.Dof] : g.Dof.ToString(), g.ChildNodeTag)).ToList();

    /// <summary>Максимальная невязка фиксации жёстких мод с λ, где она достигнута; null — gauge-DOF такого вида нет.</summary>
    public static string? GaugeResidual(GaugeResidualStep? step, bool force)
    {
        double? value = force ? step?.MaxForce : step?.MaxMoment;
        if (step is null || value is not { } v) return null;
        return string.Format(Loc.S(force ? "SubmodelGaugeMaxForce" : "SubmodelGaugeMaxMoment"),
            Number(v * 1e-3), step.Lambda.ToString("0.###", CultureInfo.CurrentCulture));
    }

    public static IReadOnlyList<DofOverrideOption> ChoiceOptions() =>
    [
        new(DofOverrideChoice.Auto, Loc.S("SubmodelDofChoiceAuto")),
        new(DofOverrideChoice.Fixed, Loc.S("SubmodelDofModeFixed")),
        new(DofOverrideChoice.Force, Loc.S("SubmodelDofModeForce")),
        new(DofOverrideChoice.Kinematic, Loc.S("SubmodelDofModeKinematic")),
    ];

    /// <summary>Переопределения для перестроения сценария: все строки с выбором, отличным от «Авто».</summary>
    public static IReadOnlyList<DofOverride> Overrides(IEnumerable<ScenarioDofRow> rows) =>
        rows.Where(r => r.Choice != DofOverrideChoice.Auto)
            .Select(r => new DofOverride(r.AtStart, r.DofIndex, ToMode(r.Choice))).ToList();

    static DofOverrideChoice ToChoice(DofMode mode) => mode switch
    {
        DofMode.Fixed => DofOverrideChoice.Fixed,
        DofMode.Force => DofOverrideChoice.Force,
        _ => DofOverrideChoice.Kinematic
    };

    static DofMode ToMode(DofOverrideChoice choice) => choice switch
    {
        DofOverrideChoice.Fixed => DofMode.Fixed,
        DofOverrideChoice.Force => DofMode.Force,
        DofOverrideChoice.Kinematic => DofMode.Kinematic,
        _ => throw new ArgumentOutOfRangeException(nameof(choice))
    };

    static string ModeText(DofMode mode) => Loc.S(mode switch
    {
        DofMode.Fixed => "SubmodelDofModeFixed",
        DofMode.Force => "SubmodelDofModeForce",
        _ => "SubmodelDofModeKinematic"
    });

    static string EndText(bool atStart) => Loc.S(atStart ? "SubmodelEndStart" : "SubmodelEndEnd");
}
