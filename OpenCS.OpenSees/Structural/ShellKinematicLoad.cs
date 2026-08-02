namespace OpenCS.OpenSees.Structural;

/// <summary>Предписанное перемещение или вращение shell-узла в одном DOF.</summary>
public sealed record ShellKinematicLoad(int NodeTag, int Dof, double Value)
{
    /// <summary>Проверяет номер DOF и конечность предписанного значения.</summary>
    public void Validate()
    {
        if (Dof is < 1 or > 6)
            throw new InvalidOperationException($"Kinematic load узла {NodeTag}: DOF должен быть от 1 до 6.");
        if (!double.IsFinite(Value))
            throw new InvalidOperationException($"Kinematic load узла {NodeTag}, DOF {Dof}: значение должно быть конечным.");
    }
}
